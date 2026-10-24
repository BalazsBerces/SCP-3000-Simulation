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
    // Landmark Furniture, found only in Landmarks
    Mattress,
    CardboardBox,
    WallClock,
    FountainBowl,
    FountainColumn,
    BasinKerb,
    // Point of interest Furniture, found only in Points of interest
    ShoppingCart,
    MealPlate,
    ExitSign,
    Mannequin,
    DoorFrame,
    // The Aquarium aisle's Furniture
    FishTank,
    TankRack,
    // The Ball pit's Furniture
    BallPool,
    NetRailing,
    PlaySlide,
    FoamStep,
    PlayBall,
    // The Cafeteria's Furniture
    /// <summary>Steel serving counter with heated wells of food behind a glass sneeze guard.</summary>
    HotFoodCounter,
    /// <summary>Chilled serving counter, a glass-fronted cabinet of cakes.</summary>
    DessertCounter,
    /// <summary>Steel tube rail customers slide their trays along.</summary>
    TrayRail,
    /// <summary>Stack of trays with cutlery bins.</summary>
    TrayStation,
    /// <summary>Coffee machine, stood on a counter.</summary>
    CoffeeMachine,
    /// <summary>Checkout counter with a till.</summary>
    Till,
    /// <summary>Tray-return trolley with shelves of used trays.</summary>
    TrayTrolley,
    /// <summary>Children's high chair.</summary>
    HighChair,
    /// <summary>Board of dish names high on two posts.</summary>
    MenuBoard,
    /// <summary>Serving tray.</summary>
    Tray,
    /// <summary>Spilled drink or food on the floor.</summary>
    Spill,
    /// <summary>A hand-written note.</summary>
    Note,
    // The Restroom's Furniture (ticket 08j)
    /// <summary>Wall partition between restroom stalls.</summary>
    StallPartition,
    /// <summary>Restroom stall door (can be closed, ajar, or hanging off).</summary>
    StallDoor,
    /// <summary>Wall-mounted electric hand dryer.</summary>
    HandDryer,
    /// <summary>Wall-mounted paper towel dispenser.</summary>
    PaperTowelDispenser,
    /// <summary>Wall-mounted fold-down baby-changing station.</summary>
    BabyChangingTable,
    /// <summary>Restroom sign with generic store icons.</summary>
    RestroomSign,
    /// <summary>Worn folding plastic out-of-order / wet-floor caution sign.</summary>
    OutOfOrderSign,
    /// <summary>Long mirror above washbasins (variant 0: intact, variant 1: cracked).</summary>
    RestroomMirror,
    /// <summary>Tiled perimeter wall segment of the restroom.</summary>
    TiledWall,
    /// <summary>Tiled floor covering of the restroom.</summary>
    TiledFloor,
    /// <summary>Discarded paper towel or paper scrap on the floor.</summary>
    LitterPaper,
    // The Lost and found's Furniture (ticket 08k)
    /// <summary>Service counter where lost items are reported and claimed.</summary>
    ServiceCounter,
    /// <summary>Service counter call bell.</summary>
    ServiceBell,
    /// <summary>Labelled cardboard storage box of lost items (variant 0: closed, variant 1: spilled open).</summary>
    LabelledBox,
    /// <summary>Mobile utility cart holding unsorted lost items.</summary>
    UnsortedCart,
    /// <summary>Notice board displaying notes and inquiries about lost items.</summary>
    NoticeBoard,
    /// <summary>Store service sign marking the Lost and found service point.</summary>
    LostAndFoundSign,
    // The Camping ground's Furniture (ticket 07z), found only in that Landmark
    /// <summary>Fake tree: a trunk and a crown (variant 0: broad oak, 1: tall conifer), or a fake bush (2: round, 3: flowering).</summary>
    Tree,
    /// <summary>Tent, of the kinds in <see cref="TentKind"/> (the variant is the kind).</summary>
    Tent,
    /// <summary>The lantern-lit door of a tent: a Lamp in a tent's doorway whose cloth glows with a person's silhouette against it when lit.</summary>
    LanternTent,
    /// <summary>Fake campfire flame: a glowing red-orange cloth flame with a Lamp inside it.</summary>
    CampfireFlame,
    /// <summary>Log seat round a campfire.</summary>
    LogSeat,
    /// <summary>Hammock slung between two trees.</summary>
    Hammock,
    /// <summary>Kayak.</summary>
    Kayak,
    /// <summary>Fishing rod leaning on its stand.</summary>
    FishingRod,
    /// <summary>Rubber duck.</summary>
    RubberDuck,
    /// <summary>Big plush bear, sitting.</summary>
    PlushBear,
    /// <summary>Trail sign post.</summary>
    TrailSign,
    /// <summary>Camping axe (Gear): the decorative axe stuck in a tree.</summary>
    Axe,
    /// <summary>Sleeping bag (a Dwelling's bedding): variant 0 laid out flat, 1 rolled up.</summary>
    SleepingBag,
    // The Camping ground's showroom staging (ticket 07z2)
    /// <summary>Tree house shell: a cabin on a deck high in its own big tree. A fixed part of the Camping ground, never taken (<see cref="FurnitureSpec.IsFixed"/>).</summary>
    TreeHouse,
    /// <summary>Tasting tray on the marshmallow stand, holding marshmallows on sticks.</summary>
    MarshmallowTray,
    /// <summary>Advertising banner stretched between two poles.</summary>
    CampBanner,
    /// <summary>Price tag on a stake beside a showroom display.</summary>
    PriceTag,
    /// <summary>Folding camping chair.</summary>
    CampChair,
    /// <summary>Camping lantern: a small Lamp.</summary>
    CampLantern,
    /// <summary>Stone ring of a campfire with its logs; the cloth flame stands in it.</summary>
    CampfireRing,
    /// <summary>A marshmallow on a roasting stick, standing in the tasting tray.</summary>
    MarshmallowStick,
}

