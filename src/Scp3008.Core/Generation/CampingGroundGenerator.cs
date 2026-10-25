using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>What the camping ground's builder knows about its pieces beyond their placement, by index into the builder's pieces.</summary>
/// <param name="Dinosaurs">Each dinosaur: kind, main tint, first piece index and piece count (its pieces are contiguous).</param>
/// <param name="Goods">The stands that hold goods: piece index and kind.</param>
/// <param name="Paths">The visitor paths, in the Landmark's own frame.</param>
/// <param name="Lake">The middle of the lake, in the Landmark's own frame.</param>
internal sealed record CampingResult(
    IReadOnlyList<(DinosaurKind Kind, int Tint, int First, int Count)> Dinosaurs,
    int AxeIndex, int AxeTreeIndex,
    IReadOnlyList<(int Index, CampGoodsKind Kind)> Goods,
    IReadOnlyList<IReadOnlyList<Vector2>> Paths, Vector2 Lake);

/// <summary>
/// Builds the camping ground in its own frame (112 x 96 m): a camping department laid out like a store's showroom, with
/// straight boardwalk aisles and goods on display along every one. Three dinosaurs of Furniture stand in a row along the north,
/// at the head of a spine aisle from the south entrance. Three aisles cross the spine. On one side of it, the front and middle
/// aisles hold the tent show (sixteen tents in four rows of four), closed by the tree house and the retro caravan, and the back
/// aisle the sleeping bag section. On the other side are the campfire with its marshmallow tasting stand and supplies stand,
/// a large lake, the sun lounger section facing it, camp furniture, kayaks and a hammock. Fake trees and bushes fill the rest
/// of the lawn; banners over the aisles name the sections, and every display has its price tag.
/// </summary>
internal static class CampingGroundGenerator
{
    private const float HalfWidth = 56f;
    private const float HalfDepth = 48f;

    /// <summary>The tent show: how far from the spine its first tents stand, and the pitch along their aisle.</summary>
    private const float FirstTent = 8f;
    private const float TentPitch = 7.5f;

    /// <summary>The sun lounger section's extent across the camp, from the spine.</summary>
    private const float BeachStart = 6.5f;
    private const float BeachEnd = 52f;

