using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    private const int PileBoxes = 6;
    private readonly Dictionary<int, Node3D> _storage = [];

    // Storehouse shelves and yard pieces are real Furniture; this adds their labels and the open pile (existing cardboard box model).
    private void ShowStorage()
    {
        var world = Host.Snapshot;
        var seen = new HashSet<int>();
        foreach (var village in world.Villages)
        {
            if (village.Storehouse is null && village.FurnitureYard is null) continue;
            seen.Add(village.Id);
            if (!_storage.TryGetValue(village.Id, out var node))
            {
                node = new Node3D { Name = $"Storage{village.Id}" };
                foreach (var name in new[] { "StorehouseLabel", "PileLabel", "YardLabel" })
                    node.AddChild(new Label3D { Name = name, FontSize = 32, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
                var pile = new Node3D { Name = "PileBoxes" };
                for (var i = 0; i < PileBoxes; i++)
                    pile.AddChild(new MeshInstance3D { Mesh = FurnitureMeshes.For(FurnitureType.CardboardBox),
                        Position = new Vector3(i % 3 * 0.7f - 0.7f, 0, i / 3 * 0.7f - 0.35f) });
                node.AddChild(pile);
                AddChild(node);
                _storage[village.Id] = node;
            }
            var near = world.Space.Delta(Rig.Focus.ToStore(), village.Position);
            node.Position = new Vector3(Rig.Focus.X + near.X, 0, Rig.Focus.Z + near.Y);
            Place(node.GetNode<Label3D>("StorehouseLabel"), world, village, village.Storehouse is null ? null : new System.Numerics.Vector2(-3.8f, 0), 2.2f,
                village.Storehouse is { } house ? $"Storehouse {village.StoresTotal - village.PileTotal}/{house.Capacity}" : null);
            var pileOffset = new System.Numerics.Vector2(-6.5f, 0);
            var pileBoxes = node.GetNode<Node3D>("PileBoxes");
            pileBoxes.Position = new Vector3(pileOffset.X, 0, pileOffset.Y);
            pileBoxes.Visible = village.PileTotal > 0;
            for (var i = 0; i < PileBoxes; i++)
                pileBoxes.GetChild<Node3D>(i).Visible = i < Math.Min(PileBoxes, 1 + village.PileTotal / 10);
            Place(node.GetNode<Label3D>("PileLabel"), world, village, pileOffset, 1.6f,
                village.PileTotal > 0 ? $"Open pile: {village.PileTotal} (spoils {Spoilage.PileFactor}× faster)" : null);
            Place(node.GetNode<Label3D>("YardLabel"), world, village, village.FurnitureYard is { } yard ? world.Space.Delta(village.Position, yard.Position) : null, 2.2f,
                village.FurnitureYard is { } y ? $"Furniture yard ({(y.Inside ? "inside" : "outside")})" : null);
        }
        foreach (var id in _storage.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _storage[id].QueueFree();
            _storage.Remove(id);
        }
    }

    private static void Place(Label3D label, WorldSnapshot world, Village village, System.Numerics.Vector2? offset, float height, string? text)
    {
        label.Visible = text is not null && offset is not null;
        if (!label.Visible) return;
        label.Text = text;
        label.Position = new Vector3(offset!.Value.X, height, offset.Value.Y);
    }

    /// <summary>The Village panel lines for stores, capacity and spoiling food.</summary>
    private static string StorageText(Village village)
    {
        var housed = village.Storehouse is not { } house ? "No Storehouse" :
            $"Storehouse {(house.Complete ? "built" : $"{house.Slots.Count(s => s.FurnitureId is not null)}/3 shelves")} | capacity {house.Capacity}";
        var pile = village.PileGoods();
        var spoiling = village.Stores.Where(g => g.Value > 0 && GoodsCatalog.ClassOf(g.Key) == GoodsClass.Food).OrderBy(g => g.Key)
            .Select(g => $"{g.Key}: {g.Value} ({(GoodsCatalog.SpoilsFast(g.Key) ? "spoils fast" : "spoils slowly")}" +
                (pile.GetValueOrDefault(g.Key) is > 0 and var open ? $", {open} in the open pile)" : ")")).ToArray();
        var yard = village.FurnitureYard is not { } y ? "No Furniture yard" : $"Furniture yard {(y.Inside ? "inside the walls" : "outside, with its own barricade")}";
        return $"{housed}\nStores {village.StoresTotal} | open pile {village.PileTotal}\n{yard}\nSpoiling food\n" +
            (spoiling.Length == 0 ? "None" : string.Join("\n", spoiling));
    }
}