/// <summary>The kinds of tent, as the variant of a <see cref="FurnitureType.Tent"/>.</summary>
public enum TentKind
{
    /// <summary>Small dome tent.</summary>
    Dome,
    /// <summary>Cone-shaped tipi.</summary>
    Tipi,
    /// <summary>Large family tent with windows, tall enough to stand in.</summary>
    Family,
    /// <summary>Wide canvas bell tent.</summary>
    Bell,
    /// <summary>Classic ridge tent with an A-shaped front.</summary>
    AFrame,
    /// <summary>Pop-up shelter: a quick-pitch canopy on four legs.</summary>
    PopUp,
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
/// <param name="DisplayId">The <see cref="Display"/> the piece belongs to, by <see cref="Display.Id"/>; -1 for Landmark and Point of interest Furniture.</param>
/// <param name="Tint">
/// The colour of its fabric, plastic or leaves, as an index into the tints (0 to
/// <see cref="FurniturePalettes.TintCount"/> - 1); always one of its Display's palette, if it has one. The Camping ground's
/// dinosaurs use the <see cref="FurniturePalettes.DinosaurTints"/> beyond that.
/// </param>
/// <param name="LandmarkId">The <see cref="Landmark"/> the piece is part of, by <see cref="Landmark.Id"/>; -1 for Display Furniture.</param>
/// <param name="PointOfInterestId">The <see cref="PointOfInterest"/> the piece is part of, by <see cref="PointOfInterest.Id"/>; -1 for other Furniture.</param>
/// <param name="Tilt">
/// How far the piece is tipped over, in radians, about its width axis through the centre of its base:
/// positive tips its front down, a quarter turn lays it on its front. Zero (upright) for Display Furniture;
/// its corners are <see cref="FurnitureCatalog.Corners"/>.
/// </param>
/// <param name="Roll">
/// How far the piece is rolled onto its side, in radians, about its depth axis through the centre of its base, applied
/// before <paramref name="Tilt"/>: positive lifts its +width side, so a quarter turn stands it on its -width end (a sofa
/// on end). Zero for everything but the Armchair henge's standing stones.
/// </param>
public readonly record struct FurniturePiece(int Id, FurnitureType Type, Vector2 Position, float Elevation, float Rotation,
    int Variant = 0, int DisplayId = -1, int Tint = 0, int LandmarkId = -1, float Tilt = 0f, int PointOfInterestId = -1, float Roll = 0f);

/// <summary>The fixed properties of a Furniture type.</summary>
/// <param name="Width">Size along the piece's width axis, in metres.</param>
/// <param name="Depth">Size along the piece's depth (front-back) axis, in metres.</param>
/// <param name="Height">Height, in metres.</param>
/// <param name="IsFloorCovering">Flat pieces (rugs) that other Furniture may stand on.</param>
/// <param name="LightRadius">
/// How far a Lamp's light reaches, in metres; zero for Furniture that gives no light. Lamps on
/// display are off while the Store is open and come on as the Store lights fade (<see cref="WorldSnapshot.LampLevel"/>).
/// </param>
/// <param name="Variant">Which version of the type this spec describes; 0 is the standard one.</param>
/// <param name="IsLeather">
/// Upholstered in leather rather than fabric (the leather sofa). Leather versions are rare: a type's leather
/// versions together are chosen about <see cref="FurnitureCatalog.LeatherShare"/> of the time.
/// </param>
/// <param name="IsFixed">A fixed part of its Landmark that no one can take or disassemble (the Camping ground's tree house shell).</param>
public sealed record FurnitureSpec(FurnitureType Type, float Width, float Depth, float Height, bool IsFloorCovering = false, float LightRadius = 0f,
    int Variant = 0, bool IsLeather = false, bool IsFixed = false)
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

