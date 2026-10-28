using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --storage-check=absolute-directory
    private async void CheckStorage(string directory)
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
            var people = Enumerable.Range(0, 3).Select(i => Host.Scenario.PlaceVillager(new(20 + i, 104))).ToArray();
            Host.Scenario.PlaceVillage(new(20, 104), people.Select(p => p.Id).ToArray());
            Host.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
            Host.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
            for (var i = 0; i < 3; i++) Host.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(28 + i * 3, 110));
            Host.Scenario.PlaceFurniture(FurnitureType.Chair, new(30, 99));
            for (var i = 0; i < 12000 && Host.Snapshot.Villages[0].Storehouse?.Complete != true; i++)
                Host.SkipTime(StoreClock.TickDuration);
            Check(Host.Snapshot.Villages[0].Storehouse?.Complete == true, "Storehouse built by hauling three shelves");
            Host.Scenario.SetStores(0, GoodsKind.TinnedFood, 30);
            Host.Scenario.SetStores(0, GoodsKind.FreshFood, 40);
            Host.Scenario.SetStores(0, GoodsKind.BottledWater, 0);
            Host.SkipTime(TimeSpan.FromHours(6));
            Check(Host.Snapshot.Villages[0].PileTotal > 0, "Stores beyond the Storehouse capacity lie in the open pile");
            await Wait(0.5);
            Rig.JumpTo(new Vector3(20, 0, 104)); Rig.JumpToYaw(0.2f); Rig.JumpToDistance(22);
            await Wait(1);
            var toggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>().Single(t => t.Text.StartsWith("Known map"));
            toggle.ButtonPressed = false;
            var button = (Button)GetTree().Root.FindChild("InspectVillage0", true, false);
            button.EmitSignal(Button.SignalName.Pressed);
            await Wait(1);
            var node = GetTree().Root.FindChild("Storage0", true, false) as Node3D;
            Check(node is not null && node.GetNode<Label3D>("StorehouseLabel").Visible && node.GetNode<Label3D>("StorehouseLabel").Text.StartsWith("Storehouse"), "Storehouse marked in world space");
            Check(node!.GetNode<Node3D>("PileBoxes").Visible && node.GetNode<Label3D>("PileLabel").Text.StartsWith("Open pile"), "Open pile shown with existing box model");
            Check(node.GetNode<Label3D>("YardLabel").Visible, "Furniture yard marked in world space");
            var info = (Label)GetTree().Root.FindChild("VillageInfo", true, false);
            Check(info.Text.Contains("Storage\nStorehouse built | capacity 36") && info.Text.Contains("FreshFood") && info.Text.Contains("spoils fast"), "Village panel lists stores, capacity and spoiling food");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "storage.png")) == Error.Ok, "Captured Storehouse, pile, yard and Village panel at 1280 px");
            lines.Add("Storage rendered check passed.");
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error); GD.PushError(error.ToString()); GetTree().Quit(2);
        }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
