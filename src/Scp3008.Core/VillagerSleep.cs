using System.Numerics;

namespace Scp3008.Core;

internal sealed partial class Villagers
{
    private sealed record SleepOption(SleepingPlace Place, Vector2 Position, int Quality);

    private IEnumerable<SleepOption> SleepingPlaces(Walker walker, WorldSnapshot world)
    {
        var village = world.Villages.FirstOrDefault(v => v.Id == walker.VillageId);
        if (village is not null)
            foreach (var d in village.Dwellings.Where(d => d.Complete && d.Admits(village, walker.Id)))
                for (var i = 0; i < (Dwelling.Sharing(village.Personality) == DwellingSharing.Personal ? 1 : d.SleepingPlaces); i++)
                    yield return new(new(d.Id, null, i), space.Wrap(d.Position + new Vector2(i == 0 ? -0.25f : 0.25f, d.Kind == DwellingKind.Tent ? 0.75f : 0.25f)),
                        d.Kind == DwellingKind.House ? 3 : 2);
        foreach (var p in FurnitureNear(walker.Position, 30).Where(p => p.Type is FurnitureType.Bed or FurnitureType.BunkBed or
            FurnitureType.Mattress or FurnitureType.SleepingBag or FurnitureType.Sofa or FurnitureType.Armchair && p.Damage < 1 && p.Elevation == 0))
            for (var i = 0; i < (p.Type == FurnitureType.BunkBed ? 2 : 1); i++)
                yield return new(new(null, p.Id, i), p.Position, 1);
    }

    private void SeekSleep(Walker walker, WorldSnapshot world)
    {
        var chosen = SleepingPlaces(walker, world)
            .Where(p => space.Distance(walker.Position, p.Position) <= 30 && !walker.Unreachable.Contains(p.Position) &&
                !_walkers.Any(w => w != walker && (p.Place.FurnitureId is not null || w.VillageId == walker.VillageId) && w.SleepingPlace == p.Place &&
                    (w.Action == VillagerAction.Resting || w.Target?.Rest == true || w.Needs.Rest < 0.4)))
            .OrderByDescending(p => p.Quality).ThenByDescending(p => p.Place == walker.SleepingPlace)
            .ThenBy(p => space.Distance(walker.Position, p.Position))
            .FirstOrDefault(p => TryPlan(walker, new(p.Position, Rest: true)));
        walker.SleepingPlace = chosen?.Place;
        walker.Target = chosen is null ? null : new(chosen.Position, Rest: true);
        walker.Action = chosen is null ? VillagerAction.Resting : VillagerAction.Idle;
        if (chosen is null)
        {
            walker.Waypoints.Clear();
            walker.Destination = null;
        }
    }

    public void ForgetSleepingFurniture(int id, IReadOnlyList<Village> villages)
    {
        foreach (var walker in _walkers)
            if (walker.SleepingPlace is { } place && (place.FurnitureId == id || place.DwellingId is { } dwelling &&
                villages.FirstOrDefault(v => v.Id == walker.VillageId)?.Dwellings.FirstOrDefault(d => d.Id == dwelling)?.Complete != true))
            {
                walker.SleepingPlace = null;
                if (walker.Target?.Rest == true) { walker.Target = null; walker.Waypoints.Clear(); walker.Destination = null; }
            }
    }

    public void ValidateSleepingPlaces(IReadOnlyList<Village> villages, IReadOnlyList<FurniturePiece> furniture)
    {
        foreach (var walker in _walkers)
        {
            if (walker.SleepingPlace is not { } p) continue;
            if ((p.DwellingId is null) == (p.FurnitureId is null) || p.Index is < 0 or > 1 ||
                p.DwellingId is { } d && !villages.Any(v => v.Id == walker.VillageId && v.Dwellings.Any(h => h.Id == d && h.Complete)) ||
                p.FurnitureId is { } f && !furniture.Any(piece => piece.Id == f && (p.Index == 0 || piece.Type == FurnitureType.BunkBed) &&
                    piece.Type is FurnitureType.Bed or FurnitureType.BunkBed or FurnitureType.Mattress or FurnitureType.SleepingBag or FurnitureType.Sofa or FurnitureType.Armchair))
                throw new InvalidDataException("Invalid world save: inconsistent sleeping place.");
        }
    }

    private int SleepQuality(Walker walker, WorldSnapshot world)
    {
        if (walker.Action != VillagerAction.Resting || walker.SleepingPlace is null) return 0;
        var place = SleepingPlaces(walker, world).FirstOrDefault(p => p.Place == walker.SleepingPlace);
        return place is not null && space.Distance(walker.Position, navigation.NearestWalkable(place.Position)) < 0.2f ? place.Quality : 0;
    }
}
