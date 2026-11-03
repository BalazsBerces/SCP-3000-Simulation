using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --band-night-check=directory after the separate --.
    private async void CheckBandNights(string directory)
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
            for (var i = 0; i < 8; i++)
                Host.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(14 + i * 1.5f, 109), tint: i);
            var band = Host.Scenario.PlaceBand([new(20, 104), new(21, 104), new(20, 105)], personality: Personality.Cautious);
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(21.48));
            Host.SkipTime(TimeSpan.FromHours(0.75));
            await Wait(1);
            Rig.JumpTo(new Vector3(20, 0, 104));
            Rig.JumpToDistance(14);
            await Wait(2);
            var label = GetTree().Root.FindChildren("Communities", "Label", true, false).Cast<Label>().Single();
            var marker = GetTree().Root.FindChildren("*", "Label3D", true, false).Cast<Label3D>().FirstOrDefault(l => l.Text.StartsWith($"Band {band.Id}"));
            Check(Host.Snapshot.Bands.Single().Barricade.Count == 8, "The hiding Band closed a ring of bookcases round itself");
            Check(label.Text.Contains($"Band {band.Id}: 3 Wanderers, hides"), "Band strategy shown in the Communities panel");
            Check(marker?.Text == $"Band {band.Id}: hides", "Band strategy shown on its marker");
            Check(label.Text.Contains($"Band {band.Id} hid in silence"), "First night strategy told in the chronicle");
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280)
                    image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Captured " + name);
            }
            await Capture("hiding");
            Host.Scenario.StaffHitsVillager(band.VillagerIds[2]);
            Host.Scenario.StaffHitsVillager(band.VillagerIds[2]);
            Host.SkipTime(StoreClock.TickDuration);
            await Wait(1);
            Check(label.Text.Contains("hides (will turn to barricades)"), "Coming strategy switch shown after a member is Taken");
            Host.SkipTime(TimeSpan.FromHours(10));
            await Wait(1);
            Check(label.Text.Contains($"Band {band.Id} gave up hiding and turned to barricading"), "Strategy switch told in the chronicle");
            await Capture("morning");
            lines.Add("Band night check passed.");
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
