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
    private readonly List<(Vector2, (float, int))> _groundOrder = [];
    private readonly PriorityQueue<Vector2, (float Distance, int Index)> _ground = new(GroundOrder);
    private IReadOnlyList<StockedGoods>? _indexedStock;
    private ILookup<int, StockedGoods> _stockByFurniture = Array.Empty<StockedGoods>().ToLookup(g => g.FurnitureId);
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

    public IEnumerable<FurniturePiece> FurnitureNear(Vector2 position, float range) => _furnitureIndex!.Near(position, range);

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
        blockers.AddRange(FurnitureNear(walker.Position, viewer.ViewRange + _largestPiece));
        var nearby = _nearby;
        nearby.Clear();
        foreach (var piece in blockers)
            if (space.Distance(walker.Position, piece.Position) <= viewer.ViewRange)
                nearby.Add(piece);
        // Sight lines are bucketed only once one is needed.
        var sightReady = false;
        bool CanSee(FurniturePiece piece)
        {
            if (!sightReady)
                _sight.Prepare(space, walker.Position, blockers, viewer.ViewRange + _largestPiece);
            sightReady = true;
            return RestockSight.CanSee(space, viewer, piece, _sight, world.Pillars, world.FogBanks);
        }
        // A source within reach can be inspected even when its own Furniture hides its centre.
        var seen = new Dictionary<FurniturePiece, bool>();
        bool Visible(FurniturePiece piece)
        {
            if (space.Distance(walker.Position, piece.Position) > viewer.ViewRange)
                return false;
            if (!seen.TryGetValue(piece, out var visible))
                seen[piece] = visible = space.Distance(walker.Position, navigation.NearestWalkable(piece.Position)) < 0.5f || CanSee(piece);
            return visible;
        }
        bool VisibleAt(Vector2 position) => Visible(new FurniturePiece(-1, FurnitureType.CoffeeTable, position, 0, 0, 0));
        if (!ReferenceEquals(world.Stock, _indexedStock))
        {
            _indexedStock = world.Stock;
            _stockByFurniture = world.Stock.ToLookup(g => g.FurnitureId);
        }
        var stock = _stockByFurniture;
        var knownIds = walker.KnownGoods.Values.Select(g => g.Goods.FurnitureId).ToHashSet();
        var nearbyAt = nearby.Where(p => knownIds.Contains(p.Id)).Select(p => (p.Id, p.Position)).ToHashSet();
        var knownKeys = walker.KnownGoods.Keys.ToLookup(k => k.Item1);
        foreach (var (key, goods) in walker.KnownGoods.ToArray())
            if (!nearbyAt.Contains((goods.Goods.FurnitureId, goods.Position)) &&
                VisibleAt(goods.Position))
                walker.KnownGoods[key] = goods with { Goods = goods.Goods with { Amount = 0 }, LastSeen = world.Clock, Stale = false };
        foreach (var piece in nearby)
        {
            if (!stock.Contains(piece.Id) && !knownKeys.Contains(piece.Id))
                continue;
            if (!Visible(piece))
                continue;
            foreach (var key in knownKeys[piece.Id])
                walker.KnownGoods[key] = walker.KnownGoods[key] with
                { Goods = walker.KnownGoods[key].Goods with { Amount = 0 }, LastSeen = world.Clock, Stale = false };
            foreach (var goods in stock[piece.Id])
                walker.KnownGoods[(piece.Id, goods.Kind, goods.LeftBehind)] = new(piece.Position, goods, world.Clock);
        }
        foreach (var point in world.WaterPoints.Where(w => space.Distance(walker.Position, w.Position) <= viewer.ViewRange))
        {
            var piece = point.FurnitureId >= 0
                ? nearby.FirstOrDefault(p => p.Id == point.FurnitureId)
                : new FurniturePiece(-1, FurnitureType.CoffeeTable, point.Position, 0, 0, 0);
            if (!Visible(piece))
                continue;
            var key = (point.FurnitureId, point.Position);
            bool? running = point.Kind == WaterPointKind.ShowroomTap
                ? walker.KnownWater.GetValueOrDefault(key)?.Running : true;
            walker.KnownWater[key] = new(point.Kind, point.Position, point.FurnitureId, running, world.Clock);
        }
        foreach (var (key, goods) in walker.KnownGoods.ToArray())
            if (world.Clock.TotalStoreTime - goods.LastSeen.TotalStoreTime >= TimeSpan.FromHours(1))
                walker.KnownGoods[key] = goods with { Stale = true };
        foreach (var (key, water) in walker.KnownWater.ToArray())
            if (world.Clock.TotalStoreTime - water.LastSeen.TotalStoreTime >= TimeSpan.FromHours(1))
                walker.KnownWater[key] = water with { Stale = true };
        foreach (var staff in world.Staff)
            if (world.Phase == DayPhase.ClosingTime &&
                space.Distance(walker.Position, staff.Position) <= 12 + 28 * world.LightBrightnessAt(walker.Position) + walker.Elevation * 2 &&
                VisibleAt(staff.Position))
                walker.KnownDangers[staff.Id] = new(staff.Id, staff.Position, world.Clock);
        foreach (var (id, danger) in walker.KnownDangers.ToArray())
            if (world.Clock.TotalStoreTime - danger.LastSeen.TotalStoreTime >= TimeSpan.FromHours(1))
                walker.KnownDangers[id] = danger with { Stale = true };
        foreach (var village in world.Villages)
            if ((village.VillagerIds.Count > 0 || walker.KnownVillages.ContainsKey(village.Id)) && VisibleAt(village.Position))
                walker.KnownVillages[village.Id] = new(village.Id, village.Position, world.Clock,
                    Abandoned: village.VillagerIds.Count == 0);
        foreach (var (id, village) in walker.KnownVillages.ToArray())
            if (world.Clock.TotalStoreTime - village.LastSeen.TotalStoreTime >= TimeSpan.FromHours(1))
                walker.KnownVillages[id] = village with { Stale = true };
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
