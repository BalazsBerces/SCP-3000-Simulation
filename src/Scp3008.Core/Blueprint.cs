using System.Numerics;
using System.Text.Json.Serialization;

namespace Scp3008.Core;

public enum Personality { Cautious, Builders, Bold }
public enum WallOutline { Round, Square, Freeform }
public enum WallRun { Straight, Corner, Curved }

/// <summary>
/// A Furniture placement; size and weight are the first barricade's sturdiness requirements. A Battlements slot also caps the
/// height, and a stacked Castle slot records the height its piece was set at.
/// </summary>
public sealed record BlueprintSlot(Vector2 Position, float Rotation, float Width, WallRun Run,
    float MinimumHeight = 0.8f, float MinimumWeight = 10, int? FurnitureId = null, int? ReservedFurnitureId = null,
    float MaximumHeight = 100f, float Elevation = 0f)
{
    public bool Fits(FurniturePiece piece)
    {
        var spec = FurnitureCatalog.Spec(piece);
        // shortcut: weight stands for sturdiness in the first barricade; add load capacity with structural stability.
        return !spec.IsFixed && !spec.IsFloorCovering && !spec.IsLamp &&
            spec.Width >= Width && spec.Width <= Width + 0.8f && spec.Depth <= 1.2f &&
            spec.Height >= MinimumHeight && spec.Height <= MaximumHeight && FurnitureResources.Weight(piece) >= MinimumWeight &&
            piece.Type is not (FurnitureType.FloorMirror or FurnitureType.DisplayCabinet or FurnitureType.FishTank or FurnitureType.DoorFrame);
    }
}

/// <summary>
/// A closed wall outline with a walkable gate opening, and the slots along its runs. A curtain wall (every home ring after the
/// first) has Battlements: its slots alternate tall and short units, and its gate is wide enough for a Gatehouse.
/// </summary>
public sealed record Blueprint(WallOutline Outline, int Population, IReadOnlyList<Vector2> Vertices,
    Vector2 GateStart, Vector2 GateEnd, IReadOnlyList<BlueprintSlot> Slots,
    int? IncorporatedFurnitureId = null, int? IncorporatedPillarId = null, bool Battlements = false)
{
    [JsonIgnore] public bool Complete => Slots.All(s => s.FurnitureId is not null);
}

public sealed record BlueprintPiecePlaced(StoreClock At, int VillageId, int Ring, int Slot, int FurnitureId) : DomainEvent(At);

public sealed partial class ScenarioBuilder
{
    public Blueprint PlanBarricade(int villageId) => _simulation.PlanBarricade(villageId);
}
