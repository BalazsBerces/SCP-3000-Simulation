using Godot;

namespace Scp3008.Game;

/// <summary>Shared presentation reach. Change FurnitureDistance here to extend Furniture and Lamp coverage together.</summary>
[GlobalClass]
public partial class StoreVisibility : Resource
{
    [Export] public float FurnitureDistance { get; set; } = 600f;

    // Placement must cover the eye-to-focus offset at the supported zoom and a complete Chunk.
    public float PlacementReach(CameraRig rig, float chunkSize) =>
        FurnitureDistance + rig.MaxDistance + chunkSize;

    // Godot measures visibility from the MultiMesh AABB centre, which can be anywhere in a Chunk.
    public float LampDistance(float chunkSize) => FurnitureDistance + chunkSize * Mathf.Sqrt(2f);
}
