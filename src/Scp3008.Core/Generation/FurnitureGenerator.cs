using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>
/// Fills every department cell with the Furniture of its Department type. Each cell is laid
/// out on its own from a stream derived from the seed and the cell, inside a 1.5 m margin, so
/// neighbouring cells are separated by 3 m aisles. Row layouts (racks, shelving) run the same way
/// across a whole Department, so their rows line up into long shelving rows from cell to cell;
/// room-sets turn freely, and loose pieces get free rotations. Nothing is placed under a Pillar
/// or in the one-aisle walkway ring around it.
/// </summary>
internal static class FurnitureGenerator
{
    private const float Margin = 1.5f;
    private const float RoomSize = 6.5f;
    private const float HalfTurn = MathF.PI;
    private const float QuarterTurn = MathF.PI / 2f;

    public static IReadOnlyList<FurniturePiece> Generate(int seed, StoreSpace space, DepartmentMap departments, IReadOnlyList<Pillar> pillars)
    {
        var pieces = new List<FurniturePiece>();
        var keepClear = pillars.Select(p => (p.Position, HalfWidth: p.ClearHalfWidth)).ToArray();
        var cellSize = departments.CellSize;
        var rowQuarters = departments.All.Select(d => SeededRandom.For(seed, stream: 3, d.Id).Next(2)).ToArray();
        for (var y = 0; y < departments.CellsPerSide; y++)
            for (var x = 0; x < departments.CellsPerSide; x++)
            {
                var department = departments.AtCell(x, y);
                var cellOrigin = new Vector2(x * cellSize, y * cellSize);
                var cell = new Cell(space, pieces, keepClear, cellOrigin, cellSize, SeededRandom.For(seed, stream: 2, x, y));
                var frame = Frame.Turned(cellOrigin, cellSize, rowQuarters[department.Id]);
                Fill(department.Type, cell, frame);
            }
        return pieces.AsReadOnly();
    }

    private static void Fill(DepartmentType type, Cell cell, Frame frame)
    {
        switch (type)
        {
            case DepartmentType.SelfServeWarehouse: Warehouse(cell, frame); break;
            case DepartmentType.LightingAndElectrical: Lighting(cell, frame); break;
            case DepartmentType.Showrooms: Rooms(cell, frame, Showroom); break;
            case DepartmentType.BathroomsAndKitchens: Rooms(cell, frame, KitchenOrBathroom); break;
            case DepartmentType.RestaurantAndFoodMarket: Restaurant(cell, frame); break;
            case DepartmentType.PlantSection: Plants(cell, frame); break;
            case DepartmentType.KidsArea: Kids(cell, frame); break;
            case DepartmentType.Workspaces: Rooms(cell, frame, Workspace); break;
            case DepartmentType.TextilesAndDecor: Decor(cell, frame); break;
            case DepartmentType.Outdoor: Rooms(cell, frame, Outdoor); break;
            case DepartmentType.DiningAndHallway: Rooms(cell, frame, Hallway); break;
        }
    }

    // ---- Self-serve warehouse: long rows of pallet racks loaded with flat-pack boxes, or bulk stacks.

    private static void Warehouse(Cell cell, Frame frame)
    {
        var random = cell.Random;
        if (random.Chance(0.15f))
        {
            for (var j = 0; j < 6; j++)
                for (var i = 0; i < 7; i++)
                    if (random.Chance(0.85f))
                        if (i == 0 && j % 2 == 0)
                            cell.Place(frame, FurnitureType.BoxCart, 2.3f + i * 1.8f, 2.2f + j * 2.0f, 0f);
                        else
                            Stack(cell, frame, 2.3f + i * 1.8f, 2.2f + j * 2.0f, random.Range(-0.05f, 0.05f), 1 + random.Next(3));
            return;
        }

        var levels = FurnitureCatalog.PalletRackLevels;
        for (var pair = 0; pair < 3; pair++)
        {
            var start = Margin + pair * 5.3f;
            // Back to back, each rack facing its aisle.
            foreach (var (v, rotation) in new[] { (start + 0.55f, HalfTurn), (start + 1.65f, 0f) })
                for (var i = 0; i < 4; i++)
                {
                    var u = 3.95f + i * 2.7f;
                    if (!cell.Place(frame, FurnitureType.PalletRack, u, v, rotation))
                        continue;
                    for (var level = 0; level < levels.Count; level++)
                        foreach (var slot in new[] { -0.65f, 0.65f })
                            if (random.Chance(level == 0 ? 0.7f : 0.5f))
                                cell.Place(frame, FurnitureType.FlatPackBox, u + slot, v, rotation, levels[level]);
                }
        }
    }

