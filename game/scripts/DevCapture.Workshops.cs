using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --workshop-check=absolute-directory
    private async void CheckWorkshops(string directory)
    {
        var lines = new List<string>();
        System.IO.Directory.CreateDirectory(directory);
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            lines.Add("PASS " + message);
        }
        try
        {
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var a = Host.Scenario.PlaceVillager(new(20, 104));
            var b = Host.Scenario.PlaceVillager(new(21, 104));
            Host.Scenario.PlaceVillage(a.Position, [a.Id, b.Id]);
            Host.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
            Host.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
            for (var i = 0; i < 3; i++) Host.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(25 + i * 3, 104), tint: i * 3);
            for (var i = 0; i < 5000 && (Host.Snapshot.Villages[0].Workshops.Count != 3 || Host.Snapshot.Villages[0].Workshops.Any(w => !w.Complete)); i++)
                Host.SkipTime(StoreClock.TickDuration);
            Check(Host.Snapshot.Villages[0].Workshops.Count == 3 && Host.Snapshot.Villages[0].Workshops.All(w => w.Complete), "Three Workshops built through Furniture hauling");
            var kinds = Recipes.Middle.Select(r => r.Gear).ToArray();
            for (var i = 0; i < kinds.Length; i++)
            {
                var person = Host.Scenario.PlaceVillager(new(12 + i * 2, 99));
                Host.Scenario.EquipGear(person.Id, kinds[i]);
                var mesh = VillagerRenderer.GearMesh(kinds[i]);
                Check(mesh.GetSurfaceCount() > 0 && mesh.GetAabb().Size.Length() > 0.05f, $"Mesh for {kinds[i]}");
            }
            var box = Host.Scenario.PlaceFurniture(FurnitureType.FlatPackBox, new(22, 104));
            Host.Scenario.PlaceAssemblyInstructions(box.Id, GearKind.Lantern);
            Host.Scenario.SendVillager(a.Id, new(20, 104));
            for (var i = 0; i < 3000 && !Host.Snapshot.Villages[0].Recipes.Contains(GearKind.Lantern); i++) Host.SkipTime(StoreClock.TickDuration);
            Check(Host.Snapshot.Villages[0].Recipes.Contains(GearKind.Lantern), "Assembly instructions teach a Recipe");
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            await Wait(0.5);
            var toggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>().Single(t => t.Text.StartsWith("Known map"));
            toggle.ButtonPressed = false;
            Rig.JumpTo(new Vector3(20, 0, 104)); Rig.JumpToYaw(0); Rig.JumpToDistance(22);
            await Wait(2);
            foreach (var kind in Enum.GetValues<WorkshopKind>())
                Check(GetTree().Root.FindChild($"Workshop0{kind}", true, false) is Node3D, $"Visible {kind} station and label");
            var button = (Button)GetTree().Root.FindChild("InspectVillage0", true, false);
            button.EmitSignal(Button.SignalName.Pressed);
            await Wait(0.5);
            var info = (Label)GetTree().Root.FindChild("VillageInfo", true, false);
            Check(info.Text.Contains("Workshops") && info.Text.Contains("Lantern") && info.Text.Contains("assembly instructions"), "Village panel lists Workshops, Recipe and discovery history");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "workshops.png")) == Error.Ok, "Captured Workshops and middle Gear");
            lines.Add("Workshop rendered check passed.");
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error); GD.PushError(error.ToString()); GetTree().Quit(2);
        }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
