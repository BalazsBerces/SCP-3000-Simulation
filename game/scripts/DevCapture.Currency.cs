using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --currency-check=absolute-directory
    private async void CheckCurrency(string directory)
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
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Captured " + name);
            }
            Host.Speed = SimulationSpeed.Paused;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            foreach (var x in new[] { 30f, 50f, 70f, 90f })
            {
                var people = Enumerable.Range(0, 3).Select(i => Host.Scenario.PlaceVillager(new(x - 1 + i, 104))).ToArray();
                var village = Host.Scenario.PlaceVillage(new(x, 104), people.Select(p => p.Id).ToArray());
                Host.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 40);
                Host.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 40);
                Host.Scenario.SetStores(village.Id, GoodsKind.Batteries, 2);
            }
            for (var i = 0; i < 20 && Host.Snapshot.Villages[0].Currency is null; i++)
            {
                Host.SkipTime(TimeSpan.FromMinutes(30));
                await Wait(0.3);
            }
            Check(Host.Snapshot.Villages[0].Currency == GoodsKind.Batteries, "Village 0 takes Batteries as currency");
            Check(Host.Snapshot.Chronicle.Any(l => l.Contains("batteries as currency")), "Chronicle records the currency and where");
            Rig.JumpTo(new Vector3(60, 0, 104));
            Rig.JumpToDistance(70);
            await Wait(1.5);
            var marker = (Button)GetTree().Root.FindChild("InspectVillage0", true, false);
            Check(marker.Text.Contains("takes Batteries"), "Map marker names the currency");
            await Capture("map");
            StoreOverview.Set(true);
            Rig.JumpToDistance(160);
            await Wait(1.5);
            await Capture("overview");
            StoreOverview.Set(false);
            Rig.JumpToDistance(70);
            await Wait(1);
            marker.EmitSignal(Button.SignalName.Pressed);
            await Wait(0.8);
            var info = (Label)GetTree().Root.FindChild("VillageInfo", true, false);
            Check(info.Text.Contains("Currency: Batteries"), "Village panel shows the currency");
            await Capture("panel");
            lines.Add("Currency rendered check passed.");
            GD.Print(lines[^1]);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error);
            GD.PushError(error.ToString());
            GetTree().Quit(2);
        }
        finally
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
        }
    }
}