    /// <summary>The share of a type's pieces, among the versions allowed, that are leather when it has both leather and fabric versions.</summary>
    public const float LeatherShare = 0.1f;

    /// <summary>
    /// Picks a version of a type from those <paramref name="allowed"/> (all by default): the fabric ones equally
    /// often, the leather ones together only <see cref="LeatherShare"/> of the time.
    /// </summary>
    internal static int PickVariant(SeededRandom random, FurnitureType type, Func<FurnitureSpec, bool>? allowed = null)
    {
        var specs = Specs[(int)type].Where(s => allowed is null || allowed(s)).ToArray();
        var leather = specs.Where(s => s.IsLeather).ToArray();
        var fabric = specs.Where(s => !s.IsLeather).ToArray();
        if (leather.Length > 0 && fabric.Length > 0)
            return random.Pick(random.Chance(LeatherShare) ? leather : fabric).Variant;
        return random.Pick(specs).Variant;
    }

    /// <summary>Heights above the floor of the shelves a <see cref="FurnitureType.TankRack"/> holds small and medium tanks on.</summary>
    public static IReadOnlyList<float> TankRackLevels { get; } = [0.15f, 0.85f, 1.55f];

    /// <summary>The <see cref="FurnitureType.FishTank"/> variant of the large tank that stands on its own cabinet rather than on a rack.</summary>
    public const int LargeFishTank = 2;

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

    /// <summary>The Furniture types found only in Landmarks, never on display.</summary>
    public static IReadOnlyList<FurnitureType> LandmarkTypes { get; } =
    [
        FurnitureType.Mattress, FurnitureType.CardboardBox, FurnitureType.WallClock, FurnitureType.FountainBowl,
        FurnitureType.FountainColumn, FurnitureType.BasinKerb,
        FurnitureType.Tree, FurnitureType.Tent, FurnitureType.LanternTent, FurnitureType.CampfireFlame, FurnitureType.LogSeat,
        FurnitureType.Hammock, FurnitureType.Kayak, FurnitureType.FishingRod, FurnitureType.RubberDuck, FurnitureType.PlushBear,
        FurnitureType.TrailSign, FurnitureType.Axe, FurnitureType.SleepingBag,
        FurnitureType.TreeHouse, FurnitureType.MarshmallowTray, FurnitureType.CampBanner, FurnitureType.PriceTag,
        FurnitureType.CampChair, FurnitureType.CampLantern, FurnitureType.CampfireRing, FurnitureType.MarshmallowStick,
    ];

    /// <summary>The Furniture types found only in the Camping ground.</summary>
    public static IReadOnlyList<FurnitureType> CampingTypes { get; } =
    [
        FurnitureType.Tree, FurnitureType.Tent, FurnitureType.LanternTent, FurnitureType.CampfireFlame, FurnitureType.LogSeat,
        FurnitureType.Hammock, FurnitureType.Kayak, FurnitureType.FishingRod, FurnitureType.RubberDuck, FurnitureType.PlushBear,
        FurnitureType.TrailSign, FurnitureType.Axe, FurnitureType.SleepingBag,
        FurnitureType.TreeHouse, FurnitureType.MarshmallowTray, FurnitureType.CampBanner, FurnitureType.PriceTag,
        FurnitureType.CampChair, FurnitureType.CampLantern, FurnitureType.CampfireRing, FurnitureType.MarshmallowStick,
    ];

