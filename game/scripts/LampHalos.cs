using Godot;

namespace Scp3008.Game;

/// <summary>
/// One instanced draw of soft camera-facing halos for all visible Lamps, attenuated by the fog
/// between the bulb and the camera. No extra lights or shadow maps. The Lamps' light on surfaces
/// comes from real lights and the pools in the opaque materials (<see cref="LampPools"/>).
/// </summary>
public partial class LampHalos : Node3D
{
    private MultiMesh _halos = null!;
    private ShaderMaterial _material = null!;
    private int _capacity;
    private float[] _buffer = [];

    public void ApplyFog(Action<ShaderMaterial> apply) => apply(_material);

    public override void _Ready()
    {
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/lamp_halo.gdshader") };
        var mesh = new QuadMesh { Size = Vector2.One * 2f, Material = _material };
        _halos = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh };
        AddChild(new MultiMeshInstance3D { Multimesh = _halos, IgnoreOcclusionCulling = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    public void Show(IReadOnlyList<Vector3> bulbs)
    {
        if (bulbs.Count > _capacity)
        {
            _capacity = Math.Max(bulbs.Count, Math.Max(64, _capacity * 2));
            _halos.InstanceCount = _capacity;
            _buffer = new float[_capacity * 12];
        }
        _halos.VisibleInstanceCount = bulbs.Count;
        for (var i = 0; i < bulbs.Count; i++)
        {
            var o = i * 12;
            _buffer[o] = _buffer[o + 5] = _buffer[o + 10] = 0.9f;
            _buffer[o + 3] = bulbs[i].X;
            _buffer[o + 7] = bulbs[i].Y;
            _buffer[o + 11] = bulbs[i].Z;
        }
        // One native call rather than thousands of transform setters every frame.
        _halos.Buffer = _buffer;
    }
}
