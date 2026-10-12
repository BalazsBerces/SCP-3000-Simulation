using System.Numerics;

namespace Scp3008.Core;

/// <summary>The kinds of Department; several separate Departments may share a type.</summary>
public enum DepartmentType
{
    RestaurantAndFoodMarket,
    Showrooms,
    LightingAndElectrical,
    SelfServeWarehouse,
    BathroomsAndKitchens,
    PlantSection,
    KidsArea,
    Workspaces,
    TextilesAndDecor,
    Outdoor,
    DiningAndHallway,
}

/// <summary>
/// One Department: a connected region of the Store made of department cells of one type.
/// </summary>
/// <param name="Id">Index of the Department in <see cref="DepartmentMap.All"/>.</param>
/// <param name="Type">What kind of Department it is, which decides its look and Furniture.</param>
/// <param name="Anchor">A point on the floor inside the Department, e.g. for labels.</param>
/// <param name="CellCount">Number of department cells it covers.</param>
/// <param name="Area">Floor area in square metres.</param>
public sealed record Department(int Id, DepartmentType Type, Vector2 Anchor, int CellCount, float Area);

/// <summary>
/// Which Department covers each part of the Store. The floor is divided into square department
/// cells; every cell belongs to exactly one Department. Cell coordinates and positions wrap.
/// </summary>
public sealed class DepartmentMap
{
    private readonly int[] _departmentOfCell;

    internal DepartmentMap(float cellSize, int cellsPerSide, int[] departmentOfCell, IReadOnlyList<Department> departments)
    {
        CellSize = cellSize;
        CellsPerSide = cellsPerSide;
        _departmentOfCell = departmentOfCell;
        All = departments;
    }

    /// <summary>Edge length of a department cell, in metres.</summary>
    public float CellSize { get; }

    /// <summary>Number of department cells along each edge of the Store.</summary>
    public int CellsPerSide { get; }

    /// <summary>Every Department in the Store, indexed by <see cref="Department.Id"/>.</summary>
    public IReadOnlyList<Department> All { get; }

    /// <summary>The Department covering a department cell; coordinates wrap.</summary>
    public Department AtCell(int x, int y) =>
        All[_departmentOfCell[Mod(y, CellsPerSide) * CellsPerSide + Mod(x, CellsPerSide)]];

    /// <summary>The Department covering a position on the floor; positions may be unwrapped.</summary>
    public Department At(Vector2 position) =>
        AtCell((int)Math.Floor(position.X / CellSize), (int)Math.Floor(position.Y / CellSize));

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
