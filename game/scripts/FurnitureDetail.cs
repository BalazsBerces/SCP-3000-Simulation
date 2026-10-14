namespace Scp3008.Game;

/// <summary>
/// The level of detail of all Furniture, one for the whole view: full meshes while the Observer
/// camera's zoom is at most <see cref="SwitchZoom"/>, coarse meshes beyond it, so at any zoom every
/// visible piece is drawn the same way, at the centre, the edges and the top of the view. Zooming
/// back in switches to full only below <see cref="SwitchZoom"/> minus <see cref="Hysteresis"/>, so
/// zooming near the threshold does not flicker. The <see cref="StoreOverview"/> is coarse, and with
/// the camera higher than <see cref="MapHeight"/> (once the <see cref="FurnitureMap"/> is ready) it
/// draws no Furniture meshes at all: the floor shows the map instead, a picture of the coarse
/// Furniture from above. Like the zoom switch, this switch is one for the whole view, so the corners
/// change together with the centre.
/// Full and coarse meshes sit on their own render layers (<see cref="FullLayer"/>,
/// <see cref="CoarseLayer"/>); a switch only changes which of the two the camera and the Lamp
/// lights see, so it costs nothing however many pieces are placed. View only.
/// </summary>
public static class FurnitureDetail
{
    /// <summary>Render layer 2: the full Furniture meshes.</summary>
    public const uint FullLayer = 1u << 1;

    /// <summary>Render layer 3: the coarse Furniture meshes.</summary>
    public const uint CoarseLayer = 1u << 2;

    /// <summary>
    /// The farthest zoom (camera distance from the focus, in metres) drawn with full meshes: the
    /// farthest at which the 120 fps benchmark passes with every visible piece full (ticket 06f).
    /// </summary>
    public static float SwitchZoom { get; set; } = 160f;

    /// <summary>How far below <see cref="SwitchZoom"/> zooming in must go to switch back to full, in metres.</summary>
    public const float Hysteresis = 8f;

    /// <summary>
    /// The overview camera height (metres above the floor) above which the <see cref="FurnitureMap"/>
    /// replaces the Furniture meshes: the highest at which the whole view of coarse meshes still
    /// renders cheaply (the overview benchmark).
    /// </summary>
    public static float MapHeight { get; set; } = 250f;

    /// <summary>How far below <see cref="MapHeight"/> the camera must go to switch back to meshes, in metres.</summary>
    public const float MapHysteresis = 15f;

    public static bool Coarse { get; private set; }

    /// <summary>Whether the floor's <see cref="FurnitureMap"/> stands in for the Furniture meshes.</summary>
    public static bool Map { get; private set; }

    /// <summary>Whether the <see cref="FurnitureMap"/> of the current world is ready; until then no switch to it.</summary>
    public static bool MapReady { get; set; }

    /// <summary>Raised with the level whenever it, or the layers to draw, change (see <see cref="Mask"/>).</summary>
    public static event Action<bool>? Changed;

    // Frames left that draw both levels.
    private static int _bothFrames;

    /// <summary>Picks the level for the current zoom and camera height; raises <see cref="Changed"/> if it switched.</summary>
    public static void Update(float zoom, float cameraHeight)
    {
        var coarse = StoreOverview.Enabled || zoom > (Coarse ? SwitchZoom - Hysteresis : SwitchZoom);
        var map = StoreOverview.Enabled && MapReady && cameraHeight > (Map ? MapHeight - MapHysteresis : MapHeight);
        var bothEnded = _bothFrames > 0 && --_bothFrames == 0;
        if (coarse == Coarse && map == Map && !bothEnded)
            return;
        Coarse = coarse;
        Map = map;
        Changed?.Invoke(coarse);
    }

    /// <summary>
    /// Draws both levels for the next few frames: the first frame that draws a level compiles its
    /// shaders for every light and shadow, which would otherwise stall the first switch.
    /// </summary>
    public static void DrawBoth(int frames)
    {
        _bothFrames = frames;
        Changed?.Invoke(Coarse);
    }

    /// <summary>
    /// A camera or light cull mask that sees the current level's layer and not the other one, or
    /// neither with the <see cref="Map"/>.
    /// </summary>
    public static uint Mask(uint mask) =>
        _bothFrames > 0 ? mask | FullLayer | CoarseLayer
        : Map ? mask & ~(FullLayer | CoarseLayer)
        : Coarse ? (mask & ~FullLayer) | CoarseLayer : (mask & ~CoarseLayer) | FullLayer;
}
