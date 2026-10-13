using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws many copies of one mesh at Store positions, grouped into one MultiMesh per chunk.
/// Around the camera focus every chunk is shown at each wrapped image within
/// <see cref="ViewReach"/>, so objects near a seam are drawn on both sides of it (ghost copies)
/// and panning across a seam never reveals a gap. Placement is refreshed only when the focus
/// moves into another chunk, and only moves instances; the instance data is built once. The
/// images are rendering-server instances rather than nodes: a Store has tens of thousands of
/// them, and nodes (with their script wrappers for the garbage collector to track) made chunk
/// crossings and collections stall frames.
/// With a <see cref="CoarseMesh"/>, chunks farther than <see cref="LodDistance"/> from the camera
/// draw it instead (level of detail); with <see cref="OccluderQuads"/>, every chunk also hides
/// what is behind those parts of its instances from rendering (occlusion culling).
/// </summary>
public partial class WrappedInstances : Node3D
{
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public Mesh? Mesh { get; set; }

    /// <summary>A simpler mesh drawn instead of <see cref="Mesh"/> for chunks far from the camera.</summary>
    [Export] public Mesh? CoarseMesh { get; set; }

    /// <summary>
    /// Distance from the camera to a chunk's centre beyond which <see cref="CoarseMesh"/> is drawn.
    /// The largest distance that keeps the 120 fps benchmark passing (ticket 06d): the floor under
    /// the focus stays detailed up to a zoom of about 165 m, and at the farthest zoom (camera about
    /// 180 m above the floor) only the nearest chunks are. Farther switches (220, 240, 265 and
    /// 525 m were measured) cost the far view too much; the coarse meshes keep the silhouettes, so
    /// the switch shows mostly as a change of material.
    /// </summary>
    [Export] public float LodDistance { get; set; } = 210f;

    /// <summary>
    /// Distance from the camera to a chunk's centre beyond which it is not drawn at all: the Edge fade
    /// hides it. The Observer camera at its farthest zoom sees floor up to about 510 m away, so
    /// this leaves a chunk's half-diagonal to spare.
    /// </summary>
    [Export] public float FarDistance { get; set; } = 600f;

    /// <summary>How far from the focus content must be drawn; at least the farthest floor the camera can see.</summary>
    [Export] public float ViewReach { get; set; } = 700f;

    /// <summary>
    /// Flat rectangles in mesh space, four corners each, that hide what is behind them; every
    /// instance contributes them as occluders.
    /// </summary>
    public Vector3[] OccluderQuads { get; set; } = [];

    /// <summary>
    /// Only chunks whose centre lies within this distance of the focus occlude: they hide the most,
    /// and every occluder makes the per-frame occlusion test dearer.
    /// </summary>
    [Export] public float OccluderReach { get; set; } = 160f;

    // Switching meshes waits this far past LodDistance, so a chunk does not flip back and forth.
    private const float LodMargin = 6f;

    // Images newly needed when the focus moves into another chunk lie at the far edge of the reach,
    // beyond what is drawn; creating them is spread over frames so a crossing does not stall any.
    // The budget is shared by every instance group, so it bounds the work of the whole frame.
    private const double CreationMillisecondsPerFrame = 1.0;
    // Placements stay valid while the focus moves within a chunk of padding, so when the focus
    // steps into the next chunk the instance groups may re-place over a few frames rather than all
    // in one. Shared by every group, like the creation budget.
    private const double PlacementMillisecondsPerFrame = 1.0;
    private static readonly System.Diagnostics.Stopwatch CreationTime = new();
    private static readonly System.Diagnostics.Stopwatch PlacementTime = new();
    private static ulong _creationFrame;

    private readonly Dictionary<ChunkCoord, ChunkImages> _chunks = new();
    // Images in use per chunk during a placement; kept, so placing allocates nothing for the collector.
    private readonly Dictionary<ChunkCoord, int> _used = new();
    private StoreSpace _space = StoreSpace.Standard;
    private (long X, long Y)? _placedAroundCell;
    private bool _placedOverview;
    // Images the last placement had no budget to create; they are created in the next frames.
    private readonly List<ChunkImage> _pending = [];

    /// <summary>Replaces all instances. Transform origins are Store positions in world space and may be unwrapped.</summary>
    public void SetInstances(StoreSpace space, IEnumerable<Transform3D> transforms) =>
        SetInstances(space, transforms.Select(t => (t, Colors.White)), useColors: false);

