namespace Scp3008.Core;

/// <summary>
/// Something that happened in the world. Domain events feed the chronicle and statistics.
/// </summary>
/// <param name="At">The Store clock when the event happened.</param>
public abstract record DomainEvent(StoreClock At);

/// <summary>A new Store day began at midnight.</summary>
public sealed record StoreDayBegan(StoreClock At, int Day) : DomainEvent(At);
