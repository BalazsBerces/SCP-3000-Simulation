using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Compares every Furniture type and variant's coarse mesh (level of detail) with its full mesh by
/// colour: both are drawn as instances (so tinted parts take an instance colour, as in the Store)
/// under the same light in a separate world, and for each part the average colour of the pixels it
/// covers must match: the same brightness within <see cref="MaxBrightnessRatio"/> and the same hue
/// within <see cref="MaxChromaDistance"/>. Tinted types are checked in three tints. Also saves
/// contact sheets with every type and variant full (left) and coarse (right) side by side. Run by
/// DevCapture's <c>--coarse-colour-check</c>; needs a GPU (not headless).
/// </summary>
public partial class CoarseColourCheck : Node
{
    private const int Size = 320;
    private const int Tile = 160;
    private const int PairsPerRow = 4;
    private const int RowsPerSheet = 6;
    // A part must cover this many pixels (after trimming its edge) in both renders to be compared.
    private const int MinPixels = 40;
    // Texture detail (normal maps, grain) and coarse shapes (posts for round legs) shift a part's
    // average a little; a wrong colour or texture average shifts it far more.
    private const float MaxBrightnessRatio = 1.25f;
    private const float MaxChromaDistance = 0.04f;
    // Two averages this close in linear luminance have the same brightness, whatever their ratio.
    private const float MinLuminanceDifference = 0.01f;
    // Tints checked for tinted types (cream, navy, terracotta); the sheet shows the last.
    private static readonly int[] Tints = [0, 4, 9];

    public sealed record PartResult(string Part, int FullPixels, int CoarsePixels, Color Full, Color Coarse)
    {
        public bool Compared => FullPixels >= MinPixels && CoarsePixels >= MinPixels;
        public float BrightnessRatio => (Luminance(Coarse) + 1e-4f) / (Luminance(Full) + 1e-4f);
        public float ChromaDistance => Chroma(Coarse).DistanceTo(Chroma(Full));
        // Both near black: chrome reflecting the dark backdrop, dark steel. Ratio and hue are noise there.
        private bool Dark => Mathf.Max(Luminance(Coarse), Luminance(Full)) < 2f * MinLuminanceDifference;
        private bool SameBrightness => Mathf.Abs(Luminance(Coarse) - Luminance(Full)) < MinLuminanceDifference
                                       || (BrightnessRatio <= MaxBrightnessRatio && BrightnessRatio >= 1f / MaxBrightnessRatio);
        public bool Passed => !Compared || Dark || (SameBrightness && ChromaDistance <= MaxChromaDistance);

        public override string ToString() => Compared
            ? FormattableString.Invariant($"{Part} full {Hex(Full)} coarse {Hex(Coarse)} brightness x{BrightnessRatio:0.00} hue {ChromaDistance:0.000}")
            : $"{Part} not compared ({FullPixels}/{CoarsePixels} px)";
    }

    public sealed record Result(FurnitureType Type, int Variant, int? Tint, IReadOnlyList<PartResult> Parts)
    {
        // A piece with no part seen in both renders would pass unchecked.
        public bool Passed => Parts.Any(p => p.Compared) && Parts.All(p => p.Passed);

        public override string ToString() =>
            $"{(Passed ? "ok  " : "FAIL")} {Type}-{Variant}{(Tint is { } t ? $" tint {t}" : "")}: {string.Join("; ", Parts)}";
    }

    private SubViewport _viewport = null!;
    private MultiMeshInstance3D _piece = null!;
    private Camera3D _camera = null!;
    private Label3D _label = null!;
    private ShaderMaterial _coarseMask = null!;
    private Shader _maskShader = null!;

