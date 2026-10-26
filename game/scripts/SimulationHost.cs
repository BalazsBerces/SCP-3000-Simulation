using System.Collections.Concurrent;
using System.Globalization;
using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Owns the simulation core for the scene: creates the world, runs the core on its own thread,
/// and hands snapshots and domain events to the presentation on the main thread. A new world
/// (e.g. from a seed the Observer entered) is generated in the background and swapped in
/// when ready. User arguments: <c>--seed=N</c> sets the first world's seed, <c>--pillars=3x3|2x2|3x3L|3x3L-corners|3x3L-diamond</c>
/// its Pillar layout, and <c>--start-time=HH:MM</c> fast-forwards it to that Store time (dev tool,
/// e.g. to see Closing time).
/// </summary>
public partial class SimulationHost : Node
{
    /// <summary>Store seed of the first world, unless given on the command line.</summary>
    [Export] public int Seed { get; set; } = 3008;

    /// <summary>
    /// Pillar layout of the first world unless given on the command line: the four diamond Pillars on
    /// the large Store. The benchmark scene sets the 3 × 3 layout, so its camera path stays on the same Store.
    /// </summary>
    [Export] public PillarLayout StartLayout { get; set; } = PillarLayout.DiamondLarge;

    /// <summary>Pillar layout of the first world; then of the latest world asked for.</summary>
    public PillarLayout Layout { get; private set; }

    /// <summary>Whether the latest world asked for is the Landmark showcase (<c>--landmark-showcase</c> for the first).</summary>
    public bool LandmarkShowcase { get; private set; }

    /// <summary>Whether the latest world asked for is the Point of interest showcase (<c>--poi-showcase</c> for the first).</summary>
    public bool PointOfInterestShowcase { get; private set; }

    public string? SandboxPreset { get; private set; }

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

    /// <summary>Dev checks use the same scenario seam as facade tests.</summary>
    internal ScenarioBuilder Scenario => _simulation.Scenario;

    /// <summary>True while a new world is being generated.</summary>
    public bool IsGenerating => _nextWorld is not null;

    /// <summary>Store time interpolated between the last two snapshots, for smooth display.</summary>
    public TimeSpan InterpolatedStoreTime => _interpolator.StoreTime(NowSeconds);

