using System.Numerics;

namespace Scp3008.Core;

public sealed partial class Simulation
{
    private bool IsBuiltFurniture(int id) => IsHomeFridge(id) || IsSlotFurniture(id);

    private bool IsSlotFurniture(int id) => _villages.Any(v => v.PillarScaffold?.Slots.Any(s => s.FurnitureId == id) == true ||
        v.Dwellings.Any(d => d.Slots.Any(s => s.FurnitureId == id)) || v.MessHall?.Slots.Any(s => s.FurnitureId == id) == true || v.Storehouse?.Slots.Any(s => s.FurnitureId == id) == true || v.Workshops.Any(w => w.Slot.FurnitureId == id) || v.Walls.Any(w =>
        w.IncorporatedFurnitureId == id || w.Slots.Any(s => s.FurnitureId == id)) || v.Castle.Any(c => c.Slots.Any(s => s.FurnitureId == id)));

    internal Blueprint PlanBarricade(int villageId) => Publish(() =>
    {
        var village = _villages.Single(v => v.Id == villageId);
        if (village.HomeWalls().LastOrDefault() is { Complete: false } pending) return pending;
        var wall = CreateBarricade(village);
        _villages[villageId] = village with { Walls = Array.AsReadOnly(village.Walls.Append(wall).ToArray()),
            Heraldry = village.Heraldry ?? ChooseHeraldry(village) };
        if (wall.Battlements) PlanCastle(villageId, village.Walls.Count);
        return wall;
    });