    /// <summary>Runs the check, writes report.txt and the sheets into <paramref name="directory"/>; true if every part matches.</summary>
    public async Task<bool> Run(string directory)
    {
        DirAccess.MakeDirRecursiveAbsolute(directory);
        BuildStage();
        var results = new List<Result>();
        var tiles = new List<(Image Full, Image Coarse)>();
        foreach (var type in Enum.GetValues<FurnitureType>())
            for (var variant = 0; variant < FurnitureCatalog.VariantCount(type); variant++)
            {
                var full = FurnitureMeshes.For(type, variant);
                var coarse = FurnitureMeshes.CoarseFor(type, variant);
                Frame(full);
                var parts = Enumerable.Range(0, full.GetSurfaceCount()).Select(s => full.SurfaceGetName(s)).Distinct().ToList();
                var fullMasks = await Masks(type, variant, full, parts, coarse: false);
                var coarseMasks = await Masks(type, variant, coarse, parts, coarse: true);
                int?[] tints = FurnitureMeshes.IsTinted(type) ? [.. Tints.Select(t => (int?)t)] : [null];
                foreach (var tint in tints)
                {
                    var colour = tint is { } t ? FurnitureMeshes.TintFor(type, t) : Colors.White;
                    var fullImage = await Render(full, colour);
                    var coarseImage = await Render(coarse, colour);
                    results.Add(new Result(type, variant, tint, parts.Select((part, i) => new PartResult(part,
                        fullMasks[i].Count, coarseMasks[i].Count,
                        Average(fullImage, fullMasks[i]), Average(coarseImage, coarseMasks[i]))).ToList()));
                }
                _label.Text = $"{type}-{variant}";
                _label.Visible = true;
                var lastTint = tints[^1] is { } last ? FurnitureMeshes.TintFor(type, last) : Colors.White;
                tiles.Add((await Render(full, lastTint), await Render(coarse, lastTint)));
                _label.Visible = false;
            }
        SaveSheets(directory, tiles);
        var failed = results.Count(r => !r.Passed);
        var lines = results.Select(r => r.ToString())
            .Append($"{results.Count} Furniture types, variants and tints, {failed} failed.").ToList();
        System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
        GD.Print(lines[^1]);
        foreach (var line in lines.Where(l => l.StartsWith("FAIL")))
            GD.Print(line);
        _viewport.QueueFree();
        return failed == 0;
    }

