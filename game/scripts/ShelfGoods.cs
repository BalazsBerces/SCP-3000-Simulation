using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// The Goods on the shelves of Stock Furniture (ticket 08r): copies of each Goods kind's product model along the shelves of
/// grocery shelves, shelf units, pallet racks, toy shelves, cushion bins, chest freezers and on the Rack canyon's boxes, as many as the stock's fill step
/// (<see cref="WorldSnapshot.FillStep"/>) shows. A lower step leaves gaps, in a fixed shuffled order, so a low shelf looks
/// picked over rather than half built; a bare shelf (no stock) shows none. Display Furniture holds no stock and stays empty;
/// a fridge's and a counter's stock stays behind their closed bodies. One instance group per Furniture type, Goods kind and
/// fill step, every piece of a group drawing the same load mesh. Goods show close only (out to <see cref="Distance"/>), cast
/// no shadow and have no coarse mesh, like the fish in the tanks. Rebuilt whenever the snapshot's stock changes (only
/// dev and test tools change it so far). View only: it never touches the simulation.
/// </summary>
public partial class ShelfGoods : Node3D
{
    public SimulationHost Host { get; set; } = null!;
    public CameraRig Rig { get; set; } = null!;
    public StoreVisibility Visibility { get; set; } = null!;

    /// <summary>How far from the camera Goods show, in metres; farther a product is a pixel or two.</summary>
    public const float Distance = 50f;

    // At most this many products side by side along a run: a long pallet rack beam shows a row, not a hundred tins.
    private const int MostAlongARun = 20;

    // The share of a run's slots a full shelf (StockConfig.ShelfCapacity units) fills: a few products spread along it, not a packed row.
    private const double FullShare = 0.1;

    /// <summary>A Goods kind's product model: its folder, the turn that brings its front to +Z, and its real longest side in metres.</summary>
    private sealed record Product(string Folder, float Yaw, float Longest);

    // Batteries and bulbs are drawn a little larger than life, so they read as more than a glint on a shelf.
    private static readonly Dictionary<GoodsKind, Product> Products = new()
    {
        [GoodsKind.FreshFood] = new("goods_fresh_food", 0f, 0.2f),
        [GoodsKind.TinnedFood] = new("goods_tinned_food", 0f, 0.11f),
        [GoodsKind.DryFood] = new("goods_dry_food", 0f, 0.2f),
        [GoodsKind.BottledWater] = new("goods_bottled_water", 0f, 0.3f),
        [GoodsKind.SoftDrinks] = new("goods_soft_drinks", 0f, 0.13f),
        [GoodsKind.Coffee] = new("goods_coffee", 0f, 0.22f),
        [GoodsKind.Sweets] = new("goods_sweets", 90f, 0.12f),
        [GoodsKind.Candles] = new("goods_candles", 0f, 0.18f),
        [GoodsKind.Batteries] = new("goods_batteries", 0f, 0.1f),
        [GoodsKind.Bulbs] = new("goods_bulbs", 0f, 0.13f),
        [GoodsKind.Cables] = new("goods_cables", 0f, 0.3f),
        [GoodsKind.Tape] = new("goods_tape", 0f, 0.1f),
    };

    /// <summary>
    /// One run of Goods on a shelf, in mesh space: the centre of its front row on the shelf, the turn its Goods face, its
    /// length, the height above it, and how many rows deep (each <paramref name="RowPitch"/> further back) and tiers high.
    /// </summary>
    private readonly record struct Run(Vector3 Centre, float Yaw, float Length, float Clearance, int Rows = 1, float RowPitch = 0f, int Tiers = 1);

    private static readonly Dictionary<(FurnitureType, int, GoodsKind, int), Mesh> Loads = new();

    private IReadOnlyList<StockedGoods>? _built;

    public override void _Process(double delta)
    {
        if (!ReferenceEquals(Host.Snapshot.Stock, _built))
            Build(Host.Snapshot);
    }

