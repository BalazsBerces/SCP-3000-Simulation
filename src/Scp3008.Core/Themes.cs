using System.Numerics;
using static Scp3008.Core.FurnitureType;
using static Scp3008.Core.PlacementRole;

namespace Scp3008.Core;

/// <summary>What a Display shows, e.g. a bedroom or a patio; decides which Furniture may stand in it.</summary>
public enum Theme
{
    LivingRoom,
    Bedroom,
    StorageShowroom,
    DiningRoom,
    Entry,
    HomeOffice,
    OpenPlanOffice,
    MeetingRoom,
    OfficeLounge,
    Kitchen,
    Bathroom,
    ApplianceDisplay,
    KidsBedroom,
    Playroom,
    Cafe,
    FoodMarket,
    Patio,
    PoolDeck,
    BbqCorner,
    GardenStock,
    Greenhouse,
    PlantHall,
    LampDisplay,
    TextileBoutique,
    GalleryWall,
    ShelvingRows,
    WarehouseRacks,
    BulkStacks,
}

/// <summary>How often a slot of a theme is furnished.</summary>
public enum ThemeTier
{
    /// <summary>In every Display of the theme; makes it readable at a glance.</summary>
    Core,
    /// <summary>In roughly half of the Displays, with a varied count.</summary>
    Common,
    /// <summary>Now and then (under 30%), at most three per Display; where the variety comes from.</summary>
    Accent,
}

/// <summary>Where a piece stands in its Display: a rule, not coordinates.</summary>
public enum PlacementRole
{
    /// <summary>Back against an edge of the Display, facing in; aligned, in a run when there are several.</summary>
    Wall,
    /// <summary>In a corner of the Display.</summary>
    Corner,
    /// <summary>In the open middle; larger Displays repeat it on a loose grid (tables, desk islands).</summary>
    Centre,
    /// <summary>Around its anchor, facing it (chairs around a table).</summary>
    Around,
    /// <summary>At the side of its anchor, aligned with it (nightstands by a bed).</summary>
    Beside,
    /// <summary>In front of its anchor (an office chair at a desk, a rug at the foot of a bed).</summary>
    InFront,
    /// <summary>A floor covering under its anchor (a rug under a coffee table).</summary>
    Under,
    /// <summary>Standing on its anchor (lamps on a nightstand, boxes on a pallet rack).</summary>
    OnTop,
    /// <summary>Anywhere free on the floor, turned freely.</summary>
    Loose,
    /// <summary>In rows filling the Display (shelving, racks, stock), with sold-out gaps.</summary>
    Row,
}

/// <summary>Which sides of its anchor an <see cref="PlacementRole.Around"/> piece may take.</summary>
internal enum AroundSides
{
    Any,
    Back,
    Front,
    Ends,
}

/// <summary>
/// One line of a theme: which types, how often, how many and where. Several types are
/// alternatives; the layout picks one of them.
/// </summary>
/// <param name="Tier">How often the slot is furnished.</param>
/// <param name="Types">The types it may place, all listed in the theme.</param>
/// <param name="Role">Where its pieces stand.</param>
/// <param name="Min">Fewest pieces (per anchor for roles relative to an anchor).</param>
/// <param name="Max">Most pieces (per anchor for roles relative to an anchor).</param>
/// <param name="Odds">Chance per Display that a common or accent slot is furnished.</param>
/// <param name="Anchors">For roles relative to another piece: the types it stands at or on.</param>
public sealed record ThemeSlot(ThemeTier Tier, IReadOnlyList<FurnitureType> Types, PlacementRole Role, int Min, int Max, float Odds,
    IReadOnlyList<FurnitureType> Anchors)
{
    /// <summary>Distance to the anchor, along a wall run, or between pieces in a row, in metres.</summary>
    internal float Gap { get; init; } = 0.05f;

    /// <summary>Grid spacing of <see cref="PlacementRole.Centre"/> groups, or Row length pitch (X) and aisle width (Y).</summary>
    internal Vector2 Pitch { get; init; } = new(6.5f, 6.5f);

    /// <summary>Wall pieces with the same non-zero group stand in one shared run (a kitchen line).</summary>
    internal int RunGroup { get; init; }

    /// <summary>Wall pieces stand side by side in a run rather than spread out.</summary>
    internal bool Run { get; init; }

    /// <summary>Centre groups of several pieces in two back-to-back rows (desk islands, kitchen islands).</summary>
    internal (int Min, int Max) Cluster { get; init; } = (1, 1);

    internal AroundSides Sides { get; init; } = AroundSides.Any;

    /// <summary>An in-front piece faces its anchor (a chair at a desk).</summary>
    internal bool Facing { get; init; }

    /// <summary>Picks a type per piece (or per row) rather than one for the whole slot.</summary>
    internal bool MixTypes { get; init; }

    /// <summary>Rows stand in back-to-back pairs.</summary>
    internal bool BackToBack { get; init; }

    /// <summary>Row slots: number of rows, or 0 to fill the rest of the Display.</summary>
    internal int Rows { get; init; }

    /// <summary>Centre groups: largest random offset from their grid point, in metres.</summary>
    internal float Jitter { get; init; } = 0.2f;

    /// <summary>Wall pieces keep this much free along the wall on each side (for pieces beside them).</summary>
    internal float WallMargin { get; init; } = 0.1f;

    /// <summary>Only anchors placed in this role count (stools at a kitchen island, not at the kitchen line).</summary>
    internal PlacementRole? AnchorRole { get; init; }

    /// <summary>On-top pieces stand on each other's tops rather than side by side (stacked boxes).</summary>
    internal bool Stack { get; init; }

    internal ThemeSlot On(params FurnitureType[] anchors) => this with { Anchors = anchors };
}

