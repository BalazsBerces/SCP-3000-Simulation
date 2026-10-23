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
                Assert.True(pieces.Count >= 60, $"{dinosaur.Kind} has only {pieces.Count} pieces.");
                // Built only of existing Furniture types, mostly in its main colour.
                Assert.All(pieces, p => Assert.DoesNotContain(p.Type, FurnitureCatalog.CampingTypes));
                Assert.True(pieces.Count(p => p.Tint == dinosaur.MainTint) > pieces.Count / 2, $"{dinosaur.Kind} is not mostly its main colour.");
                Assert.True(pieces.Select(p => p.Tint).Distinct().Count() >= 3, $"{dinosaur.Kind} has no accent colours.");
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
            Assert.Equal(1, snapshot.Landmarks.Count(l => l.SectorId == camp.SectorId && l.Type != LandmarkType.AbandonedScaffold));
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
    public void The_camp_has_tents_of_three_kinds_with_one_lit_trees_and_the_small_things()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var pieces = PiecesOf(sim.Snapshot, camp);
            var tents = pieces.Where(p => p.Type == FurnitureType.Tent).Select(p => p.Variant).Distinct().Count();
            Assert.Equal(3, tents);
            Assert.NotEmpty(pieces.Where(p => p.Type == FurnitureType.LanternTent));
            Assert.True(pieces.Count(p => p.Type == FurnitureType.Tree) >= 6);
            Assert.True(pieces.Count(p => p.Type is FurnitureType.SleepingBag or FurnitureType.Mattress) >= 3);
            foreach (var type in new[]
            {
                FurnitureType.CampfireFlame, FurnitureType.LogSeat, FurnitureType.Hammock, FurnitureType.Kayak, FurnitureType.FishingRod,
                FurnitureType.RubberDuck, FurnitureType.PlushBear, FurnitureType.TrailSign, FurnitureType.Axe, FurnitureType.BunkBed,
                FurnitureType.Wardrobe, FurnitureType.Rug,
            })
                Assert.Contains(pieces, p => p.Type == type);
            Assert.True(camp.Paths.Count >= 3);
            // Winding, not straight: each path is longer than the distance between its ends.
            foreach (var path in camp.Paths)
            {
                var length = path.Zip(path.Skip(1), Vector2.Distance).Sum();
                Assert.True(length > Vector2.Distance(path[0], path[^1]) * 1.05f, "A visitor path is a straight line.");
            }
        }
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
    public void The_tree_house_and_the_brachiosaurus_have_lookouts_that_can_be_reached_on_foot()
    {
        foreach (var seed in Seeds)
        {
            var (sim, camp) = Camp(seed);
            using var _ = sim;
            var pieces = PiecesOf(sim.Snapshot, camp);
            Assert.Equal(2, camp.Lookouts.Count);
            var reached = Reachable(pieces);
            var brachio = camp.Dinosaurs.Single(d => d.Kind == DinosaurKind.Brachiosaurus);
            var brachioIds = brachio.PieceIds.ToHashSet();
            foreach (var lookout in camp.Lookouts)
            {
                Assert.True(lookout.Z >= 5f, $"Lookout at {lookout.Z:0.0} m is low.");
                Assert.Contains(reached, p => Top(p) >= lookout.Z - 0.3f && Top(p) <= lookout.Z + 0.1f &&
                    Vector2.Distance(p.Position, new Vector2(lookout.X, lookout.Y)) <= 1.8f);
            }
            // One is on the Brachiosaurus's back, standing on its pieces; the other is the tree house, high in the giant tree.
            var onBack = camp.Lookouts.Where(l => pieces.Any(p => brachioIds.Contains(p.Id) && Top(p) >= l.Z - 0.3f && Top(p) <= l.Z + 0.1f &&
                Vector2.Distance(p.Position, new Vector2(l.X, l.Y)) <= 1.8f)).ToList();
            Assert.Single(onBack);
            Assert.Contains(camp.Lookouts, l => l.Z > 9f);
        }
    }

    /// <summary>The pieces a walker can climb on from the ground: no step up higher than 0.85 m, no gap wider than 0.4 m.</summary>
    private static HashSet<FurniturePiece> Reachable(List<FurniturePiece> pieces)
    {
        var tops = pieces.Select(Top).ToArray();
        var radii = pieces.Select(p => MathF.Max(FurnitureCatalog.Spec(p).Width, FurnitureCatalog.Spec(p).Depth) / 2f).ToArray();
        var seen = new bool[pieces.Count];
        var queue = new Queue<int>();
        for (var i = 0; i < pieces.Count; i++)
            if (tops[i] <= 0.85f)
            {
                seen[i] = true;
                queue.Enqueue(i);
            }
        while (queue.Count > 0)
        {
            var a = queue.Dequeue();
            for (var b = 0; b < pieces.Count; b++)
                if (!seen[b] && tops[b] - tops[a] <= 0.85f && Vector2.Distance(pieces[a].Position, pieces[b].Position) <= radii[a] + radii[b] + 0.4f)
                {
                    seen[b] = true;
                    queue.Enqueue(b);
                }
        }
        return pieces.Where((_, i) => seen[i]).ToHashSet();
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
            Assert.InRange(axe.Elevation, 0.5f, 3f);

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
