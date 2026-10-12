namespace Scp3008.Core.Generation;

/// <summary>How an archetype divides its department cell into Displays.</summary>
internal enum ArchetypeKind
{
    /// <summary>A room partition, each room turned its own way with a theme of its own.</summary>
    Rooms,
    /// <summary>One Display filling the cell, aligned with the Department so rows line up across cells.</summary>
    Hall,
    /// <summary>Two aligned Displays in strips across the cell, of two different themes.</summary>
    Split,
    /// <summary>Free-standing sets placed apart on an open floor.</summary>
    OpenFloor,
}

/// <summary>How Row pieces are laid out.</summary>
internal enum RowStyle
{
    Straight,
    /// <summary>Every other row shifted by half a piece.</summary>
    Staggered,
    /// <summary>Short irregular clusters instead of long rows (a market of bins).</summary>
    Clusters,
}

/// <summary>
/// One layout archetype: a parameterised way of laying out a department cell. Weights are shares
/// of the Department type's cells; themes must all be allowed in the Department type.
/// </summary>
internal sealed record Archetype(LayoutArchetype Id, DepartmentType Department, float Weight, ArchetypeKind Kind, Theme[] Themes)
{
    /// <summary>For <see cref="ArchetypeKind.Split"/>: the themes of the second strip.</summary>
    public Theme[] SecondThemes { get; init; } = [];

    /// <summary>For <see cref="ArchetypeKind.Rooms"/>: the partitions it picks from, by weight.</summary>
    public (RoomPartition Partition, float Weight)[] Partitions { get; init; } = MixedPartitions;

    /// <summary>Range of the factor applied to grid and row spacing, so cells differ in density.</summary>
    public (float Min, float Max) Spacing { get; init; } = (0.92f, 1.12f);

    public RowStyle RowStyle { get; init; } = RowStyle.Straight;

    /// <summary>Rows are cut by a cross aisle in the middle.</summary>
    public bool CrossAisle { get; init; }

    /// <summary>Centre clusters stand in one row facing the same way (cubicle rows), not in islands.</summary>
    public bool SingleRowClusters { get; init; }

    /// <summary>Every other grid row of centre groups is shifted by half a pitch and turned a quarter.</summary>
    public bool Stagger { get; init; }

    /// <summary>A variant the archetype prefers for a type (long tables in a canteen).</summary>
    public (FurnitureType Type, int Variant)? PreferredVariant { get; init; }

    public static readonly (RoomPartition, float)[] MixedPartitions =
        [(RoomPartition.OneRoom, 0.15f), (RoomPartition.TwoRooms, 0.25f), (RoomPartition.ThreeRooms, 0.25f), (RoomPartition.FourRooms, 0.35f)];

    public static readonly (RoomPartition, float)[] LargeRooms =
        [(RoomPartition.OneRoom, 0.45f), (RoomPartition.TwoRooms, 0.55f)];

    public static readonly (RoomPartition, float)[] SmallRooms =
        [(RoomPartition.ThreeRooms, 0.5f), (RoomPartition.FourRooms, 0.5f)];
}

