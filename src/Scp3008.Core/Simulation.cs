using System.Numerics;
using Scp3008.Core.Generation;

namespace Scp3008.Core;

/// <summary>
/// The simulation core facade: the single public entry point to the core.
/// The presentation layer only reads snapshots and events from it.
/// </summary>
public sealed partial class Simulation : IDisposable
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
    private FurnitureLayout _furniture;
    private readonly IReadOnlyList<Pillar> _pillars;
    private readonly IReadOnlyList<Sector> _sectors;
    private readonly IReadOnlyList<Landmark> _landmarks;
    private readonly IReadOnlyList<PointOfInterest> _pointsOfInterest;
    private readonly BallPitNetwork _ballPitNetwork;
    private IReadOnlyList<TankFish> _tankFish;
    private IReadOnlyList<CounterMeal> _counterMeals;
    private IReadOnlyList<CampGoods> _campGoods;
    private IReadOnlyList<StockedGoods> _stock;
    private IReadOnlyList<WaterPoint> _waterPoints;
    private IReadOnlyList<SectorStock> _sectorStocks;
    private readonly Weather _weather;
    private readonly Navigation _navigation;
    private readonly Villagers _villagers;
    private readonly Villagers _staff;
    private readonly List<Band> _bands = [];
    private readonly List<Gear> _gear = [];
    private readonly List<(int Id, Vector2[] Route, int Next)> _patrols = [];
    private long _tick;
    private SimulationSpeed _speed = SimulationSpeed.Normal;
    private TimeSpan _unspentRealTime;
    private volatile WorldSnapshot _snapshot;
    private volatile Action<DomainEvent>[] _subscribers = [];
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;

    private Simulation(WorldConfig config)
    {
        config.Needs?.Validate();
        config.Restock?.Validate();
        _config = config;
        _space = config.PillarLayout.Space();
        var sandbox = config.PillarLayout == PillarLayout.VillagerSandbox;
        _departments = sandbox
            ? new DepartmentMap(16f, 8, new int[64], [new Department(0, DepartmentType.RestaurantAndFoodMarket, new Vector2(64), 64, 128 * 128)])
            : DepartmentGenerator.Generate(config.Seed, _space);
        _sectors = PillarGenerator.Sectors(_space, config.PillarLayout);
        _pillars = PillarGenerator.Pillars(_sectors, config.PillarLayout);
        // The Landmarks are placed first, so the Displays keep off them; their Furniture follows the Displays'.
        // The Landmark showcase has every Landmark in a row and no Displays around them.
        // The Point of interest showcase has no Landmarks and no Displays either.
        if (config.LandmarkShowcase && config.PointOfInterestShowcase)
            throw new ArgumentException("A world is at most one showcase.", nameof(config));
        var sites = sandbox ? [] : config.LandmarkShowcase
            ? LandmarkGenerator.Showcase(_space, _sectors, _pillars, _departments.CellSize)
            : config.PointOfInterestShowcase
                ? []
                : LandmarkGenerator.Plan(config.Seed, _space, _sectors, _pillars, _departments.CellSize);
        // The Points of interest are planned next, so the Displays keep off them too; the Landmark showcase has none.
        IReadOnlyList<PointOfInterestSite> poiSites = sandbox || config.LandmarkShowcase
            ? []
            : config.PointOfInterestShowcase
                ? PointOfInterestGenerator.Showcase(_space, _sectors, _pillars)
                : PointOfInterestGenerator.Plan(config.Seed, _space, _departments, _sectors, _pillars, sites);
        var furniture = sandbox || config.LandmarkShowcase || config.PointOfInterestShowcase
            ? new FurnitureLayout([], [], [])
            : FurnitureGenerator.Generate(config.Seed, _space, _departments, _pillars, sites, poiSites);
        var landmarks = LandmarkGenerator.Build(config.Seed, _space, sites, furniture.Pieces.Count);
        var pointsOfInterest = PointOfInterestGenerator.Build(config.Seed, _space, poiSites, furniture.Pieces.Count + landmarks.Pieces.Count);
        _furniture = furniture with { Pieces = furniture.Pieces.Concat(landmarks.Pieces).Concat(pointsOfInterest.Pieces).ToList().AsReadOnly() };
        _landmarks = landmarks.Landmarks;
        _campGoods = landmarks.CampGoods;
        _pointsOfInterest = pointsOfInterest.PointsOfInterest;
        _ballPitNetwork = pointsOfInterest.BallPitNetwork;
        _tankFish = pointsOfInterest.TankFish;
        _counterMeals = pointsOfInterest.CounterMeals;
        _stock = sandbox ? [] : StockGenerator.Generate(config.Seed, config.Stock ?? new StockConfig(), _furniture.Pieces, _furniture.Displays, _departments,
            _pointsOfInterest, _tankFish, _counterMeals, _campGoods, _landmarks, _space, config.PillarLayout);
        _sectorStocks = StockGenerator.Summarize(_stock, _furniture.Pieces, _space, config.PillarLayout, config.Stock ?? new StockConfig());
        _waterPoints = WaterGenerator.Generate(config.Seed, config.Water ?? new WaterConfig(), _furniture.Pieces, _furniture.Displays,
            _pointsOfInterest, _landmarks);
        // Runtime randomness never comes from the Store seed (ADR 0003); it is unseeded unless a test fixes it.
        var runtimeSeed = config.RuntimeRngSeed ?? Random.Shared.Next();
        _weather = new Weather(_space, SeededRandom.For(runtimeSeed, WeatherStream));
        if (sandbox)
            _weather.SetFormation(false);
        else
            _weather.WarmUp(WeatherWarmUpHours);
        _navigation = new Navigation(_space, () => _furniture.Pieces, _pillars);
        _villagers = new Villagers(_space, _navigation);
        _staff = new Villagers(_space, _navigation);
        Scenario = new ScenarioBuilder(this);
        _snapshot = BuildSnapshot();
        InitializeRestock();
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

    /// <inheritdoc cref="WorldSnapshot.GroundAt"/>
    public GroundProperty GroundAt(Vector2 position) => _snapshot.GroundAt(position);

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
        if (_stockChanged)
        {
            _sectorStocks = StockGenerator.Summarize(_stock, _furniture.Pieces, _space, _config.PillarLayout, StockConfig);
            _stockChanged = false;
        }
        _snapshot = BuildSnapshot();
    }

    private void Step()
    {
        _tick++;
        var clock = new StoreClock(_tick);
        _weather.Advance(StoreClock.TickDuration.TotalHours, clock, Emit);
        AdvanceRestock();
        _villagers.Advance(StoreClock.TickDuration.TotalHours);
        if (_villagers.HasVillagers)
            _villagers.AdvanceNeeds(StoreClock.TickDuration.TotalHours, _snapshot with
                { Clock = clock, Stock = _stock, Furniture = _furniture.Pieces, WaterPoints = _waterPoints,
                    FogBanks = _weather.Snapshot(), Staff = _staff.Snapshot().Select(s => new Staff(s.Id, s.Position, s.Destination, s.Heading)).ToArray() },
                _config.Needs ?? new NeedsConfig(), TakeGoods, Emit);
        // Sandbox patrols use the same footprint navigation; daytime Staff remain dormant.
        if (clock.Phase == DayPhase.ClosingTime && _patrols.Count > 0)
        {
            var staff = _staff.Snapshot();
            for (var i = 0; i < _patrols.Count; i++)
            {
                var patrol = _patrols[i];
                if (staff.Single(s => s.Id == patrol.Id).Destination is null)
                {
                    _staff.Send(patrol.Id, patrol.Route[patrol.Next]);
                    _patrols[i] = (patrol.Id, patrol.Route, (patrol.Next + 1) % patrol.Route.Length);
                }
            }
            _staff.Advance(StoreClock.TickDuration.TotalHours);
        }
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

    /// <summary>Takes every piece of Furniture that matches out of the Store and publishes a new snapshot; returns how many.</summary>
    internal int RemoveFurniture(Func<FurniturePiece, bool> match)
    {
        lock (_stepLock)
        {
            var kept = _furniture.Pieces.Where(p => !match(p)).ToList();
            var removed = _furniture.Pieces.Count - kept.Count;
            if (removed > 0)
            {
                var keptIds = kept.Select(p => p.Id).ToHashSet();
                foreach (var piece in _furniture.Pieces.Where(p => !keptIds.Contains(p.Id)))
                    VacateFurniture(piece.Id);
                _navigation.Update(_furniture.Pieces.Where(p => !keptIds.Contains(p.Id)), []);
                _furniture = _furniture with { Pieces = kept.AsReadOnly() };
                _villagers.Replan();
                _staff.Replan();
                // Goods go with the Furniture that held them.
                var ids = kept.Select(p => p.Id).ToHashSet();
                _stock = _stock.Where(g => ids.Contains(g.FurnitureId)).ToList().AsReadOnly();
                _sectorStocks = StockGenerator.Summarize(_stock, _furniture.Pieces, _space, _config.PillarLayout, StockConfig);
                _waterPoints = _waterPoints.Where(w => w.FurnitureId < 0 || ids.Contains(w.FurnitureId)).ToList().AsReadOnly();
                _snapshot = BuildSnapshot();
            }
            return removed;
        }
    }

    /// <summary>Sets the units of one Goods kind on a piece of Stock Furniture (none takes the kind off it) and publishes a new snapshot.</summary>
    internal void SetStock(IEnumerable<(int FurnitureId, GoodsKind Kind)> goods, int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var set = goods.ToHashSet();
        lock (_stepLock)
        {
            var ids = set.Select(g => g.FurnitureId).ToHashSet();
            var stockIds = _furniture.Pieces.Where(p => ids.Contains(p.Id) && _snapshot.IsStockFurniture(p)).Select(p => p.Id).ToHashSet();
            foreach (var id in ids.Where(id => !stockIds.Contains(id)).Take(1))
                throw new ArgumentException($"Piece {id} is not Stock Furniture; Display Furniture holds no stock.", nameof(goods));
            var rest = _stock.Where(g => g.LeftBehind || !set.Contains((g.FurnitureId, g.Kind)));
            _stock = (amount > 0 ? rest.Concat(set.Select(g => new StockedGoods(g.FurnitureId, g.Kind, amount))) : rest).ToList().AsReadOnly();
            foreach (var key in set)
                _stockTargets[key] = Math.Max(_stockTargets.GetValueOrDefault(key), amount);
            _sectorStocks = StockGenerator.Summarize(_stock, _furniture.Pieces, _space, _config.PillarLayout, StockConfig);
            _snapshot = BuildSnapshot();
        }
    }

    /// <summary>Puts a new piece of Furniture on the floor (the next free id), lets walking Villagers find their way again and publishes a new snapshot.</summary>
    internal FurniturePiece PlaceFurniture(FurnitureType type, Vector2 position, float rotation, int variant) =>
        Publish(() =>
        {
            var piece = new FurniturePiece(_nextFurnitureId++, type, _space.Wrap(position), 0f,
                rotation, variant);
            _restockOrigins[piece.Id] = piece;
            _furniture = _furniture with { Pieces = _furniture.Pieces.Append(piece).ToList().AsReadOnly() };
            _navigation.Update([], [piece]);
            _villagers.Replan();
            _staff.Replan();
            return piece;
        });

    /// <summary>Moves a piece of Furniture across the floor, lets walking Villagers find their way again and publishes a new snapshot.</summary>
    internal FurniturePiece MoveFurniture(int furnitureId, Vector2 position, float rotation) =>
        Publish(() =>
        {
            var index = _furniture.Pieces.ToList().FindIndex(p => p.Id == furnitureId);
            if (index < 0)
                throw new ArgumentException($"No piece {furnitureId}.", nameof(furnitureId));
            var before = _furniture.Pieces[index];
            var moved = before with { Position = _space.Wrap(position), Rotation = rotation };
            if (moved != before)
            {
                VacateFurniture(furnitureId);
                _waterPoints = _waterPoints.Where(w => w.FurnitureId != furnitureId).ToList().AsReadOnly();
            }
            _furniture = _furniture with { Pieces = _furniture.Pieces.Select((p, i) => i == index ? moved : p).ToList().AsReadOnly() };
            _sectorStocks = StockGenerator.Summarize(_stock, _furniture.Pieces, _space, _config.PillarLayout, StockConfig);
            _navigation.Update([before], [moved]);
            _villagers.Replan();
            _staff.Replan();
            return moved;
        });

    internal Band PlaceBand(IReadOnlyList<Vector2> positions, Needs? needs = null) => Publish(() =>
    {
        (needs ?? new Needs()).Validate();
        var band = new Band(_bands.Count, Array.AsReadOnly(positions.Select(p => _villagers.Place(p, needs).Id).ToArray()));
        _bands.Add(band);
        return band;
    });

    internal Gear PlaceGear(GearKind kind, Vector2 position) => Publish(() =>
    {
        var gear = new Gear(_gear.Count, kind, _space.Wrap(position));
        _gear.Add(gear);
        return gear;
    });

    internal Staff PlaceStaff(Vector2 position, Vector2[] route) => Publish(() =>
    {
        var staff = _staff.Place(position);
        _patrols.Add((staff.Id, route.Select(_space.Wrap).ToArray(), 0));
        return new Staff(staff.Id, staff.Position, staff.Destination, staff.Heading);
    });

    internal Villager PlaceVillager(Vector2 position, Needs? needs = null) => Publish(() => _villagers.Place(position, needs));

    internal void SendVillager(int villagerId, Vector2 destination) => Publish(() =>
    {
        _villagers.Send(villagerId, destination);
        return true;
    });

    /// <summary>Moves the clock on to the next moment it shows the time of day, without running the ticks between, and publishes a new snapshot.</summary>
    internal void SetTimeOfDay(TimeSpan timeOfDay) => Publish(() =>
    {
        var target = StoreClock.TicksFor(timeOfDay) % StoreClock.TicksPerStoreDay;
        var now = StoreClock.TicksFor(new StoreClock(_tick).TimeOfDay);
        _tick += (target - now + StoreClock.TicksPerStoreDay) % StoreClock.TicksPerStoreDay;
        return true;
    });

    private T Publish<T>(Func<T> change)
    {
        lock (_stepLock)
        {
            var result = change();
            _snapshot = BuildSnapshot();
            return result;
        }
    }

    private StockConfig StockConfig => _config.Stock ?? new StockConfig();

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
        new(_config.Seed, _config.PillarLayout, new StoreClock(_tick), _space, _speed, _departments, _furniture.Pieces, _furniture.Displays, _furniture.Cells, Array.AsReadOnly(_weather.Snapshot()), _weather.Wind, _pillars, _sectors, _landmarks, _pointsOfInterest, _config.LandmarkShowcase)
        {
            BallPitNetwork = _ballPitNetwork,
            TankFish = _tankFish,
            CounterMeals = _counterMeals,
            CampGoods = _campGoods,
            Stock = _stock,
            SectorStocks = _sectorStocks,
            WaterPoints = _waterPoints,
            ShelfCapacity = StockConfig.ShelfCapacity,
            PointOfInterestShowcase = _config.PointOfInterestShowcase,
            Villagers = Array.AsReadOnly(_villagers.Snapshot()),
            Bodies = _villagers.Bodies.ToList().AsReadOnly(),
            Bands = _bands.ToList().AsReadOnly(),
            Gear = _gear.ToList().AsReadOnly(),
            Staff = Array.AsReadOnly(_staff.Snapshot().Select(s => new Staff(s.Id, s.Position, s.Destination, s.Heading)).ToArray()),
        };

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
