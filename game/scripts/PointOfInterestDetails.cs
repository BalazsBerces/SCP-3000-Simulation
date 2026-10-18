using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>Close-up lettering and lost belongings attached to snapshot Furniture. Anchors follow
/// the nearest wrapped image, so small details remain with their Furniture across Store seams.</summary>
public partial class PointOfInterestDetails : Node3D
{
    public CameraRig Rig { get; set; } = null!;
    private StoreSpace _space = null!;
    private readonly List<(Node3D Node, Transform3D Placement)> _anchors = [];
    private static readonly string[] Dishes =
    [
        "Bullbo med mos och lingon", "Mandelkaka Snöfrid", "Laxlåda Fjällvik", "Kåltrumma Ärtsvik",
        "Kaffe Mörkbrön", "Ostbit Hägervall", "Kanelgrott", "Smörgåsklump",
    ];

    public void Build(WorldSnapshot snapshot)
    {
        _space = snapshot.Space;
        foreach (var piece in snapshot.Furniture)
        {
            if (piece.PointOfInterestId < 0)
                continue;
            var poi = snapshot.PointsOfInterest[piece.PointOfInterestId].Type;
            var node = new Node3D();
            var spec = FurnitureCatalog.Spec(piece);
            switch (piece.Type)
            {
                case FurnitureType.MenuBoard:
                    for (var row = 0; row < 3; row++)
                        Text(node, Dishes[(piece.Id + row) % Dishes.Length],
                            new(0, spec.Height - 0.2f - 0.25f * row, spec.Depth / 2 + 0.012f), 0.075f, Colors.Ivory);
                    break;
                case FurnitureType.Note:
                    var note = Text(node, poi == PointOfInterestType.LostAndFound ? "back in\n5 min" : "Please leave\nthe lights on",
                        new(0, spec.Height + 0.003f, 0), 0.025f, Colors.DarkSlateGray, handwritten: true);
                    note.RotateX(-Mathf.Pi / 2);
                    break;
                case FurnitureType.NoticeBoard when poi == PointOfInterestType.LostAndFound:
                    var messages = new[] { "LOST\nred scarf\nask here", "FOUND\nkeys\nMonday", "Missing\numbrella\nA-2" };
                    for (var i = 0; i < messages.Length; i++)
                        Paper(node, messages[i], new((i - 1) * 0.32f, 0.37f + i % 2 * 0.16f, spec.Depth / 2 + 0.012f),
                            new(0.28f, 0.32f), 0.035f);
                    break;
                case FurnitureType.LostAndFoundSign when poi == PointOfInterestType.LostAndFound:
                    // Cover the source model's hospital wording on both faces with a generic service sign.
                    foreach (var side in new[] { -1, 1 })
                    {
                        var face = new Node3D { Rotation = new(0, side < 0 ? Mathf.Pi : 0, 0) };
                        node.AddChild(face);
                        Paper(face, "LOST & FOUND", new(0, spec.Height / 2, spec.Depth / 2 + 0.012f),
                            new(spec.Width + 0.01f, spec.Height + 0.01f), 0.07f);
                    }
                    break;
                case FurnitureType.OutOfOrderSign:
                    Paper(node, "OUT OF\nORDER", new(0, spec.Height * 0.6f, spec.Depth / 2 + 0.012f),
                        new(spec.Width * 0.9f, 0.24f), 0.055f);
                    break;
                case FurnitureType.CoatRack when poi == PointOfInterestType.LostAndFound:
                    // Two hanging coats: bodies, paired sleeves and collars; reused wool PBR maps.
                    foreach (var x in new[] { -0.18f, 0.18f })
                    {
                        var material = Wool(x < 0 ? new Color(0.3f, 0.16f, 0.12f) : new Color(0.12f, 0.18f, 0.2f));
                        Body(node, new(0.27f, 0.73f, 0.14f), new(x, 1.12f, 0.12f), material);
                        foreach (var side in new[] { -1, 1 })
                        {
                            var sleeve = Body(node, new(0.09f, 0.52f, 0.12f), new(x + side * 0.17f, 1.22f, 0.12f), material);
                            sleeve.RotateZ(side * 0.18f);
                        }
                    }
                    // Furled umbrella leaning against the rack, with a curved handle.
                    var umbrella = Wool(new Color(0.16f, 0.12f, 0.18f));
                    Body(node, new(0.10f, 0.7f, 0.10f), new(0.24f, 0.42f, -0.14f), umbrella);
                    var handle = new TorusMesh { InnerRadius = 0.035f, OuterRadius = 0.05f, Rings = 12, RingSegments = 6 };
                    node.AddChild(new MeshInstance3D { Mesh = handle, MaterialOverride = umbrella,
                        Position = new(0.24f, 0.81f, -0.14f), Rotation = new(Mathf.Pi / 2, 0, 0) });
                    break;
                case FurnitureType.UnsortedCart when poi == PointOfInterestType.LostAndFound:
                    Body(node, new(0.45f, 0.18f, 0.35f), new(0, 0.48f, 0), Wool(new Color(0.4f, 0.22f, 0.18f)));
                    Body(node, new(0.32f, 0.22f, 0.28f), new(0.12f, 0.66f, 0.08f), Wool(new Color(0.18f, 0.24f, 0.22f)));
                    break;
            }
            if (node.GetChildCount() == 0)
            {
                node.Free();
                continue;
            }
            AddChild(node);
            _anchors.Add((node, FurnitureRenderer.Placement(piece)));
        }
    }

