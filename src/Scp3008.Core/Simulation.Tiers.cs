using System.Numerics;

namespace Scp3008.Core;

public enum WallUpgrade { Swapped, Strapped, Backfilled }

/// <summary>A wall piece was upgraded in place (Strapped, Backfilled) or swapped for a sturdier one; <paramref name="Tier"/> is the piece's new Tier.</summary>
public sealed record WallPieceUpgraded(StoreClock At, int VillageId, int Ring, int Slot, WallUpgrade How, int Tier) : DomainEvent(At);

/// <summary>A damaged wall piece was mended.</summary>
public sealed record WallPieceRepaired(StoreClock At, int VillageId, int Ring, int Slot, float Damage) : DomainEvent(At);

/// <summary>What a Staff blow did to a wall piece: its new Damage, how far the blow carried as noise, and whether the piece broke.</summary>
public sealed record WallBlow(float Damage, float NoiseRadius, bool Broken);

public sealed partial class Simulation
{
    /// <summary>The Damage an unprotected piece takes from a Staff blow.</summary>
    public const float BlowDamage = 0.25f;
    /// <summary>How far a blow on an unprotected piece carries as noise.</summary>
    public const float BlowNoise = 30f;
    private const int StrapMetal = 2, BackfillFoam = 3;
    private const float RepairStep = 0.25f;

    /// <summary>A piece's own Tier: light pieces 1, flat-pack wood 2, reinforced cabinets 3; Strapping and Backfill add one each.</summary>
    public static int TierOf(FurniturePiece piece, bool strapped, bool backfilled)
    {
        var weight = FurnitureResources.Weight(piece);
        return Math.Min(3, (weight < 25 ? 1 : weight < 100 ? 2 : 3) + (strapped ? 1 : 0) + (backfilled ? 1 : 0));
    }

    private static bool WallsDone(Village village) => village.HomeWalls().All(w => w.Complete) && village.Castle.All(c => c.Complete);

    /// <summary>One Builder repairs a damaged wall piece (while the Store is open), or else upgrades the weakest piece of a finished wall.</summary>
    private bool TendWalls(Village village, Villager worker, StoreClock clock)
    {
        for (var r = 0; r < village.Walls.Count; r++)
            for (var s = 0; s < village.Walls[r].Slots.Count; s++)
            {
                if (village.Walls[r].Slots[s].FurnitureId is not { } id) continue;
                var index = _furniture.Pieces.ToList().FindIndex(p => p.Id == id);
                var piece = _furniture.Pieces[index];
                if (piece.Damage is <= 0f or >= 1f || clock.Phase != DayPhase.Open) continue;
                var mended = piece with { Damage = MathF.Max(0f, piece.Damage - RepairStep) };
                _stabilityChanges.Add(mended);
                SetPose(index, piece, mended);
                Emit(new WallPieceRepaired(clock, village.Id, r, s, mended.Damage));
                return true;
            }
        if (!WallsDone(village)) return false;
        var rings = village.Walls.Select((w, i) => (Wall: w, Ring: i)).Where(x => village.IsHomeWall(x.Ring));
        var weakest = rings.SelectMany(x => x.Wall.Slots.Select((slot, i) => (x.Ring, Index: i, Slot: slot)))
            .Where(x => x.Slot.Tier < 3 && x.Slot.ReservedFurnitureId is null)
            .OrderBy(x => x.Slot.Tier).ThenBy(x => x.Ring).ThenBy(x => x.Index).FirstOrDefault();
        if (weakest.Slot is null) return false;
        var slotNow = weakest.Slot;
        var materials = village.Materials;
        if (!slotNow.Strapped && materials.GetValueOrDefault(MaterialKind.Metal) >= StrapMetal)
            Reinforce(village.Id, MaterialKind.Metal, StrapMetal, weakest.Ring, weakest.Index, slotNow with { Strapped = true }, WallUpgrade.Strapped, clock);
        else if (!slotNow.Backfilled && materials.GetValueOrDefault(MaterialKind.Foam) >= BackfillFoam)
            Reinforce(village.Id, MaterialKind.Foam, BackfillFoam, weakest.Ring, weakest.Index, slotNow with { Backfilled = true }, WallUpgrade.Backfilled, clock);
        else return SwapPiece(village, worker, weakest.Ring, weakest.Index);
        return true;
    }

