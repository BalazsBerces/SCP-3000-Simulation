using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

/// <summary>The benchmark Store (seed 3008, 3 × 3 Pillars), generated once for all layout tests.</summary>
public sealed class BenchmarkStore
{
    public BenchmarkStore()
    {
        using var sim = Simulation.Create(new WorldConfig(Seed: 3008, RuntimeRngSeed: 1));
        Snapshot = sim.Snapshot;
        PiecesOf = Snapshot.Furniture.GroupBy(p => p.DisplayId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public WorldSnapshot Snapshot { get; }

    /// <summary>The pieces of each Display, by Display id.</summary>
    public Dictionary<int, List<FurniturePiece>> PiecesOf { get; }

    public DepartmentType DepartmentTypeOf(Display display) => Snapshot.Departments.All[display.DepartmentId].Type;
}

public class FurnitureLayoutTests(BenchmarkStore store) : IClassFixture<BenchmarkStore>
{
    // The Department types that were laid out as room-sets before layout archetypes (ticket 03a).
    private static readonly DepartmentType[] RoomSetDepartments =
    [
        DepartmentType.Showrooms, DepartmentType.BathroomsAndKitchens, DepartmentType.KidsArea,
        DepartmentType.Workspaces, DepartmentType.Outdoor, DepartmentType.DiningAndHallway,
    ];

    // The types that got variants.
    private static readonly FurnitureType[] VariedTypes =
    [
        FurnitureType.Chair, FurnitureType.OfficeChair, FurnitureType.Desk, FurnitureType.DiningTable, FurnitureType.GardenChair,
        FurnitureType.GardenTable, FurnitureType.Sofa, FurnitureType.Wardrobe, FurnitureType.KitchenCabinet, FurnitureType.DisplayCabinet,
    ];

    private WorldSnapshot Snapshot => store.Snapshot;

    private IEnumerable<IGrouping<DepartmentType, CellLayout>> CellsByDepartmentType() =>
        Snapshot.CellLayouts.GroupBy(c => Snapshot.Departments.AtCell(c.X, c.Y).Type);

    [Fact]
    public void Every_department_type_is_laid_out_by_at_least_four_archetypes_none_taking_over_40_percent()
    {
        foreach (var cells in CellsByDepartmentType())
        {
            var shares = cells.GroupBy(c => c.Archetype).ToDictionary(g => g.Key, g => g.Count() / (float)cells.Count());
            Assert.True(shares.Count >= 4, $"{cells.Key} uses only {string.Join(", ", shares.Keys)}.");
            Assert.All(shares, s => Assert.True(s.Value <= 0.4f, $"{s.Key} lays out {s.Value:P0} of {cells.Key}."));
        }
        // No archetype is shared between Department types.
        Assert.All(Snapshot.CellLayouts.GroupBy(c => c.Archetype),
            g => Assert.Single(g.Select(c => Snapshot.Departments.AtCell(c.X, c.Y).Type).Distinct()));
    }

    [Fact]
    public void The_theme_catalogue_lists_every_furniture_type_and_every_department_type_has_themes()
    {
        var listed = ThemeCatalog.All.SelectMany(t => t.Types).ToHashSet();

        // Landmark and Point of interest Furniture is never in a Display.
        Assert.Equal(Enum.GetValues<FurnitureType>().Except(FurnitureCatalog.LandmarkTypes).Except(FurnitureCatalog.PointOfInterestTypes).ToHashSet(), listed);
        Assert.Equal(Enum.GetValues<Theme>().ToHashSet(), ThemeCatalog.All.Select(t => t.Theme).ToHashSet());
        foreach (var department in Enum.GetValues<DepartmentType>())
        {
            Assert.NotEmpty(ThemeCatalog.ThemesOf(department));
            Assert.All(ThemeCatalog.ThemesOf(department), t => Assert.True(t.Weight > 0f));
        }
        Assert.All(ThemeCatalog.All, theme =>
        {
            Assert.Contains(theme.Slots, s => s.Tier == ThemeTier.Core);
            Assert.All(theme.Slots.Where(s => s.Tier != ThemeTier.Core), s => Assert.InRange(s.Odds, 0.01f, 1f));
        });
        // Every theme is shown by some Department type.
        Assert.Equal(Enum.GetValues<Theme>().ToHashSet(),
            Enum.GetValues<DepartmentType>().SelectMany(d => ThemeCatalog.ThemesOf(d)).Select(t => t.Theme).ToHashSet());
    }

    [Fact]
    public void Every_piece_stands_in_one_display_of_a_theme_that_lists_it_and_is_allowed_in_its_department()
    {
        var displays = Snapshot.Displays;
        Assert.Equal(Enumerable.Range(0, displays.Count), displays.Select(d => d.Id));
        foreach (var display in displays)
            Assert.Contains(ThemeCatalog.ThemesOf(store.DepartmentTypeOf(display)), t => t.Theme == display.Theme);

        var offTheme = Snapshot.Furniture.Where(p => p.LandmarkId < 0 && p.PointOfInterestId < 0).Where(p => p.DisplayId < 0 || p.DisplayId >= displays.Count
            || !ThemeCatalog.Spec(displays[p.DisplayId].Theme).Types.Contains(p.Type)).Take(5).ToList();
        Assert.True(offTheme.Count == 0, "Off theme: " + string.Join("; ", offTheme));

        // And the piece really stands in that Display's area, in its Department.
        var outside = Snapshot.Furniture.Where(p => p.LandmarkId < 0 && p.PointOfInterestId < 0).Where(p =>
        {
            var display = displays[p.DisplayId];
            var offset = Snapshot.Space.Delta(display.Centre, p.Position);
            return MathF.Abs(offset.X) > display.Size.X / 2f + 0.15f || MathF.Abs(offset.Y) > display.Size.Y / 2f + 0.15f
                || Snapshot.Departments.At(p.Position).Id != display.DepartmentId;
        }).Take(5).ToList();
        Assert.True(outside.Count == 0, "Outside their Display: " + string.Join("; ", outside));
    }

    [Theory]
    [InlineData(42)]
    [InlineData(-1)]
    public void Themes_stay_strict_and_complete_on_other_seeds(int seed)
    {
        var snapshot = TestStores.Get(seed);
        var pieces = snapshot.Furniture.GroupBy(p => p.DisplayId).ToDictionary(g => g.Key, g => g.Select(p => p.Type).ToHashSet());

        Assert.All(snapshot.Furniture.Where(p => p.LandmarkId < 0 && p.PointOfInterestId < 0), p => Assert.Contains(p.Type, ThemeCatalog.Spec(snapshot.Displays[p.DisplayId].Theme).Types));
        Assert.All(snapshot.Displays, d =>
        {
            Assert.Contains(ThemeCatalog.ThemesOf(snapshot.Departments.All[d.DepartmentId].Type), t => t.Theme == d.Theme);
            foreach (var slot in ThemeCatalog.Spec(d.Theme).Slots.Where(s => s.Tier == ThemeTier.Core && s.Min > 0))
                Assert.True(slot.Types.Any(pieces[d.Id].Contains), $"Display {d.Id} ({d.Theme}) lacks {string.Join(" or ", slot.Types)}.");
        });
    }

    [Fact]
    public void Every_display_shows_the_core_of_its_theme()
    {
        foreach (var display in Snapshot.Displays)
        {
            var types = store.PiecesOf.GetValueOrDefault(display.Id, []).Select(p => p.Type).ToHashSet();
            // A core slot with alternatives ("CoffeeTable or TvBench") is shown by any one of them.
            foreach (var slot in ThemeCatalog.Spec(display.Theme).Slots.Where(s => s.Tier == ThemeTier.Core && s.Min > 0))
                Assert.True(slot.Types.Any(types.Contains), $"Display {display.Id} ({display.Theme}) lacks {string.Join(" or ", slot.Types)}.");
        }
    }

    [Fact]
    public void Every_display_meets_the_minimum_counts_of_its_core_slots()
    {
        foreach (var display in Snapshot.Displays)
            foreach (var slot in ThemeCatalog.Spec(display.Theme).Slots.Where(s => s.Tier == ThemeTier.Core))
            {
                var count = store.PiecesOf[display.Id].Count(p => slot.Types.Contains(p.Type));
                Assert.True(count >= slot.Min,
                    $"Display {display.Id} ({display.Theme}) has {count} of {string.Join(" or ", slot.Types)}; needs {slot.Min}.");
            }
    }

    [Fact]
    public void Every_common_and_accent_type_appears_and_accents_stay_rare()
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var displays = Snapshot.Displays.Where(d => d.Theme == theme.Theme).ToList();
            Assert.True(displays.Count > 0, $"No {theme.Theme} Display.");
            var shown = displays.Select(d => store.PiecesOf[d.Id].Select(p => p.Type).ToHashSet()).ToList();
            var coreOrCommon = theme.Slots.Where(s => s.Tier != ThemeTier.Accent).SelectMany(s => s.Types).ToHashSet();
            foreach (var slot in theme.Slots.Where(s => s.Tier != ThemeTier.Core))
                foreach (var type in slot.Types)
                {
                    var share = shown.Count(s => s.Contains(type)) / (float)shown.Count;
                    Assert.True(share > 0f, $"{type} ({slot.Tier}) never appears in a {theme.Theme}.");
                    if (slot.Tier == ThemeTier.Accent && !coreOrCommon.Contains(type))
                        Assert.True(share <= 0.33f, $"Accent {type} appears in {share:P0} of {displays.Count} {theme.Theme} Displays.");
                }
        }
    }

