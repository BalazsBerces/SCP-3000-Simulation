using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --power-check=directory
    private async void CheckPower(string directory)
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
            for (var i = 0; i < 3; i++)
            {
                var at = new System.Numerics.Vector2(16 + i * 16, 104);
                var person = Host.Scenario.PlaceVillager(at);
                var home = Host.Scenario.PlaceVillage(at, [person.Id]);
                Host.Scenario.AssignRole(person.Id, Role.Guard);
                if (i == 1) Host.Scenario.EquipGear(person.Id, GearKind.HandCrankedGenerator);
                if (i == 2) Host.Scenario.EquipGear(person.Id, GearKind.Candle);
                else
                {
                    var lamp = Host.Scenario.PlaceFurniture(FurnitureType.FloorLamp, at + new System.Numerics.Vector2(0, 8));
                    Host.Scenario.MoveFurniture(lamp.Id, at + new System.Numerics.Vector2(0, 2), 0);
                }
                if (i == 0) Host.Scenario.SetStores(home.Id, GoodsKind.Batteries, 1);
                Host.Scenario.PlaceFurniture(FurnitureType.Chair, at + new System.Numerics.Vector2(2, 0));
            }
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(23));
            Host.SkipTime(TimeSpan.FromMinutes(1));
            Check(Host.Snapshot.LitAreas.Count == 3, "Battery Lamp, cranked Lamp and Candle expose light");
            Check(Host.Snapshot.Villagers.Any(v => v.Action == VillagerAction.Cranking), "Generator has a cranking Villager");
            await Wait(0.5);
            foreach (var layer in GetTree().Root.FindChildren("*", "CanvasLayer", true, false).Cast<CanvasLayer>()) layer.Visible = false;
            var lamps = GetTree().Root.FindChildren("*", "Node3D", true, false).OfType<LampLights>().Single();
            foreach (var (name, distance) in new[] { ("near", 48f), ("far", Rig.MaxDistance) })
            {
                Rig.JumpTo(new Vector3(32, 0, 104)); Rig.JumpToYaw(0); Rig.JumpToDistance(distance);
                await Wait(2);
                Check(lamps.VisibleLamps >= 3, $"Village lights have halos at {name} zoom: {lamps.Describe()}");
                if (name == "near") Check(lamps.ActiveLights >= 3, "Nearby Village lights use the shared real-light budget");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                var candle = Host.Snapshot.LitAreas.Single(a => a.Id < 0);
                var pixel = Rig.Camera.UnprojectPosition(candle.Position.ToWorld(candle.Elevation));
                var brightest = Enumerable.Range(-3, 7).SelectMany(x => Enumerable.Range(-3, 7).Select(y =>
                    image.GetPixel(Math.Clamp((int)pixel.X + x, 0, image.GetWidth() - 1),
                        Math.Clamp((int)pixel.Y + y, 0, image.GetHeight() - 1)).R)).Max();
                Check(brightest > 0.04f, $"Candle source is visible in rendered pixels at {name} zoom ({brightest:F3})");
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, $"power-{name}.png")) == Error.Ok, $"Captured {name} Village lights");
            }
            Host.SkipTime(TimeSpan.FromHours(4.1));
            await Wait(0.5);
            Check(Host.Snapshot.LitAreas.Count == 0 && lamps.ActiveLights == 0, "Exhausted battery, Candle and cranker go dark");
            lines.Add("Power rendered check passed.");
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error); GD.PushError(error.ToString()); GetTree().Quit(2);
        }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