    private static void Stack(Cell cell, Frame frame, float u, float v, float rotation, int height)
    {
        var boxHeight = FurnitureCatalog.Spec(FurnitureType.FlatPackBox).Height;
        if (!cell.Place(frame, FurnitureType.FlatPackBox, u, v, rotation))
            return;
        for (var level = 1; level < height; level++)
            cell.Place(frame, FurnitureType.FlatPackBox, u, v, rotation + cell.Random.Range(-0.08f, 0.08f), level * boxHeight);
    }

    // ---- Lighting & electrical: shelving rows, or display tables of lamps among floor lamps.

    private static void Lighting(Cell cell, Frame frame)
    {
        var random = cell.Random;
        if (random.Chance(0.15f))
        {
            ShelvingRows(cell, frame, FurnitureType.ShelfUnit, pairs: 4, pairPitch: 3.6f, count: 6, firstU: 3f);
            return;
        }

        var tableHeight = FurnitureCatalog.Spec(FurnitureType.DisplayTable).Height;
        foreach (var v in new[] { 2.5f, 6f, 9.5f, 13f })
            foreach (var u in new[] { 3.5f, 8f, 12.5f })
            {
                var rotation = random.Range(-0.1f, 0.1f);
                if (!cell.Place(frame, FurnitureType.DisplayTable, u, v, rotation))
                    continue;
                var along = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
                var lamps = 2 + random.Next(2);
                for (var i = 0; i < lamps; i++)
                {
                    var offset = along * (-0.5f + i * 1f / (lamps - 1));
                    cell.Place(frame, random.Chance(0.35f) ? FurnitureType.DeskLamp : FurnitureType.TableLamp, u + offset.X, v + offset.Y, random.Range(-HalfTurn, HalfTurn), tableHeight);
                }
            }
        cell.Scatter(frame, random.Chance(0.4f) ? FurnitureType.ArcLamp : FurnitureType.FloorLamp, attempts: 10, min: 2f, max: 14f);
    }

    /// <summary>Back-to-back pairs of shelving rows along the cell, with aisles between the pairs.</summary>
    private static void ShelvingRows(Cell cell, Frame frame, FurnitureType type, int pairs, float pairPitch, int count, float firstU)
    {
        var spec = FurnitureCatalog.Spec(type);
        for (var pair = 0; pair < pairs; pair++)
        {
            var start = Margin + pair * pairPitch;
            foreach (var (v, rotation) in new[] { (start + spec.Depth / 2f, HalfTurn), (start + spec.Depth * 1.5f, 0f) })
                for (var i = 0; i < count; i++)
                    cell.Place(frame, type, firstU + i * spec.Width, v, rotation);
        }
    }

    // ---- Room-sets: the cell is split into 2 × 2 rooms, each turned its own way.

    private static void Rooms(Cell cell, Frame frame, Action<Cell, Frame> furnishRoom)
    {
        foreach (var (u, v) in new[] { (Margin, Margin), (Margin + RoomSize, Margin), (Margin, Margin + RoomSize), (Margin + RoomSize, Margin + RoomSize) })
            furnishRoom(cell, frame.Then(Frame.Turned(new Vector2(u, v), RoomSize, cell.Random.Next(4))));
    }

