namespace Scp3008.Core;

public sealed partial class Simulation
{
    private bool _stockChanged;

    private bool TakeGoods(int villagerId, StockedGoods goods, StoreClock clock)
    {
        var stock = _stock.ToList();
        var index = stock.FindIndex(g => g.FurnitureId == goods.FurnitureId && g.Kind == goods.Kind && g.LeftBehind == goods.LeftBehind);
        if (index < 0)
            return false;
        var current = stock[index];
        if (current.Amount == 1)
            stock.RemoveAt(index);
        else
            stock[index] = current with { Amount = current.Amount - 1 };
        _stock = stock.AsReadOnly();
        _stockChanged = true;
        Emit(new GoodsConsumed(clock, villagerId, goods.FurnitureId, goods.Kind));
        return true;
    }
}
