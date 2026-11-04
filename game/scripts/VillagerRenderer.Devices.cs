using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    // Noise lures are downloaded models (a Poly Haven boombox for the radio, a Sketchfab music box); an alarm is three of the
    // tinned-food Goods models (ADR 0006: reused) on a string; a pit is a dark hole in the floor. A Furniture trap is its own
    // Furniture, drawn by the FurnitureRenderer. Generated (nothing found, see docs/ticket-28a-verification.md): the trip-wire
    // string and the pit's hole.
    private static readonly StandardMaterial3D PitMaterial = new() { AlbedoColor = new Color(0.03f, 0.03f, 0.03f), Roughness = 1f };
    private static readonly StandardMaterial3D WireMaterial = new() { AlbedoColor = new Color(0.75f, 0.72f, 0.65f), Roughness = 0.8f };
    private readonly Dictionary<int, Node3D> _devices = [];

    private void ShowDevices(StoreSpace space, Vector3 focus)
    {
        var seen = new HashSet<int>();
        var tick = Host.Snapshot.Clock.Tick;
        foreach (var device in Host.Snapshot.Devices)
        {
            if (device.Kind == DeviceKind.FurnitureTrap) continue;
            seen.Add(device.Id);
            if (!_devices.TryGetValue(device.Id, out var node))
            {
                node = DeviceNode(device);
                AddChild(node);
                _devices[device.Id] = node;
            }
            var near = space.Delta(focus.ToStore(), device.Position);
            node.Position = new Vector3(focus.X + near.X, 0, focus.Z + near.Y);
            // A playing lure shakes with its sound; a sprung alarm's cans hang askew.
            if (device.Kind == DeviceKind.NoiseLure)
                node.Scale = Vector3.One * (device.PlayingUntil > tick ? 1f + 0.08f * MathF.Sin(tick * 0.9f) : 1f);
            else if (device.Kind == DeviceKind.Alarm)
                node.Rotation = new Vector3(device.Sprung ? 0.35f : 0f, device.Id * 0.7f, 0);
        }
        foreach (var gone in _devices.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _devices[gone].QueueFree();
            _devices.Remove(gone);
        }
    }

    private static Node3D DeviceNode(Device device)
    {
        var node = new Node3D { Name = $"Device{device.Id}", Rotation = new Vector3(0, device.Id * 0.7f, 0) };
        switch (device.Kind)
        {
            case DeviceKind.NoiseLure:
                node.AddChild(new MeshInstance3D
                {
                    Mesh = device.Variant == 1 ? FurnitureModels.Product("lure_music_box", 0, 0.25f, 1500) : FurnitureModels.Product("lure_radio", 0, 0.5f, 1500),
                });
                break;
            case DeviceKind.Alarm:
                node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(4f, 0.01f, 0.01f) }, MaterialOverride = WireMaterial, Position = new Vector3(0, 0.25f, 0) });
                for (var i = -1; i <= 1; i++)
                    node.AddChild(new MeshInstance3D { Mesh = FurnitureModels.Product("goods_tinned_food", 0, 0.11f), Position = new Vector3(i * 1.2f, 0.12f, 0) });
                break;
            case DeviceKind.Pit:
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.1f, Height = 0.02f }, MaterialOverride = PitMaterial, Position = new Vector3(0, 0.012f, 0),
                });
                break;
        }
        return node;
    }
}