    /// <summary>The Furniture types found only in Points of interest, never on display.</summary>
    public static IReadOnlyList<FurnitureType> PointOfInterestTypes { get; } =
    [
        FurnitureType.ShoppingCart, FurnitureType.MealPlate, FurnitureType.ExitSign, FurnitureType.Mannequin,
        FurnitureType.DoorFrame, FurnitureType.FishTank, FurnitureType.TankRack, FurnitureType.BallPool, FurnitureType.NetRailing, FurnitureType.PlaySlide,
        FurnitureType.FoamStep, FurnitureType.PlayBall,
        FurnitureType.HotFoodCounter, FurnitureType.DessertCounter, FurnitureType.TrayRail, FurnitureType.TrayStation,
        FurnitureType.CoffeeMachine, FurnitureType.Till, FurnitureType.TrayTrolley, FurnitureType.HighChair,
        FurnitureType.MenuBoard, FurnitureType.Tray, FurnitureType.Spill, FurnitureType.Note,
        FurnitureType.StallPartition, FurnitureType.StallDoor, FurnitureType.HandDryer, FurnitureType.PaperTowelDispenser,
        FurnitureType.BabyChangingTable, FurnitureType.RestroomSign, FurnitureType.OutOfOrderSign, FurnitureType.RestroomMirror,
        FurnitureType.TiledWall, FurnitureType.TiledFloor, FurnitureType.LitterPaper,
        FurnitureType.ServiceCounter, FurnitureType.ServiceBell, FurnitureType.LabelledBox, FurnitureType.UnsortedCart,
        FurnitureType.NoticeBoard, FurnitureType.LostAndFoundSign,
    ];

