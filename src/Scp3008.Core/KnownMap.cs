using System.Numerics;

namespace Scp3008.Core;

/// <summary>One Villager's observations. Unseen sources become stale after one Store hour.</summary>
public sealed record KnownMap
{
    public IReadOnlyList<Vector2> ExploredGround { get; init; } = [];
    public IReadOnlyList<KnownGoods> Goods { get; init; } = [];
    public IReadOnlyList<KnownWater> Water { get; init; } = [];
    public IReadOnlyList<KnownDanger> Dangers { get; init; } = [];
    public IReadOnlyList<KnownVillage> Villages { get; init; } = [];
}

public sealed record KnownGoods(Vector2 Position, StockedGoods Goods, StoreClock LastSeen, bool Stale = false);

/// <summary>Running is null until a showroom tap has actually been tried; false means dry.</summary>
public sealed record KnownWater(WaterPointKind Kind, Vector2 Position, int FurnitureId, bool? Running, StoreClock LastSeen, bool Stale = false);

public sealed record KnownDanger(int StaffId, Vector2 Position, StoreClock LastSeen, bool Stale = false);

/// <summary>Abandoned once a Villager has seen the place empty; the newest sighting wins when maps merge.</summary>
public sealed record KnownVillage(int VillageId, Vector2 Position, StoreClock LastSeen, bool Stale = false, bool Abandoned = false);

