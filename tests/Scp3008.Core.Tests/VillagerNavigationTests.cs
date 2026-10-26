using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class VillagerNavigationTests
{
    // Far from every Pillar of the 3 × 3 layout (they stand 224 m in from the seams, 448 m apart).
    private static readonly Vector2 OpenFloor = new(100f, 100f);

    /// <summary>A Store whose floor is bare apart from the Pillars: the Point of interest showcase with its Furniture carried off.</summary>
    private static Simulation BareStore()
    {
        var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, PointOfInterestShowcase: true));
        sim.Scenario.RemoveFurniture(_ => true);
        return sim;
    }

    private static Villager VillagerIn(WorldSnapshot snapshot, int id) => snapshot.Villagers.Single(v => v.Id == id);

    /// <summary>Whether a position lies inside a piece's footprint (upright pieces).</summary>
    private static bool Inside(StoreSpace space, FurniturePiece piece, Vector2 position)
    {
        var spec = FurnitureCatalog.Spec(piece);
        var delta = space.Delta(piece.Position, position);
        var across = new Vector2(MathF.Cos(piece.Rotation), MathF.Sin(piece.Rotation));
        var front = new Vector2(-MathF.Sin(piece.Rotation), MathF.Cos(piece.Rotation));
        return MathF.Abs(Vector2.Dot(delta, across)) < spec.Width / 2f && MathF.Abs(Vector2.Dot(delta, front)) < spec.Depth / 2f;
    }

    /// <summary>Advances tick by tick until the Villager arrives or the time runs out; returns the positions walked through.</summary>
    private static List<Vector2> WalkUntilArrived(Simulation sim, int id, TimeSpan limit)
    {
        var walked = new List<Vector2> { VillagerIn(sim.Snapshot, id).Position };
        for (var tick = 0L; tick < StoreClock.TicksFor(limit) && VillagerIn(sim.Snapshot, id).Destination is not null; tick++)
        {
            sim.AdvanceTicks(1);
            walked.Add(VillagerIn(sim.Snapshot, id).Position);
        }
        return walked;
    }

    private static float Length(StoreSpace space, List<Vector2> walked) =>
        walked.Zip(walked.Skip(1), space.Distance).Sum();

    /// <summary>A wall of wardrobes side by side, running north-south through <paramref name="centre"/>.</summary>
    private static List<FurniturePiece> PlaceWall(Simulation sim, Vector2 centre, int wardrobes)
    {
        var width = FurnitureCatalog.Spec(FurnitureType.Wardrobe).Width;
        return Enumerable.Range(0, wardrobes)
            .Select(i => sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe,
                centre + new Vector2(0f, (i - (wardrobes - 1) / 2f) * width), MathF.PI / 2f))
            .ToList();
    }

    [Fact]
    public void The_scenario_builder_places_a_Villager_who_stands_still()
    {
        using var sim = BareStore();

        var villager = sim.Scenario.PlaceVillager(OpenFloor);
        sim.Advance(TimeSpan.FromHours(1));

        var placed = VillagerIn(sim.Snapshot, villager.Id);
        Assert.Equal(OpenFloor, placed.Position);
        Assert.Null(placed.Destination);
        Assert.Single(sim.Snapshot.Villagers);
    }

    [Fact]
    public void A_Villager_placed_on_Furniture_stands_beside_it()
    {
        using var sim = BareStore();
        var wardrobe = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, OpenFloor, 0f);

        var villager = sim.Scenario.PlaceVillager(OpenFloor);

        Assert.False(Inside(sim.Snapshot.Space, wardrobe, villager.Position));
        Assert.True(sim.Snapshot.Space.Distance(OpenFloor, villager.Position) < 2f);
    }

    [Fact]
    public void The_scenario_builder_sets_the_time_of_day_without_going_back_in_time()
    {
        using var sim = BareStore();
        var before = sim.Snapshot.Clock;

        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(23));
        Assert.Equal(TimeSpan.FromHours(23), sim.Snapshot.Clock.TimeOfDay);
        Assert.Equal(before.Day, sim.Snapshot.Clock.Day);
        Assert.Equal(DayPhase.ClosingTime, sim.Snapshot.Phase);

        sim.Scenario.SetTimeOfDay(TimeSpan.FromHours(6));
        Assert.Equal(TimeSpan.FromHours(6), sim.Snapshot.Clock.TimeOfDay);
        Assert.Equal(before.Day + 1, sim.Snapshot.Clock.Day);
    }

    [Fact]
    public void A_Villager_walks_to_a_target_in_the_open_in_a_straight_line()
    {
        using var sim = BareStore();
        var villager = sim.Scenario.PlaceVillager(OpenFloor);
        var target = OpenFloor + new Vector2(30f, 0f);

        sim.Scenario.SendVillager(villager.Id, target);
        var walked = WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(2));

        var arrived = VillagerIn(sim.Snapshot, villager.Id);
        Assert.Null(arrived.Destination);
        Assert.True(Vector2.Distance(target, arrived.Position) < 0.01f, $"Ended at {arrived.Position}.");
        Assert.InRange(Length(sim.Snapshot.Space, walked), 30f, 30.5f);
        // At walking speed 30 m take a quarter of a Store hour.
        Assert.InRange(walked.Count - 1, StoreClock.TicksFor(TimeSpan.FromHours(30 / Villager.WalkingSpeed)) - 1,
            StoreClock.TicksFor(TimeSpan.FromHours(30 / Villager.WalkingSpeed)) + 1);
    }

    [Fact]
    public void A_Villager_reaches_a_target_behind_a_wall_of_Furniture_by_walking_around_it()
    {
        using var sim = BareStore();
        var wall = PlaceWall(sim, OpenFloor + new Vector2(10f, 0f), wardrobes: 12);
        var villager = sim.Scenario.PlaceVillager(OpenFloor);
        var target = OpenFloor + new Vector2(20f, 0f);

        sim.Scenario.SendVillager(villager.Id, target);
        var walked = WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(2));

        var space = sim.Snapshot.Space;
        Assert.True(space.Distance(target, VillagerIn(sim.Snapshot, villager.Id).Position) < 0.01f);
        Assert.DoesNotContain(walked, p => wall.Any(piece => Inside(space, piece, p)));
        // Around a 12 m wall from 10 m in front of its middle: at least 2 × √(10² + 6²) ≈ 23.3 m, and no wild detour.
        Assert.InRange(Length(space, walked), 23f, 30f);
    }

    [Fact]
    public void A_Villager_crosses_a_seam_by_the_shortest_wrapped_path()
    {
        using var sim = BareStore();
        var size = sim.Snapshot.Space.Size;
        var villager = sim.Scenario.PlaceVillager(new Vector2(size - 6f, 100f));
        var target = new Vector2(6f, 100f);

        sim.Scenario.SendVillager(villager.Id, target);
        var walked = WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(1));

        var space = sim.Snapshot.Space;
        Assert.True(space.Distance(target, VillagerIn(sim.Snapshot, villager.Id).Position) < 0.01f);
        Assert.InRange(Length(space, walked), 12f, 12.5f);
        // Every step stays near the seam: positions wrap from the east edge to the west one, never through the middle.
        Assert.All(walked, p => Assert.True(p.X > size - 7f || p.X < 7f, $"Walked through {p}."));
        Assert.All(walked, p => Assert.InRange(p.X, 0f, size));
    }

    [Fact]
    public void A_Villager_walks_around_Furniture_on_a_seam()
    {
        using var sim = BareStore();
        var size = sim.Snapshot.Space.Size;
        var wall = PlaceWall(sim, new Vector2(0f, 100f), wardrobes: 8);
        var villager = sim.Scenario.PlaceVillager(new Vector2(size - 5f, 100f));
        var target = new Vector2(5f, 100f);

        sim.Scenario.SendVillager(villager.Id, target);
        var walked = WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(1));

        var space = sim.Snapshot.Space;
        Assert.True(space.Distance(target, VillagerIn(sim.Snapshot, villager.Id).Position) < 0.01f);
        Assert.DoesNotContain(walked, p => wall.Any(piece => Inside(space, piece, p)));
        Assert.InRange(Length(space, walked), 12f, 20f);
    }

    [Fact]
    public void Navigation_is_rebuilt_when_Furniture_moves_into_a_chunk()
    {
        using var sim = BareStore();
        var space = sim.Snapshot.Space;
        var villager = sim.Scenario.PlaceVillager(OpenFloor);
        var target = OpenFloor + new Vector2(20f, 0f);
        // The wall stands far away while the Villager first walks, so the chunk's navigation is built without it.
        var wall = PlaceWall(sim, OpenFloor + new Vector2(0f, 300f), wardrobes: 12);
        sim.Scenario.SendVillager(villager.Id, target);
        Assert.InRange(Length(space, WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(1))), 20f, 20.5f);

        var width = FurnitureCatalog.Spec(FurnitureType.Wardrobe).Width;
        var moved = wall.Select((piece, i) => sim.Scenario.MoveFurniture(piece.Id,
            OpenFloor + new Vector2(10f, (i - 5.5f) * width), MathF.PI / 2f)).ToList();
        sim.Scenario.SendVillager(villager.Id, OpenFloor);
        var walked = WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(2));

        Assert.True(space.Distance(OpenFloor, VillagerIn(sim.Snapshot, villager.Id).Position) < 0.01f);
        Assert.DoesNotContain(walked, p => moved.Any(piece => Inside(space, piece, p)));
        Assert.InRange(Length(space, walked), 23f, 30f);
    }

    [Fact]
    public void Navigation_is_rebuilt_when_Furniture_moves_out_of_a_chunk()
    {
        using var sim = BareStore();
        var space = sim.Snapshot.Space;
        var wall = PlaceWall(sim, OpenFloor + new Vector2(10f, 0f), wardrobes: 12);
        var villager = sim.Scenario.PlaceVillager(OpenFloor);
        var target = OpenFloor + new Vector2(20f, 0f);
        sim.Scenario.SendVillager(villager.Id, target);
        Assert.True(Length(space, WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(2))) > 23f);

        sim.Scenario.RemoveFurniture(piece => wall.Any(w => w.Id == piece.Id));
        sim.Scenario.SendVillager(villager.Id, OpenFloor);

        Assert.InRange(Length(space, WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(1))), 20f, 20.5f);
    }

    [Fact]
    public void A_walking_Villager_turns_aside_when_Furniture_is_moved_into_their_way()
    {
        using var sim = BareStore();
        var space = sim.Snapshot.Space;
        var villager = sim.Scenario.PlaceVillager(OpenFloor);
        var target = OpenFloor + new Vector2(40f, 0f);
        sim.Scenario.SendVillager(villager.Id, target);
        sim.Advance(TimeSpan.FromHours(10 / Villager.WalkingSpeed));

        var wall = PlaceWall(sim, OpenFloor + new Vector2(25f, 0f), wardrobes: 12);
        var walked = WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(2));

        Assert.True(space.Distance(target, VillagerIn(sim.Snapshot, villager.Id).Position) < 0.01f);
        Assert.DoesNotContain(walked, p => wall.Any(piece => Inside(space, piece, p)));
    }

    [Fact]
    public void A_Villager_sent_into_Furniture_stops_beside_it()
    {
        using var sim = BareStore();
        var wardrobe = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, OpenFloor + new Vector2(10f, 0f), 0f);
        var villager = sim.Scenario.PlaceVillager(OpenFloor);

        sim.Scenario.SendVillager(villager.Id, wardrobe.Position);
        WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(1));

        var arrived = VillagerIn(sim.Snapshot, villager.Id);
        Assert.Null(arrived.Destination);
        Assert.False(Inside(sim.Snapshot.Space, wardrobe, arrived.Position));
        Assert.True(sim.Snapshot.Space.Distance(wardrobe.Position, arrived.Position) < 2f);
    }

    [Fact]
    public void A_Villager_walks_around_a_Pillar()
    {
        using var sim = BareStore();
        var pillar = sim.Snapshot.Pillars[0];
        var start = pillar.Position - new Vector2(pillar.Width / 2f + 3f, 0f);
        var target = pillar.Position + new Vector2(pillar.Width / 2f + 3f, 0f);
        var villager = sim.Scenario.PlaceVillager(start);

        sim.Scenario.SendVillager(villager.Id, target);
        var walked = WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(2));

        var space = sim.Snapshot.Space;
        Assert.True(space.Distance(target, VillagerIn(sim.Snapshot, villager.Id).Position) < 0.01f);
        Assert.All(walked, p =>
        {
            var d = space.Delta(pillar.Position, p);
            Assert.True(MathF.Abs(d.X) >= pillar.Width / 2f || MathF.Abs(d.Y) >= pillar.Width / 2f, $"Walked through the Pillar at {p}.");
        });
    }

    [Fact]
    public void A_Villager_finds_their_way_through_a_generated_Store()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1));
        var space = sim.Snapshot.Space;
        var pillar = sim.Snapshot.Pillars[0];
        // From the walkway ring of a Pillar out into the Displays and back along another side.
        var villager = sim.Scenario.PlaceVillager(pillar.Position + new Vector2(0f, pillar.Width / 2f + 2f));
        var target = pillar.Position + new Vector2(60f, 50f);

        sim.Scenario.SendVillager(villager.Id, target);
        var walked = WalkUntilArrived(sim, villager.Id, TimeSpan.FromHours(4));

        var blocking = sim.Snapshot.Furniture.Where(p => !FurnitureCatalog.Spec(p).IsFloorCovering && p.Elevation < 1f
            && p.Tilt == 0f && p.Roll == 0f && space.Distance(p.Position, pillar.Position) < 150f).ToList();
        Assert.Null(VillagerIn(sim.Snapshot, villager.Id).Destination);
        Assert.True(space.Distance(target, VillagerIn(sim.Snapshot, villager.Id).Position) < 3f);
        Assert.DoesNotContain(walked, p => blocking.Any(piece => Inside(space, piece, p)));
    }
}
