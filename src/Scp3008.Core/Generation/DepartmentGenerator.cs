using System.Numerics;

namespace Scp3008.Core.Generation;

/// <summary>
/// Splits the Store into Departments from the seed: seeded sites, each with a Department type
/// and a size weight, claim the department cells closest to them (a weighted Voronoi diagram on
/// the wrapping floor); neighbouring cells of one type then form one Department.
/// </summary>
internal static class DepartmentGenerator
{
    /// <summary>Department cells are 16 m, a quarter of a chunk, so 4 × 4 cells make a chunk.</summary>
    public const float CellSize = 16f;

    private const int SiteCount = 48;
    private const float MinSiteSpacing = 110f;
    private const int MinDepartmentCells = 6;

    /// <summary>
    /// How large Departments of each type grow relative to others. The Restaurant/food market and
    /// Plant section are kept small and scarce, so the food they hold is contested.
    /// </summary>
    private static readonly Dictionary<DepartmentType, (float Weight, float Size)> TypeOdds = new()
    {
        [DepartmentType.RestaurantAndFoodMarket] = (0.8f, 0.75f),
        [DepartmentType.Showrooms] = (3.0f, 1.15f),
        [DepartmentType.LightingAndElectrical] = (1.5f, 1.0f),
        [DepartmentType.SelfServeWarehouse] = (2.5f, 1.25f),
        [DepartmentType.BathroomsAndKitchens] = (2.0f, 1.0f),
        [DepartmentType.PlantSection] = (0.8f, 0.8f),
        [DepartmentType.KidsArea] = (1.0f, 0.85f),
    };

    public static DepartmentMap Generate(int seed, StoreSpace space)
    {
        var cellsPerSide = (int)Math.Round(space.Size / CellSize);
        var random = SeededRandom.For(seed, stream: 1);
        var sites = PlaceSites(random, space, cellsPerSide);

        var typeOfCell = new DepartmentType[cellsPerSide * cellsPerSide];
        for (var y = 0; y < cellsPerSide; y++)
            for (var x = 0; x < cellsPerSide; x++)
                typeOfCell[y * cellsPerSide + x] = ClosestSite(sites, space, CellCentre(x, y)).Type;

        AbsorbSlivers(typeOfCell, cellsPerSide);
        return BuildMap(typeOfCell, cellsPerSide, sites);
    }

    private static Vector2 CellCentre(int x, int y) => new((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    private static List<Site> PlaceSites(SeededRandom random, StoreSpace space, int cellsPerSide)
    {
        // Every type appears at least twice; the rest are drawn by weight.
        var types = new List<DepartmentType>();
        foreach (var type in Enum.GetValues<DepartmentType>())
            types.AddRange([type, type]);
        var totalWeight = TypeOdds.Values.Sum(o => o.Weight);
        while (types.Count < SiteCount)
        {
            var roll = random.NextFloat() * totalWeight;
            foreach (var (type, odds) in TypeOdds)
            {
                roll -= odds.Weight;
                if (roll < 0f)
                {
                    types.Add(type);
                    break;
                }
            }
            if (roll >= 0f)
                types.Add(DepartmentType.Showrooms);
        }
        for (var i = types.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (types[i], types[j]) = (types[j], types[i]);
        }

        var sites = new List<Site>();
        foreach (var type in types)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var cell = (X: random.Next(cellsPerSide), Y: random.Next(cellsPerSide));
                var position = CellCentre(cell.X, cell.Y);
                // Spacing relaxes slowly if the floor gets crowded, so every site finds a place.
                var spacing = MinSiteSpacing * (1f - attempt / 250f);
                if (sites.All(s => space.Distance(s.Position, position) >= spacing))
                {
                    sites.Add(new Site(type, position, TypeOdds[type].Size * random.Range(0.85f, 1.15f)));
                    break;
                }
            }
        }
        return sites;
    }

