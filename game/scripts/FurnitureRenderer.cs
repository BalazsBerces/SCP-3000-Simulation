using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws every piece of Furniture in the snapshot with instancing: one <see cref="WrappedInstances"/>
/// per Furniture type and variant, which groups the pieces into one MultiMesh per chunk and shows each chunk
/// at its wrapped images around the camera, so Furniture near a seam is drawn on both sides of it.
/// Beyond a zoom threshold every piece draws its coarse mesh (level of detail, see
/// <see cref="FurnitureDetail"/>; this node keeps it in step with the camera), and solid, tall
/// Furniture hides what stands behind it (occlusion culling, switched on in the project settings).
/// User argument after "--": --lod-zoom=metres sets <see cref="FurnitureDetail.SwitchZoom"/>
/// (to find the threshold with the benchmark), and --map-height=metres sets <see cref="FurnitureDetail.MapHeight"/>.
/// Landmark Furniture is drawn by its own instance groups, out to <see cref="StoreVisibility.LandmarkDistance"/>,
/// so a whole Landmark shows from afar (until the Edge fade) and on both sides of a Seam.
/// Rebuilt whenever a new world is created. View only: it never touches the simulation.
/// </summary>
public partial class FurnitureRenderer : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public StoreVisibility Visibility { get; set; } = null!;
    private IReadOnlyList<FurniturePiece>? _builtFurniture;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--lod-zoom="))
                FurnitureDetail.SwitchZoom = float.Parse(arg["--lod-zoom=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--map-height="))
                FurnitureDetail.MapHeight = float.Parse(arg["--map-height=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        Host.WorldCreated += Rebuild;
        Host.DomainEventReceived += OnDomainEvent;
        FurnitureDetail.Changed += ShowDetail;
        FurnitureDetail.Update(Rig.Distance, Rig.Camera.GlobalPosition.Y);
        ShowDetail(FurnitureDetail.Coarse);
        Rebuild(Host.Snapshot);
    }

    public override void _ExitTree()
    {
        foreach (var node in _work.Values) node.QueueFree();
        _work.Clear();
        foreach (var fall in _falls.Values) fall.Node.QueueFree();
        _falls.Clear();
        Host.WorldCreated -= Rebuild;
        Host.DomainEventReceived -= OnDomainEvent;
        FurnitureDetail.Changed -= ShowDetail;
    }

    // Runs after the camera rig (CameraRig.ProcessPriority), so the level matches this frame's zoom.
    public override void _Process(double delta)
    {
        FurnitureDetail.Update(Rig.Distance, Rig.Camera.GlobalPosition.Y);
        var world = Host.Snapshot;
        if (!ReferenceEquals(_builtFurniture, world.Furniture))
        {
            var moving = world.FurnitureTasks.Where(t => t.Started && t.Work != FurnitureWork.Disassemble)
                .Select(t => t.FurnitureId).Concat(_falls.Keys).ToHashSet();
            // While pieces are carried the Furniture changes every step, but only those pieces: the stationary rest stands.
            // The pieces that changed, or started or stopped moving, are shown anew in their chunks; when the changes are
            // not known, every piece is compared with what is shown.
            var changes = WorldSnapshot.FurnitureChanges(_builtFurniture, world.Furniture);
            var touched = changes is null
                ? Differences(world, moving)
                : changes.SelectMany(c => c.Before is { } before ? [before.Id, c.After.Id] : new[] { c.After.Id });
            Follow(world, touched.Concat(moving.Except(_moving)).Concat(_moving.Except(moving)), moving);
            _moving = moving;
            _builtFurniture = world.Furniture;
        }
        ShowFurnitureWork(world);
        ShowFalls(world);
    }

    private HashSet<int> _moving = [];
    // The stationary pieces shown, by id and by instance group and chunk, and the groups' nodes.
    private readonly Dictionary<int, FurniturePiece> _shown = [];
    private readonly Dictionary<(GroupKey Group, ChunkCoord Chunk), Dictionary<int, FurniturePiece>> _shownByChunk = [];
    private readonly Dictionary<GroupKey, WrappedInstances> _groups = [];
    // The shown pieces the rack decks and the lettering are made from (LetteringFollows), a small part of the Store.
    private readonly Dictionary<int, FurniturePiece> _lettered = [];
    private PointOfInterestDetails? _details;

    private readonly record struct GroupKey(FurnitureType Type, int Variant, bool Landmark)
    {
        public static GroupKey Of(FurniturePiece piece) => new(piece.Type, piece.Variant, piece.LandmarkId >= 0);
    }

    /// <summary>Every piece shown otherwise than it is now: changed, added, taken away, or now moving or standing.</summary>
    private List<int> Differences(WorldSnapshot world, HashSet<int> moving)
    {
        var differences = new List<int>();
        var ids = new HashSet<int>();
        foreach (var piece in world.Furniture)
        {
            ids.Add(piece.Id);
            var shown = _shown.TryGetValue(piece.Id, out var found) ? found : (FurniturePiece?)null;
            if (moving.Contains(piece.Id) ? shown is not null : shown != piece)
                differences.Add(piece.Id);
        }
        differences.AddRange(_shown.Keys.Where(id => !ids.Contains(id)));
        return differences;
    }

    /// <summary>Shows the pieces with these ids as they stand now (moving ones not at all), re-placing only their chunks.</summary>
    private void Follow(WorldSnapshot world, IEnumerable<int> ids, HashSet<int> moving)
    {
        var dirty = new HashSet<(GroupKey, ChunkCoord)>();
        var (decks, lettering) = (false, false);
        foreach (var id in ids.Distinct())
        {
            var now = moving.Contains(id) ? null : world.FurniturePieceById(id);
            var was = _shown.TryGetValue(id, out var shown) ? shown : (FurniturePiece?)null;
            if (now == was)
                continue;
            if (was is { } before)
            {
                var key = (GroupKey.Of(before), ChunkOf(world.Space, before));
                _shownByChunk[key].Remove(id);
                dirty.Add(key);
                _shown.Remove(id);
                _lettered.Remove(id);
                decks |= DecksFollow(before);
                lettering |= LetteringFollows(before);
            }
            if (now is { } after)
            {
                var key = (GroupKey.Of(after), ChunkOf(world.Space, after));
                if (!_shownByChunk.TryGetValue(key, out var inChunk))
                    _shownByChunk[key] = inChunk = [];
                inChunk[id] = after;
                dirty.Add(key);
                _shown[id] = after;
                if (LetteringFollows(after))
                    _lettered[id] = after;
                decks |= DecksFollow(after);
                lettering |= LetteringFollows(after);
            }
        }
        foreach (var (group, chunk) in dirty)
        {
            if (!_groups.TryGetValue(group, out var node))
            {
                node = NewGroup(group, world.Space);
                AddChild(node);
                node.SetInstances(world.Space, Array.Empty<(Transform3D, Color)>());
                _groups[group] = node;
            }
            var pieces = _shownByChunk[(group, chunk)];
            node.SetChunkInstances(chunk, pieces.Values.Select(p => (Placement(p), Colour(world, p))).ToArray());
            if (pieces.Count == 0)
                _shownByChunk.Remove((group, chunk));
        }
        if (decks || lettering)
        {
            var shownWorld = world with { Furniture = Array.AsReadOnly(_lettered.Values.ToArray()) };
            if (decks)
            {
                _decks?.QueueFree();
                _decks = AddRackDecks(shownWorld);
            }
            if (lettering)
                BuildLettering(shownWorld);
        }
    }

    // The chunk WrappedInstances files a piece under.
    private static ChunkCoord ChunkOf(StoreSpace space, FurniturePiece piece) => space.ChunkOf(space.Wrap(Placement(piece).Origin.ToStore()));

    // The Landmark rack decks stand on the Rack canyon's racks under its boxes.
    private static bool DecksFollow(FurniturePiece piece) =>
        piece.LandmarkId >= 0 && piece.Type is FurnitureType.PalletRack or FurnitureType.FlatPackBox;

    // The lettering (PointOfInterestDetails) is on these pieces, and the price tags name the Landmark's products.
    private static bool LetteringFollows(FurniturePiece piece) => piece.LandmarkId >= 0 || piece.PointOfInterestId >= 0 ||
        piece.Type is FurnitureType.TrailSign or FurnitureType.CampBanner or FurnitureType.PriceTag;

    private void ShowDetail(bool coarse) => Rig.Camera.CullMask = FurnitureDetail.Mask(Rig.Camera.CullMask);

    private void Rebuild(WorldSnapshot snapshot)
    {
        _builtFurniture = snapshot.Furniture;
        _moving = [];
        var started = Time.GetTicksMsec();
        foreach (var child in GetChildren())
            child.QueueFree();
        _shown.Clear();
        _lettered.Clear();
        _shownByChunk.Clear();
        _groups.Clear();
        _decks = null;
        _details = null;
        foreach (var piece in snapshot.Furniture)
        {
            _shown[piece.Id] = piece;
            if (LetteringFollows(piece))
                _lettered[piece.Id] = piece;
            var key = (GroupKey.Of(piece), ChunkOf(snapshot.Space, piece));
            if (!_shownByChunk.TryGetValue(key, out var inChunk))
                _shownByChunk[key] = inChunk = [];
            inChunk[piece.Id] = piece;
        }
        foreach (var group in snapshot.Furniture.GroupBy(GroupKey.Of))
        {
            var instances = NewGroup(group.Key, snapshot.Space);
            AddChild(instances);
            instances.SetInstances(snapshot.Space, group.Select(p => (Placement(p), Colour(snapshot, p))));
            _groups[group.Key] = instances;
        }
        var goods = new ShelfGoods { Name = "ShelfGoods", Host = Host, Rig = Rig, Visibility = Visibility };
        AddChild(goods);
        goods.Build(snapshot);
        _decks = AddRackDecks(snapshot);
        BuildLettering(snapshot);
        FurnitureDetail.DrawBoth(3);
        GD.Print($"Furniture: {snapshot.Furniture.Count} pieces placed in {Time.GetTicksMsec() - started} ms; " +
                 $"Landmarks: {string.Join(", ", snapshot.Landmarks.Select(l => $"{l.Type} in {snapshot.Sectors[l.SectorId].Name} at {l.Centre.X:0},{l.Centre.Y:0}, {l.Height:0} m"))}.");
    }

    private WrappedInstances NewGroup(GroupKey group, StoreSpace space)
    {
        var name = group.Variant == 0 ? group.Type.ToString() : $"{group.Type}-{group.Variant}";
        return new WrappedInstances
        {
            Name = group.Landmark ? $"Landmark-{name}" : name,
            Rig = Rig,
            FarDistance = group.Landmark ? Visibility.LandmarkDistance : Visibility.FurnitureDistance,
            ViewReach = group.Landmark
                ? Visibility.LandmarkPlacementReach(Rig, space.ChunkSize)
                : Visibility.PlacementReach(Rig, space.ChunkSize),
            Mesh = FurnitureMeshes.For(group.Type, group.Variant, group.Landmark),
            CoarseMesh = FurnitureMeshes.CoarseFor(group.Type, group.Variant, group.Landmark),
            // Landmark Furniture hides nothing: the panels of thousands of stacked, tilted and touching
            // pieces stalled the first render with occlusion culling on, as the crossing pantry panels did.
            OccluderQuads = group.Landmark ? [] : FurnitureMeshes.OccluderQuads(group.Type, group.Variant),
        };
    }

    /// <summary>The Point of interest lettering, made anew for the shown pieces.</summary>
    private void BuildLettering(WorldSnapshot snapshot)
    {
        _details?.QueueFree();
        _details = new PointOfInterestDetails { Rig = Rig };
        AddChild(_details);
        _details.Build(snapshot);
    }

    private WrappedInstances? _decks;

    /// <summary>
    /// A piece's instance colour: its tint, and for a clock of the clock wall its offset from the time to Closing time,
    /// as a fraction of a day in the alpha, which turns its hands (furniture.gdshader). Damage darkens a piece.
    /// </summary>
    private static Color Colour(WorldSnapshot snapshot, FurniturePiece piece)
    {
        var colour = FurnitureMeshes.TintFor(piece).Darkened(piece.Damage * 0.45f);
        if (piece.Type == FurnitureType.WallClock && piece.LandmarkId >= 0
            && snapshot.Landmarks[piece.LandmarkId].ClockOffsets.TryGetValue(piece.Id, out var offset))
            colour.A = (float)(offset.TotalSeconds / 86400.0);
        return colour;
    }

    /// <summary>
    /// The live fish in the Aquarium aisles' tanks (Goods, not Furniture): one instance group per tank size and kind of fish,
    /// each tank drawing its own kind's school (<see cref="FurnitureMeshes.FishSchool"/>). Fish only show up close (out to
    /// <see cref="FishDistance"/>), cast no shadow, and have no coarse mesh, so a coarse or overview view draws none.
    /// </summary>
    internal static void AddFish(Node3D parent, WorldSnapshot snapshot, CameraRig rig, StoreVisibility visibility)
    {
        var tanks = snapshot.Furniture.Where(p => p.Type == FurnitureType.FishTank).ToDictionary(p => p.Id);
        foreach (var (variant, kind, placements) in FishSchools(snapshot, tanks))
        {
            var instances = FishInstances(variant, kind, snapshot, rig, visibility);
            parent.AddChild(instances);
            instances.SetInstances(snapshot.Space, placements);
        }
    }

    /// <summary>The schools to show, one per tank variant and fish kind, with the stocked tanks they swim in.</summary>
    internal static IEnumerable<(int Variant, FishKind Kind, (Transform3D, Color)[] Placements)> FishSchools(
        WorldSnapshot snapshot, IReadOnlyDictionary<int, FurniturePiece> tanks)
    {
        var stockedTanks = snapshot.Stock.Where(g => g.Kind == GoodsKind.LiveFish && g.Amount > 0).Select(g => g.FurnitureId).ToHashSet();
        foreach (var group in snapshot.TankFish.Where(f => tanks.ContainsKey(f.TankId) && stockedTanks.Contains(f.TankId))
                     .GroupBy(f => (tanks[f.TankId].Variant, f.Kind)))
        {
            var colour = FurnitureMeshes.FishColour(group.Key.Kind);
            yield return (group.Key.Variant, group.Key.Kind, group.Select(f => (Placement(tanks[f.TankId]), colour)).ToArray());
        }
    }

    /// <summary>An empty instance group for one school (<see cref="FishSchools"/>).</summary>
    internal static WrappedInstances FishInstances(int variant, FishKind kind, WorldSnapshot snapshot, CameraRig rig, StoreVisibility visibility) => new()
    {
        Name = $"Fish-{kind}-{variant}",
        Rig = rig,
        FarDistance = FishDistance,
        ViewReach = visibility.PlacementReach(rig, snapshot.Space.ChunkSize),
        Mesh = FurnitureMeshes.FishSchool(variant, kind),
        CoarseMesh = new ArrayMesh(),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>How far from the camera the fish in the tanks show, in metres; farther they are a few pixels at most.</summary>
    private const float FishDistance = 60f;

    /// <summary>
    /// The steel decks of the Rack canyon's racks, whose meshes are bare beams: on each beam level, half a deck under each box
    /// standing on it, so a level with one box is half decked, one with two fully, and an empty one not at all.
    /// </summary>
    private WrappedInstances AddRackDecks(WorldSnapshot snapshot)
    {
        var spec = FurnitureCatalog.Spec(FurnitureType.PalletRack);
        var racks = snapshot.Furniture.Where(p => p.LandmarkId >= 0 && p.Type == FurnitureType.PalletRack)
            .ToLookup(p => (Mathf.RoundToInt(p.Position.X), Mathf.RoundToInt(p.Position.Y)));
        var decks = new List<Transform3D>();
        foreach (var box in snapshot.Furniture.Where(p => p.LandmarkId >= 0 && p.Type == FurnitureType.FlatPackBox && p.Tilt == 0f && p.Roll == 0f))
        {
            var near = from dx in Enumerable.Range(-2, 5)
                       from dy in Enumerable.Range(-2, 5)
                       from rack in racks[(Mathf.RoundToInt(box.Position.X) + dx, Mathf.RoundToInt(box.Position.Y) + dy)]
                       select rack;
            foreach (var rack in near)
            {
                // The box in the rack's own frame: on one of its beam levels, within its footprint.
                var placement = Placement(rack);
                var local = placement.AffineInverse() * box.Position.ToWorld(box.Elevation);
                if (Mathf.Abs(local.X) > spec.Width / 2f || Mathf.Abs(local.Z) > spec.Depth / 2f)
                    continue;
                var level = FurnitureCatalog.PalletRackLevels.Where(l => Mathf.Abs(local.Y - l) < 0.05f).Cast<float?>().FirstOrDefault();
                if (level is null)
                    continue;
                decks.Add(placement * new Transform3D(new Basis(Vector3.Up, local.X < 0f ? Mathf.Pi : 0f), new Vector3(0f, level.Value, 0f)));
                break;
            }
        }
        var instances = new WrappedInstances
        {
            Name = "Landmark-RackDecks",
            Rig = Rig,
            FarDistance = Visibility.LandmarkDistance,
            ViewReach = Visibility.LandmarkPlacementReach(Rig, snapshot.Space.ChunkSize),
            Mesh = FurnitureMeshes.RackHalfDeck(coarse: false),
            CoarseMesh = FurnitureMeshes.RackHalfDeck(coarse: true),
        };
        AddChild(instances);
        instances.SetInstances(snapshot.Space, decks.Select(d => (d, Colors.White)));
        return instances;
    }

    /// <summary>
    /// The piece's transform in world space. Core rotations turn east towards south, which is a
    /// clockwise turn seen from above, so the Godot yaw is its negative. A tipped-over piece (Landmark
    /// Furniture) is first tilted about its width axis through its base, its front (+Z) going down; a rolled
    /// piece (a standing stone of the Armchair henge) is first rolled about its depth axis, its +width side going up.
    /// </summary>
    public static Transform3D Placement(FurniturePiece piece) =>
        new(new Basis(Vector3.Up, -piece.Rotation) * new Basis(Vector3.Right, piece.Tilt) * new Basis(Vector3.Back, piece.Roll), piece.Position.ToWorld(piece.Elevation));
}
