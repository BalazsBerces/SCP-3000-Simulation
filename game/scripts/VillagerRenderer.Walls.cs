using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    private static readonly Color StrapMetal = new(0.55f, 0.57f, 0.6f);
    private static readonly Color BackfillSoft = new(0.62f, 0.5f, 0.38f);
    private readonly Dictionary<int, (Node3D Node, int Signature)> _wallUpgrades = [];

    // Walls and houses share Strapping bands and Backfill; stacked pieces use their Blueprint elevation.
    // Damage is drawn by FurnitureRenderer (it darkens the piece). Generated boxes: plain bands and a slab are not models to search for.
    private void ShowWallUpgrades()
    {
        var world = Host.Snapshot;
        var seen = new HashSet<int>();
        foreach (var village in world.Villages)
        {
            var slots = village.HomeWalls().SelectMany(w => w.Slots).Concat(village.Dwellings.SelectMany(d => d.Slots)).Where(s => s.FurnitureId is not null && (s.Strapped || s.Backfilled)).ToArray();
            if (slots.Length == 0) continue;
            seen.Add(village.Id);
            if (!_wallUpgrades.TryGetValue(village.Id, out var entry))
            {
                entry.Node = new Node3D { Name = $"WallUpgrades{village.Id}" };
                AddChild(entry.Node);
                entry.Signature = 0;
            }
            var near = world.Space.Delta(Rig.Focus.ToStore(), village.Position);
            entry.Node.Position = new Vector3(Rig.Focus.X + near.X, 0, Rig.Focus.Z + near.Y);
            var signature = slots.Aggregate(17, (h, s) => HashCode.Combine(h, s.FurnitureId, s.Strapped, s.Backfilled));
            if (signature != entry.Signature)
            {
                foreach (var child in entry.Node.GetChildren()) child.QueueFree();
                foreach (var slot in slots)
                {
                    var spec = FurnitureCatalog.Spec(world.Furniture.First(p => p.Id == slot.FurnitureId));
                    var at = world.Space.Delta(village.Position, slot.Position);
                    var inward = at.LengthSquared() > 0 ? -System.Numerics.Vector2.Normalize(at) : System.Numerics.Vector2.Zero;
                    var turn = new Vector3(0, -slot.Rotation, 0);
                    if (slot.Strapped)
                        foreach (var height in new[] { 0.3f, 0.7f })
                            Add(entry.Node, new Vector3(spec.Width + 0.04f, 0.05f, spec.Depth + 0.04f), StrapMetal,
                                new Vector3(at.X, slot.Elevation + spec.Height * height, at.Y), turn, 0.3f);
                    if (slot.Backfilled)
                    {
                        var behind = at + inward * (spec.Depth / 2 + 0.12f);
                        Add(entry.Node, new Vector3(spec.Width, spec.Height * 0.8f, 0.2f), BackfillSoft,
                            new Vector3(behind.X, slot.Elevation + spec.Height * 0.4f, behind.Y), turn, 1f);
                    }
                }
                entry.Signature = signature;
            }
            _wallUpgrades[village.Id] = entry;
        }
        foreach (var id in _wallUpgrades.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _wallUpgrades[id].Node.QueueFree();
            _wallUpgrades.Remove(id);
        }

        static void Add(Node3D parent, Vector3 size, Color colour, Vector3 position, Vector3 rotation, float roughness) =>
            parent.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = roughness, Metallic = roughness < 0.5f ? 0.8f : 0f },
                Position = position, Rotation = rotation,
            });
    }
}
