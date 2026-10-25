using System.Numerics;
using Scp3008.Core;

namespace Scp3008.Core.Tests;

public class CampingGroundTests
{
    private static readonly int[] Seeds = [3008, 42, -1, 1, 2, 3, 4, 5];

    private static (Simulation Sim, Landmark Camp) Camp(int seed)
    {
        var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: 1));
        var camp = sim.Snapshot.Landmarks.Single(l => l.Type == LandmarkType.CampingGround);
        sim.Scenario.SetFogBankFormation(false);
        return (sim, camp);
    }

    private static List<FurniturePiece> PiecesOf(WorldSnapshot snapshot, Landmark camp) =>
        snapshot.Furniture.Where(p => p.LandmarkId == camp.Id).ToList();

    private static List<FurniturePiece> PiecesOf(WorldSnapshot snapshot, Dinosaur dinosaur)
    {
        var ids = dinosaur.PieceIds.ToHashSet();
        return snapshot.Furniture.Where(p => ids.Contains(p.Id)).ToList();
    }

    private static float Top(FurniturePiece piece) => FurnitureCatalog.Corners(piece).Max(c => c.Z);

    [Fact]
    public void Every_camping_ground_has_one_of_each_dinosaur_in_three_different_main_colours()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            Assert.Equal(3, camp.Dinosaurs.Count);
            Assert.Equal(3, camp.Dinosaurs.Select(d => d.Kind).Distinct().Count());
            Assert.Equal(3, camp.Dinosaurs.Select(d => d.MainTint).Distinct().Count());
            Assert.All(camp.Dinosaurs, d => Assert.True(d.MainTint >= FurniturePalettes.FirstDinosaurTint));
            foreach (var dinosaur in camp.Dinosaurs)
            {
                var pieces = PiecesOf(sim.Snapshot, dinosaur);
                Assert.True(pieces.Count >= 25, $"{dinosaur.Kind} has only {pieces.Count} pieces.");
                // Built only of existing Furniture types, mostly in its main colour.
                Assert.All(pieces, p => Assert.DoesNotContain(p.Type, FurnitureCatalog.CampingTypes));
                // A sculpture of whole, recognisable Furniture: sofas or beds for the body, armchairs, beds for the legs; not one type on a grid.
                Assert.True(pieces.Select(p => p.Type).Distinct().Count() >= 4, $"{dinosaur.Kind} is built of too few kinds of Furniture.");
                Assert.Contains(pieces, p => p.Type is FurnitureType.Sofa or FurnitureType.Bed);
                Assert.Contains(pieces, p => p.Tilt != 0f || p.Roll != 0f);
                Assert.True(pieces.Count(p => p.Tint == dinosaur.MainTint) > pieces.Count / 2, $"{dinosaur.Kind} is not mostly its main colour.");
                Assert.True(pieces.Select(p => p.Tint).Distinct().Count() >= 3, $"{dinosaur.Kind} has no accent colours.");
                // The spikes on the back are lamps, lit at night.
                Assert.True(pieces.Count(p => FurnitureCatalog.Spec(p).IsLamp) >= 5, $"{dinosaur.Kind} has too few lamps on its back.");
                Assert.DoesNotContain(pieces, p => p.Type == FurnitureType.KidsChair && p.Tint == (int)FurniturePalettes.DinosaurTints.Yellow);
                if (dinosaur.Kind == DinosaurKind.Triceratops)
                    Assert.True(pieces.Count(p => p.Type == FurnitureType.Bed && p.Tint == (int)FurniturePalettes.DinosaurTints.Yellow) >= 5,
                        "The triceratops' frill is not a fan of beds.");
            }
        }
    }

    [Fact]
    public void The_dinosaurs_are_the_size_the_ticket_asks()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            foreach (var dinosaur in camp.Dinosaurs)
            {
                var pieces = PiecesOf(sim.Snapshot, dinosaur);
                var length = pieces.Max(a => pieces.Max(b => Vector2.Distance(a.Position, b.Position)));
                var height = pieces.Max(Top);
                switch (dinosaur.Kind)
                {
                    case DinosaurKind.TyrannosaurusRex:
                        Assert.InRange(length, 10f, 14.5f);
                        Assert.InRange(height, 5f, 8f);
                        break;
                    case DinosaurKind.Brachiosaurus:
                        Assert.InRange(height, 13.5f, 16f);
                        break;
                    default:
                        Assert.InRange(length, 7.5f, 11f);
                        break;
                }
            }
        }
    }

    [Fact]
    public void The_camping_ground_is_at_most_a_quarter_of_its_sector_clear_of_the_pillars_and_rises_above_the_shelving()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var snapshot = sim.Snapshot;
            var sector = snapshot.Sectors[camp.SectorId];
            Assert.True(camp.Size.X * camp.Size.Y <= sector.Size * sector.Size / 4f);
            Assert.Equal(1, snapshot.Landmarks.Count(l => l.SectorId == camp.SectorId));
            foreach (var pillar in snapshot.Pillars)
            {
                var delta = snapshot.Space.Delta(pillar.Position, camp.Centre);
                Assert.True(MathF.Abs(delta.X) >= pillar.ClearHalfWidth + camp.Size.X / 2f ||
                            MathF.Abs(delta.Y) >= pillar.ClearHalfWidth + camp.Size.Y / 2f);
            }
            Assert.True(camp.Height >= Landmark.MinHeight);
            Assert.Equal(GroundProperty.None, camp.Ground);
        }
    }

    [Fact]
    public void The_seed_decides_the_poses_and_layout_and_the_same_seed_gives_the_same_camp()
    {
        List<FurniturePiece> Signature(int seed, int runtime)
        {
            using var sim = Simulation.Create(new WorldConfig(seed, RuntimeRngSeed: runtime));
            var camp = sim.Snapshot.Landmarks.Single(l => l.Type == LandmarkType.CampingGround);
            return PiecesOf(sim.Snapshot, camp);
        }

        Assert.Equal(Signature(42, 1), Signature(42, 9));

        var poses = Seeds.Select(seed =>
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            return string.Join(";", camp.Dinosaurs.Select(d =>
            {
                var pieces = PiecesOf(sim.Snapshot, d);
                var first = pieces[0].Position;
                return $"{d.Kind}:{pieces.Count}:" + string.Join(",", pieces.Take(40).Select(p => $"{p.Type}{MathF.Round(p.Position.X - first.X, 1)}{MathF.Round(p.Position.Y - first.Y, 1)}"));
            }));
        }).ToList();
        Assert.True(poses.Distinct().Count() >= Seeds.Length - 1, "The seed hardly varies the dinosaurs.");
        var layouts = Seeds.Select(seed =>
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var tents = PiecesOf(sim.Snapshot, camp).Where(p => p.Type is FurnitureType.Tent or FurnitureType.LanternTent);
            return string.Join(";", tents.Select(t => $"{t.Type}{t.Variant}{MathF.Round(t.Position.X - camp.Centre.X)}"));
        }).ToList();
        Assert.True(layouts.Distinct().Count() >= Seeds.Length - 1, "The seed hardly varies the camp.");
    }

    [Fact]
    public void The_landmark_showcase_includes_the_camping_ground()
    {
        using var sim = Simulation.Create(new WorldConfig(3008, RuntimeRngSeed: 1, LandmarkShowcase: true));
        var camp = Assert.Single(sim.Snapshot.Landmarks, l => l.Type == LandmarkType.CampingGround);
        Assert.Equal(3, camp.Dinosaurs.Count);
        Assert.NotEmpty(PiecesOf(sim.Snapshot, camp));
    }

    [Fact]
    public void The_camp_has_a_tent_show_of_sixteen_tents_with_one_lit_trees_bushes_and_the_small_things()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var pieces = PiecesOf(sim.Snapshot, camp);
            // Sixteen tents, every kind at least twice, in four straight rows of four: two facing south, two facing north.
            var tents = pieces.Where(p => p.Type == FurnitureType.Tent).ToList();
            Assert.Equal(16, tents.Count);
            Assert.All(Enum.GetValues<TentKind>(), kind => Assert.True(tents.Count(p => p.Variant == (int)kind) >= 2, $"Too few {kind} tents."));
            var facings = tents.GroupBy(p => (MathF.Round(Front(p).X), MathF.Round(Front(p).Y))).ToList();
            Assert.Equal(2, facings.Count);
            Assert.True(Vector2.Dot(Front(facings[0].First()), Front(facings[1].First())) < -0.99f, "The rows do not face each other.");
            foreach (var facing in facings)
            {
                // A row's tents stand side by side with their fronts on one line, along an aisle.
                var rows = facing.GroupBy(p => MathF.Round(Vector2.Dot(p.Position, Front(p)) + FurnitureCatalog.Spec(p).Depth / 2f)).ToList();
                Assert.Equal(2, rows.Count);
                Assert.All(rows, row => Assert.Equal(4, row.Count()));
            }
            // One tent is lit: its lantern stands in its doorway.
            var lit = Assert.Single(pieces, p => p.Type == FurnitureType.LanternTent);
            Assert.Contains(pieces, p => p.Type == FurnitureType.Tent &&
                Vector2.Distance(p.Position, lit.Position) < MathF.Max(FurnitureCatalog.Spec(p).Width, FurnitureCatalog.Spec(p).Depth) / 2f);
            Assert.True(pieces.Count(p => p.Type == FurnitureType.Tree && p.Variant < 2) >= 6);
            Assert.True(pieces.Count(p => p.Type == FurnitureType.Tree && p.Variant >= 2) >= 15, "Too few bushes.");
            foreach (var type in new[]
            {
                FurnitureType.CampfireFlame, FurnitureType.LogSeat, FurnitureType.Hammock, FurnitureType.Kayak, FurnitureType.FishingRod,
                FurnitureType.RubberDuck, FurnitureType.PlushBear, FurnitureType.TrailSign, FurnitureType.Axe, FurnitureType.BunkBed,
                FurnitureType.Rug, FurnitureType.TreeHouse, FurnitureType.MarshmallowTray, FurnitureType.CampBanner, FurnitureType.PriceTag,
                FurnitureType.CampChair, FurnitureType.CampLantern, FurnitureType.CampfireRing,
            })
                Assert.Contains(pieces, p => p.Type == type);
            // Every tent has its price tag.
            Assert.True(pieces.Count(p => p.Type == FurnitureType.PriceTag) > pieces.Count(p => p.Type is FurnitureType.Tent or FurnitureType.LanternTent));
            // Straight aisles like a store's: a spine and three aisles across it, each of at most two straight stretches.
            Assert.True(camp.Paths.Count >= 4);
            foreach (var path in camp.Paths)
            {
                var turns = path.Zip(path.Skip(1), (a, b) => Vector2.Normalize(b - a)).Zip(path.Skip(1).Zip(path.Skip(2), (a, b) => Vector2.Normalize(b - a)))
                    .Count(t => Vector2.Dot(t.First, t.Second) < 0.999f);
                Assert.True(turns <= 1, "An aisle winds.");
            }
        }
    }

    private static Vector2 Front(FurniturePiece p) => new(-MathF.Sin(p.Rotation), MathF.Cos(p.Rotation));

    [Fact]
    public void The_camp_has_a_sun_lounger_section_and_a_sleeping_bag_section_laid_out_in_rows()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var pieces = PiecesOf(sim.Snapshot, camp);
            // Wooden and white loungers in pairs under beach umbrellas, and a row of deck chairs, all facing one way.
            var loungers = pieces.Where(p => p.Type == FurnitureType.SunLounger).ToList();
            foreach (var variant in new[] { 0, 1 })
                Assert.True(loungers.Count(p => p.Variant == variant) >= 20, $"Too few sun loungers of variant {variant}.");
            Assert.True(loungers.Count(p => p.Variant == 2) >= 30, "Too few deck chairs.");
            Assert.Single(loungers.Select(p => (MathF.Round(Front(p).X), MathF.Round(Front(p).Y))).Distinct());
            Assert.True(pieces.Count(p => p.Type == FurnitureType.Parasol && p.Variant == 1) >= loungers.Count(p => p.Variant < 2) / 2);
            // Sleeping bags laid out on the lawn and on mattresses, and rolled up on display tables.
            var bags = pieces.Where(p => p.Type == FurnitureType.SleepingBag).ToList();
            Assert.True(bags.Count(p => p.Variant == 0) >= 40, "Too few sleeping bags laid out.");
            var tables = pieces.Where(p => p.Type == FurnitureType.DisplayTable).ToList();
            Assert.True(tables.Count >= 5);
            var height = FurnitureCatalog.Spec(FurnitureType.DisplayTable).Height;
            Assert.All(tables, t => Assert.True(bags.Count(p => p.Variant == 1 && p.Elevation >= height - 0.01f &&
                Vector2.Distance(p.Position, t.Position) < 1f) >= 9, "A display table holds too few rolled sleeping bags."));
            Assert.Contains(bags, p => p.Variant == 0 && pieces.Any(m => m.Type == FurnitureType.Mattress &&
                Vector2.Distance(m.Position, p.Position) < 0.1f && MathF.Abs(p.Elevation - FurnitureCatalog.Spec(m).Height) < 0.01f));
            // Price tags with the displays.
            Assert.True(pieces.Count(p => p.Type == FurnitureType.PriceTag) >= 45, "Too few price tags.");
        }
    }

    [Fact]
    public void The_tree_house_shell_is_fixed_and_furnished_and_nothing_else_is_fixed()
    {
        foreach (var type in Enum.GetValues<FurnitureType>())
            for (var v = 0; v < FurnitureCatalog.VariantCount(type); v++)
                Assert.Equal(type == FurnitureType.TreeHouse, FurnitureCatalog.Spec(type, v).IsFixed);
        var (sim, camp) = Camp(42);
        using var _ = sim;
        var pieces = PiecesOf(sim.Snapshot, camp);
        var shell = Assert.Single(pieces, p => p.Type == FurnitureType.TreeHouse);
        // Its deck holds a bunk bed: ordinary Furniture a Village may take.
        Assert.Contains(pieces, p => p.Type == FurnitureType.BunkBed && Vector2.Distance(p.Position, shell.Position) < 4.5f &&
                                     MathF.Abs(p.Elevation - CampingGround.TreeHouseDeckHeight) < 0.01f);
    }

    [Fact]
    public void The_campfire_lamp_is_lit_only_at_closing_time()
    {
        var (sim, camp) = Camp(3008);
        using var _ = sim;
        var flame = Assert.Single(PiecesOf(sim.Snapshot, camp), p => p.Type == FurnitureType.CampfireFlame);
        Assert.True(FurnitureCatalog.Spec(flame).IsLamp);
        sim.AdvanceTo(TimeSpan.FromHours(12));
        Assert.Equal(DayPhase.Open, sim.Snapshot.Phase);
        Assert.Equal(0f, sim.Snapshot.LampLevel(flame));
        sim.AdvanceTo(TimeSpan.FromHours(3));
        Assert.Equal(DayPhase.ClosingTime, sim.Snapshot.Phase);
        Assert.True(sim.Snapshot.LampLevel(flame) > 0.5f);
        sim.AdvanceTo(TimeSpan.FromHours(12));
        Assert.Equal(0f, sim.Snapshot.LampLevel(flame));
    }

    [Fact]
    public void The_tree_house_deck_is_the_only_lookout()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var pieces = PiecesOf(sim.Snapshot, camp);
            bool StandsOn(Vector3 l, FurniturePiece p) =>
                Top(p) >= l.Z - 0.3f && Top(p) <= l.Z + 0.1f && Vector2.Distance(p.Position, new Vector2(l.X, l.Y)) <= 1.8f;
            // The tree house's deck, inside the fixed shell, which has its own stair up to it.
            var deck = Assert.Single(camp.Lookouts);
            var shell = pieces.Single(p => p.Type == FurnitureType.TreeHouse);
            Assert.Equal(CampingGround.TreeHouseDeckHeight + FurnitureCatalog.Spec(FurnitureType.Mattress).Height, deck.Z, 3);
            Assert.True(Vector2.Distance(shell.Position, new Vector2(deck.X, deck.Y)) < FurnitureCatalog.Spec(shell).Width / 2f);
            Assert.Contains(pieces, p => p.Type == FurnitureType.Mattress && StandsOn(deck, p));
        }
    }

    [Fact]
    public void The_marshmallow_stand_holds_marshmallows_beside_the_campfire_and_other_camp_goods_are_listed()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var snapshot = sim.Snapshot;
            var stand = Assert.Single(snapshot.CampGoods, g => g.Kind == CampGoodsKind.Marshmallows);
            var piece = snapshot.Furniture.Single(p => p.Id == stand.StandId);
            Assert.Equal(camp.Id, piece.LandmarkId);
            var flame = PiecesOf(snapshot, camp).Single(p => p.Type == FurnitureType.CampfireFlame);
            Assert.InRange(Vector2.Distance(piece.Position, flame.Position), 2f, 9f);
            // The marshmallows show: trays of them on sticks stand on the stand's top.
            var top = FurnitureCatalog.Spec(piece).Height;
            Assert.Contains(PiecesOf(snapshot, camp), p => p.Type == FurnitureType.MarshmallowTray &&
                Vector2.Distance(p.Position, piece.Position) < 1f && MathF.Abs(p.Elevation - top) < 0.01f);
            Assert.True(PiecesOf(snapshot, camp).Count(p => p.Type == FurnitureType.MarshmallowStick &&
                Vector2.Distance(p.Position, piece.Position) < 1.2f && p.Elevation > top) >= 30, "The tasting stand has too few marshmallows.");
            Assert.Contains(snapshot.CampGoods, g => g.Kind == CampGoodsKind.TinnedFood);
            Assert.Contains(snapshot.CampGoods, g => g.Kind == CampGoodsKind.Backpack);
            Assert.All(snapshot.CampGoods, g => Assert.Contains(snapshot.Furniture, p => p.Id == g.StandId));
        }
    }

    [Fact]
    public void The_axe_is_a_separate_removable_piece_and_its_tree_stands_without_it()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var tree = sim.Snapshot.Furniture.Single(p => p.Id == camp.AxeTreeId);
            var axe = sim.Snapshot.Furniture.Single(p => p.Id == camp.AxeId);
            Assert.Equal(FurnitureType.Tree, tree.Type);
            Assert.Equal(FurnitureType.Axe, axe.Type);
            Assert.NotEqual(tree.Id, axe.Id);
            Assert.InRange(Vector2.Distance(tree.Position, axe.Position), 0.3f, 2.5f);
            Assert.InRange(axe.Elevation, 1.0f, 1.6f);
            // Beside a visitor path, on the path's side of the trunk.
            var nearest = camp.Paths.SelectMany(p => p).MinBy(p => Vector2.Distance(p, tree.Position));
            Assert.True(Vector2.Distance(nearest, tree.Position) < CampingGround.PathWidth / 2f + 4f, "The axe's tree is far from the paths.");
            Assert.True(Vector2.Distance(nearest, axe.Position) < Vector2.Distance(nearest, tree.Position), "The axe faces away from the path.");

            Assert.Equal(1, sim.Scenario.RemoveFurniture(p => p.Id == axe.Id));
            Assert.DoesNotContain(sim.Snapshot.Furniture, p => p.Type == FurnitureType.Axe && p.LandmarkId == camp.Id);
            var after = sim.Snapshot.Furniture.Single(p => p.Type == FurnitureType.Tree && p.Position == tree.Position && p.LandmarkId == camp.Id);
            Assert.Equal(tree.Elevation, after.Elevation);
            Assert.True(Top(after) >= 5f);
        }
    }

    [Fact]
    public void The_dinosaurs_can_be_disassembled_like_any_furniture()
    {
        var (sim, camp) = Camp(3008);
        using var _ = sim;
        var dinosaur = camp.Dinosaurs[0];
        var ids = dinosaur.PieceIds.ToHashSet();
        var before = sim.Snapshot.Furniture.Count;
        var removed = sim.Scenario.RemoveFurniture(p => ids.Contains(p.Id));
        Assert.Equal(ids.Count, removed);
        Assert.Equal(before - removed, sim.Snapshot.Furniture.Count);
        Assert.DoesNotContain(sim.Snapshot.Furniture, p => ids.Contains(p.Id));
    }

    [Fact]
    public void Camping_furniture_kinds_are_ordinary_catalog_types_only_in_landmarks()
    {
        foreach (var type in FurnitureCatalog.CampingTypes)
        {
            Assert.Contains(type, FurnitureCatalog.LandmarkTypes);
            Assert.True(FurnitureCatalog.Spec(type).Width > 0f);
        }
        Assert.True(FurnitureCatalog.Spec(FurnitureType.LanternTent).IsLamp);
        Assert.True(FurnitureCatalog.Spec(FurnitureType.CampfireFlame).IsLamp);
    }
}
