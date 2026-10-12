using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Builds one mesh per Furniture type from simple parts (boxes and cylinders) sized from the
/// core's Furniture specs, with CC0 PBR materials (see assets/textures/CREDITS.md). Mesh space:
/// origin at the centre of the footprint on its base, X along the width, Z along the depth with
/// the front facing +Z, Y up. Materials marked as tinted take their colour from the instance.
/// Each type also has a coarse mesh for the distance (level of detail): parts too thin to show
/// from far away are left out, round parts have fewer sides and materials skip their normal maps.
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

    // Coarse meshes leave out parts thinner than this (legs, handles, poles), in metres.
    private const float CoarseMinThickness = 0.06f;

    // Coarse meshes also leave out paper-thin parts (tape strips), in metres, and every downward
    // face: they are drawn only far below the Observer camera, which never sees an underside there.
    private const float CoarseMinSheetThickness = 0.01f;

    private static readonly Dictionary<(FurnitureType, bool Coarse), ArrayMesh> Cache = new();
    private static Dictionary<Part, ShaderMaterial>? _materials;
    private static Dictionary<Part, ShaderMaterial>? _coarseMaterials;

    /// <summary>How brightly every lamp shade glows, from 0 (off) to 1 (lit).</summary>
    public static void SetLampGlow(float level)
    {
        var energy = LampShadeGlow * Mathf.Max(level, 0f);
        Materials()[Part.LampShade].SetShaderParameter("emission_energy", energy);
        CoarseMaterials()[Part.LampShade].SetShaderParameter("emission_energy", energy);
    }

    /// <summary>The coarse mesh for a Furniture type, drawn in the distance; built once and shared.</summary>
    public static ArrayMesh CoarseFor(FurnitureType type) => Get(type, coarse: true);

    /// <summary>
    /// Upright rectangles in mesh space, four corners each, inside the solid, full-height parts of
    /// a Furniture type (wardrobe and fridge bodies, shelving back panels) that hide what stands
    /// behind them; used as occluders for occlusion culling. Flat panels rather than boxes keep the
    /// occluders cheap. Empty for Furniture that is open, low or small.
    /// </summary>
    public static Vector3[] OccluderQuads(FurnitureType type)
    {
        var spec = FurnitureCatalog.Spec(type);
        var (hw, hd, h) = (spec.Width / 2f, spec.Depth / 2f, spec.Height);
        Vector3[] Across(float z, float bottom) => [new(-hw, bottom, z), new(hw, bottom, z), new(hw, h, z), new(-hw, h, z)];
        Vector3[] Along(float x) => [new(x, 0f, -hd), new(x, 0f, hd), new(x, h, hd), new(x, h, -hd)];
        return type switch
        {
            // Solid bodies: two panels crossing at the centre.
            FurnitureType.Wardrobe or FurnitureType.Fridge or FurnitureType.FilingCabinet or FurnitureType.Dresser
                or FurnitureType.ShoeCabinet or FurnitureType.WashingMachine or FurnitureType.Stove => [.. Across(0f, 0f), .. Along(0f)],
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

    /// <summary>The mesh for a Furniture type; built once and shared.</summary>
    public static ArrayMesh For(FurnitureType type) => Get(type, coarse: false);

    private static ArrayMesh Get(FurnitureType type, bool coarse)
    {
        if (!Cache.TryGetValue((type, coarse), out var mesh))
            Cache[(type, coarse)] = mesh = Build(type, coarse);
        return mesh;
    }

    /// <summary>
    /// A colour for one piece: tinted materials vary per piece (fabric, plastic, leaves, cardboard),
    /// picked from a palette by the piece id so a piece keeps its colour. Visual only.
    /// </summary>
    public static Color TintFor(FurniturePiece piece)
    {
        var palette = piece.Type switch
        {
            FurnitureType.Sofa or FurnitureType.Armchair or FurnitureType.Rug or FurnitureType.Bed
                or FurnitureType.CushionBin or FurnitureType.BlanketStack or FurnitureType.CurtainRack
                or FurnitureType.OfficeChair or FurnitureType.Parasol => FabricColors,
            FurnitureType.BunkBed or FurnitureType.KidsTable or FurnitureType.KidsChair or FurnitureType.PlayCube or FurnitureType.ToyShelf => PlasticColors,
            FurnitureType.PottedPlant or FurnitureType.LargePlant or FurnitureType.PlanterBox => LeafColors,
            FurnitureType.FlatPackBox => CardboardColors,
            _ => null,
        };
        if (palette is null)
            return Colors.White;
        var hash = (uint)piece.Id * 2654435761u;
        return palette[(int)(hash >> 16) % palette.Length];
    }

    private static readonly Color[] FabricColors =
    [
        new(0.62f, 0.62f, 0.6f), new(0.78f, 0.72f, 0.6f), new(0.25f, 0.3f, 0.42f), new(0.72f, 0.56f, 0.24f),
        new(0.36f, 0.45f, 0.36f), new(0.62f, 0.36f, 0.28f), new(0.42f, 0.42f, 0.44f), new(0.85f, 0.83f, 0.78f),
    ];

    private static readonly Color[] PlasticColors =
    [
        new(0.85f, 0.25f, 0.2f), new(0.95f, 0.75f, 0.2f), new(0.25f, 0.55f, 0.85f), new(0.3f, 0.7f, 0.4f),
        new(0.9f, 0.5f, 0.7f), new(0.95f, 0.55f, 0.2f), new(0.95f, 0.95f, 0.92f),
    ];

    private static readonly Color[] LeafColors =
    [
        new(0.25f, 0.45f, 0.2f), new(0.32f, 0.5f, 0.22f), new(0.2f, 0.38f, 0.22f), new(0.4f, 0.52f, 0.25f),
    ];

    private static readonly Color[] CardboardColors =
    [
        new(1f, 1f, 1f), new(0.92f, 0.9f, 0.86f), new(0.85f, 0.8f, 0.72f), new(0.96f, 0.93f, 0.9f),
    ];

    private static ArrayMesh Build(FurnitureType type, bool coarse)
    {
        var spec = FurnitureCatalog.Spec(type);
        var (w, d, h) = (spec.Width, spec.Depth, spec.Height);
        var (hw, hd) = (w / 2f, d / 2f);
        // Wave-two legs and arms use 7 cm sections; omit them at distance just as the
        // narrower first-wave legs are omitted, retaining the main silhouette.
        var b = new Builder(coarse, type >= FurnitureType.Desk ? 0.09f : CoarseMinThickness, type >= FurnitureType.Desk ? 5 : 0);
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
        return b.Commit(coarse ? CoarseMaterials() : Materials());
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

    /// <summary>The materials of the coarse meshes: the same looks without normal maps.</summary>
    private static Dictionary<Part, ShaderMaterial> CoarseMaterials()
    {
        if (_coarseMaterials is not null)
            return _coarseMaterials;
        _coarseMaterials = Materials().ToDictionary(m => m.Key, m =>
        {
            var material = (ShaderMaterial)m.Value.Duplicate();
            material.SetShaderParameter("normal_enabled", false);
            return material;
        });
        return _coarseMaterials;
    }

    /// <summary>
    /// Accumulates parts into one surface per material, with UVs in metres and tangents for normal maps.
    /// A coarse builder leaves out thin parts and gives round parts fewer sides.
    /// </summary>
    private sealed class Builder(bool coarse, float minThickness, int coarseSegments)
    {
        private readonly Dictionary<Part, SurfaceTool> _surfaces = new();

        public void Box(Part part, Vector3 min, Vector3 max)
        {
            var size = max - min;
            float[] extents = [size.X, size.Y, size.Z];
            Array.Sort(extents);
            if (coarse && (extents[1] < minThickness || extents[0] < CoarseMinSheetThickness))
                return;
            var tool = Surface(part);
            // Each face: normal, the two in-face axes (scaled by size) and its corner.
            Face(tool, min with { Z = max.Z }, new Vector3(size.X, 0, 0), new Vector3(0, size.Y, 0), Vector3.Back);
            Face(tool, max with { Y = min.Y, Z = min.Z }, new Vector3(-size.X, 0, 0), new Vector3(0, size.Y, 0), Vector3.Forward);
            Face(tool, min, new Vector3(0, 0, size.Z), new Vector3(0, size.Y, 0), Vector3.Left);
            Face(tool, max with { Y = min.Y }, new Vector3(0, 0, -size.Z), new Vector3(0, size.Y, 0), Vector3.Right);
            Face(tool, min with { Y = max.Y, Z = max.Z }, new Vector3(size.X, 0, 0), new Vector3(0, 0, -size.Z), Vector3.Up);
            if (!coarse)
                Face(tool, min, new Vector3(size.X, 0, 0), new Vector3(0, 0, size.Z), Vector3.Down);
        }

        /// <summary>A capped cylinder (or cone frustum) standing on <paramref name="baseCentre"/>.</summary>
        public void Cylinder(Part part, Vector3 baseCentre, float bottomRadius, float topRadius, float height, int segments = 12)
        {
            if (coarse && 2f * Mathf.Max(bottomRadius, topRadius) < minThickness)
                return;
            if (coarse)
                segments = coarseSegments > 0 ? coarseSegments : Math.Max(5, segments / 2);
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
                // Caps.
                Vertex(tool, top, Vector3.Up, new Vector2(0, 0));
                Vertex(tool, t0, Vector3.Up, new Vector2(d0.X, d0.Z) * topRadius);
                Vertex(tool, t1, Vector3.Up, new Vector2(d1.X, d1.Z) * topRadius);
                if (coarse)
                    continue;
                Vertex(tool, baseCentre, Vector3.Down, new Vector2(0, 0));
                Vertex(tool, b1, Vector3.Down, new Vector2(d1.X, d1.Z) * bottomRadius);
                Vertex(tool, b0, Vector3.Down, new Vector2(d0.X, d0.Z) * bottomRadius);
            }
        }

        public ArrayMesh Commit(Dictionary<Part, ShaderMaterial> materials)
        {
            var mesh = new ArrayMesh();
            foreach (var (part, tool) in _surfaces)
            {
                tool.GenerateTangents();
                tool.Commit(mesh);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, materials[part]);
            }
            return mesh;
        }

        private SurfaceTool Surface(Part part)
        {
            if (!_surfaces.TryGetValue(part, out var tool))
            {
                tool = new SurfaceTool();
                tool.Begin(Mesh.PrimitiveType.Triangles);
                _surfaces[part] = tool;
            }
            return tool;
        }

        /// <summary>A quad from <paramref name="corner"/> spanning <paramref name="u"/> and <paramref name="v"/>, wound to face <paramref name="normal"/>.</summary>
        private static void Face(SurfaceTool tool, Vector3 corner, Vector3 u, Vector3 v, Vector3 normal)
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

        private static void Vertex(SurfaceTool tool, Vector3 position, Vector3 normal, Vector2 uv)
        {
            tool.SetNormal(normal);
            tool.SetUV(uv);
            tool.AddVertex(position);
        }
    }
}
