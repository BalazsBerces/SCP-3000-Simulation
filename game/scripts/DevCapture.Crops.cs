using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --crop-check=absolute-directory
    private async void CheckCrops(string directory)
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
            var people = Enumerable.Range(0, 2).Select(i => Host.Scenario.PlaceVillager(new(20 + i, 104))).ToArray();
            Host.Scenario.PlaceVillage(new(20, 104), people.Select(p => p.Id).ToArray());
            foreach (var person in people) Host.Scenario.AssignRole(person.Id, Role.Farmer);
            Host.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
            Host.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
            Host.Scenario.PlaceWaterPoint(new(26, 104));
            var tended = Host.Scenario.PlaceFurniture(FurnitureType.Crop, new(22, 108));
            Host.Scenario.PlaceFurniture(FurnitureType.Crop, new(23.5f, 108));
            Host.Scenario.PlaceFurniture(FurnitureType.PottedPlant, new(25, 108));
            Host.SkipTime(TimeSpan.FromHours(20));
            var crops = Host.Snapshot.Crops.ToDictionary(c => c.FurnitureId);
            Check(crops[tended.Id].Tended && crops[tended.Id].Growth > 0, "Farmers water and grow a Crop");
            await Wait(0.5);
            Rig.JumpTo(new Vector3(23.5f, 0, 108)); Rig.JumpToYaw(0.2f); Rig.JumpToDistance(5);
            await Wait(1);
            var labels = GetTree().Root.FindChildren("CropLabel", "Label3D", true, false).Cast<Label3D>().Select(l => l.Text).ToArray();
            Check(labels.Any(t => t.StartsWith("Crop")), $"Tended Crops show their growth: {string.Join(", ", labels)}");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "crops.png")) == Error.Ok, "Captured Crops beside a decorative potted plant at 1280 px");
            lines.Add("Crop rendered check passed.");
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error); GD.PushError(error.ToString()); GetTree().Quit(2);
        }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
