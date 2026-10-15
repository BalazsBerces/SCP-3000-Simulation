using System.Numerics;

namespace Scp3008.Core;

/// <summary>
/// The kinds of <see cref="PointOfInterest"/>: the common ones, found in every Sector or a few times per Store,
/// then the rare ones, one to three per Store. What a kind gives Villagers (food, water, fabric, metal, a lookout)
/// and the false Beliefs it breeds are its role in the simulation to come; the core only places it for now.
/// </summary>
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
    /// <summary>
    /// A modest bouncy castle with net walls, full of balls. Exactly 3 per Store, one <see cref="BallPitNetwork"/>:
    /// at Closing time the Staff pass from any Ball pit to any other; Villagers cannot.
    /// </summary>
    BallPit,
    /// <summary>
    /// A Stonehenge-like ring of armchairs: a ritual and resting place and a fabric source, and the source of the false
    /// Belief that sitting in the ring at Closing time is safe. 1–2 per Store.
    /// </summary>
    ArmchairHenge,
    /// <summary>A glowing EXIT sign whose arrow leads nowhere; its false Belief sends Expeditions after it. 1–2 per Store.</summary>
    ExitSign,
    /// <summary>A crowd of mannequins that look like the Staff from afar and cause false Staff sightings. 1–2 per Store.</summary>
    MannequinCrowd,
    /// <summary>A free-standing door frame through which the same aisle is seen; the source of the false "portal" Belief. 1–2 per Store.</summary>
    DoorToNowhere,
    /// <summary>A huge display aquarium with live fish that Restock refills: fresh food and water; it glows in the dark. 1–2 per Store.</summary>
    DisplayAquarium,
    /// <summary>
    /// A half-finished Furniture stair spiralling up one Pillar on its walkway ring (the one exception to the ring being
    /// Furniture-free) and ending abruptly: a Lookout that a Village can finish as a Pillar scaffold with fewer Materials.
    /// Exactly 1 per Store.
    /// </summary>
    AbandonedScaffold,
}

/// <summary>
/// A small, distinctive place of the Store found only up close, made of individual Furniture: the pieces
/// whose <see cref="FurniturePiece.PointOfInterestId"/> is its <see cref="Id"/>. It stands on a rectangle
/// inside one Sector, which no Display Furniture, Landmark or Pillar (with its walkway ring) enters; only the
/// abandoned scaffold stands on a walkway ring, of the Pillar it climbs.
/// </summary>
/// <param name="Id">Index in <see cref="WorldSnapshot.PointsOfInterest"/>.</param>
/// <param name="Type">Which kind of Point of interest it is.</param>
/// <param name="SectorId">The Sector it stands in, by <see cref="Sector.Id"/>.</param>
/// <param name="Centre">Centre of its rectangle on the floor, in metres.</param>
/// <param name="Size">Extent of its rectangle east-west (X) and north-south (Y), in metres.</param>
public sealed record PointOfInterest(int Id, PointOfInterestType Type, int SectorId, Vector2 Centre, Vector2 Size)
{
    /// <summary>The Pillar the abandoned scaffold climbs, by <see cref="Pillar.Id"/>; -1 for every other kind.</summary>
    public int PillarId { get; init; } = -1;

    /// <summary>
    /// The high place to look out from, if it has one: the top of the abandoned scaffold (X east, Y south, Z height
    /// above the floor, in metres).
    /// </summary>
    public Vector3? Lookout { get; init; }
}

/// <summary>
/// The three Ball pits of a Store joined as one network: at Closing time the Staff pass from any of them to any other.
/// Villagers never can, and while the Store is open the network is closed.
/// </summary>
/// <param name="BallPitIds">The Ball pits, by <see cref="PointOfInterest.Id"/>.</param>
public sealed record BallPitNetwork(IReadOnlyList<int> BallPitIds)
{
    /// <summary>A Store without Ball pits (the Landmark showcase).</summary>
    public static BallPitNetwork None { get; } = new(Array.Empty<int>());

    /// <summary>The Ball pits the Staff can pass to from one, in this phase of the day: every other one at Closing time, none while open.</summary>
    public IReadOnlyList<int> StaffExitsFrom(int ballPitId, DayPhase phase) =>
        phase == DayPhase.ClosingTime && BallPitIds.Contains(ballPitId)
            ? BallPitIds.Where(id => id != ballPitId).ToArray()
            : Array.Empty<int>();
}
