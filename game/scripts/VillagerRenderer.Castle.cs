using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    // The camp banner is 3 m wide; a castle flies a third of it.
    private const float BannerScale = 0.35f;
    private static readonly Color MoatWater = new(0.08f, 0.32f, 0.48f);
    private static readonly Color MoatDitch = new(0.16f, 0.12f, 0.09f);
    private readonly Dictionary<int, Node3D> _castles = [];
    private readonly Dictionary<(int Village, int Structure), float> _bannerHeights = [];
    private static readonly Dictionary<HeraldryDivision, Mesh> DivisionMeshes = [];

    // Castle pieces are real Furniture; this adds the Village's banners (the camp banner model, its cloth in the Heraldry field)
    // over the centre and each finished Tower, Gatehouse and Keep, and the Moats' ditches.
    private void ShowCastles()
    {
        var world = Host.Snapshot;
        var seen = new HashSet<int>();
        foreach (var village in world.Villages)
        {
            if (village.Heraldry is not { } arms) continue;
            seen.Add(village.Id);
            if (!_castles.TryGetValue(village.Id, out var node))
            {
                node = new Node3D { Name = $"Castle{village.Id}" };
                node.AddChild(new MultiMeshInstance3D { Name = "Banners" });
                node.AddChild(new MultiMeshInstance3D { Name = "Charges" });
                node.AddChild(new Node3D { Name = "Moats" });
                AddChild(node);
                _castles[village.Id] = node;
            }
            var near = world.Space.Delta(Rig.Focus.ToStore(), village.Position);
            node.Position = new Vector3(Rig.Focus.X + near.X, 0, Rig.Focus.Z + near.Y);

            var banners = new List<Transform3D> { Banner(System.Numerics.Vector2.Zero, 0, BannerScale * 1.6f) };
            for (var i = 0; i < village.Castle.Count; i++)
            {
                var structure = village.Castle[i];
                if (!structure.Complete) continue;
                if (!_bannerHeights.TryGetValue((village.Id, i), out var top))
                    _bannerHeights[(village.Id, i)] = top = structure.Slots.Max(s => s.Elevation +
                        FurnitureCatalog.Spec(world.Furniture.First(p => p.Id == s.FurnitureId)).Height);
                banners.Add(Banner(world.Space.Delta(village.Position, structure.Slots[^1].Position), top, BannerScale));
            }
            Fly(node.GetNode<MultiMeshInstance3D>("Banners"), FurnitureMeshes.For(FurnitureType.CampBanner), banners,
                FurnitureMeshes.TintFor(FurnitureType.CampBanner, arms.Field));
            Fly(node.GetNode<MultiMeshInstance3D>("Charges"), DivisionMesh(arms.Division), banners,
                FurnitureMeshes.TintFor(FurnitureType.CampBanner, arms.Charge));

            var moats = node.GetNode<Node3D>("Moats");
            var runs = village.Moats.SelectMany(m => m.Runs.Select(r => (Run: r, m.Water))).ToArray();
            if (moats.GetChildCount() != runs.Length)
            {
                foreach (var child in moats.GetChildren()) child.QueueFree();
                foreach (var (run, water) in runs)
                {
                    var start = world.Space.Delta(village.Position, run.Start);
                    var along = world.Space.Delta(run.Start, run.End);
                    var mid = start + along / 2;
                    moats.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(along.Length(), 0.02f, 1.2f) },
                        MaterialOverride = new StandardMaterial3D { AlbedoColor = water ? MoatWater : MoatDitch, Roughness = water ? 0.1f : 1f },
                        Position = new Vector3(mid.X, 0.01f, mid.Y),
                        Rotation = new Vector3(0, -MathF.Atan2(along.Y, along.X), 0),
                    });
                }
            }
        }
        foreach (var id in _castles.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _castles[id].QueueFree();
            _castles.Remove(id);
            foreach (var key in _bannerHeights.Keys.Where(k => k.Village == id).ToArray()) _bannerHeights.Remove(key);
        }

        static Transform3D Banner(System.Numerics.Vector2 at, float elevation, float scale) =>
            new(Basis.Identity.Scaled(Vector3.One * scale), new Vector3(at.X, elevation, at.Y));
    }

    private static void Fly(MultiMeshInstance3D instance, Mesh mesh, List<Transform3D> banners, Color colour)
    {
        if (instance.Multimesh is not { } multiMesh || multiMesh.InstanceCount != banners.Count)
            instance.Multimesh = multiMesh = new MultiMesh
                { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = banners.Count };
        for (var i = 0; i < banners.Count; i++)
        {
            multiMesh.SetInstanceTransform(i, banners[i]);
            multiMesh.SetInstanceColor(i, colour);
        }
    }

    /// <summary>The charge's share of the camp banner's cloth (both faces) for a division; generated: no CC0 or CC-BY heraldic
    /// banner whose arms can be recoloured was found (docs/ticket-19-verification.md).</summary>
    private static Mesh DivisionMesh(HeraldryDivision division)
    {
        if (DivisionMeshes.TryGetValue(division, out var cached)) return cached;
        var spec = FurnitureCatalog.Spec(FurnitureType.CampBanner);
        float x0 = -spec.Width / 2 + 0.08f, x1 = spec.Width / 2 - 0.08f, y0 = spec.Height - 1.3f, y1 = spec.Height - 0.1f;
        float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, bx = (x1 - x0) / 10, by = (y1 - y0) / 6;
        var polygons = division switch
        {
            HeraldryDivision.PerPale => [Rect(cx, y0, x1, y1)],
            HeraldryDivision.PerFess => [Rect(x0, y0, x1, cy)],
            HeraldryDivision.PerBend => [new[] { new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0) }],
            HeraldryDivision.Quarterly => [Rect(x0, cy, cx, y1), Rect(cx, y0, x1, cy)],
            HeraldryDivision.Cross => [Rect(cx - bx, y0, cx + bx, y1), Rect(x0, cy - by, x1, cy + by)],
            _ => new[] { new[] { new Vector2(x0, y0), new Vector2(cx, cy + by), new Vector2(x1, y0) } },
        };
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var z in new[] { -0.02f, 0.02f })
            foreach (var polygon in polygons)
                for (var i = 1; i + 1 < polygon.Length; i++)
                    foreach (var v in new[] { polygon[0], polygon[i], polygon[i + 1] })
                        tool.AddVertex(new Vector3(v.X, v.Y, z));
        var mesh = tool.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
            { CullMode = BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo = true, Roughness = 0.85f });
        return DivisionMeshes[division] = mesh;

        static Vector2[] Rect(float ax, float ay, float bx, float by) => [new(ax, ay), new(bx, ay), new(bx, by), new(ax, by)];
    }

    /// <summary>The Village panel lines for its Castle and arms.</summary>
    private static string CastleText(Village village)
    {
        var arms = village.Heraldry is { } h
            ? $"Arms: {h.Division} of tints {h.Field} and {h.Charge}" : "Arms: none yet";
        var structures = village.Castle.Count == 0 ? "No Castle planned" : string.Join("\n", village.Castle.Select(c =>
            $"{c.Part}: {c.Slots.Count(s => s.FurnitureId is not null)}/{c.Slots.Count}{(c.Complete ? " built" : "")}"));
        var moats = village.Moats.Count == 0 ? "No Moat" : string.Join("\n", village.Moats.Select(m => $"Moat: {(m.Water ? "water-filled" : "dry ditch")}"));
        var scaffold = village.PillarScaffold is not { } s ? "No Pillar scaffold" :
            $"Pillar scaffold: {s.Slots.Count(slot => slot.FurnitureId is not null)}/{s.Slots.Count} pieces" +
            (s.Complete ? $" | Lookout {s.Lookout.Z:0.0} m" : $" | {s.MaterialsNeeded} metal needed");
        return $"{arms}\n{structures}\n{moats}\n{scaffold}";
    }
}