internal sealed partial class Villagers
{
    private const float GroundStep = 8;
    private FurnitureIndex? _furnitureIndex;
    private float _largestPiece;
    private readonly SightBlockers _sight = new();
    private readonly List<FurniturePiece> _blockers = [];
    private readonly List<FurniturePiece> _nearby = [];
    private readonly Dictionary<FurniturePiece, bool> _seen = [];
    private readonly HashSet<int> _knownIds = [];
    private readonly HashSet<(int, Vector2)> _nearbyAt = [];
    // A Villager's known Goods keys by piece, from lists reused between Observations.
    private readonly Dictionary<int, List<(int, GoodsKind, bool)>> _knownKeys = [];
    private readonly Stack<List<(int, GoodsKind, bool)>> _spareKeyLists = [];
    private readonly List<(int, GoodsKind, bool)> _goodsKeys = [];
    private readonly List<(int, Vector2)> _waterKeys = [];
    private readonly List<int> _otherKeys = [];
    private readonly List<(Vector2, (float, int))> _groundOrder = [];
    private readonly PriorityQueue<Vector2, (float Distance, int Index)> _ground = new(GroundOrder);
    private IReadOnlyList<StockedGoods>? _indexedStock;
    // The Water points by chunk, as list indices, made again only when the list changes.
    private IReadOnlyList<WaterPoint>? _indexedWater;
    private readonly Dictionary<ChunkCoord, List<int>> _waterByChunk = [];
    private readonly List<int> _waterNear = [];
    private StockList _stockList = StockList.Empty;
    private Vector2[]? _groundCells;
    private int _groundPerRow;
    private bool[] _groundSkipped = [];
    private static readonly Comparer<(float Distance, int Index)> GroundOrder =
        Comparer<(float Distance, int Index)>.Create((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Index.CompareTo(b.Index));

    private void IndexFurniture(IReadOnlyList<FurniturePiece> pieces)
    {
        _furnitureIndex ??= new FurnitureIndex(space);
        _furnitureIndex.Update(pieces);
        _largestPiece = _furnitureIndex.LargestPiece;
    }

    public IEnumerable<FurniturePiece> FurnitureNear(Vector2 position, float range) => _furnitureIndex?.Near(position, range) ?? [];

    private sealed partial class Walker
    {
        public HashSet<Vector2> ExploredGround { get; } = [];
        public Dictionary<(int, GoodsKind, bool), KnownGoods> KnownGoods { get; } = [];
        public Dictionary<(int, Vector2), KnownWater> KnownWater { get; } = [];
        public Dictionary<int, KnownDanger> KnownDangers { get; } = [];
        public Dictionary<int, KnownVillage> KnownVillages { get; } = [];
        // The last published view of the above; cleared whenever they change.
        public KnownMap? Map { get; set; }
    }

    /// <summary>
    /// Whether a known source is worth a trip: it had Goods when last seen, or it is stale and Restock may have laid
    /// Goods there again (only in the Cafeterias and the Camping ground); a bare shelf elsewhere stays bare.
    /// </summary>
    private static bool WorthVisiting(KnownGoods goods, WorldSnapshot world) =>
        goods.Goods.Amount > 0 || goods.Stale && world.FurniturePieceById(goods.Goods.FurnitureId) is { } piece &&
            (piece.PointOfInterestId >= 0 && world.PointsOfInterest[piece.PointOfInterestId].Type == PointOfInterestType.Cafeteria ||
             piece.LandmarkId >= 0 && world.Landmarks[piece.LandmarkId].Type == LandmarkType.CampingGround);

    private Vector2 GroundAt(Vector2 position) => new(
        MathF.Floor(position.X / GroundStep) * GroundStep + GroundStep / 2,
        MathF.Floor(position.Y / GroundStep) * GroundStep + GroundStep / 2);

    private void Observe(Walker walker, WorldSnapshot world)
    {
        walker.Map = null;
        walker.ExploredGround.Add(GroundAt(walker.Position));
        var viewer = new Villager(walker.Id, walker.Position, walker.Destination, walker.Heading) { Elevation = walker.Elevation };
        // Reused between Observations: a Villager sees some thousands of pieces.
        var blockers = _blockers;
        blockers.Clear();
        var nearby = _nearby;
        nearby.Clear();
        // Only pieces this close can block a sight line (see SightBlockers.Prepare).
        foreach (var piece in FurnitureNear(walker.Position, viewer.ViewRange + _largestPiece))
        {
            var distance = space.Distance(walker.Position, piece.Position);
            if (distance > viewer.ViewRange + _largestPiece)
                continue;
            blockers.Add(piece);
            if (distance <= viewer.ViewRange)
                nearby.Add(piece);
        }
        // Sight lines are bucketed only once one is needed.
        var sightReady = false;
        bool CanSee(FurniturePiece piece)
        {
            if (!sightReady)
                _sight.Prepare(space, walker.Position, blockers, viewer.ViewRange + _largestPiece, viewer.ViewRange);
            sightReady = true;
            return RestockSight.CanSee(space, viewer, piece, _sight, world.Pillars, world.FogBanks);
        }
        // A source within reach can be inspected even when its own Furniture hides its centre.
        var seen = _seen;
        seen.Clear();
        bool Visible(FurniturePiece piece)
        {
            if (space.Distance(walker.Position, piece.Position) > viewer.ViewRange)
                return false;
            if (!seen.TryGetValue(piece, out var visible))
                seen[piece] = visible = space.Distance(walker.Position, navigation.NearestWalkable(piece.Position)) < 0.5f || CanSee(piece);
            return visible;
        }
        bool VisibleAt(Vector2 position) => Visible(new FurniturePiece(-1, FurnitureType.CoffeeTable, position, 0, 0, 0));
        // The Simulation hands out a StockList, whose index every Villager shares.
        if (!ReferenceEquals(world.Stock, _indexedStock))
        {
            _indexedStock = world.Stock;
            _stockList = world.Stock as StockList ?? new StockList(world.Stock);
        }
        var stock = _stockList;
        var knownIds = _knownIds;
        knownIds.Clear();
        foreach (var goods in walker.KnownGoods.Values)
            knownIds.Add(goods.Goods.FurnitureId);
        var nearbyAt = _nearbyAt;
        nearbyAt.Clear();
        foreach (var p in nearby)
            if (knownIds.Contains(p.Id))
                nearbyAt.Add((p.Id, p.Position));
        var knownKeys = _knownKeys;
        foreach (var list in knownKeys.Values)
        {
            list.Clear();
            _spareKeyLists.Push(list);
        }
        knownKeys.Clear();
        foreach (var key in walker.KnownGoods.Keys)
        {
            if (!knownKeys.TryGetValue(key.Item1, out var list))
                knownKeys[key.Item1] = list = _spareKeyLists.Count > 0 ? _spareKeyLists.Pop() : [];
            list.Add(key);
        }
        var emptied = _goodsKeys;
        emptied.Clear();
        foreach (var (key, goods) in walker.KnownGoods)
            if (!nearbyAt.Contains((goods.Goods.FurnitureId, goods.Position)) &&
                VisibleAt(goods.Position))
                emptied.Add(key);
        foreach (var key in emptied)
        {
            var goods = walker.KnownGoods[key];
            walker.KnownGoods[key] = goods with { Goods = goods.Goods with { Amount = 0 }, LastSeen = world.Clock, Stale = false };
        }
        foreach (var piece in nearby)
        {
            if (stock.On(piece.Id).IsEmpty && !knownKeys.ContainsKey(piece.Id))
                continue;
            if (!Visible(piece))
                continue;
            if (knownKeys.TryGetValue(piece.Id, out var keys))
                foreach (var key in keys)
                    walker.KnownGoods[key] = walker.KnownGoods[key] with
                    { Goods = walker.KnownGoods[key].Goods with { Amount = 0 }, LastSeen = world.Clock, Stale = false };
            foreach (var i in stock.On(piece.Id))
                walker.KnownGoods[(piece.Id, stock[i].Kind, stock[i].LeftBehind)] = new(piece.Position, stock[i], world.Clock);
        }
        foreach (var point in WaterNear(world.WaterPoints, walker.Position, viewer.ViewRange))
        {
            // A piece in sight range is one of the nearby ones.
            var piece = point.FurnitureId < 0
                ? new FurniturePiece(-1, FurnitureType.CoffeeTable, point.Position, 0, 0, 0)
                : world.FurniturePieceById(point.FurnitureId) is { } holder && space.Distance(walker.Position, holder.Position) <= viewer.ViewRange
                    ? holder : default;
            if (!Visible(piece))
                continue;
            var key = (point.FurnitureId, point.Position);
            bool? running = point.Kind == WaterPointKind.ShowroomTap
                ? walker.KnownWater.GetValueOrDefault(key)?.Running : true;
            walker.KnownWater[key] = new(point.Kind, point.Position, point.FurnitureId, running, world.Clock);
        }
        // Changed after each pass, not during it.
        var staleGoods = _goodsKeys;
        staleGoods.Clear();
        foreach (var (key, goods) in walker.KnownGoods)
            if (!goods.Stale && world.Clock.TotalStoreTime - goods.LastSeen.TotalStoreTime >= TimeSpan.FromHours(1))
                staleGoods.Add(key);
        foreach (var key in staleGoods)
            walker.KnownGoods[key] = walker.KnownGoods[key] with { Stale = true };
        _waterKeys.Clear();
        foreach (var (key, water) in walker.KnownWater)
            if (!water.Stale && world.Clock.TotalStoreTime - water.LastSeen.TotalStoreTime >= TimeSpan.FromHours(1))
                _waterKeys.Add(key);
        foreach (var key in _waterKeys)
            walker.KnownWater[key] = walker.KnownWater[key] with { Stale = true };
        foreach (var staff in world.Staff)
            if (world.Phase == DayPhase.ClosingTime &&
                space.Distance(walker.Position, staff.Position) <= 12 + 28 * world.LightBrightnessAt(walker.Position) + walker.Elevation * 2 &&
                VisibleAt(staff.Position))
                walker.KnownDangers[staff.Id] = new(staff.Id, staff.Position, world.Clock);
        _otherKeys.Clear();
        foreach (var (id, danger) in walker.KnownDangers)
            if (!danger.Stale && world.Clock.TotalStoreTime - danger.LastSeen.TotalStoreTime >= TimeSpan.FromHours(1))
                _otherKeys.Add(id);
        foreach (var id in _otherKeys)
            walker.KnownDangers[id] = walker.KnownDangers[id] with { Stale = true };
        foreach (var village in world.Villages)
            if ((village.VillagerIds.Count > 0 || walker.KnownVillages.ContainsKey(village.Id)) && VisibleAt(village.Position))
                walker.KnownVillages[village.Id] = new(village.Id, village.Position, world.Clock,
                    Abandoned: village.VillagerIds.Count == 0);
        _otherKeys.Clear();
        foreach (var (id, village) in walker.KnownVillages)
            if (!village.Stale && world.Clock.TotalStoreTime - village.LastSeen.TotalStoreTime >= TimeSpan.FromHours(1))
                _otherKeys.Add(id);
        foreach (var id in _otherKeys)
            walker.KnownVillages[id] = walker.KnownVillages[id] with { Stale = true };
    }

    /// <summary>The Water points within <paramref name="range"/> of <paramref name="position"/>, in list order.</summary>
    private IEnumerable<WaterPoint> WaterNear(IReadOnlyList<WaterPoint> points, Vector2 position, float range)
    {
        if (!ReferenceEquals(points, _indexedWater))
        {
            _indexedWater = points;
            _waterByChunk.Clear();
            for (var i = 0; i < points.Count; i++)
            {
                var chunk = space.ChunkOf(points[i].Position);
                if (!_waterByChunk.TryGetValue(chunk, out var inChunk))
                    _waterByChunk[chunk] = inChunk = [];
                inChunk.Add(i);
            }
        }
        var near = _waterNear;
        near.Clear();
        foreach (var chunk in space.ChunkImagesAround(position, range).Select(c => c.Chunk).Distinct())
            if (_waterByChunk.TryGetValue(chunk, out var inChunk))
                foreach (var i in inChunk)
                    if (space.Distance(position, points[i].Position) <= range)
                        near.Add(i);
        near.Sort();
        foreach (var i in near)
            yield return points[i];
    }

    private static KnownMap MapOf(Walker walker) => walker.Map ??= new()
    {
        ExploredGround = walker.ExploredGround.ToList().AsReadOnly(),
        Goods = walker.KnownGoods.Values.ToList().AsReadOnly(),
        Water = walker.KnownWater.Values.ToList().AsReadOnly(),
        Dangers = walker.KnownDangers.Values.ToList().AsReadOnly(),
        Villages = walker.KnownVillages.Values.ToList().AsReadOnly(),
    };

    private void SkipGround(Vector2 position)
    {
        var column = (position.X - GroundStep / 2) / GroundStep;
        var row = (position.Y - GroundStep / 2) / GroundStep;
        if (column < 0 || row < 0 || column != MathF.Floor(column) || row != MathF.Floor(row) || column >= _groundPerRow)
            return;
        var index = (int)row * _groundPerRow + (int)column;
        if (index < _groundCells!.Length && _groundCells[index] == position)
            _groundSkipped[index] = true;
    }

    private void Scout(Walker walker, WorldSnapshot world)
    {
        if (walker.Elevation == 0)
        {
            var lookouts = world.Landmarks.SelectMany(l => l.Lookouts).Concat(world.Villages
                .Where(v => v.PillarScaffold?.Complete == true).Select(v => v.PillarScaffold!.Lookout));
            foreach (var lookout in lookouts.Where(p => space.Distance(walker.Position, new(p.X, p.Y)) < 80)
                .OrderBy(p => space.Distance(walker.Position, new(p.X, p.Y))))
            {
                if (!walker.VisitedLookouts.Add(lookout)) continue;
                if (SendClimbing(walker, lookout)) { walker.Action = VillagerAction.Scouting; return; }
            }
        }
        if (_groundCells is null)
        {
            var cells = new List<Vector2>();
            for (float y = GroundStep / 2; y < space.Size; y += GroundStep)
                for (float x = GroundStep / 2; x < space.Size; x += GroundStep)
                    cells.Add(new Vector2(x, y));
            _groundCells = [.. cells];
            _groundPerRow = cells.Count(c => c.Y == cells[0].Y);
            _groundSkipped = new bool[cells.Count];
        }
        // Explored and unreachable ground, marked on the grid instead of looked up cell by cell.
        Array.Clear(_groundSkipped);
        // A Villager shut in a small pocket scouts only the ground under it: the rest of the Store would be tried cell
        // by cell, and fail.
        if (navigation.Pocket(walker.Position)?.Select(GroundAt).ToHashSet() is { } pocket)
            for (var i = 0; i < _groundCells.Length; i++)
                _groundSkipped[i] = !pocket.Contains(_groundCells[i]);
        foreach (var position in walker.ExploredGround)
            SkipGround(position);
        foreach (var position in walker.Unreachable)
            SkipGround(position);
        // Nearest unexplored ground first (ties in grid order), sorted only as far as needed.
        var order = _groundOrder;
        order.Clear();
        for (var i = 0; i < _groundCells.Length; i++)
            if (!_groundSkipped[i])
                order.Add((_groundCells[i], (space.Distance(walker.Position, _groundCells[i]), i)));
        var ground = _ground;
        ground.Clear();
        ground.EnqueueRange(order);
        while (ground.TryDequeue(out var position, out _))
        {
            if (!navigation.IsWalkable(position))
            {
                walker.Unreachable.Add(position);
                continue;
            }
            if (TryPlan(walker, new NeedTarget(position)))
            {
                walker.Action = VillagerAction.Scouting;
                return;
            }
        }
    }
}