    // A Furniture yard barricade is a small round ring around the yard, with no pillar or shelf anchor.
    private Blueprint CreateBarricade(Village village, Vector2? yard = null)
    {
        var outline = yard is not null ? WallOutline.Round : village.HomeWalls().LastOrDefault()?.Outline ??
            (village.Personality == Personality.Builders ? WallOutline.Square : WallOutline.Round);
        var radius = yard is not null ? 2.6f : 4f + village.HomeWalls().Count() * 3 + MathF.Sqrt(village.VillagerIds.Count);
        if (yard is null && village.Dwellings.Count > 0)
            radius = MathF.Max(radius, village.Dwellings.Max(d => _space.Distance(village.Position, d.Position)) + 3.5f);
        var centre = yard ?? village.Position;
        var vertices = outline == WallOutline.Round
            ? Enumerable.Range(0, 12).Select(i => centre + new Vector2(
                MathF.Cos(i * MathF.Tau / 12), MathF.Sin(i * MathF.Tau / 12)) * radius).ToArray()
            : new[] { village.Position + new Vector2(-radius, -radius), village.Position + new Vector2(radius, -radius),
                village.Position + new Vector2(radius, radius), village.Position + new Vector2(-radius, radius) };
        int? furnitureId = null, pillarId = null;
        // Keep an existing anchor when extending a freeform ring.
        var previous = yard is null ? village.HomeWalls().LastOrDefault() : null;
        var shelf = yard is not null ? null : _furniture.Pieces.Where(p => p.Type is FurnitureType.ShelfUnit or FurnitureType.Bookcase or FurnitureType.PalletRack &&
            p.Elevation == 0 && p.Tilt == 0 && p.Roll == 0 && !_furnitureTasks.Any(t => t.FurnitureId == p.Id) &&
            (!IsBuiltFurniture(p.Id) || previous?.IncorporatedFurnitureId == p.Id) && _space.Distance(village.Position, p.Position) < 12)
            .OrderBy(p => _space.Distance(village.Position, p.Position)).Select(p => (FurniturePiece?)p).FirstOrDefault();
        Vector2 anchor = default, along = default;
        float width = 0;
        foreach (var pillar in _pillars.Where(_ => yard is null).OrderBy(p => _space.Distance(village.Position, p.Position)))
        {
            var d = _space.Delta(pillar.Position, village.Position);
            var normal = MathF.Abs(d.X) > MathF.Abs(d.Y) ? new Vector2(MathF.Sign(d.X), 0) : new Vector2(0, MathF.Sign(d.Y));
            var face = village.Position - d + normal * pillar.Width / 2;
            var faceAlong = new Vector2(-normal.Y, normal.X);
            if (Vector2.Dot(village.Position - face, normal) is > 1 and < 12 &&
                MathF.Abs(Vector2.Dot(village.Position - face, faceAlong)) < pillar.Width / 2 - 1)
            { anchor = face; along = faceAlong; width = pillar.Width; pillarId = pillar.Id; break; }
        }
        if (pillarId is null && shelf is { } existing)
        {
            anchor = village.Position + _space.Delta(village.Position, existing.Position);
            along = new(MathF.Cos(existing.Rotation), MathF.Sin(existing.Rotation));
            width = FurnitureCatalog.Spec(existing).Width;
            // A shelving row only anchors a site beside its broad face, not beyond its end.
            if (MathF.Abs(Vector2.Dot(village.Position - anchor, along)) < width / 2)
                furnitureId = existing.Id;
        }
        if (previous is not null && previous.Outline != WallOutline.Freeform)
        { furnitureId = null; pillarId = null; }
        if (furnitureId is not null || pillarId is not null)
        {
            outline = WallOutline.Freeform;
            var normal = new Vector2(-along.Y, along.X);
            if (Vector2.Dot(village.Position - anchor, normal) < 0) { along = -along; normal = -normal; }
            var depth = Vector2.Dot(village.Position - anchor, normal) + radius;
            vertices = [anchor - along * width / 2, anchor + along * width / 2,
                anchor + along * (width / 2 + radius) + normal * depth,
                anchor - along * (width / 2 + radius) + normal * depth];
        }
        var slots = new List<BlueprintSlot>();
        var gateEdge = outline == WallOutline.Freeform ? 2 : 0;
        var gateCentre = (vertices[gateEdge] + vertices[gateEdge + 1]) / 2;
        var tangent = Vector2.Normalize(vertices[gateEdge + 1] - vertices[gateEdge]);
        // Every home ring after the first is a curtain wall: Battlements, and a gate wide enough for a Gatehouse.
        var curtain = yard is null && village.HomeWalls().Any();
        var gateStart = gateCentre - tangent * (curtain ? Castle.GateHalfWidth : 1);
        var gateEnd = gateCentre + tangent * (curtain ? Castle.GateHalfWidth : 1);
        void Run(Vector2 start, Vector2 end)
        {
            var delta = end - start;
            var count = (int)MathF.Ceiling(delta.Length() / 0.6f);
            if (count == 0) return;
            for (var i = 0; i < count; i++)
            {
                var slot = new BlueprintSlot(_space.Wrap(start + delta * ((i + 0.5f) / count)), MathF.Atan2(delta.Y, delta.X),
                    delta.Length() / count, outline == WallOutline.Round ? WallRun.Curved : i == 0 ? WallRun.Corner : WallRun.Straight);
                // Battlements alternate tall units (wardrobes, bookcases) with short ones.
                slots.Add(!curtain ? slot : slots.Count % 2 == 0 ? slot with { MinimumHeight = 1.8f } : slot with { MaximumHeight = 1.2f });
            }
        }
        for (var i = 0; i < vertices.Length; i++)
        {
            if (i == 0 && outline == WallOutline.Freeform) continue;
            if (i == gateEdge) { Run(vertices[i], gateStart); Run(gateEnd, vertices[i + 1]); }
            else Run(vertices[i], vertices[(i + 1) % vertices.Length]);
        }
        return new(outline, village.VillagerIds.Count, Array.AsReadOnly(vertices.Append(vertices[0]).Select(_space.Wrap).ToArray()),
            _space.Wrap(gateStart), _space.Wrap(gateEnd), slots.AsReadOnly(), furnitureId, pillarId, curtain);
    }

    private void SetWallSlot(int villageId, int ring, int slot, BlueprintSlot replacement)
    {
        var village = _villages[villageId];
        var walls = village.Walls.ToArray();
        walls[ring] = walls[ring] with { Slots = Array.AsReadOnly(walls[ring].Slots.Select((s, i) => i == slot ? replacement : s).ToArray()) };
        _villages[villageId] = village with { Walls = Array.AsReadOnly(walls) };
    }

