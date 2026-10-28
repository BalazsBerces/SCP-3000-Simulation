using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --outpost-check=directory
    private async void CheckOutpost(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);
        var lines = new List<string>();
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            lines.Add("PASS " + message);
        }
        try
        {
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var people = Enumerable.Range(0, 8).Select(i => Host.Scenario.PlaceVillager(new(16 + i, 104))).ToArray();
            var village = Host.Scenario.PlaceVillage(new(20, 104), people.Select(p => p.Id).ToArray());
            Host.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 2000);
            Host.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 2000);
            var shelf = Host.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(76, 104));
            Host.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 2000);
            Host.Scenario.PlaceBand([village.Position], knownMap: new KnownMap
            {
                Villages = [new(village.Id, village.Position, Host.Snapshot.Clock)],
                Goods = [new(shelf.Position, new StockedGoods(shelf.Id, GoodsKind.TinnedFood, 2000), Host.Snapshot.Clock)],
            });
            for (var i = 0; i < 70; i++) Host.Scenario.PlaceFurniture(FurnitureType.Bookcase, new(90 + i % 8 * 2, 80 + i / 8 * 3), tint: i % 12);
            Host.SkipTime(TimeSpan.FromMinutes(20));
            Check(Host.Snapshot.Villages[0].Outposts.Count == 1, "Depleted surroundings send an Outpost near a known distant source");
            var post = Host.Snapshot.Villages[0].Outposts[0];
            for (var i = 0; i < 1000 && !Host.Snapshot.Villages[0].Walls[post.WallIndex].Complete; i++)
                Host.SkipTime(TimeSpan.FromMinutes(15));
            var wall = Host.Snapshot.Villages[0].Walls[post.WallIndex];
            Check(wall.Complete, "Outpost barricade built from fetched Furniture through Blueprint slots");
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(23));
            Host.SkipTime(TimeSpan.FromHours(1));
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            await Wait(0.5);
            var toggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>().Single(t => t.Text.StartsWith("Known map"));
            toggle.ButtonPressed = false;
            Rig.JumpTo(new Vector3(post.Position.X + 5, 0, post.Position.Y)); Rig.JumpToYaw(0); Rig.JumpToDistance(28);
            await Wait(2);
            Check(GetTree().Root.FindChildren("*", "Label3D", true, false).Cast<Label3D>()
                .Any(label => label.Text == "Village 0 Outpost 0"), "Outpost is labelled in the Store");
            var button = (Button)GetTree().Root.FindChild("InspectVillage0", true, false);
            button.EmitSignal(Button.SignalName.Pressed);
            await Wait(0.5);
            var info = (Label)GetTree().Root.FindChild("VillageInfo", true, false);
            Check(info.Text.Contains("Outpost 0") && info.Text.Contains("2 Villagers") && info.Text.Contains("barricade pieces"), "Village panel shows Outpost residents and construction");
            Check(Host.Snapshot.Chronicle.Any(s => s.Contains("Outpost 0")), "Chronicle records Outpost founding");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "outpost-panel.png")) == Error.Ok, "Captured Outpost barricade and Village panel");
            lines.Add("Outpost rendered check passed.");
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error); GD.PushError(error.ToString()); GetTree().Quit(2);
        }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