    /// <summary>
    /// The eight corners of a piece's box, tilt and rotation included: X east and Y south around its
    /// (wrapped) position, Z its height above the floor.
    /// </summary>
    public static Vector3[] Corners(FurniturePiece piece)
    {
        var spec = Spec(piece);
        var across = new Vector2(MathF.Cos(piece.Rotation), MathF.Sin(piece.Rotation));
        var front = new Vector2(-MathF.Sin(piece.Rotation), MathF.Cos(piece.Rotation));
        var (sin, cos) = MathF.SinCos(piece.Tilt);
        var (rollSin, rollCos) = MathF.SinCos(piece.Roll);
        var corners = new Vector3[8];
        var i = 0;
        foreach (var localX in new[] { -spec.Width / 2f, spec.Width / 2f })
            foreach (var z in new[] { -spec.Depth / 2f, spec.Depth / 2f })
                foreach (var localY in new[] { 0f, spec.Height })
                {
                    var x = localX * rollCos - localY * rollSin;
                    var y = localX * rollSin + localY * rollCos;
                    var forward = z * cos + y * sin;
                    var up = y * cos - z * sin;
                    var floor = piece.Position + x * across + forward * front;
                    corners[i++] = new Vector3(floor.X, floor.Y, piece.Elevation + up);
                }
        return corners;
    }

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
            new(FurnitureType.SunLounger, 0.62f, 1.95f, 0.8f),                 // wooden slatted lounger
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
            new(FurnitureType.Mattress, 1.4f, 2.0f, 0.22f),
            new(FurnitureType.CardboardBox, 0.8f, 0.8f, 0.8f),
            new(FurnitureType.WallClock, 0.6f, 0.08f, 0.6f),                   // round wall clock, its face to the front
            new(FurnitureType.FountainBowl, 2.4f, 2.4f, 0.6f),                 // round stone bowl full of water
            new(FurnitureType.FountainColumn, 0.9f, 0.9f, 3.0f),               // stone column a bowl stands on
            new(FurnitureType.BasinKerb, 2.0f, 0.5f, 0.6f),                    // straight stone kerb of a basin
            new(FurnitureType.ShoppingCart, 0.55f, 1.0f, 1.0f),                // wire basket on a wheeled chassis, handle at the back
            new(FurnitureType.MealPlate, 0.3f, 0.3f, 0.05f),                   // a plate of food
            new(FurnitureType.ExitSign, 1.4f, 0.3f, 2.7f),                     // glowing EXIT sign with an arrow, high on two posts
            new(FurnitureType.Mannequin, 0.5f, 0.3f, 1.8f),                    // bare plastic shop mannequin, arms down, on a foot plate
            new(FurnitureType.DoorFrame, 1.3f, 0.3f, 2.3f),                    // free-standing door frame, no wall around it
            new(FurnitureType.FishTank, 0.6f, 0.35f, 0.4f),                    // small glass tank of water, gravel, stones and plants, with a lid
            new(FurnitureType.TankRack, 2.0f, 0.55f, 1.55f),                   // steel rack with three shelves of tanks (TankRackLevels)
            new(FurnitureType.BallPool, 5.0f, 3.2f, 0.6f),                     // low pool with padded foam walls and a padded floor
            new(FurnitureType.NetRailing, 1.0f, 0.06f, 0.9f),                  // one length of net railing between padded posts, on a pool wall
            new(FurnitureType.PlaySlide, 0.6f, 1.5f, 1.4f),                    // small kids' slide: ladder at the back, chute down to the front
            new(FurnitureType.FoamStep, 1.0f, 0.5f, 0.3f),                     // padded foam step up to a pool wall
            new(FurnitureType.PlayBall, 0.08f, 0.08f, 0.08f),                  // one hollow plastic ball
            new(FurnitureType.HotFoodCounter, 2.0f, 0.8f, 1.35f),             // steel counter, heated wells of food, glass sneeze guard
            new(FurnitureType.DessertCounter, 2.0f, 0.8f, 1.45f),             // chilled glass-fronted cabinet of cakes on a steel base
            new(FurnitureType.TrayRail, 2.0f, 0.35f, 0.9f),                   // steel tube rail on legs before the counters
            new(FurnitureType.TrayStation, 1.2f, 0.6f, 1.1f),                 // stacked trays and cutlery bins
            new(FurnitureType.CoffeeMachine, 0.45f, 0.5f, 0.65f),             // coffee machine, stood on a counter
            new(FurnitureType.Till, 1.2f, 0.7f, 1.1f),                        // checkout counter with a till
            new(FurnitureType.TrayTrolley, 0.6f, 0.75f, 1.6f),                // tray-return trolley, shelves of used trays
            new(FurnitureType.HighChair, 0.55f, 0.55f, 1.0f),                 // children's high chair
            new(FurnitureType.MenuBoard, 2.0f, 0.12f, 2.6f),                  // dark board (1.7–2.6 m) of dish names on two posts
            new(FurnitureType.Tray, 0.45f, 0.35f, 0.02f),                     // serving tray
            new(FurnitureType.Spill, 0.8f, 0.6f, 0.005f, IsFloorCovering: true), // spilled drink or food on the floor
            new(FurnitureType.Note, 0.21f, 0.15f, 0.01f),                     // folded hand-written note
            // Restroom Furniture (ticket 08j)
            new(FurnitureType.StallPartition, 0.04f, 1.6f, 2.0f),
            new(FurnitureType.StallDoor, 0.85f, 0.04f, 1.9f),
            new(FurnitureType.HandDryer, 0.28f, 0.20f, 0.32f),
            new(FurnitureType.PaperTowelDispenser, 0.30f, 0.18f, 0.40f),
            new(FurnitureType.BabyChangingTable, 0.88f, 0.55f, 0.50f),
            new(FurnitureType.RestroomSign, 0.40f, 0.05f, 0.30f),
            new(FurnitureType.OutOfOrderSign, 0.30f, 0.36f, 0.63f),
            new(FurnitureType.RestroomMirror, 2.4f, 0.04f, 1.0f),
            new(FurnitureType.TiledWall, 2.6f, 0.15f, 2.8f),
            new(FurnitureType.TiledFloor, 10.4f, 6.0f, 0.005f, IsFloorCovering: true),
            new(FurnitureType.LitterPaper, 0.30f, 0.30f, 0.005f, IsFloorCovering: true),
            // Lost and found Furniture (ticket 08k)
            new(FurnitureType.ServiceCounter, 2.2f, 0.8f, 1.05f),
            new(FurnitureType.ServiceBell, 0.12f, 0.12f, 0.10f),
            new(FurnitureType.LabelledBox, 0.50f, 0.40f, 0.35f),
            new(FurnitureType.UnsortedCart, 0.90f, 0.60f, 0.95f),
            new(FurnitureType.NoticeBoard, 1.20f, 0.05f, 0.80f),
            new(FurnitureType.LostAndFoundSign, 0.80f, 0.06f, 0.40f),
            // Camping ground Furniture (ticket 07z)
            new(FurnitureType.Tree, 8.4f, 8.8f, 6.5f),                         // broad oak
            new(FurnitureType.Tent, 2.6f, 2.6f, 1.75f),                        // dome
            new(FurnitureType.LanternTent, 1.0f, 0.2f, 1.2f, LightRadius: 4f),
            new(FurnitureType.CampfireFlame, 1.4f, 1.4f, 1.7f, LightRadius: 8f),
            new(FurnitureType.LogSeat, 1.96f, 0.53f, 0.33f),
            new(FurnitureType.Hammock, 3.2f, 1.38f, 1.09f),
            new(FurnitureType.Kayak, 3.2f, 0.77f, 0.26f),
            new(FurnitureType.FishingRod, 0.12f, 2.0f, 1.64f),
            new(FurnitureType.RubberDuck, 0.25f, 0.36f, 0.35f),
            new(FurnitureType.PlushBear, 1.06f, 0.71f, 1.5f),
            new(FurnitureType.TrailSign, 1.39f, 0.95f, 2.5f),
            new(FurnitureType.Axe, 0.063f, 0.25f, 0.7f),
            new(FurnitureType.SleepingBag, 0.75f, 2.0f, 0.22f),
            // Camping ground showroom staging (ticket 07z2)
            new(FurnitureType.TreeHouse, 14.6f, 15.1f, 22.5f, IsFixed: true),
            new(FurnitureType.MarshmallowTray, 0.6f, 0.4f, 0.08f),
            new(FurnitureType.CampBanner, 3.0f, 0.1f, 3.2f),
            new(FurnitureType.PriceTag, 0.4f, 0.05f, 1.0f),
            new(FurnitureType.CampChair, 0.85f, 0.82f, 0.61f),
            new(FurnitureType.CampLantern, 0.2f, 0.2f, 0.35f, LightRadius: 3f),
            new(FurnitureType.CampfireRing, 1.8f, 1.8f, 0.19f),
            new(FurnitureType.MarshmallowStick, 0.095f, 0.095f, 0.5f),