    /// <summary>A world of its own: grey backdrop, soft ambient light, one sun from the front left, an orthographic camera.</summary>
    private void BuildStage()
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(Size, Size),
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Disabled,
        };
        AddChild(_viewport);
        _viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.3f, 0.3f, 0.32f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.45f,
                TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1f };
        _viewport.AddChild(sun);
        sun.Rotation = new Vector3(Mathf.DegToRad(-55f), Mathf.DegToRad(-35f), 0f);
        _piece = new MultiMeshInstance3D();
        _viewport.AddChild(_piece);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Current = true };
        _viewport.AddChild(_camera);
        _label = new Label3D { FontSize = 28, OutlineSize = 6, NoDepthTest = true, Shaded = false, Visible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _camera.AddChild(_label);
        _maskShader = GD.Load<Shader>("res://shaders/part_mask.gdshader");
        _coarseMask = new ShaderMaterial { Shader = _maskShader };
        _coarseMask.SetShaderParameter("coarse_parts", true);
    }

    /// <summary>Points the camera at a piece from the front right and above, with room around it.</summary>
    private void Frame(Mesh mesh)
    {
        var bounds = mesh.GetAabb();
        var radius = bounds.Size.Length() / 2f;
        var centre = bounds.GetCenter();
        var direction = new Vector3(Mathf.Sin(Mathf.DegToRad(30f)), 0f, Mathf.Cos(Mathf.DegToRad(30f))) * Mathf.Cos(Mathf.DegToRad(30f))
                        + Vector3.Up * Mathf.Sin(Mathf.DegToRad(30f));
        _camera.Size = radius * 2.3f;
        _camera.Near = 0.05f;
        _camera.Far = radius * 2f + 20f;
        _camera.LookAtFromPosition(centre + direction * (radius + 10f), centre);
        _label.PixelSize = _camera.Size / Size;
        _label.Position = new Vector3(-_camera.Size * 0.47f, _camera.Size * 0.47f, -1f);
    }

    private async Task<Image> Render(Mesh mesh, Color colour)
    {
        Show(mesh, colour);
        _piece.MaterialOverride = null;
        return await Capture();
    }

    /// <summary>One instance of the mesh. A new MultiMesh each time: swapping the mesh of one keeps its old bounds.</summary>
    private void Show(Mesh mesh, Color colour)
    {
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = 1,
        };
        multiMesh.SetInstanceTransform(0, Transform3D.Identity);
        multiMesh.SetInstanceColor(0, colour);
        _piece.Multimesh = multiMesh;
    }

    /// <summary>For each part, the pixels it covers in the given mesh, minus its one-pixel edge.</summary>
    private async Task<List<List<int>>> Masks(FurnitureType type, int variant, ArrayMesh mesh, List<string> parts, bool coarse)
    {
        var result = new List<List<int>>();
        for (var first = 0; first < parts.Count; first += 3)
        {
            var group = new Vector3I(-1, -1, -1);
            for (var c = 0; c < 3 && first + c < parts.Count; c++)
                group[c] = FurnitureMeshes.PartIndex(type, variant, parts[first + c]);
            ArrayMesh drawn;
            if (coarse)
            {
                drawn = mesh;
                _coarseMask.SetShaderParameter("parts", group);
                _piece.MaterialOverride = _coarseMask;
            }
            else
            {
                drawn = (ArrayMesh)mesh.Duplicate();
                for (var s = 0; s < drawn.GetSurfaceCount(); s++)
                {
                    var material = new ShaderMaterial { Shader = _maskShader };
                    material.SetShaderParameter("parts", group);
                    material.SetShaderParameter("surface_part", FurnitureMeshes.PartIndex(type, variant, mesh.SurfaceGetName(s)));
                    drawn.SurfaceSetMaterial(s, material);
                }
                _piece.MaterialOverride = null;
            }
            Show(drawn, Colors.White);
            var image = await Capture();
            for (var c = 0; c < 3 && first + c < parts.Count; c++)
            {
                bool On(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && image.GetPixel(x, y)[c] > 0.5f;
                var pixels = new List<int>();
                for (var y = 0; y < Size; y++)
                    for (var x = 0; x < Size; x++)
                        if (On(x, y) && On(x - 1, y) && On(x + 1, y) && On(x, y - 1) && On(x, y + 1))
                            pixels.Add(y * Size + x);
                result.Add(pixels);
            }
        }
        _piece.MaterialOverride = null;
        return result;
    }

    private async Task<Image> Capture()
    {
        for (var frame = 0; frame < 2; frame++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = _viewport.GetTexture().GetImage();
        image.Convert(Image.Format.Rgb8);
        return image;
    }

    /// <summary>The average linear colour of the given pixels; black if there are none.</summary>
    private static Color Average(Image image, List<int> pixels)
    {
        if (pixels.Count == 0)
            return Colors.Black;
        var (r, g, b) = (0.0, 0.0, 0.0);
        foreach (var index in pixels)
        {
            var c = image.GetPixel(index % Size, index / Size).SrgbToLinear();
            (r, g, b) = (r + c.R, g + c.G, b + c.B);
        }
        return new Color((float)(r / pixels.Count), (float)(g / pixels.Count), (float)(b / pixels.Count));
    }

    private static float Luminance(Color c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

    private static Vector3 Chroma(Color c)
    {
        var sum = c.R + c.G + c.B + 1e-4f;
        return new Vector3(c.R, c.G, c.B) / sum;
    }

    private static string Hex(Color linear) => linear.LinearToSrgb().ToHtml(false);

    /// <summary>Sheets of full (left) and coarse (right) tiles, four pairs to a row.</summary>
    private static void SaveSheets(string directory, List<(Image Full, Image Coarse)> tiles)
    {
        var perSheet = PairsPerRow * RowsPerSheet;
        for (var sheet = 0; sheet * perSheet < tiles.Count; sheet++)
        {
            var count = Math.Min(perSheet, tiles.Count - sheet * perSheet);
            var rows = (count + PairsPerRow - 1) / PairsPerRow;
            var image = Image.CreateEmpty(PairsPerRow * 2 * Tile, rows * Tile, false, Image.Format.Rgb8);
            for (var i = 0; i < count; i++)
            {
                var (full, coarse) = tiles[sheet * perSheet + i];
                var (x, y) = (i % PairsPerRow * 2 * Tile, i / PairsPerRow * Tile);
                foreach (var (tile, offset) in new[] { (full, 0), (coarse, Tile) })
                {
                    var small = (Image)tile.Duplicate();
                    small.Resize(Tile, Tile, Image.Interpolation.Lanczos);
                    image.BlitRect(small, new Rect2I(0, 0, Tile, Tile), new Vector2I(x + offset, y));
                }
            }
            image.SaveJpg(System.IO.Path.Combine(directory, $"sheet-{sheet + 1}.jpg"), 0.9f);
        }
    }
}
