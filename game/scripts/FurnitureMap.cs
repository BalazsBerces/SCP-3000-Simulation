using Godot;

namespace Scp3008.Game;

/// <summary>
/// The farthest level of detail of the <see cref="StoreOverview"/>: a picture of all coarse Furniture
/// seen straight from above, drawn by the floor shader in place of the Furniture meshes once the
/// camera is higher than <see cref="FurnitureDetail.MapHeight"/>. There the pieces are a few pixels
/// each, and thousands of chunk draws would cost far more than one floor texture.
/// The picture is made when the overview is first shown for a world, not at start: an orthographic camera in a
/// <see cref="SubViewport"/> renders the overview's coarse Furniture unshaded (albedo only, so the
/// floor's lights light it as they light the floor) in <see cref="Tiles"/> × <see cref="Tiles"/>
/// tiles, one per frame, while the coarse meshes stay on screen. Colours are premultiplied by
/// coverage (the background is transparent black) and stored linear, so the mipmaps blend Furniture
/// edges and the floor correctly. View only.
/// </summary>
public partial class FurnitureMap : Node
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;
    [Export] public StoreFloor Floor { get; set; } = null!;

    /// <summary>Tiles per side of the Store; the picture is <see cref="Tiles"/> × <see cref="TilePixels"/> pixels square.</summary>
    private const int Tiles = 4;
    private const int TilePixels = 1024;

    private bool _baking;
    // Counts worlds, so a bake that outlives its world is dropped.
    private int _generation;
    private object? _builtFurniture;

    public override void _Ready()
    {
        Host.WorldCreated += OnWorldCreated;
        FurnitureDetail.Changed += ShowMap;
    }

    public override void _ExitTree()
    {
        Host.WorldCreated -= OnWorldCreated;
        FurnitureDetail.Changed -= ShowMap;
    }

    private void OnWorldCreated(Scp3008.Core.WorldSnapshot snapshot)
    {
        _builtFurniture = snapshot.Furniture;
        _generation++;
        FurnitureDetail.MapReady = false;
    }

    private void ShowMap(bool coarse) =>
        Floor.FloorMaterial.SetShaderParameter("show_furniture_map", FurnitureDetail.Map);

    public override void _Process(double delta)
    {
        if (!ReferenceEquals(_builtFurniture, Host.Snapshot.Furniture))
            OnWorldCreated(Host.Snapshot);
        if (!_baking && !FurnitureDetail.MapReady && StoreOverview.Enabled)
            Bake();
    }

    private async void Bake()
    {
        _baking = true;
        var generation = _generation;
        var started = Time.GetTicksMsec();
        var size = Host.Snapshot.Space.Size;
        var tile = size / Tiles;
        var viewport = new SubViewport
        {
            Size = new Vector2I(TilePixels, TilePixels),
            TransparentBg = true,
            DebugDraw = Viewport.DebugDrawEnum.Unshaded,
            Msaa3D = Viewport.Msaa.Msaa4X,
            PositionalShadowAtlasSize = 0,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        // Straight down, with the top of the picture towards -Z: picture x and y run along world x and z.
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = tile,
            Near = 1f,
            Far = 200f,
            CullMask = FurnitureDetail.CoarseLayer,
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            // Its own environment: no fog, glow or tonemapping on the albedo.
            Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor },
        };
        viewport.AddChild(camera);
        AddChild(viewport);
        camera.MakeCurrent();
        try
        {
            var map = Image.CreateEmpty(Tiles * TilePixels, Tiles * TilePixels, false, Image.Format.Rgba8);
            // The overview places every chunk once in the frame after it is switched on.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var y = 0; y < Tiles; y++)
                for (var x = 0; x < Tiles; x++)
                {
                    if (generation != _generation || !StoreOverview.Enabled)
                        return;
                    camera.Position = new Vector3((x + 0.5f) * tile, 100f, (y + 0.5f) * tile);
                    viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var picture = viewport.GetTexture().GetImage();
                    picture.Convert(Image.Format.Rgba8);
                    map.BlitRect(picture, new Rect2I(0, 0, TilePixels, TilePixels), new Vector2I(x * TilePixels, y * TilePixels));
                }
            if (generation != _generation)
                return;
            // Premultiplied colours must be blended in linear space: decoding sRGB after the
            // mipmaps blend them would darken every partly covered texel.
            map.SrgbToLinear();
            map.GenerateMipmaps();
            Floor.FloorMaterial.SetShaderParameter("furniture_map", ImageTexture.CreateFromImage(map));
            FurnitureDetail.MapReady = true;
            GD.Print($"Furniture map: {map.GetWidth()} px for {size:0} m, made in {Time.GetTicksMsec() - started} ms.");
        }
        catch (Exception e)
        {
            GD.PushError($"Furniture map failed: {e}");
        }
        finally
        {
            viewport.QueueFree();
            _baking = false;
        }
    }
}
