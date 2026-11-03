using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Smooths the presentation between snapshots. The core publishes one snapshot per fixed real-time step, but a heavy
/// step comes late and the next ones then come in a burst. So the presentation plays the world back on a clock of its
/// own, a couple of steps behind the latest snapshot, at the rate snapshots have been bringing ticks; it eases back
/// towards that lag instead of jumping, and blends the two buffered snapshots on either side of the moment it shows.
/// </summary>
public sealed class SnapshotInterpolator(TimeSpan stepInterval)
{
    // How many steps behind the latest snapshot playback aims to stay: room for a step that comes late.
    private const double LagSteps = 2;
    // How long playback takes to make up most of a gap to that lag, in seconds.
    private const double Easing = 1.5;
    // How long the measured tick rate takes to follow a change in how fast snapshots bring ticks, in seconds.
    private const double RateSmoothing = 1;
    // Further off than this many steps (a skip, a load), playback jumps instead of easing.
    private const double JumpSteps = 8;

    private readonly List<WorldSnapshot> _buffer = [];
    private double _shownTick;
    private double _lastFrameSeconds;
    private double _lastArrivalSeconds;
    private double _ticksPerSecond;
    private SimulationSpeed _speed;
    private WorldSnapshot? _from;
    private WorldSnapshot? _to;
    private float _blend;

    /// <summary>Feeds the core's current snapshot and moves playback on; call once per frame.</summary>
    public void Observe(WorldSnapshot snapshot, double nowSeconds)
    {
        var tick = snapshot.Clock.Tick;
        if (_buffer.Count == 0 || tick < _buffer[^1].Clock.Tick)
        {
            // The first snapshot, or another world: show it as it is.
            _buffer.Clear();
            _buffer.Add(snapshot);
            _shownTick = tick;
            _lastFrameSeconds = _lastArrivalSeconds = nowSeconds;
            _speed = snapshot.Speed;
            _ticksPerSecond = NominalRate(snapshot.Speed);
            Select();
            return;
        }
        if (snapshot.Speed != _speed)
        {
            _speed = snapshot.Speed;
            _ticksPerSecond = NominalRate(snapshot.Speed);
        }
        if (tick == _buffer[^1].Clock.Tick)
            // Same moment in the world (e.g. only the speed changed): nothing new to play.
            _buffer[^1] = snapshot;
        else
        {
            var sinceArrival = nowSeconds - _lastArrivalSeconds;
            if (sinceArrival > 0 && snapshot.Speed != SimulationSpeed.Paused)
            {
                var arrived = (tick - _buffer[^1].Clock.Tick) / sinceArrival;
                _ticksPerSecond += (arrived - _ticksPerSecond) * Math.Min(1, sinceArrival / RateSmoothing);
            }
            _lastArrivalSeconds = nowSeconds;
            _buffer.Add(snapshot);
        }

        var elapsed = Math.Max(0, nowSeconds - _lastFrameSeconds);
        _lastFrameSeconds = nowSeconds;
        var latest = _buffer[^1].Clock.Tick;
        // A step's worth of ticks: more than the speed while the clock races (a fast-forward).
        var step = Math.Max(Math.Max(1, (int)snapshot.Speed), _ticksPerSecond * stepInterval.TotalSeconds);
        var rate = snapshot.Speed == SimulationSpeed.Paused ? 0 : _ticksPerSecond;
        // Where the core has probably got to since the latest snapshot, so the target moves on smoothly between snapshots
        // instead of a step at a time; a late snapshot holds it back after the lag's worth.
        var since = Math.Min(nowSeconds - _lastArrivalSeconds, LagSteps * stepInterval.TotalSeconds);
        var target = latest + rate * since - LagSteps * step;
        _shownTick += rate * elapsed;
        var gap = target - _shownTick;
        if (Math.Abs(gap) > JumpSteps * step)
            _shownTick = target;
        else
            _shownTick += gap * Math.Min(1, elapsed / Easing);
        _shownTick = Math.Clamp(_shownTick, _buffer[0].Clock.Tick, latest);
        // Snapshots playback has passed are no longer needed.
        while (_buffer.Count > 2 && _buffer[1].Clock.Tick <= _shownTick)
            _buffer.RemoveAt(0);
        Select();
    }

