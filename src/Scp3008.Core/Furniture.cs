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
    // Second Furniture wave
    Desk,
    OfficeChair,
    FilingCabinet,
    MeetingTable,
    DeskLamp,
    CushionBin,
    BlanketStack,
    CurtainRack,
    FloorMirror,
    FrameDisplay,
    GardenTable,
    GardenChair,
    SunLounger,
    Parasol,
    Grill,
    DisplayCabinet,
    Sideboard,
    ShoeCabinet,
    CoatRack,
    HallBench,
    ArcLamp,
    TvBench,
    Dresser,
    Nightstand,
    WashingMachine,
    Stove,
    BoxCart,
    BarStool,
    PlanterBox,
    ToyChest,

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
/// <param name="Variant">
/// Which version of its type the piece is (e.g. a round or a long dining table), from 0 to
/// <see cref="FurnitureCatalog.VariantCount"/> - 1; its size is <see cref="FurnitureCatalog.Spec(FurniturePiece)"/>.
/// </param>
/// <param name="DisplayId">The <see cref="Display"/> the piece belongs to, by <see cref="Display.Id"/>.</param>
/// <param name="Tint">
/// The colour of its fabric, plastic or leaves, as an index into the tints (0 to
/// <see cref="FurniturePalettes.TintCount"/> - 1); always one of its Display's palette.
/// </param>
public readonly record struct FurniturePiece(int Id, FurnitureType Type, Vector2 Position, float Elevation, float Rotation,
    int Variant = 0, int DisplayId = -1, int Tint = 0);

/// <summary>The fixed properties of a Furniture type.</summary>
/// <param name="Width">Size along the piece's width axis, in metres.</param>
/// <param name="Depth">Size along the piece's depth (front-back) axis, in metres.</param>
/// <param name="Height">Height, in metres.</param>
/// <param name="IsFloorCovering">Flat pieces (rugs) that other Furniture may stand on.</param>
/// <param name="LightRadius">
/// How far a Lamp's light reaches, in metres; zero for Furniture that gives no light. Lamps on
/// display stay lit at all times, including Closing time, independently of the Store lights.
/// </param>
/// <param name="Variant">Which version of the type this spec describes; 0 is the standard one.</param>
public sealed record FurnitureSpec(FurnitureType Type, float Width, float Depth, float Height, bool IsFloorCovering = false, float LightRadius = 0f,
    int Variant = 0)
{
    /// <summary>Whether this is a Lamp: Furniture that gives light.</summary>
    public bool IsLamp => LightRadius > 0f;
}

/// <summary>The specs of every Furniture type and its variants.</summary>
public static class FurnitureCatalog
{
    private static readonly FurnitureSpec[][] Specs = BuildSpecs();

    /// <summary>The standard version (variant 0) of a type.</summary>
    public static FurnitureSpec Spec(FurnitureType type) => Specs[(int)type][0];

    /// <summary>One version of a type; its footprint may differ from the standard one.</summary>
    public static FurnitureSpec Spec(FurnitureType type, int variant) => Specs[(int)type][variant];

    /// <summary>The spec of a piece's own variant.</summary>
    public static FurnitureSpec Spec(FurniturePiece piece) => Specs[(int)piece.Type][piece.Variant];

    /// <summary>How many versions a type comes in (at least one).</summary>
    public static int VariantCount(FurnitureType type) => Specs[(int)type].Length;

    /// <summary>Heights above the floor of the beams a <see cref="FurnitureType.PalletRack"/> holds boxes on.</summary>
    public static IReadOnlyList<float> PalletRackLevels { get; } = [0.15f, 1.65f, 3.15f];

    /// <summary>
    /// Height above a piece's base at which another piece can stand on it (a lamp on a nightstand,
    /// folded blankets on a bed's mattress); its top for most Furniture.
    /// </summary>
    public static float SurfaceHeight(FurnitureType type, int variant = 0) => type switch
    {
        FurnitureType.Bed => 0.56f,
        FurnitureType.BunkBed => 0.42f,
        FurnitureType.Sofa or FurnitureType.Armchair => 0.42f,
        _ => Spec(type, variant).Height,
    };

