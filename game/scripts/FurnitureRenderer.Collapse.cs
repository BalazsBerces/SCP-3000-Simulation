using Godot;
using Scp3008.Core;

namespace Scp3008.Game;

/// <summary>
/// Collapses (ADR 0002): the core settles a falling piece at once on the floor; here it falls there instead, dropping under
/// gravity while it tips over, the stack coming down from the bottom up, and bouncing once as it lands.
/// </summary>
public partial class FurnitureRenderer
{
    private const float Gravity = 9.8f;
    private const double Bounce = 0.25;

    private sealed record Fall(FallenPiece Piece, double Start, double Duration, MultiMeshInstance3D Node);

    private readonly Dictionary<int, Fall> _falls = [];

    private void OnDomainEvent(DomainEvent domainEvent)
    {
        if (domainEvent is not StructureCollapsed collapse)
            return;
        var now = Time.GetTicksMsec() / 1000.0;
        var world = Host.Snapshot;
        foreach (var piece in collapse.Fallen)
        {
            if (_falls.Remove(piece.To.Id, out var old))
                old.Node.QueueFree();
            var node = new MultiMeshInstance3D { Name = $"Falling{piece.To.Id}", Multimesh = new MultiMesh
                { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = 1 } };
            node.Multimesh.Mesh = FurnitureMeshes.For(piece.To.Type, piece.To.Variant, piece.To.LandmarkId >= 0);
            node.Multimesh.SetInstanceColor(0, Colour(world, piece.To));
            GetParent().AddChild(node);
            var drop = MathF.Max(piece.From.Elevation - piece.To.Elevation, 0.3f);
            // Higher pieces start a little later, so the stack comes down from the bottom up.
            var start = now + Math.Min(piece.From.Elevation * 0.06, 0.6);
            _falls[piece.To.Id] = new Fall(piece, start, Math.Sqrt(2 * drop / Gravity) + 0.15, node);
        }
        // The stationary groups rebuild without the falling pieces.
        _builtFurniture = null;
    }

    private void ShowFalls(WorldSnapshot world)
    {
        var now = Time.GetTicksMsec() / 1000.0;
        foreach (var fall in _falls.Values.ToArray())
        {
            var t = (float)Math.Clamp((now - fall.Start) / fall.Duration, 0, 1);
            var from = Placement(fall.Piece.From with { Position = System.Numerics.Vector2.Zero });
            var to = Placement(fall.Piece.To with { Position = System.Numerics.Vector2.Zero });
            var shift = world.Space.Delta(fall.Piece.From.Position, fall.Piece.To.Position);
            // Falling accelerates (t²), sliding out keeps pace, and tipping over speeds up as the piece goes.
            var origin = new Vector3(shift.X * t, Mathf.Lerp(from.Origin.Y, to.Origin.Y, t * t), shift.Y * t);
            var landed = now - fall.Start - fall.Duration;
            if (landed > 0)
                origin.Y += 0.08f * Mathf.Sin((float)(landed / Bounce) * Mathf.Pi) * (landed < Bounce ? 1 : 0);
            var basis = new Basis(from.Basis.GetRotationQuaternion().Slerp(to.Basis.GetRotationQuaternion(), t * t));
            fall.Node.Multimesh.SetInstanceTransform(0, new Transform3D(basis, origin));
            var near = world.Space.Delta(Rig.Focus.ToStore(), fall.Piece.From.Position);
            fall.Node.Position = new Vector3(Rig.Focus.X + near.X, 0, Rig.Focus.Z + near.Y);
            if (landed > Bounce)
            {
                fall.Node.QueueFree();
                _falls.Remove(fall.Piece.To.Id);
                _builtFurniture = null;
            }
        }
    }
}
