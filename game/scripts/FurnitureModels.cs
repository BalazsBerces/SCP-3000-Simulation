using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The hero Furniture types drawn from external, realistic glTF models (ticket 08c) instead of
/// <see cref="FurnitureMeshes"/>' procedural parts: armchair, sofa, chair, toilet and vanity unit.
/// Presentation only: the core keeps its sizes and each model is fitted (scaled per axis) to its
/// Furniture type's footprint and height, in the same mesh space as the procedural meshes (origin at
/// the centre of the footprint on its base, front facing +Z, Y up). Every material becomes a
/// furniture.gdshader material (ADR 0005), with the model's albedo, normal and packed occlusion,
/// roughness and metallic (ORM) maps. A model over its triangle budget is drawn from a simplified
/// level of detail; its coarse mesh is a voxel proxy (see Proxy), a few boxes like the procedural
/// coarse meshes, with each material's average colour in a per-model part table (see
/// furniture.gdshader's coarse_parts).
/// Models, sources, authors and licences: game/CREDITS.md, written by scripts/fetch-models.py.
/// </summary>
public static class FurnitureModels
{
    /// <summary>
    /// One external model: which Furniture type and variant it draws, its folder under
    /// assets/models, the turn (degrees about Y) that brings its front to +Z, its triangle budget,
    /// whether its light fabric takes the piece's Palette tint, and where it comes from. A model whose
    /// simpler levels fall apart (a chair's thin seat and back) keeps only the levels with at least
    /// <c>SimplestShare</c> of its triangles.
    /// </summary>
    public sealed record Model(
        FurnitureType Type, int Variant, string Folder, float Yaw, int TriangleBudget, bool Tinted,
        string Author, string Licence, string Source, float SimplestShare = 0f);

    // Fewer triangles for the types repeated most (chairs come in sets around every table).
    private const int ChairBudget = 1500;
    // The carved dining chair needs this many even at its simplest.
    private const int CarvedChairBudget = 2500;
    // A dining chair's simpler levels lose its seat and back first, leaving a few legs: from about 30 m it
    // all but vanished, so it keeps only the levels with at least this share of its triangles.
    private const float ChairShare = 0.4f;
    private const int SeatBudget = 3500;
    private const int SofaBudget = 4500;
    // Toilets and washbasins fill whole bathroom Displays.
    private const int BathroomBudget = 1500;
    // The Camping ground shows up to nine tents and about fifteen trees at once.
    private const int TentBudget = 6000;
    private const int TreeBudget = 5000;
    // And about thirty bushes, small enough that their sources' ~1000 triangles are plenty.
    private const int BushBudget = 1200;
    // Rugs lie flat under nearly every Display: a fringe and a few folds are all they show.
    private const int RugBudget = 300;

    // The coarse mesh's voxel size, in metres (see Proxy): it is drawn only from the switch zoom
    // out (160 m), where a pixel spans about 20 cm, so a voxel is half a pixel.
    private static readonly float CoarseCell = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--coarse-cell=")) is { } arg
        ? float.Parse(arg["--coarse-cell=".Length..], System.Globalization.CultureInfo.InvariantCulture) : 0.15f;

    // At most this many voxels along a coarse mesh's longest side.
    private const float MaxCoarseCells = 20f;

    // A coarse face takes the model's surface normal only if it turns less than about 78° from the face's own.
    private const float MinShadingAlignment = 0.2f;

    // How far a rounded coarse side leans up (as a share of up added to its normal, about 19°), matched
    // to the toilet's flush pipe by the coarse colour check.
    private const float RoundedSideTilt = 0.35f;

    // A tinted material's texels lighter than this (linear luminance) are its fabric and take the
    // piece's tint; darker texels (wooden legs, frames, shadows) keep their colour. Matches
    // tint_threshold in furniture.gdshader.
    private const float TintThreshold = 0.12f;

    // Roughness the coarse mesh adds in place of the normal map it does not have.
    private const float NormalSpread = 0.4f;

    // A piece whose lowest point is under this height (metres) stands on the floor.
    private const float StandsOn = 0.01f;

    // A part that shows the Observer less than this share of its area (it faces the floor) is left out.
    private const float HiddenShare = 0.02f;

    // Simplified levels keep a normal seam sharper than this, in degrees.
    private const float LodMergeAngle = 60f;