    private static Site ClosestSite(List<Site> sites, StoreSpace space, Vector2 position)
    {
        var best = sites[0];
        var bestScore = float.MaxValue;
        foreach (var site in sites)
        {
            var score = space.Distance(site.Position, position) / site.Size;
            if (score < bestScore)
            {
                bestScore = score;
                best = site;
            }
        }
        return best;
    }

    /// <summary>Gives tiny regions (weighted Voronoi diagrams can leave islands) to their most common neighbour type.</summary>
    private static void AbsorbSlivers(DepartmentType[] typeOfCell, int cellsPerSide)
    {
        foreach (var component in Components(typeOfCell, cellsPerSide))
        {
            if (component.Count >= MinDepartmentCells)
                continue;
            var own = typeOfCell[component[0]];
            var neighbourType = component
                .SelectMany(cell => Neighbours(cell, cellsPerSide))
                .Select(cell => typeOfCell[cell])
                .Where(type => type != own)
                .GroupBy(type => type)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => (DepartmentType?)group.Key)
                .FirstOrDefault();
            if (neighbourType is { } replacement)
                foreach (var cell in component)
                    typeOfCell[cell] = replacement;
        }
    }

    private static DepartmentMap BuildMap(DepartmentType[] typeOfCell, int cellsPerSide, List<Site> sites)
    {
        var departmentOfCell = new int[typeOfCell.Length];
        var departments = new List<Department>();
        foreach (var component in Components(typeOfCell, cellsPerSide))
        {
            var id = departments.Count;
            var members = component.ToHashSet();
            // Anchor on a generating site when the region holds one (it lies well inside), else on its first cell.
            var anchor = sites
                .Select(s => (Site: s, Cell: CellIndex(s.Position, cellsPerSide)))
                .Where(s => members.Contains(s.Cell))
                .Select(s => (Vector2?)s.Site.Position)
                .FirstOrDefault() ?? CellCentre(component[0] % cellsPerSide, component[0] / cellsPerSide);
            foreach (var cell in component)
                departmentOfCell[cell] = id;
            departments.Add(new Department(id, typeOfCell[component[0]], anchor, component.Count, component.Count * CellSize * CellSize));
        }
        return new DepartmentMap(CellSize, cellsPerSide, departmentOfCell, departments.AsReadOnly());
    }

    private static int CellIndex(Vector2 position, int cellsPerSide) =>
        (int)(position.Y / CellSize) * cellsPerSide + (int)(position.X / CellSize);

    /// <summary>Connected groups of same-type cells (4-neighbour, across the seams), in scan order.</summary>
    private static List<List<int>> Components(DepartmentType[] typeOfCell, int cellsPerSide)
    {
        var seen = new bool[typeOfCell.Length];
        var components = new List<List<int>>();
        var stack = new Stack<int>();
        for (var start = 0; start < typeOfCell.Length; start++)
        {
            if (seen[start])
                continue;
            var component = new List<int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var cell = stack.Pop();
                component.Add(cell);
                foreach (var neighbour in Neighbours(cell, cellsPerSide))
                {
                    if (seen[neighbour] || typeOfCell[neighbour] != typeOfCell[start])
                        continue;
                    seen[neighbour] = true;
                    stack.Push(neighbour);
                }
            }
            component.Sort();
            components.Add(component);
        }
        return components;
    }

    private static IEnumerable<int> Neighbours(int cell, int cellsPerSide)
    {
        var x = cell % cellsPerSide;
        var y = cell / cellsPerSide;
        yield return y * cellsPerSide + (x + 1) % cellsPerSide;
        yield return y * cellsPerSide + (x + cellsPerSide - 1) % cellsPerSide;
        yield return (y + 1) % cellsPerSide * cellsPerSide + x;
        yield return (y + cellsPerSide - 1) % cellsPerSide * cellsPerSide + x;
    }

    private sealed record Site(DepartmentType Type, Vector2 Position, float Size);
}
