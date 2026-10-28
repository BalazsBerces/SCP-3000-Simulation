using System.Numerics;
using System.Text.Json.Serialization;

namespace Scp3008.Core;

public enum CastlePart { Tower, Gatehouse, Keep }

/// <summary>
/// A Castle Blueprint planned with a curtain wall: its slots are built in order, and a stacked slot's piece is set on top of
/// the piece below it (<see cref="Castle.Support"/>), at the height it reaches.
/// </summary>
public sealed record CastleStructure(CastlePart Part, int Ring, IReadOnlyList<BlueprintSlot> Slots)
{
    [JsonIgnore] public bool Complete => Slots.All(s => s.FurnitureId is not null);
}

/// <summary>One straight stretch of a Moat's ditch.</summary>
public sealed record MoatRun(Vector2 Start, Vector2 End);

/// <summary>A ditch around a curtain wall, broken by a causeway before the gate; it holds water near standing water.</summary>
public sealed record Moat(int Ring, IReadOnlyList<MoatRun> Runs, bool Water);

public enum HeraldryDivision { PerPale, PerFess, PerBend, Quarterly, Cross, Chevron }

/// <summary>A Village's arms on its banners: a field and a charge tint (<see cref="FurniturePalettes"/>) from its local fabrics.</summary>
public sealed record Heraldry(int Field, int Charge, HeraldryDivision Division);

public static class Castle
{
    /// <summary>Half the opening a curtain wall leaves for its Gatehouse.</summary>
    public const float GateHalfWidth = 1.7f;
    /// <summary>Half the walkable passage between the Gatehouse's flanking pieces.</summary>
    public const float PassageHalfWidth = 0.7f;
    /// <summary>How far a Moat lies outside its curtain wall.</summary>
    public const float MoatOffset = 2.5f;
    /// <summary>Where the Keep stands, from the Village centre: clear of the Mess hall, Storehouse, Workshops and Crops.</summary>
    internal static readonly Vector2 KeepOffset = new(0, 1.0f);

    /// <summary>The slots a stacked slot rests on (empty for one standing on the floor).</summary>
    public static IReadOnlyList<int> Support(CastlePart part, int slot) => (part, slot) switch
    {
        (CastlePart.Tower, 1 or 2) => [slot - 1],
        (CastlePart.Gatehouse, 2) => [0, 1],
        (CastlePart.Keep, 2 or 3) => [slot - 2],
        _ => [],
    };

    /// <summary>Whether a piece suits a structure's slot: shelving and a watch-lamp for a Tower, tall flankers under a double-door
    /// wardrobe for a Gatehouse, showroom storage for a Keep.</summary>
    public static bool Fits(CastlePart part, int slot, FurniturePiece piece)
    {
        var spec = FurnitureCatalog.Spec(piece);
        if (spec.IsFixed || spec.IsFloorCovering) return false;
        if (part == CastlePart.Tower && slot == 2) return spec.IsLamp;
        if (spec.IsLamp || FurnitureResources.Weight(piece) < 10) return false;
        return part switch
        {
            CastlePart.Tower => piece.Type is FurnitureType.ShelfUnit or FurnitureType.Bookcase or FurnitureType.ToyShelf or
                FurnitureType.GroceryShelf or FurnitureType.PalletRack,
            CastlePart.Gatehouse when slot == 2 => piece.Type == FurnitureType.Wardrobe && piece.Variant == 1,
            CastlePart.Gatehouse => spec.Height >= 1.8f && spec.Width is >= 0.4f and <= GateHalfWidth - PassageHalfWidth && spec.Depth <= 0.8f &&
                piece.Type is not (FurnitureType.FloorMirror or FurnitureType.DisplayCabinet or FurnitureType.FishTank or FurnitureType.DoorFrame),
            _ => piece.Type is FurnitureType.Wardrobe or FurnitureType.Bookcase or FurnitureType.Dresser or FurnitureType.Sideboard or
                FurnitureType.ShoeCabinet or FurnitureType.TvBench,
        };
    }
}
