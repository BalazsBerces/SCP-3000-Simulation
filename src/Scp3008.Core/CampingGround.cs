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
}