    /// <summary>
    /// Replaces all instances, each with its own colour (materials that use vertex colour as
    /// albedo are tinted by it). Transform origins are Store positions and may be unwrapped.
    /// </summary>
    public void SetInstances(StoreSpace space, IEnumerable<(Transform3D Transform, Color Color)> instances) =>
        SetInstances(space, instances, useColors: true);

    private void SetInstances(StoreSpace space, IEnumerable<(Transform3D Transform, Color Color)> instances, bool useColors)
    {
        FreeImages();
        _chunks.Clear();
        _space = space;
        _placedAroundCell = null;
        _placedOverview = false;
        _pending.Clear();

        var byChunk = instances
            .Select(i => i with { Transform = i.Transform with { Origin = space.Wrap(i.Transform.Origin.ToStore()).ToWorld(i.Transform.Origin.Y) } })
            .GroupBy(i => space.ChunkOf(i.Transform.Origin.ToStore()));
        var stride = useColors ? 16 : 12;
        foreach (var group in byChunk)
        {
            var chunkInstances = group.ToArray();
            // Filled in one go: per-instance setters are far slower for hundreds of thousands of pieces.
            var buffer = new float[chunkInstances.Length * stride];
            for (var i = 0; i < chunkInstances.Length; i++)
            {
                var (t, color) = chunkInstances[i];
                var o = i * stride;
                buffer[o + 0] = t.Basis.X.X; buffer[o + 1] = t.Basis.Y.X; buffer[o + 2] = t.Basis.Z.X; buffer[o + 3] = t.Origin.X;
                buffer[o + 4] = t.Basis.X.Y; buffer[o + 5] = t.Basis.Y.Y; buffer[o + 6] = t.Basis.Z.Y; buffer[o + 7] = t.Origin.Y;
                buffer[o + 8] = t.Basis.X.Z; buffer[o + 9] = t.Basis.Y.Z; buffer[o + 10] = t.Basis.Z.Z; buffer[o + 11] = t.Origin.Z;
                if (useColors)
                {
                    buffer[o + 12] = color.R; buffer[o + 13] = color.G; buffer[o + 14] = color.B; buffer[o + 15] = color.A;
                }
            }
            _chunks[group.Key] = new ChunkImages(
                NewMultiMesh(Mesh, buffer, chunkInstances.Length, useColors),
                CoarseMesh is null ? null : NewMultiMesh(CoarseMesh, buffer, chunkInstances.Length, useColors),
                OccluderQuads.Length == 0 ? null : NewOccluder(chunkInstances.Select(i => i.Transform)));
        }
    }

