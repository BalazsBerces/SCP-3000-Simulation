using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// Where a Village keeps Furniture it has gathered but not yet built in: inside the walls, or outside behind a barricade of its own
/// (<see cref="WallIndex"/> into <see cref="Village.Walls"/>).
/// </summary>
public sealed record FurnitureYard(Vector2 Position, bool Inside, int? WallIndex)
{
    /// <summary>Furniture within this distance of <see cref="Position"/> waits in the yard.</summary>
    public const float Radius = 2;
    /// <summary>The most pieces builders gather into the yard.</summary>
    public const int Capacity = 6;
    /// <summary>A Village this large has no room for the yard inside its walls.</summary>
    public const int CrowdedPopulation = 8;
    internal static readonly Vector2 InsideOffset = new(3.8f, -0.5f), OutsideOffset = new(16, 0);
}

public sealed partial class ScenarioBuilder
{
    public FurnitureYard PlanFurnitureYard(int villageId) => _simulation.PlanFurnitureYard(villageId);
}

public sealed partial class Simulation
{
    /// <summary>A cautious Village on a small site keeps its yard inside; any other keeps it outside behind its own barricade.</summary>
    internal FurnitureYard PlanFurnitureYard(int villageId) => Publish(() =>
    {
        var village = _villages.Single(v => v.Id == villageId);
        if (village.FurnitureYard is { } existing) return existing;
        var inside = village.Personality == Personality.Cautious && village.VillagerIds.Count < FurnitureYard.CrowdedPopulation;
        var yard = new FurnitureYard(_space.Wrap(village.Position + (inside ? FurnitureYard.InsideOffset : FurnitureYard.OutsideOffset)), inside, null);
        if (!inside)
            yard = yard with { WallIndex = village.Walls.Count };
        village = village with { FurnitureYard = yard };
        if (!inside)
            village = village with { Walls = Array.AsReadOnly(village.Walls.Append(CreateBarricade(village, yard.Position)).ToArray()) };
        _villages[villageId] = village;
        return yard;
    });

    private bool InYard(Village village, FurniturePiece piece) =>
        village.FurnitureYard is { } yard && _space.Distance(yard.Position, piece.Position) <= FurnitureYard.Radius;

    private static bool YardFurniture(FurniturePiece p) =>
        p.Elevation == 0 && p.Tilt == 0 && p.Roll == 0 && FurnitureCatalog.Spec(p) is { IsFixed: false, IsFloorCovering: false, IsLamp: false } &&
        p.Type is FurnitureType.Chair or FurnitureType.GardenChair or FurnitureType.HallBench or FurnitureType.BarStool or
            FurnitureType.DiningTable or FurnitureType.GardenTable or FurnitureType.MeetingTable or FurnitureType.Desk or
            FurnitureType.KitchenCabinet or FurnitureType.Counter or FurnitureType.Stove or FurnitureType.Grill or
            FurnitureType.ShelfUnit or FurnitureType.Bookcase or FurnitureType.PalletRack or FurnitureType.GroceryShelf or FurnitureType.Wardrobe;

    // A builder with nothing to build hauls the nearest loose piece to the yard, until it holds FurnitureYard.Capacity.
    private bool GatherToYard(Village village, Villager worker)
    {
        if (_villages[village.Id].FurnitureYard is not { } yard) return false;
        var held = _furniture.Pieces.Count(p => _space.Distance(yard.Position, p.Position) <= FurnitureYard.Radius && !IsBuiltFurniture(p.Id)) +
            _furnitureTasks.Count(t => !t.Started && _space.Distance(t.Destination, yard.Position) <= FurnitureYard.Radius);
        if (held >= FurnitureYard.Capacity) return false;
        var spot = yard.Position + new Vector2(held % 3 - 1, held / 3 - 0.5f) * 1.2f;
        foreach (var piece in _villagers.FurnitureNear(village.Position, 48).Where(p =>
            YardFurniture(p) && _space.Distance(yard.Position, p.Position) > FurnitureYard.Radius && !IsBuiltFurniture(p.Id) &&
            _space.Distance(village.Position, p.Position) <= 48 && !_furnitureTasks.Any(t => t.FurnitureId == p.Id))
            .OrderBy(p => _space.Distance(worker.Position, p.Position)))
        {
            try
            {
                StartFurnitureTask(piece.Id, [worker.Id], _space.Wrap(spot), false);
                return true;
            }
            catch (InvalidOperationException) { /* Try the next reachable, haulable piece. */ }
        }
        return false;
    }

    private void ValidateFurnitureYards()
    {
        foreach (var village in _villages)
            if (village.FurnitureYard is { } yard && (!float.IsFinite(yard.Position.X) || !float.IsFinite(yard.Position.Y) ||
                yard.Position != _space.Wrap(village.Position + (yard.Inside ? FurnitureYard.InsideOffset : FurnitureYard.OutsideOffset)) ||
                yard.Inside != (yard.WallIndex is null) ||
                yard.WallIndex is { } w && (w < 0 || w >= village.Walls.Count || village.Outposts.Any(o => o.WallIndex == w))))
                throw new InvalidDataException("Invalid world save: inconsistent Furniture yard.");
    }
}
