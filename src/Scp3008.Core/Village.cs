using System.Numerics;

namespace Scp3008.Core;

public enum Role { Scavenger, Builder, Cook, Guard, Trader, Farmer, NightScout }

/// <summary>A founding community; its stores are an open pile until it builds a Storehouse.</summary>
public sealed record Village(int Id, Vector2 Position, IReadOnlyList<int> VillagerIds)
{
    public IReadOnlyList<Dwelling> Dwellings { get; init; } = [];
    /// <summary>Completed covered places available under the Village's current sharing rule.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int CoveredSleepingPlaces => Dwellings.Sum(d => d.Complete && Dwelling.Sharing(Personality) == DwellingSharing.Personal ? 1 : d.SleepingPlaces);
    public MessHall? MessHall { get; init; }
    public Storehouse? Storehouse { get; init; }
    public FurnitureYard? FurnitureYard { get; init; }
    /// <summary>Fractions of a unit of each spoiling food kind lost so far, carried between spoilage passes.</summary>
    public IReadOnlyDictionary<GoodsKind, float> SpoilProgress { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<GoodsKind, float>(new Dictionary<GoodsKind, float>());
    public IReadOnlyList<Outpost> Outposts { get; init; } = [];
    public IReadOnlyList<Workshop> Workshops { get; init; } = [];
    public Personality Personality { get; init; } = Personality.Cautious;
    /// <summary>Home wall rings and Outpost barricades; Outposts identify their own WallIndex.</summary>
    public IReadOnlyList<Blueprint> Walls { get; init; } = [];
    /// <summary>Towers, Gatehouses and the Keep, planned with the curtain walls.</summary>
    public IReadOnlyList<CastleStructure> Castle { get; init; } = [];
    public PillarScaffold? PillarScaffold { get; init; }
    public IReadOnlyList<Moat> Moats { get; init; } = [];
    /// <summary>The arms on the Village's banners, chosen from local fabrics when its first wall is planned.</summary>
    public Heraldry? Heraldry { get; init; }
    public IReadOnlyDictionary<MaterialKind, int> Materials { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<MaterialKind, int>(new Dictionary<MaterialKind, int>());
    public IReadOnlyList<GearKind> Recipes { get; init; } = Array.AsReadOnly(Core.Recipes.Basic.Select(r => r.Gear).ToArray());
    public IReadOnlyList<Gear> Gear { get; init; } = [];
    public bool HasMadeGear { get; init; }
    public KnownMap KnownMap { get; init; } = new();
    public IReadOnlyDictionary<GoodsKind, int> Stores { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<GoodsKind, int>(new Dictionary<GoodsKind, int>());

    /// <summary>Whether a wall ring is the Village's own, not an Outpost's or the Furniture yard's barricade.</summary>
    public bool IsHomeWall(int index) => !Outposts.Any(o => o.WallIndex == index) && FurnitureYard?.WallIndex != index;
    public IEnumerable<Blueprint> HomeWalls() => Walls.Where((_, i) => IsHomeWall(i));

    /// <summary>The units the Storehouse can hold; none without one, so everything lies in the open pile.</summary>
    public int StorehouseCapacity => Storehouse?.Capacity ?? 0;
    /// <summary>All the stores: Goods, Materials and unused Gear, in units.</summary>
    public int StoresTotal => Stores.Values.Sum() + Materials.Values.Sum() + Gear.Count;

    /// <summary>
    /// The goods lying in the open pile: the Storehouse fills with food first (fast-spoiling kinds before slow), then other Goods,
    /// then Materials and Gear, and what does not fit lies outside.
    /// </summary>
    public IReadOnlyDictionary<GoodsKind, int> PileGoods()
    {
        var room = StorehouseCapacity;
        var pile = new Dictionary<GoodsKind, int>();
        foreach (var (kind, amount) in Stores.Where(g => g.Value > 0).OrderBy(g => GoodsCatalog.ClassOf(g.Key) != GoodsClass.Food)
            .ThenByDescending(g => GoodsCatalog.SpoilsFast(g.Key)).ThenBy(g => g.Key))
        {
            var fit = Math.Min(room, amount);
            room -= fit;
            if (amount > fit) pile[kind] = amount - fit;
        }
        return pile;
    }

    /// <summary>Units of stores of every sort lying in the open pile.</summary>
    public int PileTotal => Math.Max(0, StoresTotal - StorehouseCapacity);
}

/// <summary>A forward site owned by its Village; WallIndex points to its shared construction Blueprint.</summary>
public sealed record Outpost(int Id, Vector2 Position, int WallIndex, IReadOnlyList<int> VillagerIds, bool Abandoned = false);