    /// <summary>The Fog banks interpolated between the last two snapshots, for smooth drifting.</summary>
    public IEnumerable<FogBank> InterpolatedFogBanks => _interpolator.FogBanks(NowSeconds);

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
        var startPaused = false;
        Layout = StartLayout;
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--villager-sandbox"))
            SandboxPreset = "scavenge";
        foreach (var arg in args)
        {
            if (arg.StartsWith("--sandbox-preset="))
                SandboxPreset = arg["--sandbox-preset=".Length..];
            else if (arg.StartsWith("--seed="))
                Seed = int.Parse(arg["--seed=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--pillars="))
                Layout = PillarLayouts.Parse(arg["--pillars=".Length..]);
            else if (arg.StartsWith("--start-time="))
                startTime = TimeSpan.ParseExact(arg["--start-time=".Length..], @"hh\:mm", CultureInfo.InvariantCulture);
            else if (arg == "--pause")
                startPaused = true;
            else if (arg == "--landmark-showcase")
                LandmarkShowcase = true;
            else if (arg == "--poi-showcase")
                PointOfInterestShowcase = true;
        }
        // A world is at most one showcase.
        if (LandmarkShowcase && PointOfInterestShowcase)
            PointOfInterestShowcase = false;
        var generating = Time.GetTicksMsec();
        var simulation = SandboxPreset is { } preset
            ? ScenarioBuilder.LoadVillagerSandbox(preset)
            : Simulation.Create(new WorldConfig(Seed, PillarLayout: Layout, LandmarkShowcase: LandmarkShowcase,
                PointOfInterestShowcase: PointOfInterestShowcase));
        Layout = simulation.Snapshot.PillarLayout;
        GD.Print($"Store generated in {Time.GetTicksMsec() - generating} ms, from {generating} ms after start.");
        if (startTime is { } target)
            simulation.AdvanceTo(target);
        Adopt(simulation, startPaused ? SimulationSpeed.Paused : SimulationSpeed.Normal);
    }

    public override void _Ready()
    {
        _simulation.Start();
        // Load time, for performance passes: engine start to the first drawn frame of the Store.
        RenderingServer.Singleton.Connect(RenderingServer.SignalName.FramePostDraw,
            Callable.From(() => GD.Print($"First frame drawn {Time.GetTicksMsec()} ms after start.")), (uint)ConnectFlags.OneShot);
    }

    /// <summary>Fast-forwards the Store clock to the next <paramref name="timeOfDay"/> (dev tool, e.g. for the benchmark).</summary>
    public void AdvanceTo(TimeSpan timeOfDay) => _simulation.AdvanceTo(timeOfDay);

    /// <summary>The Villagers, blended between the last two snapshots for smooth walking.</summary>
    public IEnumerable<Villager> InterpolatedVillagers => _interpolator.Villagers(NowSeconds);

    /// <summary>Dev tool (debug panel): places a standing Villager through the scenario builder.</summary>
    public Villager PlaceVillager(System.Numerics.Vector2 position) => _simulation.Scenario.PlaceVillager(position);

    /// <summary>Dev tool (debug panel): sends a Villager walking to a Store position through the scenario builder.</summary>
    public void SendVillager(int villagerId, System.Numerics.Vector2 destination) => _simulation.Scenario.SendVillager(villagerId, destination);

    /// <summary>Dev tool (debug panel): lets Store time pass at once, simulated tick by tick.</summary>
    public void SkipTime(TimeSpan storeTime) => _simulation.Advance(storeTime);

    /// <summary>Dev tool (debug panel, benchmark): places a fully formed Fog bank through the scenario builder.</summary>
    public FogBank PlaceFogBank(System.Numerics.Vector2 position, float radius) =>
        _simulation.Scenario.PlaceFogBank(position, radius);

    /// <summary>Dev tool (benchmark): removes every Fog bank and stops new ones forming, for a fixed view.</summary>
    public void HoldClearWeather()
    {
        _simulation.Scenario.SetFogBankFormation(false);
        _simulation.Scenario.ClearFogBanks();
    }

    /// <summary>
    /// Dev tool (shelf captures, benchmark): sets the amount of every Goods kind on each piece of Stock Furniture within
    /// <paramref name="radius"/> metres of <paramref name="around"/> to <paramref name="amount"/> (0 leaves it bare). Only kinds
    /// a piece already holds are set, so a bare shelf stays bare and a shelf keeps its kind.
    /// </summary>
    public void SetShelfStock(System.Numerics.Vector2 around, float radius, int amount)
    {
        var snapshot = Snapshot;
        var pieces = snapshot.Furniture.ToDictionary(p => p.Id);
        var near = snapshot.Stock.Where(g => !g.LeftBehind
            && snapshot.Space.Delta(around, pieces[g.FurnitureId].Position).LengthSquared() <= radius * radius).ToList();
        _simulation.Scenario.SetStock(near.Select(g => (g.FurnitureId, g.Kind)), amount);
        GD.Print($"Shelf stock: {near.Count} Goods on Stock Furniture within {radius:0} m set to {amount}.");
    }

    /// <summary>
    /// Generates a new Store from <paramref name="seed"/> in the background, with the given Pillar
    /// layout or else the current one, as the Landmark showcase, the Point of interest showcase or a normal Store (else
    /// as the current world is; choosing one showcase leaves the other), then replaces the current world; its Store clock starts anew.
    /// </summary>
    public void CreateWorld(int seed, PillarLayout? layout = null, bool? landmarkShowcase = null, bool? pointOfInterestShowcase = null)
    {
        if (_nextWorld is not null)
            return;
        Seed = seed;
        Layout = layout ?? (Snapshot.VillagerSandbox ? StartLayout : Layout);
        LandmarkShowcase = landmarkShowcase ?? (pointOfInterestShowcase == true ? false : LandmarkShowcase);
        PointOfInterestShowcase = pointOfInterestShowcase ?? (landmarkShowcase == true ? false : PointOfInterestShowcase);
        var config = new WorldConfig(seed, PillarLayout: Layout, LandmarkShowcase: LandmarkShowcase,
            PointOfInterestShowcase: PointOfInterestShowcase);
        SandboxPreset = null;
        _nextWorld = Task.Run(() => Simulation.Create(config));
    }

    /// <summary>Replaces the sandbox through the same loader used by facade tests.</summary>
    public void LoadSandboxPreset(string preset)
    {
        if (_nextWorld is not null)
            return;
        SandboxPreset = preset;
        Layout = PillarLayout.VillagerSandbox;
        LandmarkShowcase = PointOfInterestShowcase = false;
        _nextWorld = Task.Run(() => ScenarioBuilder.LoadVillagerSandbox(preset));
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
                case FogBankFormed formed:
                    GD.Print($"Fog bank {formed.Bank.Id} formed at ({formed.Bank.Position.X:0}, {formed.Bank.Position.Y:0}).");
                    break;
                case FogBankDissipated dissipated:
                    GD.Print($"Fog bank {dissipated.FogBankId} dissipated at ({dissipated.Position.X:0}, {dissipated.Position.Y:0}).");
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
