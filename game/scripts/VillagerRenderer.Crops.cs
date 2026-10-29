using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    // Tended Crops this close to the focus show their growth (ticket 17f); the Store's untended ones are just pots.
    private const float CropDistance = 30;
    private static readonly Color Sprouting = new(0.45f, 0.75f, 0.3f), Ripe = new(0.95f, 0.8f, 0.2f), Withered = new(0.45f, 0.3f, 0.15f);
    private readonly Dictionary<int, Node3D> _crops = [];
    private IReadOnlyList<FurniturePiece>? _cropFurniture;
    private Dictionary<int, FurniturePiece> _cropPieces = [];

    private void ShowCrops()
    {
        var world = Host.Snapshot;
        var focus = Rig.Focus.ToStore();
        if (!ReferenceEquals(_cropFurniture, world.Furniture))
        {
            _cropFurniture = world.Furniture;
            _cropPieces = world.Furniture.Where(p => p.Type == FurnitureType.Crop).ToDictionary(p => p.Id);
        }
        var seen = new HashSet<int>();
        foreach (var crop in world.Crops)
        {
            if (!crop.Tended || !_cropPieces.TryGetValue(crop.FurnitureId, out var piece)) continue;
            var near = world.Space.Delta(focus, piece.Position);
            if (near.Length() > CropDistance) continue;
            seen.Add(crop.FurnitureId);
            if (!_crops.TryGetValue(crop.FurnitureId, out var node))
            {
                node = new Node3D { Name = $"Crop{crop.FurnitureId}" };
                node.AddChild(new MeshInstance3D { Name = "Head", Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f },
                    MaterialOverride = new StandardMaterial3D() });
                node.AddChild(new Label3D { Name = "CropLabel", Position = new Vector3(0, 0.75f, 0), FontSize = 24, PixelSize = 0.005f,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
                AddChild(node);
                _crops[crop.FurnitureId] = node;
            }
            // The head on the pot swells as the Crop grows and turns brown when it withers.
            var head = node.GetNode<MeshInstance3D>("Head");
            head.Position = new Vector3(0, FurnitureCatalog.Spec(piece).Height + 0.05f, 0);
            head.Scale = Vector3.One * (crop.Withered ? 0.6f : 0.4f + crop.Growth);
            ((StandardMaterial3D)head.MaterialOverride).AlbedoColor = crop.Withered ? Withered : crop.Ripe ? Ripe : Sprouting.Lerp(Ripe, crop.Growth * 0.5f);
            node.GetNode<Label3D>("CropLabel").Text = crop.Withered ? "Crop withered" : crop.Ripe ? "Crop ripe" :
                $"Crop {crop.Growth:P0}{(crop.Water > 0 ? "" : " · dry")}";
            node.Position = new Vector3(Rig.Focus.X + near.X, piece.Elevation, Rig.Focus.Z + near.Y);
        }
        foreach (var id in _crops.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _crops[id].QueueFree();
            _crops.Remove(id);
        }
    }
}
