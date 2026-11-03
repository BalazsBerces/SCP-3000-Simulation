using System.Numerics;

namespace Scp3008.Core;

/// <summary>Hidden Horde night tuning. <see cref="EveryNights"/> of zero disables Horde nights.</summary>
public sealed record HordeNightConfig
{
    public static HordeNightConfig Off { get; } = new() { EveryNights = 0 };

    /// <summary>Closing nights from one Horde night to the next.</summary>
    public int EveryNights { get; init; } = 7;
    /// <summary>The Closing nights after a Horde night with fewer, smaller Hordes and no Sieges.</summary>
    public int CalmNights { get; init; } = 3;
    /// <summary>The most Staff a Horde night brings, however developed the world.</summary>
    public int Ceiling { get; init; } = 48;

    internal void Validate()
    {
        if (EveryNights < 0 || CalmNights < 0 || Ceiling < 1 || EveryNights > 0 && CalmNights >= EveryNights)
            throw new ArgumentOutOfRangeException(nameof(HordeNightConfig));
    }
}

/// <summary>A Horde night began: <paramref name="Size"/> Staff came out in large Hordes to besiege the Villages.</summary>
/// <param name="Night">The Closing night, numbered by the Store day it began on.</param>
public sealed record HordeNightBegan(StoreClock At, int Night, int Size, IReadOnlyList<int> VillageIds) : DomainEvent(At);

public sealed partial class Simulation
{
    // The hidden Horde night rules (tuning): the size grows with the people and with each Village's walls and Castle.
    private const int HordeNightBase = 3;
    private const int PeoplePerHordeNightStaff = 2;
    // Within SiegeRange, so a Horde night's Hordes fall upon their Village at once.
    private const float HordeNightApproach = 20f;

    // The last Horde night (numbered as _staffNight); 0 before the first.
    private int _lastHordeNight;

    private HordeNightConfig HordeNights => _config.HordeNights ?? (_config.PillarLayout == PillarLayout.VillagerSandbox ||
        _config.LandmarkShowcase || _config.PointOfInterestShowcase ? HordeNightConfig.Off : new HordeNightConfig());

    /// <summary>Whether tonight lies in the calm stretch after a Horde night: fewer, smaller Hordes and no Sieges.</summary>
    private bool Calm => _lastHordeNight > 0 && _staffNight > _lastHordeNight && _staffNight <= _lastHordeNight + HordeNights.CalmNights;

    /// <summary>In the calm stretch every other Horde stays in, and the rest come out with half their Staff.</summary>
    private bool HeldBack(int staffId)
    {
        if (!Calm) return false;
        var horde = _hordes.First(h => h.Id == _staffStates[staffId].HordeId);
        return horde.Id % 2 == 1 || horde.StaffIds.IndexOf(staffId) >= Math.Max(1, horde.StaffIds.Count / 2);
    }

    /// <summary>How developed the world is: its people, and each standing Village's best home wall Tier and finished Castle parts.</summary>
    private int HordeNightSize() => Math.Min(HordeNights.Ceiling, HordeNightBase + _villagers.Snapshot().Count() / PeoplePerHordeNightStaff +
        _villages.Where(v => !v.Fallen).Sum(v => v.Walls.Where((w, i) => v.IsHomeWall(i)).Select(w => w.Tier).DefaultIfEmpty(0).Max() +
            v.Castle.Count(c => c.Complete)));

    /// <summary>
    /// At the start of a Closing night, every <see cref="HordeNightConfig.EveryNights"/>th night is a Horde night: Staff as many as the
    /// world's development (up to the ceiling) come out of the dark in one large Horde at each fortified Village.
    /// </summary>
    private void StartHordeNight(int night, StoreClock clock)
    {
        if (HordeNights.EveryNights == 0 || night - _lastHordeNight < HordeNights.EveryNights) return;
        _lastHordeNight = night;
        var size = HordeNightSize();
        var besieged = _villages.Where(Fortified).Take(size).ToList();
        for (var k = 0; k < besieged.Count; k++)
        {
            var village = besieged[k];
            var angle = village.Id * 2.4f;
            var approach = _navigation.NearestWalkable(_space.Wrap(village.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * HordeNightApproach));
            var horde = new HordeState(_hordes.Count == 0 ? 0 : _hordes.Max(h => h.Id) + 1, null, [approach], 0, 0, [], HordeNight: true);
            _hordes.Add(horde);
            // The Staff are shared out evenly, the first Villages taking the remainder.
            for (var n = 0; n < size / besieged.Count + (k < size % besieged.Count ? 1 : 0); n++)
            {
                var staff = _staff.Place(_navigation.NearestWalkable(_space.Wrap(approach + HordeOffset(n))));
                horde.StaffIds.Add(staff.Id);
                _staffStates[staff.Id] = new(horde.Id, 1f, 0);
            }
        }
        Emit(new HordeNightBegan(clock, night, besieged.Count == 0 ? 0 : size, Array.AsReadOnly(besieged.Select(v => v.Id).ToArray())));
    }

    /// <summary>When the Store opens, a Horde night's Hordes are gone back into the dark.</summary>
    private void WithdrawHordeNight()
    {
        foreach (var horde in _hordes.Where(h => h.HordeNight).ToList())
        {
            foreach (var id in horde.StaffIds)
            {
                _staff.Remove(id);
                _staffStates.Remove(id);
                var fight = _fights.FindIndex(f => f.StaffId == id);
                if (fight >= 0) EndFight(fight);
            }
            _hordes.Remove(horde);
        }
    }
}
