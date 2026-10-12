using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>
/// Furnishes one Display, a rectangle in its own frame (width along local X, depth along local Y),
/// from one theme: rolls which of the theme's slots appear and how many pieces each brings, then
/// places them by their placement roles. Rows fill first, then the core pieces, then the common and
/// accent ones; pieces placed relative to another (chairs around a table, a lamp on a nightstand)
/// follow as soon as their anchor stands. A Display that cannot show every core slot is dropped.
/// </summary>
internal sealed class DisplayFurnisher
{
    private const float QuarterTurn = MathF.PI / 2f;
    private const float HalfTurn = MathF.PI;
    private const float BoundsTolerance = 0.05f;
    // Floor area of a small room; counts of loose pieces scale with a Display's area against it.
    private const float SmallRoomArea = 6.5f * 6.5f;
    private const int MaxAccents = 3;
    private const int Attempts = 3;

    private static readonly PlacementRole[] CoreOrder = [PlacementRole.Centre, PlacementRole.Wall, PlacementRole.Corner, PlacementRole.Loose];
    private static readonly PlacementRole[] ExtraOrder = [PlacementRole.Wall, PlacementRole.Corner, PlacementRole.Centre, PlacementRole.Loose];

    private readonly Cell _cell;
    private readonly SeededRandom _random;
    private readonly Archetype _archetype;
    private readonly Frame _frame;
    private readonly float _width;
    private readonly float _depth;
    private readonly ThemeSpec _theme;
    private readonly float _spacing;
    private readonly IReadOnlyList<int> _palette;
    private readonly bool _aligned;
    private readonly float _soldOut;
    private readonly float _baseRotation;
    private readonly List<SlotPlan> _plans = [];
    private readonly HashSet<int> _runGroupsDone = [];

    private DisplayFurnisher(Cell cell, Archetype archetype, Frame frame, float width, float depth, Theme theme, float spacing, int palette, bool aligned)
    {
        _cell = cell;
        _random = cell.Random;
        _archetype = archetype;
        _frame = frame;
        _width = width;
        _depth = depth;
        _theme = ThemeCatalog.Spec(theme);
        _spacing = spacing;
        _palette = FurniturePalettes.All[palette];
        _aligned = aligned;
        _soldOut = _random.Range(0.05f, 0.15f);
        _baseRotation = aligned ? 0f : _random.Next(4) * QuarterTurn;
    }

