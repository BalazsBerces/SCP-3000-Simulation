using System.Numerics;

namespace Scp3008.Core;

/// <summary>A Staff carried a displaced piece back to its original place at Closing time (Tidying).</summary>
public sealed record FurnitureTidied(StoreClock At, int FurnitureId, int StaffId, Vector2 From, Vector2 To) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Fills every empty slot of a Village's wall with a bookcase at once, as if the builders had finished it.</summary>
    public void CompleteWall(int villageId, int ring) => _simulation.CompleteWall(villageId, ring);
}

public sealed partial class Simulation
{
    internal void CompleteWall(int villageId, int ring) => Publish(() =>
    {
        var slots = _villages[villageId].Walls[ring].Slots;
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i].FurnitureId is not null) continue;
            var piece = PlaceFurniture(FurnitureType.Bookcase, slots[i].Position, slots[i].Rotation, 0, 0);
            SetWallSlot(villageId, ring, i, slots[i] with { FurnitureId = piece.Id, ReservedFurnitureId = null });
        }
        return true;
    });

    // How near a Staff must pass to take a displaced piece in hand.
    private const float TidyReach = 6f;
    // A piece with a Villager this close is guarded: the Staff leave it be (they notice the Villager instead).
    private const float TidyGuard = 2.5f;

    // Where each moved piece first stood (id -> that pose); Tidying carries it back. Pieces built into a Village never come back.
    private readonly Dictionary<int, FurniturePiece> _displaced = [];

    /// <summary>Keeps the first pose of a piece that is moved, and forgets it once the piece is back.</summary>
    private void TrackDisplacement(FurniturePiece before, FurniturePiece moved)
    {
        if (moved.Position == before.Position && moved.Rotation == before.Rotation && moved.Elevation == before.Elevation) return;
        var origin = _displaced.GetValueOrDefault(before.Id, before);
        if (moved.Position == origin.Position && moved.Rotation == origin.Rotation && moved.Elevation == origin.Elevation) _displaced.Remove(before.Id);
        else _displaced[before.Id] = origin;
    }

    private void AdvanceTidying(StoreClock clock)
    {
        if (_displaced.Count == 0 || clock.Phase != DayPhase.ClosingTime || clock.Tick % 5 != 0) return;
        var staff = _staff.Snapshot().Where(s => Acting(s, clock) && _hordes.All(h => h.ChaseId < 0 || !h.StaffIds.Contains(s.Id))).ToArray();
        if (staff.Length == 0) return;
        var villagers = _villagers.Snapshot();
        foreach (var (id, origin) in _displaced.ToArray())
        {
            var index = PieceIndex(id);
            if (index < 0) { _displaced.Remove(id); continue; }
            var piece = _furniture.Pieces[index];
            if (Protected(piece) || IsHeld(id) || villagers.Any(v => _space.Distance(v.Position, piece.Position) <= TidyGuard) || _furnitureTasks.Any(t => t.FurnitureId == id)) continue;
            if (staff.FirstOrDefault(s => _space.Distance(s.Position, piece.Position) <= TidyReach) is not { } carrier) continue;
            _piecesIndex.Update(_furniture.Pieces);
            var home = piece with { Position = origin.Position, Rotation = origin.Rotation, Elevation = origin.Elevation };
            if (_piecesIndex.Near(home.Position, 6f).Any(p => p.Id != id && RestockSight.Overlaps(_space, home, p))) continue;
            MoveFurnitureCore(id, origin.Position, origin.Rotation, origin.Elevation);
            Emit(new FurnitureTidied(clock, id, carrier.Id, piece.Position, origin.Position));
        }
    }

    /// <summary>Furniture built into a Village, in a Furniture yard inside the walls, or behind a complete wall is never Tidied.</summary>
    private bool Protected(FurniturePiece piece) =>
        IsBuiltFurniture(piece.Id) || _villages.Any(v =>
            v.FurnitureYard is { Inside: true } yard && _space.Distance(yard.Position, piece.Position) <= FurnitureYard.Radius ||
            v.Walls.Any(w => w.Complete && Encloses(w, piece.Position)));

    private bool Encloses(Blueprint wall, Vector2 point)
    {
        // Ray crossing, with the vertices taken as offsets from the point so a wall across the Store seam still closes.
        var inside = false;
        for (var i = 0; i < wall.Vertices.Count - 1; i++)
        {
            var a = _space.Delta(point, wall.Vertices[i]);
            var b = _space.Delta(point, wall.Vertices[i + 1]);
            if (a.Y > 0 != b.Y > 0 && a.X + (0 - a.Y) / (b.Y - a.Y) * (b.X - a.X) > 0) inside = !inside;
        }
        return inside;
    }
}
