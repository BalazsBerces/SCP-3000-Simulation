using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>What the camping ground's builder knows about its pieces beyond their placement, by index into the builder's pieces.</summary>
/// <param name="Dinosaurs">Each dinosaur: kind, main tint, first piece index and piece count (its pieces are contiguous).</param>
/// <param name="Goods">The stands that hold goods: piece index and kind.</param>
/// <param name="Paths">The visitor paths, in the Landmark's own frame.</param>
internal sealed record CampingResult(
    IReadOnlyList<(DinosaurKind Kind, int Tint, int First, int Count)> Dinosaurs,
    int AxeIndex, int AxeTreeIndex,
    IReadOnlyList<(int Index, CampGoodsKind Kind)> Goods,
    IReadOnlyList<IReadOnlyList<Vector2>> Paths);

/// <summary>
/// Builds the camping ground in its own frame (112 x 96 m): a trade-fair camping showroom. Three dinosaurs of Furniture stand in a row along
/// the north, each on a slot the seed picks, with the camp to the south: a fake campfire with log seats and two stands, tents, fake trees, a lake,
/// a hammock, a tree house, a camper van, winding visitor paths and the small things (bear, trail sign, decorative axe).
/// </summary>
internal static class CampingGroundGenerator
{
    private const float HalfWidth = 56f;
    private const float HalfDepth = 48f;
    private const float Step = 0.5f;

