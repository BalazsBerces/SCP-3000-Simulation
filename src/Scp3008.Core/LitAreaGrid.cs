using System.Numerics;
using System.Runtime.CompilerServices;

namespace Scp3008.Core;

/// <summary>
/// The <see cref="LitArea"/>s of one list bucketed on a coarse wrapped grid, so the brightness at a spot looks only at
/// the areas around it. Made once per list: the Simulation hands out the same list while the lights stay as they were.
/// </summary>
internal sealed class LitAreaGrid
{
    private const float TargetCellSize = 16;

    private static readonly ConditionalWeakTable<IReadOnlyList<LitArea>, LitAreaGrid> Grids = [];

    private readonly StoreSpace _space;
    private readonly int _cellsPerSide;
    private readonly float _cellSize;
    // By cell, only the cells some area reaches: the lights cover a small part of the Store.
    private readonly Dictionary<int, List<LitArea>> _cells = [];

    private LitAreaGrid(StoreSpace space, IReadOnlyList<LitArea> areas)
    {
        _space = space;
        _cellsPerSide = Math.Max(1, (int)(space.Size / TargetCellSize));
        _cellSize = space.Size / _cellsPerSide;
        foreach (var area in areas)
        {
            // One cell more on each side, so rounding at a cell edge never leaves an area out of a cell it reaches.
            var position = space.Wrap(area.Position);
            int x0 = (int)MathF.Floor((position.X - area.Radius) / _cellSize) - 1, x1 = (int)MathF.Floor((position.X + area.Radius) / _cellSize) + 1;
            int y0 = (int)MathF.Floor((position.Y - area.Radius) / _cellSize) - 1, y1 = (int)MathF.Floor((position.Y + area.Radius) / _cellSize) + 1;
            x1 = Math.Min(x1, x0 + _cellsPerSide - 1);
            y1 = Math.Min(y1, y0 + _cellsPerSide - 1);
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var cell = Wrap(y) * _cellsPerSide + Wrap(x);
                    if (!_cells.TryGetValue(cell, out var inCell))
                        _cells[cell] = inCell = [];
                    inCell.Add(area);
                }
        }
    }

    public static LitAreaGrid Of(StoreSpace space, IReadOnlyList<LitArea> areas) =>
        Grids.GetValue(areas, list => new LitAreaGrid(space, list));

    /// <summary>The brightest of the areas reaching <paramref name="position"/> (it may be unwrapped), or 0 when none does.</summary>
    public float BrightnessAt(Vector2 position)
    {
        var wrapped = _space.Wrap(position);
        var x = Math.Min((int)(wrapped.X / _cellSize), _cellsPerSide - 1);
        var y = Math.Min((int)(wrapped.Y / _cellSize), _cellsPerSide - 1);
        var brightest = 0f;
        var found = false;
        if (_cells.TryGetValue(y * _cellsPerSide + x, out var areas))
            foreach (var area in areas)
                if (_space.Distance(position, area.Position) <= area.Radius)
                {
                    brightest = found ? Math.Max(brightest, area.Brightness) : area.Brightness;
                    found = true;
                }
        return brightest;
    }

    private int Wrap(int cell) => (cell % _cellsPerSide + _cellsPerSide) % _cellsPerSide;
}