    private static FurnitureSpec[][] BuildSpecs()
    {
        FurnitureSpec[] specs =
        [
            new(FurnitureType.PalletRack, 2.7f, 1.1f, 4.6f),
            new(FurnitureType.FlatPackBox, 1.2f, 0.9f, 0.7f),
            new(FurnitureType.ShelfUnit, 2.0f, 0.6f, 2.1f),
            new(FurnitureType.DisplayTable, 1.6f, 0.8f, 0.75f),
            new(FurnitureType.TableLamp, 0.3f, 0.3f, 0.5f, LightRadius: 3f),
            new(FurnitureType.FloorLamp, 0.45f, 0.45f, 1.7f, LightRadius: 5f),
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
            new(FurnitureType.Desk, 1.4f, 0.7f, 0.75f),
            new(FurnitureType.OfficeChair, 0.65f, 0.65f, 1.15f),
            new(FurnitureType.FilingCabinet, 0.5f, 0.6f, 1.3f),
            new(FurnitureType.MeetingTable, 2.4f, 1.1f, 0.75f),
            new(FurnitureType.DeskLamp, 0.3f, 0.3f, 0.55f, LightRadius: 3f),
            new(FurnitureType.CushionBin, 1f, 0.8f, 0.8f),
            new(FurnitureType.BlanketStack, 0.8f, 0.6f, 0.5f),
            new(FurnitureType.CurtainRack, 1.6f, 0.6f, 2.1f),
            new(FurnitureType.FloorMirror, 0.7f, 0.5f, 1.8f),
            new(FurnitureType.FrameDisplay, 1.2f, 0.5f, 1.6f),
            new(FurnitureType.GardenTable, 1.4f, 0.9f, 0.75f),
            new(FurnitureType.GardenChair, 0.6f, 0.6f, 0.9f),
            new(FurnitureType.SunLounger, 0.7f, 1.9f, 0.65f),
            new(FurnitureType.Parasol, 2.4f, 2.4f, 2.4f),
            new(FurnitureType.Grill, 0.9f, 0.6f, 1.05f),
            new(FurnitureType.DisplayCabinet, 1.2f, 0.45f, 1.8f),
            new(FurnitureType.Sideboard, 1.6f, 0.5f, 0.85f),
            new(FurnitureType.ShoeCabinet, 0.8f, 0.35f, 1.15f),
            new(FurnitureType.CoatRack, 0.65f, 0.65f, 1.8f),
            new(FurnitureType.HallBench, 1.2f, 0.4f, 0.5f),
            new(FurnitureType.ArcLamp, 1.6f, 0.6f, 2.1f, LightRadius: 5f),
            new(FurnitureType.TvBench, 1.6f, 0.45f, 0.5f),
            new(FurnitureType.Dresser, 1.2f, 0.5f, 0.9f),
            new(FurnitureType.Nightstand, 0.45f, 0.4f, 0.5f),
            new(FurnitureType.WashingMachine, 0.6f, 0.6f, 0.85f),
            new(FurnitureType.Stove, 0.6f, 0.6f, 0.9f),
            new(FurnitureType.BoxCart, 1.1f, 0.7f, 1.1f),
            new(FurnitureType.BarStool, 0.45f, 0.45f, 0.8f),
            new(FurnitureType.PlanterBox, 1.2f, 0.5f, 0.7f),
            new(FurnitureType.ToyChest, 0.8f, 0.45f, 0.5f),

            // Variants of the most repeated types: other proportions, backs, legs or materials.
            new(FurnitureType.Chair, 0.45f, 0.5f, 1.05f, Variant: 1),          // high ladder back
            new(FurnitureType.Chair, 0.55f, 0.55f, 0.85f, Variant: 2),         // carver with arms
            new(FurnitureType.OfficeChair, 0.6f, 0.6f, 0.95f, Variant: 1),     // low-back task chair
            new(FurnitureType.OfficeChair, 0.7f, 0.7f, 1.3f, Variant: 2),      // high-back executive chair
            new(FurnitureType.Desk, 1.2f, 0.6f, 0.75f, Variant: 1),            // compact trestle desk
            new(FurnitureType.Desk, 1.6f, 0.8f, 0.75f, Variant: 2),            // large desk with two pedestals
            new(FurnitureType.DiningTable, 1.1f, 1.1f, 0.75f, Variant: 1),     // round, on a pedestal
            new(FurnitureType.DiningTable, 2.2f, 0.95f, 0.75f, Variant: 2),    // long refectory table
            new(FurnitureType.GardenChair, 0.5f, 0.5f, 0.85f, Variant: 1),     // metal bistro chair
            new(FurnitureType.GardenChair, 0.7f, 0.75f, 0.8f, Variant: 2),     // low lounge chair
            new(FurnitureType.GardenTable, 0.9f, 0.9f, 0.75f, Variant: 1),     // round bistro table
            new(FurnitureType.GardenTable, 2.0f, 0.9f, 0.75f, Variant: 2),     // long picnic table
            new(FurnitureType.Sofa, 1.6f, 0.9f, 0.85f, Variant: 1),            // two-seat loveseat
            new(FurnitureType.Sofa, 2.7f, 1.0f, 0.9f, Variant: 2),             // large three-seat sofa
            new(FurnitureType.Wardrobe, 1.6f, 0.6f, 2.0f, Variant: 1),         // double wardrobe
            new(FurnitureType.Wardrobe, 0.6f, 0.6f, 2.1f, Variant: 2),         // narrow tall wardrobe
            new(FurnitureType.KitchenCabinet, 0.8f, 0.6f, 0.9f, Variant: 1),   // wide drawer cabinet
            new(FurnitureType.KitchenCabinet, 0.6f, 0.6f, 2.1f, Variant: 2),   // tall pantry
            new(FurnitureType.DisplayCabinet, 1.6f, 0.45f, 1.0f, Variant: 1),  // low, wide glass cabinet
            new(FurnitureType.DisplayCabinet, 0.6f, 0.45f, 1.9f, Variant: 2),  // narrow glass tower
        ];
        var byType = new FurnitureSpec[Enum.GetValues<FurnitureType>().Length][];
        foreach (var group in specs.GroupBy(s => s.Type))
        {
            var variants = group.OrderBy(s => s.Variant).ToArray();
            if (variants.Where((s, i) => s.Variant != i).Any())
                throw new InvalidOperationException($"The variants of {group.Key} must be numbered from 0 without gaps.");
            byType[(int)group.Key] = variants;
        }
        if (byType.Any(s => s is null))
            throw new InvalidOperationException("Every Furniture type needs a spec.");
        return byType;
    }
}

/// <summary>
/// The coordinated colour schemes of the Store's Displays: a Display picks one palette, and its
/// fabric, plastic and leaves take the palette's few related tints. The presentation layer maps
/// each tint index to a colour per material.
/// </summary>
public static class FurniturePalettes
{
    /// <summary>Number of distinct tints; a piece's <see cref="FurniturePiece.Tint"/> is below it.</summary>
    public const int TintCount = 12;

    /// <summary>Every palette: two or three related tint indices each.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> All { get; } =
    [
        [0, 1, 2],    // warm neutrals
        [2, 3, 11],   // greys and white
        [4, 5, 11],   // blues
        [6, 7, 1],    // greens and sand
        [8, 9, 0],    // mustard and terracotta
        [10, 0, 5],   // rose and soft blue
        [3, 8],       // charcoal and mustard
        [5, 6, 0],    // calm blue-green
    ];
}
