using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    // Staff remains are the Staff's own mannequin figure (ADR 0006: reused), lying down and darkened so it reads as dead.
    private static readonly StandardMaterial3D RemainsMaterial = new() { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Roughness = 0.9f };
    private readonly Dictionary<int, MeshInstance3D> _remains = [];

    private void ShowStaffRemains(StoreSpace space, Vector3 focus)
    {
        var seen = new HashSet<int>();
        foreach (var remains in Host.Snapshot.StaffRemains)
        {
            seen.Add(remains.Id);
            if (!_remains.TryGetValue(remains.Id, out var figure))
            {
                figure = new MeshInstance3D
                {
                    Mesh = FurnitureMeshes.For(FurnitureType.Mannequin), Name = $"StaffRemains{remains.Id}",
                    MaterialOverride = RemainsMaterial, Rotation = new Vector3(MathF.PI / 2, remains.Id * 1.3f, 0),
                };
                AddChild(figure);
                _remains[remains.Id] = figure;
            }
            var near = space.Delta(focus.ToStore(), remains.Position);
            figure.Position = new Vector3(focus.X + near.X, remains.Carried ? 0.6f : 0.2f, focus.Z + near.Y);
        }
        foreach (var gone in _remains.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _remains[gone].QueueFree();
            _remains.Remove(gone);
        }
    }
}
