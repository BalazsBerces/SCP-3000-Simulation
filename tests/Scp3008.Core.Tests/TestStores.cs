using Scp3008.Core;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCaseOrderer("Scp3008.Core.Tests.ByArgumentsOrderer", "Scp3008.Core.Tests")]

namespace Scp3008.Core.Tests;

/// <summary>
/// Generated Stores shared by the tests that only read them. Generating Stores is nearly all of the suite's time,
/// and many tests and classes ask for the same one. A Store comes from its seed alone (ADR 0003), so these are
/// generated with runtime seed 1 and no Starting Bands. Each takes about 100 MB, so only the most recently used
/// few are kept; <see cref="ByArgumentsOrderer"/> runs a class's test cases for the same Store one after another.
/// </summary>
public static class TestStores
{
    private const int Capacity = 16;
    private static readonly LinkedList<(WorldConfig Config, Lazy<WorldSnapshot> Store)> Recent = new();

    public static WorldSnapshot Get(int seed, PillarLayout layout = PillarLayout.ThreeByThree,
        bool landmarkShowcase = false, bool pointOfInterestShowcase = false)
    {
        var config = new WorldConfig(seed, RuntimeRngSeed: 1, PillarLayout: layout, LandmarkShowcase: landmarkShowcase,
            PointOfInterestShowcase: pointOfInterestShowcase, StartingBands: false);
        Lazy<WorldSnapshot> store;
        lock (Recent)
        {
            var node = Recent.First;
            while (node is not null && node.Value.Config != config)
                node = node.Next;
            if (node is null)
            {
                node = new((config, new Lazy<WorldSnapshot>(() => Generate(config))));
                if (Recent.Count >= Capacity)
                    Recent.RemoveLast();
            }
            else
                Recent.Remove(node);
            Recent.AddFirst(node);
            store = node.Value.Store;
        }
        return store.Value;
    }

    private static WorldSnapshot Generate(WorldConfig config)
    {
        using var sim = Simulation.Create(config);
        return sim.Snapshot;
    }
}

/// <summary>Orders a class's test cases by their arguments, so the cases of different theories that read the same Store run together.</summary>
public sealed class ByArgumentsOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase =>
        testCases.OrderBy(t => Arguments(t.DisplayName), StringComparer.Ordinal).ThenBy(t => t.DisplayName, StringComparer.Ordinal);

    private static string Arguments(string displayName)
    {
        var open = displayName.IndexOf('(');
        return open < 0 ? "" : displayName[open..];
    }
}
