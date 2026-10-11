using Godot;
using StoreVector = System.Numerics.Vector2;

namespace Scp3008.Game;

/// <summary>
/// Converts between core Store positions (metres, X east, Y south) and Godot world space,
/// where the Store floor lies at height 0 with Store Y along Godot Z.
/// </summary>
public static class StoreCoordinates
{
    public static StoreVector ToStore(this Vector3 world) => new(world.X, world.Z);

    public static Vector3 ToWorld(this StoreVector store, float height = 0f) => new(store.X, height, store.Y);
}
