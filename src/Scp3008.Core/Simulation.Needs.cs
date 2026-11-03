namespace Scp3008.Core;

public sealed partial class Simulation
{
    private bool _stockChanged;

    private bool TakeGoods(int villagerId, StockedGoods goods, StoreClock clock)
    {
        if (goods.FurnitureId == -1)
        {
            var village = _villages.FirstOrDefault(v => v.VillagerIds.Contains(villagerId));
            if (village is null || village.Stores.GetValueOrDefault(goods.Kind) <= 0)
                return false;
            ChangeStores(village.Id, goods.Kind, -1);
        }
        else if (!CollectGoods(goods))
            return false;
        Emit(new GoodsConsumed(clock, villagerId, goods.FurnitureId, goods.Kind));
        return true;
    }

    private bool CollectGoods(StockedGoods goods)
    {
        var index = _stock.IndexOf(goods.FurnitureId, goods.Kind, goods.LeftBehind);
        if (index < 0)
            return false;
        _stock = _stock.WithAmount(index, _stock[index].Amount - 1);
        _stockChanged = true;
        return true;
    }

    private void ChangeStores(int villageId, GoodsKind kind, int amount)
    {
        var village = _villages[villageId];
        var stores = village.Stores.ToDictionary(p => p.Key, p => p.Value);
        stores[kind] = stores.GetValueOrDefault(kind) + amount;
        if (stores[kind] == 0)
            stores.Remove(kind);
        _villages[villageId] = village with { Stores = new System.Collections.ObjectModel.ReadOnlyDictionary<GoodsKind, int>(stores) };
    }

    private void DeliverGoods(int villagerId, int villageId, GoodsKind kind)
    {
        ChangeStores(villageId, kind, 1);
        Emit(new GoodsDelivered(new StoreClock(_tick), villagerId, villageId, kind));
    }
}
