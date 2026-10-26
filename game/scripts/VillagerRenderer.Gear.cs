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
        // shortcut: scenarios use an existing cloth for armour, replace with fitted Gear when Recipes add armour.
        if (kind == GearKind.QuiltedArmour) return FurnitureModels.Product("rug", 0, 0.65f, 500);
        var model = kind == GearKind.Crowbar ? CrowbarModel : FurnitureModels.FoundGear.Single(m => m.Kind == kind);
        return FurnitureModels.Product(model.Folder, 0, model.Size, model.Budget);
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
