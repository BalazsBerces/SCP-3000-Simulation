using Godot;

namespace Scp3008.Game;

/// <summary>
/// The flat Store floor. It follows the camera focus out to the camera's far plane, and its
/// shader draws a world-space pattern that repeats exactly once per Store length, so the floor
/// has no edge and no visible seam however far the camera pans.
/// </summary>
public partial class StoreFloor : MeshInstance3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    public override void _Ready()
    {
        var reach = Rig.Camera.Far;
        Mesh = new PlaneMesh { Size = new Vector2(2f * reach, 2f * reach) };
        var space = Host.Snapshot.Space;
        FloorMaterial.SetShaderParameter("store_size", space.Size);
        FloorMaterial.SetShaderParameter("chunk_size", space.ChunkSize);
        FollowFocus();
    }

    public override void _Process(double delta) => FollowFocus();

    /// <summary>The floor material, shared with the wrap debug overlay.</summary>
    public ShaderMaterial FloorMaterial => (ShaderMaterial)MaterialOverride;

    private void FollowFocus() => Position = Rig.Focus with { Y = 0f };
}
