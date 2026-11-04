using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

public partial class VillagerRenderer
{
    // Envoys, lamp signals and Pillar marks are overlay glyphs, not props: plain primitives (see docs/ticket-30a-verification.md).
    private static readonly StandardMaterial3D EnvoyMaterial = new() { AlbedoColor = new Color(0.95f, 0.85f, 0.2f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    private static readonly StandardMaterial3D BeamMaterial = new()
    {
        AlbedoColor = new Color(1f, 0.85f, 0.45f, 0.45f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };
    private static readonly StandardMaterial3D WarningPaint = new() { AlbedoColor = new Color(0.8f, 0.1f, 0.08f), Roughness = 1 };
    private static readonly StandardMaterial3D BorderPaint = new() { AlbedoColor = new Color(0.15f, 0.35f, 0.8f), Roughness = 1 };
    private readonly Dictionary<int, MeshInstance3D> _envoyFlags = [];
    private readonly Dictionary<int, Node3D> _beams = [];
    private readonly Dictionary<int, MeshInstance3D> _markPaint = [];

    private static void Prune<T>(Dictionary<int, T> shown, HashSet<int> seen) where T : Node
    {
        foreach (var gone in shown.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            shown[gone].QueueFree();
            shown.Remove(gone);
        }
    }

    private void ShowSignals(StoreSpace space, Vector3 focus)
    {
        var world = Host.Snapshot;
        Vector3 At(System.Numerics.Vector2 p, float y)
        {
            var near = space.Delta(focus.ToStore(), p);
            return new Vector3(focus.X + near.X, y, focus.Z + near.Y);
        }
        var seenEnvoys = new HashSet<int>();
        foreach (var envoy in world.Envoys)
        {
            seenEnvoys.Add(envoy.Id);
            if (!_envoyFlags.TryGetValue(envoy.Id, out var flag))
            {
                flag = new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(0.3f, 0.35f, 0.05f) }, MaterialOverride = EnvoyMaterial, Name = $"Envoy{envoy.Id}" };
                AddChild(flag);
                _envoyFlags[envoy.Id] = flag;
            }
            flag.Position = At(envoy.Position, 2.1f);
        }
        Prune(_envoyFlags, seenEnvoys);
        var seenBeams = new HashSet<int>();
        foreach (var signal in world.Signals)
        {
            seenBeams.Add(signal.VillageId);
            if (!_beams.TryGetValue(signal.VillageId, out var beam))
            {
                beam = new Node3D { Name = $"LampSignal{signal.VillageId}" };
                beam.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.5f, Height = 9 }, MaterialOverride = BeamMaterial, Position = new Vector3(0, 4.5f, 0) });
                beam.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.4f), OmniRange = 12, Position = new Vector3(0, 3, 0) });
                AddChild(beam);
                _beams[signal.VillageId] = beam;
            }
            beam.Position = At(signal.Position, 0);
        }
        Prune(_beams, seenBeams);
        var seenMarks = new HashSet<int>();
        foreach (var mark in world.Marks)
        {
            seenMarks.Add(mark.Id);
            var pillar = world.Pillars[mark.PillarId];
            // The mark is on the face turned toward the Village that painted it.
            var toward = space.Delta(mark.Position, world.Villages[mark.VillageId].Position);
            var onX = Math.Abs(toward.X) > Math.Abs(toward.Y);
            if (!_markPaint.TryGetValue(mark.Id, out var paint))
            {
                paint = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = onX ? new Vector3(0.04f, 1.1f, 0.9f) : new Vector3(0.9f, 1.1f, 0.04f) },
                    MaterialOverride = mark.Kind == MarkKind.Warning ? WarningPaint : BorderPaint, Name = $"PillarMark{mark.Id}",
                };
                AddChild(paint);
                _markPaint[mark.Id] = paint;
            }
            var edge = pillar.Width / 2 + 0.02f;
            var offset = onX ? new System.Numerics.Vector2(Math.Sign(toward.X) * edge, 0) : new System.Numerics.Vector2(0, Math.Sign(toward.Y) * edge);
            paint.Position = At(mark.Position + offset, 1.7f);
        }
        Prune(_markPaint, seenMarks);
    }
}
