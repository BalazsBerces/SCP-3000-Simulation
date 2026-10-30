using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --fridge-check=directory
    private async void CheckFridges(string directory)
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
            var at = new System.Numerics.Vector2(20, 104);
            var person = Host.Scenario.PlaceVillager(at);
            var home = Host.Scenario.PlaceVillage(at, [person.Id]);
            Host.Scenario.AssignRole(person.Id, Role.Builder);
            Host.Scenario.EquipGear(person.Id, GearKind.ShoppingCart);
            Host.Scenario.SetStores(home.Id, GoodsKind.TinnedFood, 100);
            Host.Scenario.SetStores(home.Id, GoodsKind.BottledWater, 100);
            Host.Scenario.SetStores(home.Id, GoodsKind.FreshFood, 12);
            Host.Scenario.SetStores(home.Id, GoodsKind.Batteries, 3);
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
            var fridge = Host.Scenario.PlaceFurniture(FurnitureType.Fridge, at + new System.Numerics.Vector2(14, 0));
            Host.SkipTime(TimeSpan.FromHours(3));
            var moved = Host.Snapshot.Furniture.Single(p => p.Id == fridge.Id);
            Check(Host.Snapshot.Space.Distance(moved.Position, at) <= 8, $"Fridge brought home to {moved.Position}");
            Check(Host.Snapshot.Chronicle.Any(line => line.Contains("Fridge")), "Chronicle records the Fridge");
            await Wait(0.5);
            foreach (var layer in GetTree().Root.FindChildren("*", "CanvasLayer", true, false).Cast<CanvasLayer>()) layer.Visible = false;
            Rig.JumpTo(new Vector3(at.X, 0, at.Y)); Rig.JumpToYaw(0); Rig.JumpToDistance(16);
            await Wait(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "fridge-home.png")) == Error.Ok, "Captured the Fridge at home");
            lines.Add("Fridge rendered check passed.");
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error); GD.PushError(error.ToString()); GetTree().Quit(2);
        }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