    private static void Showroom(Cell cell, Frame room)
    {
        var random = cell.Random;
        var roll = random.NextFloat();
        if (roll < 0.4f)
        {
            // Living room around a rug.
            cell.Place(room, FurnitureType.Rug, 3.25f, 3.3f, 0f);
            cell.Place(room, random.Chance(0.3f) ? FurnitureType.TvBench : FurnitureType.CoffeeTable, 3.25f, 3.3f, random.Range(-0.1f, 0.1f));
            cell.Place(room, FurnitureType.Sofa, 3.25f, 5.2f, HalfTurn + random.Range(-0.08f, 0.08f));
            cell.Place(room, FurnitureType.Armchair, 1.2f, 3.3f, -QuarterTurn + random.Range(-0.4f, 0.4f));
            if (random.Chance(0.7f))
                cell.Place(room, FurnitureType.Armchair, 5.3f, 3.3f, QuarterTurn + random.Range(-0.4f, 0.4f));
            cell.Place(room, random.Chance(0.4f) ? FurnitureType.ArcLamp : FurnitureType.FloorLamp, 0.7f, 5.6f, random.Range(-HalfTurn, HalfTurn));
            foreach (var u in new[] { 2.3f, 3.25f, 4.2f })
                if (random.Chance(0.6f))
                    cell.Place(room, FurnitureType.Bookcase, u, 0.45f, 0f);
        }
        else if (roll < 0.75f)
        {
            // Bedroom.
            cell.Place(room, FurnitureType.Bed, 3.25f, 1.45f, 0f);
            cell.Place(room, FurnitureType.Rug, 3.25f, 3.6f, 0f);
            cell.Place(room, random.Chance(0.35f) ? FurnitureType.Dresser : FurnitureType.Wardrobe, 0.4f, 3.2f, -QuarterTurn);
            cell.Place(room, random.Chance(0.35f) ? FurnitureType.Dresser : FurnitureType.Wardrobe, 0.4f, 4.3f, -QuarterTurn);
            cell.Place(room, FurnitureType.FloorLamp, 1.6f, 0.6f, random.Range(-HalfTurn, HalfTurn));
            cell.Place(room, FurnitureType.FloorLamp, 4.9f, 0.6f, random.Range(-HalfTurn, HalfTurn));
            cell.Place(room, FurnitureType.Armchair, 5.4f, 5.3f, QuarterTurn + random.Range(0.2f, 0.8f));
            cell.Place(room, FurnitureType.Bookcase, 6.15f, 2.5f, QuarterTurn);
        }
        else
        {
            // Storage wall with loose seating in front.
            for (var i = 0; i < 3; i++)
                cell.Place(room, random.Chance(0.35f) ? FurnitureType.Dresser : FurnitureType.Wardrobe, 1.0f + i * 1.05f, 0.4f, 0f);
            cell.Place(room, FurnitureType.Bookcase, 4.3f, 0.25f, 0f);
            cell.Place(room, FurnitureType.Bookcase, 5.15f, 0.25f, 0f);
            cell.Place(room, FurnitureType.Sofa, 3.25f, 3.4f, random.Range(-0.3f, 0.3f));
            cell.Place(room, FurnitureType.Armchair, 1.3f, 4.9f, random.Range(-HalfTurn, HalfTurn));
            cell.Place(room, FurnitureType.Nightstand, 4.8f, 5.0f, random.Range(-0.6f, 0.6f));
        }
    }

    private static void KitchenOrBathroom(Cell cell, Frame room)
    {
        var random = cell.Random;
        if (random.Chance(0.55f))
        {
            // L-shaped kitchen run with a fridge, and an island.
            for (var i = 0; i < 8; i++)
                cell.Place(room, i == 3 ? FurnitureType.Stove : FurnitureType.KitchenCabinet, 0.9f + i * 0.6f, 0.4f, 0f);
            cell.Place(room, FurnitureType.Fridge, 5.8f, 0.45f, 0f);
            for (var j = 0; j < 5; j++)
                cell.Place(room, FurnitureType.KitchenCabinet, 0.4f, 1.3f + j * 0.6f, -QuarterTurn);
            for (var k = 0; k < 4; k++)
            {
                cell.Place(room, FurnitureType.KitchenCabinet, 2.6f + k * 0.6f, 3.6f, HalfTurn);
                cell.Place(room, FurnitureType.KitchenCabinet, 2.6f + k * 0.6f, 4.2f, 0f);
            }
            if (random.Chance(0.5f))
                cell.Place(room, FurnitureType.Fridge, 5.8f, 5.8f, HalfTurn);
        }
        else
        {
            // Bathroom display.
            cell.Place(room, FurnitureType.Bathtub, 1.25f, 0.475f, 0f);
            cell.Place(room, FurnitureType.Toilet, 2.8f, 0.425f, 0f);
            cell.Place(room, FurnitureType.VanityUnit, 6.15f, 2.0f, QuarterTurn);
            cell.Place(room, FurnitureType.WashingMachine, 6.1f, 3.0f, QuarterTurn);
            cell.Place(room, FurnitureType.Toilet, 1.0f, 5.9f, HalfTurn);
            cell.Place(room, FurnitureType.Toilet, 1.8f, 5.9f, HalfTurn);
            if (random.Chance(0.6f))
                cell.Place(room, FurnitureType.Bathtub, 4.5f, 5.95f, HalfTurn);
        }
    }

