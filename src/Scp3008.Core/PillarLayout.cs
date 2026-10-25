namespace Scp3008.Core;

/// <summary>
/// The world setting that chooses the Sector grid, which Sectors have a Pillar at their centre,
/// and the size of the Store. It is part of the world config and the world state; the seed never
/// changes it.
/// </summary>
public enum PillarLayout
{
    /// <summary>Nine Pillars 448 m apart, at 224, 672 and 1120 m on each axis; nine 448 m Sectors (the default).</summary>
    ThreeByThree,

    /// <summary>Four Pillars 672 m apart, at 336 and 1008 m on each axis; four 672 m Sectors.</summary>
    TwoByTwo,

    /// <summary>
    /// Nine Pillars 640 m apart, at 320, 960 and 1600 m on each axis, on the larger 1920 m Store;
    /// nine 640 m Sectors, close to the 2 × 2 Sector size but on Chunk boundaries.
    /// </summary>
    ThreeByThreeLarge,

    /// <summary>
    /// The 3 × 3 large Sectors with Pillars only in the four corner Sectors (A-1, C-1, A-3, C-3);
    /// across the Seams they stand together as a 640 m square, and the five empty Sectors form
    /// 640 m wide corridors between such squares.
    /// </summary>
    CornersLarge,

    /// <summary>
    /// The 3 × 3 large Sectors with Pillars only in the four edge-middle Sectors (B-1, A-2, C-2,
    /// B-3), a diamond around the empty centre Sector; the four empty corner Sectors join across
    /// the Seams into one 1280 m empty square.
    /// </summary>
    DiamondLarge,

    /// <summary>One Pillar in the 128 m Villager sandbox; never a generated Store layout.</summary>
    VillagerSandbox,
}

/// <summary>Names, grids, Pillar placement and Store sizes of the <see cref="PillarLayout"/>s.</summary>
public static class PillarLayouts
{
    /// <summary>Every layout, in menu order.</summary>
    public static IReadOnlyList<PillarLayout> All { get; } =
        [PillarLayout.ThreeByThree, PillarLayout.TwoByTwo, PillarLayout.ThreeByThreeLarge, PillarLayout.CornersLarge, PillarLayout.DiamondLarge];

    /// <summary>Sectors along each axis of the Store.</summary>
    public static int SectorsPerSide(this PillarLayout layout) => layout switch
    {
        PillarLayout.ThreeByThree or PillarLayout.ThreeByThreeLarge or PillarLayout.CornersLarge or PillarLayout.DiamondLarge => 3,
        PillarLayout.TwoByTwo => 2,
        PillarLayout.VillagerSandbox => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    /// <summary>
    /// Whether a Pillar stands at the centre of the Sector at (<paramref name="column"/>,
    /// <paramref name="row"/>), counted from 0 in the north-west corner.
    /// </summary>
    public static bool HasPillar(this PillarLayout layout, int column, int row) => layout switch
    {
        PillarLayout.CornersLarge => column != 1 && row != 1,
        PillarLayout.DiamondLarge => (column == 1) != (row == 1),
        _ => true,
    };

    /// <summary>How many Pillars stand in the Store.</summary>
    public static int PillarCount(this PillarLayout layout)
    {
        var perSide = layout.SectorsPerSide();
        var count = 0;
        for (var row = 0; row < perSide; row++)
            for (var column = 0; column < perSide; column++)
                if (layout.HasPillar(column, row))
                    count++;
        return count;
    }

    /// <summary>The Store the layout stands in: the standard 1344 m Store, or the 1920 m one for the large layouts.</summary>
    public static StoreSpace Space(this PillarLayout layout) => layout switch
    {
        PillarLayout.VillagerSandbox => new StoreSpace(64f, 2),
        PillarLayout.ThreeByThree or PillarLayout.TwoByTwo => StoreSpace.Standard,
        PillarLayout.ThreeByThreeLarge or PillarLayout.CornersLarge or PillarLayout.DiamondLarge => StoreSpace.Large,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    /// <summary>Edge length of each of the layout's Sectors, in metres.</summary>
    public static float SectorSize(this PillarLayout layout) => layout.Space().Size / layout.SectorsPerSide();

    /// <summary>
    /// The short name, as in the <c>--pillars=</c> dev flag: <c>3x3</c>, <c>2x2</c>, <c>3x3L</c>,
    /// <c>3x3L-corners</c> or <c>3x3L-diamond</c>.
    /// </summary>
    public static string Name(this PillarLayout layout) => layout switch
    {
        PillarLayout.VillagerSandbox => "sandbox",
        PillarLayout.ThreeByThree => "3x3",
        PillarLayout.TwoByTwo => "2x2",
        PillarLayout.ThreeByThreeLarge => "3x3L",
        PillarLayout.CornersLarge => "3x3L-corners",
        PillarLayout.DiamondLarge => "3x3L-diamond",
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    /// <summary>Reads a short name (see <see cref="Name"/>, in any case), or throws if it is none of them.</summary>
    public static PillarLayout Parse(string text) =>
        TryParse(text, out var layout) ? layout : throw new ArgumentException(
            $"Unknown Pillar layout \"{text}\"; use {string.Join(", ", All.Select(l => l.Name()))}.", nameof(text));

    /// <summary>Reads a short name (see <see cref="Name"/>, in any case).</summary>
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
