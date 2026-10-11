using Godot;

namespace Scp3008.Game;

/// <summary>The flat Store floor, sized and placed from the core's snapshot.</summary>
public partial class StoreFloor : MeshInstance3D
{
    [Export] public SimulationHost Host { get; set; } = null!;

    public override void _Ready()
    {
        var size = Host.Snapshot.StoreSize;
        Mesh = new PlaneMesh { Size = new Vector2(size, size) };
        Position = new Vector3(size / 2f, 0f, size / 2f);
    }
}
