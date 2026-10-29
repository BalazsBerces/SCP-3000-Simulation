using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --climbing-check=directory
    private async void CheckClimbing(string directory)
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
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            for (var i = 0; i < 5; i++)
                Host.Scenario.PlaceFurniture(FurnitureType.PalletRack, new(20, 100), elevation: i * 4.6f, tint: i * 2);
            var person = Host.Scenario.PlaceVillager(new(20, 104));
            Host.Scenario.SendToLookout(person.Id, new(20, 100.55f, 23));
            Host.SkipTime(TimeSpan.FromMinutes(25));
            var halfway = Host.Snapshot.Villagers.Single();
            Check(halfway.Elevation is > 5 and < 20, $"Climbing at {halfway.Elevation:F2} m");
            await Wait(0.5); // SeedPanel defers its initial sandbox camera move.
            foreach (var layer in GetTree().Root.FindChildren("*", "CanvasLayer", true, false).Cast<CanvasLayer>()) layer.Visible = false;
            Rig.JumpTo(new Vector3(20, 0, 100)); Rig.JumpToYaw(0.3f); Rig.JumpToDistance(55);
            await Wait(2);
            var figure = GetTree().Root.FindChildren($"Villager{person.Id}", "MeshInstance3D", true, false)
                .Cast<MeshInstance3D>().Single();
            Check(MathF.Abs(figure.Position.Y - halfway.Elevation) < 0.01f, "Rendered figure follows core elevation");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "climbing.png")) == Error.Ok, "Capture climbing");
            Host.SkipTime(TimeSpan.FromMinutes(5));
            await Wait(0.2);
            Check(Host.Snapshot.Villagers.Single().Elevation > halfway.Elevation + 2, "Climber continues upwards over time");
            Rig.Follow(person.Id); Rig.JumpToDistance(12);
            await Wait(2);
            var climber = Host.Snapshot.Villagers.Single();
            Check(MathF.Abs(Rig.Focus.Y - climber.Elevation) < 0.05f, "Follow camera tracks climbing height at close zoom");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var close = GetViewport().GetTexture().GetImage();
            if (close.GetWidth() > 1280) close.Resize(1280, close.GetHeight() * 1280 / close.GetWidth());
            Check(close.SavePng(System.IO.Path.Combine(directory, "climbing-follow.png")) == Error.Ok, "Capture close follow view");
            lines.Add("Climbing rendered check passed.");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
            GD.PushError(error.ToString()); GetTree().Quit(3);
        }
    }
}
