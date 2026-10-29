using System.Numerics;
using System.Text.Json.Serialization;

namespace Scp3008.Core;

public enum DwellingKind { Tent, House }
public enum DwellingSharing { Communal, Personal, Family }

/// <summary>A frame with fabric, or a Furniture house, built one Blueprint slot at a time.</summary>
public sealed record Dwelling(int Id, DwellingKind Kind, Vector2 Position, IReadOnlyList<BlueprintSlot> Slots)
{
    public double SetupHours { get; init; }
    [JsonIgnore] public double RequiredSetupHours => Kind == DwellingKind.Tent ? 0.1 : 2;
    [JsonIgnore] public bool Complete => Slots.All(s => s.FurnitureId is not null) && SetupHours + 1e-9 >= RequiredSetupHours;
    [JsonIgnore] public int SleepingPlaces => Complete ? 2 : 0;
    public static DwellingSharing Sharing(Personality personality) => personality switch
    {
        Personality.Bold => DwellingSharing.Personal,
        Personality.Builders => DwellingSharing.Family,
        _ => DwellingSharing.Communal,
    };

    public bool Admits(Village village, int villagerId)
    {
        var index = village.VillagerIds.ToList().IndexOf(villagerId);
        if (index < 0) return false;
        var sharing = Sharing(village.Personality);
        if (sharing == DwellingSharing.Communal) return true;
        var size = sharing == DwellingSharing.Personal ? 1 : 2;
        // shortcut: families are stable pairs in membership order; use relationship ids when family relationships exist.
        return index / size == Id % Math.Max(1, (village.VillagerIds.Count + size - 1) / size);
    }

    internal static IReadOnlyList<BlueprintSlot> Layout(DwellingKind kind, Vector2 centre, StoreSpace space)
    {
        BlueprintSlot At(float x, float y, float rotation = 0) => new(space.Wrap(centre + new Vector2(x, y)), rotation, 0.6f, WallRun.Straight);
        return kind == DwellingKind.Tent ? [At(0, 0), At(0, 0)] :
            [At(-1.2f, 0, MathF.PI / 2), At(1.2f, 0, MathF.PI / 2), At(0, -1.1f), At(1, 1.1f), At(0, -0.45f), At(0, 0.45f)];
    }

    internal static bool Fits(DwellingKind kind, int index, FurniturePiece p)
    {
        var s = FurnitureCatalog.Spec(p);
        if (s.IsFixed || p.Tilt != 0 || p.Roll != 0 || p.Damage >= 1) return false;
        if (kind == DwellingKind.Tent)
            return index == 0 ? p.Type == FurnitureType.CurtainRack : p.Type == FurnitureType.Rug;
        return index switch
        {
            < 3 => !s.IsFloorCovering && !s.IsLamp && s.Width is >= 0.8f and <= 1.8f && s.Depth <= 0.7f &&
                s.Height is >= 1.8f and <= 2.2f && FurnitureResources.Weight(p) >= 10 &&
                FurnitureResources.Yield(p).GetValueOrDefault(MaterialKind.WoodPanel) > 0,
            3 => p.Type == FurnitureType.Wardrobe,
            _ => p.Type is FurnitureType.DiningTable or FurnitureType.MeetingTable or FurnitureType.Desk &&
                s.Width >= 1.4f && s.Depth >= 0.7f,
        };
    }
}

/// <summary>A usual sleeping place; a Dwelling slot or one piece of bedding in the open.</summary>
public sealed record SleepingPlace(int? DwellingId, int? FurnitureId, int Index);

public sealed partial class ScenarioBuilder
{
    public Dwelling PlanDwelling(int villageId, DwellingKind kind) => _simulation.PlanDwelling(villageId, kind);
    public void SetPersonality(int villageId, Personality personality) => _simulation.SetPersonality(villageId, personality);
}

public sealed partial class Simulation
{
    internal void SetPersonality(int villageId, Personality personality) => Publish(() =>
    {
        if (!Enum.IsDefined(personality)) throw new ArgumentOutOfRangeException(nameof(personality));
        _villages[villageId] = _villages[villageId] with { Personality = personality };
        return true;
    });

