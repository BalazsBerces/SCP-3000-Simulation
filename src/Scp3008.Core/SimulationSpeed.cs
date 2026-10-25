namespace Scp3008.Core;

/// <summary>
/// How fast the simulation clock runs against real time. The value is the number of
/// ticks per real-time step; fast-forward runs more ticks per real second.
/// </summary>
public enum SimulationSpeed
{
    Paused = 0,
    Normal = 1,
    Fast2 = 2,
    Fast4 = 4,
    Fast8 = 8,
    Fast16 = 16,
    Fast60 = 60,
}