/// <summary>The layout archetypes of every Department type.</summary>
internal static class LayoutArchetypes
{
    private static readonly Archetype[] All =
    [
        // Showrooms
        new(LayoutArchetype.ShowroomRoomSets, DepartmentType.Showrooms, 0.35f, ArchetypeKind.Rooms,
            [Theme.LivingRoom, Theme.Bedroom, Theme.DiningRoom, Theme.HomeOffice, Theme.Entry, Theme.StorageShowroom]),
        new(LayoutArchetype.ShowroomBedGallery, DepartmentType.Showrooms, 0.2f, ArchetypeKind.Rooms, [Theme.Bedroom, Theme.StorageShowroom])
            { Partitions = Archetype.LargeRooms },
        new(LayoutArchetype.ShowroomLivingSuites, DepartmentType.Showrooms, 0.25f, ArchetypeKind.Rooms,
            [Theme.LivingRoom, Theme.DiningRoom, Theme.HomeOffice]) { Partitions = Archetype.SmallRooms },
        new(LayoutArchetype.ShowroomOpenFloor, DepartmentType.Showrooms, 0.2f, ArchetypeKind.OpenFloor,
            [Theme.LivingRoom, Theme.Bedroom, Theme.DiningRoom, Theme.HomeOffice, Theme.Entry]),

        // Bathrooms & kitchens
        new(LayoutArchetype.KitchenRoomSets, DepartmentType.BathroomsAndKitchens, 0.3f, ArchetypeKind.Rooms, [Theme.Kitchen])
            { Partitions = Archetype.SmallRooms },
        new(LayoutArchetype.BathroomRoomSets, DepartmentType.BathroomsAndKitchens, 0.3f, ArchetypeKind.Rooms, [Theme.Bathroom])
            { Partitions = Archetype.SmallRooms },
        new(LayoutArchetype.KitchenAndBathRoomSets, DepartmentType.BathroomsAndKitchens, 0.25f, ArchetypeKind.Rooms, [Theme.Kitchen, Theme.Bathroom]),
        new(LayoutArchetype.ApplianceHall, DepartmentType.BathroomsAndKitchens, 0.15f, ArchetypeKind.Split, [Theme.ApplianceDisplay])
            { SecondThemes = [Theme.Kitchen], Spacing = (0.95f, 1.2f) },

        // Restaurant/food market
        new(LayoutArchetype.CafeHall, DepartmentType.RestaurantAndFoodMarket, 0.3f, ArchetypeKind.Hall, [Theme.Cafe]) { Stagger = true },
        new(LayoutArchetype.CanteenHall, DepartmentType.RestaurantAndFoodMarket, 0.2f, ArchetypeKind.Hall, [Theme.Cafe])
            { PreferredVariant = (FurnitureType.DiningTable, 2), Spacing = (1.05f, 1.2f) },
        new(LayoutArchetype.MarketAisles, DepartmentType.RestaurantAndFoodMarket, 0.3f, ArchetypeKind.Hall, [Theme.FoodMarket]) { Spacing = (0.95f, 1.1f) },
        new(LayoutArchetype.MarketAndCafe, DepartmentType.RestaurantAndFoodMarket, 0.2f, ArchetypeKind.Split, [Theme.FoodMarket])
            { SecondThemes = [Theme.Cafe] },

        // Plant section
        new(LayoutArchetype.GreenhouseTables, DepartmentType.PlantSection, 0.38f, ArchetypeKind.Hall, [Theme.Greenhouse]) { Spacing = (0.95f, 1.1f) },
        new(LayoutArchetype.PlantHall, DepartmentType.PlantSection, 0.22f, ArchetypeKind.Hall, [Theme.PlantHall]),
        new(LayoutArchetype.GreenhouseAndPatio, DepartmentType.PlantSection, 0.2f, ArchetypeKind.Split, [Theme.Greenhouse])
            { SecondThemes = [Theme.Patio, Theme.PlantHall] },
        new(LayoutArchetype.GreenhouseRooms, DepartmentType.PlantSection, 0.2f, ArchetypeKind.Rooms, [Theme.Greenhouse, Theme.Patio])
            { Partitions = Archetype.LargeRooms },

        // Kids' area
        new(LayoutArchetype.KidsRoomSets, DepartmentType.KidsArea, 0.35f, ArchetypeKind.Rooms, [Theme.KidsBedroom, Theme.Playroom]),
        new(LayoutArchetype.KidsOpenFloor, DepartmentType.KidsArea, 0.2f, ArchetypeKind.OpenFloor, [Theme.Playroom, Theme.KidsBedroom]),
        new(LayoutArchetype.ToyShelvingRows, DepartmentType.KidsArea, 0.25f, ArchetypeKind.Hall, [Theme.ShelvingRows]),
        new(LayoutArchetype.ToyShelvesAndRooms, DepartmentType.KidsArea, 0.2f, ArchetypeKind.Split, [Theme.ShelvingRows])
            { SecondThemes = [Theme.Playroom, Theme.KidsBedroom] },

        // Lighting & electrical
        new(LayoutArchetype.LampTableGrid, DepartmentType.LightingAndElectrical, 0.35f, ArchetypeKind.Hall, [Theme.LampDisplay]) { Spacing = (0.9f, 1.05f) },
        new(LayoutArchetype.LampStaggeredTables, DepartmentType.LightingAndElectrical, 0.3f, ArchetypeKind.Hall, [Theme.LampDisplay])
            { Stagger = true, Spacing = (0.9f, 1.0f) },
        new(LayoutArchetype.LampRooms, DepartmentType.LightingAndElectrical, 0.2f, ArchetypeKind.Rooms, [Theme.LampDisplay])
            { Partitions = Archetype.LargeRooms, Spacing = (0.85f, 0.95f) },
        new(LayoutArchetype.LampShelvingRows, DepartmentType.LightingAndElectrical, 0.15f, ArchetypeKind.Split, [Theme.ShelvingRows])
            { SecondThemes = [Theme.LampDisplay] },

        // Self-serve warehouse: rack rows keep one pitch so they line up across the Department.
        new(LayoutArchetype.RackRows, DepartmentType.SelfServeWarehouse, 0.38f, ArchetypeKind.Hall, [Theme.WarehouseRacks]) { Spacing = (1f, 1f) },
        new(LayoutArchetype.RackRowsCrossAisle, DepartmentType.SelfServeWarehouse, 0.32f, ArchetypeKind.Hall, [Theme.WarehouseRacks])
            { Spacing = (1f, 1f), CrossAisle = true },
        new(LayoutArchetype.RacksAndBulk, DepartmentType.SelfServeWarehouse, 0.18f, ArchetypeKind.Split, [Theme.WarehouseRacks])
            { SecondThemes = [Theme.BulkStacks], Spacing = (1f, 1f) },
        new(LayoutArchetype.BulkStackHall, DepartmentType.SelfServeWarehouse, 0.12f, ArchetypeKind.Hall, [Theme.BulkStacks]),

        // Workspaces
        new(LayoutArchetype.OpenPlanHall, DepartmentType.Workspaces, 0.3f, ArchetypeKind.Hall, [Theme.OpenPlanOffice]),
        new(LayoutArchetype.CubicleRows, DepartmentType.Workspaces, 0.2f, ArchetypeKind.Hall, [Theme.OpenPlanOffice]) { SingleRowClusters = true },
        new(LayoutArchetype.OfficeRoomSets, DepartmentType.Workspaces, 0.3f, ArchetypeKind.Rooms,
            [Theme.OpenPlanOffice, Theme.HomeOffice, Theme.MeetingRoom, Theme.OfficeLounge]),
        new(LayoutArchetype.OfficeOpenFloor, DepartmentType.Workspaces, 0.2f, ArchetypeKind.OpenFloor,
            [Theme.OpenPlanOffice, Theme.HomeOffice, Theme.MeetingRoom, Theme.OfficeLounge]),

        // Textiles & Decor
        new(LayoutArchetype.CurtainAisles, DepartmentType.TextilesAndDecor, 0.3f, ArchetypeKind.Hall, [Theme.TextileBoutique]),
        new(LayoutArchetype.BinMarket, DepartmentType.TextilesAndDecor, 0.25f, ArchetypeKind.Hall, [Theme.TextileBoutique])
            { RowStyle = RowStyle.Clusters },
        new(LayoutArchetype.TextileRoomSets, DepartmentType.TextilesAndDecor, 0.25f, ArchetypeKind.Rooms,
            [Theme.Bedroom, Theme.LivingRoom, Theme.GalleryWall, Theme.TextileBoutique]),
        new(LayoutArchetype.GalleryAndBoutique, DepartmentType.TextilesAndDecor, 0.2f, ArchetypeKind.Split, [Theme.TextileBoutique])
            { SecondThemes = [Theme.GalleryWall], RowStyle = RowStyle.Staggered },

        // Outdoor
        new(LayoutArchetype.PatioRooms, DepartmentType.Outdoor, 0.35f, ArchetypeKind.Rooms, [Theme.Patio, Theme.BbqCorner]),
        new(LayoutArchetype.PoolDeckRooms, DepartmentType.Outdoor, 0.2f, ArchetypeKind.Rooms, [Theme.PoolDeck, Theme.Patio]),
        new(LayoutArchetype.GardenStockHall, DepartmentType.Outdoor, 0.2f, ArchetypeKind.Hall, [Theme.GardenStock]) { RowStyle = RowStyle.Staggered },
        new(LayoutArchetype.MixedLawn, DepartmentType.Outdoor, 0.25f, ArchetypeKind.OpenFloor, [Theme.Patio, Theme.PoolDeck, Theme.BbqCorner]),

        // Dining & Hallway
        new(LayoutArchetype.DiningRoomSets, DepartmentType.DiningAndHallway, 0.3f, ArchetypeKind.Rooms, [Theme.DiningRoom, Theme.Entry]),
        new(LayoutArchetype.EntryHalls, DepartmentType.DiningAndHallway, 0.2f, ArchetypeKind.Rooms, [Theme.Entry, Theme.StorageShowroom])
            { Partitions = Archetype.SmallRooms },
        new(LayoutArchetype.CabinetGallery, DepartmentType.DiningAndHallway, 0.15f, ArchetypeKind.Rooms, [Theme.StorageShowroom])
            { Partitions = [(RoomPartition.OneRoom, 0.6f), (RoomPartition.TwoRooms, 0.4f)] },
        new(LayoutArchetype.DiningHall, DepartmentType.DiningAndHallway, 0.2f, ArchetypeKind.Rooms, [Theme.DiningRoom])
            { Partitions = Archetype.LargeRooms, Spacing = (0.85f, 1f) },
        new(LayoutArchetype.HallwayOpenFloor, DepartmentType.DiningAndHallway, 0.15f, ArchetypeKind.OpenFloor,
            [Theme.DiningRoom, Theme.Entry, Theme.StorageShowroom]),
    ];

    private static readonly Dictionary<DepartmentType, Archetype[]> ByDepartment = Validate(All)
        .GroupBy(a => a.Department).ToDictionary(g => g.Key, g => g.ToArray());

    public static IReadOnlyList<Archetype> Of(DepartmentType department) => ByDepartment[department];

    private static Archetype[] Validate(Archetype[] archetypes)
    {
        foreach (var archetype in archetypes)
        {
            var allowed = ThemeCatalog.ThemesOf(archetype.Department).Select(t => t.Theme).ToHashSet();
            if (!archetype.Themes.Concat(archetype.SecondThemes).All(allowed.Contains))
                throw new InvalidOperationException($"{archetype.Id} uses a theme {archetype.Department} may not show.");
        }
        return archetypes;
    }
}