/// <summary>A theme's catalogue entry: its slots in core, common and accent tiers.</summary>
public sealed record ThemeSpec(Theme Theme, IReadOnlyList<ThemeSlot> Slots)
{
    /// <summary>Every type the theme allows.</summary>
    public IReadOnlySet<FurnitureType> Types { get; } = Slots.SelectMany(s => s.Types).ToHashSet();
}

/// <summary>
/// The theme catalogue: every theme with its tiers and placement roles, which Department types
/// may show which themes (by weight), and each Department type's own Furniture. Themes are strict:
/// a piece only ever stands in a Display whose theme lists its type.
/// </summary>
public static class ThemeCatalog
{
    private static readonly Dictionary<Theme, ThemeSpec> Specs = BuildThemes().ToDictionary(t => t.Theme);

    private static readonly Dictionary<DepartmentType, (Theme Theme, float Weight)[]> Weights = new()
    {
        [DepartmentType.Showrooms] = [(Theme.LivingRoom, 7), (Theme.Bedroom, 2), (Theme.DiningRoom, 2), (Theme.HomeOffice, 1), (Theme.Entry, 1), (Theme.StorageShowroom, 1)],
        [DepartmentType.BathroomsAndKitchens] = [(Theme.Kitchen, 3), (Theme.Bathroom, 3), (Theme.ApplianceDisplay, 1)],
        [DepartmentType.RestaurantAndFoodMarket] = [(Theme.Cafe, 3), (Theme.FoodMarket, 2)],
        [DepartmentType.PlantSection] = [(Theme.Greenhouse, 3), (Theme.PlantHall, 1), (Theme.Patio, 1)],
        [DepartmentType.KidsArea] = [(Theme.KidsBedroom, 2), (Theme.Playroom, 2), (Theme.ShelvingRows, 1)],
        [DepartmentType.LightingAndElectrical] = [(Theme.LampDisplay, 4), (Theme.ShelvingRows, 1)],
        [DepartmentType.SelfServeWarehouse] = [(Theme.WarehouseRacks, 5), (Theme.BulkStacks, 1)],
        [DepartmentType.Workspaces] = [(Theme.OpenPlanOffice, 3), (Theme.HomeOffice, 1), (Theme.MeetingRoom, 2), (Theme.OfficeLounge, 2)],
        [DepartmentType.TextilesAndDecor] = [(Theme.TextileBoutique, 3), (Theme.GalleryWall, 1), (Theme.Bedroom, 1), (Theme.LivingRoom, 1)],
        [DepartmentType.Outdoor] = [(Theme.Patio, 3), (Theme.PoolDeck, 1), (Theme.BbqCorner, 1), (Theme.GardenStock, 1)],
        [DepartmentType.DiningAndHallway] = [(Theme.DiningRoom, 3), (Theme.Entry, 2), (Theme.StorageShowroom, 1)],
    };

