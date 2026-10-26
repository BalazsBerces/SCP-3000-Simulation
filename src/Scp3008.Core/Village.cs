using System.Numerics;

namespace Scp3008.Core;

public enum Role { Scavenger, Builder, Cook, Guard, Trader, Farmer, NightScout }

/// <summary>A founding community; its stores are an open pile until it builds a Storehouse.</summary>
public sealed record Village(int Id, Vector2 Position, IReadOnlyList<int> VillagerIds)
{
    public KnownMap KnownMap { get; init; } = new();
    public IReadOnlyDictionary<GoodsKind, int> Stores { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<GoodsKind, int>(new Dictionary<GoodsKind, int>());
}
