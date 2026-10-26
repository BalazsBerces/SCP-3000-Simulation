using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class RestockTests
{
    private static Simulation EmptyStore(double furnitureHours = 1) => Simulation.Create(new WorldConfig(3008,
        PillarLayout: PillarLayout.VillagerSandbox, Restock: new RestockConfig { FurnitureHours = furnitureHours }));

    [Theory]
    [InlineData(-1, 1, 2)]
    [InlineData(double.NaN, 1, 2)]
    [InlineData(double.PositiveInfinity, 1, 2)]
    [InlineData(1, -1, 2)]
    [InlineData(1, 1, double.NaN)]
    public void Invalid_Restock_configuration_is_rejected(double goods, double furniture, double landmark)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Simulation.Create(new WorldConfig(3008,
            PillarLayout: PillarLayout.VillagerSandbox, Restock: new RestockConfig
            { GoodsPerStoreHour = goods, FurnitureHours = furniture, LandmarkFurnitureHours = landmark })));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Furniture_delay_is_configurable_and_zero_disables_return(double delay, bool returns)
    {
        using var sim = EmptyStore(delay);
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new Vector2(20));
        sim.Scenario.RemoveFurniture(p => p.Id == piece.Id);
        sim.Advance(TimeSpan.FromHours(1) - StoreClock.TickDuration);
        Assert.Empty(sim.Snapshot.Furniture);
        sim.AdvanceTicks(1);
        Assert.Equal(returns, sim.Snapshot.Furniture.Contains(piece));
    }

    [Fact]
    public void Returned_Furniture_rebuilds_walking_routes_and_reserved_ids_are_not_reused()
    {
        using var sim = EmptyStore();
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, new Vector2(30));
        sim.Scenario.PlaceVillager(new Vector2(70, 80));
        sim.Scenario.RemoveFurniture(p => p.Id == piece.Id);
        var other = sim.Scenario.PlaceFurniture(FurnitureType.CoffeeTable, new Vector2(80));
        Assert.NotEqual(piece.Id, other.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(piece, sim.Snapshot.Furniture);
        var walker = sim.Scenario.PlaceVillager(new Vector2(27, 30));
        sim.Scenario.SendVillager(walker.Id, new Vector2(33, 30));
        var walkedAround = false;
        for (var i = 0; i < 100; i++)
        {
            sim.AdvanceTicks(1);
            var current = sim.Snapshot.Villagers.Single(v => v.Id == walker.Id);
            if (Math.Abs(current.Position.Y - 30) > FurnitureCatalog.Spec(piece).Depth / 2)
                walkedAround = true;
            if (current.Destination is null)
            {
                Assert.InRange(sim.Snapshot.Space.Distance(current.Position, new Vector2(33, 30)), 0, 0.01f);
                break;
            }
        }
        Assert.True(walkedAround);
    }

    [Fact]
    public void Returned_Washbasins_keep_their_Water_points()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, PointOfInterestShowcase: true,
            Restock: new RestockConfig { FurnitureHours = 1 }));
        var original = sim.Snapshot.WaterPoints.First(w => w.FurnitureId >= 0 && w.Running);
        sim.Scenario.RemoveFurniture(p => p.Id == original.FurnitureId);
        Assert.DoesNotContain(original, sim.Snapshot.WaterPoints);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(original, sim.Snapshot.WaterPoints);
        Assert.Single(sim.Snapshot.WaterPoints, w => w == original);
    }

    [Fact]
    public void Furniture_waits_while_seen_and_returns_when_the_Villager_leaves()
    {
        using var sim = EmptyStore();
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new Vector2(20));
        var villager = sim.Scenario.PlaceVillager(new Vector2(25, 20));
        sim.Scenario.RemoveFurniture(p => p.Id == piece.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Empty(sim.Snapshot.Furniture);

        sim.Scenario.SendVillager(villager.Id, new Vector2(70, 20));
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(piece, Assert.Single(sim.Snapshot.Furniture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Thick_fog_blocks_sight_also_across_a_Seam(bool acrossSeam)
    {
        using var sim = EmptyStore();
        var spot = acrossSeam ? new Vector2(2, 20) : new Vector2(20);
        var piece = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, spot);
        sim.Scenario.PlaceVillager(acrossSeam ? new Vector2(125, 20) : new Vector2(25, 20));
        sim.Scenario.RemoveFurniture(p => p.Id == piece.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Empty(sim.Snapshot.Furniture);

        sim.Scenario.HoldWind(Vector2.Zero);
        sim.Scenario.PlaceFogBank(spot, 15);
        sim.AdvanceTicks(1);
        Assert.Equal(piece, Assert.Single(sim.Snapshot.Furniture));
    }

    [Fact]
    public void A_tall_blocker_hides_a_spot_but_a_low_table_does_not()
    {
        foreach (var type in new[] { FurnitureType.Wardrobe, FurnitureType.CoffeeTable })
        {
            using var sim = EmptyStore();
            var piece = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new Vector2(20));
            sim.Scenario.PlaceFurniture(type, new Vector2(23, 20), MathF.PI / 2);
            sim.Scenario.PlaceVillager(new Vector2(26, 20));
            sim.Scenario.RemoveFurniture(p => p.Id == piece.Id);
            sim.Advance(TimeSpan.FromHours(1));
            Assert.Equal(type == FurnitureType.Wardrobe, sim.Snapshot.Furniture.Contains(piece));
        }
    }

    [Fact]
    public void Occupied_spots_wait_and_moved_Furniture_does_not_duplicate_its_id()
    {
        using var sim = EmptyStore();
        var original = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new Vector2(20));
        sim.Scenario.MoveFurniture(original.Id, new Vector2(30), 0);
        var occupant = sim.Scenario.PlaceFurniture(FurnitureType.Wardrobe, original.Position);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, sim.Snapshot.Furniture.Count);
        sim.Scenario.MoveFurniture(occupant.Id, new Vector2(40), 0);
        sim.AdvanceTicks(1);
        var returned = Assert.Single(sim.Snapshot.Furniture, p => p.Position == original.Position);
        Assert.Equal(FurnitureType.Armchair, returned.Type);
        Assert.NotEqual(original.Id, returned.Id);
        Assert.Equal(sim.Snapshot.Furniture.Count, sim.Snapshot.Furniture.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Landmark_Furniture_can_return_into_its_original_arrangement()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, LandmarkShowcase: true,
            Restock: new RestockConfig { LandmarkFurnitureHours = 1 }));
        var canyon = sim.Snapshot.Landmarks.Single(l => l.Type == LandmarkType.RackCanyon);
        var box = sim.Snapshot.Furniture.First(p => p.LandmarkId == canyon.Id && p.Type == FurnitureType.FlatPackBox && p.Elevation > 1);
        sim.Scenario.RemoveFurniture(p => p.Id == box.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(box, sim.Snapshot.Furniture);
    }

    [Fact]
    public void Landmark_Furniture_returns_more_slowly_than_ordinary_Furniture()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, LandmarkShowcase: true,
            Restock: new RestockConfig { FurnitureHours = 1, LandmarkFurnitureHours = 2 }));
        var landmark = sim.Snapshot.Furniture.First(p => p.LandmarkId >= 0 && !FurnitureCatalog.Spec(p).IsFixed);
        sim.Scenario.RemoveFurniture(p => p.LandmarkId == landmark.LandmarkId);
        var ordinary = sim.Scenario.PlaceFurniture(FurnitureType.Armchair, new Vector2(20));
        sim.Scenario.RemoveFurniture(p => p.Id == ordinary.Id);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(ordinary, sim.Snapshot.Furniture);
        Assert.DoesNotContain(landmark, sim.Snapshot.Furniture);
        sim.Advance(TimeSpan.FromHours(1));
        Assert.Contains(landmark, sim.Snapshot.Furniture);
    }

    [Fact]
    public void Set_dinner_tables_Cafeterias_Lost_and_found_and_Aquarium_aisles_refill()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1, PointOfInterestShowcase: true,
            Restock: new RestockConfig { GoodsPerStoreHour = 2 }));
        var initial = sim.Snapshot;
        PointOfInterestType[] types = [PointOfInterestType.SetDinnerTable, PointOfInterestType.Cafeteria,
            PointOfInterestType.LostAndFound, PointOfInterestType.AquariumAisle];
        var pieces = initial.Furniture.ToDictionary(p => p.Id);
        var goods = types.Select(type => initial.Stock.First(g => !g.LeftBehind &&
            pieces[g.FurnitureId].PointOfInterestId >= 0 && initial.PointsOfInterest[pieces[g.FurnitureId].PointOfInterestId].Type == type)).ToArray();
        sim.Scenario.SetStock(goods.Select(g => (g.FurnitureId, g.Kind)), 0);
        sim.Advance(TimeSpan.FromHours(0.5));
        Assert.All(goods, g => Assert.Equal(1, sim.Snapshot.Stock.Single(s => s.FurnitureId == g.FurnitureId && s.Kind == g.Kind).Amount));
        Assert.NotSame(initial.Stock, sim.Snapshot.Stock);
        Assert.All(goods, g => Assert.Contains(g, initial.Stock));
    }

    [Fact]
    public void Bare_Stock_Furniture_refills_but_showroom_fridges_and_cabinets_stay_empty()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, StartingBands: false, RuntimeRngSeed: 1,
            Stock: new StockConfig { StockedShelfShare = 0, LeftBehindShare = 0, TableFoodChance = 0, SectorGuarantee = false },
            Restock: new RestockConfig { GoodsPerStoreHour = 1 }));
        var initial = sim.Snapshot;
        var pieces = initial.Furniture.ToDictionary(p => p.Id);
        var displays = initial.Furniture.Where(p => p.DisplayId >= 0 && !initial.IsStockFurniture(p) &&
            p.Type is FurnitureType.Fridge or FurnitureType.KitchenCabinet).Select(p => p.Id).ToHashSet();
        var shelves = initial.Furniture.Where(p => p.DisplayId >= 0 && initial.IsStockFurniture(p)).Select(p => p.Id).ToHashSet();
        Assert.NotEmpty(displays);
        Assert.NotEmpty(shelves);
        Assert.DoesNotContain(initial.Stock, g => shelves.Contains(g.FurnitureId));
        sim.Advance(TimeSpan.FromHours(2));
        Assert.DoesNotContain(sim.Snapshot.Stock, g => displays.Contains(g.FurnitureId));
        Assert.Contains(sim.Snapshot.Stock, g => shelves.Contains(g.FurnitureId));
        Assert.All(sim.Snapshot.Stock.Where(g => !g.LeftBehind), g => Assert.True(sim.Snapshot.IsStockFurniture(pieces[g.FurnitureId])));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Emptied_shelves_refill_at_the_configured_units_per_Store_hour(double rate, int expected)
    {
        using var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            Restock: new RestockConfig { GoodsPerStoreHour = rate }));
        var shelf = sim.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new Vector2(20));
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 3);
        sim.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 0);

        sim.Advance(TimeSpan.FromHours(0.5));
        sim.Advance(TimeSpan.FromHours(0.5));

        Assert.Equal(expected, sim.Snapshot.Stock.Where(g => g.FurnitureId == shelf.Id).Sum(g => g.Amount));
        sim.Advance(TimeSpan.FromHours(4));
        Assert.Equal(rate == 0 ? 0 : 3, sim.Snapshot.Stock.Where(g => g.FurnitureId == shelf.Id).Sum(g => g.Amount));
    }
}
