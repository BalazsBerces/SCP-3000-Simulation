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
        foreach (var group in snapshot.Furniture.GroupBy(p => (p.Type, p.Variant)))
        {
            var instances = new WrappedInstances
            {
                Name = group.Key.Variant == 0 ? group.Key.Type.ToString() : $"{group.Key.Type}-{group.Key.Variant}",
                Rig = Rig,
                FarDistance = Visibility.FurnitureDistance,
                ViewReach = Visibility.PlacementReach(Rig, snapshot.Space.ChunkSize),
                Mesh = FurnitureMeshes.For(group.Key.Type, group.Key.Variant),
                CoarseMesh = FurnitureMeshes.CoarseFor(group.Key.Type, group.Key.Variant),
                OccluderQuads = FurnitureMeshes.OccluderQuads(group.Key.Type, group.Key.Variant),
            };
            AddChild(instances);
            instances.SetInstances(snapshot.Space, group.Select(p => (Placement(p), FurnitureMeshes.TintFor(p))));
        }
        FurnitureDetail.DrawBoth(3);
        GD.Print($"Furniture: {snapshot.Furniture.Count} pieces placed in {Time.GetTicksMsec() - started} ms.");
    }

    /// <summary>
    /// The piece's transform in world space. Core rotations turn east towards south, which is a
    /// clockwise turn seen from above, so the Godot yaw is its negative.
    /// </summary>
    private static Transform3D Placement(FurniturePiece piece) =>
        new(new Basis(Vector3.Up, -piece.Rotation), piece.Position.ToWorld(piece.Elevation));
}
