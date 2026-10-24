using System.Numerics;

namespace Scp3008.Core;

/// <summary>The kinds of <see cref="Landmark"/>.</summary>
public enum LandmarkType
{
    /// <summary>A fallen Furniture castle: broken curtain walls, toppled corner towers and the stump of a keep. In every world.</summary>
    RuinedVillage,
    /// <summary>A winding canyon of collapsed warehouse racks: tilted racks for slopes and high lookouts, narrow passages in a maze.</summary>
    RackCanyon,
    /// <summary>A castle of towers and walls made of empty cardboard boxes, with a maze-like interior.</summary>
    BoxFort,
    /// <summary>A huge heap of mattresses, climbable to a lookout at its summit.</summary>
    MattressMountain,
    /// <summary>
    /// An abandoned stretch of Store where the lights never come on and no Lamp is lit: towers of racks and
    /// showroom sets left in the dark. Its ground has <see cref="GroundProperty.Darkness"/>.
    /// </summary>
    DeadZone,
    /// <summary>
    /// A long, high wall hung with thousands of clocks, all showing different times; exactly one, the true clock,
    /// shows the true time left until Closing time (<see cref="Landmark.TrueClockId"/>).
    /// </summary>
    ClockWall,
    /// <summary>
    /// A huge decorative fountain plaza around a tower of stone bowls: the Store's largest water source.
    /// Its ground has <see cref="GroundProperty.Water"/>.
    /// </summary>
    FountainCourt,
    /// <summary>
    /// A trade-fair camping showroom on artificial turf: three huge dinosaurs built of Furniture (<see cref="Landmark.Dinosaurs"/>), tents,
    /// fake trees, a fake campfire, a lake, a tree house and a camper van. Lookouts: the top of the tree house and the
    /// Brachiosaurus's back. Its ground is visual only (<see cref="GroundProperty.None"/>).
    /// </summary>
    CampingGround,
}

/// <summary>
/// What a stretch of the Store's ground is like, whatever Furniture stands on it: a lasting property
/// of the ground under a Landmark, kept when its Furniture is taken.
/// </summary>
[Flags]
public enum GroundProperty
{
    /// <summary>Ordinary Store floor.</summary>
    None = 0,
    /// <summary>The Store lights never come on here and no Lamp is lit, even while the Store is open (the dead zone).</summary>
    Darkness = 1,
    /// <summary>There is water here (the fountain court).</summary>
    Water = 2,
}

/// <summary>
/// A huge, far-visible, unique place of the Store, made entirely of individual Furniture: the pieces
/// whose <see cref="FurniturePiece.LandmarkId"/> is its <see cref="Id"/>. It stands on a rectangle of
/// whole department cells inside one Sector, which no Display Furniture enters.
/// </summary>
/// <param name="Id">Index in <see cref="WorldSnapshot.Landmarks"/>.</param>
/// <param name="Type">Which Landmark it is.</param>
/// <param name="SectorId">The Sector it stands in, by <see cref="Sector.Id"/>.</param>
/// <param name="Centre">Centre of its rectangle on the floor, in metres.</param>
/// <param name="Size">Extent of its rectangle east-west (X) and north-south (Y), in metres.</param>
/// <param name="Height">Height of the top of its highest piece above the floor, in metres.</param>
/// <param name="Lookouts">
/// High places on its Furniture to look out from (X east, Y south, Z height of the standing surface);
/// empty for Landmarks that are not climbed.
/// </param>
public sealed record Landmark(int Id, LandmarkType Type, int SectorId, Vector2 Centre, Vector2 Size, float Height, IReadOnlyList<Vector3> Lookouts)
{
    /// <summary>
    /// Every Landmark rises at least this high, in metres: well above the shelving and the Fog banks,
    /// so it can be seen from afar.
    /// </summary>
    public const float MinHeight = 20f;

    /// <summary>The property of the ground under this Landmark's rectangle (see <see cref="WorldSnapshot.GroundAt"/>).</summary>
    public GroundProperty Ground => Type switch
    {
        LandmarkType.DeadZone => GroundProperty.Darkness,
        LandmarkType.FountainCourt => GroundProperty.Water,
        _ => GroundProperty.None,
    };

    /// <summary>
    /// The clock wall's one true clock, by <see cref="FurniturePiece.Id"/>: the only clock that shows the true time
    /// left until Closing time. Known to the core and hidden from Villagers. -1 for other Landmarks.
    /// </summary>
    public int TrueClockId { get; init; } = -1;

    /// <summary>
    /// How far each of the clock wall's clocks is off, by <see cref="FurniturePiece.Id"/>: each shows the time left until
    /// Closing time plus its own offset, on a 24-hour dial that counts down (see <see cref="WorldSnapshot.ClockReading"/>). The offsets
    /// differ, so no two clocks show the same time; only the true clock's is zero. Empty for other Landmarks.
    /// </summary>
    public IReadOnlyDictionary<int, TimeSpan> ClockOffsets { get; init; } = new Dictionary<int, TimeSpan>();

    /// <summary>The Camping ground's dinosaurs, one of each kind with its own main colour; empty for other Landmarks.</summary>
    public IReadOnlyList<Dinosaur> Dinosaurs { get; init; } = [];

    /// <summary>
    /// The Camping ground's winding visitor paths over its turf, as polylines of Store positions (unwrapped, inside its rectangle)
    /// with their <see cref="CampingGround.PathWidth"/>; visual only. Empty for other Landmarks.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Paths { get; init; } = [];

    /// <summary>The middle of the Camping ground's lake as a Store position (unwrapped, inside its rectangle); null for other Landmarks.</summary>
    public Vector2? Lake { get; init; }

    /// <summary>The decorative axe stuck in a tree, by <see cref="FurniturePiece.Id"/>; -1 for other Landmarks. A separate piece: the tree stands without it.</summary>
    public int AxeId { get; init; } = -1;

    /// <summary>The tree the decorative axe is stuck in, by <see cref="FurniturePiece.Id"/>; -1 for other Landmarks.</summary>
    public int AxeTreeId { get; init; } = -1;

    /// <summary>Whether a Store position (it may be unwrapped) lies on this Landmark's rectangle, edges included.</summary>
    public bool Covers(StoreSpace space, Vector2 position)
    {
        var delta = space.Delta(Centre, position);
        return MathF.Abs(delta.X) <= Size.X / 2f && MathF.Abs(delta.Y) <= Size.Y / 2f;
    }
}