    public static CampingResult Build(LandmarkBuilder b)
    {
        var random = b.Random;
        var reserved = new List<(Vector2 Centre, float Radius)>();
        var goods = new List<(int, CampGoodsKind)>();
        var dinosaurs = new List<(DinosaurKind, int, int, int)>();

        // The side the lake is on; the tree house and the camper van go to the other.
        var side = random.Chance(0.5f) ? 1f : -1f;
        var fire = new Vector2(random.Range(-6f, 6f), random.Range(12f, 18f));
        var lake = new Vector2(side * random.Range(33f, 38f), random.Range(4f, 10f));
        var treeHouse = new Vector2(-side * random.Range(36f, 40f), random.Range(30f, 36f));
        var van = new Vector2(-side * random.Range(32f, 38f), random.Range(6f, 12f));
        var hammockTrees = new Vector2(side * random.Range(34f, 38f), random.Range(32f, 38f));

        // The visitor paths, winding, from the south edge to the campfire, on to the dinosaurs, and branching to the lake, tree house and hammock.
        var paths = new List<IReadOnlyList<Vector2>>
        {
            Winding(random, [new Vector2(random.Range(-10f, 10f), HalfDepth - 0.5f), fire + new Vector2(0f, 7f)]),
            Winding(random, [fire + new Vector2(0f, -7f), new Vector2(random.Range(-8f, 8f), -10f)]),
            Winding(random, [fire + new Vector2(side * 5f, 0f), lake - new Vector2(side * 9f, 0f)]),
            Winding(random, [fire + new Vector2(-side * 5f, 3f), treeHouse + new Vector2(side * 5f, -2f)]),
            Winding(random, [lake + new Vector2(-side * 1f, 10f), hammockTrees + new Vector2(-side * 5f, -2f)]),
        };
        var pathPoints = paths.SelectMany(Dense).ToList();

        bool Free(Vector2 at, float radius) =>
            MathF.Abs(at.X) <= HalfWidth - radius - 1f && MathF.Abs(at.Y) <= HalfDepth - radius - 1f &&
            reserved.All(r => Vector2.Distance(r.Centre, at) >= r.Radius + radius) &&
            pathPoints.All(p => Vector2.Distance(p, at) >= radius + CampingGround.PathWidth / 2f + 0.3f);

        Vector2? Spot(float radius, Vector2 centre, float spread)
        {
            for (var tries = 0; tries < 80; tries++)
            {
                var at = centre + new Vector2(random.Range(-spread, spread), random.Range(-spread, spread));
                if (Free(at, radius))
                    return at;
            }
            return null;
        }

        // The dinosaurs: one of each, one on each northern slot.
        var kinds = new List<DinosaurKind> { DinosaurKind.TyrannosaurusRex, DinosaurKind.Brachiosaurus, DinosaurKind.Triceratops };
        for (var i = kinds.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }
        for (var slot = 0; slot < 3; slot++)
        {
            var centre = new Vector2((slot - 1) * 38f + random.Range(-3f, 3f), -28f + random.Range(-3f, 3f));
            reserved.Add((centre, 11f));
            var first = b.Pieces.Count;
            var dinosaur = new DinosaurBuilder(b, centre, random.Range(-MathF.PI, MathF.PI), kinds[slot]);
            dinosaur.Build();
            dinosaurs.Add((kinds[slot], dinosaur.MainTint, first, b.Pieces.Count - first));
        }

        // The campfire: a ring of stones round the glowing flame, log seats with lanterns, and the two stands.
        reserved.Add((fire, 7f));
        b.Put(FurnitureType.CampfireFlame, fire, 0f, random.Range(-MathF.PI, MathF.PI));
        for (var k = 0; k < 5; k++)
        {
            var angle = (k + 0.5f) * MathF.Tau / 5f;
            var at = fire + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 1.7f;
            b.Put(FurnitureType.BasinKerb, at, 0f, Facing(fire - at));
        }
        var standAngle = random.Range(0f, MathF.Tau);
        var supplyAngle = standAngle + 2.4f;
        var lanterns = 0;
        for (var k = 0; k < 8; k++)
        {
            var angle = (k + 0.5f) * MathF.Tau / 8f + 0.2f;
            if (AngleGap(angle, standAngle) < 0.5f || AngleGap(angle, supplyAngle) < 0.5f)
                continue;
            var at = fire + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 3.3f;
            b.Put(FurnitureType.LogSeat, at, 0f, Facing(fire - at));
            if (lanterns < 3 && random.Chance(0.5f))
            {
                b.Put(FurnitureType.TableLamp, at, FurnitureCatalog.Spec(FurnitureType.LogSeat).Height, 0f);
                lanterns++;
            }
        }
        // The marshmallow tasting stand beside the campfire, and a supplies stand.
        var stand = fire + new Vector2(MathF.Cos(standAngle), MathF.Sin(standAngle)) * 5.6f;
        goods.Add((b.Pieces.Count, CampGoodsKind.Marshmallows));
        b.Put(FurnitureType.Counter, stand, 0f, Facing(fire - stand), variant: 0);
        var supply = fire + new Vector2(MathF.Cos(supplyAngle), MathF.Sin(supplyAngle)) * 5.6f;
        goods.Add((b.Pieces.Count, CampGoodsKind.TinnedFood));
        goods.Add((b.Pieces.Count, CampGoodsKind.Backpack));
        b.Put(FurnitureType.Counter, supply, 0f, Facing(fire - supply), variant: 0);

        // The lake: blue rugs in an irregular outline, with a kayak, ducks and a fishing rod.
        var lakeRadius = new Vector2(random.Range(7f, 9f), random.Range(5f, 6.5f));
        reserved.Add((lake, lakeRadius.X + 2f));
        var phase = (random.Range(0f, MathF.Tau), random.Range(0f, MathF.Tau));
        var rug = FurnitureCatalog.Spec(FurnitureType.Rug);
        for (var y = -lakeRadius.Y - rug.Depth; y <= lakeRadius.Y + rug.Depth; y += rug.Depth)
            for (var x = -lakeRadius.X - rug.Width; x <= lakeRadius.X + rug.Width; x += rug.Width)
            {
                var angle = MathF.Atan2(y / lakeRadius.Y, x / lakeRadius.X);
                var edge = 1f + 0.18f * MathF.Sin(2f * angle + phase.Item1) + 0.12f * MathF.Sin(3f * angle + phase.Item2);
                var d = MathF.Sqrt(x * x / (lakeRadius.X * lakeRadius.X) + y * y / (lakeRadius.Y * lakeRadius.Y));
                if (d <= edge)
                    b.Put(FurnitureType.Rug, lake + new Vector2(x, y), 0f, 0f, tint: d > edge - 0.3f ? 4 : 5);
            }
        b.Put(FurnitureType.Kayak, lake + new Vector2(random.Range(-2f, 2f), random.Range(-1f, 1f)), 0.01f, random.Range(-0.5f, 0.5f), tint: random.Next(FurniturePalettes.TintCount));
        for (var k = 0; k < 4; k++)
            b.Put(FurnitureType.RubberDuck, lake + new Vector2(random.Range(-lakeRadius.X * 0.6f, lakeRadius.X * 0.6f), random.Range(-lakeRadius.Y * 0.5f, lakeRadius.Y * 0.5f)),
                0.01f, random.Range(-MathF.PI, MathF.PI));
        b.Put(FurnitureType.FishingRod, lake + new Vector2(lakeRadius.X * 1.05f * -side, lakeRadius.Y * 0.4f) + new Vector2(-side * 0.8f, 0f), 0f, random.Range(-MathF.PI, MathF.PI));

        // The tree house: a bunk bed in a giant tree, up a spiral stair of steps, its roof a Lookout.
        reserved.Add((treeHouse, 6.5f));
        var treeIndex = b.Pieces.Count;
        b.Put(FurnitureType.Tree, treeHouse, 0f, random.Range(-MathF.PI, MathF.PI), variant: 2);
        BuildTreeHouse(b, treeHouse, random.Range(0f, MathF.Tau));

        // The camper van of wardrobes.
        reserved.Add((van, 6f));
        BuildVan(b, van, random.Range(-0.4f, 0.4f) + (random.Chance(0.5f) ? 0f : MathF.PI), random.Next(FurniturePalettes.TintCount));

        // The hammock between two trees.
        reserved.Add((hammockTrees, 5f));
        var hammockHeading = random.Range(-0.4f, 0.4f);
        var hammockAxis = new Vector2(MathF.Cos(hammockHeading), MathF.Sin(hammockHeading));
        b.Put(FurnitureType.Tree, hammockTrees - hammockAxis * 2.7f, 0f, random.Range(-MathF.PI, MathF.PI));
        b.Put(FurnitureType.Tree, hammockTrees + hammockAxis * 2.7f, 0f, random.Range(-MathF.PI, MathF.PI));
        b.Put(FurnitureType.Hammock, hammockTrees, 1.0f, hammockHeading, tint: random.Next(FurniturePalettes.TintCount));

        // Tents of three kinds, one of them lit from inside, and bedding beside them.
        var tentKinds = new List<(FurnitureType Type, int Variant)>
        {
            (FurnitureType.LanternTent, 0), (FurnitureType.Tent, 0), (FurnitureType.Tent, 1), (FurnitureType.Tent, 2),
        };
        var tentCount = 7 + random.Next(3);
        while (tentKinds.Count < tentCount)
            tentKinds.Add((FurnitureType.Tent, random.Next(3)));
        foreach (var (type, variant) in tentKinds)
        {
            var radius = variant == 2 && type == FurnitureType.Tent ? 4.2f : 2.6f;
            var at = Spot(radius, fire + new Vector2(0f, 4f), 26f);
            if (at is null)
                continue;
            reserved.Add((at.Value, radius));
            var heading = random.Range(-MathF.PI, MathF.PI);
            b.Put(type, at.Value, 0f, heading, variant: variant, tint: random.Next(FurniturePalettes.TintCount));
            var outside = at.Value + new Vector2(-MathF.Sin(heading), MathF.Cos(heading)) * (radius + 0.6f);
            if (random.Chance(0.7f))
                b.Put(FurnitureType.SleepingBag, outside, 0f, heading + random.Range(-0.4f, 0.4f), tint: random.Next(FurniturePalettes.TintCount));
            else
                b.Put(FurnitureType.Mattress, outside, 0f, heading, tint: random.Next(FurniturePalettes.TintCount));
        }

        // A plush bear, the trail sign that leads nowhere, and fake trees, one of them with the decorative axe.
        var bear = Spot(1.5f, fire, 16f);
        if (bear is not null)
        {
            reserved.Add((bear.Value, 1.5f));
            b.Put(FurnitureType.PlushBear, bear.Value, 0f, Facing(fire - bear.Value) + random.Range(-0.5f, 0.5f));
        }
        var signAt = paths[0][^2] + new Vector2(2.4f, 0f);
        b.Put(FurnitureType.TrailSign, signAt, 0f, MathF.PI + random.Range(-0.3f, 0.3f));

        var axeTree = -1;
        var axe = -1;
        var trees = 12 + random.Next(5);
        for (var k = 0; k < trees; k++)
        {
            var conifer = random.Chance(0.3f);
            var radius = conifer ? 2.2f : 2.8f;
            var at = Spot(radius, Vector2.Zero, 52f);
            if (at is null)
                continue;
            reserved.Add((at.Value, radius));
            var heading = random.Range(-MathF.PI, MathF.PI);
            var index = b.Pieces.Count;
            if (!b.Put(FurnitureType.Tree, at.Value, 0f, heading, variant: conifer ? 1 : 0, tint: 0))
                continue;
            if (axe < 0 && !conifer && at.Value.Y > -4f)
            {
                // Stuck in the trunk at chest height, its handle sticking out.
                var out2 = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
                axeTree = index;
                axe = b.Pieces.Count;
                b.Put(FurnitureType.Axe, at.Value + out2 * 0.75f, 1.5f, Facing(out2));
            }
        }
        if (axe < 0)
        {
            // No ordinary tree had room: the axe goes in the hammock's first tree.
            axeTree = treeIndex;
            axe = b.Pieces.Count;
            b.Put(FurnitureType.Axe, treeHouse + new Vector2(0.75f, 0f), 1.5f, Facing(new Vector2(1f, 0f)));
        }

        return new CampingResult(dinosaurs, axe, axeTree, goods, paths.Select(p => (IReadOnlyList<Vector2>)p).ToList());
    }

