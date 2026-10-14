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
}
