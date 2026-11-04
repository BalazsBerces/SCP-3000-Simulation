# Ticket 30: Caravans and barter trade

- Core: `Simulation.Caravans.cs`; tests `CaravanTests` (move goods, prices, Recipe + Known map, Closing-time ambush, dead-zone detour, save/load).
- Barter model: a Village's price for a Goods unit is 1..4, higher the less it holds against its need (food: 2 per person). The destination pays for the cargo's value (Recipes 10, Gear 5, map entries 0.5) with its most spare Goods, carried home by the Caravan.
- Decisions: Caravan members are the sender's own Villagers (Traders first, up to 2); only Traders Villages dispatch Caravans on their own; the Staff ambush at 5 m, hit every member and the cargo is lost.
- Human review: the Observer follows a Caravan with the debug panel's "Follow Caravan" button; no dedicated Caravan model or cargo visuals were added (Villagers walk as usual). No new models/textures.