    /// <summary>The rotation of a piece whose front faces along a direction.</summary>
    internal static float Facing(Vector2 direction) => MathF.Atan2(-direction.X, direction.Y);

    private static float AngleGap(float a, float b) => MathF.Abs((float)Math.IEEERemainder(a - b, Math.Tau));

    /// <summary>A winding path through its control points: a smooth sideways wobble along each stretch.</summary>
    private static List<Vector2> Winding(SeededRandom random, Vector2[] controls)
    {
        var points = new List<Vector2>();
        var turns = random.Range(1.6f, 2.4f);
        var phase = random.Range(0f, MathF.Tau);
        for (var i = 0; i + 1 < controls.Length; i++)
        {
            var from = controls[i];
            var to = controls[i + 1];
            var along = to - from;
            var across = Vector2.Normalize(new Vector2(-along.Y, along.X));
            var wobble = Math.Clamp(along.Length() * random.Range(0.12f, 0.18f), 2.5f, 7f);
            var n = Math.Max(6, (int)(along.Length() / 3f));
            for (var k = 0; k <= n; k++)
            {
                var t = k / (float)n;
                var fade = MathF.Sin(t * MathF.PI);
                points.Add(from + along * t + across * MathF.Sin(t * turns * MathF.PI + phase) * wobble * fade);
            }
        }
        return points;
    }

