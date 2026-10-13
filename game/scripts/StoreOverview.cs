namespace Scp3008.Game;

/// <summary>
/// Dev view of the whole Store at its true size: F4 (in <see cref="WrapDebugOverlay"/>) toggles it.
/// While it is on, the Store is drawn once, with no wrapped ghost copies, Edge fade or far cut-off, and
/// the camera can zoom out far enough to see all of it. View only: it never touches the simulation.
/// </summary>
public static class StoreOverview
{
    public static bool Enabled { get; private set; }

    /// <summary>Raised with the new state whenever the overview is switched on or off.</summary>
    public static event Action<bool>? Changed;

    public static void Set(bool enabled)
    {
        if (enabled == Enabled)
            return;
        Enabled = enabled;
        Changed?.Invoke(enabled);
    }
}
