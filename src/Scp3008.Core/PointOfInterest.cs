using System.Numerics;

namespace Scp3008.Core;

/// <summary>The kinds of common <see cref="PointOfInterest"/>.</summary>
public enum PointOfInterestType
{
    /// <summary>A working restroom: stalls, sinks and mirrors; a small water source. 1–3 per Sector.</summary>
    Restroom,
    /// <summary>A store canteen serving store-style food: a large food source that Villages fight over. 3–4 per Store, in Restaurant/food market Departments.</summary>
    Canteen,
    /// <summary>A showroom dining table laid for a meal, with ready food that Restock lays again. 2–3 per Sector.</summary>
    SetDinnerTable,
    /// <summary>A desk with rare, random Goods that Restock refills. One per Sector.</summary>
    LostAndFoundDesk,
    /// <summary>A corral of shopping carts: a metal source; carts speed up hauling. At most 4 per Store.</summary>
    ShoppingCartGraveyard,
}

/// <summary>
/// A small, distinctive place of the Store found only up close, made of individual Furniture: the pieces
/// whose <see cref="FurniturePiece.PointOfInterestId"/> is its <see cref="Id"/>. It stands on a rectangle
/// inside one Sector, which no Display Furniture, Landmark or Pillar (with its walkway ring) enters.
/// </summary>
/// <param name="Id">Index in <see cref="WorldSnapshot.PointsOfInterest"/>.</param>
/// <param name="Type">Which kind of Point of interest it is.</param>
/// <param name="SectorId">The Sector it stands in, by <see cref="Sector.Id"/>.</param>
/// <param name="Centre">Centre of its rectangle on the floor, in metres.</param>
/// <param name="Size">Extent of its rectangle east-west (X) and north-south (Y), in metres.</param>
public sealed record PointOfInterest(int Id, PointOfInterestType Type, int SectorId, Vector2 Centre, Vector2 Size);