    private void Reinforce(int villageId, MaterialKind kind, int amount, int ring, int index, BlueprintSlot slot, WallUpgrade how, StoreClock clock)
    {
        SetMaterialsCore(villageId, kind, _villages[villageId].Materials[kind] - amount);
        var piece = _furniture.Pieces.First(p => p.Id == slot.FurnitureId);
        slot = slot with { Tier = TierOf(piece, slot.Strapped, slot.Backfilled) };
        SetWallSlot(villageId, ring, index, slot);
        Emit(new WallPieceUpgraded(clock, villageId, ring, index, how, slot.Tier));
    }

    // Brings a sturdier loose piece to the slot; it replaces the old one when it arrives (CompleteBlueprintPiece).
    private bool SwapPiece(Village village, Villager worker, int ring, int index)
    {
        var slot = village.Walls[ring].Slots[index];
        foreach (var piece in _villagers.FurnitureNear(village.Position, 48).Where(p => slot.Fits(p) && p.Elevation == 0 && p.Tilt == 0 &&
            p.Roll == 0 && p.Damage == 0 && !IsBuiltFurniture(p.Id) && !_furnitureTasks.Any(t => t.FurnitureId == p.Id) &&
            TierOf(p, false, slot.Backfilled) > slot.Tier).OrderBy(p => _space.Distance(worker.Position, p.Position)))
        {
            try
            {
                StartFurnitureTask(piece.Id, [worker.Id], slot.Position, false, construction: true);
                SetWallSlot(village.Id, ring, index, slot with { ReservedFurnitureId = piece.Id });
                return true;
            }
            catch (InvalidOperationException) { /* Try the next reachable, haulable piece. */ }
        }
        return false;
    }

    /// <summary>The swapped-out piece is set down beside the Village centre; the new one takes its place (the Strapping stays with the old piece).</summary>
    private BlueprintSlot SwapIn(Village village, BlueprintSlot slot, int newId)
    {
        // shortcut: the old piece is just set down by the Village centre; give it a proper place (the yard) if swaps become common.
        if (slot.FurnitureId is { } old)
            MoveFurnitureCore(old, village.Position + new Vector2(0, 3), 0, 0);
        var piece = _furniture.Pieces.First(p => p.Id == newId);
        return slot with { FurnitureId = newId, ReservedFurnitureId = null, Strapped = false, Tier = TierOf(piece, false, slot.Backfilled) };
    }

    internal WallBlow StrikeWall(int villageId, int ring, int slot) => Publish(() =>
    {
        var village = _villages.Single(v => v.Id == villageId);
        if (ring < 0 || ring >= village.Walls.Count || slot < 0 || slot >= village.Walls[ring].Slots.Count ||
            village.Walls[ring].Slots[slot].FurnitureId is not { } id)
            throw new ArgumentException("No built wall piece there.");
        var s = village.Walls[ring].Slots[slot];
        var index = _furniture.Pieces.ToList().FindIndex(p => p.Id == id);
        var piece = _furniture.Pieces[index];
        var damage = MathF.Min(1f, piece.Damage + BlowDamage * (s.Strapped ? 0.5f : 1f) * (s.Backfilled ? 0.6f : 1f));
        var noise = BlowNoise * (s.Backfilled ? 0.4f : 1f);
        var damaged = piece with { Damage = damage };
        _stabilityChanges.Add(damaged);
        SetPose(index, piece, damaged);
        Emit(new NoiseEmitted(new StoreClock(_tick), piece.Position, noise, id));
        if (damage >= 1f) UnbuildFurniture(id);
        return new WallBlow(damage, noise, damage >= 1f);
    });
}

public sealed partial class ScenarioBuilder
{
    /// <summary>A Staff blow on a built wall piece: it takes Damage (less when Strapped or Backfilled), makes noise and breaks at 1.</summary>
    public WallBlow StrikeWall(int villageId, int ring, int slot) => _simulation.StrikeWall(villageId, ring, slot);
}
