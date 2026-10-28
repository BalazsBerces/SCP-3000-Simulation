using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --meal-check=absolute-directory
    private async void CheckMeals(string directory)
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
            var people = Enumerable.Range(0, 4).Select(i => Host.Scenario.PlaceVillager(new(20 + i, 104))).ToArray();
            Host.Scenario.PlaceVillage(new(20, 104), people.Select(p => p.Id).ToArray());
            Host.Scenario.SetStores(0, GoodsKind.TinnedFood, 1000);
            Host.Scenario.SetStores(0, GoodsKind.BottledWater, 1000);
            for (var i = 0; i < 4; i++) Host.Scenario.PlaceFurniture(FurnitureType.DiningTable, new(28 + i * 3, 104));
            Host.Scenario.PlaceFurniture(FurnitureType.Chair, new(28, 110));
            Host.Scenario.PlaceFurniture(FurnitureType.HallBench, new(31, 110));
            Host.Scenario.PlaceFurniture(FurnitureType.KitchenCabinet, new(35, 110));
            for (var i = 0; i < 10000 && Host.Snapshot.Villages[0].MessHall?.Complete != true; i++)
                Host.SkipTime(StoreClock.TickDuration);
            Check(Host.Snapshot.Villages[0].MessHall?.Complete == true, "Mess hall built by hauling four Blueprint pieces");
            foreach (var v in people)
            {
                Host.Scenario.SetNeeds(v.Id, new Needs(Morale: 0.3));
                Host.Scenario.AssignRole(v.Id, v.Id == 2 ? Role.Cook : Role.Guard);
                Host.Scenario.SendVillager(v.Id, new(20, 104));
            }
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(16));
            Host.SkipTime(TimeSpan.FromHours(2));
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(20));
            Host.SkipTime(TimeSpan.FromMinutes(35));
            Check(Host.Snapshot.Villagers.All(v => v.Action == VillagerAction.SharedMeal), "Four Villagers eat together before Closing time");
            await Wait(0.5);
            Rig.JumpTo(new Vector3(22, 0, 101)); Rig.JumpToYaw(0.2f); Rig.JumpToDistance(22);
            await Wait(1);
            var toggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>().Single(t => t.Text.StartsWith("Known map"));
            toggle.ButtonPressed = false;
            var button = (Button)GetTree().Root.FindChild("InspectVillage0", true, false);
            button.EmitSignal(Button.SignalName.Pressed);
            await Wait(1);
            var node = GetTree().Root.FindChild("MessHall0", true, false) as Node3D;
            Check(node is not null && node.GetNode<Node3D>("MealPlates").Visible, "Meal plates visible on the existing table model");
            Check(node!.GetNode<Label3D>("MealLabel").Text.Contains("4 eating together"), "Mess hall marks the shared meal in world space");
            var info = (Label)GetTree().Root.FindChild("VillageInfo", true, false);
            Check(info.Text.Contains("Mess hall\nBuilt") && info.Text.Contains("Eating together: 4"), "Village panel lists Mess hall, schedule and diners");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "shared-meal.png")) == Error.Ok, "Captured shared meal and Village panel at 1280 px");
            lines.Add("Mess hall rendered check passed.");
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error); GD.PushError(error.ToString()); GetTree().Quit(2);
        }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