    public static CampingResult Build(LandmarkBuilder b)
    {
        var random = b.Random;
        var reserved = new List<(Vector2 Centre, float Radius)>();
        // The sections' displays, as rectangles (with a margin) in which nothing else is placed.
        var blocks = new List<(Vector2 Min, Vector2 Max)>();
        var goods = new List<(int, CampGoodsKind)>();
        var dinosaurs = new List<(DinosaurKind, int, int, int)>();

        // Positions across the camp are measured from the spine: positive on the lake's side, negative on the tent show's,
        // which the seed puts east or west.
        var side = random.Chance(0.5f) ? 1f : -1f;
        Vector2 At(float across, float y) => new(side * across, y);
        var north = new Vector2(0f, -1f);
        var south = new Vector2(0f, 1f);
        var frontY = random.Range(-5f, -3f);
        var middleY = random.Range(18f, 20f);
        var backY = random.Range(35.5f, 37f);
        var fire = At(random.Range(9f, 11f), random.Range(8.5f, 10.5f));
        var lake = At(random.Range(33f, 35f), random.Range(1.5f, 3.5f));
        var treeHouse = At(-43.5f, frontY + random.Range(-1f, 1f));
        var caravan = At(-46.5f, middleY + random.Range(-0.5f, 1.5f));
        var hammockTrees = At(random.Range(39f, 43f), backY + 6f);

        // The lake's outline: an ellipse whose edge wobbles; a point on its shore at an angle (in the ellipse's own measure).
        var lakeRadius = new Vector2(random.Range(11.5f, 13f), random.Range(8.8f, 9.6f));
        var phase = (random.Range(0f, MathF.Tau), random.Range(0f, MathF.Tau));
        float LakeEdge(float angle) => 1f + 0.18f * MathF.Sin(2f * angle + phase.Item1) + 0.12f * MathF.Sin(3f * angle + phase.Item2);
        Vector2 Shore(float angle, float beyond)
        {
            var edge = lake + new Vector2(MathF.Cos(angle) * lakeRadius.X, MathF.Sin(angle) * lakeRadius.Y) * LakeEdge(angle);
            return edge + Vector2.Normalize(edge - lake) * beyond;
        }
        // The shore facing the spine.
        var innerShore = side > 0f ? MathF.PI : 0f;

        // The aisles, all straight: the spine from the entrance up to the dinosaurs, the front aisle from the tree house across
        // the spine to the lake, the middle aisle from the caravan to the far side, and the back aisle.
        var paths = new List<IReadOnlyList<Vector2>>
        {
            Straight(At(0f, HalfDepth - 0.5f), At(0f, -14f)),
            Straight(At(-36.5f, frontY), At(0f, frontY), Shore(innerShore, 1.5f)),
            Straight(At(-39.5f, middleY), At(53f, middleY)),
            Straight(At(-51f, backY), At(36f, backY)),
        };
        var pathPoints = paths.SelectMany(Dense).ToList();

        bool Free(Vector2 at, float radius) =>
            MathF.Abs(at.X) <= HalfWidth - radius - 1f && MathF.Abs(at.Y) <= HalfDepth - radius - 1f &&
            reserved.All(r => Vector2.Distance(r.Centre, at) >= r.Radius + radius) &&
            blocks.All(r => Vector2.Distance(Vector2.Clamp(at, r.Min, r.Max), at) >= radius) &&
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

        void Block(Vector2 a, Vector2 c) => blocks.Add((Vector2.Min(a, c) - new Vector2(0.5f), Vector2.Max(a, c) + new Vector2(0.5f)));

        // A price tag on the visitors' side of a display.
        void Tag(Vector2 display, float radius)
        {
            var toward = pathPoints.MinBy(p => Vector2.Distance(p, display)) - display;
            toward = toward.LengthSquared() > 0.01f ? Vector2.Normalize(toward) : south;
            var at = display + toward * (radius + 0.4f);
            b.Put(FurnitureType.PriceTag, at, 0f, Facing(toward));
        }

        // The dinosaurs: one of each, one on each northern slot.
        var kinds = new List<DinosaurKind> { DinosaurKind.TyrannosaurusRex, DinosaurKind.Brachiosaurus, DinosaurKind.Triceratops };
        Shuffle(random, kinds);
        for (var slot = 0; slot < 3; slot++)
        {
            var centre = new Vector2((slot - 1) * 38f + random.Range(-3f, 3f), -28f + random.Range(-3f, 3f));
            reserved.Add((centre, 11f));
            var first = b.Pieces.Count;
            var dinosaur = new DinosaurBuilder(b, centre, random.Range(-MathF.PI, MathF.PI), kinds[slot]);
            dinosaur.Build();
            dinosaurs.Add((kinds[slot], dinosaur.MainTint, first, b.Pieces.Count - first));
        }

        // The campfire: the glowing flame in a ring of stones and logs, log seats with lanterns, and the two stands.
        reserved.Add((fire, 7f));
        b.Put(FurnitureType.CampfireRing, fire, 0f, random.Range(-MathF.PI, MathF.PI));
        b.Put(FurnitureType.CampfireFlame, fire, 0.05f, random.Range(-MathF.PI, MathF.PI));
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
                b.Put(FurnitureType.CampLantern, at + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 0.1f, FurnitureCatalog.Spec(FurnitureType.LogSeat).Height, 0f);
                lanterns++;
            }
        }
        // The marshmallow tasting stand beside the campfire: a counter with a tray of marshmallows on sticks and its sign, and a supplies stand.
        var standOut = new Vector2(MathF.Cos(standAngle), MathF.Sin(standAngle));
        var stand = fire + standOut * 5.6f;
        goods.Add((b.Pieces.Count, CampGoodsKind.Marshmallows));
        b.Put(FurnitureType.Counter, stand, 0f, Facing(fire - stand), variant: 0);
        var counterTop = FurnitureCatalog.Spec(FurnitureType.Counter).Height;
        var along = new Vector2(-standOut.Y, standOut.X);
        var tray = FurnitureCatalog.Spec(FurnitureType.MarshmallowTray);
        var stick = FurnitureCatalog.Spec(FurnitureType.MarshmallowStick);
        foreach (var offset in new[] { -0.45f, 0.45f })
        {
            var at = stand + along * offset;
            b.Put(FurnitureType.MarshmallowTray, at, counterTop, Facing(fire - stand));
            // Three rows of sticks fanned out in the tray, leaning out towards the visitors.
            foreach (var row in new[] { -0.12f, 0f, 0.12f })
                for (var k = 0; k < 5; k++)
                {
                    var spread = (k - 2f) * 0.1f;
                    var lean = Vector3.Normalize(new Vector3(-standOut * 0.35f + along * (spread * 1.5f), 1f));
                    var foot = at + along * spread + standOut * row;
                    var middle = new Vector3(foot, counterTop + tray.Height) + lean * (stick.Height / 2f);
                    b.PutTurned(FurnitureType.MarshmallowStick, middle, new Vector3(along, 0f), lean);
                }
        }
        b.Put(FurnitureType.PriceTag, stand + along * 1.35f - standOut * 0.3f, 0f, Facing(fire - stand));
        var supply = fire + new Vector2(MathF.Cos(supplyAngle), MathF.Sin(supplyAngle)) * 5.6f;
        goods.Add((b.Pieces.Count, CampGoodsKind.TinnedFood));
        goods.Add((b.Pieces.Count, CampGoodsKind.Backpack));
        b.Put(FurnitureType.Counter, supply, 0f, Facing(fire - supply), variant: 0);
        b.Put(FurnitureType.CampLantern, supply, counterTop, 0f);

        // The lake: blue rugs in an irregular outline, with a kayak, ducks and a fishing rod.
        foreach (var s in new[] { -0.5f, 0f, 0.5f })
            reserved.Add((lake + new Vector2(s * lakeRadius.X, 0f), lakeRadius.Y * 1.3f + 1.5f));
        var rug = FurnitureCatalog.Spec(FurnitureType.Rug);
        for (var y = -lakeRadius.Y * 1.3f - rug.Depth; y <= lakeRadius.Y * 1.3f + rug.Depth; y += rug.Depth)
            for (var x = -lakeRadius.X * 1.3f - rug.Width; x <= lakeRadius.X * 1.3f + rug.Width; x += rug.Width)
            {
                var edge = LakeEdge(MathF.Atan2(y / lakeRadius.Y, x / lakeRadius.X));
                var d = MathF.Sqrt(x * x / (lakeRadius.X * lakeRadius.X) + y * y / (lakeRadius.Y * lakeRadius.Y));
                if (d <= edge)
                    b.Put(FurnitureType.Rug, lake + new Vector2(x, y), 0f, 0f, tint: d > edge - 0.2f ? 4 : 5);
            }
        b.Put(FurnitureType.Kayak, lake + new Vector2(random.Range(-3f, 3f), random.Range(-2f, 2f)), 0.01f, random.Range(-0.5f, 0.5f), tint: random.Next(FurniturePalettes.TintCount));
        for (var k = 0; k < 6; k++)
            b.Put(FurnitureType.RubberDuck, lake + new Vector2(random.Range(-lakeRadius.X * 0.6f, lakeRadius.X * 0.6f), random.Range(-lakeRadius.Y * 0.5f, lakeRadius.Y * 0.5f)),
                0.01f, random.Range(-MathF.PI, MathF.PI));
        // The fishing rod on the shore, its tip out over the water, and the lake's price tag where the front aisle reaches it.
        var rod = Shore(innerShore - side * 0.5f, 0.8f);
        b.Put(FurnitureType.FishingRod, rod, 0f, Facing(lake - rod));
        Tag(lake, Vector2.Distance(lake, Shore(innerShore, 0f)));

        // The tree house at the end of the front aisle: a fixed shell with its own tree and stair, furnished on its deck, which is a Lookout.
        reserved.Add((treeHouse, 7.8f));
        BuildTreeHouse(b, treeHouse, random.Range(-MathF.PI, MathF.PI));

        // The retro caravan at the end of the middle aisle, its door and awning towards it.
        reserved.Add((caravan, 6.5f));
        var toAisle = new Vector2(side, 0f);
        BuildCaravan(b, caravan, new Vector2(toAisle.Y, -toAisle.X) * (random.Chance(0.5f) ? 1f : -1f), toAisle, random);
        Tag(caravan, 4.6f);

        // The hammock on its own frame, between two trees, beyond the end of the back aisle.
        reserved.Add((hammockTrees, 6f));
        var hammockHeading = random.Range(-0.3f, 0.3f);
        var hammockAxis = new Vector2(MathF.Cos(hammockHeading), MathF.Sin(hammockHeading));
        b.Put(FurnitureType.Tree, hammockTrees - hammockAxis * 4.6f, 0f, random.Range(-MathF.PI, MathF.PI), variant: 1);
        b.Put(FurnitureType.Tree, hammockTrees + hammockAxis * 4.6f, 0f, random.Range(-MathF.PI, MathF.PI), variant: 1);
        b.Put(FurnitureType.Hammock, hammockTrees, 0f, hammockHeading, tint: random.Next(FurniturePalettes.TintCount));
        Tag(hammockTrees, 1.5f);
        // More hammocks on their own frames, in a row along the back aisle before it.
        var hammock = FurnitureCatalog.Spec(FurnitureType.Hammock);
        var hammockY = backY + CampingGround.PathWidth / 2f + 0.5f + hammock.Depth / 2f;
        for (var k = 0; k < 5; k++)
        {
            b.Put(FurnitureType.Hammock, At(31f + k * 4f, hammockY), 0f, Facing(north), tint: random.Next(FurniturePalettes.TintCount));
            if (k % 2 == 0)
                b.Put(FurnitureType.PriceTag, At(31f + k * 4f + 1.9f, backY + CampingGround.PathWidth / 2f + 0.35f), 0f, Facing(north));
        }
        Block(At(29f, backY + CampingGround.PathWidth / 2f), At(49f, hammockY + hammock.Depth / 2f));

        // The tent show: sixteen tents, every kind at least twice, in four rows of four facing each other in pairs across the
        // front and middle aisles; the first one lit from inside, with a lantern in its doorway, and a price tag before each.
        var tentKinds = Enum.GetValues<TentKind>().Concat(Enum.GetValues<TentKind>()).ToList();
        var extra = Enum.GetValues<TentKind>().ToList();
        Shuffle(random, extra);
        tentKinds.AddRange(extra.Take(4));
        Shuffle(random, tentKinds);
        var outward = new Vector2(-side, 0f);
        for (var k = 0; k < tentKinds.Count; k++)
        {
            var spec = FurnitureCatalog.Spec(FurnitureType.Tent, (int)tentKinds[k]);
            // Of each aisle's two rows, the northern one faces south and the southern one north.
            var row = k / 4;
            var aisleY = row < 2 ? frontY : middleY;
            var front = row % 2 == 0 ? south : north;
            var at = At(-(FirstTent + k % 4 * TentPitch), aisleY - front.Y * (CampingGround.PathWidth / 2f + 1.3f + spec.Depth / 2f));
            reserved.Add((at, MathF.Max(spec.Width, spec.Depth) / 2f + 1f));
            var heading = Facing(front);
            b.Put(FurnitureType.Tent, at, 0f, heading, variant: (int)tentKinds[k], tint: random.Next(FurniturePalettes.TintCount));
            // The lantern-lit door in the doorway: set into the others' porches, on the round bell tent's wall.
            if (k == 0)
                b.Put(FurnitureType.LanternTent, at + front * (spec.Depth / 2f + (tentKinds[k] == TentKind.Bell ? -0.05f : -0.2f)), 0f, heading);
            b.Put(FurnitureType.PriceTag, at + front * (spec.Depth / 2f + 0.7f) - outward * (spec.Width / 2f - 0.3f), 0f, heading);
        }
        // The tent show is kept clear of trees, so its rows read as a showroom's.
        Block(At(-FirstTent + 3f, frontY - 8f), At(-(FirstTent + 3 * TentPitch + 3f), middleY + 8f));

        // The sun lounger section, between the middle aisle and the back, facing the lake across the middle aisle: a row of
        // wooden loungers and one of white ones, in pairs under beach umbrellas, then a row of striped deck chairs.
        var rowY = middleY + CampingGround.PathWidth / 2f + 0.8f;
        for (var variant = 0; variant < 2; variant++)
        {
            var spec = FurnitureCatalog.Spec(FurnitureType.SunLounger, variant);
            var y = rowY + spec.Depth / 2f;
            var pair = 0;
            for (var x = BeachStart + 1.6f; x + 1.6f <= BeachEnd; x += 3.4f, pair++)
            {
                foreach (var s in new[] { -0.6f, 0.6f })
                    b.Put(FurnitureType.SunLounger, At(x + s, y), 0f, Facing(north), variant: variant);
                // The umbrella's pole between the loungers' heads, its shade over both.
                b.Put(FurnitureType.Parasol, At(x, y + spec.Depth / 2f - 0.3f), 0f, Facing(north) + random.Range(-0.3f, 0.3f), variant: 1);
                if (pair % 2 == 0)
                    b.Put(FurnitureType.PriceTag, At(x, y - spec.Depth / 2f - 0.4f), 0f, Facing(north));
            }
            rowY += spec.Depth + 2.2f;
        }
        var deckChair = FurnitureCatalog.Spec(FurnitureType.SunLounger, 2);
        var deckTints = Enumerable.Range(0, 3).Select(_ => random.Next(FurniturePalettes.TintCount)).ToArray();
        var chairs = 0;
        for (var x = BeachStart + 0.5f; x + 0.5f <= BeachEnd; x += 0.95f, chairs++)
        {
            b.Put(FurnitureType.SunLounger, At(x, rowY + deckChair.Depth / 2f), 0f, Facing(north), variant: 2, tint: deckTints[chairs % deckTints.Length]);
            if (chairs % 8 == 3)
                b.Put(FurnitureType.PriceTag, At(x + 0.47f, rowY - 0.4f), 0f, Facing(north));
        }
        Block(At(BeachStart, middleY + CampingGround.PathWidth / 2f), At(BeachEnd, rowY + deckChair.Depth));

        // The sleeping bag section along the back aisle on the tent show's side. To its north, sleeping bags laid out on the
        // lawn in fours, their feet to the aisle, each four with a display table of rolled-up ones behind it; to its south,
        // sleeping bags on mattresses in threes, with lanterns between them.
        var bag = FurnitureCatalog.Spec(FurnitureType.SleepingBag);
        var roll = FurnitureCatalog.Spec(FurnitureType.SleepingBag, 1);
        var table = FurnitureCatalog.Spec(FurnitureType.DisplayTable);
        var mattress = FurnitureCatalog.Spec(FurnitureType.Mattress);
        var bagY = backY - CampingGround.PathWidth / 2f - 0.5f - bag.Depth / 2f;
        var tableY = bagY - bag.Depth / 2f - 0.7f - table.Depth / 2f;
        for (var g = 0; g < 7; g++)
        {
            var x = -(5f + g * 6.2f);
            var tint = random.Next(FurniturePalettes.TintCount);
            for (var k = 0; k < 4; k++)
                b.Put(FurnitureType.SleepingBag, At(x + (k - 1.5f) * 1f, bagY), 0f, Facing(south), tint: (tint + k % 2 * 5) % FurniturePalettes.TintCount);
            b.Put(FurnitureType.PriceTag, At(x - 2.4f, backY - CampingGround.PathWidth / 2f - 0.35f), 0f, Facing(south));
            // The table's rolls lie across it, in three rows of three with four more in the grooves on top.
            b.Put(FurnitureType.DisplayTable, At(x, tableY), 0f, Facing(south));
            for (var i = -1; i <= 1; i++)
                for (var j = -1; j <= 1; j++)
                    b.Put(FurnitureType.SleepingBag, At(x + i * 0.53f, tableY + j * 0.23f), table.Height, Facing(new Vector2(1f, 0f)),
                        variant: 1, tint: (tint + i + 1) % FurniturePalettes.TintCount);
            foreach (var i in new[] { -0.27f, 0.27f })
                foreach (var j in new[] { -0.115f, 0.115f })
                    b.Put(FurnitureType.SleepingBag, At(x + i, tableY + j), table.Height + roll.Height * 0.85f, Facing(new Vector2(1f, 0f)),
                        variant: 1, tint: (tint + 4) % FurniturePalettes.TintCount);
        }
        Block(At(-2.5f, tableY - table.Depth / 2f), At(-46f, backY - CampingGround.PathWidth / 2f));
        var padY = backY + CampingGround.PathWidth / 2f + 0.5f + mattress.Depth / 2f;
        for (var g = 0; g < 6; g++)
        {
            var x = -(6f + g * 7.4f);
            for (var k = -1; k <= 1; k++)
            {
                var at = At(x + k * 1.7f, padY);
                b.Put(FurnitureType.Mattress, at, 0f, Facing(north), tint: 11);
                b.Put(FurnitureType.SleepingBag, at, mattress.Height, Facing(north), tint: random.Next(FurniturePalettes.TintCount));
            }
            b.Put(FurnitureType.PriceTag, At(x - 2.9f, backY + CampingGround.PathWidth / 2f + 0.35f), 0f, Facing(north));
            b.Put(FurnitureType.CampLantern, At(x - 3.7f, padY), 0f, 0f);
        }
        Block(At(-2.5f, backY + CampingGround.PathWidth / 2f), At(-46f, padY + mattress.Depth / 2f));

        // Camp furniture across the back aisle from the sun loungers: round tables with camping chairs and a lantern; then kayaks.
        for (var k = 0; k < 3; k++)
        {
            var centre = At(6.5f + k * 5f, backY + 3.4f);
            b.Put(FurnitureType.GardenTable, centre, 0f, 0f, variant: 1);
            b.Put(FurnitureType.CampLantern, centre, FurnitureCatalog.Spec(FurnitureType.GardenTable, 1).Height, 0f);
            for (var c = 0; c < 3; c++)
            {
                var angle = MathF.PI / 2f + (c - 1) * 2.1f + random.Range(-0.15f, 0.15f);
                var at = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 1.05f;
                b.Put(FurnitureType.CampChair, at, 0f, Facing(centre - at));
            }
            b.Put(FurnitureType.PriceTag, centre + new Vector2(side * 1.3f, -1.9f), 0f, Facing(north));
        }
        var kayak = FurnitureCatalog.Spec(FurnitureType.Kayak);
        for (var k = 0; k < 3; k++)
            b.Put(FurnitureType.Kayak, At(26f, backY + CampingGround.PathWidth / 2f + 0.9f + k * 1.05f), 0f, Facing(north) + random.Range(-0.04f, 0.04f),
                tint: random.Next(FurniturePalettes.TintCount));
        b.Put(FurnitureType.PriceTag, At(26f - kayak.Width / 2f - 0.3f, backY + CampingGround.PathWidth / 2f + 0.35f), 0f, Facing(north));
        Block(At(3.5f, backY + CampingGround.PathWidth / 2f), At(26f + kayak.Width / 2f, backY + 5f));

        // A plush bear by the campfire, and the trail sign that leads nowhere, beside the spine at the entrance.
        var bear = Spot(1.5f, fire, 10f);
        if (bear is not null)
        {
            reserved.Add((bear.Value, 1.5f));
            b.Put(FurnitureType.PlushBear, bear.Value, 0f, Facing(fire - bear.Value) + random.Range(-0.5f, 0.5f));
            Tag(bear.Value, 0.8f);
        }
        b.Put(FurnitureType.TrailSign, At(2.6f, HalfDepth - 4f), 0f, MathF.PI + random.Range(-0.3f, 0.3f));

        // The decorative axe: stuck at chest height in a tree right beside an aisle, facing the visitors, with its price tag.
        var (axeTree, axe) = PlantAxeTree(b, random, paths, reserved, Free);
        // Fake trees and bushes on the lawn the displays leave.
        var trees = 14 + random.Next(5);
        for (var k = 0; k < trees; k++)
        {
            var conifer = random.Chance(0.35f);
            var radius = conifer ? 2.1f : 3.6f;
            var at = Spot(radius, Vector2.Zero, 52f);
            if (at is null)
                continue;
            reserved.Add((at.Value, radius));
            b.Put(FurnitureType.Tree, at.Value, 0f, random.Range(-MathF.PI, MathF.PI), variant: conifer ? 1 : 0, tint: 0);
        }

        // Bushes, in clumps: at the foot of trees, along the aisles and on the lake's shore, and more over the open lawn.
        const float bush = 1.5f;
        foreach (var tree in b.Pieces.Where(p => p.Type == FurnitureType.Tree).Select(p => p.Position).ToList())
            Clump(Spot(bush, tree, 7f), random.Next(3));
        foreach (var path in paths)
            for (var i = 2; i < path.Count - 2; i += 4 + random.Next(3))
                Clump(Spot(bush, path[i], 4.5f), 1 + random.Next(2));
        for (var k = 0; k < 5; k++)
            Clump(Spot(bush, Shore(random.Range(0f, MathF.Tau), 2.5f), 2f), 1 + random.Next(3));
        for (var k = 0; k < 12; k++)
            Clump(Spot(bush, Vector2.Zero, 52f), 2 + random.Next(3));

        // A clump of up to `count` bushes around a first one, each touching the last.
        void Clump(Vector2? at, int count)
        {
            for (var k = 0; k < count && at is not null; k++)
            {
                reserved.Add((at.Value, bush));
                b.Put(FurnitureType.Tree, at.Value, 0f, random.Range(-MathF.PI, MathF.PI), variant: random.Chance(0.7f) ? 2 : 3, tint: 0);
                at = Spot(bush, at.Value, bush * 2.2f);
            }
        }

        // Banners over the aisles: at the entrance and before the dinosaurs, and at the head of each section, where they
        // name it (see PointOfInterestDetails).
        Banner(b, paths[0][1], north);
        Banner(b, paths[0][^3], north);
        Banner(b, At(-4f, frontY), outward);
        Banner(b, At(-4f, middleY), outward);
        Banner(b, At(4.5f, middleY), -outward);
        Banner(b, At(-3f, backY), outward);
        Banner(b, At(3f, backY), -outward);

        return new CampingResult(dinosaurs, axe, axeTree, goods, paths, lake);
    }

    /// <summary>The rotation of a piece whose front faces along a direction.</summary>
    internal static float Facing(Vector2 direction) => MathF.Atan2(-direction.X, direction.Y);

    private static float AngleGap(float a, float b) => MathF.Abs((float)Math.IEEERemainder(a - b, Math.Tau));

    private static void Shuffle<T>(SeededRandom random, List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>A straight aisle through its corners, with a point about every 2.5 m along it.</summary>
    private static List<Vector2> Straight(params Vector2[] corners)
    {
        var points = new List<Vector2> { corners[0] };
        for (var i = 0; i + 1 < corners.Length; i++)
        {
            var n = Math.Max(1, (int)MathF.Round(Vector2.Distance(corners[i], corners[i + 1]) / 2.5f));
            for (var k = 1; k <= n; k++)
                points.Add(Vector2.Lerp(corners[i], corners[i + 1], k / (float)n));
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

    /// <summary>A banner across an aisle at a point on it, square to the aisle's direction.</summary>
    private static void Banner(LandmarkBuilder b, Vector2 at, Vector2 along) => b.Put(FurnitureType.CampBanner, at, 0f, Facing(along));

    /// <summary>
    /// A broad tree beside the middle of a path, with the decorative axe stuck in its trunk at chest height on the path's side,
    /// its handle towards the visitors, and a price tag. Returns the tree's and the axe's piece indices.
    /// </summary>
    private static (int Tree, int Axe) PlantAxeTree(LandmarkBuilder b, SeededRandom random, List<IReadOnlyList<Vector2>> paths,
        List<(Vector2 Centre, float Radius)> reserved, Func<Vector2, float, bool> free)
    {
        // Only the trunk needs to be near the path; the crown may hang over it.
        const float clearance = 1.6f;
        var crown = FurnitureCatalog.Spec(FurnitureType.Tree).Width / 2f;
        var order = Enumerable.Range(0, paths.Count).OrderBy(_ => random.Next(1000)).ToList();
        foreach (var p in order)
        {
            var path = paths[p];
            for (var i = path.Count / 3; i < path.Count * 2 / 3; i++)
            {
                var along = Vector2.Normalize(path[i + 1] - path[i - 1]);
                foreach (var s in new[] { 1f, -1f })
                {
                    var toTrunk = new Vector2(-along.Y, along.X) * s;
                    var trunk = path[i] + toTrunk * (CampingGround.PathWidth / 2f + clearance);
                    // The tree turned so its trunk is at that spot.
                    var rotation = random.Range(-MathF.PI, MathF.PI);
                    var across = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
                    var front = new Vector2(-across.Y, across.X);
                    var at = trunk - across * CampingGround.TreeTrunk.X - front * CampingGround.TreeTrunk.Y;
                    if (!free(trunk, clearance - 0.4f) || reserved.Any(r => Vector2.Distance(r.Centre, at) < r.Radius + crown * 0.6f))
                        continue;
                    // As if just swung: the blade bitten into the bark on the path's side, the handle level and nearly along the
                    // trunk's surface, its end towards the visitors. The axe's height is its handle, its depth the way its blade faces.
                    var length = FurnitureCatalog.Spec(FurnitureType.Axe).Height;
                    var chest = at + across * CampingGround.TreeTrunkAtChest.X + front * CampingGround.TreeTrunkAtChest.Y;
                    var head = chest - toTrunk * (CampingGround.TreeTrunkRadius - 0.05f);
                    var handle = Vector3.Normalize(new Vector3(along + toTrunk * 0.4f, 0f));
                    var middle = new Vector3(head, CampingGround.AxeHeight) - handle * (length / 2f);
                    var flat = Vector3.Dot(Vector3.Cross(handle, Vector3.UnitZ), new Vector3(toTrunk, 0f)) >= 0f ? Vector3.UnitZ : -Vector3.UnitZ;
                    // The axe faces the path nearest the tree, not another one behind it.
                    var nearest = paths.SelectMany(q => q).MinBy(q => Vector2.Distance(q, at));
                    if (Vector2.Distance(nearest, new Vector2(middle.X, middle.Y)) >= Vector2.Distance(nearest, at) - 0.2f)
                        continue;
                    // A tree whose crown would stick out of the Landmark is left out: try the next spot.
                    var tree = b.Pieces.Count;
                    if (!b.Put(FurnitureType.Tree, at, 0f, rotation, variant: 0, tint: 0))
                        continue;
                    reserved.Add((at, crown * 0.6f));
                    var axe = b.Pieces.Count;
                    b.PutTurned(FurnitureType.Axe, middle, flat, handle);
                    b.Put(FurnitureType.PriceTag, trunk - toTrunk * 1.1f + along * 0.9f, 0f, Facing(-toTrunk));
                    return (tree, axe);
                }
            }
        }
        throw new InvalidOperationException("No room for the axe's tree beside any path.");
    }

    /// <summary>
    /// The tree house: its fixed shell (its own tree, deck, cabin and stair), with a bunk bed, a sleeping bag and a lantern in the
    /// cabin and a mattress on the open deck, which is the Lookout.
    /// </summary>
    private static void BuildTreeHouse(LandmarkBuilder b, Vector2 at, float rotation)
    {
        var deck = CampingGround.TreeHouseDeckHeight;
        var across = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
        var front = new Vector2(-across.Y, across.X);
        Vector2 Local(Vector2 v) => at + across * v.X + front * v.Y;
        b.Put(FurnitureType.TreeHouse, at, 0f, rotation);
        var cabin = Local(CampingGround.TreeHouseCabin);
        b.Put(FurnitureType.BunkBed, cabin - across * 0.6f, deck, rotation + MathF.PI / 2f, tint: 3);
        b.Put(FurnitureType.SleepingBag, cabin + across * 0.8f, deck, rotation, tint: 8);
        b.Put(FurnitureType.CampLantern, cabin + across * 0.8f + front * 1.3f, deck, 0f);
        var lookout = Local(CampingGround.TreeHouseOpenDeck);
        b.Put(FurnitureType.Mattress, lookout, deck, rotation, tint: 0);
        b.Lookouts.Add(new Vector3(lookout.X, lookout.Y, deck + FurnitureCatalog.Spec(FurnitureType.Mattress).Height));
    }

    /// <summary>
    /// A two-tone retro caravan of Furniture, about 6 m long: mattresses on edge in a retro colour for its lower walls, an
    /// off-white band of folded blankets above them and an off-white mattress roof, sloping mattress ends, mirror windows, round
    /// tables for wheels, a door, and a rug awning on two floor lamps over camping chairs and a bistro table on its door side.
    /// </summary>
    private static void BuildCaravan(LandmarkBuilder b, Vector2 at, Vector2 along2, Vector2 door2, SeededRandom random)
    {
        const int top = 11;      // off-white
        int[] retro = [8, 9, 5, 6];  // mustard, terracotta, slate blue, sage
        var band = retro[random.Next(retro.Length)];
        var along = new Vector3(along2, 0f);
        var door = new Vector3(door2, 0f);
        var across = new Vector3(-along2.Y, along2.X, 0f);
        var doorSide = Vector3.Dot(across, door) >= 0f ? 1f : -1f;
        var up = Vector3.UnitZ;
        var o = new Vector3(at, 0f);
        const float floor = 0.55f;
        const float halfWidth = 1.05f;

        // Wheels: round pedestal tables on their sides, their tops facing out.
        foreach (var s in new[] { -1f, 1f })
            b.PutTurned(FurnitureType.DiningTable, o + along * 0.4f + across * (s * (halfWidth + 0.2f)) + up * 0.55f, along, across * s, variant: 1);
        // The floor, then the walls: mattresses on their long edge, three a side.
        for (var k = 0; k < 4; k++)
            b.PutTurned(FurnitureType.Mattress, o + along * ((k - 1.5f) * 1.4f) + up * (floor - 0.11f), along, up, tint: top);
        foreach (var s in new[] { -1f, 1f })
            for (var k = 0; k < 3; k++)
                b.PutTurned(FurnitureType.Mattress, o + along * ((k - 1f) * 1.95f) + across * (s * halfWidth) + up * (floor + 0.7f), up, across * s, tint: band);
        // The coloured band under the roof: folded blankets along both sides.
        foreach (var s in new[] { -1f, 1f })
            for (var k = 0; k < 7; k++)
                b.PutTurned(FurnitureType.BlanketStack, o + along * ((k - 3f) * 0.8f) + across * (s * (halfWidth - 0.2f)) + up * (floor + 1.65f), along, up, tint: top);
        // The roof, and the rounded ends: a mattress leaning out below, one sloping in above.
        for (var k = 0; k < 4; k++)
            b.PutTurned(FurnitureType.Mattress, o + along * ((k - 1.5f) * 1.4f) + up * (floor + 2.0f), along, up, tint: top);
        foreach (var e in new[] { -1f, 1f })
        {
            // Each end panel lies across the caravan (a mattress's length is its depth), leaning in towards the roof.
            var outward = along * e;
            var lower = Vector3.Normalize(outward + up * 0.25f);
            b.PutTurned(FurnitureType.Mattress, o + outward * 2.95f + up * (floor + 0.7f), Vector3.Cross(across, lower), lower, tint: band);
            var upper = Vector3.Normalize(outward + up);
            b.PutTurned(FurnitureType.Mattress, o + outward * 2.55f + up * (floor + 1.85f), Vector3.Cross(across, upper), upper, tint: top);
        }
        // Windows: mirrors lying along the walls, two a side; the door on the door side.
        foreach (var s in new[] { -1f, 1f })
            foreach (var k in new[] { -1f, 1f })
            {
                if (s == doorSide && k < 0)
                    continue;
                b.PutTurned(FurnitureType.FloorMirror, o + along * (k * 1.5f) + across * (s * (halfWidth + 0.27f)) + up * (floor + 0.85f), up, along * s);
            }
        b.PutTurned(FurnitureType.DoorFrame, o - along * 1.4f + across * (doorSide * (halfWidth + 0.15f)) + up * 0.0f + up * (FurnitureCatalog.Spec(FurnitureType.DoorFrame).Height / 2f),
            along, up);
        // The awning: three rugs sloping out from the roof edge, their short sides along the caravan in the band
        // colour and off-white by turns (the stripes), held up by two floor lamps, over the chairs and a table.
        var outDoor = across * doorSide;
        var slope = Vector3.Normalize(outDoor - up * 0.35f);
        var awning = Vector3.Cross(along, slope);
        if (awning.Z < 0f)
            awning = -awning;
        // The ordinary tints wash out on the rug's pale weave: the stripes take a saturated camp colour.
        var stripe = FurnitureCatalog.Spec(FurnitureType.Rug).Depth;
        var stripeTint = (int)(random.Next(2) == 0 ? FurniturePalettes.DinosaurTints.OrangeRed : FurniturePalettes.DinosaurTints.Teal);
        foreach (var k in new[] { -1, 0, 1 })
            b.PutTurned(FurnitureType.Rug, o + along * (k * stripe) + outDoor * (halfWidth + 1.15f) + up * (floor + 1.75f), slope, awning,
                tint: k == 0 ? top : stripeTint);
        foreach (var k in new[] { -1f, 1f })
            b.Put(FurnitureType.FloorLamp, Flat(o + along * (k * 2.3f) + outDoor * (halfWidth + 1.6f)), 0f, 0f);
        var patio = Flat(o + outDoor * (halfWidth + 2.6f));
        b.Put(FurnitureType.GardenTable, patio, 0f, Facing(door2), variant: 1);
        foreach (var k in new[] { -1f, 1f })
        {
            var chair = patio + Flat(along * (k * 1.0f));
            b.Put(FurnitureType.CampChair, chair, 0f, Facing(Flat(-along * k)));
        }
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
