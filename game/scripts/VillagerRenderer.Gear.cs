using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    internal static readonly FurnitureModels.GearModel CrowbarModel = new(GearKind.Crowbar,
        "gear_crowbar", 0.65f, 800, "Alexander Otterbeck", "CC0", "https://polyhaven.com/a/crowbar_01");

    internal static Mesh GearMesh(GearKind kind)
    {
        if (kind == GearKind.ShoppingCart) return FurnitureMeshes.For(FurnitureType.ShoppingCart);
        if (kind == GearKind.TableLegClub) return _tableLeg ??= TableLeg();
        if (kind == GearKind.CardboardArmour) return FurnitureModels.Product("cardboard_box_01", 0, 0.65f, 500);
        if (kind == GearKind.Bandage) return FurnitureModels.Product("rug", 0, 0.2f, 300);
        if (kind == GearKind.Candle) return FurnitureModels.Product("goods_candles", 0, 0.2f, 500);
        // shortcut: scenarios use an existing cloth for armour, replace with fitted Gear when Recipes add armour.
        if (kind == GearKind.QuiltedArmour) return FurnitureModels.Product("rug", 0, 0.65f, 500);
        var model = kind == GearKind.Crowbar ? CrowbarModel : FurnitureModels.FoundGear.Single(m => m.Kind == kind);
        return FurnitureModels.Product(model.Folder, 0, model.Size, model.Budget);
    }

    private static ArrayMesh? _tableLeg;

    private static ArrayMesh TableLeg()
    {
        // Reuse the triangles of one downloaded table leg, keeping its wood material and UVs.
        var table = FurnitureModels.Product("dining_table", 0, 0.8f, 1500);
        var bounds = table.GetAabb();
        var leg = new ArrayMesh();
        for (var s = 0; s < table.GetSurfaceCount(); s++)
        {
            var arrays = table.SurfaceGetArrays(s);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            bool InLeg(int i) => vertices[i].X < bounds.Position.X + bounds.Size.X * 0.3f &&
                vertices[i].Z < bounds.Position.Z + bounds.Size.Z * 0.3f &&
                vertices[i].Y < bounds.Position.Y + bounds.Size.Y * 0.9f;
            var selected = new List<int>();
            for (var i = 0; i < indices.Length; i += 3)
                if (InLeg(indices[i]) && InLeg(indices[i + 1]) && InLeg(indices[i + 2]))
                    selected.AddRange(indices.AsSpan(i, 3).ToArray());
            if (selected.Count == 0) continue;
            var used = selected.Distinct().ToArray();
            var centre = new Vector3(used.Average(i => vertices[i].X), used.Min(i => vertices[i].Y), used.Average(i => vertices[i].Z));
            arrays[(int)Mesh.ArrayType.Vertex] = vertices.Select(v => v - centre).ToArray();
            arrays[(int)Mesh.ArrayType.Index] = selected.ToArray();
            leg.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            leg.SurfaceSetMaterial(leg.GetSurfaceCount() - 1, table.SurfaceGetMaterial(s));
        }
        if (leg.GetSurfaceCount() == 0) throw new InvalidOperationException("Downloaded table has no reusable leg.");
        return leg;
    }

    private void ShowGear(MeshInstance3D figure, Villager v)
    {
        foreach (var (slot, gear) in new[] { (GearSlot.Hand, v.Hand), (GearSlot.Bag, v.Bag), (GearSlot.Armour, v.Armour) })
        {
            var name = "Gear" + slot;
            var mesh = figure.GetNodeOrNull<MeshInstance3D>(name);
            if (gear is null)
            {
                if (mesh is not null) { figure.RemoveChild(mesh); mesh.QueueFree(); }
                continue;
            }
            if (mesh is null)
            {
                mesh = new MeshInstance3D { Name = name };
                figure.AddChild(mesh);
            }
            mesh.Mesh = GearMesh(gear.Kind);
            mesh.Position = slot == GearSlot.Bag ? new Vector3(-0.27f, 0.65f, 0) :
                slot == GearSlot.Armour ? new Vector3(0, 0.85f, 0.18f) :
                gear.Kind == GearKind.ShoppingCart ? new Vector3(0, 0, 0.95f) : new Vector3(0.37f, 0.8f, 0.1f);
            mesh.Rotation = slot == GearSlot.Armour ? new Vector3(Mathf.Pi / 2, 0, 0) : Vector3.Zero;
            var beam = mesh.GetNodeOrNull<SpotLight3D>("Beam");
            if (gear.Kind == GearKind.Flashlight && beam is null)
            {
                beam = new SpotLight3D { Name = "Beam", SpotRange = 10, SpotAngle = 38,
                    LightEnergy = 2, LightColor = new Color(1, 0.94f, 0.8f), Rotation = new Vector3(0, Mathf.Pi, 0) };
                mesh.AddChild(beam);
            }
            if (beam is not null) beam.Visible = gear.Kind == GearKind.Flashlight && gear.BatteryHours > 0 &&
                Host.Snapshot.Phase == DayPhase.ClosingTime;
        }
    }
}
