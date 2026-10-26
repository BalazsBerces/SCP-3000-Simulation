using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --gear-check=directory
    private async void CheckGear(string directory)
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
            var kinds = new[] { GearKind.HexKey, GearKind.KitchenKnife, GearKind.Scissors, GearKind.Screwdriver,
                GearKind.ShoppingCart, GearKind.Flashlight };
            for (var i = 0; i < kinds.Length; i++)
            {
                var v = Host.Scenario.PlaceVillager(new(20 + i * 2, 104));
                Host.Scenario.EquipGear(v.Id, kinds[i]);
                Host.Scenario.EquipGear(v.Id, GearKind.CanvasBag);
                if (kinds[i] == GearKind.Flashlight) Host.Scenario.SetCarriedLoad(v.Id, GoodsKind.Batteries, 1);
            }
            await Wait(0.5);
            var knownMapToggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>()
                .Single(b => b.Text.StartsWith("Known map"));
            knownMapToggle.ButtonPressed = false;
            Rig.JumpTo(new Vector3(25, 0, 104));
            Rig.JumpToDistance(13);
            await Wait(2);
            var renderer = GetTree().Root.FindChildren("VillagerRenderer", "Node3D", true, false).Cast<VillagerRenderer>().Single();
            Check(renderer.FindChildren("GearHand", "MeshInstance3D", true, false).Count == 6, "All six hand Gear models attached");
            Check(renderer.FindChildren("GearBag", "MeshInstance3D", true, false).Count == 6, "Canvas bags attached");
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Capture " + name);
            }
            await Capture("gear-open-centred");
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(22));
            Host.SkipTime(TimeSpan.FromMinutes(1));
            Rig.Follow(5);
            await Wait(2);
            var beam = renderer.FindChildren("Beam", "SpotLight3D", true, false).Cast<SpotLight3D>().Single();
            Check(beam.Visible, "Battery-powered flashlight is lit at Closing time");
            await Capture("gear-closing-centred");
            lines.Add("Gear rendered check passed.");
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
