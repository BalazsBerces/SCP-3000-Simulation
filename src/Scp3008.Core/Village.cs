using System.Numerics;

namespace Scp3008.Core;

public enum Role { Scavenger, Builder, Cook, Guard, Trader, Farmer, NightScout }

/// <summary>A founding community; its stores are an open pile until it builds a Storehouse.</summary>
public sealed record Village(int Id, Vector2 Position, IReadOnlyList<int> VillagerIds)
{
    public MessHall? MessHall { get; init; }
    public IReadOnlyList<Outpost> Outposts { get; init; } = [];
    public IReadOnlyList<Workshop> Workshops { get; init; } = [];
    public Personality Personality { get; init; } = Personality.Cautious;
    /// <summary>Home wall rings and Outpost barricades; Outposts identify their own WallIndex.</summary>
    public IReadOnlyList<Blueprint> Walls { get; init; } = [];
    public IReadOnlyDictionary<MaterialKind, int> Materials { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<MaterialKind, int>(new Dictionary<MaterialKind, int>());
    public IReadOnlyList<GearKind> Recipes { get; init; } = Array.AsReadOnly(Core.Recipes.Basic.Select(r => r.Gear).ToArray());
    public IReadOnlyList<Gear> Gear { get; init; } = [];
    public bool HasMadeGear { get; init; }
    public KnownMap KnownMap { get; init; } = new();
    public IReadOnlyDictionary<GoodsKind, int> Stores { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<GoodsKind, int>(new Dictionary<GoodsKind, int>());
}

/// <summary>A forward site owned by its Village; WallIndex points to its shared construction Blueprint.</summary>
public sealed record Outpost(int Id, Vector2 Position, int WallIndex, IReadOnlyList<int> VillagerIds, bool Abandoned = false);
