using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --collapse-check=directory
    private async void CheckCollapse(string directory)
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
            // A tower of wardrobes with a box on top, and a coffee table that will be overloaded.
            var wardrobe = FurnitureCatalog.Spec(FurnitureType.Wardrobe).Height;
            var bottom = scenario.PlaceFurniture(FurnitureType.Wardrobe, new(20, 104));
            scenario.PlaceFurniture(FurnitureType.Wardrobe, new(20, 104), elevation: wardrobe);
            scenario.PlaceFurniture(FurnitureType.Wardrobe, new(20, 104), elevation: wardrobe * 2);
            scenario.PlaceFurniture(FurnitureType.CardboardBox, new(20, 104), elevation: wardrobe * 3);
            var table = scenario.PlaceFurniture(FurnitureType.CoffeeTable, new(25, 104));
            Host.SkipTime(TimeSpan.FromMinutes(1));
            Check(Host.Snapshot.Furniture.Count(p => p.Elevation > 1) == 3, "Tower stands within its limits");
            await Wait(0.5); // SeedPanel defers its initial sandbox camera move.
            Rig.JumpTo(new Vector3(22.5f, 0, 104));
            Rig.JumpToYaw(0.5f);
            Rig.JumpToDistance(16);
            await Wait(2);
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Capture " + name);
            }
            await Capture("collapse-before");
            scenario.RemoveFurniture(p => p.Id == bottom.Id);
            Host.SkipTime(TimeSpan.FromMinutes(1));
            scenario.PlaceFurniture(FurnitureType.Wardrobe, new(25, 104), elevation: FurnitureCatalog.Spec(table).Height);
            Host.SkipTime(TimeSpan.FromMinutes(1));
            Check(Host.Snapshot.Furniture.All(p => p.Elevation < 1), "Everything came down");
            Check(Host.Snapshot.Furniture.Single(p => p.Id == table.Id).Damage == 1, "The coffee table gave way");
            Check(Host.Snapshot.Chronicle.Any(l => l.Contains("collapsed") || l.Contains("came down") || l.Contains("crash")), "Chronicle entry");
            await Wait(0.35);
            Check(GetTree().Root.FindChildren("Falling*", "MultiMeshInstance3D", true, false).Count == 4, "Four pieces falling");
            await Capture("collapse-falling");
            await Wait(2.5);
            Check(GetTree().Root.FindChildren("Falling*", "MultiMeshInstance3D", true, false).Count == 0, "Falls finished");
            await Capture("collapse-after");
            lines.Add("Collapse rendered check passed.");
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
