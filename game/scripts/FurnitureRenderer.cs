using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Draws every piece of Furniture in the snapshot with instancing: one <see cref="WrappedInstances"/>
/// per Furniture type, which groups the pieces into one MultiMesh per chunk and shows each chunk
/// at its wrapped images around the camera, so Furniture near a seam is drawn on both sides of it.
/// Rebuilt whenever a new world is created. View only: it never touches the simulation.
/// </summary>
public partial class FurnitureRenderer : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    public override void _Ready()
    {
        Host.WorldCreated += Rebuild;
        Rebuild(Host.Snapshot);
    }

    public override void _ExitTree() => Host.WorldCreated -= Rebuild;

    private void Rebuild(WorldSnapshot snapshot)
    {
        var started = Time.GetTicksMsec();
        foreach (var child in GetChildren())
            child.QueueFree();
        foreach (var group in snapshot.Furniture.GroupBy(p => p.Type))
        {
            var instances = new WrappedInstances { Name = group.Key.ToString(), Rig = Rig, Mesh = FurnitureMeshes.For(group.Key) };
            AddChild(instances);
            instances.SetInstances(snapshot.Space, group.Select(p => (Placement(p), FurnitureMeshes.TintFor(p))));
        }
        GD.Print($"Furniture: {snapshot.Furniture.Count} pieces placed in {Time.GetTicksMsec() - started} ms.");
    }

    /// <summary>
    /// The piece's transform in world space. Core rotations turn east towards south, which is a
    /// clockwise turn seen from above, so the Godot yaw is its negative.
    /// </summary>
    private static Transform3D Placement(FurniturePiece piece) =>
        new(new Basis(Vector3.Up, -piece.Rotation), piece.Position.ToWorld(piece.Elevation));
}