    private static MultiMesh NewMultiMesh(Mesh? mesh, float[] buffer, int count, bool useColors)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = useColors,
            Mesh = mesh,
            InstanceCount = count,
        };
        multiMesh.Buffer = buffer;
        return multiMesh;
    }

    /// <summary>One occluder for a whole chunk: every occluder rectangle of every instance, as two triangles each.</summary>
    private ArrayOccluder3D NewOccluder(IEnumerable<Transform3D> transforms)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        foreach (var transform in transforms)
            for (var quad = 0; quad < OccluderQuads.Length; quad += 4)
            {
                var first = vertices.Count;
                for (var corner = 0; corner < 4; corner++)
                    vertices.Add(transform * OccluderQuads[quad + corner]);
                indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            }
        var occluder = new ArrayOccluder3D();
        occluder.SetArrays([.. vertices], [.. indices]);
        return occluder;
    }

    public override void _EnterTree() => StoreOverview.Changed += OnOverviewChanged;

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged)
            foreach (var images in _chunks.Values)
                foreach (var image in images.Images)
                    image.SetTreeVisible(IsVisibleInTree());
        else if (what == NotificationPredelete)
            FreeImages();
    }

    private void FreeImages()
    {
        foreach (var images in _chunks.Values)
        {
            foreach (var image in images.Images)
                image.Free();
            images.Images.Clear();
        }
    }

    public override void _ExitTree() => StoreOverview.Changed -= OnOverviewChanged;

    private void OnOverviewChanged(bool enabled)
    {
        foreach (var images in _chunks.Values)
            foreach (var image in images.Images)
                ShowRanges(image, enabled);
        // Force a fresh placement in the new mode.
        _placedAroundCell = null;
        _placedOverview = false;
    }

    public override void _Process(double delta)
    {
        if (_chunks.Count == 0)
            return;
        if (StoreOverview.Enabled)
        {
            if (!_placedOverview)
                PlaceOverview();
            return;
        }
        // Runs after the camera rig (see CameraRig.ProcessPriority), so a recentring across a
        // seam changes the focus cell and the images are re-placed in the same frame.
        var focus = Rig.Focus.ToStore();
        var cell = ((long)Math.Floor(focus.X / _space.ChunkSize), (long)Math.Floor(focus.Y / _space.ChunkSize));
        if (cell == _placedAroundCell)
        {
            if (_pending.Count > 0)
                CreatePending(focus);
            return;
        }
        // The first placement after a rebuild, and a jump across a seam, place everything at once;
        // a step into the next chunk is spread out.
        var budgeted = _placedAroundCell is { } placed && Math.Abs(placed.X - cell.Item1) <= 1 && Math.Abs(placed.Y - cell.Item2) <= 1;
        StartFrameBudgets();
        if (budgeted && PlacementTime.Elapsed.TotalMilliseconds >= PlacementMillisecondsPerFrame)
            return;
        _placedAroundCell = cell;
        PlacementTime.Start();
        PlaceImages(focus, budgeted);
        PlacementTime.Stop();
    }

    private void PlaceImages(System.Numerics.Vector2 focus, bool budgeted)
    {
        _pending.Clear();
        // Padded by one chunk so the placement stays valid while the focus moves within its chunk.
        var used = _used;
        used.Clear();
        foreach (var image in _space.ChunkImagesAround(focus, ViewReach + _space.ChunkSize))
        {
            if (!_chunks.TryGetValue(image.Chunk, out var images))
                continue;
            var index = used.GetValueOrDefault(image.Chunk);
            if (index == images.Images.Count)
            {
                if (budgeted && CreationTime.Elapsed.TotalMilliseconds >= CreationMillisecondsPerFrame)
                {
                    _pending.Add(image);
                    continue;
                }
                CreationTime.Start();
                images.Images.Add(NewImage(images));
                CreationTime.Stop();
            }
            used[image.Chunk] = index + 1;
            Place(images.Images[index], image, focus);
        }
        foreach (var (chunk, images) in _chunks)
            for (var i = used.GetValueOrDefault(chunk); i < images.Images.Count; i++)
                images.Images[i].Show(false);
    }

    /// <summary>Creates and places, within this frame's budget, images the last placement had to leave out.</summary>
    private void CreatePending(System.Numerics.Vector2 focus)
    {
        StartFrameBudgets();
        var created = 0;
        while (created < _pending.Count && CreationTime.Elapsed.TotalMilliseconds < CreationMillisecondsPerFrame)
        {
            var image = _pending[created++];
            var images = _chunks[image.Chunk];
            CreationTime.Start();
            images.Images.Add(NewImage(images));
            CreationTime.Stop();
            Place(images.Images[^1], image, focus);
        }
        _pending.RemoveRange(0, created);
    }

    private static void StartFrameBudgets()
    {
        if (_creationFrame == Engine.GetProcessFrames())
            return;
        _creationFrame = Engine.GetProcessFrames();
        CreationTime.Reset();
        PlacementTime.Reset();
    }

    private void Place(Image placed, ChunkImage image, System.Numerics.Vector2 focus)
    {
        // Unmoved images keep their transform, so occlusion culling need not rebuild them.
        placed.MoveTo(image.Offset.ToWorld());
        placed.Show(true);
        var centre = _space.ChunkOrigin(image.Chunk) + image.Offset + new System.Numerics.Vector2(_space.ChunkSize / 2f);
        placed.ShowOccluder(System.Numerics.Vector2.Distance(centre, focus) <= OccluderReach);
    }

    /// <summary>The <see cref="StoreOverview"/>: every chunk once, at its own place, with no ghost copies.</summary>
    private void PlaceOverview()
    {
        _placedOverview = true;
        foreach (var images in _chunks.Values)
        {
            if (images.Images.Count == 0)
                images.Images.Add(NewImage(images));
            for (var i = 0; i < images.Images.Count; i++)
            {
                var image = images.Images[i];
                image.MoveTo(Vector3.Zero);
                image.Show(i == 0);
                image.ShowOccluder(false);
            }
        }
    }

    /// <summary>One wrapped image of a chunk: its detailed and coarse instances and its occluder.</summary>
    private Image NewImage(ChunkImages images)
    {
        var scenario = GetWorld3D().Scenario;
        Rid Instance(Rid resource)
        {
            var instance = RenderingServer.InstanceCreate();
            RenderingServer.InstanceSetBase(instance, resource);
            RenderingServer.InstanceSetScenario(instance, scenario);
            return instance;
        }
        var image = new Image(Instance(images.Detail.GetRid()),
            images.Coarse is null ? null : Instance(images.Coarse.GetRid()),
            images.Occluder is null ? null : Instance(images.Occluder.GetRid()),
            GlobalTransform, IsVisibleInTree());
        ShowRanges(image, StoreOverview.Enabled);
        return image;
    }

    /// <summary>
    /// Normally: the detailed mesh near the camera, the coarse one farther out, nothing past
    /// <see cref="FarDistance"/>. In the overview: the coarse mesh (or the detailed one if there is
    /// none) at every distance, since the camera is far above the whole Store.
    /// </summary>
    private void ShowRanges(Image image, bool overview)
    {
        static void Range(Rid instance, float begin, float end, float beginMargin, float endMargin) =>
            RenderingServer.InstanceGeometrySetVisibilityRange(instance, begin, end, beginMargin, endMargin,
                RenderingServer.VisibilityRangeFadeMode.Disabled);
        var (detail, coarse) = (image.Detail, image.Coarse);
        if (overview)
        {
            image.ShowDetail(coarse is null);
            Range(detail, 0f, 0f, 0f, 0f);
            if (coarse is { } overviewCoarse)
                Range(overviewCoarse, 0f, 0f, 0f, 0f);
            return;
        }
        image.ShowDetail(true);
        Range(detail, 0f, coarse is null ? FarDistance : LodDistance, 0f, coarse is null ? 0f : LodMargin);
        if (coarse is { } distant)
            Range(distant, LodDistance, FarDistance, LodMargin, 0f);
    }

    /// <summary>
    /// One placed image: rendering-server instances of a chunk's detailed and coarse MultiMeshes and
    /// of its occluder. It remembers its position and visibility, so re-placing reaches the engine
    /// only for what changed: tens of thousands of unchanged images cost no engine calls.
    /// </summary>
    private sealed class Image
    {
        private readonly Rid? _occluder;
        private readonly Transform3D _parent;
        private Vector3 _position;
        private bool _placedVisible = true;
        private bool _occluderWanted = true;
        private bool _detailWanted = true;
        private bool _treeVisible;
        private (bool Detail, bool Coarse, bool Occluder)? _applied;

        public Image(Rid detail, Rid? coarse, Rid? occluder, Transform3D parent, bool treeVisible)
        {
            Detail = detail;
            Coarse = coarse;
            _occluder = occluder;
            _parent = parent;
            _treeVisible = treeVisible;
            SetTransform();
            Apply();
        }

        public Rid Detail { get; }
        public Rid? Coarse { get; }

        public void MoveTo(Vector3 position)
        {
            if (position == _position)
                return;
            _position = position;
            SetTransform();
        }

        public void Show(bool visible) => Apply(_placedVisible = visible);

        public void ShowOccluder(bool visible) => Apply(_occluderWanted = visible);

        public void ShowDetail(bool visible) => Apply(_detailWanted = visible);

        public void SetTreeVisible(bool visible) => Apply(_treeVisible = visible);

        public void Free()
        {
            RenderingServer.FreeRid(Detail);
            if (Coarse is { } coarse)
                RenderingServer.FreeRid(coarse);
            if (_occluder is { } occluder)
                RenderingServer.FreeRid(occluder);
        }

        private void SetTransform()
        {
            var transform = _parent * new Transform3D(Basis.Identity, _position);
            RenderingServer.InstanceSetTransform(Detail, transform);
            if (Coarse is { } coarse)
                RenderingServer.InstanceSetTransform(coarse, transform);
            if (_occluder is { } occluder)
                RenderingServer.InstanceSetTransform(occluder, transform);
        }

        private void Apply(bool _ = false)
        {
            var shown = _placedVisible && _treeVisible;
            var wanted = (Detail: shown && _detailWanted, Coarse: shown, Occluder: shown && _occluderWanted);
            var applied = _applied;
            if (applied == wanted)
                return;
            _applied = wanted;
            if (applied?.Detail != wanted.Detail)
                RenderingServer.InstanceSetVisible(Detail, wanted.Detail);
            if (Coarse is { } coarse && applied?.Coarse != wanted.Coarse)
                RenderingServer.InstanceSetVisible(coarse, wanted.Coarse);
            if (_occluder is { } occluder && applied?.Occluder != wanted.Occluder)
                RenderingServer.InstanceSetVisible(occluder, wanted.Occluder);
        }
    }

    private sealed class ChunkImages(MultiMesh detail, MultiMesh? coarse, ArrayOccluder3D? occluder)
    {
        public MultiMesh Detail { get; } = detail;
        public MultiMesh? Coarse { get; } = coarse;
        public ArrayOccluder3D? Occluder { get; } = occluder;
        public List<Image> Images { get; } = [];
    }
}
