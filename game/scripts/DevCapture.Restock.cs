using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // Run with --villager-sandbox --sandbox-preset=empty --pause --restock-check=directory after Godot's separate --.
    private async void CheckRestock(string directory)
    {
        var lines = new List<string>();
        try
        {
            void Check(bool passed, string message)
            {
                if (!passed) throw new InvalidOperationException(message);
                lines.Add($"PASS {message}");
            }
            System.IO.Directory.CreateDirectory(directory);
            Check(Host.Snapshot.VillagerSandbox && Host.Snapshot.Furniture.Count == 0, "Empty Villager sandbox loaded");
            Host.Speed = SimulationSpeed.Paused;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var shelf = Host.Scenario.PlaceFurniture(FurnitureType.GroceryShelf, new System.Numerics.Vector2(25, 100));
            var chair = Host.Scenario.PlaceFurniture(FurnitureType.Armchair, new System.Numerics.Vector2(22, 98));
            Host.Scenario.PlaceFurniture(FurnitureType.FloorLamp, new System.Numerics.Vector2(28, 100));
            Host.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 3);
            Host.Scenario.SetStock(shelf.Id, GoodsKind.TinnedFood, 0);
            Host.Scenario.RemoveFurniture(p => p.Id == chair.Id);
            await Wait(0.5);
            Rig.JumpTo(new Vector3(25, 0, 100));
            Rig.JumpToDistance(12);
            await Wait(2);
            async Task<byte[]> Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280)
                    image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, $"Captured {name}");
                using var scene = image.GetRegion(new Rect2I(300, 230, 550, 400));
                return scene.GetData();
            }
            var bare = await Capture("bare");
            Host.SkipTime(TimeSpan.FromHours(8));
            await Wait(0.5);
            Check(Host.Snapshot.Stock.Single(g => g.FurnitureId == shelf.Id).Amount == 1, "One unit refilled after eight Store hours");
            Check(!Host.Snapshot.Furniture.Contains(chair), "Furniture still absent before its delay");
            Host.SkipTime(TimeSpan.FromHours(16));
            await Wait(1);
            Check(Host.Snapshot.Stock.Single(g => g.FurnitureId == shelf.Id).Amount == 3, "Shelf refilled to three units");
            Check(Host.Snapshot.Furniture.Contains(chair), "Unobserved Furniture returned after 24 Store hours");
            var full = await Capture("restocked");
            Check(bare.Length == full.Length && bare.Zip(full).Count(p => Math.Abs(p.First - p.Second) > 10) > 100,
                "Scene pixels outside the HUD change after Goods refill and Furniture return");
            lines.Add("Restock rendered check passed.");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
            GD.Print(lines[^1]);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add($"FAIL {error}");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
            GD.PushError(error.ToString());
            GetTree().Quit(2);
        }
    }
    // Uses real runtime scheduling and the same overview click as an Observer.
    private async void CheckDrySpell(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            Host.Speed = SimulationSpeed.Paused;
            Host.HoldClearWeather();
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            for (var hour = 0; hour < 110 && Host.Snapshot.DrySpells.Count == 0; hour++)
                Host.SkipTime(TimeSpan.FromHours(1));
            var spell = Host.Snapshot.DrySpells.Single();
            var department = Host.Snapshot.Departments.All[spell.DepartmentId];
            var sector = Host.Snapshot.SectorAt(department.Anchor);
            StoreOverview.Set(true);
            await Wait(6);
            var inspector = GetTree().Root.FindChildren("*", "", true, false).OfType<SectorInspector>().Single();
            var button = inspector.GetNode<Button>("InspectSectors");
            button.ButtonPressed = true;
            inspector.Click(Rig.Camera.UnprojectPosition(department.Anchor.ToWorld()));
            await Wait(1);
            var label = inspector.GetNode<Label>("SectorPanel/SectorInfo");
            if (!label.Text.Contains($"#{department.Id}:") || !label.Text.Contains("Restock 12.5%"))
                throw new InvalidOperationException("Running Dry spell missing from selected Sector panel: " + label.Text);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280)
                image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            if (image.SavePng(System.IO.Path.Combine(directory, "dry-spell.png")) != Error.Ok)
                throw new System.IO.IOException("Dry spell screenshot failed.");
            var activeText = label.Text;
            Host.SkipTime(StoreClock.TickDuration * (spell.EndsAt.Tick - Host.Snapshot.Clock.Tick));
            await Wait(1);
            if (!label.Text.Contains("Dry spells: none"))
                throw new InvalidOperationException("Sector panel did not refresh when the Dry spell ended: " + label.Text);
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "report.txt"),
                "PASS Running Dry spell displayed in Sector " + sector.Name + "\n" + activeText +
                "\nPASS Selected panel refreshed to Dry spells: none after expiry.\n");
            GD.Print("Dry spell rendered check passed.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("Dry spell check failed: " + error);
            GetTree().Quit(2);
        }
    }
}
