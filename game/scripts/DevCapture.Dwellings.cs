using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --dwelling-check=absolute-directory
    private async void CheckDwellings(string directory)
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
            var ids = Enumerable.Range(0, 6).Select(i => Host.Scenario.PlaceVillager(new(20 + i, 104)).Id).ToArray();
            Host.Scenario.PlaceVillage(new(20, 104), ids);
            Host.Scenario.SetStores(0, GoodsKind.TinnedFood, 10000);
            Host.Scenario.SetStores(0, GoodsKind.BottledWater, 10000);
            Host.Scenario.AssignRole(0, Role.Builder);
            Host.Scenario.EquipGear(0, GearKind.ShoppingCart);
            var types = new[] { FurnitureType.Bookcase, FurnitureType.Bookcase, FurnitureType.Bookcase,
                FurnitureType.Wardrobe, FurnitureType.MeetingTable, FurnitureType.MeetingTable, FurnitureType.CurtainRack, FurnitureType.Rug };
            for (var i = 0; i < types.Length; i++) Host.Scenario.PlaceFurniture(types[i], new(10 + i * 3, 94), tint: i);
            Host.Scenario.PlanDwelling(0, DwellingKind.House);
            Host.Scenario.PlanDwelling(0, DwellingKind.Tent);
            for (var i = 0; i < 12000 && Host.Snapshot.Villages[0].Dwellings.Any(d => !d.Complete); i++)
                Host.SkipTime(StoreClock.TickDuration);
            Check(Host.Snapshot.Villages[0].Dwellings.All(d => d.Complete), "House and tent built by hauling and fitting existing Furniture");
            // Give the Builders each next barricade piece, without a pile blocking its approach to the gate.
            for (var i = 0; i < 20000 && !Host.Snapshot.Villages[0].HomeWalls().All(w => w.Complete); i++)
            {
                if (i % 500 == 0)
                {
                    Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(10));
                    foreach (var id in ids) Host.Scenario.SetNeeds(id, new Needs());
                }
                if (Host.Snapshot.Villagers[0].Hand is null) Host.Scenario.EquipGear(0, GearKind.ShoppingCart);
                if (Host.Snapshot.FurnitureTasks.Count == 0 && i % 50 == 0)
                {
                    var slot = Host.Snapshot.Villages[0].HomeWalls().SelectMany(w => w.Slots).First(s => s.FurnitureId is null);
                    var piece = new FurniturePiece(-1, FurnitureType.Wardrobe, default, 0, 2);
                    if (slot.Fits(piece)) Host.Scenario.PlaceFurniture(FurnitureType.Wardrobe, Host.Snapshot.Space.Wrap(slot.Position + System.Numerics.Vector2.Normalize(Host.Snapshot.Space.Delta(slot.Position, new(20, 104))) * 2), variant: 2, tint: i / 50 % 12);
                    else Host.Scenario.PlaceFurniture(FurnitureType.Dresser, Host.Snapshot.Space.Wrap(slot.Position + System.Numerics.Vector2.Normalize(Host.Snapshot.Space.Delta(slot.Position, new(20, 104))) * 2), tint: i / 50 % 12);
                }
                Host.SkipTime(StoreClock.TickDuration);
            }
            Check(Host.Snapshot.Villages[0].HomeWalls().All(w => w.Complete), "Dwellings stand inside a completed Furniture barricade: " + string.Join(", ", Host.Snapshot.Villages[0].HomeWalls().Select(w => $"{w.Slots.Count(s => s.FurnitureId is not null)}/{w.Slots.Count}")) + $"; Furniture {Host.Snapshot.Furniture.Count}, worker {Host.Snapshot.Villagers[0].Action}");
            Host.Scenario.PlaceFurniture(FurnitureType.Bed, new(22, 108));
            foreach (var id in ids)
            {
                Host.Scenario.AssignRole(id, Role.Guard);
                Host.Scenario.SetNeeds(id, new Needs(Rest: 0.1, Morale: 0.6));
                Host.Scenario.SendVillager(id, new(20, 104));
            }
            Host.SkipTime(TimeSpan.FromMinutes(45));
            Check(Host.Snapshot.Villagers.Count(v => v.SleepingPlace?.DwellingId == 0) == 2, "Two Villagers choose the house first");
            Check(Host.Snapshot.Villagers.Count(v => v.SleepingPlace?.DwellingId == 1) == 2, "Two take the tent");
            Check(Host.Snapshot.Villagers.Count(v => v.SleepingPlace?.FurnitureId is not null) == 1, "One takes the open bed");
            Check(Host.Snapshot.Villagers.Count(v => v.SleepingPlace is null) == 1, "Overflow sleeps on the floor");
            await Wait(0.5);
            Rig.JumpTo(new Vector3(15, 0, 102)); Rig.JumpToYaw(0.7f); Rig.JumpToDistance(28);
            var toggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>().Single(t => t.Text.StartsWith("Known map"));
            toggle.ButtonPressed = false;
            await Wait(1);
            ((Button)GetTree().Root.FindChild("InspectVillage0", true, false)).EmitSignal(Button.SignalName.Pressed);
            await Wait(1);
            Check(GetTree().Root.FindChild("Dwelling0_0", true, false) is Label3D { Text: var text } && text.Contains("House"), "House has a world-space sleeping label");
            var info = (Label)GetTree().Root.FindChild("VillageInfo", true, false);
            Check(info.Text.Contains("Sleeping places: 4/6") && info.Text.Contains("Communal"), "Village panel shows sleeping places and sharing");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "dwellings.png")) == Error.Ok, "Captured Dwellings and panel at 1280 px");
            GD.Print("Dwelling rendered check passed."); GetTree().Quit();
        }
        catch (Exception error) { lines.Add("FAIL " + error); GD.PushError(error.ToString()); GetTree().Quit(2); }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