    [Fact]
    public void Every_department_type_mixes_in_six_foreign_types_but_stays_mostly_its_own()
    {
        // Landmarks stand on any Department's floor, so only the Departments' own Furniture counts.
        foreach (var pieces in Snapshot.Furniture.Where(p => p.LandmarkId < 0 && p.PointOfInterestId < 0)
                     .GroupBy(p => Snapshot.Departments.At(p.Position).Type))
        {
            var own = ThemeCatalog.OwnTypes(pieces.Key);
            var foreign = pieces.Select(p => p.Type).Distinct().Where(t => !own.Contains(t)).ToList();
            var ownShare = pieces.Count(p => own.Contains(p.Type)) / (float)pieces.Count();
            Assert.True(foreign.Count >= 6, $"{pieces.Key} mixes in only {string.Join(", ", foreign)}.");
            Assert.True(ownShare > 0.5f, $"Only {ownShare:P0} of {pieces.Key}'s pieces are its own types.");
        }
    }

    [Fact]
    public void The_gaps_in_the_older_departments_are_filled()
    {
        // Nightstands by beds in the Showrooms.
        Assert.Contains(Pairs(DepartmentType.Showrooms, FurnitureType.Bed, FurnitureType.Nightstand), d => d < 1.6f);
        // Stools at restaurant counters.
        Assert.Contains(Pairs(DepartmentType.RestaurantAndFoodMarket, FurnitureType.Counter, FurnitureType.BarStool), d => d < 1.4f);
        // A table or stools in kitchens.
        var kitchens = Snapshot.Displays.Where(d => d.Theme == Theme.Kitchen).ToList();
        var furnished = kitchens.Count(k => store.PiecesOf[k.Id].Any(p => p.Type is FurnitureType.DiningTable or FurnitureType.BarStool));
        Assert.True(furnished > kitchens.Count / 3, $"Only {furnished} of {kitchens.Count} kitchens have a table or stools.");
        // A desk corner, with its lamp, in the Kids' area.
        Assert.Contains(Pairs(DepartmentType.KidsArea, FurnitureType.Desk, FurnitureType.DeskLamp), d => d < 1f);
    }

