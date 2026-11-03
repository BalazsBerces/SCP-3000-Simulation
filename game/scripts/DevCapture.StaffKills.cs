using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --staff-kill-check=directory
    private async void CheckStaffKills(string directory)
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
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(1));
            var at = new System.Numerics.Vector2(24, 104);
            var staff = Host.Scenario.PlaceStaff(at, [at, at + new System.Numerics.Vector2(0.5f, 0)]);
            var group = Enumerable.Range(0, 4).Select(i =>
            {
                var v = Host.Scenario.PlaceVillager(at + new System.Numerics.Vector2(MathF.Cos(i * 1.6f), MathF.Sin(i * 1.6f)) * 1.5f);
                Host.Scenario.EquipGear(v.Id, GearKind.MetalArmour);
                Host.Scenario.EquipGear(v.Id, GearKind.Halberd);
                return v.Id;
            }).ToArray();
            // Old remains a few metres off, for the picture.
            Host.Scenario.PlaceStaffRemains(at + new System.Numerics.Vector2(5, 3), TimeSpan.FromHours(4));
            await Wait(0.5);
            var knownMapToggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>()
                .Single(b => b.Text.StartsWith("Known map"));
            knownMapToggle.ButtonPressed = false;
            Rig.JumpTo(new Vector3(at.X + 2, 0, at.Y + 1));
            Rig.JumpToDistance(11);
            await Wait(1.5);
            var renderer = GetTree().Root.FindChildren("VillagerRenderer", "Node3D", true, false).Cast<VillagerRenderer>().Single();
            Check(renderer.FindChildren("GearHand", "MeshInstance3D", true, false).Count == 4, "Four Halberds attached");
            Check(renderer.FindChildren("GearArmour", "MeshInstance3D", true, false).Count == 4, "Four metal armours attached");
            Check(renderer.FindChildren("StaffRemains*", "MeshInstance3D", true, false).Count == 1, "Old remains drawn");
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Capture " + name);
            }
            await Capture("staff-fight-before");
            Host.Scenario.FightStaff(staff.Id, group);
            for (var i = 0; i < 40 && Host.Snapshot.Staff.Count > 0; i++) Host.SkipTime(TimeSpan.FromSeconds(30));
            await Wait(1.5);
            Check(Host.Snapshot.Staff.Count == 0, "The group killed the Staff");
            Check(renderer.FindChildren("StaffRemains*", "MeshInstance3D", true, false).Count == 2, "Its remains are drawn where it fell");
            Check(Host.Snapshot.Chronicle.Any(l => l.Contains("killed a Staff") || l.Contains("A Staff fell for good")), "The kill is in the chronicle");
            await Capture("staff-killed-remains");
            // The night is dark by design; look at the remains again in the open Store's light.
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            await Wait(1.5);
            await Capture("staff-remains-daylight");
            lines.Add("Staff kill rendered check passed.");
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
