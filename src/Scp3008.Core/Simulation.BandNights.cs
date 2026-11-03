using System.Numerics;

namespace Scp3008.Core;

/// <summary>How a Band without walls survives Closing time; its Personality picks it, a bad night may change it.</summary>
public enum BandStrategy
{
    /// <summary>Withdraws into a room-set Display (or stays where it is), blocks the way with Furniture, keeps one watch and stays silent.</summary>
    Hide,
    /// <summary>Keeps travelling quietly, away from the Staff it hears or sees.</summary>
    MoveOn,
    /// <summary>Quickly builds a small barricade where it is.</summary>
    Barricade,
}

/// <summary>Closing time began for a Band, which survives it by <paramref name="Strategy"/>; <paramref name="First"/> on its first night.</summary>
public sealed record BandNightBegan(StoreClock At, int BandId, BandStrategy Strategy, bool First) : DomainEvent(At);

/// <summary>After a bad night (a member Wounded or Taken) a Band changed how it survives Closing time.</summary>
public sealed record BandStrategyChanged(StoreClock At, int BandId, BandStrategy From, BandStrategy To) : DomainEvent(At);

public sealed partial class Simulation
{
    // The Band night rules (tuning).
    private static readonly TimeSpan ShelterBefore = TimeSpan.FromMinutes(30);
    private const float BarricadeReach = 15f;
    private const int BarricadePieces = 8;
    private const float BarricadeMaxWeight = 100f;
    private const float StaffWariness = 25f;
    private const float NoisyMemberNoise = 10f;

    internal static BandStrategy StrategyFor(Personality personality) => personality switch
    {
        Personality.Bold => BandStrategy.MoveOn,
        Personality.Builders => BandStrategy.Barricade,
        _ => BandStrategy.Hide,
    };

    // What a Band tries after its way failed: a found hiding place is walled up, a broken barricade left, a chased Band hides.
    private static BandStrategy NextStrategy(BandStrategy strategy) => strategy switch
    {
        BandStrategy.Hide => BandStrategy.Barricade,
        BandStrategy.Barricade => BandStrategy.MoveOn,
        _ => BandStrategy.Hide,
    };

    /// <summary>
    /// A Band's Closing time, from shortly before it until the Store reopens: whether it is busy with the night (and does
    /// not travel or found). At dusk it picks its shelter but may still reach and join a Village. In the morning it opens
    /// its barricade and takes up a new strategy after a bad night.
    /// </summary>
    private bool AdvanceBandNight(StoreClock clock, Band band, Villager[] members)
    {
        var closing = clock.Phase == DayPhase.ClosingTime;
        if (!closing && clock.TimeOfDay < StoreHours.ClosingTimeStarts - ShelterBefore)
        {
            if (band.Shelter is null && band.Barricade.Count == 0 && band.NextStrategy is null) return false;
            OpenBarricade(band);
            var strategy = band.NextStrategy ?? band.Strategy;
            _bands[_bands.IndexOf(band)] = band with { Strategy = strategy, NextStrategy = null, Shelter = null, Barricade = [] };
            if (strategy != band.Strategy) Emit(new BandStrategyChanged(clock, band.Id, band.Strategy, strategy));
            return false;
        }
        var night = clock.TimeOfDay >= StoreHours.ClosingTimeStarts ? clock.Day : clock.Day - 1;
        if (closing && band.LastNight < night)
        {
            Emit(new BandNightBegan(clock, band.Id, band.Strategy, band.LastNight == 0));
            band = _bands[_bands.IndexOf(band)] = band with { LastNight = night };
        }
        if (band.Strategy == BandStrategy.MoveOn)
        {
            if (closing) MoveOn(band, members);
        }
        else if (band.Shelter is not { } shelter)
        {
            shelter = band.Strategy == BandStrategy.Hide ? HidingPlace(band.Position) : members[0].Position;
            _bands[_bands.IndexOf(band)] = band with { Shelter = shelter, Site = shelter };
        }
        else if (closing && band.Barricade.Count == 0)
        {
            // Still on its way when the lights went out: it shelters where it got to.
            if (_space.Distance(members[0].Position, shelter) > ArrivalRadius + 1) shelter = members[0].Position;
            _bands[_bands.IndexOf(band)] = band with { Shelter = shelter, Barricade = BuildBandBarricade(shelter, members) };
        }
        // A Wounded, starving or parched member groans and stirs; the whole Band is heard.
        if (closing)
            foreach (var member in members.Where(m => _wounds.ContainsKey(m.Id) || m.Needs.Hunger >= 0.9 || m.Needs.Thirst >= 0.9))
                Emit(new NoiseEmitted(clock, member.Position, NoisyMemberNoise, -1));
        return closing;
    }

    /// <summary>The nearest room-set Display within reach to withdraw into; where the Band stands if there is none.</summary>
    private Vector2 HidingPlace(Vector2 from) =>
        _furniture.Displays.Where(d => _space.Distance(from, d.Centre) <= BarricadeReach * 2)
            .OrderBy(d => _space.Distance(from, d.Centre))
            .Select(d => _navigation.NearestWalkable(d.Centre))
            .FirstOrDefault(p => _navigation.FindPath(from, p) is not null, from);