    // The Furniture each Department type showed before themes mixed the catalogue (tickets 03 and 03a).
    private static readonly Dictionary<DepartmentType, FurnitureType[]> Own = new()
    {
        [DepartmentType.SelfServeWarehouse] = [PalletRack, FlatPackBox, BoxCart],
        [DepartmentType.LightingAndElectrical] = [ShelfUnit, DisplayTable, TableLamp, FloorLamp, DeskLamp, ArcLamp],
        [DepartmentType.Showrooms] = [Sofa, Armchair, CoffeeTable, Bed, Wardrobe, Bookcase, Rug, FloorLamp, ArcLamp, TvBench, Dresser, Nightstand],
        [DepartmentType.BathroomsAndKitchens] = [KitchenCabinet, Fridge, Bathtub, Toilet, VanityUnit, WashingMachine, Stove],
        [DepartmentType.RestaurantAndFoodMarket] = [GroceryShelf, Freezer, Counter, DiningTable, Chair, BarStool],
        [DepartmentType.PlantSection] = [PlantTable, PottedPlant, LargePlant, PlanterBox, FurnitureType.Crop],
        [DepartmentType.KidsArea] = [ToyShelf, BunkBed, KidsTable, KidsChair, PlayCube, Rug, ToyChest],
        [DepartmentType.Workspaces] = [Desk, OfficeChair, FilingCabinet, MeetingTable, DeskLamp],
        [DepartmentType.TextilesAndDecor] = [CushionBin, BlanketStack, CurtainRack, FloorMirror, FrameDisplay],
        [DepartmentType.Outdoor] = [GardenTable, GardenChair, SunLounger, Parasol, Grill, PottedPlant],
        [DepartmentType.DiningAndHallway] = [DisplayCabinet, Sideboard, ShoeCabinet, CoatRack, HallBench, DiningTable, Chair],
    };

    /// <summary>Every theme in the catalogue.</summary>
    public static IEnumerable<ThemeSpec> All => Specs.Values;

    public static ThemeSpec Spec(Theme theme) => Specs[theme];

    /// <summary>The themes a Department type may show, with the weights it picks them by.</summary>
    public static IReadOnlyList<(Theme Theme, float Weight)> ThemesOf(DepartmentType department) => Weights[department];

    /// <summary>
    /// A Department type's own Furniture, which keeps it recognisable: the types it showed before
    /// the themes mixed in Furniture from the whole catalogue.
    /// </summary>
    public static IReadOnlyList<FurnitureType> OwnTypes(DepartmentType department) => Own[department];

    private static ThemeSlot Core(PlacementRole role, int min, int max, params FurnitureType[] types) =>
        new(ThemeTier.Core, types, role, min, max, 1f, []);

    private static ThemeSlot Common(float odds, PlacementRole role, int min, int max, params FurnitureType[] types) =>
        new(ThemeTier.Common, types, role, min, max, odds, []);

    private static ThemeSlot Accent(float odds, PlacementRole role, int min, int max, params FurnitureType[] types) =>
        new(ThemeTier.Accent, types, role, min, max, odds, []);

