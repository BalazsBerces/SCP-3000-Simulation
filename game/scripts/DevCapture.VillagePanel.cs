using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --village-panel-check=absolute-directory
    private async void CheckVillagePanel(string directory)
    {
        var lines = new List<string>();
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            void Check(bool passed, string message)
            {
                if (!passed) throw new InvalidOperationException(message);
                lines.Add("PASS " + message);
            }
            async Task Click(Vector2 point)
            {
                Input.ParseInputEvent(new InputEventMouseButton
                    { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
                await Wait(0.1);
                Input.ParseInputEvent(new InputEventMouseButton
                    { ButtonIndex = MouseButton.Left, Pressed = false, Position = point, GlobalPosition = point });
                await Wait(0.4);
            }
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Captured " + name);
            }
            Host.Speed = SimulationSpeed.Paused;
            Check(Host.Snapshot.VillagerSandbox && Host.Snapshot.Villagers.Count == 0, "Empty sandbox loaded");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var shelf = Host.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new(20, 108));
            Host.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 20);
            Host.Scenario.PlaceWaterPoint(new(22, 108));
            Host.Scenario.PlaceBand([new(20, 104), new(22, 104)]);
            Host.SkipTime(StoreClock.TickDuration);
            await Wait(0.5);
            var knownMapToggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>()
                .Single(b => b.Text.StartsWith("Known map"));
            knownMapToggle.ButtonPressed = false;
            Rig.JumpTo(new Vector3(20, 0, 104));
            Rig.JumpToDistance(28);
            await Wait(2);
            var panel = (PanelContainer)GetTree().Root.FindChild("VillagePanel", true, false);
            var info = (Label)GetTree().Root.FindChild("VillageInfo", true, false);
            var marker = (Button)GetTree().Root.FindChild("InspectVillage0", true, false);
            Check(!panel.Visible, "Village panel starts closed");
            await Click(marker.GetGlobalRect().GetCenter());
            Check(panel.Visible && info.Text.Contains("Population: 2"), "Clicking the Village marker opens live population");
            Check(info.Text.Contains("Stores\nEmpty") && info.Text.Contains("Wall condition") &&
                info.Text.Contains("Beliefs") && info.Text.Contains("Relations") && info.Text.Contains("History"),
                "Panel includes stores, wall condition, Beliefs, relations and history");
            Check(info.Text.Contains("Mara") && info.Text.Contains("founded Village 0") && info.Text.Contains("Sector A-1"),
                "Founding story fills in names and places");
            await Capture("founding");
            Host.SkipTime(TimeSpan.FromHours(1));
            await Wait(0.6);
            Check(info.Text.Contains("TinnedFood:") && !info.Text.Contains("Stores\nEmpty"), "Stores update while the panel stays open");
            await Capture("stores");
            var close = (Button)GetTree().Root.FindChild("CloseVillage", true, false);
            await Click(close.GetGlobalRect().GetCenter());
            Check(!panel.Visible, "Close button dismisses the panel");
            // Pick the ground itself, below the marker, through the regular input path.
            var home = Host.Snapshot.Villages[0].Position;
            var focus = Rig.Focus;
            var near = Host.Snapshot.Space.Delta(focus.ToStore(), home);
            await Click(Rig.Camera.UnprojectPosition(new Vector3(focus.X + near.X, 0, focus.Z + near.Y)));
            Check(panel.Visible, "Clicking the Village site opens its panel");
            Host.Scenario.RemoveFurniture(_ => true);
            // Occupy the shelf's old spot so Restock cannot bring its food supply back.
            Host.Scenario.PlaceFurniture(FurnitureType.Wardrobe, shelf.Position);
            Host.SkipTime(TimeSpan.FromHours(200));
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            await Wait(0.6);
            Check(info.Text.Contains("Population: 0") && info.Text.Contains("starvation") && info.Text.Contains("of Village 0"),
                "Population and Village death history update live");
            await Capture("deaths");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            await Wait(0.3);
            Check(!panel.Visible, "Escape dismisses the panel");
            marker.EmitSignal(Button.SignalName.Pressed);
            Host.LoadSandboxPreset("empty");
            for (var i = 0; Host.IsGenerating && i < 100; i++) await Wait(0.1);
            await Wait(0.5);
            Check(!Host.IsGenerating && !panel.Visible &&
                GetTree().Root.FindChild("InspectVillage0", true, false) is null, "Changing worlds clears Village selection and markers");
            lines.Add("Village panel rendered check passed.");
            GD.Print(lines[^1]);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error);
            GD.PushError(error.ToString());
            GetTree().Quit(2);
        }
        finally
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
        }
    }
}
