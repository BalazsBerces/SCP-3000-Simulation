using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    // Pulleys and winches are generated rigs (no usable CC0/CC-BY model found, see docs/ticket-30b-verification.md);
    // Caravan carts reuse the platform cart model.
    private static readonly StandardMaterial3D RigWood = new() { AlbedoColor = new Color(0.45f, 0.3f, 0.16f), Roughness = 0.9f };
    private static readonly StandardMaterial3D RigIron = new() { AlbedoColor = new Color(0.3f, 0.3f, 0.33f), Metallic = 0.7f, Roughness = 0.5f };
    private readonly Dictionary<int, Node3D> _hoistRigs = [];
    private readonly Dictionary<int, Node3D> _trainCarts = [];

    private static MeshInstance3D Part(Mesh mesh, StandardMaterial3D material, Vector3 at, Vector3? rotation = null) =>
        new() { Mesh = mesh, MaterialOverride = material, Position = at, RotationDegrees = rotation ?? Vector3.Zero };

    private static Node3D BuildRig(bool winch)
    {
        var rig = new Node3D();
        rig.AddChild(Part(new BoxMesh { Size = new Vector3(0.15f, 3, 0.15f) }, RigWood, new Vector3(0, 1.5f, 0)));
        rig.AddChild(Part(new BoxMesh { Size = new Vector3(1.6f, 0.12f, 0.12f) }, RigWood, new Vector3(0.6f, 3, 0)));
        rig.AddChild(Part(new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.2f, Height = 0.08f }, RigIron, new Vector3(1.2f, 2.85f, 0), new Vector3(90, 0, 0)));
        rig.AddChild(Part(new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.01f, Height = 2.3f }, RigIron, new Vector3(1.2f, 1.7f, 0)));
        if (winch)
        {
            rig.AddChild(Part(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 0.6f }, RigIron, new Vector3(0.3f, 0.5f, 0), new Vector3(90, 0, 0)));
            rig.AddChild(Part(new BoxMesh { Size = new Vector3(0.5f, 0.06f, 0.06f) }, RigIron, new Vector3(0.3f, 0.5f, 0.4f)));
        }
        return rig;
    }

    private void ShowHauling(StoreSpace space, Vector3 focus)
    {
        var world = Host.Snapshot;
        Vector3 At(System.Numerics.Vector2 p, float y)
        {
            var near = space.Delta(focus.ToStore(), p);
            return new Vector3(focus.X + near.X, y, focus.Z + near.Y);
        }
        var seenRigs = new HashSet<int>();
        foreach (var village in world.Villages)
        {
            var winch = village.Gear.Any(g => g.Kind == GearKind.Winch);
            if (!winch && !village.Gear.Any(g => g.Kind == GearKind.Pulley)) continue;
            seenRigs.Add(village.Id);
            if (!_hoistRigs.TryGetValue(village.Id, out var rig))
            {
                rig = BuildRig(winch);
                rig.Name = $"HoistRig{village.Id}";
                AddChild(rig);
                _hoistRigs[village.Id] = rig;
            }
            rig.Position = At(village.Position + new System.Numerics.Vector2(-3, 3), 0);
        }
        Prune(_hoistRigs, seenRigs);
        var seenCarts = new HashSet<int>();
        foreach (var caravan in world.Caravans)
        {
            var heading = space.Delta(caravan.Position, caravan.Route[caravan.Next]);
            var back = heading.LengthSquared() > 0.01f ? -System.Numerics.Vector2.Normalize(heading) : new System.Numerics.Vector2(-1, 0);
            for (var i = 0; i < caravan.Carts; i++)
            {
                var key = caravan.Id * 4 + i;
                seenCarts.Add(key);
                if (!_trainCarts.TryGetValue(key, out var cart))
                {
                    cart = new Node3D { Name = $"TrainCart{key}" };
                    cart.AddChild(new MeshInstance3D { Mesh = GearMesh(GearKind.PlatformCart) });
                    AddChild(cart);
                    _trainCarts[key] = cart;
                }
                cart.Position = At(caravan.Position + back * (1.5f + 1.8f * i), 0);
                cart.Rotation = new Vector3(0, -MathF.Atan2(heading.Y, heading.X), 0);
            }
        }
        Prune(_trainCarts, seenCarts);
    }
}
