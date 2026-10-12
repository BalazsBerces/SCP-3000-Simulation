using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// One Display: a room or a free-standing set of Furniture furnished from exactly one
/// <see cref="Core.Theme"/>. Every piece of Furniture belongs to one Display.
/// </summary>
/// <param name="Id">Index of the Display in <see cref="WorldSnapshot.Displays"/>.</param>
/// <param name="Theme">What the Display shows; only types listed in the theme stand in it.</param>
/// <param name="DepartmentId">The Department it stands in.</param>
/// <param name="CellX">Department cell column of the Display.</param>
/// <param name="CellY">Department cell row of the Display.</param>
/// <param name="Centre">Centre of its floor area, wrapped onto the floor.</param>
/// <param name="Size">Extent of its floor area along X (east) and Y (south), in metres.</param>
/// <param name="Palette">Its colour scheme, an index into <see cref="FurniturePalettes.All"/>.</param>
public sealed record Display(int Id, Theme Theme, int DepartmentId, int CellX, int CellY, Vector2 Centre, Vector2 Size, int Palette);

/// <summary>How a room-set department cell is divided into rooms.</summary>
public enum RoomPartition
{
    /// <summary>Not divided into rooms: one or two Displays laid out in rows or grids.</summary>
    None,
    /// <summary>One large room filling the cell.</summary>
    OneRoom,
    /// <summary>Two long rooms side by side.</summary>
    TwoRooms,
    /// <summary>One long room and two small ones.</summary>
    ThreeRooms,
    /// <summary>Two by two small rooms.</summary>
    FourRooms,
    /// <summary>No rooms: free-standing sets placed apart on an open floor.</summary>
    OpenFloor,
}

/// <summary>
/// The layout archetypes: the different ways a department cell can be laid out. Each belongs to
/// one Department type, which has at least four.
/// </summary>
public enum LayoutArchetype
{
    // Showrooms
    ShowroomRoomSets,
    ShowroomBedGallery,
    ShowroomLivingSuites,
    ShowroomOpenFloor,
    // Bathrooms & kitchens
    KitchenRoomSets,
    BathroomRoomSets,
    KitchenAndBathRoomSets,
    ApplianceHall,
    // Restaurant/food market
    CafeHall,
    CanteenHall,
    MarketAisles,
    MarketAndCafe,
    // Plant section
    GreenhouseTables,
    PlantHall,
    GreenhouseAndPatio,
    GreenhouseRooms,
    // Kids' area
    KidsRoomSets,
    KidsOpenFloor,
    ToyShelvingRows,
    ToyShelvesAndRooms,
    // Lighting & electrical
    LampTableGrid,
    LampStaggeredTables,
    LampRooms,
    LampShelvingRows,
    // Self-serve warehouse
    RackRows,
    RackRowsCrossAisle,
    RacksAndBulk,
    BulkStackHall,
    // Workspaces
    OpenPlanHall,
    CubicleRows,
    OfficeRoomSets,
    OfficeOpenFloor,
    // Textiles & Decor
    CurtainAisles,
    BinMarket,
    TextileRoomSets,
    GalleryAndBoutique,
    // Outdoor
    PatioRooms,
    PoolDeckRooms,
    GardenStockHall,
    MixedLawn,
    // Dining & Hallway
    DiningRoomSets,
    EntryHalls,
    CabinetGallery,
    DiningHall,
    HallwayOpenFloor,
}

/// <summary>How one department cell is laid out.</summary>
/// <param name="X">Department cell column.</param>
/// <param name="Y">Department cell row.</param>
/// <param name="Archetype">The layout archetype it was laid out by.</param>
/// <param name="Partition">How it is divided into rooms; <see cref="RoomPartition.None"/> for row and grid layouts.</param>
public readonly record struct CellLayout(int X, int Y, LayoutArchetype Archetype, RoomPartition Partition);
