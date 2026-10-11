using System.Numerics;

namespace Scp3008.Core;

/// <summary>What a piece of Furniture is.</summary>
public enum FurnitureType
{
    // Self-serve warehouse
    PalletRack,
    FlatPackBox,
    // Lighting & electrical
    ShelfUnit,
    DisplayTable,
    TableLamp,
    FloorLamp,
    // Showrooms
    Sofa,
    Armchair,
    CoffeeTable,
    Bed,
    Wardrobe,
    Bookcase,
    Rug,
    // Bathrooms & kitchens
    KitchenCabinet,
    Fridge,
    Bathtub,
    Toilet,
    VanityUnit,
    // Restaurant/food market
    GroceryShelf,
    Freezer,
    Counter,
    DiningTable,
    Chair,
    // Plant section
    PlantTable,
    PottedPlant,
    LargePlant,
    // Kids' area
    ToyShelf,
    BunkBed,
    KidsTable,
    KidsChair,
    PlayCube,
}

/// <summary>
/// One piece of Furniture: an individual object in the Store with a free position and rotation.
/// </summary>
/// <param name="Id">Unique within the world.</param>
/// <param name="Type">What the piece is.</param>
/// <param name="Position">Centre of its footprint on the floor, wrapped onto the floor, in metres.</param>
/// <param name="Elevation">Height of its base above the floor, in metres (e.g. a lamp on a table, a box on a rack).</param>
/// <param name="Rotation">
/// Heading in radians within [-π, π]: the piece's width axis turned from east (+X) towards south (+Y).
/// Its front faces along its depth axis, which is south when the rotation is zero.
/// </param>
public readonly record struct FurniturePiece(int Id, FurnitureType Type, Vector2 Position, float Elevation, float Rotation);

/// <summary>The fixed properties of a Furniture type.</summary>
/// <param name="Width">Size along the piece's width axis, in metres.</param>
/// <param name="Depth">Size along the piece's depth (front-back) axis, in metres.</param>
/// <param name="Height">Height, in metres.</param>
/// <param name="IsFloorCovering">Flat pieces (rugs) that other Furniture may stand on.</param>
public sealed record FurnitureSpec(FurnitureType Type, float Width, float Depth, float Height, bool IsFloorCovering = false);

/// <summary>The specs of every Furniture type.</summary>
public static class FurnitureCatalog
{
    private static readonly FurnitureSpec[] Specs = BuildSpecs();

    public static FurnitureSpec Spec(FurnitureType type) => Specs[(int)type];

    /// <summary>Heights above the floor of the beams a <see cref="FurnitureType.PalletRack"/> holds boxes on.</summary>
    public static IReadOnlyList<float> PalletRackLevels { get; } = [0.15f, 1.65f, 3.15f];

    private static FurnitureSpec[] BuildSpecs()
    {
        FurnitureSpec[] specs =
        [
            new(FurnitureType.PalletRack, 2.7f, 1.1f, 4.6f),
            new(FurnitureType.FlatPackBox, 1.2f, 0.9f, 0.7f),
            new(FurnitureType.ShelfUnit, 2.0f, 0.6f, 2.1f),
            new(FurnitureType.DisplayTable, 1.6f, 0.8f, 0.75f),
            new(FurnitureType.TableLamp, 0.3f, 0.3f, 0.5f),
            new(FurnitureType.FloorLamp, 0.45f, 0.45f, 1.7f),
            new(FurnitureType.Sofa, 2.2f, 0.95f, 0.85f),
            new(FurnitureType.Armchair, 0.85f, 0.85f, 0.9f),
            new(FurnitureType.CoffeeTable, 1.2f, 0.6f, 0.45f),
            new(FurnitureType.Bed, 1.6f, 2.1f, 0.9f),
            new(FurnitureType.Wardrobe, 1.0f, 0.6f, 2.0f),
            new(FurnitureType.Bookcase, 0.8f, 0.3f, 2.0f),
            new(FurnitureType.Rug, 2.4f, 1.7f, 0.01f, IsFloorCovering: true),
            new(FurnitureType.KitchenCabinet, 0.6f, 0.6f, 0.9f),
            new(FurnitureType.Fridge, 0.7f, 0.7f, 1.85f),
            new(FurnitureType.Bathtub, 1.7f, 0.75f, 0.6f),
            new(FurnitureType.Toilet, 0.4f, 0.65f, 0.8f),
            new(FurnitureType.VanityUnit, 0.8f, 0.5f, 0.85f),
            new(FurnitureType.GroceryShelf, 2.0f, 0.9f, 1.8f),
            new(FurnitureType.Freezer, 2.0f, 0.9f, 0.9f),
            new(FurnitureType.Counter, 2.0f, 0.7f, 1.05f),
            new(FurnitureType.DiningTable, 1.6f, 0.9f, 0.75f),
            new(FurnitureType.Chair, 0.45f, 0.5f, 0.9f),
            new(FurnitureType.PlantTable, 2.4f, 1.0f, 0.8f),
            new(FurnitureType.PottedPlant, 0.35f, 0.35f, 0.6f),
            new(FurnitureType.LargePlant, 0.7f, 0.7f, 2.2f),
            new(FurnitureType.ToyShelf, 1.6f, 0.45f, 1.2f),
            new(FurnitureType.BunkBed, 2.0f, 0.95f, 1.6f),
            new(FurnitureType.KidsTable, 0.8f, 0.8f, 0.5f),
            new(FurnitureType.KidsChair, 0.35f, 0.35f, 0.55f),
            new(FurnitureType.PlayCube, 0.5f, 0.5f, 0.5f),
        ];
        var byType = new FurnitureSpec[Enum.GetValues<FurnitureType>().Length];
        foreach (var spec in specs)
            byType[(int)spec.Type] = spec;
        if (byType.Any(s => s is null))
            throw new InvalidOperationException("Every Furniture type needs a spec.");
        return byType;
    }
}
