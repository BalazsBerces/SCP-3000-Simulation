using Godot;

namespace Scp3008.Game;

/// <summary>Shared presentation reach. Change FurnitureDistance here to extend Furniture and Lamp coverage together.</summary>
[GlobalClass]
public partial class StoreVisibility : Resource
{
    [Export] public float FurnitureDistance { get; set; } = 600f;

    /// <summary>
    /// How far Landmark Furniture is drawn, like the Pillars: a Landmark spans several chunks, so it
    /// is drawn whole whenever any of it is in view, and the Edge fade alone decides how far it shows.
    /// </summary>
    [Export] public float LandmarkDistance { get; set; } = 1200f;

    // Placement must cover the eye-to-focus offset at the supported zoom and a complete Chunk.
    public float PlacementReach(CameraRig rig, float chunkSize) =>
        FurnitureDistance + rig.MaxDistance + chunkSize;

    public float LandmarkPlacementReach(CameraRig rig, float chunkSize) =>
        LandmarkDistance + rig.MaxDistance + chunkSize;

    // Godot measures visibility from the MultiMesh AABB centre, which can be anywhere in a Chunk.
    public float LampDistance(float chunkSize) => FurnitureDistance + chunkSize * Mathf.Sqrt(2f);
}
