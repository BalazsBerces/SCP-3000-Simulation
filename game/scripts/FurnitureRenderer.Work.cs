using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class FurnitureRenderer
{
    private readonly Dictionary<int, Node3D> _work = [];

    private void ShowFurnitureWork(WorldSnapshot world)
    {
        foreach (var task in world.FurnitureTasks)
        {
            var piece = world.Furniture.FirstOrDefault(p => p.Id == task.FurnitureId);
            if (!_work.TryGetValue(piece.Id, out var node))
            {
                node = new Node3D { Name = $"FurnitureWork{piece.Id}" };
                // The static groups rebuild independently of these moving pieces.
                GetParent().AddChild(node);
                node.AddChild(new MeshInstance3D { Name = "Furniture" });
                node.AddChild(new Label3D { Name = "Work", FontSize = 40, PixelSize = 0.015f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
                _work.Add(piece.Id, node);
            }
            var near = world.Space.Delta(Rig.Focus.ToStore(), piece.Position);
            node.Position = new Vector3(Rig.Focus.X + near.X, piece.Elevation, Rig.Focus.Z + near.Y);
            var mesh = node.GetNode<MeshInstance3D>("Furniture");
            mesh.Visible = task.Started && task.Work != FurnitureWork.Disassemble;
            if (mesh.Visible)
            {
                mesh.Mesh = FurnitureMeshes.For(piece.Type, piece.Variant, piece.LandmarkId >= 0);
                mesh.Transform = Placement(piece with { Position = System.Numerics.Vector2.Zero, Elevation = 0 });
            }
            var label = node.GetNode<Label3D>("Work");
            label.Position = new Vector3(0, FurnitureCatalog.Spec(piece).Height + 0.6f, 0);
            label.Text = !task.Started ? "Approaching Furniture" : task.Work switch
            {
                FurnitureWork.Disassemble => $"Disassembling {task.Progress:P0}",
                FurnitureWork.TeamCarry => $"Team carrying ({task.VillagerIds.Count})",
                FurnitureWork.CartHaul => "Cart hauling",
                FurnitureWork.Drag => "Dragging (noisy)",
                _ => "Carrying",
            };
        }
        foreach (var gone in _work.Keys.Where(id => !world.FurnitureTasks.Any(t => t.FurnitureId == id)).ToArray())
        {
            _work[gone].QueueFree();
            _work.Remove(gone);
        }
    }
}
