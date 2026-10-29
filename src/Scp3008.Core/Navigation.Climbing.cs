using System.Numerics;

namespace Scp3008.Core;

internal sealed partial class Navigation
{
    internal bool SupportsClimber(Vector3 point) => ClimbLinks(new(point.X, point.Y)).Any(link =>
    {
        var span = new Vector3(_space.Delta(new(link.Bottom.X, link.Bottom.Y), new(link.Top.X, link.Top.Y)), link.Top.Z - link.Bottom.Z);
        var offset = new Vector3(_space.Delta(new(link.Bottom.X, link.Bottom.Y), new(point.X, point.Y)), point.Z - link.Bottom.Z);
        var t = span.LengthSquared() == 0 ? 0 : Math.Clamp(Vector3.Dot(offset, span) / span.LengthSquared(), 0, 1);
        var gap = offset - span * t;
        return MathF.Abs(gap.Z) <= 0.55f && new Vector2(gap.X, gap.Y).Length() <= 1.8f;
    });

    // shortcut: vertical routes stay within 100 m of their Lookout; use a coarse graph for journeys spanning several structures.
    // Rungs on open shelving and the sloping surfaces of mattresses are climbable Furniture.
    private IEnumerable<(Vector3 Bottom, Vector3 Top)> ClimbLinks(Vector2 destination)
    {
        foreach (var p in _pieces().Where(p => p.Damage < 1 && MathF.Abs(p.Roll) < 0.1f &&
            _space.Distance(p.Position, destination) < 100))
        {
            var spec = FurnitureCatalog.Spec(p);
            var front = new Vector2(-MathF.Sin(p.Rotation), MathF.Cos(p.Rotation));
            if (p.Type is FurnitureType.PalletRack or FurnitureType.ShelfUnit or FurnitureType.Bookcase && MathF.Abs(p.Tilt) < 0.1f)
            {
                var at = p.Position + front * spec.Depth / 2;
                yield return (new(at, p.Elevation), new(at, p.Elevation + spec.Height));
            }
            else if (p.Type == FurnitureType.Mattress)
            {
                var half = front * (spec.Depth / 2 * MathF.Cos(p.Tilt));
                var rise = spec.Depth / 2 * MathF.Sin(p.Tilt);
                var height = p.Elevation + spec.Height * MathF.Cos(p.Tilt);
                yield return (new(p.Position - half + front * spec.Height * MathF.Sin(p.Tilt), MathF.Max(0, height + rise)), new(p.Position + half + front * spec.Height * MathF.Sin(p.Tilt), MathF.Max(0, height - rise)));
                // A flat mattress can also be stepped onto from the one underneath it.
                if (MathF.Abs(p.Tilt) < 0.1f)
                    yield return (new(p.Position, p.Elevation), new(p.Position, height));
            }
        }
    }

    internal List<Vector3>? FindClimbPath(Vector3 from, Vector3 to)
    {
        var target = new Vector2(to.X, to.Y);
        var links = ClimbLinks(to.Z > 0 ? target : new(from.X, from.Y)).ToArray();
        var nodes = links.SelectMany(l => new[] { l.Bottom, l.Top }).ToArray();
        float Horizontal(Vector3 a, Vector3 b) => _space.Distance(new(a.X, a.Y), new(b.X, b.Y));
        bool Touch(Vector3 a, Vector3 b) => MathF.Abs(a.Z - b.Z) <= 0.55f && Horizontal(a, b) <= 1.8f;
        bool OnLink(Vector3 point, int node)
        {
            var a = nodes[node]; var b = nodes[node ^ 1];
            var span = new Vector3(_space.Delta(new(a.X, a.Y), new(b.X, b.Y)), b.Z - a.Z);
            var offset = new Vector3(_space.Delta(new(a.X, a.Y), new(point.X, point.Y)), point.Z - a.Z);
            var t = span.LengthSquared() == 0 ? 0 : Math.Clamp(Vector3.Dot(offset, span) / span.LengthSquared(), 0, 1);
            return (offset - span * t).Length() < 0.6f;
        }
        var previous = Enumerable.Repeat(-2, nodes.Length).ToArray();
        var queue = new Queue<int>();
        // Search from the elevated end towards free floor. Links are bidirectional.
        var high = to.Z > 0 ? to : from;
        for (var i = 0; i < nodes.Length; i++)
            if (Touch(nodes[i], high) || OnLink(high, i)) { previous[i] = -1; queue.Enqueue(i); }
        while (queue.TryDequeue(out var at))
        {
            var node = nodes[at];
            if (to.Z > 0 && from.Z > 0 && (Touch(node, from) || OnLink(from, at)))
            {
                var route = new List<Vector3>();
                for (var n = at; n >= 0; n = previous[n]) route.Add(nodes[n]);
                route.Add(high);
                return route;
            }
            if (node.Z <= 0.55f && (to.Z == 0 || from.Z == 0))
            {
                var entry = NearestWalkable(new(node.X, node.Y));
                if (_space.Distance(entry, new(node.X, node.Y)) <= 2 &&
                    FindPath(to.Z > 0 ? new(from.X, from.Y) : target, entry) is { } floor)
                {
                    var route = floor.Select(p => new Vector3(p, 0)).ToList();
                    for (var n = at; n >= 0; n = previous[n]) route.Add(nodes[n]);
                    route.Add(high);
                    if (to.Z == 0) { route.Reverse(); route.Add(new(NearestWalkable(target), 0)); }
                    return route;
                }
            }
            for (var next = 0; next < nodes.Length; next++)
                if (previous[next] == -2 && (next == (at ^ 1) || Touch(node, nodes[next])))
                { previous[next] = at; queue.Enqueue(next); }
        }
        return null;
    }
}
