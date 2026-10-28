using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --wall-check=directory
    private async void CheckWalls(string directory)
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
            var at = new System.Numerics.Vector2(30, 104);
            void Run(Func<Village, bool> done)
            {
                for (var i = 0; i < 4000 && !done(Host.Snapshot.Villages[0]); i++)
                {
                    foreach (var v in Host.Snapshot.Villagers.Where(v => v.VillageId == 0 && v.Hand is null))
                        Host.Scenario.EquipGear(v.Id, GearKind.ShoppingCart);
                    Host.SkipTime(TimeSpan.FromMinutes(5));
                }
            }
            var members = new[] { Host.Scenario.PlaceVillager(at), Host.Scenario.PlaceVillager(at + new System.Numerics.Vector2(1, 0)) };
            var village = Host.Scenario.PlaceVillage(at, members.Select(v => v.Id).ToArray(), Personality.Cautious);
            Host.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 4000);
            Host.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 4000);
            var slots = Host.Scenario.PlanBarricade(village.Id).Slots.Count;
            for (var i = 0; i < slots + 3; i++)
                Host.Scenario.PlaceFurniture(FurnitureType.Bookcase, at + new System.Numerics.Vector2(-22 + i % 12 * 2.2f, -24 + i / 12 * 2.5f), tint: i % 12);
            Run(v => v.Walls[0].Complete);
            Check(Host.Snapshot.Villages[0].Walls[0].Complete, "Barricade built");
            Host.Scenario.SetMaterials(village.Id, MaterialKind.Metal, 6);
            Host.Scenario.SetMaterials(village.Id, MaterialKind.Foam, 9);
            Run(v => v.Walls[0].Slots.Count(s => s.Strapped) >= 3 && v.Walls[0].Slots.Count(s => s.Backfilled) >= 3);
            var wall = Host.Snapshot.Villages[0].Walls[0];
            Check(wall.Slots.Count(s => s.Strapped) == 3 && wall.Slots.Count(s => s.Backfilled) == 3, "Three Strapped and three Backfilled pieces: a patchy wall");
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            // Damage shows on the pieces beside the reinforced ones.
            for (var i = 6; i < 14; i++)
                for (var blow = 0; blow < i % 3 + 1; blow++) Host.Scenario.StrikeWall(village.Id, 0, i);
            await Wait(1);
            Check(Host.Snapshot.Furniture.Any(p => p.Damage > 0f), "Struck pieces are damaged");
            var anchor = wall.Slots[1].Position;
            Rig.JumpTo(new Vector3(anchor.X, 0, anchor.Y)); Rig.JumpToYaw(0.6f); Rig.JumpToDistance(12);
            await Wait(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "walls.png")) == Error.Ok, "Capture walls");
            lines.Add("Wall tiers rendered check passed.");
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
