namespace Scp3008.Core;

/// <summary>
/// Something that happened in the world. Domain events feed the chronicle and statistics.
/// </summary>
/// <param name="At">The Store clock when the event happened.</param>
public abstract record DomainEvent(StoreClock At);

public sealed record VillageFounded(StoreClock At, int BandId, Village Village, bool Forced) : DomainEvent(At);

public sealed record WanderersJoined(StoreClock At, int BandId, int VillageId, IReadOnlyList<int> VillagerIds) : DomainEvent(At);

public sealed record RoleChanged(StoreClock At, int VillagerId, Role Role) : DomainEvent(At);

public sealed record GoodsDelivered(StoreClock At, int VillagerId, int VillageId, GoodsKind Kind) : DomainEvent(At);

public sealed record VillagerDied(StoreClock At, Body Body) : DomainEvent(At);

public sealed record GoodsConsumed(StoreClock At, int VillagerId, int FurnitureId, GoodsKind Kind) : DomainEvent(At);

public sealed record WaterDrunk(StoreClock At, int VillagerId, WaterPoint WaterPoint) : DomainEvent(At);

/// <summary>A new Store day began at midnight.</summary>
public sealed record StoreDayBegan(StoreClock At, int Day) : DomainEvent(At);

/// <summary>The Store lights went out: Closing time began on Store day <paramref name="Day"/>.</summary>
public sealed record ClosingTimeBegan(StoreClock At, int Day) : DomainEvent(At);

/// <summary>The Store lights came back on: Closing time ended and the Store reopened on Store day <paramref name="Day"/>.</summary>
public sealed record StoreReopened(StoreClock At, int Day) : DomainEvent(At);

/// <summary>A Fog bank formed; <paramref name="Bank"/> is how it looked at that moment.</summary>
public sealed record FogBankFormed(StoreClock At, FogBank Bank) : DomainEvent(At);

/// <summary>The Fog bank <paramref name="FogBankId"/> thinned out and dissipated at <paramref name="Position"/>.</summary>
public sealed record FogBankDissipated(StoreClock At, int FogBankId, System.Numerics.Vector2 Position) : DomainEvent(At);

public sealed record DrySpellStarted(StoreClock At, DrySpell Spell) : DomainEvent(At);

public sealed record DrySpellEnded(StoreClock At, DrySpell Spell) : DomainEvent(At);

public sealed record OutpostFounded(StoreClock At, int VillageId, Outpost Outpost) : DomainEvent(At);

public sealed record OutpostAbandoned(StoreClock At, int VillageId, int OutpostId) : DomainEvent(At);