    public static readonly IReadOnlyList<Model> All =
    [
        new(FurnitureType.Armchair, 0, "ArmChair_01", 0f, SeatBudget, true, "Kirill Sannikov", "CC0", "https://polyhaven.com/a/ArmChair_01"),
        // The modern cube armchair goes with the boxy fabric sofas; the classic and the modern one are equally common.
        new(FurnitureType.Armchair, 1, "cube_armchair", 0f, SeatBudget, true, "Muwanga Mohammed", "CC-BY", "https://sketchfab.com/3d-models/muwanga-lego-sofa-office-masterae-c27b118e5001429fb4411ba6f33fb3f3"),
        // Everyday fabric sofas, tinted to the Display's palette; the leather one (variant 3) is rare.
        new(FurnitureType.Sofa, 0, "fabric_sofa", 0f, SofaBudget, true, "Sempay_3D", "CC-BY", "https://sketchfab.com/3d-models/modern-minimalist-fabric-sofa-be39bf64cf4c4a02b55a0ef1d40e1fbb"),
        new(FurnitureType.Sofa, 1, "fabric_loveseat", 90f, SofaBudget, true, "Renend Studio", "CC-BY", "https://sketchfab.com/3d-models/modern-fabric-sofa-low-poly-game-ready-5a1ccfe43ffd4f1883abe4dca7d2b3bb"),
        new(FurnitureType.Sofa, 2, "fabric_sofa", 0f, SofaBudget, true, "Sempay_3D", "CC-BY", "https://sketchfab.com/3d-models/modern-minimalist-fabric-sofa-be39bf64cf4c4a02b55a0ef1d40e1fbb"),
        new(FurnitureType.Sofa, 3, "sofa_02", 0f, SofaBudget, false, "Kirill Sannikov", "CC0", "https://polyhaven.com/a/sofa_02"),
        new(FurnitureType.Chair, 0, "painted_wooden_chair_01", 0f, ChairBudget, false, "Kuutti Siitonen", "CC0", "https://polyhaven.com/a/painted_wooden_chair_01", ChairShare),
        new(FurnitureType.Chair, 1, "dining_chair_02", 0f, CarvedChairBudget, false, "James Ray Cock", "CC0", "https://polyhaven.com/a/dining_chair_02", ChairShare),
        new(FurnitureType.Chair, 2, "GreenChair_01", 0f, ChairBudget, false, "Kirill Sannikov", "CC0", "https://polyhaven.com/a/GreenChair_01", ChairShare),
        new(FurnitureType.Toilet, 0, "toilet", 0f, BathroomBudget, false, "loafbrr_1", "CC0", "https://opengameart.org/content/toilets"),
        new(FurnitureType.VanityUnit, 0, "washbasin", 0f, BathroomBudget, false, "loafbrr_1", "CC0", "https://opengameart.org/content/toilets"),
        new(FurnitureType.OutOfOrderSign, 0, "WetFloorSign_01", 0f, 5000, false, "Fran Calvente", "CC0", "https://polyhaven.com/a/WetFloorSign_01"),
        new(FurnitureType.HandDryer, 0, "hand_dryer", 0f, 5000, false, "Sousinho", "CC-BY", "https://sketchfab.com/3d-models/hand-dryer-67031384356e40dc96ec1d4fb61a18fd"),
        new(FurnitureType.PaperTowelDispenser, 0, "paper_towel_dispenser", 0f, 5000, false, "Carolyn Wong", "CC-BY", "https://sketchfab.com/3d-models/paper-towel-dispenser-15c4a2aa81994874a8dfc8038339b08d"),
        new(FurnitureType.BabyChangingTable, 0, "baby_changing_station", 0f, 5000, false, "JakeBradley", "CC-BY", "https://sketchfab.com/3d-models/changing-station-8d9e952dc3c34536b30337450c29300f"),
        new(FurnitureType.RestroomSign, 0, "restroom_sign", 0f, 5000, false, "Ellis Fossett", "CC-BY", "https://sketchfab.com/3d-models/airport-toiletrestroom-sign-562555b57b5540ed81e25593a2d62791"),
        new(FurnitureType.LabelledBox, 0, "cardboard_box_01", 0f, 20000, false, "Rahul Chaudhary", "CC0", "https://polyhaven.com/a/cardboard_box_01"),
        new(FurnitureType.UnsortedCart, 0, "industrial_storage_cart", 0f, 20000, false, "Jule Bielitz", "CC0", "https://polyhaven.com/a/industrial_storage_cart"),
        new(FurnitureType.ServiceBell, 0, "desk_bell", 0f, 5000, false, "ClintonAbbott.Art", "CC-BY", "https://sketchfab.com/3d-models/desk-bell-cc21b304c4bb4750bdde7a8fb538b116"),
        new(FurnitureType.NoticeBoard, 0, "notice_board", 0f, 5000, false, "Mehdi Shahsavan", "CC-BY", "https://sketchfab.com/3d-models/notice-board-9a2ae180dae541d2abfec62864dd9ef4"),
        new(FurnitureType.LostAndFoundSign, 0, "lost_and_found_sign", 90f, 5000, false, "Sousinho", "CC-BY", "https://sketchfab.com/3d-models/hospital-hanging-signage-74157aae9eb746d9a0cfe7f9c5c7f83f"),
        new(FurnitureType.ServiceCounter, 0, "service_counter", 0f, 5000, false, "DZigner", "CC-BY", "https://sketchfab.com/3d-models/small-modern-reception-counter-c8392731f0da42d68042013df924ea66"),
        new(FurnitureType.DiningTable, 0, "dining_table", 0f, 10000, false, "Aron Łyczek", "CC0", "https://polyhaven.com/a/dining_table"),
        new(FurnitureType.DiningTable, 1, "round_wooden_table_01", 0f, 2000, false, "Ulan Cabanilla", "CC0", "https://polyhaven.com/a/round_wooden_table_01"),
        new(FurnitureType.DiningTable, 2, "painted_wooden_table", 0f, 10000, false, "Kirill Sannikov", "CC0", "https://polyhaven.com/a/painted_wooden_table"),
        new(FurnitureType.Sideboard, 0, "modern_wooden_cabinet", 0f, 1200, false, "Patrik Pangerl", "CC0", "https://polyhaven.com/a/modern_wooden_cabinet"),
        new(FurnitureType.ShelfUnit, 0, "steel_frame_shelves_01", 0f, 1500, false, "James Ray Cock", "CC0", "https://polyhaven.com/a/steel_frame_shelves_01"),
        new(FurnitureType.ShoppingCart, 0, "shopping_cart", 0f, 3500, false, "Sebastian ramirez muñoz", "CC-BY", "https://sketchfab.com/3d-models/shopping-cart-8b2de85475184aa3a7fa9f244307dadf"),
        new(FurnitureType.MealPlate, 0, "meal_plate", 0f, 2000, false, "_dark_alice_", "CC-BY", "https://sketchfab.com/3d-models/plate-of-food-49a9493f884d405997d0f021639b22cf"),
        new(FurnitureType.Rug, 0, "rug", 0f, RugBudget, true, "mfb64", "CC-BY", "https://sketchfab.com/3d-models/kuuma-ye-rug-by-kristiina-lassus-5f5de1ba643746fc9da76d2cac94fbb3"),
        new(FurnitureType.DoorFrame, 0, "door_frame", 0f, 3000, false, "witnessk", "CC-BY", "https://sketchfab.com/3d-models/door-with-frame-2f2f149f3ec44d658a02c1f924dfa449"),
        new(FurnitureType.ExitSign, 0, "exit_sign", 0f, 5000, false, "tboiston", "CC-BY", "https://sketchfab.com/3d-models/exit-sign-46b52e08b8b64b81ad9ff5fb752bcc06"),
        // The Camping ground (ticket 07z2). Its Lamps (the cloth flame, the tent's lantern-lit door, the lanterns) stay
        // procedural: their glow follows the Lamp level, which a model's material cannot.
        new(FurnitureType.Tent, 0, "camp_tent_dome", 0f, TentBudget, true, "Guy in a Poncho", "CC-BY", "https://sketchfab.com/3d-models/modern-camping-tent-3f624d58d582430595179bdf9a156be3"),
        new(FurnitureType.Tent, 1, "camp_tent_tipi", 0f, TentBudget, false, "lampyre3d", "CC-BY", "https://sketchfab.com/3d-models/teepee-63e67c40b3994278a09705941b6981b7"),
        new(FurnitureType.Tent, 2, "camp_tent_family", 0f, TentBudget, true, "Alex272005", "CC-BY", "https://sketchfab.com/3d-models/camping-tent-5fa10f0a207d4c9580b6e77bd45f774e"),
        new(FurnitureType.Tent, 3, "camp_tent_round", 0f, TentBudget, false, "Periklis", "CC-BY", "https://sketchfab.com/3d-models/circus-tent-4998d155bd654544a577dec078ef5aea"),
        new(FurnitureType.Tent, 4, "camp_tent_ridge", 0f, TentBudget, true, "Adeel Qadir", "CC-BY", "https://sketchfab.com/3d-models/camping-tent-21cc274c86104fc997e0f172938d4205"),
        new(FurnitureType.Tent, 5, "camp_tent_popup", 0f, TentBudget, true, "Harold.Llanos", "CC-BY", "https://sketchfab.com/3d-models/carpa-playera-beach-tent-ffdd0519fa604e138d88a0c7fa262bb8"),
        new(FurnitureType.Tree, 0, "camp_tree_oak", 0f, TreeBudget, false, "Maple", "CC-BY", "https://sketchfab.com/3d-models/oak-tree-game-ready-2f4ae6b907fd4741abda5d1b71c83b10"),
        new(FurnitureType.Tree, 1, "camp_tree_spruce", 0f, TreeBudget, false, "Alan Zimmerman", "CC-BY", "https://sketchfab.com/3d-models/spruce-tree-low-poly-b68f79aee62f4e849be265c903f724f5"),
        new(FurnitureType.Tree, 2, "camp_bush", 0f, BushBudget, false, "Daniel", "CC-BY", "https://sketchfab.com/3d-models/stylized-bush-9d9ce79d3ae040619e96d5b22c7de1a6"),
        new(FurnitureType.Tree, 3, "camp_bush_flowering", 0f, BushBudget, false, "SangeetBlaze", "CC-BY", "https://sketchfab.com/3d-models/stylized-bush-v1-7ccbbb3307004ad18bed624e356c5975"),
        new(FurnitureType.TreeHouse, 0, "camp_tree_house", 0f, 8000, false, "d-luX", "CC-BY", "https://sketchfab.com/3d-models/dlx-tree-housewith-tree-1cf11dd38e03427a843fb034d3f7eec9"),
        new(FurnitureType.CampfireRing, 0, "camp_campfire", 0f, 4000, false, "Sarah Shahzad", "CC-BY", "https://sketchfab.com/3d-models/campfire-logs-360f5729f7d447b598170fd66b26c3b5"),
        new(FurnitureType.LogSeat, 0, "camp_log_bench", 0f, 1000, false, "AmbiMesh", "CC-BY", "https://sketchfab.com/3d-models/log-bench-55fea7c2f116426a9c7332fcf5ef1705"),
        new(FurnitureType.Kayak, 0, "camp_kayak", 0f, 5000, true, "nayangway", "CC-BY", "https://sketchfab.com/3d-models/kayak-with-paddle-cda3354816af4a0aa1ca5cfee6c5dcd7"),
        new(FurnitureType.PlushBear, 0, "camp_teddy_bear", -90f, 5000, false, "Xeno Studios", "CC-BY", "https://sketchfab.com/3d-models/teddy-bear-9c7081efd187447da9a333f89ea43f80"),
        new(FurnitureType.Axe, 0, "camp_axe", 0f, 2000, false, "James Ray Cock, Ulan Cabanilla", "CC0", "https://polyhaven.com/a/hatchet"),
        new(FurnitureType.MarshmallowStick, 0, "camp_marshmallow", 0f, 400, false, "Sillyy_", "CC-BY", "https://sketchfab.com/3d-models/marshmallow-on-a-stick-e7a92ddb4a11433690334eab67f29474"),
        new(FurnitureType.SleepingBag, 0, "camp_sleeping_bag", 90f, 1000, true, "Joe", "CC-BY", "https://sketchfab.com/3d-models/sleeping-bag-b1b452fe764a4cc9af3030f69939f506"),
        new(FurnitureType.Hammock, 0, "camp_hammock", 90f, 4500, true, "AndreyUEDev", "CC-BY", "https://sketchfab.com/3d-models/hammock-low-poly-c5fd4cef873f44f5a31db1fc6a04c572"),
        new(FurnitureType.FishingRod, 0, "camp_fishing_rod", 0f, 3000, false, "emelyarules", "CC-BY", "https://sketchfab.com/3d-models/fishing-rod-86c77e3d23d341cd90245c21402f68b9"),
        new(FurnitureType.RubberDuck, 0, "camp_rubber_duck", 0f, 1500, false, "Plat251", "CC0", "https://polyhaven.com/a/rubber_duck_toy"),
        new(FurnitureType.TrailSign, 0, "camp_signpost", 0f, 2500, false, "Tetiana_Denysenko", "CC-BY", "https://sketchfab.com/3d-models/signpost-493bcedc8b344a38a9a67587b14bfcff"),
        new(FurnitureType.CampChair, 0, "camp_chair", 0f, ChairBudget * 2, false, "lpfeffer", "CC-BY", "https://sketchfab.com/3d-models/camping-chair-46b592f41c1f441ca19faadd2b4bb90a"),
        // The Camping ground's sun lounger and sleeping bag sections (ticket 07z3), which show dozens of each at once.
        new(FurnitureType.SleepingBag, 1, "camp_sleeping_bag_rolled", 0f, 600, true, "Fochdog", "CC-BY", "https://sketchfab.com/3d-models/camping-sleeping-bag-d2ab6e8a8ffc472e9fca8b920662dafe"),
        new(FurnitureType.SunLounger, 0, "sun_lounger_wood", 0f, ChairBudget, false, "kirill", "CC-BY", "https://sketchfab.com/3d-models/deck-chair-f7b31f2190ae425682a873e28bd3244c"),
        new(FurnitureType.SunLounger, 1, "sun_lounger_plastic", 0f, ChairBudget, false, "rodestrela", "CC-BY", "https://sketchfab.com/3d-models/sun-lounger-3d-974e74f55bbd4dc9a85c4a48fc987a18"),
        new(FurnitureType.SunLounger, 2, "deck_chair", 90f, ChairBudget, true, "MaX3Dd", "CC-BY", "https://sketchfab.com/3d-models/deck-chair-38de6171cbeb41c5bdc49c07df76ede9"),
        new(FurnitureType.Parasol, 1, "beach_umbrella", 0f, 2000, false, "stealth86", "CC-BY", "https://sketchfab.com/3d-models/beach-umbrella-1014ad7c8a4c4aa8ba5e88fc6151aa4f"),
    ];