    /// <summary>Distances from each piece of the first type to the nearest one of the second type in the same Display.</summary>
    private IEnumerable<float> Pairs(DepartmentType department, FurnitureType first, FurnitureType second) =>
        Snapshot.Displays.Where(d => store.DepartmentTypeOf(d) == department)
            .SelectMany(d => store.PiecesOf[d.Id].Where(p => p.Type == first).Select(a =>
                store.PiecesOf[d.Id].Where(p => p.Type == second).Select(b => Snapshot.Space.Distance(a.Position, b.Position))
                    .DefaultIfEmpty(float.MaxValue).Min()));

    [Fact]
    public void Room_set_departments_use_several_room_partitions_and_two_by_two_for_at_most_half_their_cells()
    {
        foreach (var cells in CellsByDepartmentType().Where(g => RoomSetDepartments.Contains(g.Key)))
        {
            var partitions = cells.Where(c => c.Partition != RoomPartition.None).Select(c => c.Partition).Distinct().ToList();
            var twoByTwo = cells.Count(c => c.Partition == RoomPartition.FourRooms) / (float)cells.Count();
            Assert.True(partitions.Count >= 3, $"{cells.Key} uses only {string.Join(", ", partitions)}.");
            Assert.True(twoByTwo <= 0.5f, $"{cells.Key} splits {twoByTwo:P0} of its cells two by two.");
        }
    }

