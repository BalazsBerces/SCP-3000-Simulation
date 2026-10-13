using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Builds one mesh per Furniture type and variant from simple parts (boxes and cylinders) sized from the
/// core's Furniture specs, with CC0 PBR materials (see assets/textures/CREDITS.md). Mesh space:
/// origin at the centre of the footprint on its base, X along the width, Z along the depth with
/// the front facing +Z, Y up. Materials marked as tinted take their colour from the instance.
/// Each mesh also has a coarse version for the distance (level of detail) that keeps the silhouette:
/// legs, poles and frames stay (thin round ones as plain four-sided posts), only small details
/// (handles, trim, tape strips) are left out, round parts have fewer sides and materials skip
/// their normal maps. <see cref="SilhouetteCheck"/> compares the two.
/// </summary>
public static class FurnitureMeshes
{
    private enum Part
    {
        Pine,
        Teak,
        Linen,
        PaintedMetal,
        Wool,
        Oak,
        Walnut,
        Maple,
        Fabric,
        Bedding,
        Cardboard,
        RackUpright,
        RackBeam,
        DarkSteel,
        LightSteel,
        Chrome,
        Ceramic,
        Glass,
        Rubber,
        Plastic,
        Leaf,
        Terracotta,
        Soil,
        LampShade,
    }

    // Emission of a lit lamp shade.
    private const float LampShadeGlow = 4f;

    // A part thinner than this (in its two smaller extents) counts as thin, in metres. Coarse meshes
    // draw thin round parts as plain box posts and may leave out thin parts that are small details.
    private const float CoarseMinThickness = 0.06f;

    // A thin part shorter than this that does not stand on the floor is a detail (a handle, a knob,
    // a trim piece) and is left out of coarse meshes, in metres. Longer thin parts (legs, poles,
    // frames, lamp arms) and anything standing on the floor carry the silhouette and stay.
    private const float CoarseDetailLength = 0.15f;

    // Coarse meshes also leave out sheets thinner than this that are narrower than
    // CoarseMaxStripWidth (tape, trim lines, labels), in metres; larger thin panels (glass, mirrors,
    // back panels) stay. They also leave out every downward face: coarse meshes are drawn only far
    // below the Observer camera, which never sees an underside there. Full and coarse meshes both
    // leave out faces hidden against another part of the piece.
    private const float CoarseMinSheetThickness = 0.01f;
    private const float CoarseMaxStripWidth = 0.3f;

    private static readonly Dictionary<(FurnitureType, int Variant, bool Coarse), ArrayMesh> Cache = new();
    private static Dictionary<Part, ShaderMaterial>? _materials;
    private static ShaderMaterial? _coarseMaterial;

    // Size of the part tables in the furniture shader; at least the number of parts.
    private const int PartSlots = 32;

    /// <summary>How brightly every lamp shade glows, from 0 (off) to 1 (lit).</summary>
    public static void SetLampGlow(float level)
    {
        var energy = LampShadeGlow * Mathf.Max(level, 0f);
        Materials()[Part.LampShade].SetShaderParameter("emission_energy", energy);
        CoarseMaterial().SetShaderParameter("emission_energy", energy);
    }

    /// <summary>The coarse mesh for a Furniture type and variant, drawn in the distance; built once and shared.</summary>
    public static ArrayMesh CoarseFor(FurnitureType type, int variant = 0) => Get(type, variant, coarse: true);

    /// <summary>
    /// Upright rectangles in mesh space, four corners each, inside the solid, full-height parts of
    /// a Furniture type (wardrobe and fridge bodies, shelving back panels) that hide what stands
    /// behind them; used as occluders for occlusion culling. Flat panels rather than boxes keep the
    /// occluders cheap. Empty for Furniture that is open, low or small.
    /// </summary>
    public static Vector3[] OccluderQuads(FurnitureType type, int variant = 0)
    {
        var spec = FurnitureCatalog.Spec(type, variant);
        var (hw, hd, h) = (spec.Width / 2f, spec.Depth / 2f, spec.Height);
        Vector3[] Across(float z, float bottom) => [new(-hw, bottom, z), new(hw, bottom, z), new(hw, h, z), new(-hw, h, z)];
        Vector3[] Along(float x) => [new(x, 0f, -hd), new(x, 0f, hd), new(x, h, hd), new(x, h, -hd)];
        return type switch
        {
            // Solid bodies: two panels crossing at the centre.
            FurnitureType.Wardrobe or FurnitureType.Fridge or FurnitureType.FilingCabinet or FurnitureType.Dresser
                or FurnitureType.ShoeCabinet or FurnitureType.WashingMachine or FurnitureType.Stove => [.. Across(0f, 0f), .. Along(0f)],
            // The tall pantry hides what is behind it with one panel along its width: crossing
            // panels on the many pantries in kitchen lines stalled the render startup on the
            // reference machine, as the thin mirror panels did in ticket 03a. The other kitchen
            // cabinets are low.
            FurnitureType.KitchenCabinet when variant == 2 => Across(0f, 0f),
            // The low, wide glass cabinet is too short to hide anything.
            FurnitureType.DisplayCabinet when variant == 1 => [],
            FurnitureType.DisplayCabinet => Across(-hd + 0.015f, 0.1f),
            FurnitureType.Bookcase => Across(-hd + 0.005f, 0f),
            FurnitureType.ShelfUnit => Across(-hd + 0.0075f, 0.1f),
            FurnitureType.GroceryShelf => Across(0f, 0f),
            _ => [],
        };
    }

    /// <summary>Height of a Lamp's bulb above its base, inside the shade.</summary>
    public static float BulbHeight(FurnitureType type) => type switch
    {
        FurnitureType.DeskLamp => 0.47f,
        FurnitureType.ArcLamp => 1.95f,
        FurnitureType.TableLamp => 0.4f,
        FurnitureType.FloorLamp => 1.55f,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a Lamp."),
    };

    /// <summary>The bulb in mesh space; ArcLamp's shade hangs away from its upright.</summary>
    public static Vector3 BulbOffset(FurnitureType type) =>
        new(type == FurnitureType.ArcLamp ? FurnitureCatalog.Spec(type).Width / 2f - 0.25f : 0f,
            BulbHeight(type), 0f);

    /// <summary>The mesh for a Furniture type and variant; built once and shared.</summary>
    public static ArrayMesh For(FurnitureType type, int variant = 0) => Get(type, variant, coarse: false);

    private static ArrayMesh Get(FurnitureType type, int variant, bool coarse)
    {
        if (!Cache.TryGetValue((type, variant, coarse), out var mesh))
            Cache[(type, variant, coarse)] = mesh = Build(type, variant, coarse);
        return mesh;
    }

    /// <summary>
    /// A colour for one piece: tinted materials take the piece's tint index (see
    /// <see cref="FurniturePalettes"/>) into a colour table of their material family (fabric,
    /// plastic, leaves, cardboard), so every piece of a Display shares its palette. Visual only.
    /// </summary>
    public static Color TintFor(FurniturePiece piece)
    {
        var palette = piece.Type switch
        {
            FurnitureType.Sofa or FurnitureType.Armchair or FurnitureType.Rug or FurnitureType.Bed
                or FurnitureType.CushionBin or FurnitureType.BlanketStack or FurnitureType.CurtainRack
                or FurnitureType.OfficeChair or FurnitureType.Parasol or FurnitureType.FrameDisplay => FabricColors,
            FurnitureType.BunkBed or FurnitureType.KidsTable or FurnitureType.KidsChair or FurnitureType.PlayCube or FurnitureType.ToyShelf => PlasticColors,
            FurnitureType.PottedPlant or FurnitureType.LargePlant or FurnitureType.PlanterBox => LeafColors,
            FurnitureType.FlatPackBox => CardboardColors,
            _ => null,
        };
        return palette is null ? Colors.White : palette[Math.Clamp(piece.Tint, 0, palette.Length - 1)];
    }

    // The colour tables below are indexed by tint: cream, sand, warm grey, charcoal, navy, slate blue,
    // sage, forest green, mustard, terracotta, rose, off-white.
    private static readonly Color[] FabricColors =
    [
        new(0.85f, 0.81f, 0.7f), new(0.76f, 0.67f, 0.5f), new(0.55f, 0.52f, 0.5f), new(0.22f, 0.22f, 0.24f),
        new(0.15f, 0.2f, 0.38f), new(0.35f, 0.45f, 0.58f), new(0.55f, 0.62f, 0.5f), new(0.2f, 0.36f, 0.24f),
        new(0.78f, 0.58f, 0.15f), new(0.65f, 0.34f, 0.24f), new(0.78f, 0.5f, 0.52f), new(0.92f, 0.91f, 0.88f),
    ];

    private static readonly Color[] PlasticColors =
    [
        new(0.98f, 0.92f, 0.72f), new(0.95f, 0.78f, 0.5f), new(0.7f, 0.7f, 0.7f), new(0.3f, 0.3f, 0.34f),
        new(0.15f, 0.3f, 0.75f), new(0.4f, 0.6f, 0.9f), new(0.55f, 0.82f, 0.55f), new(0.1f, 0.6f, 0.3f),
        new(1f, 0.8f, 0.1f), new(0.95f, 0.4f, 0.2f), new(1f, 0.5f, 0.7f), new(0.97f, 0.97f, 0.95f),
    ];

    // From yellow-green through mid greens to dark and blue-green.
    private static readonly Color[] LeafColors =
    [
        new(0.5f, 0.6f, 0.2f), new(0.42f, 0.56f, 0.2f), new(0.34f, 0.5f, 0.2f), new(0.28f, 0.46f, 0.2f),
        new(0.22f, 0.42f, 0.2f), new(0.2f, 0.38f, 0.22f), new(0.14f, 0.3f, 0.16f), new(0.18f, 0.4f, 0.3f),
        new(0.2f, 0.45f, 0.38f), new(0.25f, 0.5f, 0.28f), new(0.38f, 0.52f, 0.25f), new(0.3f, 0.42f, 0.34f),
    ];

