using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --blueprint-check=directory
    private async void CheckBlueprint(string directory)
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
            Check(Host.Snapshot.VillagerSandbox && Host.Snapshot.Villagers.Count == 0, "Empty Villager sandbox");
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            var a = Host.Scenario.PlaceVillager(new(30, 104));
            var b = Host.Scenario.PlaceVillager(new(31, 104));
            var village = Host.Scenario.PlaceVillage(a.Position, [a.Id, b.Id], Personality.Builders);
            Host.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 1000);
            Host.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 1000);
            var plan = Host.Scenario.PlanBarricade(village.Id);
            for (var i = 0; i < plan.Slots.Count; i++)
                Host.Scenario.PlaceFurniture(i % 3 == 0 ? FurnitureType.Armchair : i % 3 == 1 ? FurnitureType.Bookcase : FurnitureType.KitchenCabinet,
                    new(4 + i % 12 * 2, 80 + i / 12 * 3), variant: i % 3 == 2 ? 2 : 0, tint: i % 12);
            for (var i = 0; i < 1000 && !Host.Snapshot.FurnitureTasks.Any(t => t.Started); i++)
                Host.SkipTime(StoreClock.TickDuration);
            Check(Host.Snapshot.FurnitureTasks.Any(t => t.Started), "Builder fetches Furniture through the shared hauling path");
            await Wait(0.5);
            Rig.JumpTo(new Vector3(27, 0, 101)); Rig.JumpToYaw(0); Rig.JumpToDistance(27);
            await Wait(2);
            var toggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>()
                .Single(button => button.Text.StartsWith("Known map"));
            toggle.ButtonPressed = false;
            var task = Host.Snapshot.FurnitureTasks.Single();
            var piece = Host.Snapshot.Furniture.Single(p => p.Id == task.FurnitureId);
            var moving = GetTree().Root.FindChildren("Furniture", "MultiMeshInstance3D", true, false)
                .Cast<MultiMeshInstance3D>().Single(m => m.GetParent().Name == $"FurnitureWork{piece.Id}");
            Check(moving.Visible && moving.Multimesh.Mesh == FurnitureMeshes.For(piece.Type, piece.Variant), "Moving piece keeps its model");
            Check(moving.Multimesh.GetInstanceColor(0).IsEqualApprox(FurnitureMeshes.TintFor(piece)), "Moving piece keeps its own tint");
            async Task Capture(string name)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
                Check(image.SavePng(System.IO.Path.Combine(directory, name + ".png")) == Error.Ok, "Capture " + name);
            }
            await Capture("blueprint-fetching");
            var last = 0;
            for (var i = 0; i < 1000 && !Host.Snapshot.Villages[0].Walls[0].Complete; i++)
            {
                Host.SkipTime(TimeSpan.FromMinutes(15));
                var count = Host.Snapshot.Villages[0].Walls[0].Slots.Count(s => s.FurnitureId is not null);
                Check(count >= last, "Construction progress increases"); last = count;
            }
            var wall = Host.Snapshot.Villages[0].Walls[0];
            Check(wall.Complete, "Entire barricade completed one piece at a time");
            var built = Host.Snapshot.Furniture.Where(p => wall.Slots.Any(s => s.FurnitureId == p.Id)).ToArray();
            Check(built.Select(p => p.Type).Distinct().Count() == 3 && built.Select(p => p.Tint).Distinct().Count() > 1,
                "Completed wall keeps three Furniture models and individual tints");
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            await Wait(2);
            await Capture("blueprint-completed");
            lines.Add("Blueprint rendered check passed.");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
            GD.Print(lines[^1]); GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error);
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines);
            GD.PushError(error.ToString()); GetTree().Quit(3);
        }
    }
}
