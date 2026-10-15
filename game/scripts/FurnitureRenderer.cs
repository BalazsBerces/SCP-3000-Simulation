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

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--lod-zoom="))
                FurnitureDetail.SwitchZoom = float.Parse(arg["--lod-zoom=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--map-height="))
                FurnitureDetail.MapHeight = float.Parse(arg["--map-height=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        Host.WorldCreated += Rebuild;
        FurnitureDetail.Changed += ShowDetail;
        FurnitureDetail.Update(Rig.Distance, Rig.Camera.GlobalPosition.Y);
        ShowDetail(FurnitureDetail.Coarse);
        Rebuild(Host.Snapshot);
    }

    public override void _ExitTree()
    {
        Host.WorldCreated -= Rebuild;
        FurnitureDetail.Changed -= ShowDetail;
    }

    // Runs after the camera rig (CameraRig.ProcessPriority), so the level matches this frame's zoom.
    public override void _Process(double delta) => FurnitureDetail.Update(Rig.Distance, Rig.Camera.GlobalPosition.Y);

    private void ShowDetail(bool coarse) => Rig.Camera.CullMask = FurnitureDetail.Mask(Rig.Camera.CullMask);

    private void Rebuild(WorldSnapshot snapshot)
    {
        var started = Time.GetTicksMsec();
        foreach (var child in GetChildren())
            child.QueueFree();
        foreach (var group in snapshot.Furniture.GroupBy(p => (p.Type, p.Variant, Landmark: p.LandmarkId >= 0)))
        {
            var name = group.Key.Variant == 0 ? group.Key.Type.ToString() : $"{group.Key.Type}-{group.Key.Variant}";
            var instances = new WrappedInstances
            {
                Name = group.Key.Landmark ? $"Landmark-{name}" : name,
                Rig = Rig,
                FarDistance = group.Key.Landmark ? Visibility.LandmarkDistance : Visibility.FurnitureDistance,
                ViewReach = group.Key.Landmark
                    ? Visibility.LandmarkPlacementReach(Rig, snapshot.Space.ChunkSize)
                    : Visibility.PlacementReach(Rig, snapshot.Space.ChunkSize),
                Mesh = FurnitureMeshes.For(group.Key.Type, group.Key.Variant),
                CoarseMesh = FurnitureMeshes.CoarseFor(group.Key.Type, group.Key.Variant),
                // Landmark Furniture hides nothing: the panels of thousands of stacked, tilted and touching
                // pieces stalled the first render with occlusion culling on, as the crossing pantry panels did.
                OccluderQuads = group.Key.Landmark ? [] : FurnitureMeshes.OccluderQuads(group.Key.Type, group.Key.Variant),
            };
            AddChild(instances);
            instances.SetInstances(snapshot.Space, group.Select(p => (Placement(p), Colour(snapshot, p))));
        }
        FurnitureDetail.DrawBoth(3);
        GD.Print($"Furniture: {snapshot.Furniture.Count} pieces placed in {Time.GetTicksMsec() - started} ms; " +
                 $"Landmarks: {string.Join(", ", snapshot.Landmarks.Select(l => $"{l.Type} in {snapshot.Sectors[l.SectorId].Name} at {l.Centre.X:0},{l.Centre.Y:0}, {l.Height:0} m"))}.");
    }

    /// <summary>
    /// A piece's instance colour: its tint, and for a clock of the clock wall its offset from the time to Closing time,
    /// as a fraction of a day in the alpha, which turns its hands (furniture.gdshader).
    /// </summary>
    private static Color Colour(WorldSnapshot snapshot, FurniturePiece piece)
    {
        var colour = FurnitureMeshes.TintFor(piece);
        if (piece.Type == FurnitureType.WallClock && piece.LandmarkId >= 0
            && snapshot.Landmarks[piece.LandmarkId].ClockOffsets.TryGetValue(piece.Id, out var offset))
            colour.A = (float)(offset.TotalSeconds / 86400.0);
        return colour;
    }

    /// <summary>
    /// The piece's transform in world space. Core rotations turn east towards south, which is a
    /// clockwise turn seen from above, so the Godot yaw is its negative. A tipped-over piece (Landmark
    /// Furniture) is first tilted about its width axis through its base, its front (+Z) going down.
    /// </summary>
    public static Transform3D Placement(FurniturePiece piece) =>
        new(new Basis(Vector3.Up, -piece.Rotation) * new Basis(Vector3.Right, piece.Tilt), piece.Position.ToWorld(piece.Elevation));
}
