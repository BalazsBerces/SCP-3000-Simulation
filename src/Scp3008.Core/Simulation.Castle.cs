using System.Numerics;

namespace Scp3008.Core;

public sealed record CastlePiecePlaced(StoreClock At, int VillageId, int Structure, int Slot, int FurnitureId) : DomainEvent(At);

public sealed partial class Simulation
{
    // Fabric pieces this close to a Village give its Heraldry; standing water this close to a Moat fills it.
    private const float LocalFabricRange = 30, MoatWaterRange = 30;

    private bool InPillar(Vector2 position, float margin) => _pillars.Any(p =>
        _space.Delta(p.Position, position) is var d && MathF.Abs(d.X) < p.Width / 2 + margin && MathF.Abs(d.Y) < p.Width / 2 + margin);

    /// <summary>Plans a curtain wall's Towers at its corners, a Gatehouse in its gate, the Keep (once) and a Moat outside it.</summary>
    private void PlanCastle(int villageId, int ring)
    {
        var village = _villages[villageId];
        var wall = village.Walls[ring];
        var corners = wall.Vertices.Take(wall.Vertices.Count - 1).ToArray();
        var centre = corners[0] + corners.Select(v => _space.Delta(corners[0], v)).Aggregate(Vector2.Zero, (a, b) => a + b) / corners.Length;
        var structures = new List<CastleStructure>();

        var towerCorners = wall.Outline switch
        {
            WallOutline.Round => [2, 5, 8, 11],
            WallOutline.Square => [0, 1, 2, 3],
            _ => new[] { 2, 3 },
        };
        foreach (var corner in towerCorners)
        {
            var outward = Vector2.Normalize(_space.Delta(centre, corners[corner]));
            var at = _space.Wrap(corners[corner] + outward * 1.2f);
            // Fitted to the terrain: no Tower inside a Pillar.
            if (InPillar(at, 1f)) continue;
            var slot = new BlueprintSlot(at, MathF.Atan2(outward.X, -outward.Y), 0.8f, WallRun.Corner, 1.2f);
            structures.Add(new(CastlePart.Tower, ring, [slot, slot, slot]));
        }

        var gate = _space.Delta(wall.GateStart, wall.GateEnd);
        var tangent = Vector2.Normalize(gate);
        var gateCentre = wall.GateStart + gate / 2;
        var rotation = MathF.Atan2(tangent.Y, tangent.X);
        var flank = Castle.PassageHalfWidth + 0.5f;
        structures.Add(new(CastlePart.Gatehouse, ring, [
            new(_space.Wrap(gateCentre - tangent * flank), rotation, 0.4f, WallRun.Straight, 1.8f),
            new(_space.Wrap(gateCentre + tangent * flank), rotation, 0.4f, WallRun.Straight, 1.8f),
            new(_space.Wrap(gateCentre), rotation, 1.6f, WallRun.Straight, 2f)]));

        if (!village.Castle.Any(c => c.Part == CastlePart.Keep))
        {
            var keep = village.Position + Castle.KeepOffset;
            BlueprintSlot Base(float x) => new(_space.Wrap(keep + new Vector2(x, 0)), 0, 0.8f, WallRun.Straight, 0.5f);
            structures.Add(new(CastlePart.Keep, ring, [Base(-0.85f), Base(0.85f), Base(-0.85f), Base(0.85f)]));
        }

        // The Moat follows the ring's edges outside it (not along a freeform ring's anchor face), with a causeway before the gate.
        var runs = new List<MoatRun>();
        var gateEdge = wall.Outline == WallOutline.Freeform ? 2 : 0;
        var reach = Castle.MoatOffset * MathF.Tan(MathF.PI / (wall.Outline == WallOutline.Round ? 12 : 4));
        for (var i = wall.Outline == WallOutline.Freeform ? 1 : 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var edge = _space.Delta(a, corners[(i + 1) % corners.Length]);
            var along = Vector2.Normalize(edge);
            var normal = new Vector2(-along.Y, along.X);
            if (Vector2.Dot(normal, _space.Delta(centre, a + edge / 2)) < 0) normal = -normal;
            var start = a - along * reach + normal * Castle.MoatOffset;
            var end = a + edge + along * reach + normal * Castle.MoatOffset;
            var pieces = i != gateEdge ? new[] { (start, end) } : [
                (start, a + _space.Delta(a, wall.GateStart) + normal * Castle.MoatOffset),
                (a + _space.Delta(a, wall.GateEnd) + normal * Castle.MoatOffset, end)];
            foreach (var (from, to) in pieces)
                if (!InPillar(from + (to - from) / 2, 0))
                    runs.Add(new(_space.Wrap(from), _space.Wrap(to)));
        }
        // The flooded parts of the Store are the fountain court and the Camping lake: a Moat dug near them fills.
        var water = runs.Any(r => r.Start + _space.Delta(r.Start, r.End) / 2 is var mid &&
            (GroundAt(mid).HasFlag(GroundProperty.Water) || _waterPoints.Any(w => w.Kind is WaterPointKind.FountainCourt or WaterPointKind.CampingLake &&
                _space.Distance(w.Position, mid) <= MoatWaterRange)));

        _villages[villageId] = village with
        {
            Castle = Array.AsReadOnly(village.Castle.Concat(structures).ToArray()),
            Moats = runs.Count == 0 ? village.Moats : Array.AsReadOnly(village.Moats.Append(new Moat(ring, runs.AsReadOnly(), water)).ToArray()),
        };
    }

