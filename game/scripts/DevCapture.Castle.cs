using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class DevCapture
{
    // --villager-sandbox --sandbox-preset=empty --pause --castle-check=directory
    private async void CheckCastle(string directory)
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
            void Supply(System.Numerics.Vector2 from, FurnitureType type, int count, int variant = 0)
            {
                for (var i = 0; i < count; i++)
                    Host.Scenario.PlaceFurniture(type, from + new System.Numerics.Vector2(i % 12 * 2.2f, i / 12 * 2.5f), variant: variant, tint: i % 12);
            }
            void Run(Func<Village, bool> done)
            {
                for (var i = 0; i < 4000 && !done(Host.Snapshot.Villages[0]); i++)
                {
                    foreach (var v in Host.Snapshot.Villagers.Where(v => v.VillageId == 0 && v.Hand is null))
                        Host.Scenario.EquipGear(v.Id, GearKind.ShoppingCart);
                    Host.SkipTime(TimeSpan.FromMinutes(5));
                }
            }
            // The facade test's Village (CastleTests), with the fountain court beside it so its Moat holds water.
            var members = new[] { Host.Scenario.PlaceVillager(at), Host.Scenario.PlaceVillager(at + new System.Numerics.Vector2(1, 0)) };
            var village = Host.Scenario.PlaceVillage(at, members.Select(v => v.Id).ToArray(), Personality.Cautious);
            Host.Scenario.SetStores(village.Id, GoodsKind.TinnedFood, 4000);
            Host.Scenario.SetStores(village.Id, GoodsKind.BottledWater, 4000);
            Supply(at + new System.Numerics.Vector2(-22, -24), FurnitureType.Bookcase, Host.Scenario.PlanBarricade(village.Id).Slots.Count + 3);
            Run(v => v.Walls[0].Complete);
            Check(Host.Snapshot.Villages[0].Walls[0].Complete, "Barricade built");
            Host.Scenario.PlaceWaterPoint(at + new System.Numerics.Vector2(0, -20));
            for (var i = 0; i < 4; i++)
                Host.Scenario.PlaceBand([at], knownMap: new KnownMap { Villages = [new KnownVillage(village.Id, at, Host.Snapshot.Clock)] });
            Run(v => v.HomeWalls().Count() == 2);
            var curtain = Host.Snapshot.Villages[0].HomeWalls().ElementAt(1);
            Check(curtain.Battlements && Host.Snapshot.Villages[0].Castle.Count == 6, "Curtain wall planned with four Towers, a Gatehouse and a Keep");
            var talls = curtain.Slots.Count(s => s.MinimumHeight >= 1.8f);
            Supply(new(8, 120), FurnitureType.Bookcase, talls + 6);
            Supply(new(8, 130), FurnitureType.KitchenCabinet, curtain.Slots.Count - talls + 2, variant: 1);
            Supply(new(36, 120), FurnitureType.ShelfUnit, 8);
            Supply(new(36, 128), FurnitureType.TableLamp, 4);
            Supply(new(36, 132), FurnitureType.Wardrobe, 1, variant: 1);
            Supply(new(36, 136), FurnitureType.Dresser, 12);
            Run(v => v.Castle.All(c => c.Complete));
            var castle = Host.Snapshot.Villages[0];
            Check(castle.Castle.All(c => c.Complete), "Towers, Gatehouse and Keep built");
            Check(castle.Moats.Single().Water, "Moat beside the fountain court holds water");
            Host.Scenario.SetTimeOfDay(TimeSpan.FromHours(12));
            await Wait(1);
            var banners = GetTree().Root.FindChildren("Banners", "MultiMeshInstance3D", true, false).Cast<MultiMeshInstance3D>().Single();
            Check(banners.Multimesh.InstanceCount == 7, "A banner over the centre and each Tower, Gatehouse and Keep");
            Check(banners.Multimesh.GetInstanceColor(0).IsEqualApprox(FurnitureMeshes.TintFor(FurnitureType.CampBanner, castle.Heraldry!.Field)),
                "Banners fly the Village's field tint");
            Rig.JumpTo(new Vector3(at.X, 0, at.Y)); Rig.JumpToYaw(0.6f); Rig.JumpToDistance(30);
            await Wait(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.GetWidth() > 1280) image.Resize(1280, image.GetHeight() * 1280 / image.GetWidth());
            Check(image.SavePng(System.IO.Path.Combine(directory, "castle.png")) == Error.Ok, "Capture castle");
            lines.Add($"Castle rendered check passed: {castle.Heraldry}.");
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
