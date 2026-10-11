using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>Static, far-visible Pillars and their Ghost copies. Reads the snapshot only.</summary>
public partial class PillarRenderer : Node3D
{
    [Export] public SimulationHost Host { get; set; } = null!;
    [Export] public CameraRig Rig { get; set; } = null!;

    // Landmarks extend beyond Furniture culling. Keep placement conservative at maximum zoom.
    [Export] public float FarDistance { get; set; } = 1200f;
    private WrappedInstances _instances = null!;

    public override void _Ready()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/pillar.gdshader") };
        material.SetShaderParameter("numbers", NumberAtlas());
        _instances = new WrappedInstances
        {
            Rig = Rig,
            FarDistance = FarDistance,
            ViewReach = FarDistance + Rig.MaxDistance + Host.Snapshot.Space.ChunkSize,
            Mesh = new BoxMesh { Size = Vector3.One, Material = material },
        };
        AddChild(_instances);
        Host.WorldCreated += Rebuild;
        Rebuild(Host.Snapshot);
    }

    public override void _ExitTree() => Host.WorldCreated -= Rebuild;

    private void Rebuild(WorldSnapshot snapshot)
    {
        _instances.SetInstances(snapshot.Space, snapshot.Pillars.Select(p =>
            (new Transform3D(Basis.Identity.Scaled(new Vector3(p.Width, p.Height, p.Width)), p.Position.ToWorld(p.Height / 2f)),
                new Color((float)p.Marking.Band / 4f, (float)p.Marking.Damage / 2f, int.Parse(p.Marking.Number) / 512f, 1f))));
        GD.Print($"Pillars: {snapshot.Pillars.Count} concrete Landmarks, no emission or lights, reach {FarDistance:0} m.");
    }

    // Painted industrial digits, baked once into a shared atlas rather than hundreds of labels.
    private static ImageTexture NumberAtlas()
    {
        const int cell = 64;
        var image = Image.CreateEmpty(cell * 32, cell * 16, false, Image.Format.Rgba8);
        int[] masks = [0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f];
        (int X, int Y, int W, int H)[] segments = [(2, 0, 8, 2), (10, 2, 2, 10), (10, 14, 2, 10),
            (2, 24, 8, 2), (0, 14, 2, 10), (0, 2, 2, 10), (2, 12, 8, 2)];
        for (var number = 0; number < 512; number++)
        {
            var digits = number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            for (var digit = 0; digit < 3; digit++)
                for (var segment = 0; segment < 7; segment++)
                {
                    if ((masks[digits[digit] - '0'] & (1 << segment)) == 0)
                        continue;
                    var rect = segments[segment];
                    image.FillRect(new Rect2I(number % 32 * cell + 10 + digit * 16 + rect.X,
                        number / 32 * cell + 19 + rect.Y, rect.W, rect.H), Colors.White);
                }
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