    /// <summary>
    /// The arms of a Village: the two commonest tints among the fabric pieces around it, and the first division no other Village
    /// with those tints bears.
    /// </summary>
    private Heraldry ChooseHeraldry(Village village)
    {
        // Planned rarely, and also from scenarios outside a tick, where the Furniture index is not built.
        var tints = _furniture.Pieces.Where(p => _space.Distance(village.Position, p.Position) <= LocalFabricRange && p.Tint is >= 0 and < FurniturePalettes.TintCount &&
                FurnitureResources.Yield(p).GetValueOrDefault(MaterialKind.Fabric) > 0)
            .GroupBy(p => p.Tint).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToArray();
        var field = tints.Length > 0 ? tints[0] : village.Id % FurniturePalettes.TintCount;
        var charge = tints.Length > 1 ? tints[1] : (field + FurniturePalettes.TintCount / 2) % FurniturePalettes.TintCount;
        // shortcut: after six Villages with the same two tints the arms repeat; add a charge shape if that shows up.
        var division = Enumerable.Range(0, 6).Select(i => (HeraldryDivision)((village.Id + i) % 6))
            .FirstOrDefault(d => !_villages.Any(v => v.Id != village.Id && v.Heraldry == new Heraldry(field, charge, d)), (HeraldryDivision)(village.Id % 6));
        return new(field, charge, division);
    }

    private void SetCastleSlot(int villageId, int structure, int index, BlueprintSlot slot)
    {
        var village = _villages[villageId];
        var castle = village.Castle.ToArray();
        castle[structure] = castle[structure] with { Slots = Array.AsReadOnly(castle[structure].Slots.Select((s, i) => i == index ? slot : s).ToArray()) };
        _villages[villageId] = village with { Castle = Array.AsReadOnly(castle) };
    }

    // The pieces a stacked slot's piece would stand on, when they are all built.
    private FurniturePiece[]? Supports(CastleStructure structure, int index)
    {
        var below = Castle.Support(structure.Part, index).Select(i => structure.Slots[i].FurnitureId).ToArray();
        return below.Any(id => id is null) ? null : below.Select(id => _furniture.Pieces.Single(p => p.Id == id)).ToArray();
    }

    /// <summary>Starts the next Castle piece once every wall ring is built: one slot at a time, a stacked slot after what carries it.</summary>
    private bool BuildCastle(Village village, Villager worker)
    {
        if (village.Castle.Any(c => c.Slots.Any(s => s.ReservedFurnitureId is not null))) return true;
        for (var c = 0; c < village.Castle.Count; c++)
        {
            var structure = village.Castle[c];
            var index = structure.Slots.ToList().FindIndex(s => s.FurnitureId is null);
            if (index < 0 || Supports(structure, index) is not { } supports) continue;
            var slot = structure.Slots[index];
            foreach (var piece in _villagers.FurnitureNear(village.Position, 48).Where(p =>
                Castle.Fits(structure.Part, index, p) && p.Elevation == 0 && p.Tilt == 0 && p.Roll == 0 && !IsBuiltFurniture(p.Id) &&
                _space.Distance(village.Position, p.Position) <= 48 && !_furnitureTasks.Any(t => t.FurnitureId == p.Id) &&
                // A stacked piece must not crush what carries it.
                supports.All(s => FurnitureResources.Weight(p) / supports.Length <= Stability.LoadCapacity(s) * (1 - s.Damage)))
                .OrderBy(p => InYard(village, p) ? 0 : 1).ThenBy(p => _space.Distance(worker.Position, p.Position)))
            {
                try
                {
                    StartFurnitureTask(piece.Id, [worker.Id], slot.Position, false, construction: true);
                    SetCastleSlot(village.Id, c, index, slot with { ReservedFurnitureId = piece.Id });
                    return true;
                }
                catch (InvalidOperationException) { /* Try the next reachable, haulable piece. */ }
            }
        }
        return false;
    }

