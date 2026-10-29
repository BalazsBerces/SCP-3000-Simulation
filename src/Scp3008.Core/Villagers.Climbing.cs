using System.Numerics;

namespace Scp3008.Core;

internal sealed partial class Villagers
{
    private sealed partial class Walker
    {
        public HashSet<Vector3> VisitedLookouts { get; } = [];
    }
    public void Send(int id, Vector3 destination)
    {
        var walker = _walkers.Single(w => w.Id == id);
        walker.Target = null;
        SendClimbing(walker, destination);
    }

    private bool SendClimbing(Walker walker, Vector3 destination)
    {
        var path = navigation.FindClimbPath(new(walker.Position, walker.Elevation), destination);
        walker.Waypoints.Clear();
        walker.ClimbWaypoints = path ?? [];
        walker.ClimbDestination = path is null ? null : destination;
        walker.Destination = path is null ? null : new(destination.X, destination.Y);
        return path is not null;
    }

    private void AdvanceClimbing(Walker walker, float left)
    {
        while (left > 0 && walker.ClimbWaypoints.Count > 0)
        {
            var next = walker.ClimbWaypoints[0];
            var horizontal = space.Delta(walker.Position, new(next.X, next.Y));
            var delta = new Vector3(horizontal, next.Z - walker.Elevation);
            var distance = delta.Length();
            var step = distance <= left ? 1 : left / distance;
            walker.Position = space.Wrap(walker.Position + horizontal * step);
            walker.Elevation = MathF.Max(0, walker.Elevation + delta.Z * step);
            if (horizontal.LengthSquared() > 0.001f) walker.Heading = MathF.Atan2(horizontal.Y, horizontal.X);
            left -= distance;
            if (step == 1) walker.ClimbWaypoints.RemoveAt(0);
        }
        if (walker.ClimbWaypoints.Count == 0)
        {
            walker.ClimbDestination = null;
            walker.Destination = null;
            walker.NextDecision = 0.25;
        }
    }
}

public sealed partial class ScenarioBuilder
{
    /// <summary>Sends a Villager up climbable Furniture to a Lookout (Z is height), or down to the floor.</summary>
    public void SendToLookout(int id, Vector3 destination)
    {
        if (!float.IsFinite(destination.X) || !float.IsFinite(destination.Y) || !float.IsFinite(destination.Z) || destination.Z < 0)
            throw new ArgumentOutOfRangeException(nameof(destination));
        _simulation.SendClimbing(id, destination);
    }
}

public sealed partial class Simulation
{
    internal void SendClimbing(int id, Vector3 destination) => Publish(() =>
    {
        if (!_villagers.Snapshot().Any(v => v.Id == id)) throw new ArgumentException("Unknown Villager.", nameof(id));
        CancelFurnitureTasks(t => t.VillagerIds.Contains(id));
        if (_craftingTasks.RemoveAll(t => t.VillagerId == id) > 0) _villagers.CraftingPose(id, false);
        _villagers.Send(id, destination);
        return true;
    });
}