    // ---- Restaurant/food market: dining halls, market aisles, serving counters.

    private static void Restaurant(Cell cell, Frame frame)
    {
        var random = cell.Random;
        var roll = random.NextFloat();
        if (roll < 0.45f)
        {
            foreach (var v in new[] { 2.2f, 5.4f, 8.6f, 11.8f })
                for (var i = 0; i < 6; i++)
                    cell.Place(frame, FurnitureType.GroceryShelf, 3f + i * 2f, v, 0f);
            for (var i = 0; i < 5; i++)
                cell.Place(frame, FurnitureType.Freezer, 3.2f + i * 2.4f, 13.9f, HalfTurn);
            return;
        }

        var tableRows = new List<float> { 2.6f, 6.1f, 9.6f, 13.1f };
        if (roll < 0.6f)
        {
            for (var i = 0; i < 5; i++)
                cell.Place(frame, FurnitureType.Counter, 4f + i * 2f, 2.2f, HalfTurn);
            tableRows.RemoveAt(0);
        }
        foreach (var v in tableRows)
            foreach (var u in new[] { 3f, 8f, 13f })
            {
                if (!cell.Place(frame, FurnitureType.DiningTable, u, v, random.Range(-0.05f, 0.05f)))
                    continue;
                foreach (var side in new[] { -0.4f, 0.4f })
                {
                    if (random.Chance(0.9f))
                        cell.Place(frame, roll < 0.7f ? FurnitureType.BarStool : FurnitureType.Chair, u + side, v - 0.85f, random.Range(-0.3f, 0.3f));
                    if (random.Chance(0.9f))
                        cell.Place(frame, roll < 0.7f ? FurnitureType.BarStool : FurnitureType.Chair, u + side, v + 0.85f, HalfTurn + random.Range(-0.3f, 0.3f));
                }
            }
    }

    // ---- Plant section: rows of plant tables with potted plants, or halls of large plants.

    private static void Plants(Cell cell, Frame frame)
    {
        var random = cell.Random;
        if (random.Chance(0.25f))
        {
            for (var j = 0; j < 8; j++)
                for (var i = 0; i < 8; i++)
                    if (random.Chance(0.75f))
                        cell.Place(frame, (i + j) % 4 == 0 ? FurnitureType.PlanterBox : FurnitureType.LargePlant,
                            2.5f + i * 1.6f + random.Range(-0.35f, 0.35f), 2.5f + j * 1.6f + random.Range(-0.35f, 0.35f),
                            random.Range(-HalfTurn, HalfTurn));
            return;
        }

        var tableHeight = FurnitureCatalog.Spec(FurnitureType.PlantTable).Height;
        foreach (var v in new[] { 2.2f, 5.0f, 7.8f, 10.6f, 13.4f })
            for (var i = 0; i < 4; i++)
            {
                var u = 3.2f + i * 3.2f;
                if (!cell.Place(frame, FurnitureType.PlantTable, u, v, 0f))
                    continue;
                var plants = 3 + random.Next(3);
                for (var p = 0; p < plants; p++)
                    cell.Place(frame, FurnitureType.PottedPlant,
                        u - 0.95f + (p + 0.5f) * 1.9f / plants, v + random.Range(-0.25f, 0.25f),
                        random.Range(-HalfTurn, HalfTurn), tableHeight);
            }
        for (var i = 0; i < 3; i++)
            cell.Place(frame, FurnitureType.LargePlant, random.Range(2f, 14f), random.Pick([3.6f, 6.4f, 9.2f, 12.0f]),
                random.Range(-HalfTurn, HalfTurn));
    }

    // ---- Kids' area: toy shelving rows, or kids' bedrooms and play rooms full of play cubes.

