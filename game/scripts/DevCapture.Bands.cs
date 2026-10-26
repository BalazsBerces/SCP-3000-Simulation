using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --band-check=directory after the separate --.
    private async void CheckBands(string directory)
    {
        var lines = new List<string>();
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            void Check(bool passed, string message)
            {
                if (!passed) throw new InvalidOperationException(message);
                lines.Add("PASS " + message);
            }
            Host.Speed = SimulationSpeed.Paused;
            Check(Host.Snapshot.VillagerSandbox && Host.Snapshot.Villagers.Count == 0, "Empty sandbox loaded");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var shelf = Host.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
            Host.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
            Host.Scenario.PlaceWaterPoint(new(22, 108));
            Host.Scenario.PlaceBand([new(20, 104), new(22, 104)]);
            await Wait(0.5);
            Rig.JumpTo(new Vector3(20, 0, 104));
            Rig.JumpToDistance(18);
            await Wait(2);
            var label = GetTree().Root.FindChildren("Communities", "Label", true, false).Cast<Label>().Single();
            Check(label.Text.Contains("Band 0: 2 Wanderers"), "Band membership displayed");
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280)
                    image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Captured " + name);
            }
            await Capture("band");
            Host.SkipTime(TimeSpan.FromHours(1));
            await Wait(1);
            Check(Host.Snapshot.Bands.Count == 0 && Host.Snapshot.Villages.Count == 1, "Band founded a Village");
            Check(Host.Snapshot.Villages[0].Stores.GetValueOrDefault(GoodsKind.TinnedFood) > 0, "Scavengers delivered food to stores");
            Check(label.Text.Contains("Village 0: 2 Villagers") && label.Text.Contains("TinnedFood") && label.Text.Contains("founded Village"),
                "Village membership, stores and founding chronicle displayed");
            await Capture("village");
            var band = Host.Scenario.PlaceBand([new(34, 104)], knownMap: new KnownMap
            {
                ExploredGround = Array.AsReadOnly(new[] { new System.Numerics.Vector2(100, 100) }),
            });
            Host.SkipTime(TimeSpan.FromHours(1));
            await Wait(1);
            Check(Host.Snapshot.Villages[0].VillagerIds.Contains(band.VillagerIds[0]), "Wanderer walked to and joined the Village");
            Check(Host.Snapshot.Villages[0].KnownMap.ExploredGround.Contains(new(100, 100)), "Joining Wanderer shared their Known map");
            Check(label.Text.Contains("Village 0: 3 Villagers") && label.Text.Contains("joined Village 0"),
                "Population and joining Chronicle update in the presentation");
            await Capture("joined");
            lines.Add("Band rendered check passed.");
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
            GetTree().Quit(2);
        }
    }
}
