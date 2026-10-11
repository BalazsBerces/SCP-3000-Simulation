using Godot;

namespace Scp3008.Game;

/// <summary>
/// Two instanced draws for all visible Lamps: depth-projected warm light on opaque surfaces
/// (including Furniture), and soft camera-facing halos. No extra lights or shadow maps.
/// The pool weight complements the real light's spatial fade at the budget boundary.
/// </summary>
public partial class LampImpostors : Node3D
{
    private MultiMesh _pools = null!;
    private MultiMesh _halos = null!;
    private int _capacity;
    private float[] _poolBuffer = [];
    private float[] _haloBuffer = [];

    public override void _Ready()
    {
        _pools = Make(new BoxMesh { Size = Vector3.One * 2f }, "res://shaders/lamp_pool.gdshader");
        _halos = Make(new QuadMesh { Size = Vector2.One * 2f }, "res://shaders/lamp_halo.gdshader");
    }

    private MultiMesh Make(PrimitiveMesh mesh, string shader)
    {
        mesh.Material = new ShaderMaterial { Shader = GD.Load<Shader>(shader) };
        var instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh };
        AddChild(new MultiMeshInstance3D { Multimesh = instances, IgnoreOcclusionCulling = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return instances;
    }

    public void Show(IEnumerable<(Vector3 Position, float Radius, float Weight)> lamps)
    {
        var visible = lamps.ToArray();
        if (visible.Length > _capacity)
        {
            _capacity = Math.Max(visible.Length, Math.Max(64, _capacity * 2));
            _pools.InstanceCount = _halos.InstanceCount = _capacity;
            _poolBuffer = new float[_capacity * 16];
            _haloBuffer = new float[_capacity * 16];
        }
        _pools.VisibleInstanceCount = _halos.VisibleInstanceCount = visible.Length;
        for (var i = 0; i < visible.Length; i++)
        {
            var lamp = visible[i];
            Write(_poolBuffer, i, lamp.Position, lamp.Radius, lamp.Weight);
            Write(_haloBuffer, i, lamp.Position, 0.9f, 1f);
        }
        // Two native calls rather than thousands of transform/colour setters every frame.
        _pools.Buffer = _poolBuffer;
        _halos.Buffer = _haloBuffer;
    }

    private static void Write(float[] buffer, int instance, Vector3 position, float scale, float weight)
    {
        var o = instance * 16;
        buffer[o] = buffer[o + 5] = buffer[o + 10] = scale;
        buffer[o + 3] = position.X;
        buffer[o + 7] = position.Y;
        buffer[o + 11] = position.Z;
        buffer[o + 12] = buffer[o + 13] = buffer[o + 14] = 1f;
        buffer[o + 15] = weight;
    }
}
