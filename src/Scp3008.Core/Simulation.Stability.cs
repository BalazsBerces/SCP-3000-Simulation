namespace Scp3008.Core;

public sealed partial class Simulation
{
    /// <summary>The poses that changed since the last tick (where pieces left and arrived); the structures touching them are checked.</summary>
    private readonly List<FurniturePiece> _stabilityChanges = [];

    /// <summary>How many pieces must fall for a collapse to enter the Chronicle.</summary>
    private const int NotableCollapse = 3;

    /// <summary>Whether people are carrying the piece: it rests on nothing and holds nothing up.</summary>
    private bool IsHeld(int furnitureId) =>
        _furnitureTasks.Any(t => t.FurnitureId == furnitureId && t.Started && t.Work != FurnitureWork.Disassemble);

    /// <summary>Settles the structures touching this tick's changes: overloaded pieces give way and unsupported ones fall.</summary>
    private void AdvanceStability(StoreClock clock)
    {
        if (_stabilityChanges.Count == 0)
            return;
        var changed = _stabilityChanges.ToArray();
        _stabilityChanges.Clear();
        // Only Furniture near the changes can be part of their structures; it is handed over in list order.
        _piecesIndex.Update(_furniture.Pieces);
        var near = changed.SelectMany(pose => _piecesIndex.NearIndices(pose.Position, Stability.RegionReach))
            .Distinct().Order().Select(i => _furniture.Pieces[i]).ToArray();
        var (broken, fallen, origin) = Stability.Settle(near, _space, changed, IsHeld);
        if (broken.Count == 0 && fallen.Count == 0)
            return;
        var wrecked = broken.ToHashSet();
        var landed = new List<FallenPiece>();
        foreach (var fall in fallen)
        {
            var to = wrecked.Contains(fall.To.Id) ? fall.To with { Damage = 1f } : fall.To;
            if (IsBuiltFurniture(to.Id)) UnbuildFurniture(to.Id);
            CancelFurnitureTasks(t => t.FurnitureId == to.Id);
            var index = PieceIndex(to.Id);
            landed.Add(new FallenPiece(fall.From, SetPose(index, _furniture.Pieces[index], to)));
        }
        foreach (var id in wrecked.Where(id => !fallen.Any(f => f.From.Id == id)))
        {
            var index = PieceIndex(id);
            SetPose(index, _furniture.Pieces[index], _furniture.Pieces[index] with { Damage = 1f });
        }
        Emit(new StructureCollapsed(clock, origin, landed, broken));
    }

    internal void DamageFurniture(int furnitureId, float damage) => Publish(() =>
    {
        var index = PieceIndex(furnitureId);
        if (index < 0)
            throw new ArgumentException($"No piece {furnitureId}.", nameof(furnitureId));
        var damaged = _furniture.Pieces[index] with { Damage = damage };
        if (damage >= 1f && IsBuiltFurniture(furnitureId)) UnbuildFurniture(furnitureId);
        _stabilityChanges.Add(damaged);
        return SetPose(index, _furniture.Pieces[index], damaged);
    });
}

public sealed partial class ScenarioBuilder
{
    /// <summary>Sets how damaged a piece of Furniture is, from 0 (whole) to 1 (wrecked); its structure is checked at the next tick.</summary>
    public void DamageFurniture(int furnitureId, float damage)
    {
        if (!(damage is >= 0f and <= 1f))
            throw new ArgumentOutOfRangeException(nameof(damage), "Damage lies from 0 to 1.");
        _simulation.DamageFurniture(furnitureId, damage);
    }
}
