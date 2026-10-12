using System.Numerics;
using Scp3008.Core.Generation;

namespace Scp3008.Core;

/// <summary>
/// The simulation core facade: the single public entry point to the core.
/// The presentation layer only reads snapshots and events from it.
/// </summary>
public sealed class Simulation : IDisposable
{
    /// <summary>
    /// Real time between simulation steps. Each step runs as many fixed ticks as the speed value,
    /// so at 1x the core ticks 20 times per real second and a Store day lasts 10 real minutes.
    /// </summary>
    public static readonly TimeSpan RealTimeStep = TimeSpan.FromMilliseconds(50);

    // After a stall (debugger break, very heavy step) the thread catches up at most this much
    // real time instead of spiralling; the rest is dropped.
    private static readonly TimeSpan MaxCatchUp = RealTimeStep * 5;

    // A new world opens with weather already under way, as if it had been running this long.
    private const double WeatherWarmUpHours = 12;

    // Keys for the independent runtime random streams, one per subsystem.
    private const int WeatherStream = 1;

    private readonly object _stepLock = new();
    private readonly WorldConfig _config;
    private readonly StoreSpace _space;
    private readonly DepartmentMap _departments;
    private readonly FurnitureLayout _furniture;
    private readonly IReadOnlyList<Pillar> _pillars;
    private readonly IReadOnlyList<Sector> _sectors;
    private readonly Weather _weather;
    private long _tick;
    private SimulationSpeed _speed = SimulationSpeed.Normal;
    private TimeSpan _unspentRealTime;
    private volatile WorldSnapshot _snapshot;
    private volatile Action<DomainEvent>[] _subscribers = [];
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;

    private Simulation(WorldConfig config)
    {
        _config = config;
        _space = config.PillarLayout.Space();
        _departments = DepartmentGenerator.Generate(config.Seed, _space);
        _sectors = PillarGenerator.Sectors(_space, config.PillarLayout);
        _pillars = PillarGenerator.Pillars(_sectors, config.PillarLayout);
        _furniture = FurnitureGenerator.Generate(config.Seed, _space, _departments, _pillars);
        // Runtime randomness never comes from the Store seed (ADR 0003); it is unseeded unless a test fixes it.
        var runtimeSeed = config.RuntimeRngSeed ?? Random.Shared.Next();
        _weather = new Weather(_space, SeededRandom.For(runtimeSeed, WeatherStream));
        _weather.WarmUp(WeatherWarmUpHours);
        Scenario = new ScenarioBuilder(this);
        _snapshot = BuildSnapshot();
    }

    /// <summary>Creates a new world from the seed in <paramref name="config"/>.</summary>
    public static Simulation Create(WorldConfig config) => new(config);

    /// <summary>The latest published snapshot. Safe to read from any thread.</summary>
    public WorldSnapshot Snapshot => _snapshot;

    /// <summary>Sets up situations (e.g. places a Fog bank), for tests and the dev debug panel.</summary>
    public ScenarioBuilder Scenario { get; }

    /// <summary>
    /// The fog density at a Store position in the latest snapshot, from 0 (only the thin haze) to
    /// 1 (deep inside a thick Fog bank). Safe to call from any thread.
    /// </summary>
    public float FogDensityAt(Vector2 position) => _snapshot.FogDensityAt(position);

    /// <inheritdoc cref="WorldSnapshot.SectorAt"/>
    public Sector SectorAt(Vector2 position) => _snapshot.SectorAt(position);

    /// <inheritdoc cref="WorldSnapshot.NearestPillar"/>
    public Pillar NearestPillar(Vector2 position) => _snapshot.NearestPillar(position);

    /// <summary>True while the simulation thread is running the clock.</summary>
    public bool IsRunning => _thread is { IsAlive: true } && !_stop.IsCancellationRequested;

