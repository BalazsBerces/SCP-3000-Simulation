using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --device-check=directory
    private async void CheckDevices(string directory)
    {
        var lines = new List<string>();
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            void Check(bool ok, string text)
            {
                if (!ok) throw new InvalidOperationException(text);
                lines.Add("PASS " + text);
            }
            Check(Host.Snapshot.VillagerSandbox && Host.Snapshot.Villagers.Count == 0, "Empty sandbox");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            var at = new System.Numerics.Vector2(24, 104);
            var home = at + new System.Numerics.Vector2(0, -8);
            var members = Enumerable.Range(0, 3).Select(i => Host.Scenario.PlaceVillager(home + new System.Numerics.Vector2(i - 1, 0)).Id).ToArray();
            var village = Host.Scenario.PlaceVillage(home, members, Personality.Cautious);
            var radio = Host.Scenario.SetDevice(village.Id, DeviceKind.NoiseLure, at + new System.Numerics.Vector2(-3, 0));
            Host.Scenario.SetDevice(village.Id, DeviceKind.NoiseLure, at + new System.Numerics.Vector2(-1.5f, 0), variant: 1);
            Host.Scenario.SetDevice(village.Id, DeviceKind.Alarm, at + new System.Numerics.Vector2(0.5f, 2));
            var trap = Host.Scenario.SetDevice(village.Id, DeviceKind.FurnitureTrap, at + new System.Numerics.Vector2(3, 0));
            Host.Scenario.SetDevice(village.Id, DeviceKind.Pit, at + new System.Numerics.Vector2(0, -2.5f));
            await Wait(0.5);
            var knownMapToggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>()
                .Single(b => b.Text.StartsWith("Known map"));
            knownMapToggle.ButtonPressed = false;
            Rig.JumpTo(new Vector3(at.X, 0, at.Y));
            Rig.JumpToDistance(10);
            await Wait(1.5);
            var renderer = GetTree().Root.FindChildren("VillagerRenderer", "Node3D", true, false).Cast<VillagerRenderer>().Single();
            var drawn = renderer.FindChildren("Device*", "Node3D", true, false);
            Check(drawn.Count == 4 && drawn.Sum(d => d.GetChildCount()) == 7, $"Lures, alarm and pit drawn ({drawn.Count} devices, {drawn.Sum(d => d.GetChildCount())} meshes)");
            Check(trap.FurnitureIds.All(id => Host.Snapshot.Furniture.Any(p => p.Id == id && (id == trap.FurnitureIds[0] || p.Elevation > 0))),
                "The Furniture trap's stack stands");
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Capture " + name);
            }
            await Capture("devices-set");
            // At Closing time a Staff walks into the trap and the stack comes down on it.
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
            var from = at + new System.Numerics.Vector2(9, 0);
            Host.Scenario.PlaceStaff(from, [from, at + new System.Numerics.Vector2(3, 0)]);
            for (var i = 0; i < 40 && !Host.Snapshot.Devices.Single(d => d.Id == trap.Id).Sprung; i++) Host.SkipTime(TimeSpan.FromMinutes(1));
            Check(Host.Snapshot.Devices.Single(d => d.Id == trap.Id).Sprung, "The Staff sprang the Furniture trap");
            Check(Host.Snapshot.Chronicle.Any(l => l.Contains("a trap of Village")), "The trap is in the chronicle");
            Host.Scenario.SetOffLure(radio.Id);
            Host.SkipTime(TimeSpan.FromMinutes(1));
            Check(Host.Snapshot.Devices.Single(d => d.Id == radio.Id).PlayingUntil > Host.Snapshot.Clock.Tick, "The radio plays");
            await Wait(1.5);
            await Capture("devices-trap-sprung-night");
            // The night is dark by design; look at the fallen stack again in the open Store's light.
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            await Wait(1.5);
            await Capture("devices-trap-sprung-day");
            lines.Add("Device rendered check passed.");
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