    private static IEnumerable<ThemeSpec> BuildThemes()
    {
        yield return new(Theme.LivingRoom,
        [
            Core(Centre, 1, 1, CoffeeTable) with { Pitch = new(5.5f, 5.5f), Jitter = 0.3f },
            Core(Around, 1, 1, Sofa).On(CoffeeTable) with { Sides = AroundSides.Back, Gap = 0.45f, AnchorRole = Centre },
            Core(Under, 1, 1, Rug).On(CoffeeTable) with { AnchorRole = Centre },
            Common(0.85f, Around, 2, 2, Armchair).On(CoffeeTable) with { Sides = AroundSides.Ends, Gap = 0.35f, AnchorRole = Centre },
            // A second table beside a reading seat uses floor space released by optional Lamps.
            Common(0.6f, Beside, 1, 1, CoffeeTable).On(Armchair) with { Gap = 0.2f },
            Common(0.6f, Wall, 2, 4, Bookcase) with { Run = true },
            Common(0.35f, Corner, 1, 1, FloorLamp, ArcLamp),
            Common(0.5f, Wall, 1, 1, Sideboard),
            Common(0.6f, Wall, 1, 1, TvBench),
            Accent(0.25f, Corner, 1, 2, PottedPlant),
            Accent(0.2f, Corner, 1, 1, LargePlant),
            Accent(0.15f, Wall, 1, 2, FrameDisplay),
            Accent(0.12f, Loose, 1, 1, CushionBin),
            Accent(0.12f, Loose, 1, 1, BlanketStack),
            Accent(0.25f, OnTop, 1, 1, TableLamp).On(Sideboard),
            Accent(0.15f, Wall, 1, 1, DisplayCabinet),
        ]);
        yield return new(Theme.Bedroom,
        [
            Core(Wall, 1, 1, Bed) with { WallMargin = 1.0f },
            Core(Beside, 1, 2, Nightstand).On(Bed),
            Common(0.7f, Wall, 1, 4, Wardrobe, Dresser) with { Run = true },
            Common(0.6f, InFront, 1, 1, Rug).On(Bed) with { Gap = -0.7f },
            Common(0.35f, OnTop, 1, 1, TableLamp, DeskLamp).On(Nightstand),
            Common(0.25f, Corner, 1, 1, FloorLamp),
            Accent(0.2f, Wall, 1, 1, FloorMirror),
            Accent(0.2f, OnTop, 1, 1, BlanketStack).On(Bed),
            Accent(0.15f, Loose, 1, 1, CushionBin),
            Accent(0.2f, Corner, 1, 1, Armchair),
            Accent(0.12f, Wall, 1, 1, CurtainRack),
            Accent(0.15f, Wall, 1, 1, FrameDisplay),
            Accent(0.2f, Corner, 1, 1, PottedPlant),
        ]);
        yield return new(Theme.StorageShowroom,
        [
            // Display cabinets join the run so a whole hall of them forms a cabinet gallery.
            Core(Wall, 3, 6, Wardrobe, Dresser, DisplayCabinet) with { Run = true, MixTypes = true },
            Common(0.5f, Wall, 1, 3, Bookcase) with { Run = true },
            Common(0.4f, Wall, 1, 1, DisplayCabinet),
            Common(0.4f, Wall, 1, 1, Sideboard),
            // Islands of low storage back to back in the middle of the floor.
            Common(0.6f, Centre, 1, 1, Dresser, Sideboard, DisplayCabinet) with { Pitch = new(4.4f, 3.4f), Cluster = (2, 4), Jitter = 0.15f },
            Common(0.5f, Loose, 1, 1, Rug),
            Accent(0.2f, Corner, 1, 1, Armchair),
            Accent(0.15f, Loose, 1, 1, Nightstand),
            Accent(0.2f, Wall, 1, 1, FloorMirror),
            Accent(0.15f, Wall, 1, 2, ShoeCabinet),
            Accent(0.2f, Corner, 1, 1, PottedPlant),
        ]);
        yield return new(Theme.DiningRoom,
        [
            Core(Centre, 1, 1, DiningTable) with { Pitch = new(3.7f, 4.4f), Jitter = 0.3f },
            Core(Around, 2, 8, Chair).On(DiningTable),
            Common(0.5f, Wall, 1, 1, Sideboard),
            Common(0.4f, Wall, 1, 2, DisplayCabinet),
            Common(0.5f, Under, 1, 1, Rug).On(DiningTable),
            Common(0.25f, Corner, 1, 1, ArcLamp, FloorLamp),
            Accent(0.15f, Loose, 1, 2, BarStool),
            Accent(0.2f, Corner, 1, 2, PottedPlant),
            Accent(0.15f, Corner, 1, 1, LargePlant),
            Accent(0.15f, Wall, 1, 2, FrameDisplay),
            Accent(0.12f, Wall, 1, 1, FloorMirror),
            Accent(0.25f, OnTop, 1, 1, TableLamp).On(Sideboard),
            // A chair wall: spare chairs lined up on show.
            Accent(0.2f, Wall, 3, 6, Chair) with { Run = true, Gap = 0.12f },
        ]);
        yield return new(Theme.Entry,
        [
            Core(Wall, 3, 6, ShoeCabinet, CoatRack, HallBench) with { MixTypes = true },
            Common(0.5f, Wall, 1, 3, FloorMirror),
            Common(0.5f, Wall, 2, 4, ShoeCabinet) with { Run = true, Gap = 0.05f },
            Common(0.5f, Loose, 1, 1, Rug),
            Common(0.4f, Wall, 1, 1, Sideboard),
            Accent(0.2f, Corner, 1, 1, PottedPlant),
            Accent(0.15f, Wall, 1, 2, FrameDisplay),
            Accent(0.25f, OnTop, 1, 1, TableLamp).On(Sideboard),
            Accent(0.15f, Wall, 1, 1, Dresser),
        ]);
        yield return new(Theme.HomeOffice,
        [
            Core(Wall, 1, 2, Desk) with { WallMargin = 0.3f },
            Core(InFront, 1, 1, OfficeChair).On(Desk) with { Facing = true },
            Common(0.45f, OnTop, 1, 1, DeskLamp).On(Desk),
            Common(0.5f, Wall, 1, 3, Bookcase) with { Run = true },
            Common(0.4f, Wall, 1, 3, FilingCabinet) with { Run = true },
            Accent(0.2f, Corner, 1, 1, Armchair),
            Accent(0.2f, Loose, 1, 1, Rug),
            Accent(0.2f, Corner, 1, 1, PottedPlant),
            Accent(0.15f, Corner, 1, 1, LargePlant),
            Accent(0.2f, Corner, 1, 1, FloorLamp),
        ]);
        yield return new(Theme.OpenPlanOffice,
        [
            Core(Centre, 1, 1, Desk) with { Pitch = new(4.8f, 3.6f), Cluster = (4, 6), Jitter = 0.15f },
            Core(InFront, 1, 1, OfficeChair).On(Desk) with { Facing = true },
            Common(0.45f, OnTop, 1, 1, DeskLamp).On(Desk),
            Common(0.6f, Wall, 1, 4, FilingCabinet) with { Run = true },
            Common(0.5f, Corner, 1, 2, LargePlant),
            Accent(0.2f, Wall, 1, 2, ShelfUnit) with { Run = true },
            Accent(0.2f, Wall, 1, 2, Bookcase) with { Run = true },
            Accent(0.2f, Corner, 1, 2, PottedPlant),
            Accent(0.12f, Wall, 1, 1, Counter),
        ]);
        yield return new(Theme.MeetingRoom,
        [
            Core(Centre, 1, 1, MeetingTable) with { Pitch = new(6f, 5f) },
            Core(Around, 4, 10, OfficeChair).On(MeetingTable),
            Common(0.5f, Corner, 1, 2, LargePlant),
            Common(0.5f, Wall, 1, 1, Sideboard),
            Accent(0.2f, Wall, 1, 2, FrameDisplay),
            Accent(0.2f, Corner, 1, 1, FloorLamp),
            Accent(0.2f, Corner, 1, 1, PottedPlant),
            Accent(0.15f, Wall, 1, 1, DisplayCabinet),
        ]);
        yield return new(Theme.OfficeLounge,
        [
            Core(Centre, 1, 1, CoffeeTable) with { Pitch = new(5.5f, 5.5f), Jitter = 0.3f },
            Core(Around, 2, 4, Sofa, Armchair).On(CoffeeTable) with { Gap = 0.4f, MixTypes = true },
            Common(0.5f, Under, 1, 1, Rug).On(CoffeeTable),
            Common(0.5f, Corner, 1, 2, LargePlant),
            Common(0.25f, Corner, 1, 1, FloorLamp, ArcLamp),
            Accent(0.2f, Wall, 1, 2, Bookcase) with { Run = true },
            Accent(0.2f, Corner, 1, 1, PottedPlant),
            Accent(0.2f, Loose, 1, 1, CushionBin),
        ]);
        yield return new(Theme.Kitchen,
        [
            Core(Wall, 6, 12, KitchenCabinet) with { Run = true, RunGroup = 1, Gap = 0f },
            Core(Wall, 1, 1, Stove) with { Run = true, RunGroup = 1, Gap = 0f },
            Core(Wall, 1, 1, Fridge) with { Run = true, RunGroup = 1, Gap = 0f },
            Common(0.5f, Wall, 1, 1, WashingMachine) with { Run = true, RunGroup = 1, Gap = 0f },
            // An island of cabinets with stools, or a kitchen table with chairs.
            Common(0.5f, Centre, 1, 1, KitchenCabinet) with { Cluster = (2, 6), Jitter = 0.1f },
            Common(0.9f, Around, 1, 1, BarStool).On(KitchenCabinet) with { Sides = AroundSides.Front, Gap = 0.1f, AnchorRole = Centre },
            Common(0.45f, Centre, 1, 1, DiningTable) with { Jitter = 0.3f },
            Common(0.95f, Around, 2, 4, Chair).On(DiningTable),
            Accent(0.2f, Corner, 1, 1, PottedPlant),
            Accent(0.2f, Loose, 1, 1, Rug),
            Accent(0.15f, Wall, 1, 1, Sideboard),
            Accent(0.15f, Wall, 1, 1, DisplayCabinet),
        ]);
        yield return new(Theme.Bathroom,
        [
            Core(Wall, 2, 4, Toilet),
            Core(Wall, 1, 3, VanityUnit) with { Run = true },
            Common(0.6f, Wall, 1, 2, Bathtub),
            Common(0.4f, Wall, 1, 2, WashingMachine) with { Run = true, Gap = 0f },
            Common(0.5f, Wall, 1, 2, FloorMirror),
            Accent(0.2f, Loose, 1, 1, CushionBin),
            Accent(0.2f, Loose, 1, 1, BlanketStack),
            Accent(0.2f, Corner, 1, 1, PottedPlant),
        ]);
        yield return new(Theme.ApplianceDisplay,
        [
            Core(Row, 1, 1, Fridge, Stove, WashingMachine) with { MixTypes = true, Gap = 0.35f, Pitch = new(0f, 2.2f) },
            Common(0.5f, Row, 1, 1, Freezer) with { Rows = 1, Gap = 0.5f, Pitch = new(0f, 2.2f) },
            Common(0.5f, Row, 1, 1, KitchenCabinet) with { Rows = 1, Gap = 0f, Pitch = new(0f, 2.2f) },
            Accent(0.25f, Loose, 1, 2, BoxCart),
        ]);
        yield return new(Theme.KidsBedroom,
        [
            Core(Wall, 1, 1, BunkBed, Bed) with { WallMargin = 0.6f },
            Common(0.6f, Wall, 1, 4, ToyShelf) with { Run = true },
            Common(0.5f, Wall, 1, 2, ToyChest),
            Common(0.5f, Loose, 1, 1, Rug),
            Common(0.4f, Beside, 1, 1, Nightstand).On(Bed, BunkBed),
            Common(0.6f, Centre, 1, 1, KidsTable) with { Jitter = 0.4f },
            Common(0.95f, Around, 2, 4, KidsChair).On(KidsTable),
            // A desk corner.
            Accent(0.25f, Wall, 1, 1, Desk) with { WallMargin = 0.2f },
            Accent(0.9f, OnTop, 1, 1, DeskLamp).On(Desk),
            Accent(0.15f, Loose, 1, 1, CushionBin),
            Accent(0.2f, Loose, 1, 3, PlayCube),
            Accent(0.15f, Corner, 1, 1, CoatRack),
            Accent(0.15f, Wall, 1, 1, FloorMirror),
            Accent(0.15f, OnTop, 1, 1, BlanketStack).On(Bed, BunkBed),
        ]);
        yield return new(Theme.Playroom,
        [
            Core(Centre, 1, 1, KidsTable) with { Pitch = new(3.4f, 3.4f), Jitter = 0.5f },
            Core(Around, 2, 4, KidsChair).On(KidsTable),
            Core(Loose, 4, 8, PlayCube),
            Common(0.6f, Wall, 1, 3, ToyShelf) with { Run = true },
            Common(0.5f, Wall, 1, 2, ToyChest),
            Common(0.5f, Loose, 1, 1, Rug),
            Accent(0.2f, Loose, 1, 1, CushionBin),
            Accent(0.2f, Loose, 1, 1, BlanketStack),
            Accent(0.2f, Corner, 1, 1, PottedPlant),
        ]);
        yield return new(Theme.Cafe,
        [
            Core(Centre, 1, 1, DiningTable) with { Pitch = new(3.6f, 3.3f), Jitter = 0.25f },
            // Chairs, or stools at high tables.
            Core(Around, 2, 4, Chair, BarStool).On(DiningTable),
            Common(0.5f, Wall, 1, 3, Counter) with { Run = true, Gap = 0f },
            Common(0.95f, Around, 1, 2, BarStool).On(Counter) with { Sides = AroundSides.Front, Gap = 0.15f },
            // Booth seating along a wall.
            Common(0.4f, Wall, 2, 4, HallBench) with { Run = true, Gap = 0.1f },
            Common(0.5f, Loose, 2, 4, PottedPlant, PlanterBox),
            Accent(0.2f, Wall, 1, 2, DisplayCabinet),
            Accent(0.2f, Wall, 1, 1, Sideboard),
            // A terrace table under a parasol.
            Accent(0.2f, Loose, 1, 1, GardenTable),
            Accent(0.9f, Beside, 1, 1, Parasol).On(GardenTable) with { Gap = 0.1f },
            Accent(0.15f, Corner, 1, 1, FloorLamp),
        ]);
        yield return new(Theme.FoodMarket,
        [
            Core(Row, 1, 1, GroceryShelf) with { BackToBack = true, Gap = 0f, Pitch = new(0f, 2.4f) },
            Core(Row, 1, 1, Freezer) with { Rows = 1, Gap = 0.4f, Pitch = new(0f, 2.4f) },
            Common(0.5f, Wall, 1, 3, Counter) with { Run = true, Gap = 0f },
            Common(0.4f, Row, 1, 1, Fridge) with { Rows = 1, Gap = 0.1f, Pitch = new(0f, 2.2f) },
            Common(0.4f, Loose, 1, 2, BoxCart),
            // A stand of herbs.
            Accent(0.2f, Loose, 1, 1, PlantTable),
            Accent(0.95f, OnTop, 3, 5, PottedPlant).On(PlantTable),
        ]);
        yield return new(Theme.Patio,
        [
            Core(Centre, 1, 1, GardenTable) with { Pitch = new(4.2f, 3.6f), Jitter = 0.35f },
            Core(Around, 3, 6, GardenChair).On(GardenTable),
            Common(0.6f, Beside, 1, 1, Parasol).On(GardenTable) with { Gap = 0.1f },
            Common(0.5f, Wall, 1, 3, PlanterBox) with { Run = true, Gap = 0.3f },
            Common(0.5f, Corner, 1, 2, PottedPlant, LargePlant),
            Accent(0.2f, Under, 1, 1, Rug).On(GardenTable),
            Accent(0.2f, Wall, 1, 1, HallBench),
            Accent(0.15f, Corner, 1, 1, Grill),
            Accent(0.2f, Loose, 1, 1, SunLounger),
            Accent(0.15f, Loose, 1, 2, BarStool),
        ]);
        yield return new(Theme.PoolDeck,
        [
            Core(Wall, 4, 8, SunLounger) with { Run = true, Gap = 0.4f },
            Common(0.5f, Loose, 1, 2, Parasol),
            Common(0.6f, Wall, 1, 3, PlanterBox),
            Common(0.5f, Loose, 1, 1, GardenTable),
            // The "pool".
            Accent(0.25f, Centre, 1, 1, Rug),
            Accent(0.2f, Corner, 1, 2, LargePlant),
        ]);
        yield return new(Theme.BbqCorner,
        [
            Core(Corner, 1, 2, Grill),
            Common(0.75f, Centre, 1, 1, GardenTable) with { Pitch = new(4.2f, 3.6f), Jitter = 0.35f },
            Common(0.95f, Around, 2, 4, GardenChair, HallBench).On(GardenTable) with { Gap = 0.1f },
            Common(0.5f, Wall, 1, 3, PlanterBox) with { Run = true, Gap = 0.3f },
            Accent(0.2f, Beside, 1, 1, Parasol).On(GardenTable) with { Gap = 0.1f },
            Accent(0.2f, Loose, 1, 2, BarStool),
        ]);
        yield return new(Theme.GardenStock,
        [
            Core(Row, 1, 1, GardenChair, GardenTable, SunLounger, Parasol, Grill, PlanterBox) with { MixTypes = true, Gap = 0.25f, Pitch = new(0f, 1.6f) },
            Common(0.5f, Loose, 1, 2, BoxCart),
            Accent(0.15f, Loose, 1, 3, PottedPlant),
        ]);
        yield return new(Theme.Greenhouse,
        [
            Core(Centre, 1, 1, PlantTable) with { Pitch = new(3.1f, 2.6f), Jitter = 0.1f },
            Core(OnTop, 3, 6, PottedPlant).On(PlantTable),
            // Crops (ticket 17f) stand among the decorative pots; only the Plant section grows them.
            Common(0.6f, OnTop, 1, 3, FurnitureType.Crop).On(PlantTable),
            Common(0.5f, Loose, 1, 4, LargePlant),
            Common(0.5f, Wall, 1, 3, PlanterBox) with { Run = true, Gap = 0.2f },
            Accent(0.2f, Loose, 1, 1, GardenTable),
            Accent(0.9f, Around, 1, 2, GardenChair).On(GardenTable),
        ]);
        yield return new(Theme.PlantHall,
        [
            Core(Centre, 1, 1, LargePlant) with { Pitch = new(1.6f, 1.6f), Jitter = 0.35f },
            Common(0.6f, Wall, 2, 4, PlanterBox) with { Gap = 0.2f },
            Accent(0.25f, Loose, 1, 3, PottedPlant),
            Accent(0.35f, Loose, 1, 3, FurnitureType.Crop),
        ]);
        yield return new(Theme.LampDisplay,
        [
            Core(Centre, 1, 1, DisplayTable) with { Pitch = new(4.3f, 3.25f), Jitter = 0.15f },
            Core(OnTop, 2, 3, TableLamp, DeskLamp).On(DisplayTable, Nightstand, Desk, Sideboard, Dresser) with { MixTypes = true },
            Common(0.7f, Loose, 1, 3, FloorLamp, ArcLamp) with { MixTypes = true },
            Common(0.4f, Wall, 1, 3, ShelfUnit) with { Run = true, Gap = 0f },
            // Lamps staged on other Furniture, and an arc lamp over a seat.
            Accent(0.25f, Wall, 1, 2, Nightstand, Desk, Sideboard, Dresser) with { MixTypes = true },
            Accent(0.2f, Loose, 1, 1, Sofa, Armchair),
            Accent(0.95f, Beside, 1, 1, ArcLamp).On(Sofa, Armchair) with { Gap = 0.05f },
        ]);
        yield return new(Theme.TextileBoutique,
        [
            Core(Row, 1, 1, CushionBin, BlanketStack, CurtainRack) with { MixTypes = true, BackToBack = true, Gap = 0.3f, Pitch = new(0f, 1.4f) },
            Common(0.5f, Loose, 1, 2, Rug),
            Common(0.5f, Wall, 1, 3, FloorMirror),
            // A bed or sofa dressed with blankets.
            Accent(0.15f, Wall, 1, 1, Bed, Sofa),
            Accent(0.95f, OnTop, 1, 1, BlanketStack).On(Bed, Sofa),
            Accent(0.2f, Corner, 1, 1, Armchair),
            Accent(0.2f, Corner, 1, 2, PottedPlant),
        ]);
        yield return new(Theme.GalleryWall,
        [
            Core(Wall, 6, 12, FrameDisplay, FloorMirror) with { Run = true, MixTypes = true, Gap = 0.2f },
            Common(0.5f, Wall, 1, 1, Sideboard),
            Common(0.5f, Wall, 1, 2, DisplayCabinet),
            Accent(0.2f, Corner, 1, 1, Armchair),
            Accent(0.2f, Corner, 1, 2, PottedPlant),
        ]);
        yield return new(Theme.ShelvingRows,
        [
            Core(Row, 1, 1, ShelfUnit, ToyShelf, GroceryShelf) with { BackToBack = true, Gap = 0f, Pitch = new(0f, 2.4f) },
            Common(0.5f, Loose, 1, 2, BoxCart),
        ]);
        yield return new(Theme.WarehouseRacks,
        [
            Core(Row, 1, 1, PalletRack) with { BackToBack = true, Gap = 0f, Pitch = new(0f, 3.1f) },
            Core(OnTop, 1, 2, FlatPackBox).On(PalletRack),
            Common(0.5f, Loose, 1, 2, BoxCart),
            // An assembled sample at the end of a row.
            Accent(0.28f, Loose, 1, 1, Wardrobe, KitchenCabinet, Bookcase, Dresser, ShoeCabinet, Sideboard, Desk, FilingCabinet, Nightstand),
        ]);
        yield return new(Theme.BulkStacks,
        [
            Core(Centre, 1, 1, FlatPackBox) with { Pitch = new(1.8f, 2f), Jitter = 0.05f },
            Core(OnTop, 0, 2, FlatPackBox).On(FlatPackBox) with { Stack = true },
            Common(0.6f, Loose, 1, 3, BoxCart),
        ]);
    }
}
