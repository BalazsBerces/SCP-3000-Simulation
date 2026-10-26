namespace Scp3008.Core;

/// <summary>Hunger and thirst rise from 0 (satisfied) to 1 (depleted); rest, safety and morale rise from 0 to 1 (best).</summary>
public sealed record Needs(double Hunger = 0, double Thirst = 0, double Rest = 1, double Safety = 1, double Morale = 0.7)
{
    internal void Validate()
    {
        foreach (var value in new[] { Hunger, Thirst, Rest, Safety, Morale })
            if (!double.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Needs must lie from 0 to 1.");
    }
}

/// <summary>Store hours at full hunger or thirst before death. Eating or drinking resets that deprivation.</summary>
public sealed record NeedsConfig(double StarvationHours = 72, double DehydrationHours = 48)
{
    internal void Validate()
    {
        if (!double.IsFinite(StarvationHours) || StarvationHours <= 0 ||
            !double.IsFinite(DehydrationHours) || DehydrationHours <= 0)
            throw new ArgumentOutOfRangeException(nameof(StarvationHours), "Death periods must be finite and positive.");
    }
}

public enum DeathCause { Starvation, Thirst }

/// <summary>A dead Villager, left where they died; their id stays unique among living Villagers and bodies.</summary>
public sealed record Body(int VillagerId, System.Numerics.Vector2 Position, float Heading, StoreClock At, DeathCause Cause);

public enum VillagerAction { Idle, SeekingFood, SeekingDrink, SeekingComfort, Eating, Drinking, Resting, UsingComfort, SeekingSafety, Scouting }