    private void AdvanceConstruction(StoreClock clock)
    {
        if (_tick % 50 != 1 || clock.Phase == DayPhase.ClosingTime) return;
        foreach (var village in _villages.ToArray())
        {
            var workers = _villagers.Snapshot().Where(v => v.VillageId == village.Id && v.Role == Role.Builder &&
                IsAvailableWorker(v) && v.CarriedAmount + v.CarriedMaterials == 0 &&
                v.Hand?.Kind is null or GearKind.ShoppingCart or GearKind.PlatformCart && v.Needs.Hunger < 0.7 && v.Needs.Thirst < 0.7).ToArray();
            if (workers.Length == 0) continue;
            if (GatherFridge(village, workers)) continue;
            if (BuildPillarScaffold(village, workers[0], clock)) continue;
            if (BuildDwellings(village, workers[0])) continue;
            if (TendDwellings(_villages[village.Id])) continue;
            if (BuildWorkshop(_villages[village.Id], workers[0])) continue;
            if (BuildMessHall(_villages[village.Id], workers[0])) continue;
            if (BuildStorehouse(_villages[village.Id], workers[0])) continue;
            if (TendWalls(_villages[village.Id], workers[0], clock)) continue;
            var homeWalls = village.HomeWalls().ToArray();
            if (homeWalls.Length == 0 || homeWalls.All(w => w.Complete) &&
                village.VillagerIds.Count >= homeWalls[^1].Population + 4)
                PlanBarricade(village.Id);
            PlanFurnitureYard(village.Id);
            var current = _villages[village.Id];
            var ring = current.Outposts.Where(o => !o.Abandoned && !current.Walls[o.WallIndex].Complete)
                .Select(o => o.WallIndex).DefaultIfEmpty(-1).First();
            if (ring < 0) ring = current.Walls.ToList().FindIndex(w => !w.Complete &&
                !current.Outposts.Any(o => o.Abandoned && current.Walls[o.WallIndex] == w));
            if (ring < 0)
            {
                if (BuildCastle(current, workers[0])) continue;
                if (current.PillarScaffold is null && current.Castle.Count > 0 && current.Castle.All(c => c.Complete))
                {
                    var pillar = _pillars.OrderBy(p => _space.Distance(current.Position, p.Position)).FirstOrDefault(p =>
                        _space.Distance(current.Position, p.Position) <= 80 && !_villages.Any(v => v.PillarScaffold?.PillarId == p.Id));
                    if (pillar is not null) PlanScaffold(current.Id, pillar.Id);
                }
                GatherToYard(_villages[village.Id], workers[0]); continue;
            }
            var wall = current.Walls[ring];
            if (wall.Slots.Any(s => s.ReservedFurnitureId is not null)) continue;
            var index = wall.Slots.ToList().FindIndex(s => s.FurnitureId is null);
            if (index < 0) { GatherToYard(_villages[village.Id], workers[0]); continue; }
            var slot = wall.Slots[index];
            var site = current.Outposts.FirstOrDefault(o => o.WallIndex == ring)?.Position ??
                (current.FurnitureYard?.WallIndex == ring ? current.FurnitureYard.Position : village.Position);
            var started = false;
            foreach (var piece in _villagers.FurnitureNear(site, 48).Where(p =>
                slot.Fits(p) && p.Damage < 1f && p.Elevation == 0 && p.Tilt == 0 && p.Roll == 0 && !IsBuiltFurniture(p.Id) &&
                _space.Distance(site, p.Position) <= 48 &&
                !_furnitureTasks.Any(t => t.FurnitureId == p.Id)).OrderBy(p => InYard(current, p) ? 0 : 1).ThenBy(p => _space.Distance(workers[0].Position, p.Position)))
            {
                try
                {
                    StartFurnitureTask(piece.Id, [workers[0].Id], slot.Position, false, construction: true);
                    SetWallSlot(village.Id, ring, index, slot with { ReservedFurnitureId = piece.Id });
                    started = true;
                    break;
                }
                catch (InvalidOperationException) { /* Try the next reachable, haulable piece. */ }
            }
            if (!started) GatherToYard(_villages[village.Id], workers[0]);
        }
    }

    private void CompleteBlueprintPiece(FurnitureTask task, StoreClock clock)
    {
        if (CompleteDwelling(task) || CompleteMessHall(task) || CompleteStorehouse(task) || CompleteWorkshop(task) || CompleteCastle(task, clock)) return;
        foreach (var village in _villages.ToArray())
            for (var r = 0; r < village.Walls.Count; r++)
                for (var s = 0; s < village.Walls[r].Slots.Count; s++)
                {
                    var slot = village.Walls[r].Slots[s];
                    if (slot.ReservedFurnitureId != task.FurnitureId) continue;
                    MoveFurnitureCore(task.FurnitureId, slot.Position, slot.Rotation, 0);
                    var swap = slot.FurnitureId is not null;
                    var placed = swap ? SwapIn(village, slot, task.FurnitureId) : slot with { FurnitureId = task.FurnitureId, ReservedFurnitureId = null,
                        Tier = TierOf(Piece(task.FurnitureId), false, false) };
                    SetWallSlot(village.Id, r, s, placed);
                    if (swap) Emit(new WallPieceUpgraded(clock, village.Id, r, s, WallUpgrade.Swapped, placed.Tier));
                    foreach (var id in task.VillagerIds) _villagers.PracticeBuilding(id, false);
                    Emit(new BlueprintPiecePlaced(clock, village.Id, r, s, task.FurnitureId));
                    return;
                }
    }

