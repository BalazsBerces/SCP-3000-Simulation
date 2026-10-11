using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws many copies of one mesh at Store positions, grouped into one MultiMesh per chunk.
/// Around the camera focus every chunk is shown at each wrapped image within
/// <see cref="ViewReach"/>, so objects near a seam are drawn on both sides of it (ghost copies)
/// and panning across a seam never reveals a gap. Placement is refreshed only when the focus
/// moves into another chunk, and only moves nodes; the instance data is built once.
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

    /// <summary>Distance from the camera to a chunk's centre beyond which <see cref="CoarseMesh"/> is drawn.</summary>
    [Export] public float LodDistance { get; set; } = 110f;

    /// <summary>
    /// Distance from the camera to a chunk's centre beyond which it is not drawn at all: the haze
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

    private readonly Dictionary<ChunkCoord, ChunkImages> _chunks = new();
    private StoreSpace _space = StoreSpace.Standard;
    private (long X, long Y)? _placedAroundCell;
    private bool _placedOverview;

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
        foreach (var images in _chunks.Values)
            foreach (var image in images.Images)
                image.Root.QueueFree();
        _chunks.Clear();
        _space = space;
        _placedAroundCell = null;
        _placedOverview = false;

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
            return;
        _placedAroundCell = cell;
        PlaceImages(focus);
    }

    private void PlaceImages(System.Numerics.Vector2 focus)
    {
        // Padded by one chunk so the placement stays valid while the focus moves within its chunk.
        var used = new Dictionary<ChunkCoord, int>();
        foreach (var image in _space.ChunkImagesAround(focus, ViewReach + _space.ChunkSize))
        {
            if (!_chunks.TryGetValue(image.Chunk, out var images))
                continue;
            var index = used.GetValueOrDefault(image.Chunk);
            used[image.Chunk] = index + 1;
            if (index == images.Images.Count)
                images.Images.Add(NewImage(images));
            var placed = images.Images[index];
            // Unmoved images keep their transform, so occlusion culling need not rebuild them.
            var position = image.Offset.ToWorld();
            if (placed.Root.Position != position)
                placed.Root.Position = position;
            placed.Root.Visible = true;
            if (placed.Occluder is not null)
            {
                var centre = _space.ChunkOrigin(image.Chunk) + image.Offset + new System.Numerics.Vector2(_space.ChunkSize / 2f);
                placed.Occluder.Visible = System.Numerics.Vector2.Distance(centre, focus) <= OccluderReach;
            }
        }
        foreach (var (chunk, images) in _chunks)
            for (var i = used.GetValueOrDefault(chunk); i < images.Images.Count; i++)
                images.Images[i].Root.Visible = false;
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
                image.Root.Position = Vector3.Zero;
                image.Root.Visible = i == 0;
                if (image.Occluder is not null)
                    image.Occluder.Visible = false;
            }
        }
    }

    /// <summary>One wrapped image of a chunk: its detailed and coarse instances and its occluder.</summary>
    private Image NewImage(ChunkImages images)
    {
        var root = new Node3D();
        AddChild(root);
        var detail = new MultiMeshInstance3D { Multimesh = images.Detail };
        root.AddChild(detail);
        MultiMeshInstance3D? coarse = null;
        if (images.Coarse is not null)
            root.AddChild(coarse = new MultiMeshInstance3D { Multimesh = images.Coarse });
        OccluderInstance3D? occluder = null;
        if (images.Occluder is not null)
            root.AddChild(occluder = new OccluderInstance3D { Occluder = images.Occluder });
        var image = new Image(root, detail, coarse, occluder);
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
        var (detail, coarse) = (image.Detail, image.Coarse);
        if (overview)
        {
            detail.Visible = coarse is null;
            detail.VisibilityRangeEnd = 0f;
            if (coarse is not null)
                coarse.VisibilityRangeBegin = coarse.VisibilityRangeEnd = 0f;
            return;
        }
        detail.Visible = true;
        detail.VisibilityRangeEnd = coarse is null ? FarDistance : LodDistance;
        detail.VisibilityRangeEndMargin = coarse is null ? 0f : LodMargin;
        if (coarse is not null)
        {
            coarse.VisibilityRangeBegin = LodDistance;
            coarse.VisibilityRangeBeginMargin = LodMargin;
            coarse.VisibilityRangeEnd = FarDistance;
        }
    }

    private sealed record Image(Node3D Root, MultiMeshInstance3D Detail, MultiMeshInstance3D? Coarse, OccluderInstance3D? Occluder);

    private sealed class ChunkImages(MultiMesh detail, MultiMesh? coarse, ArrayOccluder3D? occluder)
    {
        public MultiMesh Detail { get; } = detail;
        public MultiMesh? Coarse { get; } = coarse;
        public ArrayOccluder3D? Occluder { get; } = occluder;
        public List<Image> Images { get; } = [];
    }
}
