using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// An immutable, read-only view of the world at one moment. Safe to read from any thread.
/// </summary>
/// <param name="Seed">The seed the Store was generated from.</param>
/// <param name="PillarLayout">The world's Pillar layout, which fixes its Pillars and Sectors.</param>
/// <param name="Clock">The Store clock.</param>
/// <param name="Space">The geometry of the wrapping Store floor and its chunks.</param>
/// <param name="Speed">The speed the simulation clock is running at.</param>
/// <param name="Departments">Which Department covers each part of the Store.</param>
/// <param name="Furniture">Every piece of Furniture in the Store, each an individual object.</param>
/// <param name="Displays">Every Display (room or set) the Furniture stands in, indexed by <see cref="Display.Id"/>.</param>
/// <param name="CellLayouts">How each department cell is laid out, row by row from the north-west.</param>
/// <param name="FogBanks">The Fog banks drifting through the Store.</param>
/// <param name="Wind">The wind the Fog banks drift with, in metres per Store hour (X east, Y south).</param>
/// <param name="Pillars">The identical concrete Pillars on the layout's grid (9 or 4), marked by grid coordinate; never light or power sources.</param>
/// <param name="Sectors">The equal Sectors of the layout's grid, row by row from the north-west; a Pillar stands in each one or, on the four-Pillar large layouts, in some of them.</param>
/// <param name="Landmarks">The world's Landmarks, indexed by <see cref="Landmark.Id"/>; their Furniture is in <see cref="Furniture"/>, after the Displays' pieces.</param>
/// <param name="PointsOfInterest">The world's Points of interest, indexed by <see cref="PointOfInterest.Id"/>; their Furniture is in <see cref="Furniture"/>, after the Landmarks' pieces.</param>
/// <param name="LandmarkShowcase">Whether this is the Landmark showcase (every Landmark in a row, no Displays) rather than a normal Store.</param>
public sealed record WorldSnapshot(
    int Seed,
    PillarLayout PillarLayout,
    StoreClock Clock,
    StoreSpace Space,
    SimulationSpeed Speed,
    DepartmentMap Departments,
    IReadOnlyList<FurniturePiece> Furniture,
    IReadOnlyList<Display> Displays,
    IReadOnlyList<CellLayout> CellLayouts,
    IReadOnlyList<FogBank> FogBanks,
    Vector2 Wind,
    IReadOnlyList<Pillar> Pillars,
    IReadOnlyList<Sector> Sectors,
    IReadOnlyList<Landmark> Landmarks,
    IReadOnlyList<PointOfInterest> PointsOfInterest,
    bool LandmarkShowcase = false)
{
    /// <summary>How a department cell is laid out; coordinates wrap.</summary>
    public CellLayout CellLayoutAt(int x, int y)
    {
        var side = Departments.CellsPerSide;
        return CellLayouts[(((y % side) + side) % side) * side + (((x % side) + side) % side)];
    }

    /// <summary>Whether the Store is open or in Closing time.</summary>
    public DayPhase Phase => Clock.Phase;

    /// <summary>Whether this is the Point of interest showcase (every Point of interest in a row, no Displays or Landmarks) rather than a normal Store.</summary>
    public bool PointOfInterestShowcase { get; init; }

    /// <summary>The live fish in every tank of the Aquarium aisles, one entry per <see cref="FurnitureType.FishTank"/>, in tank order.</summary>
    public IReadOnlyList<TankFish> TankFish { get; init; } = Array.Empty<TankFish>();

    /// <summary>The meals on the Cafeterias' serving counters, one entry per <see cref="FurnitureType.HotFoodCounter"/> and <see cref="FurnitureType.DessertCounter"/>, in counter order.</summary>
    public IReadOnlyList<CounterMeal> CounterMeals { get; init; } = Array.Empty<CounterMeal>();

    /// <summary>The goods on the Camping grounds' stands, one entry per kind per stand (the marshmallow tasting stand holds marshmallows).</summary>
    public IReadOnlyList<CampGoods> CampGoods { get; init; } = [];

    /// <summary>
    /// All the Store's Goods: which piece holds them, which kind, how much. Store stock lies on Stock Furniture
    /// (<see cref="IsStockFurniture"/>), the Cafeteria meals, Aquarium fish and Camping ground goods included; the rare
    /// <see cref="StockedGoods.LeftBehind"/> finds lie in Display Furniture.
    /// </summary>
    public IReadOnlyList<StockedGoods> Stock { get; init; } = [];

    /// <summary>
    /// Whether a piece is Stock Furniture, which holds Goods as real store stock (grocery shelves, freezers, warehouse shelving,
    /// Cafeteria counters, Aquarium tanks, the Camping ground's stands, a Lost and found's boxes), whether or not any are on it now.
    /// </summary>
    public bool IsStockFurniture(FurniturePiece piece) =>
        Generation.StockGenerator.IsStockFurniture(piece, Displays, Departments, PointsOfInterest, CampGoods);

    /// <summary>The units of Goods in the Store per Goods class, every class listed.</summary>
    public IReadOnlyDictionary<GoodsClass, int> StockByClass()
    {
        var totals = Enum.GetValues<GoodsClass>().ToDictionary(c => c, _ => 0);
        foreach (var goods in Stock)
            totals[GoodsCatalog.ClassOf(goods.Kind)] += goods.Amount;
        return totals;
    }

    /// <summary>The Store's three Ball pits as the one network the Staff pass through at Closing time.</summary>
    public BallPitNetwork BallPitNetwork { get; init; } = BallPitNetwork.None;

    /// <summary>
    /// The Ball pits, by <see cref="PointOfInterest.Id"/>, the Staff can pass to now from one Point of interest:
    /// every other Ball pit at Closing time, none while open or from anything else. Villagers never pass.
    /// </summary>
    public IReadOnlyList<int> StaffExitsFrom(int pointOfInterestId) => BallPitNetwork.StaffExitsFrom(pointOfInterestId, Phase);

    /// <summary>How bright the Store lights are, from 0 (out) to 1 (fully on).</summary>
    public double StoreLightLevel => Clock.StoreLightLevel;

    /// <summary>
    /// How bright the Store lights are at a Store position (it may be unwrapped), from 0 (out) to 1 (fully on):
    /// <see cref="StoreLightLevel"/>, except on ground with <see cref="GroundProperty.Darkness"/>, where they never come on.
    /// </summary>
    public double StoreLightLevelAt(Vector2 position) =>
        GroundAt(position).HasFlag(GroundProperty.Darkness) ? 0.0 : StoreLightLevel;

    /// <summary>
    /// How far a Lamp has come on, from 0 (off) to 1 (fully lit); see <see cref="StoreHours.LampLevelAt"/>.
    /// A Lamp on ground with <see cref="GroundProperty.Darkness"/> is never lit.
    /// </summary>
    public float LampLevel(FurniturePiece lamp) =>
        GroundAt(lamp.Position).HasFlag(GroundProperty.Darkness) ? 0f : StoreHours.LampLevelAt(lamp.Id, Clock.TimeOfDay);

    /// <summary>
    /// What the ground is like at a Store position (it may be unwrapped): the <see cref="Landmark.Ground"/> of the
    /// Landmark whose rectangle covers it, <see cref="GroundProperty.None"/> elsewhere. It belongs to the ground, so
    /// it stays whatever happens to the Landmark's Furniture.
    /// </summary>
    public GroundProperty GroundAt(Vector2 position)
    {
        var ground = GroundProperty.None;
        foreach (var landmark in Landmarks)
            if (landmark.Ground != GroundProperty.None && landmark.Covers(Space, position))
                ground |= landmark.Ground;
        return ground;
    }

    /// <summary>The true time left until Closing time next begins: up to a whole Store day, zero at the moment it begins.</summary>
    public TimeSpan TimeToClosing
    {
        get
        {
            var left = (StoreHours.ClosingTimeStarts - Clock.TimeOfDay).Ticks % TimeSpan.TicksPerDay;
            return TimeSpan.FromTicks(left < 0 ? left + TimeSpan.TicksPerDay : left);
        }
    }

    /// <summary>
    /// The time a clock of the clock wall shows on its 24-hour dial, which counts down: the time left until Closing time
    /// plus the clock's own offset (<see cref="Landmark.ClockOffsets"/>), within one day. Only the true clock shows
    /// <see cref="TimeToClosing"/>.
    /// </summary>
    public TimeSpan ClockReading(FurniturePiece clock)
    {
        if (clock.LandmarkId < 0 || clock.LandmarkId >= Landmarks.Count ||
            !Landmarks[clock.LandmarkId].ClockOffsets.TryGetValue(clock.Id, out var offset))
            throw new ArgumentException($"Piece {clock.Id} is not a clock of the clock wall.", nameof(clock));
        return TimeSpan.FromTicks((TimeToClosing + offset).Ticks % TimeSpan.TicksPerDay);
    }

    /// <summary>
    /// The fog density at a Store position, from 0 (only the thin haze) to 1 (deep inside a thick
    /// Fog bank), using wrapped distances to the Fog banks.
    /// </summary>
    public float FogDensityAt(Vector2 position) => FogBank.CombinedDensityAt(Space, FogBanks, position);

    /// <summary>
    /// The Sector a Store position is in. Positions may be unwrapped; a position on a Sector
    /// boundary belongs to the Sector east or south of it.
    /// </summary>
    public Sector SectorAt(Vector2 position) => Sectors[Generation.PillarGenerator.SectorIndexAt(Space, PillarLayout, position)];

    /// <summary>
    /// The Pillar nearest to a Store position by the shortest wrapped distance. Positions may be
    /// unwrapped. In 2 × 2 the Pillar to the east and the one to the west are the same Pillar.
    /// </summary>
    public Pillar NearestPillar(Vector2 position)
    {
        var nearest = Pillars[0];
        var nearestDistance = float.MaxValue;
        foreach (var pillar in Pillars)
        {
            var distance = Space.Delta(position, pillar.Position).LengthSquared();
            if (distance < nearestDistance)
                (nearest, nearestDistance) = (pillar, distance);
        }
        return nearest;
    }

    /// <summary>
    /// The position itself, or, if it lies inside a Pillar's footprint, the point straight out
    /// through the nearest face onto the middle of the walkway ring. Positions may be unwrapped and
    /// keep their image of the Store.
    /// </summary>
    public Vector2 OutsidePillars(Vector2 position)
    {
        var pillar = NearestPillar(position);
        var delta = Space.Delta(pillar.Position, position);
        var half = pillar.Width / 2f;
        if (MathF.Abs(delta.X) >= half || MathF.Abs(delta.Y) >= half)
            return position;
        var outside = half + Pillar.WalkwayWidth / 2f;
        var moved = MathF.Abs(delta.X) > MathF.Abs(delta.Y)
            ? delta with { X = delta.X < 0f ? -outside : outside }
            : delta with { Y = delta.Y < 0f ? -outside : outside };
        return position + moved - delta;
    }
}
