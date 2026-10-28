using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    private readonly Dictionary<(int Village, WorkshopKind Kind), Node3D> _workshops = [];

    private void ShowWorkshops()
    {
        var seen = new HashSet<(int, WorkshopKind)>();
        foreach (var village in Host.Snapshot.Villages)
            foreach (var workshop in village.Workshops.Where(w => w.Complete))
            {
                var key = (village.Id, workshop.Kind);
                seen.Add(key);
                if (!_workshops.TryGetValue(key, out var node))
                {
                    node = new Node3D { Name = $"Workshop{village.Id}{workshop.Kind}" };
                    var tool = workshop.Kind switch
                    {
                        WorkshopKind.Workbench => GearKind.Crowbar,
                        WorkshopKind.SewingTable => GearKind.Scissors,
                        _ => GearKind.Screwdriver,
                    };
                    node.AddChild(new MeshInstance3D { Mesh = GearMesh(tool), Rotation = new Vector3(Mathf.Pi / 2, 0, 0), Position = new Vector3(0, 0.82f, 0) });
                    node.AddChild(new Label3D { Text = workshop.Kind switch
                    {
                        WorkshopKind.Workbench => "Workbench",
                        WorkshopKind.SewingTable => "Sewing table",
                        _ => "Electrics bench",
                    }, Position = new Vector3(0, 1.5f, 0), FontSize = 32, PixelSize = 0.006f,
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
                    AddChild(node);
                    _workshops.Add(key, node);
                }
                var focus = Rig.Focus;
                var near = Host.Snapshot.Space.Delta(focus.ToStore(), workshop.Slot.Position);
                node.Position = new Vector3(focus.X + near.X, 0, focus.Z + near.Y);
            }
        foreach (var key in _workshops.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            _workshops[key].QueueFree();
            _workshops.Remove(key);
        }
    }
}
