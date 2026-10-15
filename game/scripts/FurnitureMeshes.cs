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
/// The hero types (armchair, sofa, chair, toilet, vanity unit) are drawn from external models
/// instead (<see cref="FurnitureModels"/>), fitted to the same sizes in the same mesh space.
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
        Stone,
        Water,
        // The hands of the clock wall's clocks, turned in the shader (see furniture.gdshader's clock_hand).
        HourHand,
        MinuteHand,
        Food,
        // The rare Points of interest (ticket 08b): the Ball pit's balls, the EXIT sign's green panel and white
        // letters, the display aquarium's water and fish, the mannequins' bare plastic.
        Balls,
        SignPanel,
        SignLetters,
        AquariumWater,
        Fish,
        MannequinSkin,
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

    // Size of the part tables (part_albedo, part_surface, part_glow) in furniture.gdshader; at least the number of parts. Change both together.
    internal const int PartSlots = 40;

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
        if (FurnitureModels.Find(type, variant) is { } model)
            return coarse ? FurnitureModels.Coarse(model) : FurnitureModels.Full(model);
        if (!Cache.TryGetValue((type, variant, coarse), out var mesh))
            Cache[(type, variant, coarse)] = mesh = Build(type, variant, coarse);
        return mesh;
    }

    /// <summary>
    /// A colour for one piece: tinted materials take the piece's tint index (see
    /// <see cref="FurniturePalettes"/>) into a colour table of their material family (fabric,
    /// plastic, leaves, cardboard), so every piece of a Display shares its palette. Visual only.
    /// </summary>
    public static Color TintFor(FurniturePiece piece) => TintFor(piece.Type, piece.Tint);

    /// <summary>The colour of tint index <paramref name="tint"/> for a Furniture type; white if the type has no palette.</summary>
    public static Color TintFor(FurnitureType type, int tint)
    {
        var palette = Palette(type);
        return palette is null ? Colors.White : palette[Math.Clamp(tint, 0, palette.Length - 1)];
    }

    /// <summary>Whether pieces of this type take a colour from a palette (their tinted materials vary).</summary>
    public static bool IsTinted(FurnitureType type) => Palette(type) is not null;

    /// <summary>The name of the part with this index: a full mesh's surface name, a coarse mesh's UV2.x.</summary>
    public static string PartName(int part) => ((Part)part).ToString();

    /// <summary>
    /// The part a full mesh's surface draws, as its coarse mesh marks it in UV2.x: a procedural
    /// part's index, or a model's surface index (each model has its own coarse part table).
    /// </summary>
    public static int PartIndex(FurnitureType type, int variant, string surfaceName)
    {
        if (FurnitureModels.Find(type, variant) is not null)
        {
            var full = For(type, variant);
            for (var s = 0; s < full.GetSurfaceCount(); s++)
                if (full.SurfaceGetName(s) == surfaceName)
                    return s;
        }
        else if (Enum.TryParse<Part>(surfaceName, out var part))
            return (int)part;
        throw new ArgumentException($"Unknown part {surfaceName} of {type} {variant}.");
    }

    private static Color[]? Palette(FurnitureType type)
    {
        return type switch
        {
            FurnitureType.Sofa or FurnitureType.Armchair or FurnitureType.Rug or FurnitureType.Bed
                or FurnitureType.CushionBin or FurnitureType.BlanketStack or FurnitureType.CurtainRack
                or FurnitureType.OfficeChair or FurnitureType.Parasol or FurnitureType.FrameDisplay or FurnitureType.Mattress => FabricColors,
            FurnitureType.BunkBed or FurnitureType.BouncyCastle or FurnitureType.KidsTable or FurnitureType.KidsChair or FurnitureType.PlayCube or FurnitureType.ToyShelf => PlasticColors,
            FurnitureType.PottedPlant or FurnitureType.LargePlant or FurnitureType.PlanterBox => LeafColors,
            FurnitureType.FlatPackBox or FurnitureType.CardboardBox => CardboardColors,
            _ => null,
        };
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

    private static ArrayMesh Build(FurnitureType type, int variant, bool coarse) => Describe(type, variant, coarse).Commit();

    /// <summary>
    /// Every pair of visible box faces of a Furniture type's full mesh that lie in one plane, face
    /// the same way and overlap, so they z-fight: one line per pair, empty when there are none.
    /// </summary>
    public static IEnumerable<string> CoplanarFaces(FurnitureType type, int variant = 0) =>
        Describe(type, variant, coarse: false).CoplanarFaces().Select(f => $"{type} {variant}: {f}");

    private static Builder Describe(FurnitureType type, int variant, bool coarse)
    {
        var spec = FurnitureCatalog.Spec(type, variant);
        var (w, d, h) = (spec.Width, spec.Depth, spec.Height);
        var (hw, hd) = (w / 2f, d / 2f);
        // Wave-two legs and arms use 7 cm sections; they count as thin like the narrower first-wave legs.
        var b = new Builder(coarse, type >= FurnitureType.Desk ? 0.09f : CoarseMinThickness, type >= FurnitureType.Desk ? 5 : 0);
        if (type is FurnitureType.WallClock or FurnitureType.FountainBowl or FurnitureType.FountainColumn or FurnitureType.BasinKerb)
        {
            BuildStoneAndClocks(b, type, variant, w, d, h);
            return b;
        }
        if (variant > 0)
        {
            BuildVariant(b, type, variant, w, d, h);
            return b;
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
            case FurnitureType.CardboardBox:
                // An empty, taped-up moving box: Landmark Furniture (the box fort).
                b.Box(Part.Cardboard, new Vector3(-hw, 0f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.LightSteel, new Vector3(-0.05f, h, -hd), new Vector3(0.05f, h + 0.003f, hd));
                break;
            case FurnitureType.Mattress:
                // A bare mattress with a coloured border: Landmark Furniture (the mattress mountain).
                b.Box(Part.Fabric, new Vector3(-hw, 0f, -hd), new Vector3(hw, h - 0.02f, hd));
                b.Box(Part.Bedding, new Vector3(-hw + 0.04f, h - 0.02f, -hd + 0.04f), new Vector3(hw - 0.04f, h, hd - 0.04f));
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
                b.Box(Part.Pine, new(-hw, 0.1f, -hd), new(hw, 0.14f, hd));
                b.Box(Part.Pine, new(-hw, h - 0.04f, -hd), new(hw, h, hd));
                // A shelf behind the glass stops at its back face, or the two fronts would z-fight.
                b.Box(Part.Pine, new(-hw, h * 0.5f, -hd), new(hw, h * 0.5f + 0.04f, type == FurnitureType.DisplayCabinet ? hd - 0.02f : hd));
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
                // Control panel; deep enough not to count as a paper-thin strip, so coarse meshes keep its colour.
                b.Box(Part.PaintedMetal, new(-hw + 0.02f, h - 0.14f, hd), new(hw - 0.02f, h - 0.02f, hd + 0.012f));
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
            case FurnitureType.ShoppingCart:
            {
                // A wire basket on a wheeled chassis; the handle is at the back (-z).
                var zBack = -hd + 0.14f;
                foreach (var x in new[] { -hw + 0.04f, hw - 0.04f })
                    foreach (var z in new[] { -hd + 0.1f, hd - 0.1f })
                        b.Box(Part.Rubber, new Vector3(x - 0.02f, 0f, z - 0.045f), new Vector3(x + 0.02f, 0.1f, z + 0.045f));
                b.Box(Part.LightSteel, new Vector3(-hw + 0.02f, 0.1f, -hd + 0.06f), new Vector3(-hw + 0.06f, 0.14f, hd - 0.06f));
                b.Box(Part.LightSteel, new Vector3(hw - 0.06f, 0.1f, -hd + 0.06f), new Vector3(hw - 0.02f, 0.14f, hd - 0.06f));
                b.Box(Part.LightSteel, new Vector3(-hw + 0.06f, 0.2f, zBack), new Vector3(hw - 0.06f, 0.215f, hd - 0.06f));
                b.Box(Part.LightSteel, new Vector3(-hw, 0.42f, zBack), new Vector3(hw, 0.44f, hd));
                foreach (var x in new[] { -hw, hw - 0.03f })
                    foreach (var z in new[] { zBack, hd - 0.03f })
                        b.Box(Part.LightSteel, new Vector3(x, 0.44f, z), new Vector3(x + 0.03f, 0.9f, z + 0.03f));
                b.Box(Part.LightSteel, new Vector3(-hw, 0.9f, zBack), new Vector3(hw, 0.93f, zBack + 0.03f));
                b.Box(Part.LightSteel, new Vector3(-hw, 0.9f, hd - 0.03f), new Vector3(hw, 0.93f, hd));
                b.Box(Part.LightSteel, new Vector3(-hw, 0.9f, zBack + 0.03f), new Vector3(-hw + 0.03f, 0.93f, hd - 0.03f));
                b.Box(Part.LightSteel, new Vector3(hw - 0.03f, 0.9f, zBack + 0.03f), new Vector3(hw, 0.93f, hd - 0.03f));
                b.Box(Part.LightSteel, new Vector3(-hw, 0.64f, zBack + 0.03f), new Vector3(-hw + 0.015f, 0.66f, hd - 0.03f));
                b.Box(Part.LightSteel, new Vector3(hw - 0.015f, 0.64f, zBack + 0.03f), new Vector3(hw, 0.66f, hd - 0.03f));
                b.Box(Part.LightSteel, new Vector3(-hw + 0.03f, 0.64f, hd - 0.015f), new Vector3(hw - 0.03f, 0.66f, hd));
                b.Box(Part.LightSteel, new Vector3(-hw + 0.03f, 0.64f, zBack), new Vector3(hw - 0.03f, 0.66f, zBack + 0.015f));
                foreach (var x in new[] { -hw + 0.03f, hw - 0.06f })
                    b.Box(Part.DarkSteel, new Vector3(x, 0.14f, -hd + 0.03f), new Vector3(x + 0.03f, 0.97f, -hd + 0.06f));
                b.Box(Part.Rubber, new Vector3(-hw + 0.03f, 0.97f, -hd + 0.01f), new Vector3(hw - 0.03f, 1f, -hd + 0.08f));
                break;
            }
            case FurnitureType.MealPlate:
                // A plate of food: store-style meatballs, hot dogs and buns, as a mound.
                b.Cylinder(Part.Ceramic, Vector3.Zero, hw * 0.8f, hw, 0.02f, 16);
                b.Cylinder(Part.Food, new Vector3(0f, 0.02f, 0f), hw * 0.6f, hw * 0.4f, 0.03f, 12);
                break;
            case FurnitureType.BouncyCastle:
                BuildBouncyCastle(b, hw, hd);
                break;
            case FurnitureType.ExitSign:
            {
                // A glowing green sign on two posts: an arrow and EXIT in white on both faces.
                foreach (var x in new[] { -hw + 0.1f, hw - 0.14f })
                {
                    b.Box(Part.DarkSteel, new Vector3(x, 0.03f, -0.02f), new Vector3(x + 0.04f, h - 0.4f, 0.02f));
                    b.Box(Part.DarkSteel, new Vector3(x - 0.03f, 0f, -hd), new Vector3(x + 0.07f, 0.03f, hd));
                }
                b.Box(Part.DarkSteel, new Vector3(-hw, h - 0.4f, -0.07f), new Vector3(hw, h, 0.07f));
                b.Box(Part.SignPanel, new Vector3(-hw + 0.02f, h - 0.38f, 0.07f), new Vector3(hw - 0.02f, h - 0.02f, 0.08f));
                b.Box(Part.SignPanel, new Vector3(-hw + 0.02f, h - 0.38f, -0.08f), new Vector3(hw - 0.02f, h - 0.02f, -0.07f));
                SignLetters(b, h - 0.35f, 0.08f, 1f);
                SignLetters(b, h - 0.35f, -0.08f, -1f);
                break;
            }
            case FurnitureType.Mannequin:
                // A tall, faceless figure in a dark suit (the matte black rubber) on a chrome stand: from afar it passes for the Staff.
                b.Cylinder(Part.Chrome, Vector3.Zero, hd - 0.01f, hd - 0.01f, 0.02f, 16);
                b.Cylinder(Part.Chrome, new Vector3(0f, 0.02f, -0.09f), 0.015f, 0.015f, 0.9f, 6);
                foreach (var x in new[] { -0.13f, 0.03f })
                    b.Box(Part.Rubber, new Vector3(x, 0.02f, -0.07f), new Vector3(x + 0.1f, 0.98f, 0.07f));
                b.Box(Part.Rubber, new Vector3(-0.17f, 0.98f, -0.1f), new Vector3(0.17f, 1.58f, 0.1f));
                foreach (var x in new[] { -hw, hw - 0.07f })
                {
                    b.Box(Part.Rubber, new Vector3(x, 0.98f, -0.05f), new Vector3(x + 0.07f, 1.54f, 0.05f));
                    b.Box(Part.MannequinSkin, new Vector3(x + 0.005f, 0.8f, -0.04f), new Vector3(x + 0.065f, 0.98f, 0.04f));
                }
                b.Box(Part.Rubber, new Vector3(-hw + 0.07f, 1.42f, -0.1f), new Vector3(-0.17f, 1.58f, 0.1f));
                b.Box(Part.Rubber, new Vector3(0.17f, 1.42f, -0.1f), new Vector3(hw - 0.07f, 1.58f, 0.1f));
                b.Cylinder(Part.MannequinSkin, new Vector3(0f, 1.58f, 0f), 0.05f, 0.045f, 0.1f, 8);
                b.Cylinder(Part.MannequinSkin, new Vector3(0f, 1.68f, 0f), 0.085f, 0.1f, h - 1.68f, 12);
                break;
            case FurnitureType.DoorFrame:
                // A door frame standing alone, with nothing but the aisle behind it.
                b.Box(Part.Ceramic, new Vector3(-hw, 0f, -0.08f), new Vector3(-hw + 0.1f, h, 0.08f));
                b.Box(Part.Ceramic, new Vector3(hw - 0.1f, 0f, -0.08f), new Vector3(hw, h, 0.08f));
                b.Box(Part.Ceramic, new Vector3(-hw + 0.1f, h - 0.1f, -0.08f), new Vector3(hw - 0.1f, h, 0.08f));
                b.Box(Part.Oak, new Vector3(-hw + 0.1f, 0f, -0.08f), new Vector3(hw - 0.1f, 0.02f, 0.08f));
                // Feet that keep it standing.
                foreach (var x in new[] { -hw, hw - 0.1f })
                    b.Box(Part.PaintedMetal, new Vector3(x, 0f, -hd), new Vector3(x + 0.1f, 0.04f, hd));
                break;
            case FurnitureType.Aquarium:
            {
                // A long tank of glowing water on a dark cabinet, with fish against the front and back glass.
                b.Box(Part.Walnut, new Vector3(-hw, 0f, -hd), new Vector3(hw, 0.8f, hd));
                b.Box(Part.AquariumWater, new Vector3(-hw + 0.05f, 0.8f, -hd + 0.05f), new Vector3(hw - 0.05f, h - 0.2f, hd - 0.05f));
                foreach (var x in new[] { -hw, hw - 0.05f })
                    foreach (var z in new[] { -hd, hd - 0.05f })
                        b.Box(Part.DarkSteel, new Vector3(x, 0.8f, z), new Vector3(x + 0.05f, h - 0.15f, z + 0.05f));
                b.Box(Part.DarkSteel, new Vector3(-hw, h - 0.15f, -hd), new Vector3(hw, h, hd));
                b.Box(Part.Stone, new Vector3(-hw + 0.05f, 0.8f, hd - 0.05f), new Vector3(hw - 0.05f, 0.95f, hd - 0.04f));
                for (var i = 0; i < 18; i++)
                {
                    var x = -hw + 0.3f + (w - 0.6f) * ((i * 0.618034f) % 1f);
                    var y = 1.05f + (h - 1.45f) * ((i * 0.7548777f + 0.3f) % 1f);
                    var size = 1.1f + 0.5f * ((i * 0.4142f) % 1f);
                    var facing = i % 2 == 0 ? 1f : -1f;
                    var z = i % 3 == 2 ? -hd + 0.03f : hd - 0.05f;
                    // Body and tail, the tail behind the body.
                    b.Box(Part.Fish, new Vector3(x - 0.09f * size, y - 0.04f * size, z), new Vector3(x + 0.09f * size, y + 0.04f * size, z + 0.02f));
                    var tail = x - facing * 0.09f * size;
                    b.Box(Part.Fish, new Vector3(Mathf.Min(tail, tail - facing * 0.05f * size), y - 0.05f * size, z),
                        new Vector3(Mathf.Max(tail, tail - facing * 0.05f * size), y + 0.05f * size, z + 0.02f));
                }
                break;
            }
            default:
                b.Box(Part.Maple, new Vector3(-hw, 0f, -hd), new Vector3(hw, h, hd));
                break;
        }
        return b;
    }

    /// <summary>
    /// The parts of a variant (1 or 2) of the types that have several; each has its own silhouette,
    /// sized from the variant's spec.
    /// </summary>
    /// <summary>Landmark Furniture of ticket 08: the clock wall's clocks and the fountain court's stonework.</summary>
    private static void BuildStoneAndClocks(Builder b, FurnitureType type, int variant, float w, float d, float h)
    {
        var (hw, hd) = (w / 2f, d / 2f);
        switch (type)
        {
            case FurnitureType.WallClock:
            {
                // A wooden case, round (stepped) or square, a white dial and two hands turning about its centre.
                var mid = h / 2f;
                var round = variant != 3;
                var frame = variant == 1 ? Part.Oak : variant == 2 ? Part.DarkSteel : Part.Walnut;
                (float X, float Y)[] shapes = round ? [(hw, hw * 0.58f), (hw * 0.58f, hw), (hw * 0.86f, hw * 0.86f)] : [(hw, hw)];
                foreach (var (x, y) in shapes)
                    b.Box(frame, new Vector3(-x, mid - y, -hd), new Vector3(x, mid + y, hd - 0.012f));
                // The dial is the same stepped outline cut into slabs that do not overlap: overlapping dial boxes share
                // their faces' planes and z-fight.
                var steps = shapes.OrderBy(s => s.Y).ToArray();
                for (var i = 0; i < steps.Length; i++)
                {
                    var (x, y) = (steps[i].X * 0.88f, steps[i].Y * 0.88f);
                    var below = i == 0 ? 0f : steps[i - 1].Y * 0.88f;
                    foreach (var sign in i == 0 ? new[] { 1f } : new[] { -1f, 1f })
                    {
                        var (y0, y1) = i == 0 ? (mid - y, mid + y) : sign > 0 ? (mid + below, mid + y) : (mid - y, mid - below);
                        b.Box(Part.Ceramic, new Vector3(-x, y0, hd - 0.012f), new Vector3(x, y1, hd - 0.007f));
                    }
                }
                b.Pivot = mid;
                b.Box(Part.HourHand, new Vector3(-hw * 0.035f, mid - hw * 0.06f, hd - 0.007f), new Vector3(hw * 0.035f, mid + hw * 0.48f, hd - 0.004f));
                b.Box(Part.MinuteHand, new Vector3(-hw * 0.022f, mid - hw * 0.08f, hd - 0.004f), new Vector3(hw * 0.022f, mid + hw * 0.76f, hd));
                break;
            }
            case FurnitureType.FountainBowl:
                // A wide stone bowl on a short foot, brimming with water.
                b.Cylinder(Part.Stone, Vector3.Zero, hw * 0.3f, hw * 0.34f, h * 0.3f, 16);
                b.Cylinder(Part.Stone, new Vector3(0f, h * 0.3f, 0f), hw * 0.4f, hw, h * 0.66f, 24);
                b.Cylinder(Part.Water, new Vector3(0f, h * 0.96f, 0f), hw * 0.9f, hw * 0.9f, h * 0.035f, 24);
                break;
            case FurnitureType.FountainColumn:
                // A stone column with a base and a capital; the slender one tapers to a finial.
                if (variant == 1)
                {
                    b.Cylinder(Part.Stone, Vector3.Zero, hw, hw * 0.6f, h * 0.15f, 12);
                    b.Cylinder(Part.Stone, new Vector3(0f, h * 0.15f, 0f), hw * 0.45f, hw * 0.2f, h * 0.85f, 12);
                    break;
                }
                b.Cylinder(Part.Stone, Vector3.Zero, hw, hw, h * 0.08f, 16);
                b.Cylinder(Part.Stone, new Vector3(0f, h * 0.08f, 0f), hw * 0.62f, hw * 0.55f, h * 0.82f, 16);
                b.Cylinder(Part.Stone, new Vector3(0f, h * 0.9f, 0f), hw * 0.6f, hw, h * 0.1f, 16);
                break;
            case FurnitureType.BasinKerb:
                // A block of stone kerb with a wider coping on top.
                b.Box(Part.Stone, new Vector3(-hw, 0f, -hd + 0.04f), new Vector3(hw, h - 0.1f, hd - 0.04f));
                b.Box(Part.Stone, new Vector3(-hw, h - 0.1f, -hd), new Vector3(hw, h, hd));
                break;
        }
    }

    /// <summary>
    /// The Ball pit: an inflated floor, walls and corner towers in the piece's plastic colour with red tower tops, net
    /// walls up to an open rim and a layer of balls; open at the front, and from above, so the balls show.
    /// </summary>
    private static void BuildBouncyCastle(Builder b, float hw, float hd)
    {
        const float Floor = 0.45f, Wall = 0.35f, WallTop = 1.2f, FrontTop = 0.9f, Canopy = 2.1f, Door = 0.8f;
        b.Box(Part.Plastic, new Vector3(-hw, 0f, -hd), new Vector3(hw, Floor, hd));
        foreach (var x in new[] { -hw + 0.4f, hw - 0.4f })
            foreach (var z in new[] { -hd + 0.4f, hd - 0.4f })
            {
                b.Cylinder(Part.Plastic, new Vector3(x, Floor, z), 0.4f, 0.4f, 2.3f - Floor);
                b.Cylinder(Part.Balls, new Vector3(x, 2.3f, z), 0.42f, 0.04f, 0.3f);
            }
        b.Box(Part.Plastic, new Vector3(-hw + 0.4f, Floor, -hd), new Vector3(hw - 0.4f, WallTop, -hd + Wall));
        b.Box(Part.Plastic, new Vector3(-hw, Floor, -hd + 0.4f), new Vector3(-hw + Wall, WallTop, hd - 0.4f));
        b.Box(Part.Plastic, new Vector3(hw - Wall, Floor, -hd + 0.4f), new Vector3(hw, WallTop, hd - 0.4f));
        b.Box(Part.Plastic, new Vector3(-hw + 0.4f, Floor, hd - Wall), new Vector3(-Door, FrontTop, hd));
        b.Box(Part.Plastic, new Vector3(Door, Floor, hd - Wall), new Vector3(hw - 0.4f, FrontTop, hd));
        b.Box(Part.Plastic, new Vector3(-hw + 0.4f, Canopy, -hd), new Vector3(hw - 0.4f, Canopy + 0.15f, -hd + Wall));
        b.Box(Part.Plastic, new Vector3(-hw + 0.4f, Canopy, hd - Wall), new Vector3(hw - 0.4f, Canopy + 0.15f, hd));
        b.Box(Part.Plastic, new Vector3(-hw, Canopy, -hd + 0.4f), new Vector3(-hw + Wall, Canopy + 0.15f, hd - 0.4f));
        b.Box(Part.Plastic, new Vector3(hw - Wall, Canopy, -hd + 0.4f), new Vector3(hw, Canopy + 0.15f, hd - 0.4f));
        // The net: strands along the middle of each wall, from its top to the canopy; none across the doorway.
        void Net(float from, float to, float bottom, bool alongX, float at)
        {
            for (var t = from; t <= to + 0.01f; t += 0.2f)
                b.Box(Part.Rubber, alongX ? new Vector3(t - 0.012f, bottom, at - 0.015f) : new Vector3(at - 0.015f, bottom, t - 0.012f),
                    alongX ? new Vector3(t + 0.012f, Canopy, at + 0.015f) : new Vector3(at + 0.015f, Canopy, t + 0.012f));
            for (var y = bottom + 0.2f; y < Canopy - 0.05f; y += 0.2f)
                b.Box(Part.Rubber, alongX ? new Vector3(from, y - 0.01f, at - 0.01f) : new Vector3(at - 0.01f, y - 0.01f, from),
                    alongX ? new Vector3(to, y + 0.01f, at + 0.01f) : new Vector3(at + 0.01f, y + 0.01f, to));
        }
        Net(-hw + 0.8f, hw - 0.8f, WallTop, true, -hd + Wall / 2f);
        Net(-hd + 0.8f, hd - 0.8f, WallTop, false, -hw + Wall / 2f);
        Net(-hd + 0.8f, hd - 0.8f, WallTop, false, hw - Wall / 2f);
        Net(-hw + 0.8f, -Door, FrontTop, true, hd - Wall / 2f);
        Net(Door, hw - 0.8f, FrontTop, true, hd - Wall / 2f);
        b.Box(Part.Balls, new Vector3(-hw + Wall, Floor, -hd + Wall), new Vector3(hw - Wall, 0.75f, hd - Wall));
        // Loose balls on top, in other colours; each sits a hair higher than the last, so none share a top face.
        Part[] colours = [Part.RackUpright, Part.RackBeam, Part.Ceramic, Part.Fish];
        for (var i = 0; i < 40; i++)
        {
            var x = -hw + 0.45f + (2f * hw - 0.9f) * ((i * 0.618034f) % 1f);
            var z = -hd + 0.45f + (2f * hd - 0.9f) * ((i * 0.7548777f + 0.3f) % 1f);
            b.Box(colours[i % colours.Length], new Vector3(x - 0.055f, 0.7f, z - 0.055f), new Vector3(x + 0.055f, 0.8f + 0.002f * i, z + 0.055f));
        }
    }

    // The EXIT sign's face, five cells high: an arrow pointing left (to nowhere), then E, X, I, T.
    private static readonly string[] SignRows =
    [
        "..#....###.#.#.###.###",
        ".#.....#...#.#..#...#.",
        "#####..##...#...#...#.",
        ".#.....#...#.#..#...#.",
        "..#....###.#.#.###..#.",
    ];

    /// <summary>
    /// The EXIT sign's white letters on one face, from <paramref name="bottom"/> up, standing out from the panel face at
    /// <paramref name="z"/> towards <paramref name="side"/> (+1 the front, -1 the back, mirrored so it reads from there).
    /// </summary>
    private static void SignLetters(Builder b, float bottom, float z, float side)
    {
        const float Cell = 0.055f;
        var left = -SignRows[0].Length * Cell / 2f;
        for (var row = 0; row < SignRows.Length; row++)
        {
            var line = SignRows[row];
            var y = bottom + (SignRows.Length - 1 - row) * Cell;
            for (var start = 0; start < line.Length; start++)
            {
                if (line[start] != '#')
                    continue;
                var end = start;
                while (end + 1 < line.Length && line[end + 1] == '#')
                    end++;
                var (x0, x1) = (side * (left + start * Cell), side * (left + (end + 1) * Cell));
                b.Box(Part.SignLetters, new Vector3(Mathf.Min(x0, x1), y, Mathf.Min(z, z + side * 0.006f)),
                    new Vector3(Mathf.Max(x0, x1), y + Cell, Mathf.Max(z, z + side * 0.006f)));
                start = end;
            }
        }
    }

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
                // The coarse mesh keeps the fronts' wood as one panel, so the front keeps its colour.
                if (b.Coarse)
                    b.Box(Part.Pine, new Vector3(-hw + 0.01f, 0.11f, hd - 0.02f), new Vector3(hw - 0.01f, h - 0.05f, hd));
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
                b.Box(Part.Pine, new(-hw, 0.1f, -hd), new(hw, 0.14f, hd));
                b.Box(Part.Pine, new(-hw, h - 0.04f, -hd), new(hw, h, hd));
                b.Box(Part.Pine, new(-hw, h * 0.5f, -hd), new(hw, h * 0.5f + 0.04f, hd - 0.02f));
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
                b.Box(Part.Walnut, new(-hw, 0.1f, -hd), new(hw, 0.14f, hd));
                b.Box(Part.Walnut, new(-hw, h - 0.04f, -hd), new(hw, h, hd));
                foreach (var y in new[] { 0.6f, 1.05f, 1.45f })
                    b.Box(Part.Walnut, new(-hw, y, -hd), new(hw, y + 0.04f, hd - 0.02f));
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
    /// <summary>Dev aid: draws every coarse mesh magenta, to show which level of detail is drawn.</summary>
    public static void ShowCoarseInMagenta(bool enabled)
    {
        CoarseMaterial().SetShaderParameter("lod_debug", enabled);
        FurnitureModels.ShowCoarseInMagenta(enabled);
    }

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
        // Glows the same day and night, unlike a lamp shade.
        ShaderMaterial Steady(Color color, float roughness, Color glow, float energy)
        {
            var material = Plain(color, roughness);
            material.SetShaderParameter("emission_color", glow);
            material.SetShaderParameter("emission_energy", energy);
            material.SetShaderParameter("lamp_glow", false);
            return material;
        }
        ShaderMaterial Hand(int hand)
        {
            var material = Plain(new Color(0.06f, 0.06f, 0.07f), 0.5f, 0.4f);
            material.SetShaderParameter("clock_hand", hand);
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
            [Part.Stone] = Plain(new Color(0.8f, 0.78f, 0.72f), 0.75f),
            [Part.Water] = Plain(new Color(0.16f, 0.3f, 0.33f), 0.04f),
            [Part.HourHand] = Hand(1),
            [Part.MinuteHand] = Hand(2),
            [Part.Food] = Plain(new Color(0.62f, 0.36f, 0.16f), 0.65f),
            [Part.Balls] = Plain(new Color(0.85f, 0.16f, 0.14f), 0.4f),
            [Part.SignPanel] = Steady(new Color(0.05f, 0.55f, 0.25f), 0.4f, new Color(0.1f, 0.85f, 0.35f), 2.5f),
            [Part.SignLetters] = Steady(new Color(0.95f, 0.97f, 0.95f), 0.4f, new Color(0.9f, 1f, 0.92f), 3f),
            [Part.AquariumWater] = Steady(new Color(0.06f, 0.24f, 0.3f), 0.05f, new Color(0.15f, 0.5f, 0.65f), 0.6f),
            [Part.Fish] = Plain(new Color(1f, 0.5f, 0.1f), 0.45f),
            [Part.MannequinSkin] = Plain(new Color(0.88f, 0.86f, 0.82f), 0.35f),
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
        // Vector4 rather than Color: Godot converts a Color array from sRGB to linear on its own,
        // which made every coarse part far too dark and too saturated (ticket 06f).
        var albedo = new Vector4[PartSlots];
        var surface = new Vector2[PartSlots];
        var glow = new Vector3[PartSlots];
        foreach (var (part, material) in Materials())
        {
            // Every glowing part but the lamp shade (see emissive_part) glows steadily.
            if (part != Part.LampShade && (float)material.GetShaderParameter("emission_energy") > 0f)
            {
                var emission = ((Color)material.GetShaderParameter("emission_color")).SrgbToLinear() * (float)material.GetShaderParameter("emission_energy");
                glow[(int)part] = new Vector3(emission.R, emission.G, emission.B);
            }
            // Linear, as the full material's shader sees it: its albedo_color and textures are sRGB.
            var linear = ((Color)material.GetShaderParameter("albedo_color")).SrgbToLinear();
            var roughness = (float)material.GetShaderParameter("roughness");
            if ((bool)material.GetShaderParameter("textured"))
            {
                linear *= Average((Texture2D)material.GetShaderParameter("albedo_texture"), srgb: true);
                roughness *= Average((Texture2D)material.GetShaderParameter("roughness_texture"), srgb: false).R;
            }
            albedo[(int)part] = new Vector4(linear.R, linear.G, linear.B, (bool)material.GetShaderParameter("tinted") ? 1f : 0f);
            surface[(int)part] = new Vector2(roughness, (float)material.GetShaderParameter("metallic"));
        }
        var shade = Materials()[Part.LampShade];
        _coarseMaterial = new ShaderMaterial { Shader = shade.Shader };
        _coarseMaterial.SetShaderParameter("coarse_parts", true);
        _coarseMaterial.SetShaderParameter("part_albedo", albedo);
        _coarseMaterial.SetShaderParameter("part_surface", surface);
        _coarseMaterial.SetShaderParameter("part_glow", glow);
        _coarseMaterial.SetShaderParameter("emissive_part", (int)Part.LampShade);
        _coarseMaterial.SetShaderParameter("emission_color", shade.GetShaderParameter("emission_color"));
        _coarseMaterial.SetShaderParameter("emission_energy", shade.GetShaderParameter("emission_energy"));
        return _coarseMaterial;
    }

    /// <summary>
    /// The average colour of a texture, sampled on a sparse grid. For an <paramref name="srgb"/>
    /// texture it is the linear average, as the texture's smallest mip level shows it.
    /// </summary>
    private static Color Average(Texture2D texture, bool srgb)
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
                sum += srgb ? image.GetPixel(x, y).SrgbToLinear() : image.GetPixel(x, y);
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

        /// <summary>Height of the dial centre the clock hands turn about; full meshes carry it in the hands' UV2.y.</summary>
        public float Pivot { get; set; }
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

        /// <summary>
        /// Visible faces of two boxes in one plane, facing the same way and overlapping, that are
        /// left after <see cref="SeparateCoplanarFaces"/> (see <see cref="CoplanarFaces(FurnitureType, int)"/>).
        /// </summary>
        public IEnumerable<string> CoplanarFaces()
        {
            SeparateCoplanarFaces();
            string[] sides = ["-X", "-Y", "-Z", "+X", "+Y", "+Z"];
            foreach (var (i, j, axis, upper, _, area) in CoplanarPairs())
            {
                var (a, b) = (_boxes[i], _boxes[j]);
                yield return $"box {i} {a.Part} {a.Min}-{a.Max} and box {j} {b.Part} {b.Min}-{b.Max} on {sides[axis + (upper ? 3 : 0)]}, {area * 1e4f:0.#} cm2";
            }
        }

        /// <summary>
        /// Two boxes that overlap with a face in one plane would z-fight wherever those faces overlap
        /// (a shelf's end flush with a corner post, a sofa back flush with an arm). In each such pair
        /// the smaller face moves 2 mm into its box, so the larger face shows there; the step this
        /// leaves is far too small to see. Repeats until no pair is left, as one move can line up two
        /// faces that were both flush with a third.
        /// </summary>
        private void SeparateCoplanarFaces()
        {
            const float Inset = 0.002f;
            for (var pass = 0; pass < 4; pass++)
            {
                var pairs = CoplanarPairs().ToList();
                if (pairs.Count == 0)
                    return;
                foreach (var (i, j, axis, upper, plane, _) in pairs)
                {
                    var k = FaceArea(_boxes[i], axis) < FaceArea(_boxes[j], axis) ? i : j;
                    var (part, min, max) = _boxes[k];
                    // From the shared plane, so a face in several pairs moves only once.
                    var inset = Mathf.Min(Inset, (max[axis] - min[axis]) / 4f);
                    if (upper)
                        max[axis] = Mathf.Min(max[axis], plane - inset);
                    else
                        min[axis] = Mathf.Max(min[axis], plane + inset);
                    _boxes[k] = (part, min, max);
                }
            }
        }

        private static float FaceArea((Part Part, Vector3 Min, Vector3 Max) box, int axis)
        {
            var size = box.Max - box.Min;
            return axis switch { 0 => size.Y * size.Z, 1 => size.X * size.Z, _ => size.X * size.Y };
        }

        private IEnumerable<(int I, int J, int Axis, bool Upper, float Plane, float Area)> CoplanarPairs()
        {
            const float Tolerance = 1e-3f;
            for (var i = 0; i < _boxes.Count; i++)
                for (var j = i + 1; j < _boxes.Count; j++)
                    for (var axis = 0; axis < 3; axis++)
                        foreach (var upper in new[] { false, true })
                        {
                            var (a, b) = (_boxes[i], _boxes[j]);
                            var planeA = upper ? a.Max[axis] : a.Min[axis];
                            var planeB = upper ? b.Max[axis] : b.Min[axis];
                            if (Mathf.Abs(planeA - planeB) > Tolerance || FaceHidden(i, axis, upper) || FaceHidden(j, axis, upper))
                                continue;
                            var area = 1f;
                            for (var other = 0; other < 3; other++)
                                if (other != axis)
                                    area *= Mathf.Max(0f, Mathf.Min(a.Max[other], b.Max[other]) - Mathf.Max(a.Min[other], b.Min[other]));
                            if (area > 1e-6f)
                                yield return (i, j, axis, upper, planeA, area);
                        }
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
            SeparateCoplanarFaces();
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
                if (!coarse)
                    mesh.SurfaceSetName(mesh.GetSurfaceCount() - 1, part.ToString());
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
            else if (_part is Part.HourHand or Part.MinuteHand)
                tool.SetUV2(new Vector2(0f, Pivot));
            tool.AddVertex(position);
        }
    }
}