    internal Dwelling PlanDwelling(int villageId, DwellingKind kind) => Publish(() =>
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var village = _villages.Single(v => v.Id == villageId);
        var id = village.Dwellings.Count;
        var centre = Vector2.Zero;
        Blueprint? expansion = null;
        var found = false;
        var sitesPerRing = village.HomeWalls().LastOrDefault()?.Outline == WallOutline.Freeform ? 6 : 5;
        // The eastern site leaves extra room for the Furniture yard; later rings clear the old walls.
        for (var site = 0; 6 + site / sitesPerRing * 8 < _space.Size / 2 - 3; site++)
        {
            var angle = ((site % sitesPerRing) switch { 0 => 3f, 1 => 4f, 2 => 5f, 3 => 1f, 4 => 2f, _ => 0f }) * MathF.PI / 3;
            var radius = (site % sitesPerRing == 5 ? 10 : 6) + site / sitesPerRing * 8;
            var at = _space.Wrap(village.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
            if (InPillar(at, 2) || village.Dwellings.Any(d => _space.Distance(d.Position, at) < 4) ||
                village.Walls.SelectMany(w => w.Slots).Any(s => _space.Distance(s.Position, at) < 2.5f)) continue;
            var wall = village.HomeWalls().LastOrDefault();
            if (wall is null || !InsideDwellingWall(village.Position, at, wall))
            {
                var planned = new Dwelling(id, kind, at, []);
                var expanded = CreateBarricade(village with { Dwellings = village.Dwellings.Append(planned).ToArray() });
                if (!InsideDwellingWall(village.Position, at, expanded)) continue;
                expansion = expanded;
            }
            centre = at; found = true; break;
        }
        if (!found) throw new InvalidOperationException("No clear site for a Dwelling inside an expanded wall.");
        var dwelling = new Dwelling(id, kind, centre, Dwelling.Layout(kind, centre, _space));
        village = village with { Dwellings = Array.AsReadOnly(village.Dwellings.Append(dwelling).ToArray()) };
        // Keep the old ring while Builders assemble the larger one.
        if (expansion is { } outer)
        {
            village = village with { Walls = Array.AsReadOnly(village.Walls.Append(outer).ToArray()),
                Heraldry = village.Heraldry ?? ChooseHeraldry(village) };
            _villages[villageId] = village;
            if (outer.Battlements) PlanCastle(villageId, village.Walls.Count - 1);
        }
        else _villages[villageId] = village;
        return dwelling;
    });

    private bool InsideDwellingWall(Vector2 village, Vector2 position, Blueprint wall)
    {
        var point = _space.Delta(village, position);
        for (var i = 0; i < wall.Vertices.Count - 1; i++)
        {
            var start = _space.Delta(village, wall.Vertices[i]);
            var edge = _space.Delta(wall.Vertices[i], wall.Vertices[i + 1]);
            var offset = point - start;
            if (edge.X * offset.Y - edge.Y * offset.X < 2.5f * edge.Length()) return false;
        }
        return true;
    }

    private void SetDwellingSlot(int villageId, int dwellingId, int index, BlueprintSlot slot)
    {
        var village = _villages[villageId];
        var dwellings = village.Dwellings.ToArray();
        var dwelling = dwellings[dwellingId];
        dwellings[dwellingId] = dwelling with { Slots = Array.AsReadOnly(dwelling.Slots.Select((s, i) => i == index ? slot : s).ToArray()) };
        _villages[villageId] = village with { Dwellings = Array.AsReadOnly(dwellings) };
    }