            // Variants of the most repeated types: other proportions, backs, legs or materials.
            new(FurnitureType.LabelledBox, 0.50f, 0.40f, 0.35f, Variant: 1), // open / spilled box
            new(FurnitureType.RestroomMirror, 2.4f, 0.04f, 1.0f, Variant: 1), // cracked mirror
            new(FurnitureType.TiledWall, 2.85f, 0.15f, 2.8f, Variant: 1),     // fills a side of the room between the front and back walls (5.7 m = two of them)
            new(FurnitureType.TiledWall, 1.85f, 0.15f, 2.8f, Variant: 2),     // the rest of a side wall beside a 1 m doorway
            new(FurnitureType.FishTank, 0.95f, 0.45f, 0.5f, Variant: 1),      // medium tank
            new(FurnitureType.FishTank, 2.4f, 0.9f, 1.9f, Variant: 2),        // large tank on its own cabinet (LargeFishTank)
            new(FurnitureType.Mannequin, 0.75f, 0.35f, 1.8f, Variant: 1),      // one arm raised, the other hand on the hip
            new(FurnitureType.Mannequin, 0.5f, 0.75f, 1.8f, Variant: 2),       // mid-stride
            new(FurnitureType.Mannequin, 0.46f, 0.28f, 1.95f, Variant: 3),     // tall and slim, arms down
            new(FurnitureType.Mannequin, 0.6f, 0.36f, 1.65f, Variant: 4),      // short and broad, arms down
            new(FurnitureType.Mannequin, 0.5f, 0.3f, 1.8f, Variant: 5),        // damaged: the left arm is missing
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
            new(FurnitureType.Sofa, 2.2f, 0.95f, 0.85f, Variant: 3, IsLeather: true), // leather sofa, rare
            new(FurnitureType.Wardrobe, 1.6f, 0.6f, 2.0f, Variant: 1),         // double wardrobe
            new(FurnitureType.Wardrobe, 0.6f, 0.6f, 2.1f, Variant: 2),         // narrow tall wardrobe
            new(FurnitureType.KitchenCabinet, 0.8f, 0.6f, 0.9f, Variant: 1),   // wide drawer cabinet
            new(FurnitureType.KitchenCabinet, 0.6f, 0.6f, 2.1f, Variant: 2),   // tall pantry
            new(FurnitureType.DisplayCabinet, 1.6f, 0.45f, 1.0f, Variant: 1),  // low, wide glass cabinet
            new(FurnitureType.DisplayCabinet, 0.6f, 0.45f, 1.9f, Variant: 2),  // narrow glass tower
            new(FurnitureType.WallClock, 1.0f, 0.08f, 1.0f, Variant: 1),       // large round clock
            new(FurnitureType.WallClock, 1.4f, 0.1f, 1.4f, Variant: 2),        // huge round clock
            new(FurnitureType.WallClock, 0.8f, 0.08f, 0.8f, Variant: 3),       // square clock
            new(FurnitureType.FountainBowl, 4.0f, 4.0f, 0.8f, Variant: 1),
            new(FurnitureType.FountainBowl, 6.0f, 6.0f, 1.0f, Variant: 2),
            new(FurnitureType.FountainBowl, 9.0f, 9.0f, 1.2f, Variant: 3),
            new(FurnitureType.FountainColumn, 0.5f, 0.5f, 2.0f, Variant: 1),   // slender column
            // The camping ground's sizes keep the proportions of their models (see game/CREDITS.md).
            new(FurnitureType.Tree, 3.7f, 4.2f, 9.0f, Variant: 1),             // tall conifer
            new(FurnitureType.Tree, 2.6f, 2.7f, 1.6f, Variant: 2),             // round bush
            new(FurnitureType.Tree, 3.0f, 2.2f, 1.55f, Variant: 3),            // flowering bush
            new(FurnitureType.Tent, 3.1f, 3.3f, 4.1f, Variant: 1),             // tipi
            new(FurnitureType.Tent, 4.6f, 3.6f, 2.7f, Variant: 2),             // family cabin tent
            new(FurnitureType.Tent, 5.0f, 5.0f, 2.8f, Variant: 3),             // round striped canvas bell tent
            new(FurnitureType.Tent, 2.4f, 2.8f, 1.45f, Variant: 4),            // A-frame ridge tent
            new(FurnitureType.Tent, 3.0f, 3.0f, 2.55f, Variant: 5),            // pop-up shelter
            new(FurnitureType.SleepingBag, 0.22f, 0.52f, 0.22f, Variant: 1),   // rolled up, lying along its depth
            new(FurnitureType.SunLounger, 0.7f, 1.9f, 0.95f, Variant: 1),      // white plastic lounger
            new(FurnitureType.SunLounger, 0.66f, 1.05f, 0.93f, Variant: 2),    // folding canvas deck chair
            new(FurnitureType.Parasol, 1.8f, 2.07f, 2.3f, Variant: 1),         // tilted beach umbrella
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

    /// <summary>The first of the Camping ground's dinosaur tints, which follow the ordinary ones.</summary>
    public const int FirstDinosaurTint = TintCount;

    /// <summary>
    /// The dinosaur tints: three saturated main colours (orange-red, green, teal) and the accents (cream, dark, yellow),
    /// from <see cref="FirstDinosaurTint"/> on.
    /// </summary>
    public enum DinosaurTints
    {
        /// <summary>The Tyrannosaurus's orange-red.</summary>
        OrangeRed = FirstDinosaurTint,
        /// <summary>The Brachiosaurus's green.</summary>
        Green,
        /// <summary>The Triceratops's teal.</summary>
        Teal,
        /// <summary>Teeth and horns.</summary>
        Cream,
        /// <summary>Eyes.</summary>
        Dark,
        /// <summary>Spikes and plates.</summary>
        Yellow,
    }

    /// <summary>Number of tints including the dinosaur tints.</summary>
    public const int TotalTintCount = FirstDinosaurTint + 6;

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
