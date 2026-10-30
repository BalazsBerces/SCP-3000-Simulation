namespace Scp3008.Core;

public sealed partial class Simulation
{
    private readonly List<Gear> _gearSources = [];

    private void GenerateGear()
    {
        foreach (var group in _furniture.Pieces.Where(p => p.Type is FurnitureType.FlatPackBox or FurnitureType.LabelledBox)
            .GroupBy(p => _departments.At(p.Position).Id))
        {
            var box = group.First();
            _instructions.Add(new(box.Id, Recipes.Middle[box.Id % Recipes.Middle.Count].Gear));
        }
        var random = SeededRandom.For(_config.Seed, 146);
        var candidates = _furniture.Pieces.SelectMany(piece => FoundKinds(piece).Select(kind => (piece, kind)))
            .GroupBy(p => (_departments.At(p.piece.Position).Id, p.kind));
        foreach (var group in candidates)
        {
            // One scarce find of each suitable kind per Department, rather than a tool on every Display.
            var suitable = group.Where(p => p.kind != GearKind.ShoppingCart || CartCanLeave(p.piece)).ToArray();
            if (suitable.Length == 0) continue;
            var source = random.Pick(suitable);
            var gear = new Gear(NextGearId(), source.kind, source.piece.Position) { FurnitureId = source.piece.Id };
            _gear.Add(gear);
            _gearSources.Add(gear);
        }
    }

    private bool CartCanLeave(FurniturePiece cart)
    {
        var graveyard = _pointsOfInterest[cart.PointOfInterestId];
        var aisle = _navigation.NearestWalkable(graveyard.Centre + new System.Numerics.Vector2(graveyard.Size.X / 2 + 4, 0));
        return _navigation.FindPath(_navigation.NearestWalkable(cart.Position), aisle) is not null;
    }

    private IEnumerable<GearKind> FoundKinds(FurniturePiece p)
    {
        if (p.PointOfInterestId >= 0 && _pointsOfInterest[p.PointOfInterestId].Type == PointOfInterestType.LostAndFound &&
            p.Type == FurnitureType.LabelledBox)
        {
            yield return p.Id % 2 == 0 ? GearKind.Screwdriver : GearKind.CanvasBag;
            if (p.Id % 3 == 0) yield return GearKind.Crowbar;
            yield break;
        }
        var department = _departments.At(p.Position).Type;
        if (p.Type == FurnitureType.FlatPackBox && department == DepartmentType.SelfServeWarehouse)
            yield return GearKind.HexKey;
        if (p.Type == FurnitureType.KitchenCabinet && department == DepartmentType.BathroomsAndKitchens)
            yield return GearKind.KitchenKnife;
        if (p.Type is FurnitureType.Desk or FurnitureType.ShelfUnit && department is DepartmentType.Workspaces or DepartmentType.TextilesAndDecor)
            yield return GearKind.Scissors;
        if (p.Type == FurnitureType.GroceryShelf && department == DepartmentType.RestaurantAndFoodMarket ||
            p.Type == FurnitureType.ShelfUnit && department == DepartmentType.TextilesAndDecor)
            yield return GearKind.CanvasBag;
        if (p.Type == FurnitureType.ShoppingCart && p.PointOfInterestId >= 0 &&
            _pointsOfInterest[p.PointOfInterestId].Type == PointOfInterestType.ShoppingCartGraveyard)
            yield return GearKind.ShoppingCart;
        if (p.Type == FurnitureType.ShelfUnit && department == DepartmentType.LightingAndElectrical)
            yield return GearKind.Flashlight;
    }

    private void RestockGear(IReadOnlyDictionary<int, double> refill, Func<int, bool> present)
    {
        foreach (var source in _gearSources)
        {
            if (!present(source.FurnitureId) || !_restockOrigins.ContainsKey(source.FurnitureId) ||
                refill.GetValueOrDefault(_departments.At(source.Position).Id) < 1 ||
                _gear.Any(g => g.FurnitureId == source.FurnitureId && g.Kind == source.Kind)) continue;
            _gear.Add(source with { Id = NextGearId() });
        }
    }
}
