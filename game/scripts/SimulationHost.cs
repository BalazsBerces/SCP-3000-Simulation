using System.Collections.Concurrent;
using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Owns the simulation core for the scene: creates the world, runs the core on its own thread,
/// and hands snapshots and domain events to the presentation on the main thread.
/// </summary>
public partial class SimulationHost : Node
{
    /// <summary>Store seed used until the Observer can enter one.</summary>
    [Export] public int Seed { get; set; } = 3008;

    private readonly ConcurrentQueue<DomainEvent> _pendingEvents = new();
    private readonly SnapshotInterpolator _interpolator = new(Simulation.RealTimeStep);
    private Simulation _simulation = null!;
    private IDisposable? _subscription;

    /// <summary>Raised on the main thread for every domain event from the core.</summary>
    public event Action<DomainEvent>? DomainEventReceived;

    /// <summary>The latest snapshot from the core.</summary>
    public WorldSnapshot Snapshot => _simulation.Snapshot;

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
        _simulation = Simulation.Create(new WorldConfig(Seed));
        _subscription = _simulation.Subscribe(_pendingEvents.Enqueue);
        _interpolator.Observe(_simulation.Snapshot, NowSeconds);
    }

    public override void _Ready()
    {
        _simulation.Start();
    }

    public override void _Process(double delta)
    {
        _interpolator.Observe(_simulation.Snapshot, NowSeconds);
        while (_pendingEvents.TryDequeue(out var domainEvent))
        {
            if (domainEvent is StoreDayBegan began)
                GD.Print($"Store day {began.Day} began.");
            DomainEventReceived?.Invoke(domainEvent);
        }
    }

    public override void _ExitTree()
    {
        _subscription?.Dispose();
        _simulation.Dispose();
    }
}
