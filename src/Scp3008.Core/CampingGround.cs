using System.Numerics;

namespace Scp3008.Core;

/// <summary>The Camping ground's three dinosaurs.</summary>
public enum DinosaurKind
{
    /// <summary>About 12 m long and 6 m tall; orange-red.</summary>
    TyrannosaurusRex,
    /// <summary>Its neck reaches about 14 m high; green.</summary>
    Brachiosaurus,
    /// <summary>About 9 m long; teal.</summary>
    Triceratops,
}

/// <summary>
/// One of the Camping ground's dinosaurs: built only of ordinary Furniture, and taken apart like any Furniture.
/// </summary>
/// <param name="Kind">Which dinosaur it is.</param>
/// <param name="MainTint">Its main colour, a <see cref="FurniturePalettes.DinosaurTints"/> tint no other dinosaur shares.</param>
/// <param name="PieceIds">Its Furniture, by <see cref="FurniturePiece.Id"/>: body in the main colour, small accents (eyes, teeth, spikes) in their own.</param>
public sealed record Dinosaur(DinosaurKind Kind, int MainTint, IReadOnlyList<int> PieceIds);

/// <summary>Camping goods (Goods, not Furniture) a stand of the Camping ground holds.</summary>
public enum CampGoodsKind
{
    /// <summary>Marshmallows on sticks: food, the Camping ground's own.</summary>
    Marshmallows,
    /// <summary>Tinned food.</summary>
    TinnedFood,
    /// <summary>Backpacks (Gear).</summary>
    Backpack,
}

/// <summary>A kind of camping goods a stand holds (Goods, not Furniture), by the stand's Furniture id.</summary>
public readonly record struct CampGoods(int StandId, CampGoodsKind Kind);

/// <summary>Constants of the Camping ground.</summary>
public static class CampingGround
{
    /// <summary>Width of the visitor paths over the turf, in metres.</summary>
    public const float PathWidth = 2.2f;

    /// <summary>Height of the tree house's deck above the floor, in metres; the fixed shell has its own stair up to it.</summary>
    public const float TreeHouseDeckHeight = 4.5f;

    /// <summary>The middle of the tree house's roofed cabin on its deck, in the shell's own frame (along its width, along its depth), in metres.</summary>
    public static readonly Vector2 TreeHouseCabin = new(1.58f, -2.33f);

    /// <summary>The middle of the tree house's open deck, in the shell's own frame, in metres.</summary>
    public static readonly Vector2 TreeHouseOpenDeck = new(1.58f, 2.47f);

    /// <summary>Where a broad tree's (variant 0) trunk stands, in its own frame (along its width, along its depth), in metres.</summary>
    public static readonly Vector2 TreeTrunk = new(-0.47f, -0.12f);

    /// <summary>Where a broad tree's trunk is at chest height (<see cref="AxeHeight"/>), in its own frame: the trunk leans.</summary>
    public static readonly Vector2 TreeTrunkAtChest = new(-0.27f, 0.33f);

    /// <summary>Radius of a broad tree's trunk at chest height, in metres.</summary>
    public const float TreeTrunkRadius = 0.4f;

    /// <summary>Height of the decorative axe above the floor, in metres: chest height.</summary>
    public const float AxeHeight = 1.3f;
}