    [Fact]
    public void No_cell_layout_repeats_across_a_department_type_or_next_door()
    {
        var map = Snapshot.Departments;
        var size = map.CellSize;
        var signatures = Snapshot.Furniture
            .GroupBy(p => ((int)(p.Position.X / size), (int)(p.Position.Y / size)))
            .ToDictionary(g => g.Key, g => Signature(g, new Vector2((g.Key.Item1 + 0.5f) * size, (g.Key.Item2 + 0.5f) * size)));

        // (a) No layout covers more than 10% of the furnished cells of a Department type.
        foreach (var cells in signatures.GroupBy(s => map.AtCell(s.Key.Item1, s.Key.Item2).Type))
        {
            var most = cells.GroupBy(s => s.Value).Max(g => g.Count());
            Assert.True(most <= Math.Max(1, cells.Count() / 10), $"One layout fills {most} of {cells.Count()} {cells.Key} cells.");
        }
        // (b) No two edge-adjacent cells of one Department look the same.
        foreach (var ((x, y), signature) in signatures)
            foreach (var next in new[] { ((x + 1) % map.CellsPerSide, y), (x, (y + 1) % map.CellsPerSide) })
                if (map.AtCell(x, y) == map.AtCell(next.Item1, next.Item2) && signatures.TryGetValue(next, out var other))
                    Assert.NotEqual(signature, other);
    }

    [Fact]
    public void No_room_layout_repeats_across_a_room_set_department_type()
    {
        foreach (var displays in Snapshot.Displays
                     .Where(d => Snapshot.CellLayoutAt(d.CellX, d.CellY).Partition != RoomPartition.None)
                     .GroupBy(store.DepartmentTypeOf))
        {
            var signatures = displays.Select(d => Signature(store.PiecesOf[d.Id], d.Centre)).ToList();
            var most = signatures.GroupBy(s => s).Max(g => g.Count());
            Assert.True(most <= Math.Max(1, signatures.Count / 10), $"One room layout fills {most} of {signatures.Count} {displays.Key} rooms.");
        }
    }

    /// <summary>
    /// A layout signature: the sorted multiset of type, variant and position (and height) quantised
    /// to 0.5 m around a centre. Taken under all four quarter turns, keeping the smallest, so a layout
    /// that is merely turned counts as the same.
    /// </summary>
    private string Signature(IEnumerable<FurniturePiece> pieces, Vector2 centre)
    {
        string? best = null;
        var list = pieces.ToList();
        for (var quarters = 0; quarters < 4; quarters++)
        {
            var items = list.Select(p =>
            {
                var d = Snapshot.Space.Delta(centre, p.Position);
                for (var k = 0; k < quarters; k++)
                    d = new Vector2(-d.Y, d.X);
                return $"{p.Type}.{p.Variant}.{MathF.Round(d.X * 2f)}.{MathF.Round(d.Y * 2f)}.{MathF.Round(p.Elevation * 2f)}";
            }).OrderBy(s => s, StringComparer.Ordinal);
            var signature = string.Join("|", items);
            if (best is null || string.CompareOrdinal(signature, best) < 0)
                best = signature;
        }
        return best!;
    }