    private void ReleaseBlueprintReservation(int furnitureId)
    {
        ClearDwellingFurniture(furnitureId, false);
        ClearMessHallFurniture(furnitureId, false);
        ClearStorehouseFurniture(furnitureId, false);
        ClearWorkshopFurniture(furnitureId, false);
        ClearCastleFurniture(furnitureId, false);
        foreach (var village in _villages.ToArray())
            for (var r = 0; r < village.Walls.Count; r++)
                for (var s = 0; s < village.Walls[r].Slots.Count; s++)
                    if (village.Walls[r].Slots[s].ReservedFurnitureId == furnitureId)
                        SetWallSlot(village.Id, r, s, village.Walls[r].Slots[s] with { ReservedFurnitureId = null });
    }

    private void UnbuildFurniture(int id)
    {
        ClearDwellingFurniture(id, true);
        _villagers.ForgetSleepingFurniture(id, _villages);
        ClearMessHallFurniture(id, true);
        ClearStorehouseFurniture(id, true);
        ClearWorkshopFurniture(id, true);
        ClearCastleFurniture(id, true);
        ClearScaffoldFurniture(id);
        foreach (var village in _villages.ToArray())
        {
            var walls = village.Walls.Select(w =>
            {
                var slots = w.Slots.Select(s => s.FurnitureId == id ? s with { FurnitureId = null } : s).ToList();
                if (w.IncorporatedFurnitureId == id)
                {
                    var delta = _space.Delta(w.Vertices[0], w.Vertices[1]);
                    var count = (int)MathF.Ceiling(delta.Length() / 0.6f);
                    for (var i = 0; i < count; i++)
                        slots.Add(new(_space.Wrap(w.Vertices[0] + delta * ((i + 0.5f) / count)),
                            MathF.Atan2(delta.Y, delta.X), delta.Length() / count, WallRun.Straight));
                }
                return w with { Slots = slots.AsReadOnly(), IncorporatedFurnitureId = w.IncorporatedFurnitureId == id ? null : w.IncorporatedFurnitureId };
            }).ToArray();
            _villages[village.Id] = village with { Walls = Array.AsReadOnly(walls) };
        }
    }

    private void ValidateBlueprints()
    {
        static bool Finite(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
        var slots = _villages.SelectMany(v => v.Walls).SelectMany(w => w.Slots).ToArray();
        var ids = slots.SelectMany(s => new[] { s.FurnitureId, s.ReservedFurnitureId }).OfType<int>().ToArray();
        if (ids.Distinct().Count() != ids.Length || _bands.Any(b => !Enum.IsDefined(b.Personality)) ||
            _villages.Any(v => !Enum.IsDefined(v.Personality) || v.Walls.Any(w => !Enum.IsDefined(w.Outline) ||
                w.Population <= 0 || w.Vertices.Count < 4 || w.Vertices[0] != w.Vertices[^1] ||
                w.Vertices.Any(p => !Finite(p)) || !Finite(w.GateStart) || !Finite(w.GateEnd) || w.Slots.Count == 0 ||
                w.IncorporatedFurnitureId is { } f && !HasPiece(f) ||
                w.IncorporatedPillarId is { } pillar && !_pillars.Any(p => p.Id == pillar) ||
                w.Slots.Any(s => !Finite(s.Position) || !float.IsFinite(s.Rotation) || !Enum.IsDefined(s.Run) ||
                    !float.IsFinite(s.Width) || s.Width <= 0 || !float.IsFinite(s.MinimumHeight) || s.MinimumHeight <= 0 ||
                    !float.IsFinite(s.MinimumWeight) || s.MinimumWeight <= 0 ||
                    s.Tier is < 1 or > 3 ||
                    s.FurnitureId is { } id && !HasPiece(id, p => s.Fits(p) &&
                        p.Position == s.Position && p.Rotation == s.Rotation && p.Elevation == 0) ||
                    s.ReservedFurnitureId is { } reserved && (!HasPiece(reserved, p => s.Fits(p)) ||
                        !_furnitureTasks.Any(t => t.FurnitureId == reserved && t.Work != FurnitureWork.Disassemble &&
                            t.VillagerIds.All(id => v.VillagerIds.Contains(id))))))))
            throw new InvalidDataException("Invalid world save: inconsistent Blueprints.");
    }
}