    private static void Kids(Cell cell, Frame frame)
    {
        if (cell.Random.Chance(0.3f))
        {
            ShelvingRows(cell, frame, FurnitureType.ToyShelf, pairs: 4, pairPitch: 3.1f, count: 7, firstU: 3.2f);
            return;
        }
        Rooms(cell, frame, KidsRoom);
    }

    private static void KidsRoom(Cell cell, Frame room)
    {
        var random = cell.Random;
        if (random.Chance(0.45f))
        {
            cell.Place(room, FurnitureType.BunkBed, 1.4f, 0.875f, 0f);
            cell.Place(room, FurnitureType.BunkBed, 4.6f, 0.875f, 0f);
            cell.Place(room, FurnitureType.ToyShelf, 6.2f, 3.5f, QuarterTurn);
            cell.Place(room, FurnitureType.Rug, 3.0f, 3.6f, 0f);
            KidsTable(cell, room, 3.0f, 3.6f);
            cell.Scatter(room, FurnitureType.ToyChest, attempts: 4, min: 0.4f, max: 6.1f);
        }
        else
        {
            foreach (var u in new[] { 1.0f, 2.7f, 4.4f })
                cell.Place(room, FurnitureType.ToyShelf, u, 0.3f, 0f);
            cell.Place(room, FurnitureType.Rug, 1.8f, 3.5f, QuarterTurn);
            KidsTable(cell, room, 1.8f, 3.5f);
            KidsTable(cell, room, 4.6f, 3.5f);
            cell.Scatter(room, FurnitureType.PlayCube, attempts: 8, min: 0.4f, max: 6.1f);
        }
    }

    private static void KidsTable(Cell cell, Frame room, float u, float v)
    {
        var random = cell.Random;
        if (!cell.Place(room, FurnitureType.KidsTable, u, v, random.Range(-0.2f, 0.2f)))
            return;
        foreach (var (du, dv, facing) in new[] { (0f, -0.75f, 0f), (0f, 0.75f, HalfTurn), (-0.75f, 0f, -QuarterTurn), (0.75f, 0f, QuarterTurn) })
            if (random.Chance(0.8f))
                cell.Place(room, FurnitureType.KidsChair, u + du, v + dv, facing + random.Range(-0.3f, 0.3f));
    }

    // New Departments replace a share of the existing Department sites and layout rolls.
    private static void Workspace(Cell cell, Frame room)
    {
        if (cell.Random.Chance(0.3f))
        {
            cell.Place(room, FurnitureType.MeetingTable, 3.25f, 3.25f, 0f);
            foreach (var u in new[] { 2.4f, 3.3f, 4.2f })
                foreach (var v in new[] { 2.2f, 4.3f })
                    cell.Place(room, FurnitureType.OfficeChair, u, v, v < 3f ? 0f : HalfTurn);
        }
        else
        {
            foreach (var u in new[] { 1.2f, 3.1f, 5.0f })
                foreach (var v in new[] { 1.0f, 4.0f })
                {
                    if (cell.Place(room, FurnitureType.Desk, u, v, 0f))
                        cell.Place(room, FurnitureType.DeskLamp, u - 0.4f, v, 0f, 0.75f);
                    cell.Place(room, FurnitureType.OfficeChair, u, v + 0.85f, 0f);
                }
        }
        foreach (var u in new[] { 1f, 2f, 3f, 4f, 5f })
            cell.Place(room, FurnitureType.FilingCabinet, u, 5.6f, HalfTurn);
    }

    private static void Decor(Cell cell, Frame frame)
    {
        // Alternating racks and low textile bins, with a mirror/frame aisle.
        foreach (var v in new[] { 2.2f, 5.2f, 8.2f, 11.2f })
            foreach (var u in new[] { 2.8f, 4.6f, 6.4f, 8.2f, 10f, 11.8f, 13.6f })
            {
                var type = cell.Random.Pick(new[] { FurnitureType.CushionBin, FurnitureType.BlanketStack, FurnitureType.CurtainRack });
                cell.Place(frame, type, u, v, 0f);
                cell.Place(frame, FurnitureType.BlanketStack, u, v + 1.0f, 0f);
            }
        foreach (var u in new[] { 2.8f, 4.6f, 6.4f, 8.2f, 10f, 11.8f, 13.6f })
            cell.Place(frame, cell.Random.Chance(0.5f) ? FurnitureType.FloorMirror : FurnitureType.FrameDisplay, u, 13.8f, 0f);
    }

