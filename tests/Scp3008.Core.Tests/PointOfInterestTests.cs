using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class PointOfInterestTests
{
    private static readonly int[] Seeds = [3008, 42, -1, 1, 2, 3, 4, 5];

    public static TheoryData<PillarLayout, int> LayoutsAndSeeds()
    {
        var data = new TheoryData<PillarLayout, int>();
        foreach (var layout in Enum.GetValues<PillarLayout>())
            foreach (var seed in new[] { 3008, 42, 7 })
                data.Add(layout, seed);
        return data;
    }

    private static WorldSnapshot Generate(int seed, PillarLayout layout = PillarLayout.ThreeByThree, bool showcase = false)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout, LandmarkShowcase: showcase));
        return sim.Snapshot;
    }

    private static IEnumerable<FurniturePiece> PiecesOf(WorldSnapshot snapshot, PointOfInterest poi) =>
        snapshot.Furniture.Where(p => p.PointOfInterestId == poi.Id);

    [Theory]
    [InlineData(PillarLayout.ThreeByThree)]
    [InlineData(PillarLayout.CornersLarge)]
    public void The_same_seed_and_pillar_layout_give_the_same_points_of_interest(PillarLayout layout)
    {
        var first = Generate(42, layout);
        using var again = Simulation.Create(new WorldConfig(42, RuntimeRngSeed: 9, PillarLayout: layout));
        again.Advance(TimeSpan.FromHours(7));
        var second = again.Snapshot;

        Assert.NotEmpty(first.PointsOfInterest);
        Assert.Equal(first.PointsOfInterest, second.PointsOfInterest);
        Assert.Equal(first.Furniture.Where(p => p.PointOfInterestId >= 0), second.Furniture.Where(p => p.PointOfInterestId >= 0));
        Assert.NotEmpty(first.TankFish);
        Assert.Equal(first.TankFish, second.TankFish);
    }

    [Fact]
    public void Another_seed_puts_them_elsewhere()
    {
        var places = Seeds.Select(s => string.Join(";", Generate(s).PointsOfInterest.Select(p => $"{p.Type}@{p.Centre}"))).Distinct();
        Assert.Equal(Seeds.Length, places.Count());
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Counts_per_sector_and_per_store_match_the_ranges_on_every_layout(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var pois = snapshot.PointsOfInterest;
        Assert.Equal(Enumerable.Range(0, pois.Count), pois.Select(p => p.Id));

        foreach (var sector in snapshot.Sectors)
        {
            int Count(PointOfInterestType type) => pois.Count(p => p.Type == type && p.SectorId == sector.Id);
            Assert.InRange(Count(PointOfInterestType.StoreRestroom), 1, 3);
            Assert.InRange(Count(PointOfInterestType.SetDinnerTable), 2, 3);
            Assert.Equal(1, Count(PointOfInterestType.LostAndFound));
        }
        Assert.InRange(pois.Count(p => p.Type == PointOfInterestType.Cafeteria), 2, 3);
        Assert.InRange(pois.Count(p => p.Type == PointOfInterestType.ShoppingCartGraveyard), 2, 4);
        // The rare ones, per Store.
        Assert.Equal(3, pois.Count(p => p.Type == PointOfInterestType.BallPit));
        foreach (var type in new[] { PointOfInterestType.ArmchairHenge, PointOfInterestType.ExitSign, PointOfInterestType.MannequinCrowd,
                     PointOfInterestType.DoorToNowhere, PointOfInterestType.AquariumAisle })
            Assert.InRange(pois.Count(p => p.Type == type), 1, 2);
        Assert.All(pois.Where(p => p.Type == PointOfInterestType.Cafeteria),
            p => Assert.Equal(DepartmentType.RestaurantAndFoodMarket, snapshot.Departments.At(p.Centre).Type));
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Each_point_of_interest_keeps_to_its_sector_and_clear_of_landmarks_pillars_walkway_rings_and_each_other(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var space = snapshot.Space;
        foreach (var poi in snapshot.PointsOfInterest)
        {
            var sector = snapshot.Sectors[poi.SectorId];
            Assert.Equal(sector.Id, snapshot.SectorAt(poi.Centre).Id);
            var low = poi.Centre - poi.Size / 2f;
            var high = poi.Centre + poi.Size / 2f;
            Assert.True(low.X >= sector.Origin.X && low.Y >= sector.Origin.Y &&
                        high.X <= sector.Origin.X + sector.Size && high.Y <= sector.Origin.Y + sector.Size,
                $"{poi.Type} reaches out of Sector {sector.Name}.");
            foreach (var pillar in snapshot.Pillars)
            {
                var delta = space.Delta(pillar.Position, poi.Centre);
                Assert.True(MathF.Abs(delta.X) >= pillar.ClearHalfWidth + poi.Size.X / 2f ||
                            MathF.Abs(delta.Y) >= pillar.ClearHalfWidth + poi.Size.Y / 2f,
                    $"{poi.Type} covers Pillar {pillar.Coordinate} or its walkway ring.");
            }
            foreach (var landmark in snapshot.Landmarks)
            {
                var delta = space.Delta(landmark.Centre, poi.Centre);
                Assert.True(MathF.Abs(delta.X) >= (landmark.Size.X + poi.Size.X) / 2f ||
                            MathF.Abs(delta.Y) >= (landmark.Size.Y + poi.Size.Y) / 2f,
                    $"{poi.Type} overlaps the {landmark.Type}.");
            }
            foreach (var other in snapshot.PointsOfInterest.Where(o => o.Id > poi.Id))
            {
                var delta = space.Delta(other.Centre, poi.Centre);
                Assert.True(MathF.Abs(delta.X) >= (other.Size.X + poi.Size.X) / 2f ||
                            MathF.Abs(delta.Y) >= (other.Size.Y + poi.Size.Y) / 2f,
                    $"{poi.Type} overlaps another {other.Type}.");
            }
        }
    }

    [Theory]
    [InlineData(3008)]
    [InlineData(5)]
    public void Each_point_of_interest_is_individual_furniture_inside_its_rectangle_that_no_display_enters(int seed)
    {
        var snapshot = Generate(seed);
        Assert.Equal(Enumerable.Range(0, snapshot.Furniture.Count), snapshot.Furniture.Select(p => p.Id));
        foreach (var poi in snapshot.PointsOfInterest)
        {
            var pieces = PiecesOf(snapshot, poi).ToList();
            Assert.True(pieces.Count >= 5, $"{poi.Type} has only {pieces.Count} pieces.");
            Assert.All(pieces, p =>
            {
                Assert.Equal(-1, p.DisplayId);
                Assert.Equal(-1, p.LandmarkId);
                foreach (var corner in FurnitureCatalog.Corners(p))
                {
                    var delta = snapshot.Space.Delta(poi.Centre, new Vector2(corner.X, corner.Y));
                    Assert.True(MathF.Abs(delta.X) <= poi.Size.X / 2f + 0.01f && MathF.Abs(delta.Y) <= poi.Size.Y / 2f + 0.01f,
                        $"A {p.Type} of the {poi.Type} sticks out of it.");
                }
            });
        }
        var intruders = snapshot.Furniture.Where(p => p.PointOfInterestId < 0 && snapshot.PointsOfInterest.Any(poi =>
        {
            var delta = snapshot.Space.Delta(poi.Centre, p.Position);
            return MathF.Abs(delta.X) < poi.Size.X / 2f && MathF.Abs(delta.Y) < poi.Size.Y / 2f;
        })).Take(5).ToList();
        Assert.True(intruders.Count == 0, "Other Furniture inside a Point of interest: " + string.Join("; ", intruders));
    }

    [Fact]
    public void Each_kind_is_made_of_the_furniture_it_is_about()
    {
        var snapshots = Seeds.Select(s => Generate(s)).ToList();
        HashSet<FurnitureType> Types(PointOfInterestType kind) => snapshots
            .SelectMany(s => s.PointsOfInterest.Where(p => p.Type == kind).SelectMany(p => PiecesOf(s, p)))
            .Select(p => p.Type).ToHashSet();

        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.Toilet, FurnitureType.VanityUnit }, Types(PointOfInterestType.StoreRestroom));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.HotFoodCounter, FurnitureType.DiningTable, FurnitureType.Chair }, Types(PointOfInterestType.Cafeteria));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.DiningTable, FurnitureType.Chair, FurnitureType.MealPlate }, Types(PointOfInterestType.SetDinnerTable));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.LabelledBox, FurnitureType.NoticeBoard }, Types(PointOfInterestType.LostAndFound));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.ShoppingCart }, Types(PointOfInterestType.ShoppingCartGraveyard));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.BallPool, FurnitureType.NetRailing, FurnitureType.PlaySlide, FurnitureType.PlayBall }, Types(PointOfInterestType.BallPit));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.Armchair, FurnitureType.Sofa }, Types(PointOfInterestType.ArmchairHenge));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.ExitSign }, Types(PointOfInterestType.ExitSign));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.Mannequin }, Types(PointOfInterestType.MannequinCrowd));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.DoorFrame, FurnitureType.ShelfUnit }, Types(PointOfInterestType.DoorToNowhere));
        Assert.Superset(new HashSet<FurnitureType> { FurnitureType.FishTank, FurnitureType.TankRack }, Types(PointOfInterestType.AquariumAisle));
        // The rare Furniture is found nowhere else, but in Landmarks (the Camping ground's caravan has a door).
        foreach (var (furniture, kind) in new[]
                 {
                     (FurnitureType.BallPool, PointOfInterestType.BallPit), (FurnitureType.NetRailing, PointOfInterestType.BallPit),
                     (FurnitureType.PlaySlide, PointOfInterestType.BallPit), (FurnitureType.FoamStep, PointOfInterestType.BallPit),
                     (FurnitureType.PlayBall, PointOfInterestType.BallPit), (FurnitureType.ExitSign, PointOfInterestType.ExitSign),
                     (FurnitureType.Mannequin, PointOfInterestType.MannequinCrowd), (FurnitureType.DoorFrame, PointOfInterestType.DoorToNowhere),
                     (FurnitureType.FishTank, PointOfInterestType.AquariumAisle), (FurnitureType.TankRack, PointOfInterestType.AquariumAisle),
                 })
            foreach (var snapshot in snapshots)
                Assert.All(snapshot.Furniture.Where(p => p.Type == furniture && p.LandmarkId < 0),
                    p => Assert.Equal(kind, snapshot.PointsOfInterest[p.PointOfInterestId].Type));
        // Meals are laid on tables or counters, not on the floor.
        Assert.All(snapshots.SelectMany(s => s.Furniture.Where(p => p.Type == FurnitureType.MealPlate)), p => Assert.True(p.Elevation > 0.5f));
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Each_mannequin_crowd_holds_24_to_42_bare_mannequins_in_three_poses_none_overlapping_and_the_crowd_count_is_unchanged(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var crowds = snapshot.PointsOfInterest.Where(p => p.Type == PointOfInterestType.MannequinCrowd).ToList();
        Assert.InRange(crowds.Count, 1, 2);
        foreach (var crowd in crowds)
        {
            var figures = PiecesOf(snapshot, crowd).ToList();
            Assert.All(figures, f => Assert.Equal(FurnitureType.Mannequin, f.Type));
            Assert.InRange(figures.Count, 24, 42);
            // At least three poses, and some variety in build and plastic.
            Assert.True(figures.Select(f => f.Variant).Where(v => v != 5).Distinct().Count() >= 3);
            Assert.True(figures.Select(f => f.Tint).Distinct().Count() >= 3);
            // One or two are damaged: one knocked over, and maybe one missing an arm.
            var fallen = figures.Count(f => f.Tilt != 0f);
            var armless = figures.Count(f => f.Variant == 5);
            Assert.Equal(1, fallen);
            Assert.InRange(fallen + armless, 1, 2);
            // None overlap: the discs round their footprints keep apart.
            float Radius(FurniturePiece f) => FurnitureCatalog.Corners(f).Max(c => (new Vector2(c.X, c.Y) - f.Position).Length());
            for (var i = 0; i < figures.Count; i++)
                for (var j = i + 1; j < figures.Count; j++)
                {
                    var gap = snapshot.Space.Delta(figures[i].Position, figures[j].Position).Length();
                    Assert.True(gap >= (Radius(figures[i]) + Radius(figures[j])) * 0.6f,
                        $"Mannequins {figures[i].Id} and {figures[j].Id} overlap ({gap:0.00} m apart).");
                    Assert.True(gap > 0.45f, $"Mannequins {figures[i].Id} and {figures[j].Id} stand on one spot.");
                }
            // Most still face one way.
            var upright = figures.Where(f => f.Tilt == 0f).ToList();
            var mean = MathF.Atan2(upright.Sum(f => MathF.Sin(f.Rotation)), upright.Sum(f => MathF.Cos(f.Rotation)));
            var facingTheSameWay = upright.Count(f => MathF.Abs((float)Math.IEEERemainder(f.Rotation - mean, 2 * Math.PI)) < 0.5f);
            Assert.True(facingTheSameWay > upright.Count / 2, "Most mannequins should face one way.");
        }
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Each_ball_pit_is_a_padded_ball_pool_with_net_railing_a_step_and_a_slide_filled_with_hundreds_of_balls(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var pits = snapshot.PointsOfInterest.Where(p => p.Type == PointOfInterestType.BallPit).ToList();
        Assert.Equal(3, pits.Count);
        foreach (var pit in pits)
        {
            var pieces = PiecesOf(snapshot, pit).ToList();
            Assert.All(pieces, p => Assert.Contains(p.Type, new[]
            {
                FurnitureType.BallPool, FurnitureType.NetRailing, FurnitureType.PlaySlide, FurnitureType.FoamStep, FurnitureType.PlayBall,
                FurnitureType.ShoeCabinet, FurnitureType.HallBench,
            }));
            var pool = Assert.Single(pieces, p => p.Type == FurnitureType.BallPool);
            var poolSpec = FurnitureCatalog.Spec(pool);
            Assert.Single(pieces, p => p.Type == FurnitureType.PlaySlide);
            Assert.Single(pieces, p => p.Type == FurnitureType.FoamStep);
            // The railing stands on the pool's walls, round all four sides, with gaps for the step and the slide.
            var railings = pieces.Where(p => p.Type == FurnitureType.NetRailing).ToList();
            Assert.Equal(14, railings.Count);
            Assert.All(railings, r => Assert.Equal(poolSpec.Height, r.Elevation));

            bool InPool(FurniturePiece p)
            {
                var delta = snapshot.Space.Delta(pool.Position, p.Position);
                var turned = MathF.Abs(MathF.Sin(pool.Rotation)) > 0.5f;
                var half = turned ? new Vector2(poolSpec.Depth, poolSpec.Width) / 2f : new Vector2(poolSpec.Width, poolSpec.Depth) / 2f;
                return MathF.Abs(delta.X) < half.X && MathF.Abs(delta.Y) < half.Y;
            }
            var balls = pieces.Where(p => p.Type == FurnitureType.PlayBall).ToList();
            var inPool = balls.Where(InPool).ToList();
            var spilled = balls.Where(b => !InPool(b)).ToList();
            // Hundreds of balls fill the pool to below its rim, in many colours; a few lie on the floor round it.
            Assert.InRange(inPool.Count, 300, 2000);
            Assert.All(inPool, b => Assert.InRange(b.Elevation, 0.2f, poolSpec.Height - FurnitureCatalog.Spec(b).Height));
            Assert.True(inPool.Select(b => b.Tint).Distinct().Count() >= 6, "The balls should come in many colours.");
            Assert.InRange(spilled.Count, 8, 20);
            Assert.All(spilled, b => Assert.Equal(0f, b.Elevation));
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_balls_and_another_seed_other_balls()
    {
        IEnumerable<FurniturePiece> Balls(WorldSnapshot s) => s.Furniture.Where(p => p.Type == FurnitureType.PlayBall);
        Assert.Equal(Balls(Generate(42)), Balls(Generate(42)));
        Assert.NotEqual(Balls(Generate(42)).Select(b => (b.Position, b.Tint)), Balls(Generate(43)).Select(b => (b.Position, b.Tint)));
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Each_aquarium_aisle_is_a_row_of_racked_tanks_in_three_sizes_with_three_kinds_of_fish_and_the_count_is_unchanged(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var aisles = snapshot.PointsOfInterest.Where(p => p.Type == PointOfInterestType.AquariumAisle).ToList();
        Assert.InRange(aisles.Count, 1, 2);
        var fish = snapshot.TankFish.ToDictionary(f => f.TankId, f => f.Kind);
        foreach (var aisle in aisles)
        {
            Assert.Equal(new Vector2(12f, 6f), new Vector2(MathF.Max(aisle.Size.X, aisle.Size.Y), MathF.Min(aisle.Size.X, aisle.Size.Y)));
            var pieces = PiecesOf(snapshot, aisle).ToList();
            Assert.All(pieces, p => Assert.Contains(p.Type, new[] { FurnitureType.FishTank, FurnitureType.TankRack, FurnitureType.HallBench }));
            var racks = pieces.Where(p => p.Type == FurnitureType.TankRack).ToList();
            var tanks = pieces.Where(p => p.Type == FurnitureType.FishTank).ToList();
            Assert.InRange(racks.Count, 4, 12);
            // Tanks of at least three sizes: small and medium ones on the racks' shelves, one or two large ones on the floor at the end.
            Assert.True(tanks.Select(t => t.Variant).Distinct().Count() >= 3, "The tanks should come in at least three sizes.");
            Assert.True(tanks.Select(t => FurnitureCatalog.Spec(t).Width).Distinct().Count() >= 3);
            var large = tanks.Where(t => t.Variant == FurnitureCatalog.LargeFishTank).ToList();
            Assert.InRange(large.Count, 1, 2);
            Assert.All(large, t => Assert.Equal(0f, t.Elevation));
            var racked = tanks.Where(t => t.Variant != FurnitureCatalog.LargeFishTank).ToList();
            Assert.True(racked.Count >= 3 * racks.Count, $"Only {racked.Count} small tanks on {racks.Count} racks.");
            Assert.All(racked, t =>
            {
                Assert.Contains(t.Elevation, FurnitureCatalog.TankRackLevels);
                Assert.Contains(racks, r =>
                {
                    var delta = snapshot.Space.Delta(r.Position, t.Position);
                    var spec = FurnitureCatalog.Spec(r);
                    var turned = MathF.Abs(MathF.Sin(r.Rotation)) > 0.5f;
                    var half = turned ? new Vector2(spec.Depth, spec.Width) / 2f : new Vector2(spec.Width, spec.Depth) / 2f;
                    return MathF.Abs(delta.X) < half.X && MathF.Abs(delta.Y) < half.Y && t.Rotation == r.Rotation;
                });
            });
            // Every tank holds live fish of one kind (Goods, not Furniture); at least three kinds in the aisle, and
            // neighbouring tanks on a shelf hold different kinds.
            Assert.All(tanks, t => Assert.True(fish.ContainsKey(t.Id), $"Tank {t.Id} has no fish."));
            Assert.True(tanks.Select(t => fish[t.Id]).Distinct().Count() >= 3, "The aisle should hold at least three kinds of fish.");
            foreach (var a in racked)
                foreach (var b in racked.Where(b => b.Id > a.Id && b.Elevation == a.Elevation))
                    if (snapshot.Space.Delta(a.Position, b.Position).Length() < 1.1f)
                        Assert.NotEqual(fish[a.Id], fish[b.Id]);
        }
        // Only tanks hold fish.
        Assert.All(snapshot.TankFish, f => Assert.Equal(FurnitureType.FishTank, snapshot.Furniture[f.TankId].Type));
        Assert.Equal(snapshot.Furniture.Count(p => p.Type == FurnitureType.FishTank), snapshot.TankFish.Count);
    }

    [Fact]
    public void There_is_no_display_aquarium_any_more_and_fish_are_goods_not_furniture()
    {
        Assert.DoesNotContain("DisplayAquarium", Enum.GetNames<PointOfInterestType>());
        Assert.DoesNotContain("Aquarium", Enum.GetNames<FurnitureType>());
        Assert.All(Enum.GetNames<FishKind>(), kind => Assert.DoesNotContain(kind, Enum.GetNames<FurnitureType>()));
        Assert.True(Enum.GetValues<FishKind>().Length >= 3);
    }

    [Fact]
    public void There_is_no_bouncy_castle_any_more() =>
        Assert.DoesNotContain("BouncyCastle", Enum.GetNames<FurnitureType>());

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void The_three_ball_pits_are_one_network_the_staff_pass_through_only_at_closing_time(PillarLayout layout, int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout));
        var pits = sim.Snapshot.PointsOfInterest.Where(p => p.Type == PointOfInterestType.BallPit).Select(p => p.Id).ToList();
        Assert.Equal(3, pits.Count);
        Assert.Equal(pits, sim.Snapshot.BallPitNetwork.BallPitIds);

        void AdvanceTo(DayPhase phase)
        {
            for (var hours = 0; sim.Snapshot.Phase != phase; hours++)
            {
                Assert.True(hours < 48, $"No {phase} within two days.");
                sim.Advance(TimeSpan.FromHours(1));
            }
        }

        AdvanceTo(DayPhase.Open);
        Assert.All(pits, id => Assert.Empty(sim.Snapshot.StaffExitsFrom(id)));

        AdvanceTo(DayPhase.ClosingTime);
        foreach (var id in pits)
            Assert.Equal(pits.Where(other => other != id), sim.Snapshot.StaffExitsFrom(id));
        // Nothing else is part of the network.
        var notPit = sim.Snapshot.PointsOfInterest.First(p => p.Type != PointOfInterestType.BallPit);
        Assert.Empty(sim.Snapshot.StaffExitsFrom(notPit.Id));

        AdvanceTo(DayPhase.Open);
        Assert.All(pits, id => Assert.Empty(sim.Snapshot.StaffExitsFrom(id)));
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Each_armchair_henge_is_a_26_m_stonehenge_of_sofas_and_armchairs_and_the_count_is_unchanged(PillarLayout layout, int seed)
    {
        var snapshot = Generate(seed, layout);
        var henges = snapshot.PointsOfInterest.Where(p => p.Type == PointOfInterestType.ArmchairHenge).ToList();
        Assert.InRange(henges.Count, 1, 2);
        foreach (var henge in henges)
        {
            var pieces = PiecesOf(snapshot, henge).ToList();
            Assert.All(pieces, p => Assert.True(p.Type is FurnitureType.Sofa or FurnitureType.Armchair, $"A {p.Type} in the henge."));
            const float quarter = MathF.PI / 2f;
            bool Near(float a, float b) => MathF.Abs(a - b) < 0.01f;

            // The altar lies at the centre; everything is measured from it.
            var altar = Assert.Single(pieces, p => Near(p.Tilt, -quarter));
            float Distance(FurniturePiece p) => snapshot.Space.Delta(altar.Position, p.Position).Length();

            // Standing stones: sofas on end. The outer circle is most of 28, one more stands alone as the heel stone.
            var stones = pieces.Where(p => MathF.Abs(p.Roll) > 1f).ToList();
            var heel = stones.OrderByDescending(Distance).First();
            Assert.InRange(Distance(heel), 14.5f, 17f);
            var circle = stones.Where(p => p.Id != heel.Id && Distance(p) > 10f).ToList();
            Assert.InRange(circle.Count, 22, 25);
            Assert.All(circle, p => Assert.InRange(Distance(p), 11.5f, 13.5f));
            // About 26 m across, counting the outer edge of the ring.
            Assert.InRange(circle.Max(p => Distance(p)) + circle.Min(p => Distance(p)), 24f, 27f);

            // The lintels: a ring of them on the circle, and five trilithons' (four 5.4 m up, the middle one 8.1 m).
            var lintels = pieces.Where(p => p.Type == FurnitureType.Sofa && p.Tilt == 0f && p.Roll == 0f && p.Elevation > 2f).ToList();
            Assert.InRange(lintels.Count(p => Near(p.Elevation, 2.7f)), 12, 28);
            Assert.Equal(4, lintels.Count(p => Near(p.Elevation, 5.4f)));
            Assert.Equal(1, lintels.Count(p => Near(p.Elevation, 8.1f)));
            // Each trilithon lintel rests on two stacked uprights below it.
            foreach (var lintel in lintels.Where(p => p.Elevation > 5f))
                Assert.Equal(2, stones.Count(u => Near(u.Elevation + 1.35f, lintel.Elevation) && Distance(u) < 8f &&
                                                  snapshot.Space.Delta(u.Position, lintel.Position).Length() < 1.6f));

            // The horseshoe of armchairs inside the trilithons.
            var armchairs = pieces.Where(p => p.Type == FurnitureType.Armchair).ToList();
            Assert.Equal(7, armchairs.Count);
            Assert.All(armchairs, p => Assert.InRange(Distance(p), 3f, 4.3f));
            // Gaps and fallen pieces: some of the ring is missing and something lies on the ground.
            Assert.True(circle.Count < 28);
            Assert.Contains(pieces, p => p.Type == FurnitureType.Sofa && p.Roll == 0f && p.Elevation < 1.2f && p.Id != altar.Id && Distance(p) > 8f);
        }
    }

    [Fact]
    public void The_landmark_showcase_has_none()
    {
        Assert.Empty(Generate(3008, showcase: true).PointsOfInterest);
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void The_point_of_interest_showcase_has_every_kind_once_with_no_displays_or_landmarks(PillarLayout layout, int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout, PointOfInterestShowcase: true));
        var snapshot = sim.Snapshot;

        Assert.True(snapshot.PointOfInterestShowcase);
        Assert.False(snapshot.LandmarkShowcase);
        Assert.Empty(snapshot.Landmarks);
        Assert.Empty(snapshot.Displays);
        Assert.Equal(Enum.GetValues<PointOfInterestType>().Order(), snapshot.PointsOfInterest.Select(p => p.Type).Order());
        Assert.All(snapshot.PointsOfInterest, poi => Assert.NotEmpty(PiecesOf(snapshot, poi)));
        Assert.All(snapshot.Furniture, piece => Assert.True(piece.PointOfInterestId >= 0));
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Every_lost_and_found_piece_in_the_showcase_stays_inside_its_rectangle_and_on_the_floor_or_a_surface(PillarLayout layout, int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout, PointOfInterestShowcase: true));
        var snapshot = sim.Snapshot;
        var laf = Assert.Single(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.LostAndFound);
        var pieces = PiecesOf(snapshot, laf).ToList();
        Assert.NotEmpty(pieces);
        foreach (var piece in pieces)
        {
            var corners = FurnitureCatalog.Corners(piece);
            Assert.True(corners.Min(c => c.Z) >= -0.001f, $"Piece {piece.Type} lowest corner below floor.");
            foreach (var corner in corners)
            {
                var delta = snapshot.Space.Delta(laf.Centre, new Vector2(corner.X, corner.Y));
                Assert.True(MathF.Abs(delta.X) <= laf.Size.X / 2f + 0.01f && MathF.Abs(delta.Y) <= laf.Size.Y / 2f + 0.01f,
                    $"A {piece.Type} of the LostAndFound sticks out of it.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Every_store_restroom_piece_in_the_showcase_stays_inside_its_rectangle_and_on_the_floor_or_a_wall(PillarLayout layout, int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout, PointOfInterestShowcase: true));
        var snapshot = sim.Snapshot;
        var restroom = Assert.Single(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.StoreRestroom);
        var pieces = PiecesOf(snapshot, restroom).ToList();
        Assert.NotEmpty(pieces);
        foreach (var piece in pieces)
        {
            var corners = FurnitureCatalog.Corners(piece);
            Assert.True(corners.Min(c => c.Z) >= -0.001f, $"Piece {piece.Type} lowest corner below floor.");
            foreach (var corner in corners)
            {
                var delta = snapshot.Space.Delta(restroom.Centre, new Vector2(corner.X, corner.Y));
                Assert.True(MathF.Abs(delta.X) <= restroom.Size.X / 2f + 0.01f && MathF.Abs(delta.Y) <= restroom.Size.Y / 2f + 0.01f,
                    $"A {piece.Type} of the StoreRestroom sticks out of it.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(LayoutsAndSeeds))]
    public void Every_cafeteria_piece_in_the_showcase_stays_inside_its_rectangle_and_on_the_floor_or_a_table(PillarLayout layout, int seed)
    {
        using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout, PointOfInterestShowcase: true));
        var snapshot = sim.Snapshot;
        var cafeteria = Assert.Single(snapshot.PointsOfInterest, p => p.Type == PointOfInterestType.Cafeteria);
        var pieces = PiecesOf(snapshot, cafeteria).ToList();
        Assert.NotEmpty(pieces);
        foreach (var piece in pieces)
        {
            var corners = FurnitureCatalog.Corners(piece);
            Assert.True(corners.Min(c => c.Z) >= -0.001f, $"Piece {piece.Type} lowest corner below floor.");
            foreach (var corner in corners)
            {
                var delta = snapshot.Space.Delta(cafeteria.Centre, new Vector2(corner.X, corner.Y));
                Assert.True(MathF.Abs(delta.X) <= cafeteria.Size.X / 2f + 0.01f && MathF.Abs(delta.Y) <= cafeteria.Size.Y / 2f + 0.01f,
                    $"A {piece.Type} of the Cafeteria sticks out of it.");
            }
        }
    }

    [Fact]
    public void A_normal_store_is_not_the_point_of_interest_showcase() => Assert.False(Generate(3008).PointOfInterestShowcase);

    [Fact]
    public void A_world_cannot_be_both_showcases()
    {
        Assert.Throws<ArgumentException>(() =>
            Simulation.Create(new WorldConfig(3008, LandmarkShowcase: true, PointOfInterestShowcase: true)));
    }
}
