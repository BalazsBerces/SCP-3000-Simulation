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
    private IReadOnlyList<FurniturePiece>? _indexedFurniture;
    private ILookup<ChunkCoord, FurniturePiece>? _furnitureByChunk;
    private float _largestPiece;

    private void IndexFurniture(IReadOnlyList<FurniturePiece> pieces)
    {
        if (ReferenceEquals(pieces, _indexedFurniture))
            return;
        _indexedFurniture = pieces;
        _furnitureByChunk = pieces.ToLookup(p => space.ChunkOf(p.Position));
        _largestPiece = pieces.Select(p => FurnitureCatalog.Spec(p))
            .Select(s => new Vector3(s.Width, s.Depth, s.Height).Length()).DefaultIfEmpty().Max();
    }

    public IEnumerable<FurniturePiece> FurnitureNear(Vector2 position, float range) =>
        space.ChunkImagesAround(position, range).Select(c => c.Chunk).Distinct()
            .SelectMany(c => _furnitureByChunk![c]);

    private sealed partial class Walker
    {
        public HashSet<Vector2> ExploredGround { get; } = [];
        public Dictionary<(int, GoodsKind, bool), KnownGoods> KnownGoods { get; } = [];
        public Dictionary<(int, Vector2), KnownWater> KnownWater { get; } = [];
        public Dictionary<int, KnownDanger> KnownDangers { get; } = [];
        public Dictionary<int, KnownVillage> KnownVillages { get; } = [];
    }

    private Vector2 GroundAt(Vector2 position) => new(
        MathF.Floor(position.X / GroundStep) * GroundStep + GroundStep / 2,
        MathF.Floor(position.Y / GroundStep) * GroundStep + GroundStep / 2);

    private void Observe(Walker walker, WorldSnapshot world)
    {
        walker.ExploredGround.Add(GroundAt(walker.Position));
        var viewer = new Villager(walker.Id, walker.Position, walker.Destination, walker.Heading) { Elevation = walker.Elevation };
        var blockers = FurnitureNear(walker.Position, viewer.ViewRange + _largestPiece).ToArray();
        var nearby = blockers.Where(p => space.Distance(walker.Position, p.Position) <= viewer.ViewRange).ToArray();
        // A source within reach can be inspected even when its own Furniture hides its centre.
        bool Visible(FurniturePiece piece) => space.Distance(walker.Position, piece.Position) <= viewer.ViewRange &&
            (space.Distance(walker.Position, navigation.NearestWalkable(piece.Position)) < 0.5f ||
             RestockSight.CanSee(space, viewer, piece, blockers, world.Pillars, world.FogBanks));
        bool VisibleAt(Vector2 position) => Visible(new FurniturePiece(-1, FurnitureType.CoffeeTable, position, 0, 0, 0));
        var stock = world.Stock.ToLookup(g => g.FurnitureId);
        foreach (var (key, goods) in walker.KnownGoods.ToArray())
            if (!nearby.Any(p => p.Id == goods.Goods.FurnitureId && p.Position == goods.Position) &&
                VisibleAt(goods.Position))
                walker.KnownGoods[key] = goods with { Goods = goods.Goods with { Amount = 0 }, LastSeen = world.Clock, Stale = false };
        foreach (var piece in nearby)
        {
            if (!stock.Contains(piece.Id) && !walker.KnownGoods.Keys.Any(k => k.Item1 == piece.Id))
                continue;
            if (!Visible(piece))
                continue;
            foreach (var key in walker.KnownGoods.Keys.Where(k => k.Item1 == piece.Id).ToArray())
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

    private static KnownMap MapOf(Walker walker) => new()
    {
        ExploredGround = walker.ExploredGround.ToList().AsReadOnly(),
        Goods = walker.KnownGoods.Values.ToList().AsReadOnly(),
        Water = walker.KnownWater.Values.ToList().AsReadOnly(),
        Dangers = walker.KnownDangers.Values.ToList().AsReadOnly(),
        Villages = walker.KnownVillages.Values.ToList().AsReadOnly(),
    };

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
        var ground = new List<Vector2>();
        for (float y = GroundStep / 2; y < space.Size; y += GroundStep)
            for (float x = GroundStep / 2; x < space.Size; x += GroundStep)
            {
                var position = new Vector2(x, y);
                if (!walker.ExploredGround.Contains(position) && !walker.Unreachable.Contains(position))
                    ground.Add(position);
            }
        foreach (var position in ground.OrderBy(p => space.Distance(walker.Position, p)))
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
