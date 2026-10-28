namespace Scp3008.Core;

/// <summary>The eight abstract Materials counted in Village stores.</summary>
public enum MaterialKind { WoodPanel, Metal, Fabric, Leather, Foam, Glass, Electrics, Plastic }
public enum ToolLevel { BareHands = 1, HexKey, KnifeOrScissors, Screwdriver, SawOrCrowbar }

internal static class FurnitureResources
{
    public static ToolLevel Tool(FurniturePiece p) => p.Type switch
    {
        FurnitureType.CardboardBox or FurnitureType.LabelledBox or FurnitureType.FlatPackBox or
        FurnitureType.BlanketStack or FurnitureType.PlayBall or FurnitureType.FoamStep or FurnitureType.LitterPaper or
        FurnitureType.Note => ToolLevel.BareHands,
        FurnitureType.Armchair or FurnitureType.Sofa or FurnitureType.Mattress or FurnitureType.Rug or
        FurnitureType.Bed or FurnitureType.SleepingBag or FurnitureType.CushionBin or FurnitureType.CurtainRack or
        FurnitureType.Tent or FurnitureType.Hammock or FurnitureType.PlushBear => ToolLevel.KnifeOrScissors,
        FurnitureType.TableLamp or FurnitureType.FloorLamp or FurnitureType.DeskLamp or FurnitureType.ArcLamp or
        FurnitureType.Fridge or FurnitureType.Freezer or FurnitureType.Stove or FurnitureType.WashingMachine or
        FurnitureType.HandDryer or FurnitureType.CoffeeMachine or FurnitureType.CampLantern or
        FurnitureType.ExitSign or FurnitureType.WallClock => ToolLevel.Screwdriver,
        FurnitureType.PalletRack or FurnitureType.ShoppingCart or FurnitureType.UnsortedCart or FurnitureType.BoxCart or
        FurnitureType.LogSeat or FurnitureType.GardenChair or FurnitureType.GardenTable or FurnitureType.Grill or
        FurnitureType.TankRack or FurnitureType.TrayRail or FurnitureType.NetRailing => ToolLevel.SawOrCrowbar,
        _ => ToolLevel.HexKey,
    };

    public static bool HasTool(FurniturePiece piece, Gear? hand) => Tool(piece) switch
    {
        ToolLevel.BareHands => hand is null || hand.Kind is not (GearKind.ShoppingCart or GearKind.PlatformCart),
        ToolLevel.HexKey => hand?.Kind == GearKind.HexKey,
        ToolLevel.KnifeOrScissors => hand?.Kind is GearKind.KitchenKnife or GearKind.Scissors,
        ToolLevel.Screwdriver => hand?.Kind == GearKind.Screwdriver,
        ToolLevel.SawOrCrowbar => hand?.Kind is GearKind.Saw or GearKind.Crowbar,
        _ => false,
    };

    public static Dictionary<MaterialKind, int> Yield(FurniturePiece p) => p.Type switch
    {
        FurnitureType.Armchair or FurnitureType.Sofa => new()
            { [FurnitureCatalog.Spec(p).IsLeather || p.Type == FurnitureType.Armchair && p.Variant == 0 ? MaterialKind.Leather : MaterialKind.Fabric] = 4, [MaterialKind.Foam] = 4, [MaterialKind.WoodPanel] = 3 },
        FurnitureType.Bed => new() { [MaterialKind.Metal] = 6, [MaterialKind.Fabric] = 4, [MaterialKind.Foam] = 4 },
        FurnitureType.Mattress or FurnitureType.SleepingBag or FurnitureType.CushionBin => new()
            { [MaterialKind.Fabric] = 4, [MaterialKind.Foam] = 4 },
        FurnitureType.Rug or FurnitureType.BlanketStack or FurnitureType.CurtainRack or FurnitureType.Tent or
        FurnitureType.Hammock or FurnitureType.PlushBear => new() { [MaterialKind.Fabric] = 4 },
        FurnitureType.TableLamp or FurnitureType.FloorLamp or FurnitureType.DeskLamp or FurnitureType.ArcLamp or
        FurnitureType.CampLantern or FurnitureType.ExitSign or FurnitureType.WallClock => new()
            { [MaterialKind.Electrics] = 2, [MaterialKind.Glass] = 2, [MaterialKind.Metal] = 1 },
        FurnitureType.Fridge or FurnitureType.Freezer or FurnitureType.WashingMachine or FurnitureType.Stove or
        FurnitureType.CoffeeMachine or FurnitureType.HandDryer or FurnitureType.HotFoodCounter or FurnitureType.DessertCounter => new()
            { [MaterialKind.Metal] = 6, [MaterialKind.Electrics] = 3, [MaterialKind.Glass] = 2, [MaterialKind.Plastic] = 2 },
        FurnitureType.ShoppingCart or FurnitureType.UnsortedCart or FurnitureType.BoxCart or FurnitureType.PalletRack or
        FurnitureType.TankRack or FurnitureType.TrayRail or FurnitureType.NetRailing or FurnitureType.Grill => new() { [MaterialKind.Metal] = 6 },
        FurnitureType.FloorMirror or FurnitureType.RestroomMirror or FurnitureType.FishTank or FurnitureType.DisplayCabinet => new()
            { [MaterialKind.Glass] = 4, [MaterialKind.Metal] = 2 },
        FurnitureType.CardboardBox or FurnitureType.LabelledBox or FurnitureType.FlatPackBox or FurnitureType.Note or FurnitureType.LitterPaper => new()
            { [MaterialKind.WoodPanel] = 2 },
        FurnitureType.PlayBall or FurnitureType.RubberDuck or FurnitureType.OutOfOrderSign or FurnitureType.Mannequin or
        FurnitureType.PlayCube or FurnitureType.Tray or FurnitureType.BallPool => new() { [MaterialKind.Plastic] = 3 },
        FurnitureType.FoamStep => new() { [MaterialKind.Foam] = 3, [MaterialKind.Fabric] = 1 },
        _ => new() { [MaterialKind.WoodPanel] = 4, [MaterialKind.Metal] = 1 },
    };

    public static float Weight(FurniturePiece piece) => piece.Type switch
    {
        FurnitureType.CardboardBox or FurnitureType.LabelledBox => 2,
        FurnitureType.FlatPackBox => 15,
        FurnitureType.Mattress => 20,
        FurnitureType.Armchair => 40,
        FurnitureType.Sofa => 75,
        FurnitureType.Wardrobe => piece.Variant == 2 ? 70 : 140,
        FurnitureType.PalletRack or FurnitureType.Fridge or FurnitureType.Freezer or FurnitureType.FountainBowl => 160,
        FurnitureType.ShoppingCart => 20,
        _ => Math.Clamp(FurnitureCatalog.Spec(piece).Width * FurnitureCatalog.Spec(piece).Depth *
            FurnitureCatalog.Spec(piece).Height * 25, 1, 250),
    };
}
