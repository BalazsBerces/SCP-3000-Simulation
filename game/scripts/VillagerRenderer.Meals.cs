using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    private static readonly string MealTimes = string.Join(" · ", MessHall.MealHours.Select(h => $"{h:00}:00"));
    private readonly Dictionary<int, (Node3D Node, int? TableId)> _messHalls = [];

    private void ShowMessHalls()
    {
        var world = Host.Snapshot;
        var seen = new HashSet<int>();
        foreach (var village in world.Villages)
        {
            if (village.MessHall is not { Complete: true } hall) continue;
            seen.Add(village.Id);
            if (!_messHalls.TryGetValue(village.Id, out var shown))
            {
                var created = new Node3D { Name = $"MessHall{village.Id}" };
                created.AddChild(new Label3D { Name = "MealLabel", Position = new Vector3(0, 2.5f, 0),
                    FontSize = 32, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
                var plates = new Node3D { Name = "MealPlates" };
                for (var i = 0; i < 4; i++)
                    plates.AddChild(new MeshInstance3D { Mesh = FurnitureMeshes.For(FurnitureType.MealPlate),
                        Position = new Vector3(i % 2 == 0 ? -0.4f : 0.4f, 0.02f, i < 2 ? -0.2f : 0.2f) });
                created.AddChild(plates);
                AddChild(created);
                shown = (created, null);
            }
            // A different table can complete the slot between two frames; the plates follow its height.
            if (shown.TableId != hall.Slots[0].FurnitureId)
            {
                var table = world.Furniture.Single(p => p.Id == hall.Slots[0].FurnitureId);
                shown.Node.GetNode<Node3D>("MealPlates").Position = new Vector3(0, FurnitureCatalog.Spec(table).Height, 0);
                shown = (shown.Node, table.Id);
            }
            _messHalls[village.Id] = shown;
            var node = shown.Node;
            var diners = world.Villagers.Count(v => v.VillageId == village.Id && v.Action == VillagerAction.SharedMeal);
            node.GetNode<Label3D>("MealLabel").Text = diners > 0 ? $"Mess hall — {diners} eating together" : $"Mess hall | {MealTimes}";
            node.GetNode<Node3D>("MealPlates").Visible = diners > 0;
            var near = world.Space.Delta(Rig.Focus.ToStore(), hall.Position);
            node.Position = new Vector3(Rig.Focus.X + near.X, 0, Rig.Focus.Z + near.Y);
        }
        foreach (var id in _messHalls.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _messHalls[id].Node.QueueFree();
            _messHalls.Remove(id);
        }
    }
}