    /// <summary>The licences external models may have.</summary>
    public static readonly IReadOnlyList<string> AllowedLicences = ["CC0", "CC-BY"];

    private static readonly Dictionary<(FurnitureType, int), (ArrayMesh Full, ArrayMesh Coarse)> Cache = new();
    private static readonly List<ShaderMaterial> CoarseMaterials = [];
    private static bool _coarseInMagenta;

    // Per model part, the linear colour gain that makes the coarse mesh match the full mesh's rendered look
    // (the textures' average misses highlights, occlusion and the weight of lit tops); written by
    // CoarseColourCheck (coarse_gains.json), keyed by "Type-Variant" and part name.
    private const string GainsPath = "res://assets/models/coarse_gains.json";
    private static Dictionary<string, Dictionary<string, float[]>>? _gains;

    public static Vector3 CoarseGain(FurnitureType type, int variant, string part)
    {
        if (_gains is null)
        {
            using var file = Godot.FileAccess.Open(GainsPath, Godot.FileAccess.ModeFlags.Read);
            _gains = file is null ? [] : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, float[]>>>(file.GetAsText()) ?? [];
        }
        return _gains.TryGetValue($"{type}-{variant}", out var parts) && parts.TryGetValue(part, out var g) ? new Vector3(g[0], g[1], g[2]) : Vector3.One;
    }

    /// <summary>The model that draws a Furniture type and variant, or null if it is procedural.</summary>
    public static Model? Find(FurnitureType type, int variant) =>
        All.FirstOrDefault(m => m.Type == type && m.Variant == variant);

    /// <summary>The fitted full mesh (with automatic levels of detail) of a model; built once and shared.</summary>
    public static ArrayMesh Full(Model model) => Get(model).Full;

    /// <summary>The fitted coarse mesh of a model, one surface; built once and shared.</summary>
    public static ArrayMesh Coarse(Model model) => Get(model).Coarse;

    /// <summary>Dev aid: draws every coarse model mesh magenta (see <see cref="FurnitureMeshes.ShowCoarseInMagenta"/>).</summary>
    public static void ShowCoarseInMagenta(bool enabled)
    {
        _coarseInMagenta = enabled;
        foreach (var material in CoarseMaterials)
            material.SetShaderParameter("lod_debug", enabled);
    }