    [Fact]
    public void Loose_pieces_in_room_sets_are_turned_off_the_quarter_turns()
    {
        var loose = new HashSet<FurnitureType>
        {
            FurnitureType.Chair, FurnitureType.OfficeChair, FurnitureType.GardenChair, FurnitureType.BarStool, FurnitureType.KidsChair,
            FurnitureType.Armchair, FurnitureType.DiningTable, FurnitureType.CoffeeTable, FurnitureType.GardenTable, FurnitureType.KidsTable,
        };
        var pieces = Snapshot.Displays
            .Where(d => Snapshot.CellLayoutAt(d.CellX, d.CellY).Partition != RoomPartition.None)
            .SelectMany(d => store.PiecesOf[d.Id]).Where(p => loose.Contains(p.Type)).ToList();
        // More than 1° off the nearest quarter turn.
        var turned = pieces.Count(p => MathF.Abs(MathF.Sin(2f * p.Rotation)) > MathF.Sin(2f * MathF.PI / 180f));

        Assert.NotEmpty(pieces);
        Assert.True(turned > pieces.Count / 2, $"Only {turned} of {pieces.Count} loose pieces are off the quarter turns.");
    }

    [Fact]
    public void The_most_repeated_types_come_in_variants_that_all_appear()
    {
        foreach (var type in VariedTypes)
        {
            Assert.True(FurnitureCatalog.VariantCount(type) >= 2, $"{type} has no variants.");
            for (var variant = 0; variant < FurnitureCatalog.VariantCount(type); variant++)
            {
                var spec = FurnitureCatalog.Spec(type, variant);
                Assert.Equal((type, variant), (spec.Type, spec.Variant));
                Assert.Contains(Snapshot.Furniture, p => p.Type == type && p.Variant == variant);
            }
        }
        Assert.All(Snapshot.Furniture, p => Assert.InRange(p.Variant, 0, FurnitureCatalog.VariantCount(p.Type) - 1));
    }

    [Fact]
    public void Pieces_of_a_display_share_its_palette_and_neighbouring_displays_differ()
    {
        var palettes = FurniturePalettes.All;
        Assert.All(Snapshot.Displays, d => Assert.InRange(d.Palette, 0, palettes.Count - 1));
        Assert.All(Snapshot.Furniture.Where(p => p.LandmarkId < 0 && p.PointOfInterestId < 0), p => Assert.Contains(p.Tint, palettes[Snapshot.Displays[p.DisplayId].Palette]));

        // Displays next to each other in a cell: their rectangles touch.
        var (pairs, differing) = (0, 0);
        foreach (var cell in Snapshot.Displays.GroupBy(d => (d.CellX, d.CellY)))
        {
            var displays = cell.ToList();
            for (var i = 0; i < displays.Count; i++)
                for (var j = i + 1; j < displays.Count; j++)
                {
                    var gap = Vector2.Abs(Snapshot.Space.Delta(displays[i].Centre, displays[j].Centre)) - (displays[i].Size + displays[j].Size) / 2f;
                    if (gap.X > 0.01f || gap.Y > 0.01f)
                        continue;
                    pairs++;
                    if (displays[i].Palette != displays[j].Palette)
                        differing++;
                }
        }
        Assert.True(pairs > 1000, $"Only {pairs} neighbouring Displays.");
        Assert.True(differing >= pairs * 0.9f, $"Only {differing} of {pairs} neighbouring Displays differ in palette.");
    }

    [Fact]
    public void The_benchmark_store_keeps_furnished_density_with_the_reduced_lamp_share()
    {
        // Ticket 03c supersedes the old Lamp-count budget, retaining the 03b furnished density.
        Assert.InRange(Snapshot.Furniture.Count, 410_806 * 0.9, 410_806 * 1.1);
        Assert.InRange(Snapshot.Furniture.Count(p => FurnitureCatalog.Spec(p).IsLamp) / (double)Snapshot.Furniture.Count, 0.03, 0.04);
    }
}
