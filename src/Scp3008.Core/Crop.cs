using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// The growth of a Crop (ticket 17f), a <see cref="FurnitureType.Crop"/> piece of the Plant section. A Crop no Village has tended
/// stays as the Store keeps it; once a farmer waters it, it grows while it has water and withers for good after
/// <see cref="WitherDays"/> dry Store days. A ripe Crop yields <see cref="Yield"/> fresh food when a farmer harvests it.
/// </summary>
/// <param name="FurnitureId">The Crop's piece; it keeps its growth wherever it is carried.</param>
/// <param name="Growth">From 0, just harvested, to 1, ripe.</param>
/// <param name="Water">From 0, dry, to 1, just watered.</param>
/// <param name="DryDays">Store days a tended Crop has gone without water.</param>
public sealed record Crop(int FurnitureId, float Growth = 0, float Water = 0, float DryDays = 0, bool Tended = false, bool Withered = false)
{
    /// <summary>A Village tends the Crops this close to its centre.</summary>
    public const float TendingRange = 24;
    /// <summary>A farmer carries home Crops from this far, until the Village tends <see cref="Kept"/>.</summary>
    public const float GatherRange = 48;
    public const int Kept = 4;
    public const float GrowthPerDay = 0.5f, WaterPerDay = 1, WitherDays = 2;
    /// <summary>A farmer waters a Crop drier than this.</summary>
    public const float Thirsty = 0.5f;
    public const int Yield = 2;
    /// <summary>Waterings a farmer carries from one visit to a Water point.</summary>
    public const int WaterCarried = 4;

    public bool Ripe => !Withered && Growth >= 1;
}

public sealed record CropHarvested(StoreClock At, int VillageId, int VillagerId, int FurnitureId, int Amount) : DomainEvent(At);

public sealed record CropWithered(StoreClock At, int FurnitureId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    /// <summary>Waters a Crop as a farmer would, which makes it tended.</summary>
    public void WaterCrop(int furnitureId) => _simulation.WaterCrop(furnitureId);
}

public sealed partial class Simulation
{
    private readonly SortedDictionary<int, Crop> _crops = [];
    private const long CropTicks = 50;

    private void AddCrops(IEnumerable<FurniturePiece> pieces)
    {
        foreach (var piece in pieces.Where(p => p.Type == FurnitureType.Crop))
            _crops.TryAdd(piece.Id, new Crop(piece.Id));
    }

    internal bool WaterCrop(int furnitureId) => Publish(() =>
    {
        if (!_crops.TryGetValue(furnitureId, out var crop) || crop.Withered)
            throw new ArgumentException($"Piece {furnitureId} is no living Crop.", nameof(furnitureId));
        _crops[furnitureId] = crop with { Water = 1, DryDays = 0, Tended = true };
        return true;
    });

    // A farmer at a Crop: waters it if asked and it is thirsty, and picks it if ripe.
    private (int Picked, bool Watered) TendCrop(int villagerId, int villageId, int furnitureId, bool water)
    {
        if (!_crops.TryGetValue(furnitureId, out var crop) || crop.Withered)
            return (0, false);
        var watered = water && crop.Water < Crop.Thirsty;
        if (watered)
            crop = crop with { Water = 1, DryDays = 0, Tended = true };
        var picked = 0;
        if (crop.Ripe)
        {
            picked = Crop.Yield;
            crop = crop with { Growth = 0 };
            Emit(new CropHarvested(new StoreClock(_tick), villageId, villagerId, furnitureId, picked));
        }
        _crops[furnitureId] = crop;
        return (picked, watered);
    }

    private void AdvanceCrops(StoreClock clock)
    {
        if (_tick % CropTicks != 1) return;
        var days = (float)CropTicks / StoreClock.TicksPerStoreDay;
        foreach (var crop in _crops.Values.Where(c => c.Tended && !c.Withered).ToArray())
        {
            if (crop.Water > 0)
                _crops[crop.FurnitureId] = crop with
                {
                    Water = Math.Max(0, crop.Water - Crop.WaterPerDay * days),
                    Growth = Math.Min(1, crop.Growth + Crop.GrowthPerDay * days),
                };
            else if (crop.DryDays + days >= Crop.WitherDays)
            {
                _crops[crop.FurnitureId] = crop with { DryDays = Crop.WitherDays, Withered = true };
                Emit(new CropWithered(clock, crop.FurnitureId));
            }
            else
                _crops[crop.FurnitureId] = crop with { DryDays = crop.DryDays + days };
        }
        GatherCrops();
    }

    // An idle farmer of a Village tending fewer than Crop.Kept Crops carries the nearest living one home from further out.
    private void GatherCrops()
    {
        if (_villages.Count == 0) return;
        var pieces = _furniture.Pieces.Where(p => p.Type == FurnitureType.Crop && !_crops[p.Id].Withered).ToArray();
        if (pieces.Length == 0) return;
        var people = _villagers.Snapshot();
        foreach (var village in _villages)
        {
            var tended = pieces.Count(p => _space.Distance(village.Position, p.Position) <= Crop.TendingRange) +
                _furnitureTasks.Count(t => pieces.Any(p => p.Id == t.FurnitureId) && _space.Distance(village.Position, t.Destination) <= Crop.TendingRange);
            if (tended >= Crop.Kept) continue;
            if (people.FirstOrDefault(v => v.VillageId == village.Id && v.Role == Role.Farmer && v.Action == VillagerAction.Idle &&
                    v.FurnitureTaskId is null && !v.Crafting && v.CarriedAmount + v.CarriedMaterials == 0 && v.Hand is null) is not { } farmer)
                continue;
            // The Crops stand in a row beside the Village's centre, inside its walls.
            var spot = _space.Wrap(village.Position + new Vector2(-2.4f + tended % 4 * 0.7f, 2f + tended / 4 * 0.7f));
            foreach (var piece in pieces.Where(p => _space.Distance(village.Position, p.Position) is > Crop.TendingRange and <= Crop.GatherRange &&
                    !_villages.Any(v => _space.Distance(v.Position, p.Position) <= Crop.TendingRange) &&
                    !_furnitureTasks.Any(t => t.FurnitureId == p.Id))
                .OrderBy(p => _space.Distance(farmer.Position, p.Position)))
            {
                try
                {
                    StartFurnitureTask(piece.Id, [farmer.Id], spot, false);
                    break;
                }
                catch (InvalidOperationException) { /* Try the next reachable Crop. */ }
            }
        }
    }

    // Living Crops a Village tends, or could carry home: they keep its farmers from being sent scavenging.
    private bool HasCrops(Village village) =>
        _furniture.Pieces.Any(p => p.Type == FurnitureType.Crop && !_crops[p.Id].Withered &&
            _space.Distance(village.Position, p.Position) <= Crop.GatherRange);

    private void RestoreCrops(WorldSnapshot s)
    {
        var cropIds = s.Furniture.Where(p => p.Type == FurnitureType.Crop).Select(p => p.Id).ToHashSet();
        static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
        if (s.Crops.Count != cropIds.Count || s.Crops.Any(c => !cropIds.Contains(c.FurnitureId) || !Unit(c.Growth) || !Unit(c.Water) ||
                !float.IsFinite(c.DryDays) || c.DryDays is < 0 or > Crop.WitherDays || !c.Tended && (c.Water > 0 || c.Growth > 0 || c.Withered)) ||
            s.Crops.Select(c => c.FurnitureId).Distinct().Count() != s.Crops.Count)
            throw new InvalidDataException("Invalid world save: inconsistent Crops.");
        foreach (var crop in s.Crops) _crops[crop.FurnitureId] = crop;
    }
}