    /// <summary>
    /// Starts the simulation thread, which lets real time pass at the current <see cref="Speed"/>
    /// in fixed <see cref="RealTimeStep"/> steps, independent of the frame rate.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_stop.IsCancellationRequested, this);
        lock (_stepLock)
        {
            if (_thread is not null)
                throw new InvalidOperationException("The simulation thread is already started.");
            _thread = new Thread(RunClock) { IsBackground = true, Name = "Simulation" };
            _thread.Start();
        }
    }

    /// <summary>The speed the clock runs at against real time. Safe to set from any thread.</summary>
    public SimulationSpeed Speed
    {
        get => _snapshot.Speed;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            lock (_stepLock)
            {
                _speed = value;
                _snapshot = BuildSnapshot();
            }
        }
    }

    /// <summary>
    /// Lets real time pass at the current speed: every whole <see cref="RealTimeStep"/> runs
    /// one step of ticks. Leftover real time is carried over to the next call; real time that
    /// passes while paused is discarded.
    /// </summary>
    public void AdvanceRealTime(TimeSpan realElapsed)
    {
        if (realElapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(realElapsed), "Time only runs forward.");
        lock (_stepLock)
        {
            if (_speed == SimulationSpeed.Paused)
            {
                _unspentRealTime = TimeSpan.Zero;
                return;
            }
            _unspentRealTime += realElapsed;
            var steps = _unspentRealTime.Ticks / RealTimeStep.Ticks;
            _unspentRealTime -= RealTimeStep * steps;
            RunTicks(steps * (long)_speed);
        }
    }

    /// <summary>Advances simulated Store time synchronously, rounded to whole ticks.</summary>
    public void Advance(TimeSpan storeTime)
    {
        if (storeTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(storeTime), "Time only runs forward.");
        AdvanceTicks(StoreClock.TicksFor(storeTime));
    }

    /// <summary>
    /// Advances simulated Store time synchronously to the next moment the Store clock shows
    /// <paramref name="timeOfDay"/> (rounded to whole ticks); does nothing if it already does.
    /// Lands exactly even while the simulation thread is running.
    /// </summary>
    public void AdvanceTo(TimeSpan timeOfDay)
    {
        if (timeOfDay < TimeSpan.Zero || timeOfDay >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(timeOfDay), "A time of day lies from 00:00 up to 24:00.");
        var target = StoreClock.TicksFor(timeOfDay) % StoreClock.TicksPerStoreDay;
        lock (_stepLock)
        {
            var now = StoreClock.TicksFor(new StoreClock(_tick).TimeOfDay);
            RunTicks((target - now + StoreClock.TicksPerStoreDay) % StoreClock.TicksPerStoreDay);
        }
    }

    /// <summary>Advances the simulation synchronously by a number of fixed ticks.</summary>
    public void AdvanceTicks(long ticks)
    {
        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks), "Time only runs forward.");
        lock (_stepLock)
            RunTicks(ticks);
    }

    /// <summary>
    /// Subscribes to the domain event stream. Handlers run synchronously on whichever thread
    /// advances the simulation (the simulation thread while the clock is running), so they
    /// must be quick and must not call back into the facade's advance methods.
    /// Dispose the result to unsubscribe.
    /// </summary>
    public IDisposable Subscribe(Action<DomainEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_stepLock)
            _subscribers = [.. _subscribers, handler];
        return new Subscription(this, handler);
    }

    /// <summary>Stops the simulation thread, if started.</summary>
    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
            return;
        _stop.Cancel();
        if (_thread is not null && _thread != Thread.CurrentThread)
            _thread.Join();
    }

    private void RunClock()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var last = stopwatch.Elapsed;
        var nextStep = last + RealTimeStep;
        while (!_stop.IsCancellationRequested)
        {
            var wait = nextStep - stopwatch.Elapsed;
            if (wait > TimeSpan.Zero && _stop.Token.WaitHandle.WaitOne(wait))
                break;

            var now = stopwatch.Elapsed;
            var elapsed = now - last;
            if (elapsed > MaxCatchUp)
                elapsed = MaxCatchUp;
            last = now;
            nextStep += RealTimeStep;
            if (nextStep < now)
                nextStep = now + RealTimeStep;

            AdvanceRealTime(elapsed);
        }
    }

    private void RunTicks(long ticks)
    {
        if (ticks == 0)
            return;
        for (long i = 0; i < ticks; i++)
            Step();
        _snapshot = BuildSnapshot();
    }

    private void Step()
    {
        _tick++;
        var clock = new StoreClock(_tick);
        _weather.Advance(StoreClock.TickDuration.TotalHours, clock, Emit);
        var timeOfDay = clock.TimeOfDay;
        if (timeOfDay == TimeSpan.Zero)
            Emit(new StoreDayBegan(clock, clock.Day));
        if (timeOfDay == StoreHours.ClosingTimeStarts)
            Emit(new ClosingTimeBegan(clock, clock.Day));
        else if (timeOfDay == StoreHours.Reopens)
            Emit(new StoreReopened(clock, clock.Day));
    }

    private void Emit(DomainEvent domainEvent)
    {
        foreach (var handler in _subscribers)
            handler(domainEvent);
    }

    /// <summary>Applies a scenario change to the weather under the step lock and publishes a new snapshot.</summary>
    internal T Change<T>(Func<Weather, StoreClock, Action<DomainEvent>, T> change)
    {
        lock (_stepLock)
        {
            var result = change(_weather, new StoreClock(_tick), Emit);
            _snapshot = BuildSnapshot();
            return result;
        }
    }

    /// <inheritdoc cref="Change{T}"/>
    internal void Change(Action<Weather, StoreClock, Action<DomainEvent>> change) =>
        Change<bool>((weather, now, emit) =>
        {
            change(weather, now, emit);
            return true;
        });

    private void Unsubscribe(Action<DomainEvent> handler)
    {
        lock (_stepLock)
        {
            var remaining = _subscribers.ToList();
            remaining.Remove(handler);
            _subscribers = [.. remaining];
        }
    }

    private WorldSnapshot BuildSnapshot() =>
        new(_config.Seed, _config.PillarLayout, new StoreClock(_tick), _space, _speed, _departments, _furniture.Pieces, _furniture.Displays, _furniture.Cells, Array.AsReadOnly(_weather.Snapshot()), _weather.Wind, _pillars, _sectors);

    private sealed class Subscription(Simulation owner, Action<DomainEvent> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Unsubscribe(handler);
        }
    }
}
