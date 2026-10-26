namespace Scp3008.Core;

/// <summary>A temporary Restock slowdown in one Department, with faster Restock in another.</summary>
public sealed record DrySpell(int DepartmentId, int BoostedDepartmentId, StoreClock StartedAt, StoreClock EndsAt, double Strength);
