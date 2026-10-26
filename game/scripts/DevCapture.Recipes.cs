using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --recipe-check=absolute-directory
    private async void CheckRecipes(string directory)
    {
        var lines = new List<string>();
        System.IO.Directory.CreateDirectory(directory);
        void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
            lines.Add("PASS " + message);
        }
        try
        {
            Host.Speed = SimulationSpeed.Paused;
            DisplayServer.WindowSetSize(new Vector2I(1280, 720));
            Check(Host.Snapshot.VillagerSandbox && Host.Snapshot.Villagers.Count == 0, "Empty sandbox loaded");
            var worker = Host.Scenario.PlaceVillager(new(20, 104));
            Host.Scenario.PlaceVillage(worker.Position, [worker.Id]);
            Host.Scenario.EquipGear(worker.Id, GearKind.Scissors);
            Host.Scenario.SetMaterials(0, MaterialKind.Fabric, 4);
            Host.Scenario.MakeGear(worker.Id, GearKind.CanvasBag);
            Host.SkipTime(TimeSpan.FromMinutes(31));
            Check(Host.Snapshot.Villages[0].Gear.Any(g => g.Kind == GearKind.CanvasBag) || Host.Snapshot.Villagers[0].Bag?.Kind == GearKind.CanvasBag, "Canvas bag made from fabric with scissors");
            var kinds = new[] { GearKind.TableLegClub, GearKind.Candle, GearKind.CardboardArmour, GearKind.Bandage };
            for (var i = 0; i < kinds.Length; i++)
            {
                var person = Host.Scenario.PlaceVillager(new(23 + i * 2, 104));
                Host.Scenario.EquipGear(person.Id, kinds[i]);
                var mesh = VillagerRenderer.GearMesh(kinds[i]);
                Check(mesh.GetAabb().Size.Length() > 0.05f && mesh.GetSurfaceCount() > 0, $"Reused model renders {kinds[i]}");
            }
            await Wait(0.5);
            var knownMapToggle = GetTree().Root.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>()
                .Single(b => b.Text.StartsWith("Known map"));
            knownMapToggle.ButtonPressed = false;
            Rig.JumpTo(new Vector3(25, 0, 104));
            Rig.JumpToDistance(14);
            await Wait(2);
            var marker = (Button)GetTree().Root.FindChild("InspectVillage0", true, false);
            marker.EmitSignal(Button.SignalName.Pressed);
            await Wait(0.5);
            var panel = (PanelContainer)GetTree().Root.FindChild("VillagePanel", true, false);
            var info = (Label)GetTree().Root.FindChild("VillageInfo", true, false);
            Check(panel.Visible && Recipes.Basic.All(r => info.Text.Contains(r.Name)), "Panel lists all five basic Recipes");
            Check(info.Text.Contains("Skills") && info.Text.Contains("Mara:") && info.Text.Contains($"Builder {0.1:0.0}"), "Panel lists personal Builder skill after practice");
            Check(info.Text.Contains("first Gear") && info.Text.Contains("Canvas bag"), "Panel includes first Gear history");
            info.GetParent<ScrollContainer>().ScrollVertical = 260;
            await Wait(0.3);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "recipes-skills.png")) == Error.Ok, "Captured skills and Gear at <=1280 px");
            lines.Add("Recipe rendered check passed.");
            GD.Print(lines[^1]);
            GetTree().Quit();
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error);
            GD.PushError(error.ToString());
            GetTree().Quit(2);
        }
        finally { System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "report.txt"), lines); }
    }
}