    /// <summary>
    /// The Band crowds together at its shelter and closes a ring of loose Furniture round itself: tall pieces stood
    /// edge to edge hide the members from Staff passing by. Returns the pieces used.
    /// </summary>
    private IReadOnlyList<int> BuildBandBarricade(Vector2 shelter, Villager[] members)
    {
        // shortcut: the members within reach step into the huddle and the ring stands at once; no carrying animation yet.
        for (var i = 0; i < members.Length; i++)
            if (_space.Distance(members[i].Position, shelter) <= ArrivalRadius + 1)
                _villagers.Teleport(members[i].Id, _space.Wrap(shelter + HordeOffset(i) * 0.2f));
        var used = _bands.SelectMany(b => b.Barricade).ToHashSet();
        var pieces = _furniture.Pieces
            .Where(p => p.Elevation == 0 && !used.Contains(p.Id) && FurnitureResources.Weight(p) <= BarricadeMaxWeight &&
                FurnitureCatalog.Spec(p) is { IsFixed: false, IsFloorCovering: false, Height: >= 1f } &&
                !IsSlotFurniture(p.Id) && !_furnitureTasks.Any(t => t.FurnitureId == p.Id) &&
                _space.Distance(p.Position, shelter) <= BarricadeReach)
            .OrderBy(p => _space.Distance(p.Position, shelter)).Take(BarricadePieces).ToArray();
        if (pieces.Length == 0) return [];
        // Overlapping a little, so no sight line passes between neighbours.
        var radius = MathF.Max(0.9f, pieces.Sum(p => FurnitureCatalog.Spec(p).Width) * 0.85f / MathF.Tau);
        for (var i = 0; i < pieces.Length; i++)
        {
            var angle = MathF.Tau * i / pieces.Length;
            MoveFurnitureCore(pieces[i].Id, _space.Wrap(shelter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius), angle + MathF.PI / 2);
        }
        return Array.AsReadOnly(pieces.Select(p => p.Id).ToArray());
    }

    /// <summary>In the morning the Band pushes its barricade outwards to walk out; the pieces stay where they end up.</summary>
    private void OpenBarricade(Band band)
    {
        if (band.Shelter is not { } shelter) return;
        foreach (var id in band.Barricade)
            if (PieceById(id) is { } piece && piece.Elevation == 0 && !_furnitureTasks.Any(t => t.FurnitureId == id) && !IsSlotFurniture(id))
            {
                var outward = _space.Delta(shelter, piece.Position);
                if (outward.LengthSquared() > 1e-6f)
                    MoveFurnitureCore(id, _space.Wrap(piece.Position + Vector2.Normalize(outward) * 1.5f), piece.Rotation);
            }
    }

    /// <summary>A Band moving on walks on quietly: away from any hostile Staff near it, else towards its site or onwards.</summary>
    private void MoveOn(Band band, Villager[] members)
    {
        var clock = new StoreClock(_tick);
        var lead = members[0].Position;
        var threat = _staff.Snapshot().Where(s => Acting(s, clock) && _space.Distance(s.Position, lead) <= StaffWariness)
            .OrderBy(s => _space.Distance(s.Position, lead)).FirstOrDefault();
        Vector2 goal;
        if (threat is not null)
            goal = lead + Vector2.Normalize(_space.Delta(threat.Position, lead) + new Vector2(0.001f)) * StaffWariness;
        else if (members.Any(m => m.Destination is not null))
            return;
        else if (band.Site is { } site && _space.Distance(lead, site) > ArrivalRadius)
            goal = site;
        else
        {
            var angle = _arrivalRandom.Range(0, MathF.Tau);
            goal = lead + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 15;
        }
        for (var i = 0; i < members.Length; i++)
            _villagers.Send(members[i].Id, _space.Wrap(goal + HordeOffset(i)));
    }

    /// <summary>A member of a Band was Wounded or Taken: after the night the Band may try another way, surely after a loss.</summary>
    private void BandHarmed(int villagerId, bool taken)
    {
        var index = _bands.FindIndex(b => b.VillagerIds.Contains(villagerId));
        if (index < 0 || _bands[index].NextStrategy is not null) return;
        if (taken || _arrivalRandom.Next(2) == 0)
            _bands[index] = _bands[index] with { NextStrategy = NextStrategy(_bands[index].Strategy) };
    }

    /// <summary>
    /// Whether a Village does noisy work (building, hauling) at Closing time: cautious ones leave it for open hours, bold
    /// ones risk it, and so do desperate ones, which the Staff have already struck (a member lies Wounded).
    /// </summary>
    private bool RisksNoisyWork(Village village) =>
        village.Personality == Personality.Bold || village.VillagerIds.Any(_wounds.ContainsKey);
}
