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
    /// A ball pool like a store's kids' play area: padded walls, net railing, a slide and hundreds of balls (Furniture). Exactly 3 per Store, one <see cref="BallPitNetwork"/>:
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
    /// <summary>
    /// A pet-shop row of tanks of different sizes on racks, with one or two large ones at its end, each tank with its own kind of live
    /// fish (<see cref="TankFish"/>) that Restock refills: fresh food and water; it glows bluish in the dark. 1–2 per Store.
    /// </summary>
    AquariumAisle,
    /// <summary>
    /// A large self-service dining hall like a Scandinavian furniture store's restaurant (24 × 20 m): a tray line past hot food
    /// and dessert counters to a till along one wall, and a big dining zone left mid-meal. For now only in the Point of interest
    /// showcase; it replaces the Canteen in the Store generator in ticket 08m.
    /// </summary>
    Cafeteria,
}

/// <summary>A kind of live fish kept in the tanks of an Aquarium aisle.</summary>
public enum FishKind
{
    Goldfish,
    NeonTetra,
    Guppy,
    Angelfish,
    Discus,
    Betta,
    Zebrafish,
    Koi,
}

/// <summary>
/// The live fish in one tank of an Aquarium aisle: Goods, not Furniture (the tank is the Furniture), and food for whoever takes
/// them. Restock refills them.
/// </summary>
/// <param name="TankId">The tank, a <see cref="FurnitureType.FishTank"/>, by <see cref="FurniturePiece.Id"/>.</param>
/// <param name="Kind">The one kind of fish the tank holds.</param>
public readonly record struct TankFish(int TankId, FishKind Kind);

/// <summary>A meal a Cafeteria serves (Goods, not Furniture); eating it comes with the Villager needs.</summary>
public enum MealKind
{
    /// <summary>Meatballs with mash and lingonberry, at a hot food counter.</summary>
    Meatballs,
    /// <summary>Almond cake, at the chilled dessert counter.</summary>
    AlmondCake,
}

/// <summary>A meal a Cafeteria's serving counter holds (Goods, not Furniture), by the counter's Furniture id.</summary>
public readonly record struct CounterMeal(int CounterId, MealKind Kind);

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
