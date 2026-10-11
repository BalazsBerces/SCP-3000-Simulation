using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws many copies of one mesh at Store positions, grouped into one MultiMesh per chunk.
/// Around the camera focus every chunk is shown at each wrapped image within
/// <see cref="ViewReach"/>, so objects near a seam are drawn on both sides of it (ghost copies)
/// and panning across a seam never reveals a gap. Placement is refreshed only when the focus
/// moves into another chunk, and only moves nodes; the instance data is built once.
/// </summary>
public partial class WrappedInstances : Node3D
{
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public Mesh? Mesh { get; set; }

    /// <summary>How far from the focus content must be drawn; at least the farthest floor the camera can see.</summary>
    [Export] public float ViewReach { get; set; } = 700f;

    private readonly Dictionary<ChunkCoord, ChunkImages> _chunks = new();
    private StoreSpace _space = StoreSpace.Standard;
    private (long X, long Y)? _placedAroundCell;

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
            foreach (var image in images.Nodes)
                image.QueueFree();
        _chunks.Clear();
        _space = space;
        _placedAroundCell = null;

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
            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = useColors,
                Mesh = Mesh,
                InstanceCount = chunkInstances.Length,
            };
            multiMesh.Buffer = buffer;
            _chunks[group.Key] = new ChunkImages(multiMesh);
        }
    }

    public override void _Process(double delta)
    {
        if (_chunks.Count == 0)
            return;
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
            if (index == images.Nodes.Count)
            {
                var node = new MultiMeshInstance3D { Multimesh = images.MultiMesh };
                AddChild(node);
                images.Nodes.Add(node);
            }
            images.Nodes[index].Position = image.Offset.ToWorld();
            images.Nodes[index].Visible = true;
        }
        foreach (var (chunk, images) in _chunks)
            for (var i = used.GetValueOrDefault(chunk); i < images.Nodes.Count; i++)
                images.Nodes[i].Visible = false;
    }

    private sealed class ChunkImages(MultiMesh multiMesh)
    {
        public MultiMesh MultiMesh { get; } = multiMesh;
        public List<MultiMeshInstance3D> Nodes { get; } = [];
    }
}
