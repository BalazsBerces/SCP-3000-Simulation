using System.Collections.Concurrent;
using System.Globalization;
using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Owns the simulation core for the scene: creates the world, runs the core on its own thread,
/// and hands snapshots and domain events to the presentation on the main thread. A new world
/// (e.g. from a seed the Observer entered) is generated in the background and swapped in
/// when ready. User arguments: <c>--seed=N</c> sets the first world's seed, and
/// <c>--start-time=HH:MM</c> fast-forwards it to that Store time (dev tool, e.g. to see Closing time).
/// </summary>
public partial class SimulationHost : Node
{
    /// <summary>Store seed of the first world, unless given on the command line.</summary>
    [Export] public int Seed { get; set; } = 3008;

    private readonly ConcurrentQueue<DomainEvent> _pendingEvents = new();
    private SnapshotInterpolator _interpolator = new(Simulation.RealTimeStep);
    private Simulation _simulation = null!;
    private IDisposable? _subscription;
    private Task<Simulation>? _nextWorld;

    /// <summary>Raised on the main thread for every domain event from the core.</summary>
    public event Action<DomainEvent>? DomainEventReceived;

    /// <summary>Raised on the main thread when a new world has replaced the old one.</summary>
    public event Action<WorldSnapshot>? WorldCreated;

    /// <summary>The latest snapshot from the core.</summary>
    public WorldSnapshot Snapshot => _simulation.Snapshot;

    /// <summary>True while a new world is being generated.</summary>
    public bool IsGenerating => _nextWorld is not null;

    /// <summary>Store time interpolated between the last two snapshots, for smooth display.</summary>
    public TimeSpan InterpolatedStoreTime => _interpolator.StoreTime(NowSeconds);

    /// <summary>Time control: the speed the Store clock runs at.</summary>
    public SimulationSpeed Speed
    {
        get => _simulation.Speed;
        set => _simulation.Speed = value;
    }

    private static double NowSeconds => Time.GetTicksUsec() / 1_000_000.0;

    public override void _EnterTree()
    {
        TimeSpan? startTime = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--seed="))
                Seed = int.Parse(arg["--seed=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--start-time="))
                startTime = TimeSpan.ParseExact(arg["--start-time=".Length..], @"hh\:mm", CultureInfo.InvariantCulture);
        }
        var simulation = Simulation.Create(new WorldConfig(Seed));
        if (startTime is { } target)
            simulation.AdvanceTo(target);
        Adopt(simulation, SimulationSpeed.Normal);
    }

    public override void _Ready()
    {
        _simulation.Start();
    }

    /// <summary>Fast-forwards the Store clock to the next <paramref name="timeOfDay"/> (dev tool, e.g. for the benchmark).</summary>
    public void AdvanceTo(TimeSpan timeOfDay) => _simulation.AdvanceTo(timeOfDay);

    /// <summary>Generates a new Store from <paramref name="seed"/> in the background, then replaces the current world.</summary>
    public void CreateWorld(int seed)
    {
        if (_nextWorld is not null)
            return;
        Seed = seed;
        _nextWorld = Task.Run(() => Simulation.Create(new WorldConfig(seed)));
    }

    public override void _Process(double delta)
    {
        if (_nextWorld is { IsFaulted: true } failed)
        {
            _nextWorld = null;
            GD.PushError($"Store generation failed: {failed.Exception}");
        }
        if (_nextWorld is { IsCompleted: true } next)
        {
            _nextWorld = null;
            var speed = _simulation.Speed;
            _subscription?.Dispose();
            _simulation.Dispose();
            _pendingEvents.Clear();
            Adopt(next.Result, speed);
            _simulation.Start();
            WorldCreated?.Invoke(_simulation.Snapshot);
        }

        _interpolator.Observe(_simulation.Snapshot, NowSeconds);
        while (_pendingEvents.TryDequeue(out var domainEvent))
        {
            switch (domainEvent)
            {
                case StoreDayBegan began:
                    GD.Print($"Store day {began.Day} began.");
                    break;
                case ClosingTimeBegan closing:
                    GD.Print($"Closing time began on Store day {closing.Day}.");
                    break;
                case StoreReopened reopened:
                    GD.Print($"The Store reopened on Store day {reopened.Day}.");
                    break;
            }
            DomainEventReceived?.Invoke(domainEvent);
        }
    }

    public override void _ExitTree()
    {
        _subscription?.Dispose();
        _simulation.Dispose();
        _nextWorld?.ContinueWith(t => t.Result.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private void Adopt(Simulation simulation, SimulationSpeed speed)
    {
        _simulation = simulation;
        _simulation.Speed = speed;
        _subscription = _simulation.Subscribe(_pendingEvents.Enqueue);
        _interpolator = new SnapshotInterpolator(Simulation.RealTimeStep);
        _interpolator.Observe(_simulation.Snapshot, NowSeconds);
    }
}