    private static void Outdoor(Cell cell, Frame room)
    {
        cell.Place(room, FurnitureType.GardenTable, 2f, 2f, 0f);
        foreach (var u in new[] { 1.5f, 2.5f })
            foreach (var v in new[] { 1.1f, 2.9f })
                cell.Place(room, FurnitureType.GardenChair, u, v, v < 2f ? 0f : HalfTurn);
        cell.Place(room, FurnitureType.Parasol, 4.8f, 1.8f, 0f);
        cell.Place(room, FurnitureType.SunLounger, 4.8f, 4.2f, 0f);
        cell.Place(room, FurnitureType.Grill, 1f, 5.7f, 0f);
        cell.Place(room, FurnitureType.GardenTable, 2.8f, 5.1f, 0f);
        cell.Place(room, FurnitureType.GardenChair, 2.8f, 4.1f, 0f);
        cell.Place(room, FurnitureType.PottedPlant, 3.5f, 3.5f, 0f);
    }

    private static void Hallway(Cell cell, Frame room)
    {
        foreach (var u in new[] { 1f, 2.5f, 4f, 5.5f })
            cell.Place(room, FurnitureType.DisplayCabinet, u, 0.6f, 0f);
        foreach (var v in new[] { 2f, 3.4f, 4.8f })
        {
            cell.Place(room, FurnitureType.ShoeCabinet, 0.6f, v, -QuarterTurn);
            cell.Place(room, FurnitureType.CoatRack, 5.6f, v, 0f);
        }
        cell.Place(room, FurnitureType.Sideboard, 2.4f, 5.8f, HalfTurn);
        cell.Place(room, FurnitureType.HallBench, 4.2f, 5.8f, 0f);
        cell.Place(room, FurnitureType.DiningTable, 3f, 3f, 0f);
        cell.Place(room, FurnitureType.Chair, 3f, 2.1f, 0f);
        cell.Place(room, FurnitureType.Chair, 3f, 3.9f, HalfTurn);
    }

    /// <summary>
    /// A local coordinate frame inside the Store, turned by whole quarter turns: maps local
    /// positions and rotations to unwrapped Store positions and rotations.
    /// </summary>
    private readonly record struct Frame(Vector2 Origin, int Quarters)
    {
        /// <summary>The square region of the given size at <paramref name="corner"/> of the parent, turned about its centre.</summary>
        public static Frame Turned(Vector2 corner, float size, int quarters)
        {
            var half = new Vector2(size / 2f);
            return new Frame(corner + half - Turn(half, quarters), quarters & 3);
        }

        public Vector2 Map(Vector2 local) => Origin + Turn(local, Quarters);

        public float MapRotation(float local) => local + Quarters * QuarterTurn;

        /// <summary>The frame <paramref name="inner"/>, given in this frame's local coordinates, in Store coordinates.</summary>
        public Frame Then(Frame inner) => new(Map(inner.Origin), (Quarters + inner.Quarters) & 3);

        // A quarter turn takes east to south, matching the rotation convention of Furniture.
        private static Vector2 Turn(Vector2 v, int quarters) => (quarters & 3) switch
        {
            0 => v,
            1 => new Vector2(-v.Y, v.X),
            2 => -v,
            _ => new Vector2(v.Y, -v.X),
        };
    }