    private bool BuildDwellings(Village village, Villager worker)
    {
        if (village.Dwellings.Count == 0 || village.Dwellings.All(d => d.Complete) &&
            village.Dwellings.Sum(d => Dwelling.Sharing(village.Personality) == DwellingSharing.Personal ? 1 : 2) < village.VillagerIds.Count)
        {
            var kind = village.Personality == Personality.Builders ? DwellingKind.House : DwellingKind.Tent;
            // Do not take wall supplies for housing that cannot be finished with the local Furniture.
            var available = _villagers.FurnitureNear(village.Position, 48).Where(p => p.Elevation == 0 && !IsBuiltFurniture(p.Id) &&
                _space.Distance(village.Position, p.Position) <= 48 && !_furnitureTasks.Any(t => t.FurnitureId == p.Id))
                .OrderBy(p => p.Type == FurnitureType.Wardrobe ? 1 : 0).ToList();
            for (var i = 0; i < (kind == DwellingKind.Tent ? 2 : 6); i++)
            {
                var match = available.FindIndex(p => Dwelling.Fits(kind, i, p));
                if (match < 0) return false;
                available.RemoveAt(match);
            }
            try { PlanDwelling(village.Id, kind); }
            catch (InvalidOperationException) { return false; }
            village = _villages[village.Id];
        }
        foreach (var dwelling in village.Dwellings)
        {
            if (dwelling.Slots.Any(s => s.ReservedFurnitureId is not null)) return true;
            var index = dwelling.Slots.ToList().FindIndex(s => s.FurnitureId is null);
            if (index < 0)
            {
                if (dwelling.Complete) continue;
                if (_space.Distance(worker.Position, dwelling.Position) > 3)
                {
                    _villagers.Send(worker.Id, _navigation.NearestWalkable(dwelling.Position));
                    return true;
                }
                var dwellings = village.Dwellings.ToArray();
                dwellings[dwelling.Id] = dwelling with { SetupHours = Math.Min(dwelling.RequiredSetupHours, dwelling.SetupHours + 0.1) };
                _villages[village.Id] = village with { Dwellings = Array.AsReadOnly(dwellings) };
                _villagers.PracticeBuilding(worker.Id, false);
                return true;
            }
            var slot = dwelling.Slots[index];
            foreach (var piece in _villagers.FurnitureNear(village.Position, 48).Where(p =>
                Dwelling.Fits(dwelling.Kind, index, p) && p.Elevation == 0 && !IsBuiltFurniture(p.Id) &&
                _space.Distance(village.Position, p.Position) <= 48 && !_furnitureTasks.Any(t => t.FurnitureId == p.Id))
                .OrderBy(p => index < 3 && dwelling.Kind == DwellingKind.House && p.Type == FurnitureType.Wardrobe ? 1 : 0)
                .ThenBy(p => _space.Distance(worker.Position, p.Position)))
            {
                try
                {
                    StartFurnitureTask(piece.Id, [worker.Id], slot.Position, false, construction: true);
                    SetDwellingSlot(village.Id, dwelling.Id, index, slot with { ReservedFurnitureId = piece.Id });
                    return true;
                }
                catch (InvalidOperationException) { /* Try the next reachable, haulable piece. */ }
            }
        }
        return false;
    }

    private bool CompleteDwelling(FurnitureTask task)
    {
        foreach (var village in _villages.ToArray())
            foreach (var dwelling in village.Dwellings)
                for (var i = 0; i < dwelling.Slots.Count; i++)
                {
                    var slot = dwelling.Slots[i];
                    if (slot.ReservedFurnitureId != task.FurnitureId) continue;
                    var roof = dwelling.Kind == DwellingKind.Tent ? i == 1 : i >= 4;
                    if (roof && dwelling.Slots.Take(dwelling.Kind == DwellingKind.Tent ? 1 : 4).Any(s => s.FurnitureId is null))
                    {
                        SetDwellingSlot(village.Id, dwelling.Id, i, slot with { ReservedFurnitureId = null });
                        return true;
                    }
                    var supportIndex = dwelling.Kind == DwellingKind.Tent ? 0 : i == 4 ? 2 : 3;
                    var support = roof ? new[] { _furniture.Pieces.Single(p => p.Id == dwelling.Slots[supportIndex].FurnitureId) } : [];
                    var elevation = roof ? support.Max(p => p.Elevation + FurnitureCatalog.Spec(p).Height) : 0;
                    MoveFurnitureCore(task.FurnitureId, slot.Position, slot.Rotation, elevation);
                    SetDwellingSlot(village.Id, dwelling.Id, i, slot with { FurnitureId = task.FurnitureId,
                        ReservedFurnitureId = null, Elevation = elevation, Tier = TierOf(_furniture.Pieces.Single(p => p.Id == task.FurnitureId), false, false) });
                    foreach (var id in task.VillagerIds) _villagers.PracticeBuilding(id, false);
                    return true;
                }
        return false;
    }