    /// <summary>Furnishes the Display and commits it, or leaves it empty if its core does not fit.</summary>
    public static bool Furnish(Cell cell, Archetype archetype, Frame frame, float width, float depth, Theme theme, float spacing, int palette, bool aligned)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var furnisher = new DisplayFurnisher(cell, archetype, frame, width, depth, theme, spacing, palette, aligned);
            if (furnisher.Fill())
            {
                var extent = (frame.Quarters & 1) == 0 ? new Vector2(width, depth) : new Vector2(depth, width);
                cell.Commit(theme, frame.Map(new Vector2(width, depth) / 2f), extent, palette);
                return true;
            }
            cell.Discard();
        }
        return false;
    }

    private bool Fill()
    {
        Roll();
        PlaceRows(_plans.Where(p => p.Slot.Role == PlacementRole.Row).ToList());
        foreach (var role in CoreOrder)
            foreach (var plan in _plans.Where(p => p.Slot.Tier == ThemeTier.Core && p.Slot.Role == role).ToList())
                PlaceIndependent(plan);
        foreach (var role in ExtraOrder)
            foreach (var plan in _plans.Where(p => p.Slot.Tier != ThemeTier.Core && p.Slot.Role == role).ToList())
                PlaceIndependent(plan);
        return _plans.Where(p => p.Slot.Tier == ThemeTier.Core).All(p => p.Placed >= p.Slot.Min);
    }

    // ---- Rolling: which slots appear, with which type, variant, tint and count.

    private void Roll()
    {
        var own = ThemeCatalog.OwnTypes(_cell.Department.Type);
        var accents = new List<SlotPlan>();
        foreach (var slot in _theme.Slots)
        {
            var plan = new SlotPlan(slot, this);
            var odds = slot.Odds;
            // A Department's own Furniture shows up more often in the themes it borrows.
            if (!IsRelative(slot.Role) && slot.Types.Any(own.Contains))
                odds = MathF.Min(odds * 1.4f, slot.Tier == ThemeTier.Accent ? 0.22f : 0.85f);
            if (slot.Tier == ThemeTier.Core)
                _plans.Add(plan);
            else if (slot.Tier == ThemeTier.Accent && !IsRelative(slot.Role))
                accents.Add(plan with { Odds = odds });
            else if (_random.Chance(odds))
                _plans.Add(plan);
        }
        Shuffle(accents);
        var picked = 0;
        foreach (var accent in accents)
            if (picked < MaxAccents && _random.Chance(accent.Odds))
            {
                _plans.Add(accent);
                picked++;
            }
    }

    private static bool IsRelative(PlacementRole role) => role is PlacementRole.Around or PlacementRole.Beside
        or PlacementRole.InFront or PlacementRole.Under or PlacementRole.OnTop;

    /// <summary>How many independent pieces a slot brings, scaled to the Display's size.</summary>
    private int CountFor(ThemeSlot slot)
    {
        var count = slot.Min + _random.Next(slot.Max - slot.Min + 1);
        var scale = slot.Role switch
        {
            // Wall pieces get more room in larger Displays, but less than in proportion to the area.
            PlacementRole.Wall => MathF.Pow(_width * _depth / SmallRoomArea, 0.75f),
            PlacementRole.Loose => _width * _depth / SmallRoomArea,
            _ => 1f,
        };
        var scaled = count * scale;
        var whole = (int)scaled;
        if (_random.Chance(scaled - whole))
            whole++;
        return slot.Tier == ThemeTier.Core ? Math.Max(whole, Math.Max(slot.Min, 1)) : Math.Max(whole, 1);
    }

    // ---- Independent roles: centre groups, wall pieces and runs, corners, loose pieces.

    private void PlaceIndependent(SlotPlan plan)
    {
        switch (plan.Slot.Role)
        {
            case PlacementRole.Centre: PlaceCentre(plan); break;
            case PlacementRole.Wall: PlaceWall(plan); break;
            case PlacementRole.Corner: PlaceCorners(plan); break;
            case PlacementRole.Loose: PlaceLoose(plan); break;
        }
    }

    /// <summary>Groups on a loose grid over the Display: one in a small room, more in larger ones.</summary>
    private void PlaceCentre(SlotPlan plan)
    {
        var slot = plan.Slot;
        var turned = MathF.Abs(MathF.Sin(_baseRotation)) > 0.5f;
        var pitch = slot.Pitch * _spacing;
        // Cubicle rows: clusters stand in one long row, so their grid is wider.
        if (_archetype.SingleRowClusters && slot.Cluster.Max > 1)
            pitch.X *= 1.5f;
        if (turned)
            pitch = new Vector2(pitch.Y, pitch.X);
        var columns = Math.Max(1, (int)(_width / pitch.X + 0.3f));
        var rows = Math.Max(1, (int)(_depth / pitch.Y + 0.3f));
        var (stepU, stepV) = (_width / columns, _depth / rows);
        var total = columns * rows;
        for (var j = 0; j < rows; j++)
            for (var i = 0; i < columns; i++)
            {
                var last = j == rows - 1 && i == columns - 1;
                if (total >= 3 && _random.Chance(_soldOut) && !(last && plan.Placed == 0))
                    continue;
                var u = (i + 0.5f) * stepU;
                var rotation = _baseRotation;
                if (_archetype.Stagger && j % 2 == 1)
                {
                    u += stepU / 2f;
                    rotation += QuarterTurn;
                    if (u > _width - stepU / 4f)
                        continue;
                }
                var centre = new Vector2(u, (j + 0.5f) * stepV) + Jitter(slot.Jitter * _spacing);
                rotation += SmallTurn();
                var cluster = slot.Cluster.Min + _random.Next(slot.Cluster.Max - slot.Cluster.Min + 1);
                if (cluster <= 1)
                    TryPlace(plan, plan.PickType(), centre, rotation, PlacementRole.Centre);
                else
                    PlaceCluster(plan, centre, rotation, cluster);
            }
    }

    /// <summary>Several pieces side by side: two back-to-back rows (an island), or one row.</summary>
    private void PlaceCluster(SlotPlan plan, Vector2 centre, float rotation, int count)
    {
        var type = plan.PickType();
        var variant = plan.VariantOf(type, PlacementRole.Centre);
        var spec = FurnitureCatalog.Spec(type, variant);
        var along = Along(rotation);
        var front = Front(rotation);
        if (_archetype.SingleRowClusters)
        {
            for (var k = 0; k < count; k++)
                TryPlace(plan, type, centre + along * ((k - (count - 1) / 2f) * spec.Width), rotation, PlacementRole.Centre, variant: variant);
            return;
        }
        var perRow = (count + 1) / 2;
        for (var k = 0; k < count; k++)
        {
            var (row, column) = (k % 2, k / 2);
            var offset = along * ((column - (perRow - 1) / 2f) * spec.Width) + front * (row == 0 ? -spec.Depth / 2f : spec.Depth / 2f);
            TryPlace(plan, type, centre + offset, row == 0 ? rotation + HalfTurn : rotation, PlacementRole.Centre, variant: variant);
        }
    }

    private void PlaceWall(SlotPlan plan)
    {
        var slot = plan.Slot;
        if (slot.RunGroup != 0)
        {
            if (!_runGroupsDone.Add(slot.RunGroup))
                return;
            var group = _plans.Where(p => p.Slot.Role == PlacementRole.Wall && p.Slot.RunGroup == slot.RunGroup).ToList();
            PlaceRun(group.SelectMany(p => Enumerable.Range(0, p.Count).Select(_ => (p, p.PickType()))).ToList(), slot.Gap);
            return;
        }
        if (slot.Run)
        {
            PlaceRun(Enumerable.Range(0, plan.Count).Select(_ => (plan, plan.PickType())).ToList(), slot.Gap);
            return;
        }
        for (var i = 0; i < plan.Count; i++)
        {
            var type = plan.PickType();
            var spec = FurnitureCatalog.Spec(type, plan.VariantOf(type, PlacementRole.Wall));
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var wall = _random.Next(4);
                var length = WallLength(wall);
                var low = spec.Width / 2f + slot.WallMargin;
                if (length - low < low)
                    continue;
                var (position, rotation) = WallPose(wall, _random.Range(low, length - low), spec.Depth);
                if (TryPlace(plan, type, position, rotation, PlacementRole.Wall) is not null)
                    break;
            }
        }
    }

    /// <summary>
    /// Pieces side by side along a wall, the tall ones at the ends (a kitchen line with its
    /// fridge); a run too long for its wall turns the corner onto the next one.
    /// </summary>
    private void PlaceRun(List<(SlotPlan Plan, FurnitureType Type)> pieces, float gap)
    {
        var items = pieces.Select(p => (p.Plan, p.Type, Variant: p.Plan.VariantOf(p.Type, PlacementRole.Wall, perPiece: true))).ToList();
        Shuffle(items);
        var tall = items.Where(i => FurnitureCatalog.Spec(i.Type, i.Variant).Height > 1.5f).ToList();
        var shorter = items.Where(i => FurnitureCatalog.Spec(i.Type, i.Variant).Height <= 1.5f).ToList();
        var ordered = tall.Take(1).Concat(shorter).Concat(tall.Skip(1)).ToList();
        const float margin = 0.1f;
        var wall = _random.Next(4);
        var length = WallLength(wall);
        var total = ordered.Sum(i => FurnitureCatalog.Spec(i.Type, i.Variant).Width) + gap * (ordered.Count - 1);
        var along = total <= length - 2f * margin ? _random.Range(margin, length - margin - total) : margin;
        var deepest = 0f;
        var turns = 0;
        foreach (var (plan, type, variant) in ordered)
        {
            var spec = FurnitureCatalog.Spec(type, variant);
            if (along + spec.Width > length - margin + 0.01f)
            {
                if (++turns > 1)
                    break;
                wall = (wall + 1) & 3;
                length = WallLength(wall);
                along = deepest + 0.02f;
            }
            var (position, rotation) = WallPose(wall, along + spec.Width / 2f, spec.Depth);
            TryPlace(plan, type, position, rotation, PlacementRole.Wall, variant: variant);
            deepest = MathF.Max(deepest, spec.Depth);
            along += spec.Width + gap;
        }
    }

    private float WallLength(int wall) => (wall & 1) == 0 ? _width : _depth;

    /// <summary>
    /// A piece with its back against a wall, facing in. Walls go round clockwise seen from above:
    /// 0 along the back (local Y = 0), 1 down the right, 2 along the front, 3 up the left.
    /// </summary>
    private (Vector2 Position, float Rotation) WallPose(int wall, float along, float depth)
    {
        var off = depth / 2f + 0.02f;
        return wall switch
        {
            0 => (new Vector2(along, off), 0f),
            1 => (new Vector2(_width - off, along), QuarterTurn),
            2 => (new Vector2(_width - along, _depth - off), HalfTurn),
            _ => (new Vector2(off, _depth - along), -QuarterTurn),
        };
    }

    private void PlaceCorners(SlotPlan plan)
    {
        for (var i = 0; i < plan.Count; i++)
        {
            var type = plan.PickType();
            var spec = FurnitureCatalog.Spec(type, plan.VariantOf(type, PlacementRole.Corner));
            var inset = MathF.Sqrt(spec.Width * spec.Width + spec.Depth * spec.Depth) / 2f + 0.05f;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var corner = _random.Next(4);
                var position = new Vector2(
                    corner is 1 or 2 ? _width - inset : inset,
                    corner >= 2 ? _depth - inset : inset) + Jitter(0.1f);
                var toCentre = new Vector2(_width, _depth) / 2f - position;
                var rotation = MathF.Atan2(-toCentre.X, toCentre.Y) + _random.Range(-0.2f, 0.2f);
                if (TryPlace(plan, type, position, rotation, PlacementRole.Corner) is not null)
                    break;
            }
        }
    }

    private void PlaceLoose(SlotPlan plan)
    {
        for (var i = 0; i < plan.Count; i++)
        {
            var type = plan.PickType();
            var spec = FurnitureCatalog.Spec(type, plan.VariantOf(type, PlacementRole.Loose));
            var inset = MathF.Sqrt(spec.Width * spec.Width + spec.Depth * spec.Depth) / 2f + 0.1f;
            if (_width < 2f * inset || _depth < 2f * inset)
                continue;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var position = new Vector2(_random.Range(inset, _width - inset), _random.Range(inset, _depth - inset));
                var rotation = MathF.Max(spec.Width, spec.Depth) < 0.9f
                    ? _random.Range(-HalfTurn, HalfTurn)
                    : _baseRotation + _random.Next(4) * QuarterTurn + _random.Range(-0.25f, 0.25f);
                if (TryPlace(plan, type, position, rotation, PlacementRole.Loose) is not null)
                    break;
            }
        }
    }

    // ---- Rows: stock, shelving and racks filling the Display, with sold-out gaps.

    private void PlaceRows(List<SlotPlan> plans)
    {
        const float pad = 0.1f;
        var end = _depth - pad;
        var index = 0;
        // Fixed rows stand at the far end, facing back into the Display.
        foreach (var plan in plans.Where(p => p.Slot.Rows > 0))
            for (var r = 0; r < plan.Slot.Rows; r++)
            {
                var type = plan.RowType(index);
                var depth = RowDepth(plan, type);
                if (end - depth < pad)
                    break;
                PlaceRow(plan, type, end - depth, HalfTurn, index++);
                end -= depth + plan.Slot.Pitch.Y * _spacing;
            }
        foreach (var plan in plans.Where(p => p.Slot.Rows == 0))
        {
            var start = pad;
            while (true)
            {
                var type = plan.RowType(index);
                var depth = RowDepth(plan, type);
                if (start + depth > end + 0.01f)
                    break;
                // A whole row now and then is sold out, but never the first.
                if (index == 0 || type == FurnitureType.PalletRack || !_random.Chance(0.06f))
                    PlaceRow(plan, type, start, 0f, index);
                index++;
                start += depth + plan.Slot.Pitch.Y * _spacing;
            }
        }
    }

    private float RowDepth(SlotPlan plan, FurnitureType type)
    {
        var depth = FurnitureCatalog.Spec(type, plan.VariantOf(type, PlacementRole.Row)).Depth;
        return plan.Slot.BackToBack ? 2f * depth : depth;
    }

    /// <summary>One row (or back-to-back pair of rows) whose band starts at <paramref name="top"/>.</summary>
    private void PlaceRow(SlotPlan plan, FurnitureType type, float top, float facing, int index)
    {
        var variant = plan.VariantOf(type, PlacementRole.Row);
        var spec = FurnitureCatalog.Spec(type, variant);
        var gap = plan.Slot.Gap * _spacing;
        if (_archetype.RowStyle == RowStyle.Clusters)
        {
            PlaceClusters(plan, top, index);
            return;
        }
        var lines = plan.Slot.BackToBack
            ? new[] { (top + spec.Depth / 2f, HalfTurn), (top + spec.Depth * 1.5f, 0f) }
            : [(top + spec.Depth / 2f, facing)];
        var endPad = Math.Clamp(0.4f * spec.Width, 0.5f, 1.1f);
        var count = (int)((_width - 2f * endPad + gap) / (spec.Width + gap));
        if (count < 1)
            return;
        var first = (_width - (count * spec.Width + (count - 1) * gap)) / 2f + spec.Width / 2f;
        if (_archetype.RowStyle == RowStyle.Staggered && index % 2 == 1 && count > 1)
        {
            first += (spec.Width + gap) / 2f;
            count--;
        }
        // Long rows are shortened now and then; a cross aisle takes out the piece in the middle.
        var (skipStart, skipEnd) = (0, 0);
        if (count >= 5 && _random.Chance(0.25f))
        {
            if (_random.Chance(0.5f))
                skipStart = 1 + _random.Next(2);
            else
                skipEnd = 1 + _random.Next(2);
        }
        foreach (var (v, rotation) in lines)
            for (var i = skipStart; i < count - skipEnd; i++)
            {
                var u = first + i * (spec.Width + gap);
                if (_archetype.CrossAisle && index % 2 == 1 && i == count / 2)
                    continue;
                // Racks stay; their sold-out gaps are the missing boxes.
                if (type != FurnitureType.PalletRack && _random.Chance(_soldOut) && plan.Placed > 0)
                    continue;
                TryPlace(plan, type, new Vector2(u, v), rotation, PlacementRole.Row, variant: variant);
            }
    }

    /// <summary>Short irregular clusters along a row band, each of one type (a market of bins).</summary>
    private void PlaceClusters(SlotPlan plan, float top, int index)
    {
        var u = 0.6f + _random.Range(0f, 0.8f);
        var cluster = index;
        while (u < _width - 0.6f)
        {
            var type = plan.RowType(cluster++);
            var variant = plan.VariantOf(type, PlacementRole.Row);
            var spec = FurnitureCatalog.Spec(type, variant);
            var size = 2 + _random.Next(3);
            for (var k = 0; k < size; k++)
            {
                var v = top + (plan.Slot.BackToBack && k % 2 == 1 ? spec.Depth * 1.5f : spec.Depth / 2f) + _random.Range(-0.15f, 0.15f);
                var rotation = (k % 2 == 1 && plan.Slot.BackToBack ? 0f : HalfTurn) + _random.Range(-0.45f, 0.45f);
                TryPlace(plan, type, new Vector2(u + spec.Width / 2f, v), rotation, PlacementRole.Row, variant: variant);
                if (!plan.Slot.BackToBack || k % 2 == 1)
                    u += spec.Width + 0.15f;
            }
            u += spec.Width + 0.6f + _random.Range(0f, 0.5f);
        }
    }

    // ---- Pieces placed relative to an anchor that has just been placed.

    private void PlaceRelatives(Placed anchor)
    {
        foreach (var plan in _plans)
        {
            var slot = plan.Slot;
            if (!IsRelative(slot.Role) || !slot.Anchors.Contains(anchor.Type) || (slot.AnchorRole is { } role && role != anchor.Role))
                continue;
            switch (slot.Role)
            {
                case PlacementRole.Under: PlaceUnder(plan, anchor); break;
                case PlacementRole.Beside: PlaceBeside(plan, anchor); break;
                case PlacementRole.InFront: PlaceInFront(plan, anchor); break;
                case PlacementRole.Around: PlaceAround(plan, anchor); break;
                case PlacementRole.OnTop: PlaceOnTop(plan, anchor); break;
            }
        }
    }

    private int RelativeCount(ThemeSlot slot) => slot.Min + _random.Next(slot.Max - slot.Min + 1);

    private void PlaceUnder(SlotPlan plan, Placed anchor) =>
        TryPlace(plan, plan.PickType(), anchor.Position, anchor.Rotation + _random.Range(-0.05f, 0.05f), PlacementRole.Under);

    private void PlaceBeside(SlotPlan plan, Placed anchor)
    {
        var count = RelativeCount(plan.Slot);
        var firstSide = _random.Chance(0.5f) ? 1f : -1f;
        for (var i = 0; i < count; i++)
        {
            var type = plan.PickType();
            var spec = FurnitureCatalog.Spec(type, plan.VariantOf(type, PlacementRole.Beside));
            var side = i == 0 ? firstSide : -firstSide;
            var offset = Along(anchor.Rotation) * (side * (anchor.Spec.Width / 2f + plan.Slot.Gap + spec.Width / 2f))
                + Front(anchor.Rotation) * (-anchor.Spec.Depth / 2f + spec.Depth / 2f);
            TryPlace(plan, type, anchor.Position + offset, anchor.Rotation + SmallTurn(), PlacementRole.Beside);
        }
    }

    private void PlaceInFront(SlotPlan plan, Placed anchor)
    {
        var type = plan.PickType();
        var spec = FurnitureCatalog.Spec(type, plan.VariantOf(type, PlacementRole.InFront));
        var offset = Front(anchor.Rotation) * (anchor.Spec.Depth / 2f + plan.Slot.Gap + spec.Depth / 2f);
        var seat = IsSeat(type);
        var rotation = anchor.Rotation + (plan.Slot.Facing ? HalfTurn : 0f) + (seat ? _random.Range(-0.35f, 0.35f) : SmallTurn());
        TryPlace(plan, type, anchor.Position + offset + (seat ? Jitter(0.1f) : Vector2.Zero), rotation, PlacementRole.InFront);
    }

    /// <summary>Seats around an anchor on the allowed sides, facing it: long sides first, then the ends.</summary>
    private void PlaceAround(SlotPlan plan, Placed anchor)
    {
        var count = RelativeCount(plan.Slot);
        var (aw, ad) = (anchor.Spec.Width, anchor.Spec.Depth);
        var seats = new List<(float X, float Y, float Facing)>();
        var type = plan.PickType();
        var spec = FurnitureCatalog.Spec(type, plan.VariantOf(type, PlacementRole.Around));
        var gap = plan.Slot.Gap;
        var perSide = Math.Max(1, (int)((aw + 0.2f) / (spec.Width + 0.12f)));
        var backY = -(ad / 2f + gap + spec.Depth / 2f);
        var endX = aw / 2f + gap + spec.Depth / 2f;
        var sides = plan.Slot.Sides;
        // Centre-out, so a few seats sit in the middle of a side.
        var order = Enumerable.Range(0, perSide).OrderBy(k => MathF.Abs(k - (perSide - 1) / 2f)).ToList();
        foreach (var k in order)
        {
            var x = (k - (perSide - 1) / 2f) * aw / perSide;
            if (sides is AroundSides.Any or AroundSides.Back)
                seats.Add((x, backY, 0f));
            if (sides is AroundSides.Any or AroundSides.Front)
                seats.Add((x, -backY, HalfTurn));
        }
        if (sides is AroundSides.Any or AroundSides.Ends)
        {
            var ends = new List<(float, float, float)> { (-endX, 0f, -QuarterTurn), (endX, 0f, QuarterTurn) };
            if (_random.Chance(0.5f))
                ends.Reverse();
            if (sides == AroundSides.Ends || _random.Chance(0.3f))
                seats.InsertRange(0, ends);
            else
                seats.AddRange(ends);
        }
        var along = Along(anchor.Rotation);
        var front = Front(anchor.Rotation);
        var placed = 0;
        foreach (var (x, y, facing) in seats)
        {
            if (placed == count)
                break;
            var seatType = plan.Slot.MixTypes ? plan.PickType() : type;
            var position = anchor.Position + along * x + front * y + Jitter(IsSeat(seatType) ? 0.08f : 0.03f);
            var rotation = anchor.Rotation + facing + (IsSeat(seatType) ? _random.Range(-0.3f, 0.3f) : SmallTurn());
            if (TryPlace(plan, seatType, position, rotation, PlacementRole.Around) is not null)
                placed++;
        }
    }

    private void PlaceOnTop(SlotPlan plan, Placed anchor)
    {
        var slot = plan.Slot;
        if (anchor.Type == FurnitureType.PalletRack)
        {
            // Boxes on each beam level, two per level.
            var levels = FurnitureCatalog.PalletRackLevels;
            for (var level = 0; level < levels.Count; level++)
                foreach (var side in new[] { -0.65f, 0.65f })
                    if (_random.Chance(level == 0 ? 0.75f : 0.6f))
                        TryPlace(plan, plan.PickType(), anchor.Position + Along(anchor.Rotation) * side, anchor.Rotation,
                            PlacementRole.OnTop, levels[level]);
            return;
        }
        var count = RelativeCount(slot);
        if (slot.Stack)
        {
            var height = anchor.Spec.Height;
            for (var level = 1; level <= count; level++)
                TryPlace(plan, anchor.Type, anchor.Position, anchor.Rotation + _random.Range(-0.08f, 0.08f), PlacementRole.OnTop,
                    level * height, anchor.Variant);
            return;
        }
        var surface = FurnitureCatalog.SurfaceHeight(anchor.Type, anchor.Variant);
        var along = Along(anchor.Rotation);
        var front = Front(anchor.Rotation);
        var (aw, ad) = (anchor.Spec.Width, anchor.Spec.Depth);
        for (var i = 0; i < count; i++)
        {
            var type = plan.PickType();
            // One piece stands off to a side (a lamp at the corner of a desk); several share the top.
            var x = count == 1
                ? (aw > 0.8f ? (_random.Chance(0.5f) ? 1f : -1f) * (aw / 2f - 0.25f) : 0f)
                : -aw / 2f + (i + 0.5f) * aw / count;
            var y = _random.Range(-0.15f, 0.15f) * ad;
            TryPlace(plan, type, anchor.Position + along * x + front * y, _random.Range(-HalfTurn, HalfTurn), PlacementRole.OnTop, surface);
        }
    }

    // ---- Placement.

    /// <summary>Places a piece inside the Display, then the pieces that stand relative to it.</summary>
    private Placed? TryPlace(SlotPlan plan, FurnitureType type, Vector2 position, float rotation, PlacementRole role, float elevation = 0f, int? variant = null)
    {
        var chosen = variant ?? plan.VariantOf(type, role);
        var spec = FurnitureCatalog.Spec(type, chosen);
        if (elevation == 0f && !InsideDisplay(position, rotation, spec))
            return null;
        if (!_cell.Place(_frame, type, chosen, position, rotation, elevation, plan.Tint))
            return null;
        plan.Placed++;
        var placed = new Placed(type, chosen, position, rotation, role);
        if (elevation == 0f)
            PlaceRelatives(placed);
        return placed;
    }

    private bool InsideDisplay(Vector2 position, float rotation, FurnitureSpec spec)
    {
        var along = Along(rotation) * (spec.Width / 2f);
        var front = Front(rotation) * (spec.Depth / 2f);
        var reach = new Vector2(MathF.Abs(along.X) + MathF.Abs(front.X), MathF.Abs(along.Y) + MathF.Abs(front.Y));
        return position.X - reach.X > -BoundsTolerance && position.Y - reach.Y > -BoundsTolerance
            && position.X + reach.X < _width + BoundsTolerance && position.Y + reach.Y < _depth + BoundsTolerance;
    }

    private static Vector2 Along(float rotation) => new(MathF.Cos(rotation), MathF.Sin(rotation));

    private static Vector2 Front(float rotation) => new(-MathF.Sin(rotation), MathF.Cos(rotation));

    private static bool IsSeat(FurnitureType type) => type is FurnitureType.Chair or FurnitureType.OfficeChair or FurnitureType.GardenChair
        or FurnitureType.BarStool or FurnitureType.KidsChair or FurnitureType.Armchair;

    private Vector2 Jitter(float reach) => new(_random.Range(-reach, reach), _random.Range(-reach, reach));

    /// <summary>A small turn off the grid, 2° to 8° either way.</summary>
    private float SmallTurn() => (_random.Chance(0.5f) ? 1f : -1f) * _random.Range(0.035f, 0.14f);

    private void Shuffle<T>(List<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private readonly record struct Placed(FurnitureType Type, int Variant, Vector2 Position, float Rotation, PlacementRole Role)
    {
        public FurnitureSpec Spec => FurnitureCatalog.Spec(Type, Variant);
    }

    /// <summary>
    /// A slot as it appears in this Display: its type (unless it mixes types), a variant per type
    /// and one tint of the Display's palette, so a set of chairs matches.
    /// </summary>
    private sealed record SlotPlan
    {
        private readonly DisplayFurnisher _owner;
        private readonly Dictionary<(FurnitureType, bool), int> _variants = [];
        private readonly FurnitureType _type;
        private readonly FurnitureType[] _rowTypes;

        public SlotPlan(ThemeSlot slot, DisplayFurnisher owner)
        {
            _owner = owner;
            Slot = slot;
            Odds = slot.Odds;
            var random = owner._random;
            var own = ThemeCatalog.OwnTypes(owner._cell.Department.Type);
            var candidates = slot.Role == PlacementRole.Row && slot.Types.Any(own.Contains)
                ? slot.Types.Where(own.Contains).ToArray()
                : slot.Types.ToArray();
            _type = random.Pick(candidates);
            // Mixed rows show all of a few types, or two of many.
            var mixed = candidates.ToList();
            owner.Shuffle(mixed);
            _rowTypes = slot.MixTypes ? [.. mixed.Take(mixed.Count <= 3 ? mixed.Count : 2)] : [_type];
            Tint = random.Pick(owner._palette);
            Count = slot.Role is PlacementRole.Wall or PlacementRole.Corner or PlacementRole.Loose ? owner.CountFor(slot) : 1;
        }

        public ThemeSlot Slot { get; }
        public float Odds { get; init; }
        public int Tint { get; }
        public int Count { get; }
        public int Placed { get; set; }

        public FurnitureType PickType() => Slot.MixTypes ? _owner._random.Pick(Slot.Types) : _type;

        public FurnitureType RowType(int row) => _rowTypes[row % _rowTypes.Length];

        /// <summary>
        /// The slot's variant of a type, chosen once so a set matches; runs and rows vary now and
        /// then per piece. Tall variants never stand in the middle of a room.
        /// </summary>
        public int VariantOf(FurnitureType type, PlacementRole role, bool perPiece = false)
        {
            var centre = role == PlacementRole.Centre;
            if (!_variants.TryGetValue((type, centre), out var variant))
                _variants[(type, centre)] = variant = ChooseVariant(type, centre);
            if (perPiece && _owner._random.Chance(0.3f))
                return ChooseVariant(type, centre);
            return variant;
        }

        private int ChooseVariant(FurnitureType type, bool centre)
        {
            var random = _owner._random;
            if (_owner._archetype.PreferredVariant is { } preferred && preferred.Type == type && random.Chance(0.75f))
                return preferred.Variant;
            var allowed = Enumerable.Range(0, FurnitureCatalog.VariantCount(type))
                .Where(v => !centre || v == 0 || FurnitureCatalog.Spec(type, v).Height <= 1.5f).ToArray();
            return random.Pick(allowed);
        }
    }
}