    /// <summary>
    /// Places Furniture in one department cell, keeping it inside the cell's margin, out of the
    /// squares kept clear around the Pillars, and never letting pieces on the floor overlap.
    /// Raised pieces are placed by the caller only on a piece that was placed.
    /// </summary>
    private sealed class Cell(StoreSpace space, List<FurniturePiece> pieces, (Vector2 Centre, float HalfWidth)[] keepClear,
        Vector2 origin, float size, SeededRandom random)
    {
        private const float BoundsTolerance = 0.1f;
        private readonly List<Footprint> _floor = [];

        public SeededRandom Random { get; } = random;

        public bool Place(Frame frame, FurnitureType type, float u, float v, float rotation, float elevation = 0f)
        {
            var spec = FurnitureCatalog.Spec(type);
            var footprint = new Footprint(frame.Map(new Vector2(u, v)), frame.MapRotation(rotation), spec.Width, spec.Depth);
            if (!Inside(footprint) || InKeptClear(footprint))
                return false;
            var blocksFloor = elevation == 0f && !spec.IsFloorCovering;
            if (blocksFloor)
            {
                foreach (var other in _floor)
                    if (footprint.Overlaps(other))
                        return false;
                _floor.Add(footprint);
            }
            var heading = (float)Math.IEEERemainder(footprint.Rotation, 2.0 * Math.PI);
            pieces.Add(new FurniturePiece(pieces.Count, type, space.Wrap(footprint.Centre), elevation, heading));
            return true;
        }

        /// <summary>Tries to drop pieces at random places and headings within [min, max]² of the frame.</summary>
        public void Scatter(Frame frame, FurnitureType type, int attempts, float min, float max)
        {
            for (var i = 0; i < attempts; i++)
                Place(frame, type, Random.Range(min, max), Random.Range(min, max), Random.Range(-HalfTurn, HalfTurn));
        }

        private bool Inside(Footprint footprint)
        {
            var low = origin + new Vector2(Margin - BoundsTolerance);
            var high = origin + new Vector2(size - Margin + BoundsTolerance);
            foreach (var corner in footprint.Corners())
                if (corner.X < low.X || corner.Y < low.Y || corner.X > high.X || corner.Y > high.Y)
                    return false;
            return true;
        }

        /// <summary>Whether the footprint's axis-aligned bounds reach into a square kept clear.</summary>
        private bool InKeptClear(Footprint footprint)
        {
            var corners = footprint.Corners();
            var low = new Vector2(corners.Min(c => c.X), corners.Min(c => c.Y));
            var high = new Vector2(corners.Max(c => c.X), corners.Max(c => c.Y));
            foreach (var (centre, halfWidth) in keepClear)
            {
                // The square's image nearest this cell, in case it lies across a Seam.
                var near = footprint.Centre + space.Delta(footprint.Centre, centre);
                if (high.X > near.X - halfWidth && low.X < near.X + halfWidth &&
                    high.Y > near.Y - halfWidth && low.Y < near.Y + halfWidth)
                    return true;
            }
            return false;
        }
    }

    /// <summary>A rotated footprint rectangle on the floor, in unwrapped Store coordinates.</summary>
    private readonly record struct Footprint(Vector2 Centre, float Rotation, float Width, float Depth)
    {
        // Pieces may touch; only overlaps deeper than this count.
        private const float Slack = 0.005f;

        public Vector2[] Corners()
        {
            var along = new Vector2(MathF.Cos(Rotation), MathF.Sin(Rotation));
            var across = new Vector2(-along.Y, along.X);
            var halfWidth = along * (Width / 2f - Slack);
            var halfDepth = across * (Depth / 2f - Slack);
            return [Centre + halfWidth + halfDepth, Centre + halfWidth - halfDepth, Centre - halfWidth - halfDepth, Centre - halfWidth + halfDepth];
        }

        /// <summary>Separating-axis test of two rotated rectangles.</summary>
        public bool Overlaps(Footprint other)
        {
            var reach = (Width + Depth + other.Width + other.Depth) / 2f;
            if (Vector2.DistanceSquared(Centre, other.Centre) > reach * reach)
                return false;
            var mine = Corners();
            var theirs = other.Corners();
            return !(Separated(mine, theirs, mine) || Separated(mine, theirs, theirs));
        }

        private static bool Separated(Vector2[] a, Vector2[] b, Vector2[] axesFrom)
        {
            for (var i = 0; i < 2; i++)
            {
                var edge = axesFrom[i + 1] - axesFrom[i];
                var axis = new Vector2(-edge.Y, edge.X);
                var (minA, maxA) = Project(a, axis);
                var (minB, maxB) = Project(b, axis);
                if (maxA <= minB || maxB <= minA)
                    return true;
            }
            return false;
        }

        private static (float Min, float Max) Project(Vector2[] corners, Vector2 axis)
        {
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var corner in corners)
            {
                var d = Vector2.Dot(corner, axis);
                min = MathF.Min(min, d);
                max = MathF.Max(max, d);
            }
            return (min, max);
        }
    }
}
