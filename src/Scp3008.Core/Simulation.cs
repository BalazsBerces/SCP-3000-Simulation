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

    private readonly object _stepLock = new();
    private readonly WorldConfig _config;
    private readonly StoreSpace _space = StoreSpace.Standard;
    private readonly DepartmentMap _departments;
    private readonly IReadOnlyList<FurniturePiece> _furniture;
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
        _departments = DepartmentGenerator.Generate(config.Seed, _space);
        _furniture = FurnitureGenerator.Generate(config.Seed, _space, _departments);
        _snapshot = BuildSnapshot();
    }

    /// <summary>Creates a new world from the seed in <paramref name="config"/>.</summary>
    public static Simulation Create(WorldConfig config) => new(config);

    /// <summary>The latest published snapshot. Safe to read from any thread.</summary>
    public WorldSnapshot Snapshot => _snapshot;

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
        new(_config.Seed, new StoreClock(_tick), _space, _speed, _departments, _furniture);

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