    public override void _Process(double delta)
    {
        var camera = Rig.Camera.GlobalPosition;
        foreach (var (node, placement) in _anchors)
        {
            var offset = _space.Delta(camera.ToStore(), placement.Origin.ToStore());
            node.Transform = placement with { Origin = new(camera.X + offset.X, placement.Origin.Y, camera.Z + offset.Y) };
            node.Visible = !FurnitureDetail.Coarse && camera.DistanceTo(node.Position) < 60f;
        }
    }

    private static Label3D Text(Node3D parent, string text, Vector3 position, float height, Color colour, bool handwritten = false)
    {
        var label = new Label3D { Text = text, Position = position, FontSize = 48, PixelSize = height / 48,
            OutlineSize = 0, Modulate = colour, Shaded = true, DoubleSided = false };
        if (handwritten)
            label.Font = new SystemFont { FontNames = ["Segoe Print", "Comic Sans MS"] };
        parent.AddChild(label);
        return label;
    }

    private static void Paper(Node3D parent, string text, Vector3 position, Vector2 size, float height)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/furniture.gdshader") };
        material.SetShaderParameter("albedo_color", new Color(0.94f, 0.92f, 0.83f));
        material.SetShaderParameter("roughness", 0.9f);
        parent.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = size }, MaterialOverride = material, Position = position });
        Text(parent, text, position + new Vector3(0, 0, 0.002f), height, Colors.DarkSlateGray, handwritten: true);
    }

    private static ShaderMaterial Wool(Color colour)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/furniture.gdshader") };
        material.SetShaderParameter("albedo_color", colour);
        material.SetShaderParameter("textured", true);
        material.SetShaderParameter("normal_enabled", true);
        foreach (var kind in new[] { "albedo", "normal", "roughness" })
            material.SetShaderParameter($"{kind}_texture", GD.Load<Texture2D>($"res://assets/textures/wool_{kind}.jpg"));
        return material;
    }

    private static MeshInstance3D Body(Node3D parent, Vector3 size, Vector3 position, Material material)
    {
        var body = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.5f, Height = 2f, RadialSegments = 12, Rings = 4 },
            Scale = size / new Vector3(1, 2, 1), Position = position, MaterialOverride = material };
        parent.AddChild(body);
        return body;
    }
}
