using System.Numerics;

namespace Scp3008.Core;

public enum Role { Scavenger, Builder, Cook, Guard, Trader, Farmer, NightScout }

/// <summary>A founding community; its stores are an open pile until it builds a Storehouse.</summary>
public sealed record Village(int Id, Vector2 Position, IReadOnlyList<int> VillagerIds)
{
    public IReadOnlyList<Workshop> Workshops { get; init; } = [];
    public Personality Personality { get; init; } = Personality.Cautious;
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