    private void ClearDwellingFurniture(int id, bool built)
    {
        foreach (var village in _villages.ToArray())
            foreach (var dwelling in village.Dwellings)
                for (var i = 0; i < dwelling.Slots.Count; i++)
                {
                    var slot = _villages[village.Id].Dwellings[dwelling.Id].Slots[i];
                    if (built ? slot.FurnitureId == id : slot.ReservedFurnitureId == id)
                        SetDwellingSlot(village.Id, dwelling.Id, i, built ? slot with { FurnitureId = null } : slot with { ReservedFurnitureId = null });
                }
    }

    private bool TendDwellings(Village village)
    {
        foreach (var d in village.Dwellings.Where(d => d.Kind == DwellingKind.House && d.Complete))
            foreach (var (slot, index) in d.Slots.Select((s, i) => (s, i)).OrderBy(x => x.s.Tier))
            {
                if (slot.Tier >= 3) continue;
                var metal = !slot.Strapped && village.Materials.GetValueOrDefault(MaterialKind.Metal) >= StrapMetal;
                var foam = !slot.Backfilled && village.Materials.GetValueOrDefault(MaterialKind.Foam) >= BackfillFoam;
                if (!metal && !foam) continue;
                SetMaterialsCore(village.Id, metal ? MaterialKind.Metal : MaterialKind.Foam,
                    village.Materials[metal ? MaterialKind.Metal : MaterialKind.Foam] - (metal ? StrapMetal : BackfillFoam));
                var piece = _furniture.Pieces.Single(p => p.Id == slot.FurnitureId);
                var upgraded = slot with { Strapped = slot.Strapped || metal, Backfilled = slot.Backfilled || !metal };
                SetDwellingSlot(village.Id, d.Id, index, upgraded with { Tier = TierOf(piece, upgraded.Strapped, upgraded.Backfilled) });
                return true;
            }
        return false;
    }

    private void ValidateDwellings()
    {
        foreach (var village in _villages)
            for (var d = 0; d < village.Dwellings.Count; d++)
            {
                var dwelling = village.Dwellings[d];
                if (dwelling.Id != d || !Enum.IsDefined(dwelling.Kind) || !float.IsFinite(dwelling.Position.X) || !float.IsFinite(dwelling.Position.Y) ||
                    dwelling.Slots.Count != (dwelling.Kind == DwellingKind.Tent ? 2 : 6) ||
                    !double.IsFinite(dwelling.SetupHours) || dwelling.SetupHours < 0 || dwelling.SetupHours > dwelling.RequiredSetupHours)
                    throw new InvalidDataException("Invalid world save: inconsistent Dwelling.");
                var layout = Dwelling.Layout(dwelling.Kind, dwelling.Position, _space);
                for (var i = 0; i < dwelling.Slots.Count; i++)
                {
                    var s = dwelling.Slots[i];
                    if (s.Position != layout[i].Position || s.Rotation != layout[i].Rotation || !float.IsFinite(s.Elevation) || s.Elevation < 0 ||
                        s.Tier is < 1 or > 3 || s.FurnitureId is not null && s.ReservedFurnitureId is not null ||
                        s.FurnitureId is { } f && !_furniture.Pieces.Any(p => p.Id == f && Dwelling.Fits(dwelling.Kind, i, p) &&
                            p.Position == s.Position && p.Rotation == s.Rotation && p.Elevation == s.Elevation) ||
                        s.ReservedFurnitureId is { } r && (!_furniture.Pieces.Any(p => p.Id == r && Dwelling.Fits(dwelling.Kind, i, p)) ||
                            !_furnitureTasks.Any(t => t.FurnitureId == r && t.VillagerIds.All(village.VillagerIds.Contains))))
                        throw new InvalidDataException("Invalid world save: inconsistent Dwelling Blueprint.");
                }
            }
    }
}