    private bool CompleteCastle(FurnitureTask task, StoreClock clock)
    {
        foreach (var village in _villages.ToArray())
            for (var c = 0; c < village.Castle.Count; c++)
                for (var i = 0; i < village.Castle[c].Slots.Count; i++)
                {
                    var structure = village.Castle[c];
                    var slot = structure.Slots[i];
                    if (slot.ReservedFurnitureId != task.FurnitureId) continue;
                    // What it was to stand on fell meanwhile: the piece stays where it was put down.
                    if (Supports(structure, i) is not { } supports)
                    {
                        SetCastleSlot(village.Id, c, i, slot with { ReservedFurnitureId = null });
                        return true;
                    }
                    var piece = _furniture.Pieces.Single(p => p.Id == task.FurnitureId);
                    var elevation = supports.Length == 0 ? 0 : supports.Max(s => s.Elevation + FurnitureCatalog.Spec(s).Height);
                    var position = slot.Position;
                    // A Gatehouse flanker stands with its inner face at the passage's edge, whatever its width.
                    if (structure.Part == CastlePart.Gatehouse && i < 2)
                    {
                        var centre = structure.Slots[2].Position;
                        var outward = Vector2.Normalize(_space.Delta(centre, slot.Position));
                        position = _space.Wrap(centre + outward * (Castle.PassageHalfWidth + FurnitureCatalog.Spec(piece).Width / 2));
                    }
                    MoveFurnitureCore(task.FurnitureId, position, slot.Rotation, elevation);
                    SetCastleSlot(village.Id, c, i, slot with { Position = position, Elevation = elevation, FurnitureId = task.FurnitureId, ReservedFurnitureId = null });
                    foreach (var id in task.VillagerIds) _villagers.PracticeBuilding(id, false);
                    Emit(new CastlePiecePlaced(clock, village.Id, c, i, task.FurnitureId));
                    return true;
                }
        return false;
    }

    private void ClearCastleFurniture(int id, bool built)
    {
        foreach (var village in _villages.ToArray())
            for (var c = 0; c < village.Castle.Count; c++)
                for (var i = 0; i < village.Castle[c].Slots.Count; i++)
                {
                    var slot = _villages[village.Id].Castle[c].Slots[i];
                    if (built ? slot.FurnitureId == id : slot.ReservedFurnitureId == id)
                        SetCastleSlot(village.Id, c, i, built ? slot with { FurnitureId = null } : slot with { ReservedFurnitureId = null });
                }
    }

    private void ValidateCastles()
    {
        static bool Finite(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
        var wallIds = _villages.SelectMany(v => v.Walls).SelectMany(w => w.Slots).SelectMany(s => new[] { s.FurnitureId, s.ReservedFurnitureId }).OfType<int>();
        var ids = _villages.SelectMany(v => v.Castle).SelectMany(c => c.Slots).SelectMany(s => new[] { s.FurnitureId, s.ReservedFurnitureId }).OfType<int>().ToArray();
        if (ids.Distinct().Count() != ids.Length || ids.Intersect(wallIds).Any() || _villages.Any(v =>
                v.Heraldry is { } arms && (arms.Field is < 0 or >= FurniturePalettes.TintCount || arms.Charge is < 0 or >= FurniturePalettes.TintCount ||
                    !Enum.IsDefined(arms.Division)) ||
                v.Moats.Any(m => m.Ring < 0 || m.Ring >= v.Walls.Count || m.Runs.Count == 0 || m.Runs.Any(r => !Finite(r.Start) || !Finite(r.End))) ||
                v.Castle.Any(c => !Enum.IsDefined(c.Part) || c.Ring < 0 || c.Ring >= v.Walls.Count || !v.Walls[c.Ring].Battlements ||
                    c.Slots.Count != (c.Part == CastlePart.Keep ? 4 : 3) ||
                    c.Slots.Select((s, i) => (s, i)).Any(x => !Finite(x.s.Position) || !float.IsFinite(x.s.Rotation) || !float.IsFinite(x.s.Elevation) ||
                        x.s.FurnitureId is not null && x.s.ReservedFurnitureId is not null ||
                        x.s.FurnitureId is { } f && !_furniture.Pieces.Any(p => p.Id == f && Castle.Fits(c.Part, x.i, p) &&
                            p.Position == x.s.Position && p.Rotation == x.s.Rotation && p.Elevation == x.s.Elevation) ||
                        x.s.ReservedFurnitureId is { } r && (!_furniture.Pieces.Any(p => p.Id == r && Castle.Fits(c.Part, x.i, p)) ||
                            !_furnitureTasks.Any(t => t.FurnitureId == r && t.Work != FurnitureWork.Disassemble && t.VillagerIds.All(v.VillagerIds.Contains)))))))
            throw new InvalidDataException("Invalid world save: inconsistent Castle.");
    }
}