    /// <summary>The triangles of a mesh's base level, over every surface.</summary>
    public static int Triangles(ArrayMesh mesh)
    {
        var count = 0;
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var indices = mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index].AsInt32Array();
            count += indices.Length > 0 ? indices.Length / 3 : mesh.SurfaceGetArrayLen(s) / 3;
        }
        return count;
    }

    private static (ArrayMesh Full, ArrayMesh Coarse) Get(Model model)
    {
        if (!Cache.TryGetValue((model.Type, model.Variant), out var meshes))
        {
            var stamp = Stamp(model);
            Cache[(model.Type, model.Variant)] = meshes = Saved(model, stamp) ?? Save(model, stamp, Build(model));
        }
        return meshes;
    }

    // Building every model (simplifying it, voxelising its coarse mesh, averaging its textures) takes
    // seconds, so the built meshes are saved here and loaded again while nothing they are built from changes.
    private const string SavedDirectory = "user://model_cache";

    private static string SavedPath(Model model, string stamp, string mesh) => $"{SavedDirectory}/{model.Type}-{model.Variant}-{stamp}-{mesh}.res";

    /// <summary>
    /// What a model's meshes are built from: its record, the coarse voxel size, this code (the assembly's
    /// version id changes with every change to it), the coarse gains and the model's files.
    /// </summary>
    private static string Stamp(Model model)
    {
        var text = new System.Text.StringBuilder(FormattableString.Invariant(
            $"{model}|{CoarseCell}|{typeof(FurnitureModels).Assembly.ManifestModule.ModuleVersionId}|{Godot.FileAccess.GetModifiedTime(GainsPath)}"));
        void Add(string directory)
        {
            foreach (var file in DirAccess.GetFilesAt(directory))
                text.Append($"|{file}:{Godot.FileAccess.GetModifiedTime($"{directory}/{file}")}");
            foreach (var child in DirAccess.GetDirectoriesAt(directory))
                Add($"{directory}/{child}");
        }
        Add($"res://assets/models/{model.Folder}");
        return text.ToString().Md5Text();
    }

    private static (ArrayMesh Full, ArrayMesh Coarse)? Saved(Model model, string stamp)
    {
        var (fullPath, coarsePath) = (SavedPath(model, stamp, "full"), SavedPath(model, stamp, "coarse"));
        if (!Godot.FileAccess.FileExists(fullPath) || !Godot.FileAccess.FileExists(coarsePath)
            || ResourceLoader.Load<ArrayMesh>(fullPath) is not { } full || ResourceLoader.Load<ArrayMesh>(coarsePath) is not { } coarse)
            return null;
        var material = (ShaderMaterial)coarse.SurfaceGetMaterial(0);
        material.SetShaderParameter("lod_debug", _coarseInMagenta);
        CoarseMaterials.Add(material);
        return (full, coarse);
    }

    private static (ArrayMesh Full, ArrayMesh Coarse) Save(Model model, string stamp, (ArrayMesh Full, ArrayMesh Coarse) meshes)
    {
        DirAccess.MakeDirRecursiveAbsolute(SavedDirectory);
        // Meshes built from anything older are stale.
        foreach (var file in DirAccess.GetFilesAt(SavedDirectory).Where(f => f.StartsWith($"{model.Type}-{model.Variant}-")))
            DirAccess.RemoveAbsolute($"{SavedDirectory}/{file}");
        ResourceSaver.Save(meshes.Full, SavedPath(model, stamp, "full"));
        ResourceSaver.Save(meshes.Coarse, SavedPath(model, stamp, "coarse"));
        return meshes;
    }

    /// <summary>One material's triangles, fitted into mesh space.</summary>
    private sealed class Part(string name, BaseMaterial3D material)
    {
        public readonly string Name = name;
        public readonly BaseMaterial3D Material = material;
        public readonly List<Vector3> Positions = [];
        public readonly List<Vector3> Normals = [];
        public readonly List<float> Tangents = [];
        public readonly List<Vector2> Uvs = [];
        public readonly List<int> Indices = [];
    }

    private static (ArrayMesh Full, ArrayMesh Coarse) Build(Model model)
    {
        var scene = GD.Load<PackedScene>($"res://assets/models/{model.Folder}/{model.Folder}.gltf").Instantiate<Node3D>();
        var parts = new List<Part>();
        Collect(scene, new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(model.Yaw)), Vector3.Zero), parts);
        scene.Free();
        Fit(parts, FurnitureCatalog.Spec(model.Type, model.Variant));
        // A part the Observer never sees (a rug's back, flat on the floor) is drawn for nothing.
        parts.RemoveAll(p => Shown(p) < HiddenShare * Area(p));

        var fullIndices = Simplified(parts, model.TriangleBudget);
        var full = new ImporterMesh();
        for (var s = 0; s < parts.Count; s++)
            full.AddSurface(Mesh.PrimitiveType.Triangles, Arrays(parts[s], fullIndices[s]), name: parts[s].Name, material: Material(parts[s], model.Tinted));
        full.GenerateLods(LodMergeAngle, 0f, new Godot.Collections.Array());
        var fullMesh = model.SimplestShare > 0f ? WithLevelsDownTo(full, model.SimplestShare) : full.GetMesh();
        for (var s = 0; s < parts.Count; s++)
            fullMesh.SurfaceSetName(s, parts[s].Name);
        return (fullMesh, CoarseMesh(model, parts));
    }

    /// <summary>The mesh with only the simpler levels of each surface that keep at least <paramref name="share"/> of its triangles.</summary>
    private static ArrayMesh WithLevelsDownTo(ImporterMesh mesh, float share)
    {
        var result = new ArrayMesh();
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.GetSurfaceArrays(s);
            var full = arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length;
            var levels = new Godot.Collections.Dictionary();
            for (var level = 0; level < mesh.GetSurfaceLodCount(s); level++)
                if (mesh.GetSurfaceLodIndices(s, level) is var indices && indices.Length >= share * full)
                    levels[mesh.GetSurfaceLodSize(s, level)] = indices;
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, lods: levels);
            result.SurfaceSetMaterial(s, mesh.GetSurfaceMaterial(s));
        }
        return result;
    }

    /// <summary>
    /// Each part's indices at the most detailed simplified level that fits <paramref name="budget"/>
    /// triangles; a part simplified away entirely is removed.
    /// </summary>
    private static List<int[]> Simplified(List<Part> parts, int budget)
    {
        var levels = parts.Select(ComponentLevels).ToList();

        DropSmallestPieces(parts, levels, budget);

        // Over budget: the base level is the most detailed simplified level that fits.
        var total = parts.Sum(p => p.Indices.Count / 3);
        var fraction = Math.Min(1f, (float)budget / total);
        while (fraction > 1e-4f && levels.Sum(l => Level(l, fraction).Length / 3) > budget)
            fraction *= 0.8f;
        var indices = levels.Select(l => Level(l, fraction)).ToList();
        // A part simplified away entirely has no surface.
        for (var s = parts.Count - 1; s >= 0; s--)
            if (indices[s].Length == 0)
            {
                parts.RemoveAt(s);
                indices.RemoveAt(s);
            }
        return indices;
    }

    /// <summary>
    /// A model of a Goods product (ticket 08r), turned by <paramref name="yaw"/> so its front faces +Z and scaled
    /// evenly so its longest side is <paramref name="longest"/> metres, its footprint centred on the origin and its
    /// base at Y = 0; one surface per material at most <see cref="ProductBudget"/> triangles, no levels of detail
    /// (<see cref="ShelfGoods"/> copies it along a shelf and simplifies the whole load). Built once per folder and shared.
    /// </summary>
    public static ArrayMesh Product(string folder, float yaw, float longest)
    {
        if (Products.TryGetValue(folder, out var cached))
            return cached;
        var scene = GD.Load<PackedScene>($"res://assets/models/{folder}/{folder}.gltf").Instantiate<Node3D>();
        var parts = new List<Part>();
        Collect(scene, new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(yaw)), Vector3.Zero), parts);
        scene.Free();
        Fit(parts, size => Vector3.One * (longest / size[(int)size.MaxAxisIndex()]));
        var indices = Simplified(parts, ProductBudget);
        var mesh = new ArrayMesh();
        for (var s = 0; s < parts.Count; s++)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Arrays(parts[s], indices[s]));
            mesh.SurfaceSetMaterial(s, Material(parts[s], tinted: false));
            mesh.SurfaceSetName(s, parts[s].Name);
        }
        return Products[folder] = mesh;
    }

    // A shelf shows dozens of products and a store aisle hundreds of shelves, each product a few centimetres
    // across seen from a metre or more away: this many triangles keep its silhouette and label.
    private const int ProductBudget = 120;

    private static readonly Dictionary<string, ArrayMesh> Products = new();

    /// <summary>
    /// A model whose pieces are still over budget at their simplest (a cabinet with many small
    /// bevelled details) loses its smallest pieces, by bounding-box volume, until it fits. The pieces that
    /// reach its outline go last: those it stands on (legs, feet, a plinth), since a thin leg is as small as a
    /// screw but without it the model floats, and those at its sides and top (a tree's outermost leaves),
    /// without which it no longer fills its Furniture size.
    /// </summary>
    private static void DropSmallestPieces(List<Part> parts, List<List<int[][]>> levels, int budget)
    {
        var floor = levels.Sum(l => l.Sum(p => p[^1].Length / 3));
        if (floor <= budget)
            return;
        var pieces = levels.SelectMany((list, s) => list.Select(piece => (Part: s, Piece: piece, Box: Bounds(parts[s].Positions, piece[0])))).ToList();
        var outline = pieces.Select(p => p.Box).Aggregate((a, b) => a.Merge(b));
        bool ReachesOutline(Aabb box) =>
            (box.Position - outline.Position).Abs() is var low && (outline.End - box.End).Abs() is var high
            && Mathf.Min(Mathf.Min(low.X, low.Y), Mathf.Min(low.Z, Mathf.Min(high.X, Mathf.Min(high.Y, high.Z)))) < StandsOn;
        pieces = pieces.OrderBy(p => ReachesOutline(p.Box)).ThenBy(p => p.Box.Volume).ToList();
        // The last piece stays, over budget or not: a model with nothing left draws nothing.
        foreach (var (part, piece, _) in pieces.SkipLast(1))
        {
            if (floor <= budget)
                break;
            floor -= piece[^1].Length / 3;
            levels[part].Remove(piece);
        }
    }

    /// <summary>The bounding box around the vertices of <paramref name="indices"/>.</summary>
    private static Aabb Bounds(IReadOnlyList<Vector3> positions, IEnumerable<int> indices)
    {
        Aabb? box = null;
        foreach (var i in indices)
            box = box?.Expand(positions[i]) ?? new Aabb(positions[i], Vector3.Zero);
        return box ?? new Aabb();
    }

    /// <summary>
    /// A part's connected pieces (legs, cushions, frame), each with its own simplified levels, most
    /// detailed first, in the part's vertex indices. Simplifying piece by piece keeps small pieces
    /// such as legs, which simplifying the whole part at once would collapse to nothing.
    /// </summary>
    private static List<int[][]> ComponentLevels(Part part)
    {
        // Vertices at one position (split for normals or UVs) belong to the same piece.
        var root = Enumerable.Range(0, part.Positions.Count).ToArray();
        int Find(int i)
        {
            while (root[i] != i)
                i = root[i] = root[root[i]];
            return i;
        }
        void Union(int a, int b) => root[Find(a)] = Find(b);
        var byPosition = new Dictionary<(int, int, int), int>();
        for (var i = 0; i < part.Positions.Count; i++)
        {
            var key = PositionKey(part.Positions[i]);
            if (byPosition.TryGetValue(key, out var other))
                Union(i, other);
            else
                byPosition[key] = i;
        }
        for (var t = 0; t < part.Indices.Count; t += 3)
        {
            Union(part.Indices[t], part.Indices[t + 1]);
            Union(part.Indices[t], part.Indices[t + 2]);
        }
        var pieces = new Dictionary<int, List<int>>();
        for (var t = 0; t < part.Indices.Count; t += 3)
        {
            var key = Find(part.Indices[t]);
            if (!pieces.TryGetValue(key, out var list))
                pieces[key] = list = [];
            list.AddRange([part.Indices[t], part.Indices[t + 1], part.Indices[t + 2]]);
        }
        var result = new List<int[][]>();
        foreach (var indices in pieces.Values)
        {
            var vertices = indices.Distinct().ToArray();
            var local = new Dictionary<int, int>();
            for (var i = 0; i < vertices.Length; i++)
                local[vertices[i]] = i;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices.Select(v => part.Positions[v]).ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = vertices.Select(v => part.Normals[v]).ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = vertices.Select(v => part.Uvs[v]).ToArray();
            arrays[(int)Mesh.ArrayType.Index] = indices.Select(i => local[i]).ToArray();
            var mesh = new ImporterMesh();
            mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays);
            mesh.GenerateLods(LodMergeAngle, 0f, new Godot.Collections.Array());
            var levels = new List<int[]> { indices.ToArray() };
            for (var level = 0; level < mesh.GetSurfaceLodCount(0); level++)
                levels.Add(mesh.GetSurfaceLodIndices(0, level).Select(i => vertices[i]).ToArray());
            // A piece standing on the floor (a trunk, a leg) keeps only the levels that still reach it:
            // simplifying can drop its bottom ring and leave it floating.
            if (Bounds(part.Positions, levels[0]).Position.Y < StandsOn)
                levels.RemoveAll(l => Bounds(part.Positions, l).Position.Y >= StandsOn);
            result.Add([.. levels]);
        }
        return result;
    }

    /// <summary>A position rounded to 0.01 mm, to find the vertices at one position.</summary>
    private static (int, int, int) PositionKey(Vector3 position)
    {
        var p = position * 1e5f;
        return ((int)Mathf.Round(p.X), (int)Mathf.Round(p.Y), (int)Mathf.Round(p.Z));
    }

    /// <summary>A part's indices with each piece at its most detailed level that has at most <paramref name="fraction"/> of its triangles, or its simplest.</summary>
    private static int[] Level(List<int[][]> pieces, float fraction)
    {
        var result = new List<int>();
        foreach (var levels in pieces)
        {
            var target = levels[0].Length * fraction;
            result.AddRange(levels.FirstOrDefault(l => l.Length <= target) ?? levels[^1]);
        }
        return [.. result];
    }

    /// <summary>Every mesh surface under <paramref name="node"/>, in the model's space turned by its yaw; one part per material, transparent ones left out.</summary>
    private static void Collect(Node node, Transform3D transform, List<Part> parts)
    {
        if (node is Node3D spatial && node.GetParent() is not null)
            transform *= spatial.Transform;
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                // Opaque parts only (ADR 0005); transparent parts are small details.
                if ((instance.GetActiveMaterial(s) ?? mesh.SurfaceGetMaterial(s)) is not BaseMaterial3D material
                    || material.Transparency != BaseMaterial3D.TransparencyEnum.Disabled
                    || (mesh is ArrayMesh am && am.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles))
                    continue;
                var part = parts.FirstOrDefault(p => p.Material == material);
                if (part is null)
                    parts.Add(part = new Part(material.ResourceName is { Length: > 0 } name ? name : $"Material{parts.Count}", material));
                Append(part, mesh.SurfaceGetArrays(s), transform);
            }
        foreach (var child in node.GetChildren())
            Collect(child, transform, parts);
    }

    private static void Append(Part part, Godot.Collections.Array arrays, Transform3D transform)
    {
        var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var tangents = arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        if (indices.Length % 3 != 0 || (indices.Length == 0 && positions.Length % 3 != 0))
            return;
        var first = part.Positions.Count;
        var normalBasis = transform.Basis.Inverse().Transposed();
        for (var i = 0; i < positions.Length; i++)
        {
            part.Positions.Add(transform * positions[i]);
            part.Normals.Add(i < normals.Length ? (normalBasis * normals[i]).Normalized() : Vector3.Up);
            var tangent = 4 * i + 3 < tangents.Length ? transform.Basis * new Vector3(tangents[4 * i], tangents[4 * i + 1], tangents[4 * i + 2]) : Vector3.Right;
            part.Tangents.AddRange([tangent.X, tangent.Y, tangent.Z, 4 * i + 3 < tangents.Length ? tangents[4 * i + 3] : 1f]);
            part.Uvs.Add(i < uvs.Length ? uvs[i] : Vector2.Zero);
        }
        if (indices.Length == 0)
            indices = [.. Enumerable.Range(0, positions.Length)];
        part.Indices.AddRange(indices.Select(i => first + i));
    }

    /// <summary>
    /// Scales and moves the parts so their bounds fill the Furniture size exactly: width along X,
    /// depth along Z, height along Y, the footprint centred on the origin and the base at Y = 0.
    /// Normals and tangents follow the per-axis scale.
    /// </summary>
    private static void Fit(List<Part> parts, FurnitureSpec spec) =>
        Fit(parts, size => new Vector3(spec.Width / size.X, spec.Height / size.Y, spec.Depth / size.Z));

    /// <summary>Scales the parts by <paramref name="scaleFor"/> their bounds' size, the footprint centred on the origin and the base at Y = 0.</summary>
    private static void Fit(List<Part> parts, Func<Vector3, Vector3> scaleFor)
    {
        var all = parts.SelectMany(p => p.Positions).ToList();
        var (min, max) = (all.Aggregate(Vector3.Inf, (a, b) => a.Min(b)), all.Aggregate(-Vector3.Inf, (a, b) => a.Max(b)));
        var size = max - min;
        var scale = scaleFor(size);
        var centre = new Vector3((min.X + max.X) / 2f, min.Y, (min.Z + max.Z) / 2f);
        var inverse = new Vector3(1f / scale.X, 1f / scale.Y, 1f / scale.Z);
        foreach (var part in parts)
            for (var i = 0; i < part.Positions.Count; i++)
            {
                part.Positions[i] = (part.Positions[i] - centre) * scale;
                part.Normals[i] = (part.Normals[i] * inverse).Normalized();
                var tangent = (new Vector3(part.Tangents[4 * i], part.Tangents[4 * i + 1], part.Tangents[4 * i + 2]) * scale).Normalized();
                (part.Tangents[4 * i], part.Tangents[4 * i + 1], part.Tangents[4 * i + 2]) = (tangent.X, tangent.Y, tangent.Z);
            }
    }

    /// <summary>
    /// A surface of only the vertices its triangles use, in the order they first use them: a simplified
    /// level uses a small share of a detailed model's vertices, and the rest would bloat every instance's
    /// vertex fetch.
    /// </summary>
    private static Godot.Collections.Array Arrays(Part part, int[] indices)
    {
        var local = new Dictionary<int, int>();
        var used = new List<int>();
        var remapped = new int[indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            if (!local.TryGetValue(indices[i], out var index))
            {
                local[indices[i]] = index = used.Count;
                used.Add(indices[i]);
            }
            remapped[i] = index;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = used.Select(v => part.Positions[v]).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = used.Select(v => part.Normals[v]).ToArray();
        arrays[(int)Mesh.ArrayType.Tangent] = used.SelectMany(v => part.Tangents.GetRange(4 * v, 4)).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = used.Select(v => part.Uvs[v]).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = remapped;
        return arrays;
    }

    /// <summary>
    /// The coarse mesh: the model's voxel proxy (<see cref="Proxy"/>) in one surface, each vertex
    /// carrying its part in UV2.x and, on a tinted model, whether it lies on the light fabric that
    /// takes the tint in UV2.y. The part tables hold each part's average colour.
    /// </summary>
    private static ArrayMesh CoarseMesh(Model model, List<Part> parts)
    {
        var images = parts.Select(p => Decompressed(p.Material.AlbedoTexture)).ToList();
        // A low-poly model (a canvas tent) can have fewer triangles than its voxels' faces: coarsen the grid until it does not.
        var fullTriangles = parts.Sum(p => p.Indices.Count / 3);
        var cells = MaxCoarseCells;
        var (positions, normals, parts2) = Proxy(parts, images, model.Tinted, cells);
        while (positions.Count / 3 > fullTriangles && cells > 3f)
            (positions, normals, parts2) = Proxy(parts, images, model.Tinted, cells *= 0.75f);
        var albedo = new Vector4[FurnitureMeshes.PartSlots];
        var surface = new Vector2[FurnitureMeshes.PartSlots];
        var glow = new Vector3[FurnitureMeshes.PartSlots];
        for (var s = 0; s < parts.Count; s++)
        {
            var part = parts[s];
            var image = images[s];
            var colour = part.Material.AlbedoColor.SrgbToLinear() * (image is null ? Colors.White : Average(part, image, srgb: true, model.Tinted ? -1 : 0));
            var gain = CoarseGain(model.Type, model.Variant, part.Name);
            colour = new Color(colour.R * gain.X, colour.G * gain.Y, colour.B * gain.Z, colour.A);
            albedo[s] = new Vector4(colour.R, colour.G, colour.B, 0f);
            var orm = Decompressed(part.Material.RoughnessTexture);
            var packed = orm is null ? Colors.White : Average(part, orm, srgb: false);
            var channel = Orm(part.Material) ? 1 : 0;
            // The coarse mesh has no normal map: widen the roughness by the spread the map's bumps gave
            // the highlights (Toksvig-style), or polished metal turns into a dark mirror at a distance.
            var roughness = part.Material.Roughness * packed[channel];
            if (part.Material.NormalEnabled && part.Material.NormalTexture is not null)
                roughness = Mathf.Min(1f, Mathf.Sqrt(roughness * roughness + NormalSpread * NormalSpread));
            surface[s] = new Vector2(roughness, part.Material.Metallic * (Orm(part.Material) ? packed.B : 1f));
            if (part.Material.EmissionEnabled)
            {
                var emission = part.Material.Emission.SrgbToLinear() * part.Material.EmissionEnergyMultiplier;
                if (Decompressed(part.Material.EmissionTexture) is { } emissionImage)
                    emission *= Average(part, emissionImage, srgb: true);
                glow[s] = new Vector3(emission.R, emission.G, emission.B);
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = parts2.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = Enumerable.Range(0, positions.Count).ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/furniture.gdshader") };
        material.SetShaderParameter("coarse_parts", true);
        material.SetShaderParameter("part_albedo", albedo);
        material.SetShaderParameter("part_surface", surface);
        material.SetShaderParameter("part_glow", glow);
        material.SetShaderParameter("lod_debug", _coarseInMagenta);
        CoarseMaterials.Add(material);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    /// <summary>
    /// The coarse mesh's triangles: the model as solid voxels of about <see cref="CoarseCell"/> (its
    /// surface, filled inside), drawn as the voxels' outer faces merged into rectangles of one part
    /// (greedy meshing), downward faces left out. Like the procedural coarse meshes it is a few boxes,
    /// tens to a few hundred triangles however detailed the model: thin legs and frames thicken to one
    /// voxel, which a pixel at the switch zoom hides. Each voxel takes the part with the most surface
    /// in it and, on a tinted model, whether that surface is mostly light fabric.
    /// </summary>
    private static (List<Vector3> Positions, List<Vector3> Normals, List<Vector2> Parts) Proxy(List<Part> parts, List<Image?> images, bool tinted, float maxCells)
    {
        var all = parts.SelectMany(p => p.Positions).ToList();
        var (min, max) = (all.Aggregate(Vector3.Inf, (a, b) => a.Min(b)), all.Aggregate(-Vector3.Inf, (a, b) => a.Max(b)));
        var size = max - min;
        // A big model (a tree) gets bigger voxels, so its coarse mesh stays a few hundred triangles.
        var voxelSize = Mathf.Max(CoarseCell, Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) / maxCells);
        int Cells(float extent) => Math.Max(1, (int)Mathf.Round(extent / voxelSize));
        var n = new Vector3I(Cells(size.X), Cells(size.Y), Cells(size.Z));
        var cell = size / new Vector3(n.X, n.Y, n.Z);
        int Index(Vector3I v) => (v.Y * n.Z + v.Z) * n.X + v.X;
        bool Inside(Vector3I v) => v.X >= 0 && v.Y >= 0 && v.Z >= 0 && v.X < n.X && v.Y < n.Y && v.Z < n.Z;

        // The surface: each triangle sampled at a third of a voxel, its area shared among the samples
        // as votes for its part, light fabric or not, and to each voxel's surface normal.
        var votes = new Dictionary<int, float[]>();
        var surfaceNormals = new Dictionary<int, Vector3>();
        // A flat model (a rug) has one voxel on its thin axis, whose tiny cell would multiply the samples by thousands.
        var spacing = Mathf.Min(n.X > 1 ? cell.X : voxelSize, Mathf.Min(n.Y > 1 ? cell.Y : voxelSize, n.Z > 1 ? cell.Z : voxelSize)) / 3f;
        for (var s = 0; s < parts.Count; s++)
        {
            var part = parts[s];
            var image = tinted ? images[s] : null;
            for (var t = 0; t + 2 < part.Indices.Count; t += 3)
            {
                var (ia, ib, ic) = (part.Indices[t], part.Indices[t + 1], part.Indices[t + 2]);
                var (a, b, c) = (part.Positions[ia], part.Positions[ib], part.Positions[ic]);
                var steps = Math.Max(1, (int)Mathf.Ceil(Mathf.Max(a.DistanceTo(b), Mathf.Max(b.DistanceTo(c), c.DistanceTo(a))) / spacing));
                var weight = ((b - a).Cross(c - a).Length() / 2f + 1e-9f) * 2f / ((steps + 1) * (steps + 2));
                for (var i = 0; i <= steps; i++)
                    for (var j = 0; i + j <= steps; j++)
                    {
                        var (u, v) = ((float)i / steps, (float)j / steps);
                        var p = (a + (b - a) * u + (c - a) * v - min) / cell;
                        var voxel = new Vector3I(Math.Clamp((int)p.X, 0, n.X - 1), Math.Clamp((int)p.Y, 0, n.Y - 1), Math.Clamp((int)p.Z, 0, n.Z - 1));
                        var fabric = image is not null && Luminance(Sample(image,
                            part.Uvs[ia] + (part.Uvs[ib] - part.Uvs[ia]) * u + (part.Uvs[ic] - part.Uvs[ia]) * v)) > TintThreshold;
                        if (!votes.TryGetValue(Index(voxel), out var tally))
                            votes[Index(voxel)] = tally = new float[2 * parts.Count];
                        tally[2 * s + (fabric ? 1 : 0)] += weight;
                        surfaceNormals[Index(voxel)] = surfaceNormals.GetValueOrDefault(Index(voxel)) + weight *
                            (part.Normals[ia] + (part.Normals[ib] - part.Normals[ia]) * u + (part.Normals[ic] - part.Normals[ia]) * v);
                    }
            }
        }

        // Solid: every voxel that the outside, flooded in from the bounds' faces, does not reach.
        var outside = new bool[n.X * n.Y * n.Z];
        var queue = new Queue<Vector3I>();
        for (var y = 0; y < n.Y; y++)
            for (var z = 0; z < n.Z; z++)
                for (var x = 0; x < n.X; x++)
                {
                    var v = new Vector3I(x, y, z);
                    if ((x == 0 || y == 0 || z == 0 || x == n.X - 1 || y == n.Y - 1 || z == n.Z - 1) && !votes.ContainsKey(Index(v)))
                    {
                        outside[Index(v)] = true;
                        queue.Enqueue(v);
                    }
                }
        Vector3I[] directions = [Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward];
        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            foreach (var d in directions)
                if (v + d is var w && Inside(w) && !outside[Index(w)] && !votes.ContainsKey(Index(w)))
                {
                    outside[Index(w)] = true;
                    queue.Enqueue(w);
                }
        }
        bool Solid(Vector3I v) => Inside(v) && !outside[Index(v)];
        // A surface voxel's part and fabric as one key (2 × part + fabric); inside voxels show no face.
        int Key(Vector3I v)
        {
            if (!votes.TryGetValue(Index(v), out var tally))
                return -1;
            var best = 0;
            for (var k = 1; k < tally.Length; k++)
                if (tally[k] > tally[best])
                    best = k;
            return best;
        }

        var (positions, normals, keys) = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>());
        foreach (var d in directions.Where(d => d != Vector3I.Down))
        {
            // The faces' axis and the two axes of their plane.
            var axis = d.X != 0 ? 0 : d.Y != 0 ? 1 : 2;
            var (ua, va) = ((axis + 1) % 3, (axis + 2) % 3);
            var normal = new Vector3(d.X, d.Y, d.Z);
            for (var k = 0; k < n[axis]; k++)
            {
                var mask = new int[n[ua], n[va]];
                for (var u = 0; u < n[ua]; u++)
                    for (var v = 0; v < n[va]; v++)
                    {
                        var voxel = new Vector3I { [axis] = k, [ua] = u, [va] = v };
                        mask[u, v] = Solid(voxel) && !Solid(voxel + d) ? Key(voxel) : -1;
                    }
                for (var v = 0; v < n[va]; v++)
                    for (var u = 0; u < n[ua]; u++)
                    {
                        var key = mask[u, v];
                        if (key < 0)
                            continue;
                        var width = 1;
                        while (u + width < n[ua] && mask[u + width, v] == key)
                            width++;
                        var height = 1;
                        while (v + height < n[va] && Enumerable.Range(u, width).All(x => mask[x, v + height] == key))
                            height++;
                        // Shaded with the model's surface normal under the face (a slope stepped into voxels
                        // lights like the slope), unless that cancels out, as round a thin leg or pipe, whose
                        // sides then lean up a little, lighting like a rounded side under the Store light.
                        var sum = Vector3.Zero;
                        for (var y = v; y < v + height; y++)
                            for (var x = u; x < u + width; x++)
                            {
                                mask[x, y] = -1;
                                sum += surfaceNormals.GetValueOrDefault(Index(new Vector3I { [axis] = k, [ua] = x, [va] = y }));
                            }
                        var shading = sum.Normalized().Dot(normal) > MinShadingAlignment ? sum.Normalized()
                            : axis == 1 ? normal : (normal + RoundedSideTilt * Vector3.Up).Normalized();
                        Vector3 Corner(int cu, int cv) =>
                            min + new Vector3 { [axis] = k + (d[axis] > 0 ? 1 : 0), [ua] = cu, [va] = cv } * cell;
                        var (p0, p1, p2, p3) = (Corner(u, v), Corner(u + width, v), Corner(u + width, v + height), Corner(u, v + height));
                        // Front faces wind clockwise: (c - a) × (b - a) points out.
                        if ((p2 - p0).Cross(p1 - p0).Dot(normal) < 0f)
                            (p1, p3) = (p3, p1);
                        foreach (var p in (Vector3[])[p0, p1, p2, p0, p2, p3])
                        {
                            positions.Add(p);
                            normals.Add(shading);
                            keys.Add(new Vector2(key / 2, key % 2));
                        }
                    }
            }
        }
        return (positions, normals, keys);
    }

    /// <summary>Whether a material's roughness map is a packed occlusion, roughness, metallic map (glTF's metallicRoughness texture).</summary>
    private static bool Orm(BaseMaterial3D material) =>
        material.RoughnessTextureChannel == BaseMaterial3D.TextureChannel.Green;

    /// <summary>A model material as a furniture.gdshader material: albedo, normal and ORM maps, and on a tinted model the tint of its light fabric.</summary>
    private static ShaderMaterial Material(Part part, bool tinted)
    {
        var source = part.Material;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/furniture.gdshader") };
        material.SetShaderParameter("albedo_color", source.AlbedoColor);
        material.SetShaderParameter("roughness", source.Roughness);
        material.SetShaderParameter("metallic", source.Metallic);
        material.SetShaderParameter("textured", source.AlbedoTexture is not null);
        material.SetShaderParameter("orm", Orm(source));
        material.SetShaderParameter("normal_enabled", source.NormalEnabled && source.NormalTexture is not null);
        if (source.EmissionEnabled)
        {
            material.SetShaderParameter("emission_color", source.Emission);
            material.SetShaderParameter("emission_energy", source.EmissionEnergyMultiplier);
            material.SetShaderParameter("lamp_glow", false);
            if (source.EmissionTexture is { } emission)
            {
                material.SetShaderParameter("emission_textured", true);
                material.SetShaderParameter("emission_texture", emission);
            }
        }
        foreach (var (name, texture) in new[] { ("albedo_texture", source.AlbedoTexture), ("roughness_texture", source.RoughnessTexture), ("normal_texture", source.NormalTexture) })
            if (texture is not null)
                material.SetShaderParameter(name, texture);
        if (tinted && Decompressed(source.AlbedoTexture) is { } image)
        {
            material.SetShaderParameter("tinted", true);
            material.SetShaderParameter("tint_light", true);
            material.SetShaderParameter("tint_threshold", TintThreshold);
            material.SetShaderParameter("tint_luminance", Luminance(Average(part, image, srgb: true, light: 1)));
        }
        return material;
    }

    private static Image? Decompressed(Texture2D? texture)
    {
        var image = texture?.GetImage();
        if (image is null || image.IsEmpty())
            return null;
        if (image.IsCompressed())
            image.Decompress();
        return image.IsCompressed() ? null : image;
    }

    private static Color Sample(Image image, Vector2 uv, bool srgb = true)
    {
        var (w, h) = (image.GetWidth(), image.GetHeight());
        var x = Math.Clamp((int)(Mathf.PosMod(uv.X, 1f) * w), 0, w - 1);
        var y = Math.Clamp((int)(Mathf.PosMod(uv.Y, 1f) * h), 0, h - 1);
        return srgb ? image.GetPixel(x, y).SrgbToLinear() : image.GetPixel(x, y);
    }

    // The Observer camera's views of a piece: from the four sides and between them, 30° up, and from above.
    private static readonly Vector3[] Views =
    [
        .. Enumerable.Range(0, 8).Select(k => new Vector3(Mathf.Sin(k * Mathf.Pi / 4f) * Mathf.Cos(Mathf.Pi / 6f), Mathf.Sin(Mathf.Pi / 6f), Mathf.Cos(k * Mathf.Pi / 4f) * Mathf.Cos(Mathf.Pi / 6f))),
        Vector3.Up,
    ];

    /// <summary>The average area a triangle (its area vector, front-face normal times area) shows over the Observer's <see cref="Views"/>.</summary>
    private static float Shown(Vector3 areaVector) => Views.Average(v => Mathf.Max(0f, areaVector.Dot(v)));

    /// <summary>The area a part shows over the Observer's <see cref="Views"/>, summed over its triangles.</summary>
    private static float Shown(Part part) => Triangles(part).Sum(Shown);

    /// <summary>A part's surface area.</summary>
    private static float Area(Part part) => Triangles(part).Sum(t => t.Length());

    /// <summary>Each triangle's area vector: its front-face normal times its area.</summary>
    private static IEnumerable<Vector3> Triangles(Part part)
    {
        for (var i = 0; i + 2 < part.Indices.Count; i += 3)
        {
            var (a, b, c) = (part.Positions[part.Indices[i]], part.Positions[part.Indices[i + 1]], part.Positions[part.Indices[i + 2]]);
            yield return (c - a).Cross(b - a) / 2f;
        }
    }

    private static float Luminance(Color linear) => 0.2126f * linear.R + 0.7152f * linear.G + 0.0722f * linear.B;

    /// <summary>
    /// The average of a texture over a part's surface as the part shows it (texture atlases have
    /// unused, often black, regions): sampled at four points per triangle, weighted by the area it
    /// shows the Observer camera (<see cref="Shown"/>), so undersides do not count; linear
    /// for an <paramref name="srgb"/> texture. With <paramref name="light"/> 1 only its light texels
    /// count (a tinted material's fabric), with -1 only its dark ones (the untinted rest).
    /// </summary>
    private static Color Average(Part part, Image image, bool srgb, int light = 0)
    {
        var (sum, weight) = (new Color(0f, 0f, 0f, 0f), 0f);
        for (var i = 0; i + 2 < part.Indices.Count; i += 3)
        {
            var (a, b, c) = (part.Indices[i], part.Indices[i + 1], part.Indices[i + 2]);
            var area = Shown((part.Positions[c] - part.Positions[a]).Cross(part.Positions[b] - part.Positions[a]) / 2f);
            var centre = (part.Uvs[a] + part.Uvs[b] + part.Uvs[c]) / 3f;
            foreach (var uv in (Vector2[])[centre, (centre + part.Uvs[a]) / 2f, (centre + part.Uvs[b]) / 2f, (centre + part.Uvs[c]) / 2f])
            {
                var texel = Sample(image, uv, srgb);
                if (light != 0 && Luminance(texel) > TintThreshold != light > 0)
                    continue;
                sum += texel * area;
                weight += area;
            }
        }
        return weight <= 0f ? Colors.White : sum / weight;
    }
}