    /// <summary>A path's points, with more between them so that nothing is far from a point.</summary>
    private static IEnumerable<Vector2> Dense(IReadOnlyList<Vector2> path)
    {
        for (var i = 0; i + 1 < path.Count; i++)
            for (var k = 0; k < 4; k++)
                yield return Vector2.Lerp(path[i], path[i + 1], k / 4f);
        yield return path[^1];
    }

    /// <summary>A bunk bed on a deck up a giant tree, a spiral stair of blanket stacks round the trunk to its roof, which is a Lookout.</summary>
    private static void BuildTreeHouse(LandmarkBuilder b, Vector2 trunk, float angle)
    {
        const float deckHeight = 8.8f;
        var radial = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var tangent = new Vector2(-radial.Y, radial.X);
        var bedAt = trunk + radial * 1.45f;
        b.Put(FurnitureType.Mattress, bedAt, deckHeight, angle + MathF.PI / 2f, tint: 5);
        b.Put(FurnitureType.BunkBed, bedAt, deckHeight + 0.22f, Facing(-radial) + MathF.PI / 2f - MathF.PI / 2f, tint: 3);
        var roof = deckHeight + 0.22f + FurnitureCatalog.Spec(FurnitureType.BunkBed).Height;
        // The stair: a step every half metre, a quarter turn and a bit apart, ending beside the bed at its roof.
        var steps = (int)MathF.Round(roof / Step);
        const float turn = 0.42f;
        for (var i = 0; i < steps; i++)
        {
            var a = angle - (steps - 1 - i) * turn;
            var at = trunk + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 2.7f;
            b.Put(FurnitureType.BlanketStack, at, i * Step, a, tint: 2);
        }
        b.Lookouts.Add(new Vector3(bedAt.X, bedAt.Y, roof));
        _ = tangent;
    }

    /// <summary>A camper van of wardrobes: walls of wardrobes, a roof of mattresses, washing machines for wheels.</summary>
    private static void BuildVan(LandmarkBuilder b, Vector2 at, float heading, int tint)
    {
        var along = new Vector2(MathF.Cos(heading), MathF.Sin(heading));
        var across = new Vector2(-along.Y, along.X);
        for (var k = 0; k < 3; k++)
            foreach (var sideSign in new[] { -1f, 1f })
                b.Put(FurnitureType.Wardrobe, at + along * ((k - 1) * 1.6f) + across * (sideSign * 1.0f), 0f, heading + (sideSign > 0 ? MathF.PI : 0f), variant: 1);
        foreach (var end in new[] { -1f, 1f })
            b.Put(FurnitureType.Wardrobe, at + along * (end * 2.6f), 0f, heading + MathF.PI / 2f, variant: 1);
        for (var k = 0; k < 3; k++)
            b.Put(FurnitureType.Mattress, at + along * ((k - 1) * 1.45f), 2.0f, heading + MathF.PI / 2f, tint: tint);
        foreach (var end in new[] { -1f, 1f })
            foreach (var sideSign in new[] { -1f, 1f })
                b.Put(FurnitureType.WashingMachine, at + along * (end * 1.9f) + across * (sideSign * 1.75f), 0f, heading);
    }
}
