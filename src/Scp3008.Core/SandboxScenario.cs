using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scp3008.Core;

/// <summary>Wanderers sharing their observations while looking for a site.</summary>
public sealed record Band(int Id, IReadOnlyList<int> VillagerIds)
{
    public Personality Personality { get; init; } = Personality.Cautious;
    public IReadOnlyList<GearKind> Recipes { get; init; } = Array.AsReadOnly(Core.Recipes.Basic.Select(r => r.Gear).ToArray());
    public Vector2 Position { get; init; }
    public KnownMap KnownMap { get; init; } = new();
    public Vector2? Site { get; init; }
    public int FoundingPopulation { get; init; }
}

/// <summary>A Staff and its current patrol movement; dormant while the Store is open.</summary>
public sealed record Staff(int Id, Vector2 Position, Vector2? Destination, float Heading);

public sealed partial class ScenarioBuilder
{
    public Staff PlaceStaff(Vector2 position, Vector2[] patrol)
    {
        ArgumentNullException.ThrowIfNull(patrol);
        if (patrol.Length == 0 || patrol.Append(position).Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
            throw new ArgumentException("A Staff patrol needs finite positions and at least one destination.");
        return _simulation.PlaceStaff(position, patrol);
    }

    /// <summary>Built-in sandbox presets, embedded from the same JSON files used by the game.</summary>
    public static IReadOnlyList<string> VillagerSandboxPresets { get; } = typeof(Simulation).Assembly
        .GetManifestResourceNames().Where(n => n.Contains(".Scenarios.") && n.EndsWith(".json"))
        .Select(n => n[(n.LastIndexOf(".Scenarios.", StringComparison.Ordinal) + 11)..^5])
        .OrderBy(n => n, StringComparer.Ordinal).ToList().AsReadOnly();

    /// <summary>Creates a tiny wrapping Store and applies a named preset through the scenario builder.</summary>
    public static Simulation LoadVillagerSandbox(string preset = "scavenge")
    {
        if (!VillagerSandboxPresets.Contains(preset))
            throw new ArgumentException($"Unknown Villager sandbox preset '{preset}'. Choose {string.Join(", ", VillagerSandboxPresets)}.", nameof(preset));
        using var stream = typeof(Simulation).Assembly.GetManifestResourceStream($"Scp3008.Core.Scenarios.{preset}.json")!;
        using var reader = new StreamReader(stream);
        return LoadVillagerSandboxJson(reader.ReadToEnd());
    }

    /// <summary>Loads a custom scenario file's JSON through the same facade seam as built-in presets.</summary>
    public static Simulation LoadVillagerSandboxJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        var file = JsonSerializer.Deserialize<SandboxFile>(json, options) ?? throw new JsonException("Expected a sandbox scenario.");
        if (file.Furniture is null || file.Bands is null || file.Gear is null || file.Staff is null)
            throw new JsonException("Scenario collections must be arrays, not null.");
        file.Needs?.Validate();
        var sim = Simulation.Create(new WorldConfig(3008, PillarLayout: PillarLayout.VillagerSandbox,
            RuntimeRngSeed: file.RuntimeRngSeed, Needs: file.NeedsConfig, Arrivals: file.Arrivals));
        try
        {
            var scenario = sim.Scenario;
            scenario.SetTimeOfDay(TimeSpan.ParseExact(file.Time, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture));
            foreach (var piece in file.Furniture)
            {
                if (piece is null)
                    throw new JsonException("Furniture entries must be objects, not null.");
                if (!float.IsFinite(piece.Rotation))
                    throw new ArgumentException("Furniture rotation must be finite.");
                var placed = scenario.PlaceFurniture(piece.Type, Position(piece.Position), piece.Rotation);
                foreach (var stock in piece.Stock ?? throw new JsonException("Stock must be an array, not null."))
                    scenario.SetStock(placed.Id, stock.Kind, stock.Amount);
            }
            foreach (var band in file.Bands)
            {
                if (band is null || band.Length == 0)
                    throw new ArgumentException("A Band must have members.");
                scenario.PlaceBand(band.Select(Position).ToArray(), file.Needs);
            }
            foreach (var gear in file.Gear)
                scenario.PlaceGear(gear.Kind, Position(gear.Position));
            foreach (var staff in file.Staff)
            {
                if (staff.Patrol is null || staff.Patrol.Length == 0)
                    throw new ArgumentException("A Staff patrol must have destinations.");
                sim.PlaceStaff(Position(staff.Position), staff.Patrol.Select(Position).ToArray());
            }
            return sim;
        }
        catch
        {
            sim.Dispose();
            throw;
        }
    }

    private static Vector2 Position(float[]? point)
    {
        if (point is null || point.Length != 2 || point.Any(v => !float.IsFinite(v)))
            throw new ArgumentException("A position must contain two finite coordinates in metres.");
        return new Vector2(point[0], point[1]);
    }

    private sealed record SandboxFile
    {
        public string Time { get; init; } = "12:00";
        public Needs? Needs { get; init; }
        public NeedsConfig? NeedsConfig { get; init; }
        public ArrivalConfig? Arrivals { get; init; }
        public int? RuntimeRngSeed { get; init; }
        public SandboxFurniture[] Furniture { get; init; } = [];
        public float[][][] Bands { get; init; } = [];
        public SandboxGear[] Gear { get; init; } = [];
        public SandboxStaff[] Staff { get; init; } = [];
    }

    private sealed record SandboxFurniture(FurnitureType Type, float[] Position, float Rotation = 0)
    {
        public SandboxStock[] Stock { get; init; } = [];
    }
    private readonly record struct SandboxStock(GoodsKind Kind, int Amount);
    private readonly record struct SandboxGear(GearKind Kind, float[] Position);
    private readonly record struct SandboxStaff(float[] Position, float[][] Patrol);
}