    public void Build(WorldSnapshot snapshot)
    {
        foreach (var child in GetChildren())
            child.QueueFree();
        _built = snapshot.Stock;
        if (System.Environment.GetEnvironmentVariable("NOGOODS") != null) return;
        var pieces = snapshot.Furniture.ToDictionary(p => p.Id);
        var shown = snapshot.Stock
            .Where(g => !g.LeftBehind && Products.ContainsKey(g.Kind))
            .Select(g => (Goods: g, Piece: pieces[g.FurnitureId]))
            .Where(s => Runs(s.Piece.Type, s.Piece.Variant).Any());
        // shortcut: a second kind on one piece (only SetStock makes one) is drawn over the first; give each kind its share of the runs if pieces ever stock two kinds.
        foreach (var group in shown.GroupBy(s => (s.Piece.Type, s.Piece.Variant, s.Goods.Kind, Step: snapshot.FillStep(s.Goods))))
        {
            var instances = new WrappedInstances
            {
                Name = $"Goods-{group.Key.Type}-{group.Key.Variant}-{group.Key.Kind}-{group.Key.Step}",
                Rig = Rig,
                FarDistance = Distance,
                ViewReach = Visibility.PlacementReach(Rig, snapshot.Space.ChunkSize),
                Mesh = Load(group.Key.Type, group.Key.Variant, group.Key.Kind, group.Key.Step),
                CoarseMesh = new ArrayMesh(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(instances);
            instances.SetInstances(snapshot.Space, group.Select(s => FurnitureRenderer.Placement(s.Piece)));
        }
        GD.Print($"ShelfGoods: {GetChildCount()} groups.");
    }

    /// <summary>The runs of Goods on a Furniture type, or none if it shows no Goods.</summary>
    private static IEnumerable<Run> Runs(FurnitureType type, int variant)
    {
        var spec = FurnitureCatalog.Spec(type, variant);
        var (w, hw, hd, h) = (spec.Width, spec.Width / 2f, spec.Depth / 2f, spec.Height);
        // Shelf tops, their board thickness and the top they end under, as FurnitureMeshes and the shelf model build them.
        IEnumerable<Run> Levels(float[] tops, float board, float top, Func<float, float, IEnumerable<Run>> runs) =>
            tops.SelectMany((y, i) => runs(y, (i + 1 < tops.Length ? tops[i + 1] - board : top) - y));
        switch (type)
        {
            case FurnitureType.GroceryShelf:
                // Both faces of the double-sided aisle shelf.
                return Levels([0.12f, 0.525f, 0.925f, 1.325f], 0.025f, h - 0.08f, (y, clear) =>
                    [new(new(0f, y, hd - 0.12f), 0f, w - 0.1f, clear), new(new(0f, y, -hd + 0.12f), Mathf.Pi, w - 0.1f, clear)]);
            case FurnitureType.ShelfUnit:
                // The board tops of the steel-frame shelf model (steel_frame_shelves_01), fitted to the shelf unit's size.
                return Levels([0.111f, 0.608f, 1.105f, 1.602f], 0.02f, h, (y, clear) =>
                    [new(new(0f, y, hd - 0.15f), 0f, w - 0.15f, clear, Rows: 2, RowPitch: 0.25f)]);
            case FurnitureType.PalletRack:
                return Levels([.. FurnitureCatalog.PalletRackLevels], 0.1f, h - 0.1f, (y, clear) =>
                    [new(new(0f, y, hd - 0.15f), 0f, w - 0.2f, clear, Rows: 3, RowPitch: (spec.Depth - 0.3f) / 2f, Tiers: 2)]);
            case FurnitureType.ToyShelf:
                // Every cell: on the shelf in the empty ones, on top of the plastic box in the others.
                var (cell, row) = ((w - 0.06f) / 4f, (h - 0.02f) / 3f);
                return from r in Enumerable.Range(0, 3)
                       from c in Enumerable.Range(0, 4)
                       let box = (r + c) % 3 != 1 ? 0.24f : 0f
                       select new Run(new(-hw + 0.03f + (c + 0.5f) * cell, 0.02f + r * row + box, 0.08f), 0f, cell - 0.06f, row - 0.02f - box,
                           Rows: 2, RowPitch: 0.16f, Tiers: 3);
            case FurnitureType.CushionBin:
                // On the two cushions on top of the bin.
                return [.. new[] { -hw / 2f, hw / 2f }.Select(x => new Run(new(x, h, 0.12f), 0f, w * 0.4f, 0.3f, Rows: 2, RowPitch: 0.24f))];
            case FurnitureType.Freezer:
                // Lying in the open tub under the clear lid.
                return [new(new(0f, FurnitureMeshes.FreezerFloor, hd - 0.15f), 0f, w - 0.2f, h - 0.08f - FurnitureMeshes.FreezerFloor,
                    Rows: 3, RowPitch: (spec.Depth - 0.3f) / 2f)];
            case FurnitureType.FlatPackBox:
                // On the lid of a Rack canyon box, under the next beam of its rack.
                return [new(new(0f, h + 0.003f, hd - 0.15f), 0f, w - 0.2f, 0.4f, Rows: 3, RowPitch: (spec.Depth - 0.3f) / 2f)];
            default:
                return [];
        }
    }

    /// <summary>
    /// The load mesh of a Furniture type holding a Goods kind at a fill step: the product at the step's share of the slots of
    /// every run. The slots are shuffled in an order fixed per type and kind, so each step's products are the next step's minus
    /// some. Merged into one mesh per product material, with levels of detail; built once and shared.
    /// </summary>
    private static Mesh Load(FurnitureType type, int variant, GoodsKind kind, int step)
    {
        if (Loads.TryGetValue((type, variant, kind, step), out var cached))
            return cached;
        var product = Products[kind];
        var mesh = FurnitureModels.Product(product.Folder, product.Yaw, product.Longest);
        var size = mesh.GetAabb().Size;
        var random = new Random((int)type * 1000 + (int)kind);
        var slots = new List<Transform3D>();
        foreach (var run in Runs(type, variant))
        {
            var scale = Mathf.Min(1f, 0.9f * run.Clearance / size.Y);
            var tiers = Math.Clamp((int)(0.9f * run.Clearance / (size.Y * scale)), 1, run.Tiers);
            var pitch = Mathf.Max(Mathf.Max(size.X, size.Z) * scale * 1.15f, run.Length / MostAlongARun);
            var along = Math.Max(1, (int)(run.Length / pitch));
            var turn = new Basis(Vector3.Up, run.Yaw);
            var columns = (from c in Enumerable.Range(0, along) from r in Enumerable.Range(0, run.Rows) select (c, r))
                .Select(slot => (Slot: slot, Order: random.Next(), Jitter: (float)(random.NextDouble() - 0.5) * 0.3f)).ToList();
            var shownCount = (int)Math.Ceiling(columns.Count * FullShare * step / WorldSnapshot.FillSteps);
            foreach (var (slot, _, jitter) in columns.OrderBy(c => c.Order).Take(shownCount))
                for (var t = 0; t < tiers; t++)
                {
                    var at = run.Centre + turn * new Vector3((slot.c - (along - 1) / 2f) * pitch, t * size.Y * scale, -slot.r * run.RowPitch);
                    slots.Add(new Transform3D(turn * new Basis(Vector3.Up, jitter) * Basis.FromScale(Vector3.One * scale), at));
                }
        }
        return Loads[(type, variant, kind, step)] = Merged(mesh, slots);
    }

    /// <summary>Copies of every surface of <paramref name="mesh"/> at <paramref name="slots"/>, one surface each, with levels of detail.</summary>
    private static Mesh Merged(ArrayMesh mesh, List<Transform3D> slots)
    {
        var merged = new ImporterMesh();
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var source = mesh.SurfaceGetArrays(s);
            var (positions, normals, tangents, uvs, indices) = (source[(int)Mesh.ArrayType.Vertex].AsVector3Array(),
                source[(int)Mesh.ArrayType.Normal].AsVector3Array(), source[(int)Mesh.ArrayType.Tangent].AsFloat32Array(),
                source[(int)Mesh.ArrayType.TexUV].AsVector2Array(), source[(int)Mesh.ArrayType.Index].AsInt32Array());
            var (p, n, t, uv, ix) = (new List<Vector3>(), new List<Vector3>(), new List<float>(), new List<Vector2>(), new List<int>());
            foreach (var slot in slots)
            {
                var first = p.Count;
                var turn = slot.Basis.Orthonormalized();
                for (var i = 0; i < positions.Length; i++)
                {
                    p.Add(slot * positions[i]);
                    n.Add(turn * normals[i]);
                    var tangent = turn * new Vector3(tangents[4 * i], tangents[4 * i + 1], tangents[4 * i + 2]);
                    t.AddRange([tangent.X, tangent.Y, tangent.Z, tangents[4 * i + 3]]);
                    uv.Add(uvs[i]);
                }
                ix.AddRange(indices.Select(i => first + i));
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = p.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = n.ToArray();
            arrays[(int)Mesh.ArrayType.Tangent] = t.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = ix.ToArray();
            merged.AddSurface(Mesh.PrimitiveType.Triangles, arrays, material: mesh.SurfaceGetMaterial(s));
        }
        merged.GenerateLods(60f, 0f, new Godot.Collections.Array());
        return merged.GetMesh();
    }
}