    // Subtle off-white to kraft shades.
    private static readonly Color[] CardboardColors =
    [
        new(1f, 1f, 1f), new(0.97f, 0.95f, 0.91f), new(0.94f, 0.91f, 0.86f), new(0.92f, 0.9f, 0.86f),
        new(0.9f, 0.86f, 0.8f), new(0.88f, 0.83f, 0.74f), new(0.85f, 0.8f, 0.72f), new(0.96f, 0.93f, 0.9f),
        new(0.93f, 0.88f, 0.8f), new(0.82f, 0.76f, 0.66f), new(0.9f, 0.89f, 0.86f), new(0.86f, 0.82f, 0.75f),
    ];

    private static ArrayMesh Build(FurnitureType type, int variant, bool coarse)
    {
        var spec = FurnitureCatalog.Spec(type, variant);
        var (w, d, h) = (spec.Width, spec.Depth, spec.Height);
        var (hw, hd) = (w / 2f, d / 2f);
        // Wave-two legs and arms use 7 cm sections; they count as thin like the narrower first-wave legs.
        var b = new Builder(coarse, type >= FurnitureType.Desk ? 0.09f : CoarseMinThickness, type >= FurnitureType.Desk ? 5 : 0);
        if (variant > 0)
        {
            BuildVariant(b, type, variant, w, d, h);
            return b.Commit();
        }
        switch (type)
        {
            case FurnitureType.PalletRack:
            {
                foreach (var x in new[] { -hw + 0.04f, hw - 0.04f })
                    foreach (var z in new[] { -hd + 0.04f, hd - 0.04f })
                        b.Box(Part.RackUpright, new Vector3(x - 0.04f, 0f, z - 0.04f), new Vector3(x + 0.04f, h, z + 0.04f));
                foreach (var level in FurnitureCatalog.PalletRackLevels.Append(h - 0.1f))
                    foreach (var z in new[] { -hd + 0.04f, hd - 0.04f })
                        b.Box(Part.RackBeam, new Vector3(-hw + 0.08f, level - 0.1f, z - 0.03f), new Vector3(hw - 0.08f, level, z + 0.03f));
                foreach (var x in new[] { -hw + 0.04f, hw - 0.04f })
                    for (var y = 0.6f; y < h; y += 1.2f)
                        b.Box(Part.RackUpright, new Vector3(x - 0.02f, y, -hd + 0.08f), new Vector3(x + 0.02f, y + 0.04f, hd - 0.08f));
                break;
            }
            case FurnitureType.FlatPackBox:
                b.Box(Part.Cardboard, new Vector3(-hw, 0f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.LightSteel, new Vector3(-0.04f, h, -hd), new Vector3(0.04f, h + 0.003f, hd));
                break;
            case FurnitureType.ShelfUnit:
                Posts(b, Part.DarkSteel, hw, hd, h, 0.025f);
                foreach (var y in new[] { 0.1f, 0.55f, 1.0f, 1.45f, 1.9f })
                    b.Box(Part.LightSteel, new Vector3(-hw, y, -hd), new Vector3(hw, y + 0.03f, hd));
                b.Box(Part.LightSteel, new Vector3(-hw, 0.1f, -hd), new Vector3(hw, h, -hd + 0.015f));
                break;
            case FurnitureType.DisplayTable:
                b.Box(Part.Maple, new Vector3(-hw, h - 0.04f, -hd), new Vector3(hw, h, hd));
                Legs(b, Part.DarkSteel, hw - 0.06f, hd - 0.06f, h - 0.04f, 0.025f);
                break;
            case FurnitureType.TableLamp:
                b.Cylinder(Part.DarkSteel, Vector3.Zero, 0.1f, 0.1f, 0.03f);
                b.Cylinder(Part.DarkSteel, new Vector3(0f, 0.03f, 0f), 0.012f, 0.012f, 0.27f);
                b.Cylinder(Part.LampShade, new Vector3(0f, 0.3f, 0f), 0.15f, 0.09f, 0.2f);
                break;
            case FurnitureType.FloorLamp:
                b.Cylinder(Part.DarkSteel, Vector3.Zero, 0.18f, 0.18f, 0.03f);
                b.Cylinder(Part.DarkSteel, new Vector3(0f, 0.03f, 0f), 0.015f, 0.015f, 1.37f);
                b.Cylinder(Part.LampShade, new Vector3(0f, 1.4f, 0f), 0.22f, 0.14f, 0.3f);
                break;
            case FurnitureType.Sofa:
            case FurnitureType.Armchair:
            {
                var arm = type == FurnitureType.Sofa ? 0.2f : 0.14f;
                var back = type == FurnitureType.Sofa ? 0.22f : 0.2f;
                b.Box(Part.Fabric, new Vector3(-hw, 0.1f, -hd), new Vector3(hw, 0.42f, hd));
                b.Box(Part.Fabric, new Vector3(-hw, 0.42f, -hd), new Vector3(hw, h, -hd + back));
                b.Box(Part.Fabric, new Vector3(-hw, 0.42f, -hd), new Vector3(-hw + arm, 0.64f, hd));
                b.Box(Part.Fabric, new Vector3(hw - arm, 0.42f, -hd), new Vector3(hw, 0.64f, hd));
                var cushions = type == FurnitureType.Sofa ? 2 : 1;
                var span = (w - 2f * arm) / cushions;
                for (var i = 0; i < cushions; i++)
                {
                    var x0 = -hw + arm + i * span;
                    b.Box(Part.Fabric, new Vector3(x0 + 0.01f, 0.42f, -hd + back), new Vector3(x0 + span - 0.01f, 0.53f, hd - 0.02f));
                }
                Legs(b, Part.Walnut, hw - 0.06f, hd - 0.06f, 0.1f, 0.025f);
                break;
            }
            case FurnitureType.CoffeeTable:
                b.Box(Part.Oak, new Vector3(-hw, h - 0.04f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.Oak, new Vector3(-hw + 0.05f, 0.1f, -hd + 0.05f), new Vector3(hw - 0.05f, 0.12f, hd - 0.05f));
                Legs(b, Part.Oak, hw - 0.05f, hd - 0.05f, h - 0.04f, 0.025f);
                break;
            case FurnitureType.Bed:
                b.Box(Part.Oak, new Vector3(-hw, 0.1f, -hd), new Vector3(hw, 0.3f, hd));
                b.Box(Part.Bedding, new Vector3(-hw + 0.03f, 0.3f, -hd + 0.08f), new Vector3(hw - 0.03f, 0.5f, hd - 0.03f));
                b.Box(Part.Fabric, new Vector3(-hw + 0.01f, 0.5f, -hd + 0.6f), new Vector3(hw - 0.01f, 0.56f, hd - 0.01f));
                b.Box(Part.Bedding, new Vector3(-hw + 0.1f, 0.5f, -hd + 0.12f), new Vector3(-0.04f, 0.62f, -hd + 0.5f));
                b.Box(Part.Bedding, new Vector3(0.04f, 0.5f, -hd + 0.12f), new Vector3(hw - 0.1f, 0.62f, -hd + 0.5f));
                b.Box(Part.Oak, new Vector3(-hw, 0.1f, -hd), new Vector3(hw, h, -hd + 0.06f));
                Legs(b, Part.Oak, hw - 0.04f, hd - 0.04f, 0.1f, 0.03f);
                break;
            case FurnitureType.Wardrobe:
                b.Box(Part.Maple, new Vector3(-hw, 0.05f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.Rubber, new Vector3(-hw + 0.03f, 0f, -hd + 0.03f), new Vector3(hw - 0.03f, 0.05f, hd - 0.03f));
                b.Box(Part.Walnut, new Vector3(-0.004f, 0.06f, hd), new Vector3(0.004f, h - 0.01f, hd + 0.004f));
                b.Box(Part.Chrome, new Vector3(-0.06f, 1.0f, hd), new Vector3(-0.04f, 1.25f, hd + 0.03f));
                b.Box(Part.Chrome, new Vector3(0.04f, 1.0f, hd), new Vector3(0.06f, 1.25f, hd + 0.03f));
                break;
            case FurnitureType.Bookcase:
                b.Box(Part.Oak, new Vector3(-hw, 0f, -hd), new Vector3(-hw + 0.02f, h, hd));
                b.Box(Part.Oak, new Vector3(hw - 0.02f, 0f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.Oak, new Vector3(-hw, 0f, -hd), new Vector3(hw, h, -hd + 0.01f));
                for (var y = 0f; y < h; y += (h - 0.02f) / 5f)
                    b.Box(Part.Oak, new Vector3(-hw + 0.02f, y, -hd), new Vector3(hw - 0.02f, y + 0.02f, hd));
                break;
            case FurnitureType.Rug:
                b.Box(Part.Fabric, new Vector3(-hw, 0f, -hd), new Vector3(hw, h, hd));
                break;
            case FurnitureType.KitchenCabinet:
                b.Box(Part.Rubber, new Vector3(-hw, 0f, -hd), new Vector3(hw, 0.1f, hd - 0.06f));
                b.Box(Part.Maple, new Vector3(-hw + 0.005f, 0.1f, -hd), new Vector3(hw - 0.005f, h - 0.04f, hd - 0.02f));
                b.Box(Part.Walnut, new Vector3(-hw, h - 0.04f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.Chrome, new Vector3(-0.15f, h - 0.12f, hd - 0.02f), new Vector3(0.15f, h - 0.1f, hd + 0.01f));
                break;
            case FurnitureType.Fridge:
                b.Box(Part.Ceramic, new Vector3(-hw, 0f, -hd), new Vector3(hw, h, hd - 0.05f));
                b.Box(Part.Ceramic, new Vector3(-hw, 0.02f, hd - 0.05f), new Vector3(hw, 1.18f, hd));
                b.Box(Part.Ceramic, new Vector3(-hw, 1.2f, hd - 0.05f), new Vector3(hw, h - 0.02f, hd));
                b.Box(Part.Chrome, new Vector3(hw - 0.08f, 0.8f, hd), new Vector3(hw - 0.06f, 1.1f, hd + 0.03f));
                b.Box(Part.Chrome, new Vector3(hw - 0.08f, 1.3f, hd), new Vector3(hw - 0.06f, 1.5f, hd + 0.03f));
                break;
            case FurnitureType.Bathtub:
                b.Box(Part.Ceramic, new Vector3(-hw, 0f, -hd), new Vector3(hw, 0.12f, hd));
                b.Box(Part.Ceramic, new Vector3(-hw, 0.12f, -hd), new Vector3(hw, h, -hd + 0.07f));
                b.Box(Part.Ceramic, new Vector3(-hw, 0.12f, hd - 0.07f), new Vector3(hw, h, hd));
                b.Box(Part.Ceramic, new Vector3(-hw, 0.12f, -hd + 0.07f), new Vector3(-hw + 0.07f, h, hd - 0.07f));
                b.Box(Part.Ceramic, new Vector3(hw - 0.07f, 0.12f, -hd + 0.07f), new Vector3(hw, h, hd - 0.07f));
                b.Cylinder(Part.Chrome, new Vector3(-hw + 0.035f, h, 0f), 0.02f, 0.02f, 0.18f);
                break;
            case FurnitureType.Toilet:
                b.Cylinder(Part.Ceramic, new Vector3(0f, 0f, 0.08f), 0.13f, 0.18f, 0.4f);
                b.Box(Part.Ceramic, new Vector3(-0.17f, 0.4f, -0.12f), new Vector3(0.17f, 0.42f, hd - 0.02f));
                b.Box(Part.Ceramic, new Vector3(-hw, 0.35f, -hd), new Vector3(hw, h, -hd + 0.18f));
                break;
            case FurnitureType.VanityUnit:
                b.Box(Part.Maple, new Vector3(-hw + 0.01f, 0.12f, -hd), new Vector3(hw - 0.01f, h - 0.05f, hd - 0.02f));
                b.Box(Part.Ceramic, new Vector3(-hw, h - 0.05f, -hd), new Vector3(hw, h, hd));
                b.Cylinder(Part.Chrome, new Vector3(0f, h, -hd + 0.08f), 0.015f, 0.015f, 0.2f);
                Legs(b, Part.Chrome, hw - 0.05f, hd - 0.05f, 0.12f, 0.015f);
                break;
            case FurnitureType.GroceryShelf:
                b.Box(Part.LightSteel, new Vector3(-hw, 0f, -hd), new Vector3(hw, 0.12f, hd));
                b.Box(Part.LightSteel, new Vector3(-hw, 0.12f, -0.02f), new Vector3(hw, h, 0.02f));
                foreach (var y in new[] { 0.5f, 0.9f, 1.3f })
                {
                    b.Box(Part.LightSteel, new Vector3(-hw, y, -hd + 0.02f), new Vector3(hw, y + 0.025f, -0.02f));
                    b.Box(Part.LightSteel, new Vector3(-hw, y, 0.02f), new Vector3(hw, y + 0.025f, hd - 0.02f));
                }
                b.Box(Part.RackBeam, new Vector3(-hw, h - 0.08f, -0.03f), new Vector3(hw, h, 0.03f));
                break;
            case FurnitureType.Freezer:
                b.Box(Part.Ceramic, new Vector3(-hw, 0f, -hd), new Vector3(hw, h - 0.12f, hd));
                b.Box(Part.LightSteel, new Vector3(-hw, h - 0.12f, -hd), new Vector3(hw, h - 0.08f, hd));
                b.Box(Part.Glass, new Vector3(-hw + 0.04f, h - 0.08f, -hd + 0.04f), new Vector3(hw - 0.04f, h - 0.06f, hd - 0.04f));
                b.Box(Part.LightSteel, new Vector3(-hw, h - 0.08f, -hd), new Vector3(hw, h, -hd + 0.04f));
                b.Box(Part.LightSteel, new Vector3(-hw, h - 0.08f, hd - 0.04f), new Vector3(hw, h, hd));
                break;
            case FurnitureType.Counter:
                b.Box(Part.Rubber, new Vector3(-hw, 0f, -hd + 0.06f), new Vector3(hw, 0.1f, hd - 0.06f));
                b.Box(Part.Walnut, new Vector3(-hw, 0.1f, -hd + 0.03f), new Vector3(hw, h - 0.05f, hd - 0.03f));
                b.Box(Part.Maple, new Vector3(-hw, h - 0.05f, -hd), new Vector3(hw, h, hd));
                break;
            case FurnitureType.DiningTable:
                b.Box(Part.Oak, new Vector3(-hw, h - 0.04f, -hd), new Vector3(hw, h, hd));
                Legs(b, Part.Oak, hw - 0.08f, hd - 0.08f, h - 0.04f, 0.03f);
                break;
            case FurnitureType.Chair:
                b.Box(Part.Oak, new Vector3(-hw, 0.43f, -hd), new Vector3(hw, 0.47f, hd));
                Legs(b, Part.Oak, hw - 0.03f, hd - 0.03f, 0.43f, 0.018f);
                b.Box(Part.Oak, new Vector3(-hw, 0.47f, -hd), new Vector3(-hw + 0.035f, h, -hd + 0.035f));
                b.Box(Part.Oak, new Vector3(hw - 0.035f, 0.47f, -hd), new Vector3(hw, h, -hd + 0.035f));
                b.Box(Part.Oak, new Vector3(-hw, 0.62f, -hd), new Vector3(hw, h - 0.02f, -hd + 0.02f));
                break;
            case FurnitureType.PlantTable:
                b.Box(Part.LightSteel, new Vector3(-hw, h - 0.04f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.LightSteel, new Vector3(-hw + 0.05f, 0.15f, -hd + 0.05f), new Vector3(hw - 0.05f, 0.17f, hd - 0.05f));
                Legs(b, Part.LightSteel, hw - 0.05f, hd - 0.05f, h - 0.04f, 0.02f);
                break;
            case FurnitureType.PottedPlant:
                b.Cylinder(Part.Terracotta, Vector3.Zero, 0.1f, 0.14f, 0.24f);
                b.Cylinder(Part.Soil, new Vector3(0f, 0.24f, 0f), 0.13f, 0.13f, 0.01f);
                b.Cylinder(Part.Leaf, new Vector3(0f, 0.25f, 0f), 0.12f, 0.17f, 0.15f, segments: 7);
                b.Cylinder(Part.Leaf, new Vector3(0.02f, 0.4f, -0.01f), 0.17f, 0.04f, 0.2f, segments: 7);
                break;
            case FurnitureType.LargePlant:
                b.Cylinder(Part.Terracotta, Vector3.Zero, 0.24f, 0.3f, 0.5f);
                b.Cylinder(Part.Soil, new Vector3(0f, 0.5f, 0f), 0.28f, 0.28f, 0.01f);
                b.Cylinder(Part.Walnut, new Vector3(0f, 0.5f, 0f), 0.04f, 0.03f, 0.9f);
                b.Cylinder(Part.Leaf, new Vector3(0f, 1.1f, 0f), 0.2f, 0.35f, 0.4f, segments: 8);
                b.Cylinder(Part.Leaf, new Vector3(0.03f, 1.5f, 0.02f), 0.33f, 0.2f, 0.35f, segments: 8);
                b.Cylinder(Part.Leaf, new Vector3(-0.02f, 1.85f, 0f), 0.2f, 0.03f, 0.35f, segments: 8);
                break;
            case FurnitureType.ToyShelf:
                b.Box(Part.Maple, new Vector3(-hw, 0f, -hd), new Vector3(-hw + 0.02f, h, hd));
                b.Box(Part.Maple, new Vector3(hw - 0.02f, 0f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.Maple, new Vector3(-hw, 0f, -hd), new Vector3(hw, h, -hd + 0.01f));
                for (var y = 0f; y < h; y += (h - 0.02f) / 3f)
                    b.Box(Part.Maple, new Vector3(-hw + 0.02f, y, -hd), new Vector3(hw - 0.02f, y + 0.02f, hd));
                foreach (var x in new[] { -hw / 2f, 0f, hw / 2f })
                    b.Box(Part.Maple, new Vector3(x - 0.01f, 0f, -hd), new Vector3(x + 0.01f, h, hd));
                for (var row = 0; row < 3; row++)
                    for (var col = 0; col < 4; col++)
                        if ((row + col) % 3 != 1)
                        {
                            var x0 = -hw + 0.03f + col * (w - 0.06f) / 4f;
                            var y0 = 0.02f + row * (h - 0.02f) / 3f;
                            b.Box(Part.Plastic, new Vector3(x0 + 0.02f, y0, -hd + 0.03f), new Vector3(x0 + (w - 0.06f) / 4f - 0.03f, y0 + 0.24f, hd - 0.02f));
                        }
                break;
            case FurnitureType.BunkBed:
                Posts(b, Part.Maple, hw, hd, h, 0.03f);
                foreach (var y in new[] { 0.2f, 1.05f })
                {
                    b.Box(Part.Maple, new Vector3(-hw + 0.06f, y, -hd + 0.02f), new Vector3(hw - 0.06f, y + 0.1f, hd - 0.02f));
                    b.Box(Part.Bedding, new Vector3(-hw + 0.08f, y + 0.1f, -hd + 0.05f), new Vector3(hw - 0.08f, y + 0.22f, hd - 0.05f));
                    b.Box(Part.Plastic, new Vector3(-hw + 0.5f, y + 0.22f, -hd + 0.06f), new Vector3(hw - 0.09f, y + 0.26f, hd - 0.06f));
                }
                b.Box(Part.Maple, new Vector3(-hw + 0.06f, 1.4f, -hd), new Vector3(hw - 0.06f, 1.46f, -hd + 0.03f));
                b.Box(Part.Maple, new Vector3(-hw + 0.06f, 1.4f, hd - 0.03f), new Vector3(hw - 0.6f, 1.46f, hd));
                for (var y = 0.45f; y < 1.4f; y += 0.25f)
                    b.Box(Part.Maple, new Vector3(hw - 0.5f, y, hd - 0.03f), new Vector3(hw - 0.06f, y + 0.03f, hd));
                break;
            case FurnitureType.KidsTable:
                b.Box(Part.Plastic, new Vector3(-hw, h - 0.04f, -hd), new Vector3(hw, h, hd));
                Legs(b, Part.Maple, hw - 0.05f, hd - 0.05f, h - 0.04f, 0.025f);
                break;
            case FurnitureType.KidsChair:
                b.Box(Part.Plastic, new Vector3(-hw, 0.28f, -hd), new Vector3(hw, 0.31f, hd));
                Legs(b, Part.Maple, hw - 0.03f, hd - 0.03f, 0.28f, 0.015f);
                b.Box(Part.Plastic, new Vector3(-hw, 0.36f, -hd), new Vector3(hw, h, -hd + 0.03f));
                b.Box(Part.Maple, new Vector3(-hw, 0.31f, -hd), new Vector3(-hw + 0.03f, 0.36f, -hd + 0.03f));
                b.Box(Part.Maple, new Vector3(hw - 0.03f, 0.31f, -hd), new Vector3(hw, 0.36f, -hd + 0.03f));
                break;
            case FurnitureType.PlayCube:
                b.Box(Part.Plastic, new Vector3(-hw + 0.01f, 0f, -hd + 0.01f), new Vector3(hw - 0.01f, h, hd - 0.01f));
                break;
            case FurnitureType.Desk:
            case FurnitureType.MeetingTable:
            case FurnitureType.GardenTable:
                var tablePart = type == FurnitureType.GardenTable ? Part.Teak : Part.Pine;
                b.Box(tablePart, new(-hw, h - 0.05f, -hd), new(hw, h, hd));
                Legs(b, type == FurnitureType.Desk ? Part.PaintedMetal : tablePart, hw - 0.06f, hd - 0.06f, h - 0.05f, 0.035f);
                if (type == FurnitureType.Desk)
                    b.Box(Part.Pine, new(hw - 0.4f, 0.3f, -hd), new(hw - 0.04f, h - 0.05f, hd));
                break;
            case FurnitureType.OfficeChair:
                b.Cylinder(Part.PaintedMetal, Vector3.Zero, 0.3f, 0.3f, 0.08f);
                b.Cylinder(Part.Chrome, new(0, 0.08f, 0), 0.04f, 0.04f, 0.36f);
                b.Box(Part.Linen, new(-hw, 0.44f, -hd), new(hw, 0.52f, hd));
                b.Box(Part.Linen, new(-hw, 0.52f, -hd), new(hw, h, -hd + 0.12f));
                break;
            case FurnitureType.GardenChair:
                b.Box(Part.Wool, new(-hw, 0.42f, -hd), new(hw, 0.5f, hd));
                b.Box(Part.Wool, new(-hw, 0.5f, -hd), new(hw, h, -hd + 0.06f));
                Legs(b, Part.Teak, hw - 0.04f, hd - 0.04f, 0.42f, 0.035f);
                break;
            case FurnitureType.FilingCabinet:
            case FurnitureType.Sideboard:
            case FurnitureType.ShoeCabinet:
            case FurnitureType.Dresser:
            case FurnitureType.Nightstand:
            case FurnitureType.ToyChest:
                var cabinetPart = type == FurnitureType.FilingCabinet ? Part.PaintedMetal : Part.Pine;
                b.Box(cabinetPart, new(-hw, 0.08f, -hd), new(hw, h, hd - 0.025f));
                var drawers = type == FurnitureType.ToyChest ? 1 : 3;
                for (var drawer = 0; drawer < drawers; drawer++)
                {
                    var y = 0.08f + drawer * (h - 0.08f) / drawers;
                    b.Box(cabinetPart, new(-hw + 0.015f, y + 0.01f, hd - 0.025f), new(hw - 0.015f, y + (h - 0.08f) / drawers - 0.01f, hd));
                    b.Box(Part.Chrome, new(-0.07f, y + 0.1f, hd), new(0.07f, y + 0.12f, hd + 0.015f));
                }
                Legs(b, cabinetPart, hw - 0.04f, hd - 0.04f, 0.08f, 0.025f);
                break;
            case FurnitureType.DisplayCabinet:
            case FurnitureType.TvBench:
                b.Box(Part.Pine, new(-hw, 0.1f, -hd), new(-hw + 0.04f, h, hd));
                b.Box(Part.Pine, new(hw - 0.04f, 0.1f, -hd), new(hw, h, hd));
                b.Box(Part.Pine, new(-hw, 0.1f, -hd), new(hw, h, -hd + 0.03f));
                foreach (var y in new[] { 0.1f, h * 0.5f, h - 0.04f })
                    b.Box(Part.Pine, new(-hw, y, -hd), new(hw, y + 0.04f, hd));
                if (type == FurnitureType.DisplayCabinet)
                    b.Box(Part.Glass, new(-hw + 0.04f, 0.14f, hd - 0.02f), new(hw - 0.04f, h - 0.04f, hd));
                break;
            case FurnitureType.CushionBin:
                b.Box(Part.Wool, new(-hw, 0, -hd), new(hw, h * 0.55f, hd));
                foreach (var x in new[] { -hw / 2f, hw / 2f })
                    b.Box(Part.Linen, new(x - w * 0.22f, h * 0.55f, -hd + 0.03f), new(x + w * 0.22f, h, hd - 0.03f));
                break;
            case FurnitureType.BlanketStack:
                for (var layer = 0; layer < 5; layer++)
                    b.Box(layer % 2 == 0 ? Part.Linen : Part.Bedding,
                        new(-hw + layer * 0.008f, layer * h / 5f, -hd), new(hw, (layer + 1) * h / 5f - 0.006f, hd));
                break;
            case FurnitureType.CurtainRack:
                Posts(b, Part.PaintedMetal, hw, hd, h, 0.035f);
                b.Box(Part.PaintedMetal, new(-hw, h - 0.06f, -hd), new(hw, h, hd));
                for (var fold = 0; fold < 8; fold++)
                {
                    var x = -hw + fold * w / 8f;
                    b.Box(Part.Linen, new(x, 0.15f, -0.06f), new(x + w / 8f - 0.008f, h - 0.06f, 0.06f));
                }
                break;
            case FurnitureType.FloorMirror:
            case FurnitureType.FrameDisplay:
                b.Box(Part.Pine, new(-hw, 0.15f, -0.05f), new(hw, h, 0.05f));
                b.Box(type == FurnitureType.FloorMirror ? Part.Chrome : Part.Linen,
                    new(-hw + 0.045f, 0.2f, 0.05f), new(hw - 0.045f, h - 0.045f, 0.065f));
                Legs(b, Part.Pine, hw - 0.05f, hd - 0.05f, 0.15f, 0.035f);
                break;
            case FurnitureType.SunLounger:
                b.Box(Part.Teak, new(-hw, 0.23f, -hd), new(hw, 0.3f, hd));
                b.Box(Part.Wool, new(-hw + 0.035f, 0.3f, -hd + 0.03f), new(hw - 0.035f, 0.36f, hd - 0.03f));
                b.Box(Part.Wool, new(-hw, 0.36f, -hd), new(hw, h, -hd + 0.45f));
                Legs(b, Part.Teak, hw - 0.05f, hd - 0.08f, 0.23f, 0.035f);
                break;
            case FurnitureType.Parasol:
                b.Cylinder(Part.PaintedMetal, Vector3.Zero, 0.22f, 0.22f, 0.08f);
                b.Cylinder(Part.Teak, new(0, 0.08f, 0), 0.035f, 0.035f, h - 0.35f);
                b.Cylinder(Part.Linen, new(0, h - 0.27f, 0), hw, 0.06f, 0.27f);
                break;
            case FurnitureType.Grill:
                b.Box(Part.PaintedMetal, new(-hw, 0.6f, -hd), new(hw, 0.85f, hd));
                b.Cylinder(Part.DarkSteel, new(0, 0.85f, 0), hd, hd * 0.7f, h - 0.85f);
                Legs(b, Part.PaintedMetal, hw - 0.07f, hd - 0.07f, 0.6f, 0.035f);
                break;
            case FurnitureType.CoatRack:
                b.Cylinder(Part.Pine, Vector3.Zero, 0.3f, 0.3f, 0.08f);
                b.Cylinder(Part.Pine, new(0, 0.08f, 0), 0.06f, 0.045f, h - 0.08f);
                b.Box(Part.Pine, new(-hw, h - 0.25f, -0.035f), new(hw, h - 0.18f, 0.035f));
                b.Box(Part.Pine, new(-0.035f, h - 0.4f, -hd), new(0.035f, h - 0.33f, hd));
                break;
            case FurnitureType.HallBench:
            case FurnitureType.BarStool:
                b.Box(Part.Pine, new(-hw, h - 0.07f, -hd), new(hw, h, hd));
                Legs(b, Part.Pine, hw - 0.05f, hd - 0.05f, h - 0.07f, 0.035f);
                break;
            case FurnitureType.WashingMachine:
            case FurnitureType.Stove:
                b.Box(Part.Ceramic, new(-hw, 0, -hd), new(hw, h, hd));
                b.Box(Part.PaintedMetal, new(-hw + 0.02f, h - 0.14f, hd), new(hw - 0.02f, h - 0.02f, hd + 0.01f));
                if (type == FurnitureType.WashingMachine)
                {
                    // Cylinders stand vertically in the mesh builder; a squared porthole keeps
                    // the front readable with the same box/cylinder construction as wave one.
                    b.Box(Part.DarkSteel, new(-0.22f, 0.18f, hd), new(0.22f, 0.62f, hd + 0.015f));
                    b.Box(Part.Glass, new(-0.17f, 0.23f, hd + 0.015f), new(0.17f, 0.57f, hd + 0.025f));
                }
                else
                {
                    b.Box(Part.DarkSteel, new(-hw + 0.04f, 0.15f, hd), new(hw - 0.04f, 0.6f, hd + 0.015f));
                    foreach (var x in new[] { -0.15f, 0.15f })
                        foreach (var z in new[] { -0.15f, 0.15f })
                            b.Cylinder(Part.DarkSteel, new(x, h, z), 0.08f, 0.08f, 0.01f);
                }
                break;
            case FurnitureType.BoxCart:
                b.Box(Part.PaintedMetal, new(-hw, 0.15f, -hd), new(hw, 0.22f, hd));
                foreach (var x in new[] { -hw + 0.08f, hw - 0.08f })
                    foreach (var z in new[] { -hd + 0.08f, hd - 0.08f })
                        b.Cylinder(Part.Rubber, new(x, 0, z), 0.08f, 0.08f, 0.15f);
                b.Box(Part.PaintedMetal, new(-hw, 0.22f, -hd), new(-hw + 0.07f, h, hd));
                b.Box(Part.Cardboard, new(-hw + 0.1f, 0.22f, -hd + 0.04f), new(hw - 0.04f, 0.65f, hd - 0.04f));
                break;
            case FurnitureType.PlanterBox:
                b.Box(Part.Teak, new(-hw, 0, -hd), new(hw, 0.4f, hd));
                b.Box(Part.Soil, new(-hw + 0.035f, 0.4f, -hd + 0.035f), new(hw - 0.035f, 0.42f, hd - 0.035f));
                foreach (var x in new[] { -0.4f, 0f, 0.4f })
                    b.Cylinder(Part.Leaf, new(x, 0.42f, 0), 0.15f, 0.03f, h - 0.42f);
                break;
            case FurnitureType.DeskLamp:
                b.Cylinder(Part.PaintedMetal, Vector3.Zero, 0.14f, 0.14f, 0.04f);
                b.Cylinder(Part.Chrome, new(-0.06f, 0.04f, 0), 0.025f, 0.025f, 0.4f);
                b.Cylinder(Part.LampShade, new(0, 0.42f, 0), 0.14f, 0.08f, 0.13f);
                break;
            case FurnitureType.ArcLamp:
                b.Cylinder(Part.DarkSteel, new(-hw + 0.25f, 0, 0), 0.25f, 0.25f, 0.08f);
                b.Cylinder(Part.Chrome, new(-hw + 0.25f, 0.08f, 0), 0.04f, 0.035f, h - 0.12f);
                // Stepped slender arm approximates an arc without a separate asset pipeline.
                for (var segment = 0; segment < 6; segment++)
                {
                    var x = -hw + 0.25f + segment * (w - 0.5f) / 6f;
                    var y = h - 0.06f - 0.012f * segment * segment;
                    b.Box(Part.Chrome, new(x, y, -0.035f), new(x + (w - 0.5f) / 6f + 0.01f, y + 0.04f, 0.035f));
                }
                b.Cylinder(Part.LampShade, new(hw - 0.25f, 1.8f, 0), 0.24f, 0.13f, 0.3f);
                break;
            default:
                b.Box(Part.Maple, new Vector3(-hw, 0f, -hd), new Vector3(hw, h, hd));
                break;
        }
        return b.Commit();
    }

    /// <summary>
    /// The parts of a variant (1 or 2) of the types that have several; each has its own silhouette,
    /// sized from the variant's spec.
    /// </summary>
    private static void BuildVariant(Builder b, FurnitureType type, int variant, float w, float d, float h)
    {
        var (hw, hd) = (w / 2f, d / 2f);
        switch (type, variant)
        {
            case (FurnitureType.Chair, 1):
                // High ladder back: tall rear posts joined by four slats.
                b.Box(Part.Oak, new Vector3(-hw, 0.43f, -hd), new Vector3(hw, 0.47f, hd));
                Legs(b, Part.Oak, hw - 0.03f, hd - 0.03f, 0.43f, 0.018f);
                foreach (var x in new[] { -hw, hw - 0.035f })
                    b.Box(Part.Oak, new Vector3(x, 0.47f, -hd), new Vector3(x + 0.035f, h, -hd + 0.035f));
                foreach (var y in new[] { 0.58f, 0.7f, 0.82f, 0.94f })
                    b.Box(Part.Oak, new Vector3(-hw, y, -hd), new Vector3(hw, y + 0.06f, -hd + 0.02f));
                break;
            case (FurnitureType.Chair, 2):
                // Carver: solid splat back, top rail and two armrests on short posts.
                b.Box(Part.Walnut, new Vector3(-hw, 0.43f, -hd), new Vector3(hw, 0.48f, hd));
                Legs(b, Part.Walnut, hw - 0.03f, hd - 0.03f, 0.43f, 0.025f);
                foreach (var x in new[] { -hw, hw - 0.04f })
                    b.Box(Part.Walnut, new Vector3(x, 0.48f, -hd), new Vector3(x + 0.04f, h, -hd + 0.04f));
                b.Box(Part.Walnut, new Vector3(-hw, h - 0.08f, -hd), new Vector3(hw, h, -hd + 0.04f));
                b.Box(Part.Walnut, new Vector3(-hw + 0.1f, 0.48f, -hd), new Vector3(hw - 0.1f, h - 0.08f, -hd + 0.03f));
                foreach (var x in new[] { -hw, hw - 0.04f })
                {
                    b.Box(Part.Walnut, new Vector3(x, 0.62f, -hd), new Vector3(x + 0.04f, 0.66f, hd));
                    b.Box(Part.Walnut, new Vector3(x, 0.48f, hd - 0.04f), new Vector3(x + 0.04f, 0.62f, hd));
                }
                break;
            case (FurnitureType.OfficeChair, 1):
                // Low-back task chair: thin pad seat, small floating backrest.
                b.Cylinder(Part.PaintedMetal, Vector3.Zero, 0.28f, 0.28f, 0.06f);
                b.Cylinder(Part.Chrome, new(0, 0.06f, 0), 0.035f, 0.035f, 0.33f);
                b.Box(Part.Linen, new(-hw, 0.39f, -hd), new(hw, 0.46f, hd));
                b.Box(Part.PaintedMetal, new(-0.03f, 0.46f, -hd + 0.02f), new(0.03f, 0.55f, -hd + 0.08f));
                b.Box(Part.Linen, new(-hw + 0.06f, 0.55f, -hd), new(hw - 0.06f, h, -hd + 0.08f));
                break;
            case (FurnitureType.OfficeChair, 2):
                // High-back executive chair: thick seat, tall back with headrest, armrests.
                b.Cylinder(Part.DarkSteel, Vector3.Zero, 0.35f, 0.35f, 0.08f);
                b.Cylinder(Part.Chrome, new(0, 0.08f, 0), 0.045f, 0.045f, 0.37f);
                b.Box(Part.Linen, new(-hw, 0.45f, -hd), new(hw, 0.58f, hd));
                b.Box(Part.Linen, new(-hw, 0.58f, -hd), new(hw, h - 0.2f, -hd + 0.16f));
                b.Box(Part.Linen, new(-hw + 0.1f, h - 0.2f, -hd + 0.02f), new(hw - 0.1f, h, -hd + 0.14f));
                foreach (var x in new[] { -hw, hw - 0.05f })
                {
                    b.Box(Part.DarkSteel, new(x, 0.58f, -0.1f), new(x + 0.05f, 0.7f, -0.05f));
                    b.Box(Part.Linen, new(x - 0.01f, 0.7f, -hd + 0.1f), new(x + 0.06f, 0.75f, hd - 0.05f));
                }
                break;
            case (FurnitureType.Desk, 1):
                // Compact trestle desk: two side panels braced by a stretcher, no pedestal.
                b.Box(Part.Pine, new(-hw, h - 0.04f, -hd), new(hw, h, hd));
                foreach (var x in new[] { -hw + 0.06f, hw - 0.1f })
                    b.Box(Part.PaintedMetal, new(x, 0f, -hd + 0.05f), new(x + 0.04f, h - 0.04f, hd - 0.05f));
                b.Box(Part.PaintedMetal, new(-hw + 0.1f, h - 0.2f, -0.02f), new(hw - 0.1f, h - 0.14f, 0.02f));
                break;
            case (FurnitureType.Desk, 2):
                // Large desk: thick top on two drawer pedestals and a back panel.
                b.Box(Part.Walnut, new(-hw, h - 0.06f, -hd), new(hw, h, hd));
                foreach (var side in new[] { -1f, 1f })
                {
                    var (x0, x1) = side < 0 ? (-hw + 0.03f, -hw + 0.5f) : (hw - 0.5f, hw - 0.03f);
                    b.Box(Part.Pine, new(x0, 0.05f, -hd + 0.03f), new(x1, h - 0.06f, hd - 0.03f));
                    b.Box(Part.Rubber, new(x0 + 0.02f, 0f, -hd + 0.06f), new(x1 - 0.02f, 0.05f, hd - 0.06f));
                    for (var drawer = 0; drawer < 3; drawer++)
                        b.Box(Part.Chrome, new(x0 + 0.15f, 0.2f + drawer * 0.18f, hd - 0.03f), new(x1 - 0.15f, 0.22f + drawer * 0.18f, hd));
                }
                b.Box(Part.Pine, new(-hw + 0.5f, 0.25f, -hd + 0.03f), new(hw - 0.5f, h - 0.06f, -hd + 0.05f));
                break;
            case (FurnitureType.DiningTable, 1):
                // Round table: a disc top on a single column and a flared foot.
                b.Cylinder(Part.Oak, new(0, h - 0.05f, 0), hw, hw, 0.05f, segments: 20);
                b.Cylinder(Part.Oak, new(0, 0.05f, 0), 0.09f, 0.07f, h - 0.1f);
                b.Cylinder(Part.Oak, Vector3.Zero, 0.32f, 0.12f, 0.05f);
                break;
            case (FurnitureType.DiningTable, 2):
                // Refectory table: a thick plank top on heavy legs joined by a low stretcher.
                b.Box(Part.Walnut, new(-hw, h - 0.09f, -hd), new(hw, h, hd));
                Legs(b, Part.Walnut, hw - 0.15f, hd - 0.12f, h - 0.09f, 0.06f);
                b.Box(Part.Walnut, new(-hw + 0.15f, 0.18f, -0.03f), new(hw - 0.15f, 0.25f, 0.03f));
                break;
            case (FurnitureType.GardenChair, 1):
                // Metal bistro chair: slim steel frame, thin seat, rail back.
                b.Box(Part.PaintedMetal, new(-hw, 0.42f, -hd), new(hw, 0.45f, hd));
                Legs(b, Part.DarkSteel, hw - 0.025f, hd - 0.025f, 0.42f, 0.012f);
                foreach (var x in new[] { -hw + 0.015f, hw - 0.015f })
                    b.Box(Part.DarkSteel, new(x - 0.012f, 0.45f, -hd + 0.015f), new(x + 0.012f, h, -hd + 0.04f));
                b.Box(Part.DarkSteel, new(-hw + 0.015f, h - 0.06f, -hd + 0.015f), new(hw - 0.015f, h, -hd + 0.04f));
                b.Box(Part.DarkSteel, new(-hw + 0.015f, 0.65f, -hd + 0.015f), new(hw - 0.015f, 0.68f, -hd + 0.035f));
                break;
            case (FurnitureType.GardenChair, 2):
                // Low lounge chair: wide teak frame with arms, deep cushion and a reclined back.
                b.Box(Part.Teak, new(-hw, 0.18f, -hd), new(hw, 0.24f, hd));
                Legs(b, Part.Teak, hw - 0.04f, hd - 0.04f, 0.18f, 0.035f);
                foreach (var x in new[] { -hw, hw - 0.07f })
                    b.Box(Part.Teak, new(x, 0.24f, -hd), new(x + 0.07f, 0.5f, hd));
                b.Box(Part.Wool, new(-hw + 0.07f, 0.24f, -hd + 0.2f), new(hw - 0.07f, 0.34f, hd));
                b.Box(Part.Wool, new(-hw + 0.07f, 0.34f, -hd), new(hw - 0.07f, h, -hd + 0.2f));
                break;
            case (FurnitureType.GardenTable, 1):
                // Round bistro table: small disc top on a steel column and base.
                b.Cylinder(Part.Teak, new(0, h - 0.04f, 0), hw, hw, 0.04f, segments: 16);
                b.Cylinder(Part.DarkSteel, new(0, 0.04f, 0), 0.025f, 0.025f, h - 0.08f);
                b.Cylinder(Part.DarkSteel, Vector3.Zero, 0.25f, 0.1f, 0.04f);
                break;
            case (FurnitureType.GardenTable, 2):
                // Picnic table: planked top on two trestle end panels and a centre beam.
                b.Box(Part.Teak, new(-hw, h - 0.05f, -hd), new(hw, h, hd));
                foreach (var x in new[] { -hw + 0.25f, hw - 0.3f })
                    b.Box(Part.Teak, new(x, 0f, -hd + 0.08f), new(x + 0.05f, h - 0.05f, hd - 0.08f));
                b.Box(Part.Teak, new(-hw + 0.25f, 0.3f, -0.04f), new(hw - 0.25f, 0.38f, 0.04f));
                break;
            case (FurnitureType.Sofa, 1) or (FurnitureType.Sofa, 2):
            {
                // Two-seat loveseat with low arms, or large three-seat sofa with tall arms and loose back cushions.
                var large = variant == 2;
                var arm = large ? 0.24f : 0.15f;
                var back = large ? 0.26f : 0.2f;
                var armTop = large ? 0.68f : 0.58f;
                b.Box(Part.Fabric, new Vector3(-hw, 0.1f, -hd), new Vector3(hw, 0.42f, hd));
                b.Box(Part.Fabric, new Vector3(-hw, 0.42f, -hd), new Vector3(hw, h, -hd + back));
                b.Box(Part.Fabric, new Vector3(-hw, 0.42f, -hd), new Vector3(-hw + arm, armTop, hd));
                b.Box(Part.Fabric, new Vector3(hw - arm, 0.42f, -hd), new Vector3(hw, armTop, hd));
                // The body already covers the cushion bases: keep one seat top at distance
                // instead of overlapping cushion boxes and back pads that occupy subpixels.
                var cushions = b.Coarse ? 1 : large ? 3 : 2;
                var span = (w - 2f * arm) / cushions;
                for (var i = 0; i < cushions; i++)
                {
                    var x0 = -hw + arm + i * span;
                    b.Box(Part.Fabric, new Vector3(x0 + 0.01f, 0.42f, -hd + back), new Vector3(x0 + span - 0.01f, 0.54f, hd - 0.02f));
                    if (large && !b.Coarse)
                        b.Box(Part.Fabric, new Vector3(x0 + 0.04f, 0.54f, -hd + back), new Vector3(x0 + span - 0.04f, h - 0.1f, -hd + back + 0.12f));
                }
                Legs(b, large ? Part.DarkSteel : Part.Oak, hw - 0.06f, hd - 0.06f, 0.1f, large ? 0.03f : 0.022f);
                break;
            }
            case (FurnitureType.Wardrobe, 1):
                // Double wardrobe: two doors above a drawer bank, with a cornice on top.
                b.Box(Part.Oak, new Vector3(-hw, 0.05f, -hd), new Vector3(hw, h - 0.06f, hd));
                b.Box(Part.Oak, new Vector3(-hw - 0.02f, h - 0.06f, -hd - 0.02f), new Vector3(hw + 0.02f, h, hd + 0.02f));
                b.Box(Part.Rubber, new Vector3(-hw + 0.03f, 0f, -hd + 0.03f), new Vector3(hw - 0.03f, 0.05f, hd - 0.03f));
                b.Box(Part.Walnut, new Vector3(-0.004f, 0.6f, hd), new Vector3(0.004f, h - 0.08f, hd + 0.004f));
                b.Box(Part.Walnut, new Vector3(-hw + 0.02f, 0.58f, hd), new Vector3(hw - 0.02f, 0.6f, hd + 0.004f));
                b.Box(Part.Chrome, new Vector3(-0.07f, 1.1f, hd), new Vector3(-0.05f, 1.4f, hd + 0.03f));
                b.Box(Part.Chrome, new Vector3(0.05f, 1.1f, hd), new Vector3(0.07f, 1.4f, hd + 0.03f));
                foreach (var x in new[] { -hw / 2f, hw / 2f })
                    b.Box(Part.Chrome, new Vector3(x - 0.1f, 0.3f, hd), new Vector3(x + 0.1f, 0.32f, hd + 0.015f));
                break;
            case (FurnitureType.Wardrobe, 2):
                // Narrow tall wardrobe: one door with a mirror strip and a single long handle.
                b.Box(Part.Walnut, new Vector3(-hw, 0.05f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.Rubber, new Vector3(-hw + 0.03f, 0f, -hd + 0.03f), new Vector3(hw - 0.03f, 0.05f, hd - 0.03f));
                b.Box(Part.Chrome, new Vector3(-hw + 0.08f, 0.35f, hd), new Vector3(hw - 0.08f, h - 0.2f, hd + 0.008f));
                b.Box(Part.Chrome, new Vector3(hw - 0.07f, 0.9f, hd + 0.008f), new Vector3(hw - 0.05f, 1.4f, hd + 0.035f));
                break;
            case (FurnitureType.KitchenCabinet, 1):
                // Wide drawer cabinet: plinth, three drawer fronts with long handles, worktop.
                b.Box(Part.Rubber, new Vector3(-hw, 0f, -hd), new Vector3(hw, 0.1f, hd - 0.06f));
                b.Box(Part.Maple, new Vector3(-hw + 0.005f, 0.1f, -hd), new Vector3(hw - 0.005f, h - 0.04f, hd - 0.02f));
                b.Box(Part.Walnut, new Vector3(-hw, h - 0.04f, -hd), new Vector3(hw, h, hd));
                for (var drawer = 0; !b.Coarse && drawer < 3; drawer++)
                {
                    var y = 0.1f + drawer * (h - 0.14f) / 3f;
                    b.Box(Part.Pine, new Vector3(-hw + 0.01f, y + 0.01f, hd - 0.02f), new Vector3(hw - 0.01f, y + (h - 0.14f) / 3f - 0.01f, hd));
                    b.Box(Part.Chrome, new Vector3(-0.2f, y + 0.2f, hd), new Vector3(0.2f, y + 0.22f, hd + 0.02f));
                }
                break;
            case (FurnitureType.KitchenCabinet, 2):
                // Tall pantry: floor-to-ceiling cupboard with two doors and tall handles.
                b.Box(Part.Rubber, new Vector3(-hw, 0f, -hd), new Vector3(hw, 0.1f, hd - 0.06f));
                b.Box(Part.Maple, new Vector3(-hw, 0.1f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.Walnut, new Vector3(-hw + 0.01f, 1.0f, hd), new Vector3(hw - 0.01f, 1.015f, hd + 0.004f));
                b.Box(Part.Chrome, new Vector3(hw - 0.08f, 0.5f, hd), new Vector3(hw - 0.06f, 0.85f, hd + 0.03f));
                b.Box(Part.Chrome, new Vector3(hw - 0.08f, 1.15f, hd), new Vector3(hw - 0.06f, 1.5f, hd + 0.03f));
                break;
            case (FurnitureType.DisplayCabinet, 1):
                // Low, wide glass cabinet: two glass panes over a single shelf, on short legs.
                b.Box(Part.Pine, new(-hw, 0.1f, -hd), new(-hw + 0.04f, h, hd));
                b.Box(Part.Pine, new(hw - 0.04f, 0.1f, -hd), new(hw, h, hd));
                b.Box(Part.Pine, new(-hw, 0.1f, -hd), new(hw, h, -hd + 0.03f));
                foreach (var y in new[] { 0.1f, h * 0.5f, h - 0.04f })
                    b.Box(Part.Pine, new(-hw, y, -hd), new(hw, y + 0.04f, hd));
                foreach (var x in new[] { -hw / 2f, hw / 2f })
                    b.Box(Part.Glass, new(x - hw / 2f + 0.03f, 0.14f, hd - 0.02f), new(x + hw / 2f - 0.03f, h - 0.04f, hd));
                b.Box(Part.Pine, new(-0.015f, 0.14f, hd - 0.025f), new(0.015f, h - 0.04f, hd + 0.005f));
                Legs(b, Part.Pine, hw - 0.05f, hd - 0.05f, 0.1f, 0.03f);
                break;
            case (FurnitureType.DisplayCabinet, 2):
                // Narrow glass tower: tall frame with four shelves behind a full-height glass door.
                b.Box(Part.Walnut, new(-hw, 0.1f, -hd), new(-hw + 0.04f, h, hd));
                b.Box(Part.Walnut, new(hw - 0.04f, 0.1f, -hd), new(hw, h, hd));
                b.Box(Part.Walnut, new(-hw, 0.1f, -hd), new(hw, h, -hd + 0.03f));
                foreach (var y in new[] { 0.1f, 0.6f, 1.05f, 1.45f, h - 0.04f })
                    b.Box(Part.Walnut, new(-hw, y, -hd), new(hw, y + 0.04f, hd));
                b.Box(Part.Glass, new(-hw + 0.04f, 0.14f, hd - 0.02f), new(hw - 0.04f, h - 0.04f, hd));
                b.Box(Part.Rubber, new(-hw + 0.02f, 0f, -hd + 0.02f), new(hw - 0.02f, 0.1f, hd - 0.02f));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, $"{type} has no variant {variant}.");
        }
    }

    /// <summary>Four legs of square section reaching from the floor to <paramref name="top"/>.</summary>
    private static void Legs(Builder b, Part part, float x, float z, float top, float half)
    {
        foreach (var lx in new[] { -x, x })
            foreach (var lz in new[] { -z, z })
                b.Box(part, new Vector3(lx - half, 0f, lz - half), new Vector3(lx + half, top, lz + half));
    }

    /// <summary>Four corner posts flush with the footprint.</summary>
    private static void Posts(Builder b, Part part, float hw, float hd, float h, float half) =>
        Legs(b, part, hw - half, hd - half, h, half);

    private static Dictionary<Part, ShaderMaterial> Materials()
    {
        if (_materials is not null)
            return _materials;
        // One shader for every part, so all Furniture receives the Lamp pools' light.
        var shader = GD.Load<Shader>("res://shaders/furniture.gdshader");
        ShaderMaterial Plain(Color color, float roughness, float metallic = 0f, bool tinted = false)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("albedo_color", color);
            material.SetShaderParameter("roughness", roughness);
            material.SetShaderParameter("metallic", metallic);
            material.SetShaderParameter("tinted", tinted);
            return material;
        }
        ShaderMaterial Textured(string name, float metres, Color tint, bool tinted = false)
        {
            var material = Plain(tint, 1f, tinted: tinted);
            material.SetShaderParameter("textured", true);
            material.SetShaderParameter("normal_enabled", true);
            material.SetShaderParameter("albedo_texture", GD.Load<Texture2D>($"res://assets/textures/{name}_albedo.jpg"));
            material.SetShaderParameter("normal_texture", GD.Load<Texture2D>($"res://assets/textures/{name}_normal.jpg"));
            material.SetShaderParameter("roughness_texture", GD.Load<Texture2D>($"res://assets/textures/{name}_roughness.jpg"));
            material.SetShaderParameter("uv_scale", new Vector2(1f / metres, 1f / metres));
            return material;
        }
        var shade = Plain(new Color(0.95f, 0.9f, 0.78f), 0.8f);
        shade.SetShaderParameter("emission_color", new Color(1f, 0.85f, 0.6f));
        shade.SetShaderParameter("emission_energy", LampShadeGlow);

        _materials = new Dictionary<Part, ShaderMaterial>
        {
            [Part.Pine] = Textured("pine", 1.2f, Colors.White),
            [Part.Teak] = Textured("teak", 1.2f, Colors.White),
            [Part.Linen] = Textured("linen", 0.8f, Colors.White, tinted: true),
            [Part.PaintedMetal] = Textured("painted_metal", 1f, Colors.White),
            [Part.Wool] = Textured("wool", 0.6f, Colors.White),
            [Part.Oak] = Textured("oak", 1.2f, Colors.White),
            [Part.Walnut] = Textured("walnut", 1.2f, Colors.White),
            [Part.Maple] = Textured("maple", 1.2f, Colors.White),
            [Part.Fabric] = Textured("fabric", 0.8f, Colors.White, tinted: true),
            [Part.Bedding] = Textured("fabric", 0.8f, new Color(0.95f, 0.95f, 0.93f)),
            [Part.Cardboard] = Textured("cardboard", 1.0f, Colors.White, tinted: true),
            [Part.RackUpright] = Plain(new Color(0.12f, 0.25f, 0.55f), 0.45f, 0.4f),
            [Part.RackBeam] = Plain(new Color(0.9f, 0.42f, 0.08f), 0.45f, 0.3f),
            [Part.DarkSteel] = Plain(new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.6f),
            [Part.LightSteel] = Plain(new Color(0.72f, 0.73f, 0.74f), 0.45f, 0.5f),
            [Part.Chrome] = Plain(new Color(0.85f, 0.86f, 0.88f), 0.15f, 1f),
            [Part.Ceramic] = Plain(new Color(0.93f, 0.93f, 0.92f), 0.2f),
            [Part.Glass] = Plain(new Color(0.55f, 0.65f, 0.7f), 0.05f, 0.3f),
            [Part.Rubber] = Plain(new Color(0.08f, 0.08f, 0.08f), 0.9f),
            [Part.Plastic] = Plain(Colors.White, 0.5f, tinted: true),
            [Part.Leaf] = Plain(Colors.White, 0.7f, tinted: true),
            [Part.Terracotta] = Plain(new Color(0.62f, 0.32f, 0.2f), 0.85f),
            [Part.Soil] = Plain(new Color(0.18f, 0.12f, 0.08f), 1f),
            [Part.LampShade] = shade,
        };
        return _materials;
    }

    /// <summary>
    /// The one material of every coarse mesh. A coarse mesh draws all its parts in one surface, so
    /// a chunk costs one draw call per Furniture type and variant however many materials it uses:
    /// each vertex carries its part in UV2, and the part's look (the average colour and roughness of
    /// its textures, its metallic, whether the instance colour tints it, its glow) comes from tables
    /// in the shader. At the distances coarse meshes are drawn from, textures blur to that average.
    /// </summary>
    private static ShaderMaterial CoarseMaterial()
    {
        if (_coarseMaterial is not null)
            return _coarseMaterial;
        var albedo = new Color[PartSlots];
        var surface = new Vector2[PartSlots];
        foreach (var (part, material) in Materials())
        {
            var color = (Color)material.GetShaderParameter("albedo_color");
            var roughness = (float)material.GetShaderParameter("roughness");
            if ((bool)material.GetShaderParameter("textured"))
            {
                color *= Average((Texture2D)material.GetShaderParameter("albedo_texture"));
                roughness *= Average((Texture2D)material.GetShaderParameter("roughness_texture")).R;
            }
            var linear = color.SrgbToLinear();
            albedo[(int)part] = new Color(linear.R, linear.G, linear.B, (bool)material.GetShaderParameter("tinted") ? 1f : 0f);
            surface[(int)part] = new Vector2(roughness, (float)material.GetShaderParameter("metallic"));
        }
        var shade = Materials()[Part.LampShade];
        _coarseMaterial = new ShaderMaterial { Shader = shade.Shader };
        _coarseMaterial.SetShaderParameter("coarse_parts", true);
        _coarseMaterial.SetShaderParameter("part_albedo", albedo);
        _coarseMaterial.SetShaderParameter("part_surface", surface);
        _coarseMaterial.SetShaderParameter("emissive_part", (int)Part.LampShade);
        _coarseMaterial.SetShaderParameter("emission_color", shade.GetShaderParameter("emission_color"));
        _coarseMaterial.SetShaderParameter("emission_energy", shade.GetShaderParameter("emission_energy"));
        return _coarseMaterial;
    }

    /// <summary>The average colour of a texture, sampled on a sparse grid.</summary>
    private static Color Average(Texture2D texture)
    {
        var image = texture.GetImage();
        if (image.IsCompressed())
            image.Decompress();
        if (image.IsCompressed())
            return Colors.White;
        var (sum, count) = (new Color(0f, 0f, 0f, 0f), 0);
        var step = Math.Max(1, image.GetWidth() / 64);
        for (var y = 0; y < image.GetHeight(); y += step)
            for (var x = 0; x < image.GetWidth(); x += step)
            {
                sum += image.GetPixel(x, y);
                count++;
            }
        return sum / count;
    }

    /// <summary>
    /// Accumulates parts into one surface per material, with UVs in metres and tangents for normal maps.
    /// A coarse builder leaves out small details, draws thin round parts as four-sided posts, gives
    /// other round parts fewer sides and puts every part in one surface, marking each vertex with its part.
    /// </summary>
    private sealed class Builder(bool coarse, float minThickness, int coarseSegments)
    {
        public bool Coarse => coarse;
        private readonly Dictionary<Part, SurfaceTool> _surfaces = new();
        private readonly List<(Part Part, Vector3 Min, Vector3 Max)> _boxes = new();
        private Part _part;

        public void Box(Part part, Vector3 min, Vector3 max)
        {
            var size = max - min;
            float[] extents = [size.X, size.Y, size.Z];
            Array.Sort(extents);
            // Boxes wait for the whole piece, so faces hidden against other parts can go.
            if (!coarse || !IsDetail(extents, min.Y))
                _boxes.Add((part, min, max));
        }

        private void EmitBox(Part part, Vector3 min, Vector3 max, Func<int, bool, bool> hidden)
        {
            var size = max - min;
            var tool = Surface(part);
            // Each face: normal, the two in-face axes (scaled by size) and its corner.
            if (!hidden(2, true))
                Face(tool, min with { Z = max.Z }, new Vector3(size.X, 0, 0), new Vector3(0, size.Y, 0), Vector3.Back);
            if (!hidden(2, false))
                Face(tool, max with { Y = min.Y, Z = min.Z }, new Vector3(-size.X, 0, 0), new Vector3(0, size.Y, 0), Vector3.Forward);
            if (!hidden(0, false))
                Face(tool, min, new Vector3(0, 0, size.Z), new Vector3(0, size.Y, 0), Vector3.Left);
            if (!hidden(0, true))
                Face(tool, max with { Y = min.Y }, new Vector3(0, 0, -size.Z), new Vector3(0, size.Y, 0), Vector3.Right);
            if (!hidden(1, true))
                Face(tool, min with { Y = max.Y, Z = max.Z }, new Vector3(size.X, 0, 0), new Vector3(0, 0, -size.Z), Vector3.Up);
            if (!coarse && !hidden(1, false))
                Face(tool, min, new Vector3(size.X, 0, 0), new Vector3(0, 0, size.Z), Vector3.Down);
        }

        /// <summary>
        /// Whether the face of box <paramref name="index"/> on <paramref name="axis"/> (its
        /// upper face if <paramref name="upper"/>) lies inside or against another box that covers
        /// all of it and continues beyond it, so it can never be seen.
        /// </summary>
        private bool FaceHidden(int index, int axis, bool upper)
        {
            const float Tolerance = 1e-4f;
            var (_, min, max) = _boxes[index];
            var plane = upper ? max[axis] : min[axis];
            for (var other = 0; other < _boxes.Count; other++)
            {
                if (other == index)
                    continue;
                var (_, oMin, oMax) = _boxes[other];
                if (plane < oMin[axis] - Tolerance || plane > oMax[axis] + Tolerance)
                    continue;
                if (upper ? oMax[axis] <= plane + Tolerance : oMin[axis] >= plane - Tolerance)
                    continue;
                var covers = true;
                for (var a = 0; a < 3 && covers; a++)
                    if (a != axis)
                        covers = oMin[a] <= min[a] + Tolerance && oMax[a] >= max[a] - Tolerance;
                if (covers)
                    return true;
            }
            return false;
        }

        /// <summary>A capped cylinder (or cone frustum) standing on <paramref name="baseCentre"/>.</summary>
        public void Cylinder(Part part, Vector3 baseCentre, float bottomRadius, float topRadius, float height, int segments = 12)
        {
            if (coarse)
            {
                var diameter = 2f * Mathf.Max(bottomRadius, topRadius);
                float[] extents = [diameter, diameter, height];
                Array.Sort(extents);
                if (IsDetail(extents, baseCentre.Y))
                    return;
                if (diameter < minThickness)
                {
                    // A plain post as wide as the pole.
                    var half = diameter / 2f;
                    Box(part, baseCentre - new Vector3(half, 0f, half), baseCentre + new Vector3(half, height, half));
                    return;
                }
                segments = coarseSegments > 0 ? coarseSegments : Math.Max(5, segments / 2);
                // Fewer sides around the circle rather than inside it, so the part keeps its width.
                var circumscribe = 1f / Mathf.Cos(Mathf.Pi / segments);
                bottomRadius *= circumscribe;
                topRadius *= circumscribe;
            }
            var tool = Surface(part);
            var top = baseCentre + new Vector3(0, height, 0);
            var slope = (bottomRadius - topRadius) / height;
            for (var i = 0; i < segments; i++)
            {
                var a0 = Mathf.Tau * i / segments;
                var a1 = Mathf.Tau * (i + 1) / segments;
                var (d0, d1) = (new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)));
                var (n0, n1) = ((d0 + new Vector3(0, slope, 0)).Normalized(), (d1 + new Vector3(0, slope, 0)).Normalized());
                var (u0, u1) = (bottomRadius * a0, bottomRadius * a1);
                var b0 = baseCentre + d0 * bottomRadius;
                var b1 = baseCentre + d1 * bottomRadius;
                var t0 = top + d0 * topRadius;
                var t1 = top + d1 * topRadius;
                // Clockwise seen from outside, like the box faces.
                Vertex(tool, b0, n0, new Vector2(u0, 0));
                Vertex(tool, t1, n1, new Vector2(u1, height));
                Vertex(tool, t0, n0, new Vector2(u0, height));
                Vertex(tool, b0, n0, new Vector2(u0, 0));
                Vertex(tool, b1, n1, new Vector2(u1, 0));
                Vertex(tool, t1, n1, new Vector2(u1, height));
                // Caps; a coarse cap fans out from its first rim corner, two triangles fewer.
                if (coarse)
                {
                    if (i > 0 && i < segments - 1)
                    {
                        Vertex(tool, top + new Vector3(topRadius, 0, 0), Vector3.Up, new Vector2(topRadius, 0));
                        Vertex(tool, t0, Vector3.Up, new Vector2(d0.X, d0.Z) * topRadius);
                        Vertex(tool, t1, Vector3.Up, new Vector2(d1.X, d1.Z) * topRadius);
                    }
                    continue;
                }
                Vertex(tool, top, Vector3.Up, new Vector2(0, 0));
                Vertex(tool, t0, Vector3.Up, new Vector2(d0.X, d0.Z) * topRadius);
                Vertex(tool, t1, Vector3.Up, new Vector2(d1.X, d1.Z) * topRadius);
                Vertex(tool, baseCentre, Vector3.Down, new Vector2(0, 0));
                Vertex(tool, b1, Vector3.Down, new Vector2(d1.X, d1.Z) * bottomRadius);
                Vertex(tool, b0, Vector3.Down, new Vector2(d0.X, d0.Z) * bottomRadius);
            }
        }

        /// <summary>
        /// Whether a part with these extents (sorted, smallest first) and bottom is a small detail
        /// that a coarse mesh leaves out: a narrow paper-thin strip, or a short thin part off the floor.
        /// </summary>
        private bool IsDetail(float[] extents, float bottom) =>
            (extents[0] < CoarseMinSheetThickness && extents[1] < CoarseMaxStripWidth)
            || (extents[1] < minThickness && extents[2] < CoarseDetailLength && bottom > 0.001f);

        public ArrayMesh Commit()
        {
            for (var i = 0; i < _boxes.Count; i++)
            {
                var index = i;
                EmitBox(_boxes[i].Part, _boxes[i].Min, _boxes[i].Max, (axis, upper) => FaceHidden(index, axis, upper));
            }
            var mesh = new ArrayMesh();
            foreach (var (part, tool) in _surfaces)
            {
                if (!coarse)
                    tool.GenerateTangents();
                tool.Commit(mesh);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, coarse ? CoarseMaterial() : Materials()[part]);
            }
            return mesh;
        }

        private SurfaceTool Surface(Part part)
        {
            _part = part;
            if (coarse)
                part = Part.Pine;
            if (!_surfaces.TryGetValue(part, out var tool))
            {
                tool = new SurfaceTool();
                tool.Begin(Mesh.PrimitiveType.Triangles);
                _surfaces[part] = tool;
            }
            return tool;
        }

        /// <summary>A quad from <paramref name="corner"/> spanning <paramref name="u"/> and <paramref name="v"/>, wound to face <paramref name="normal"/>.</summary>
        private void Face(SurfaceTool tool, Vector3 corner, Vector3 u, Vector3 v, Vector3 normal)
        {
            var (lu, lv) = (u.Length(), v.Length());
            var p00 = corner;
            var p10 = corner + u;
            var p11 = corner + u + v;
            var p01 = corner + v;
            // Godot treats clockwise triangles (seen from the front) as front faces.
            var flip = u.Cross(v).Dot(normal) > 0f;
            Vector3[] quad = flip ? [p00, p11, p10, p00, p01, p11] : [p00, p10, p11, p00, p11, p01];
            Vector2[] uvs = flip
                ? [new(0, lv), new(lu, 0), new(lu, lv), new(0, lv), new(0, 0), new(lu, 0)]
                : [new(0, lv), new(lu, lv), new(lu, 0), new(0, lv), new(lu, 0), new(0, 0)];
            for (var i = 0; i < 6; i++)
                Vertex(tool, quad[i], normal, uvs[i]);
        }

        private void Vertex(SurfaceTool tool, Vector3 position, Vector3 normal, Vector2 uv)
        {
            tool.SetNormal(normal);
            tool.SetUV(uv);
            if (coarse)
                tool.SetUV2(new Vector2((int)_part, 0f));
            tool.AddVertex(position);
        }
    }
}