    private double NominalRate(SimulationSpeed speed) => (int)speed / stepInterval.TotalSeconds;

    // The two snapshots around the shown moment, and how far it is from the first to the second.
    private void Select()
    {
        _from = _buffer[0];
        _to = _buffer.Count > 1 ? _buffer[1] : _buffer[0];
        var span = _to.Clock.Tick - _from.Clock.Tick;
        _blend = span > 0 ? (float)Math.Clamp((_shownTick - _from.Clock.Tick) / span, 0, 1) : 1f;
    }

    /// <summary>Total Store time to display.</summary>
    public TimeSpan StoreTime()
    {
        if (_from is null || _to is null)
            return TimeSpan.Zero;
        var from = _from.Clock.TotalStoreTime;
        var to = _to.Clock.TotalStoreTime;
        return from + (to - from) * _blend;
    }

    /// <summary>
    /// The Fog banks to display: each bank of the later snapshot blended from where it was in the earlier one, along the
    /// shortest wrapped path, so a bank drifting across a seam moves smoothly. Positions may lie just outside the Store floor.
    /// </summary>
    public IEnumerable<FogBank> FogBanks()
    {
        if (_from is not { } earlier || _to is not { } later)
            yield break;
        var blend = _blend;
        foreach (var bank in later.FogBanks)
        {
            var before = earlier.FogBanks.FirstOrDefault(b => b.Id == bank.Id);
            if (before is null)
            {
                yield return bank;
                continue;
            }
            var step = later.Space.Delta(before.Position, bank.Position);
            yield return bank with
            {
                Position = before.Position + step * blend,
                Radius = before.Radius + (bank.Radius - before.Radius) * blend,
                Density = before.Density + (bank.Density - before.Density) * blend,
            };
        }
    }

    /// <summary>
    /// The Villagers to display, each blended from where they stood in the earlier snapshot along the shortest wrapped
    /// path, so a Villager crossing a seam walks on smoothly.
    /// </summary>
    public IEnumerable<Villager> Villagers()
    {
        if (_from is not { } earlier || _to is not { } later)
            yield break;
        var blend = _blend;
        foreach (var villager in later.Villagers)
        {
            var before = earlier.Villagers.FirstOrDefault(v => v.Id == villager.Id);
            if (before is null)
            {
                yield return villager;
                continue;
            }
            var turn = MathF.IEEERemainder(villager.Heading - before.Heading, MathF.Tau);
            yield return villager with
            {
                Position = before.Position + later.Space.Delta(before.Position, villager.Position) * blend,
                Heading = before.Heading + turn * blend,
                Elevation = before.Elevation + (villager.Elevation - before.Elevation) * blend,
            };
        }
    }

    /// <summary>
    /// Where a piece of the later snapshot's Furniture is shown: blended from <paramref name="earlier"/>, the same piece
    /// in the earlier snapshot, so carried Furniture keeps pace with the Villagers carrying it.
    /// </summary>
    public FurniturePiece Piece(FurniturePiece later, FurniturePiece? earlier) =>
        _to is not { } snapshot || earlier is not { } before ? later : later with
        {
            Position = before.Position + snapshot.Space.Delta(before.Position, later.Position) * _blend,
            Elevation = before.Elevation + (later.Elevation - before.Elevation) * _blend,
            Rotation = before.Rotation + MathF.IEEERemainder(later.Rotation - before.Rotation, MathF.Tau) * _blend,
        };

    /// <summary>The snapshots playback is between: Villagers and moving Furniture are shown blended from the first to the second.</summary>
    public (WorldSnapshot? Earlier, WorldSnapshot? Later) Shown => (_from, _to);
}
