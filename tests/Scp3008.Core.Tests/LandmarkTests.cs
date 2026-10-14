using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class LandmarkTests
{
    private static readonly int[] Seeds = [3008, 42, -1, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    private static WorldSnapshot Generate(int seed, PillarLayout layout = PillarLayout.ThreeByThree)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout));
        return sim.Snapshot;
    }

    private static IEnumerable<FurniturePiece> PiecesOf(WorldSnapshot snapshot, Landmark landmark) =>
        snapshot.Furniture.Where(p => p.LandmarkId == landmark.Id);

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.DiamondLarge)]
    public void The_same_seed_and_pillar_layout_give_the_same_landmarks_in_the_same_places(PillarLayout layout)
    {
        var first = Generate(42, layout);
        using var again = Simulation.Create(new WorldConfig(42, RuntimeRngSeed: 7, PillarLayout: layout));
        again.Advance(TimeSpan.FromHours(5));
        var second = again.Snapshot;

        Assert.NotEmpty(first.Landmarks);
        Assert.Equal(first.Landmarks.Count, second.Landmarks.Count);
        foreach (var (a, b) in first.Landmarks.Zip(second.Landmarks))
        {
            Assert.Equal((a.Id, a.Type, a.SectorId, a.Centre, a.Size, a.Height), (b.Id, b.Type, b.SectorId, b.Centre, b.Size, b.Height));
            Assert.Equal(a.Lookouts, b.Lookouts);
        }
        Assert.Equal(first.Furniture.Where(p => p.LandmarkId >= 0), second.Furniture.Where(p => p.LandmarkId >= 0));
    }

    [Fact]
    public void Every_world_has_the_ruined_village_and_the_seed_picks_the_other_landmarks()
    {
        var kinds = Seeds.Select(seed => Generate(seed).Landmarks.Select(l => l.Type).ToHashSet()).ToList();

        Assert.All(kinds, k => Assert.Contains(LandmarkType.RuinedVillage, k));
        Assert.All(Seeds.Select(seed => Generate(seed)), s => Assert.Single(s.Landmarks, l => l.Type == LandmarkType.RuinedVillage));
        foreach (var other in new[] { LandmarkType.RackCanyon, LandmarkType.BoxFort, LandmarkType.MattressMountain })
        {
            Assert.Contains(kinds, k => k.Contains(other));
            Assert.Contains(kinds, k => !k.Contains(other));
        }
        Assert.True(kinds.Select(k => string.Join(",", k.Order())).Distinct().Count() >= 3, "The seed hardly changes which Landmarks a world has.");
    }

    [Theory]
    [InlineData(PillarLayout.ThreeByThree, 3008)]
    [InlineData(PillarLayout.ThreeByThree, 5)]
    [InlineData(PillarLayout.TwoByTwo, 3008)]
    [InlineData(PillarLayout.TwoByTwo, 42)]
    [InlineData(PillarLayout.ThreeByThreeLarge, 3008)]
    [InlineData(PillarLayout.CornersLarge, 3008)]
    [InlineData(PillarLayout.CornersLarge, 7)]
    [InlineData(PillarLayout.DiamondLarge, 3008)]
    [InlineData(PillarLayout.DiamondLarge, -1)]
    public void Landmarks_take_one_sector_each_pillar_less_first_clear_of_pillars_and_at_most_a_quarter_of_it(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var landmarks = snapshot.Landmarks;
        Assert.Equal(landmarks.Count, landmarks.Select(l => l.SectorId).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, landmarks.Count), landmarks.Select(l => l.Id));

        var pillarLess = snapshot.Sectors.Count(s => !snapshot.Pillars.Any(p => p.Coordinate == s.Name));
        var inPillarLess = landmarks.Count(l => !snapshot.Pillars.Any(p => p.Coordinate == snapshot.Sectors[l.SectorId].Name));
        Assert.Equal(Math.Min(pillarLess, landmarks.Count), inPillarLess);

        foreach (var landmark in landmarks)
        {
            var sector = snapshot.Sectors[landmark.SectorId];
            Assert.True(landmark.Size.X * landmark.Size.Y <= sector.Size * sector.Size / 4f, $"{landmark.Type} is more than a quarter of its Sector.");
            var low = landmark.Centre - landmark.Size / 2f;
            var high = landmark.Centre + landmark.Size / 2f;
            Assert.True(low.X >= sector.Origin.X && low.Y >= sector.Origin.Y &&
                        high.X <= sector.Origin.X + sector.Size && high.Y <= sector.Origin.Y + sector.Size,
                $"{landmark.Type} reaches out of Sector {sector.Name}.");
            foreach (var pillar in snapshot.Pillars)
            {
                var delta = snapshot.Space.Delta(pillar.Position, landmark.Centre);
                Assert.True(MathF.Abs(delta.X) >= pillar.ClearHalfWidth + landmark.Size.X / 2f ||
                            MathF.Abs(delta.Y) >= pillar.ClearHalfWidth + landmark.Size.Y / 2f,
                    $"{landmark.Type} covers Pillar {pillar.Coordinate} or its walkway ring.");
            }
            Assert.All(PiecesOf(snapshot, landmark), p => AssertInside(snapshot, landmark, p));
        }
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(42)]
    [InlineData(5)]
    public void Each_landmark_is_individual_furniture_marked_with_it_and_rises_above_the_fog_banks(int seed)
    {
        var snapshot = Generate(seed);
        Assert.Equal(Enumerable.Range(0, snapshot.Furniture.Count), snapshot.Furniture.Select(p => p.Id));
        foreach (var landmark in snapshot.Landmarks)
        {
            var pieces = PiecesOf(snapshot, landmark).ToList();
            Assert.True(pieces.Count >= 300, $"{landmark.Type} has only {pieces.Count} pieces.");
            Assert.All(pieces, p => Assert.Equal(-1, p.DisplayId));
            var top = pieces.Max(p => FurnitureCatalog.Corners(p).Max(c => c.Z));
            Assert.Equal(top, landmark.Height, 3);
            Assert.True(landmark.Height >= Landmark.MinHeight, $"{landmark.Type} is only {landmark.Height:0.0} m high.");
        }
        // Display Furniture stays out of every Landmark.
        var intruders = snapshot.Furniture.Where(p => p.LandmarkId < 0 && snapshot.Landmarks.Any(l =>
        {
            var delta = snapshot.Space.Delta(l.Centre, p.Position);
            return MathF.Abs(delta.X) < l.Size.X / 2f && MathF.Abs(delta.Y) < l.Size.Y / 2f;
        })).Take(5).ToList();
        Assert.True(intruders.Count == 0, "Display Furniture inside a Landmark: " + string.Join("; ", intruders));
    }

    [Fact]
    public void Each_landmark_is_built_from_its_own_furniture()
    {
        var landmarks = Seeds.Select(seed => Generate(seed))
            .SelectMany(s => s.Landmarks.Select(l => (Landmark: l, Pieces: PiecesOf(s, l).ToList())))
            .GroupBy(x => x.Landmark.Type).ToDictionary(g => g.Key, g => g.First());
        Assert.Equal(4, landmarks.Count);

        static float Share(List<FurniturePiece> pieces, params FurnitureType[] types) =>
            pieces.Count(p => types.Contains(p.Type)) / (float)pieces.Count;

        var village = landmarks[LandmarkType.RuinedVillage];
        Assert.True(village.Pieces.Select(p => p.Type).Distinct().Count() >= 5, "The ruined Village is a castle of mixed Furniture.");
        Assert.Contains(village.Pieces, p => MathF.Abs(p.Tilt) > 0.3f);

        var canyon = landmarks[LandmarkType.RackCanyon];
        Assert.True(Share(canyon.Pieces, FurnitureType.PalletRack) >= 0.5f, "The rack canyon is mostly pallet racks.");
        Assert.True(canyon.Pieces.Count(p => p.Type == FurnitureType.PalletRack && MathF.Abs(p.Tilt) > 0.2f) >= 20, "The rack canyon has tilted racks.");
        Assert.NotEmpty(canyon.Landmark.Lookouts);

        var fort = landmarks[LandmarkType.BoxFort];
        Assert.True(Share(fort.Pieces, FurnitureType.CardboardBox) >= 0.95f, "The box fort is made of cardboard boxes.");

        var mountain = landmarks[LandmarkType.MattressMountain];
        Assert.True(Share(mountain.Pieces, FurnitureType.Mattress) >= 0.8f, "The mattress mountain is a heap of mattresses.");
        Assert.Contains(mountain.Pieces, p => p.Type == FurnitureType.Bed);

        foreach (var (landmark, pieces) in new[] { canyon, mountain })
        {
            // A lookout stands high in the Landmark, on its Furniture.
            Assert.All(landmark.Lookouts, lookout =>
            {
                Assert.True(lookout.Z >= Landmark.MinHeight * 0.75f, $"{landmark.Type} lookout at {lookout.Z:0.0} m is low.");
                var delta = new Vector2(lookout.X, lookout.Y) - landmark.Centre;
                Assert.True(MathF.Abs(delta.X) <= landmark.Size.X / 2f && MathF.Abs(delta.Y) <= landmark.Size.Y / 2f);
            });
        }
    }

    [Fact]
    public void A_tilted_piece_tips_its_front_down_about_its_base()
    {
        var spec = FurnitureCatalog.Spec(FurnitureType.Wardrobe);
        var standing = new FurniturePiece(0, FurnitureType.Wardrobe, new Vector2(10f, 10f), 0f, 0f);
        Assert.Equal(spec.Height, FurnitureCatalog.Corners(standing).Max(c => c.Z), 4);

        // Tipped forward a quarter turn it lies on its front, reaching south (its front) by its height.
        var fallen = standing with { Tilt = MathF.PI / 2f, Elevation = spec.Depth / 2f };
        var corners = FurnitureCatalog.Corners(fallen);
        Assert.Equal(spec.Depth, corners.Max(c => c.Z), 4);
        Assert.Equal(0f, corners.Min(c => c.Z), 4);
        Assert.Equal(10f + spec.Height, corners.Max(c => c.Y), 4);
    }

    private static void AssertInside(WorldSnapshot snapshot, Landmark landmark, FurniturePiece piece)
    {
        const float tolerance = 0.01f;
        foreach (var corner in FurnitureCatalog.Corners(piece))
        {
            var delta = snapshot.Space.Delta(landmark.Centre, new Vector2(corner.X, corner.Y));
            Assert.True(MathF.Abs(delta.X) <= landmark.Size.X / 2f + tolerance && MathF.Abs(delta.Y) <= landmark.Size.Y / 2f + tolerance,
                $"{piece.Type} {piece.Id} sticks out of {landmark.Type}.");
            Assert.True(corner.Z >= -tolerance, $"{piece.Type} {piece.Id} of {landmark.Type} sinks into the floor.");
        }
    }
}
