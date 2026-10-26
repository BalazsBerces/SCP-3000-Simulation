using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --furniture-work-check=directory
    private async void CheckFurnitureWork(string directory)
    {
        var lines = new List<string>();
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            void Check(bool ok, string message)
            {
                if (!ok) throw new InvalidOperationException(message);
                lines.Add("PASS " + message);
            }
            Check(Host.Snapshot.VillagerSandbox && Host.Snapshot.Villagers.Count == 0, "Empty Villager sandbox");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var scenario = Host.Scenario;
            var columns = new[] { 16f, 24f, 32f, 42f, 52f, 62f };
            var types = new[] { FurnitureType.CardboardBox, FurnitureType.Armchair, FurnitureType.Sofa,
                FurnitureType.Wardrobe, FurnitureType.Armchair, FurnitureType.ShoppingCart };
            var pieces = columns.Select((x, i) => scenario.PlaceFurniture(types[i], new(x, 100))).ToArray();
            for (var i = 0; i < columns.Length; i++)
            {
                var count = i == 2 ? 3 : 1;
                var workers = Enumerable.Range(0, count).Select(_ => scenario.PlaceVillager(new(columns[i] - 2, 100)).Id).ToArray();
                if (i == 3) scenario.EquipGear(workers[0], GearKind.ShoppingCart);
                if (i >= 4)
                {
                    scenario.PlaceVillage(new(columns[i] - 2, 100), workers);
                    scenario.EquipGear(workers[0], i == 4 ? GearKind.KitchenKnife : GearKind.Crowbar);
                    scenario.DisassembleFurniture(pieces[i].Id, workers[0]);
                }
                else scenario.CarryFurniture(pieces[i].Id, workers, new(columns[i], 116));
            }
            Host.SkipTime(TimeSpan.FromMinutes(6));
            Check(Host.Snapshot.FurnitureTasks.Count == 6 && Host.Snapshot.FurnitureTasks.All(t => t.Started), "All Furniture tasks started");
            foreach (var work in Enum.GetValues<FurnitureWork>())
                Check(Host.Snapshot.FurnitureTasks.Any(t => t.Work == work), $"{work} active");
            await Wait(0.5); // SeedPanel defers its initial sandbox camera move.
            Rig.JumpTo(new Vector3(39, 0, 107));
            Rig.JumpToYaw(0);
            Rig.JumpToDistance(38);
            await Wait(2);
            var toggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>()
                .Single(b => b.Text.StartsWith("Known map"));
            toggle.ButtonPressed = false;
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Capture " + name);
            }
            var labels = GetTree().Root.FindChildren("Work", "Label3D", true, false).Cast<Label3D>().ToArray();
            Check(labels.Length == 6 && labels.Any(l => l.Text.StartsWith("Disassembling")) &&
                labels.Any(l => l.Text.StartsWith("Team carrying")), "Rendered work labels and progress");
            Check(GetTree().Root.FindChildren("GearHand", "MeshInstance3D", true, false).Cast<MeshInstance3D>()
                .Any(m => m.Mesh == VillagerRenderer.GearMesh(GearKind.Crowbar)), "Downloaded crowbar rendered");
            await Capture("furniture-work-active");
            Host.SkipTime(TimeSpan.FromHours(1));
            Check(Host.Snapshot.FurnitureTasks.Count == 0, "All tasks completed");
            Check(Host.Snapshot.Villages[0].Materials[MaterialKind.Leather] == 4 &&
                Host.Snapshot.Villages[1].Materials[MaterialKind.Metal] == 6, "Leather and metal deposited in Village stores");
            await Wait(1);
            await Capture("furniture-work-completed");
            lines.Add("Furniture work rendered check passed.");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
            GD.Print(lines[^1]);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error);
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
            GD.PushError(error.ToString());
            GetTree().Quit(3);
        }
    }
}
