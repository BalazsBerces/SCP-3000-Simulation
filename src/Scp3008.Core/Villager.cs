using System.Numerics;

namespace Scp3008.Core;

/// <summary>A Villager: a person trapped in the Store.</summary>
/// <param name="Id">Unique for the world.</param>
/// <param name="Position">Where the Villager stands, wrapped.</param>
/// <param name="Destination">Where the Villager is walking to, wrapped; null while standing still.</param>
/// <param name="Heading">The way the Villager faces, in radians from east (+X) towards south (+Y).</param>
public sealed record Villager(int Id, Vector2 Position, Vector2? Destination, float Heading)
{
    /// <summary>
    /// How fast a Villager walks, in metres per Store hour: far below a real walking pace, so a walk across a Display
    /// looks like a brisk walk at 1x (2.4 m per real second) rather than a blink, the Store day lasting only 10 real minutes.
    /// </summary>
    public const float WalkingSpeed = 60f;

    public Needs Needs { get; init; } = new();
    public VillagerAction Action { get; init; }
    public KnownMap KnownMap { get; init; } = new();
    public int? VillageId { get; init; }
    public Role Role { get; init; } = Role.Scavenger;
    public IReadOnlyDictionary<Role, double> Skills { get; init; } = new Dictionary<Role, double>();
    public GoodsKind? CarriedGoods { get; init; }
}

/// <summary>The Villagers in the world and their walking, each tick along the path <see cref="Navigation"/> finds.</summary>
internal sealed partial class Villagers(StoreSpace space, Navigation navigation)
{
    private readonly List<Walker> _walkers = [];
    private int _nextId;

    private sealed partial class Walker(int id, Vector2 position)
    {
        public Needs Needs { get; set; } = new();
        public int? VillageId { get; set; }
        public Role Role { get; set; } = Role.Scavenger;
        public Dictionary<Role, double> Skills { get; } = [];
        public GoodsKind? CarriedGoods { get; set; }
        public VillagerAction Action { get; set; }
        public int Id { get; } = id;
        public Vector2 Position { get; set; } = position;
        public Vector2? Destination { get; set; }
        public float Heading { get; set; }
        public List<Vector2> Waypoints { get; set; } = [];
    }

    /// <summary>Places a standing Villager at the position, or on the nearest free floor if Furniture stands there.</summary>
    public Villager Place(Vector2 position, Needs? needs = null)
    {
        var walker = new Walker(_nextId++, navigation.NearestWalkable(position));
        walker.Needs = needs ?? new Needs();
        _walkers.Add(walker);
        return View(walker);
    }

    /// <summary>Sends a Villager walking the shortest way to the destination (beside it, if Furniture stands there); a Villager with no way there stays.</summary>
    public void Send(int villagerId, Vector2 destination)
    {
        var walker = _walkers.Find(w => w.Id == villagerId) ?? throw new ArgumentException($"No Villager {villagerId}.", nameof(villagerId));
        walker.Target = null;
        walker.Action = VillagerAction.Idle;
        Plan(walker, navigation.NearestWalkable(destination));
    }

    /// <summary>Finds every walking Villager's way again, after the Furniture changed.</summary>
    public void Replan()
    {
        foreach (var walker in _walkers)
        {
            walker.Unreachable.Clear();
            if (walker.Destination is { } destination)
                Plan(walker, navigation.NearestWalkable(destination));
        }
    }

    public void Advance(double storeHours)
    {
        var stride = (float)(Villager.WalkingSpeed * storeHours);
        foreach (var walker in _walkers)
        {
            var left = stride;
            while (left > 0f && walker.Waypoints.Count > 0)
            {
                var delta = space.Delta(walker.Position, walker.Waypoints[0]);
                var distance = delta.Length();
                if (distance > 0f)
                    walker.Heading = MathF.Atan2(delta.Y, delta.X);
                if (distance <= left)
                {
                    walker.Position = walker.Waypoints[0];
                    walker.Waypoints.RemoveAt(0);
                    left -= distance;
                }
                else
                {
                    walker.Position = space.Wrap(walker.Position + delta * (left / distance));
                    left = 0f;
                }
            }
            if (walker.Waypoints.Count == 0)
                walker.Destination = null;
        }
    }

    public Villager[] Snapshot() => _walkers.Select(View).ToArray();

    private void Plan(Walker walker, Vector2 destination)
    {
        var path = navigation.FindPath(walker.Position, destination);
        walker.Waypoints = path ?? [];
        walker.Destination = path is null ? null : destination;
    }

    private static Villager View(Walker walker) => new(walker.Id, walker.Position, walker.Destination, walker.Heading)
    { Needs = walker.Needs, Action = walker.Action, KnownMap = MapOf(walker),
        VillageId = walker.VillageId, Role = walker.Role, CarriedGoods = walker.CarriedGoods,
        Skills = new System.Collections.ObjectModel.ReadOnlyDictionary<Role, double>(new Dictionary<Role, double>(walker.Skills)) };
}
