namespace Scp3008.Core;

/// <summary>
/// The world setting that chooses the Pillar grid and, with it, the Sector division. It is part of
/// the world config and the world state; the seed never changes it.
/// </summary>
public enum PillarLayout
{
    /// <summary>Nine Pillars 448 m apart, at 224, 672 and 1120 m on each axis; nine 448 m Sectors (the default).</summary>
    ThreeByThree,

    /// <summary>Four Pillars 672 m apart, at 336 and 1008 m on each axis; four 672 m Sectors.</summary>
    TwoByTwo,
}

/// <summary>Names and grid sizes of the <see cref="PillarLayout"/>s.</summary>
public static class PillarLayouts
{
    /// <summary>Every layout, in menu order.</summary>
    public static IReadOnlyList<PillarLayout> All { get; } = [PillarLayout.ThreeByThree, PillarLayout.TwoByTwo];

    /// <summary>Pillars (and Sectors) along each axis of the Store.</summary>
    public static int PillarsPerSide(this PillarLayout layout) => layout switch
    {
        PillarLayout.ThreeByThree => 3,
        PillarLayout.TwoByTwo => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    /// <summary>The short name, as in the <c>--pillars=</c> dev flag: <c>3x3</c> or <c>2x2</c>.</summary>
    public static string Name(this PillarLayout layout) => $"{layout.PillarsPerSide()}x{layout.PillarsPerSide()}";

    /// <summary>Reads a short name (<c>3x3</c> or <c>2x2</c>), or throws if it is neither.</summary>
    public static PillarLayout Parse(string text) =>
        TryParse(text, out var layout) ? layout : throw new ArgumentException($"Unknown Pillar layout \"{text}\"; use 3x3 or 2x2.", nameof(text));

    /// <summary>Reads a short name (<c>3x3</c> or <c>2x2</c>).</summary>
    public static bool TryParse(string text, out PillarLayout layout)
    {
        foreach (var candidate in All)
            if (string.Equals(text.Trim(), candidate.Name(), StringComparison.OrdinalIgnoreCase))
            {
                layout = candidate;
                return true;
            }
        layout = default;
        return false;
    }
}
