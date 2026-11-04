using System.Numerics;

namespace Scp3008.Core;

public enum Role { Scavenger, Builder, Cook, Guard, Trader, Farmer, NightScout }

/// <summary>A founding community; its stores are an open pile until it builds a Storehouse.</summary>
public sealed record Village(int Id, Vector2 Position, IReadOnlyList<int> VillagerIds)
{
    public IReadOnlyList<Dwelling> Dwellings { get; init; } = [];
    /// <summary>Whether a Siege broke it: its walls stand empty and its survivors left as Refugees.</summary>
    public bool Fallen { get; init; }
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
    /// <summary>The type of Department it stands in; it gives the Village its flavour.</summary>
    public DepartmentType Department { get; init; }
    /// <summary>Ticks of unbroken famine; a long one turns the Personality toward raiding.</summary>
    public int FamineTicks { get; init; }
    /// <summary>Home wall rings and Outpost barricades; Outposts identify their own WallIndex.</summary>
    public IReadOnlyList<Blueprint> Walls { get; init; } = [];
    /// <summary>Towers, Gatehouses and the Keep, planned with the curtain walls.</summary>
    public IReadOnlyList<CastleStructure> Castle { get; init; } = [];
    public PillarScaffold? PillarScaffold { get; init; }
    public IReadOnlyList<Moat> Moats { get; init; } = [];
    /// <summary>The arms on the Village's banners, chosen from local fabrics when its first wall is planned.</summary>
    public Heraldry? Heraldry { get; init; }
    /// <summary>Where the Village believes the Hordes come from; it shapes where it sets its alarms, traps and lures.</summary>
    public Belief? HordeBelief { get; init; }
    public IReadOnlyDictionary<MaterialKind, int> Materials { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<MaterialKind, int>(new Dictionary<MaterialKind, int>());
    public IReadOnlyList<GearKind> Recipes { get; init; } = Array.AsReadOnly(Core.Recipes.Basic.Select(r => r.Gear).ToArray());
    public IReadOnlyList<Gear> Gear { get; init; } = [];
    public bool HasMadeGear { get; init; }
    /// <summary>The tick it last split or was split from; a Village splits at most once a Store day.</summary>
    public long? LastSplit { get; init; }
    /// <summary>The Villages it is allied with: mutual defence, shared Recipes and Beliefs, better barter, refuge for Refugees.</summary>
    public IReadOnlyList<int> Allies { get; init; } = [];
    /// <summary>Guards of its allies who came to defend it and have not gone home yet.</summary>
    public IReadOnlyList<Reinforcement> Reinforcements { get; init; } = [];
    public KnownMap KnownMap { get; init; } = new();
    /// <summary>The Goods kind the Village takes as currency, beyond its own need, because the Villages it knows want it; null for pure barter.</summary>
    public GoodsKind? Currency { get; init; }
    /// <summary>The territory it holds: Pillars, the fountain court, Cafeterias and Store shelves guarded for food.</summary>
    public IReadOnlyList<Holding> Holdings { get; init; } = [];
    /// <summary>Members seized in Raids and not yet absorbed.</summary>
    public IReadOnlyList<Captive> Captives { get; init; } = [];
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
/// <summary>Founded is the tick it was set up; a distant Outpost drifts apart after a Store day.</summary>
public sealed record Outpost(int Id, Vector2 Position, int WallIndex, IReadOnlyList<int> VillagerIds, bool Abandoned = false, long Founded = 0);
