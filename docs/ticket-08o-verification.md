# Ticket 08o verification: Stock Furniture holds Goods

## What was built

- `GoodsClass` (food, drink, comfort goods, supplies) and `GoodsKind` (fresh, tinned and dry food, live fish, meatballs, almond cake,
  marshmallows; bottled water, soft drinks, coffee; sweets, candles; batteries, bulbs, cables, tape), with `GoodsCatalog.ClassOf`,
  `SpoilsFast`, `KindsOf(DepartmentType)` and `KindsOn(department, theme, type)`.
- `StockGenerator` gives each piece of Stock Furniture one kind and an amount; `WorldSnapshot.Stock` lists all of it
  (`StockedGoods`: piece, kind, amount, left-behind flag), `WorldSnapshot.IsStockFurniture` says which pieces are Stock Furniture
  and `WorldSnapshot.StockByClass` totals it per Goods class.
- `StockConfig` (on `WorldConfig.Stock`) exposes every amount range and the left-behind share.

## Decisions

- **Stock Furniture** is decided by Department type, Display theme and Furniture type, so a toy shelf in a kids' bedroom or a
  fridge in a kitchen showroom stays Display Furniture:
  - Restaurant/food market, food market: grocery shelves (tinned, dry food, water, soft drinks, coffee), freezers (fresh food),
    fridges (fresh food, water, soft drinks), deli counters (fresh food); café counters (coffee, soft drinks).
  - Lighting & electrical: shelving rows and the lamp displays' wall shelves (bulbs, batteries).
  - Self-serve warehouse: pallet racks (tinned and dry food, cables, tape).
  - Textiles & Decor: the boutique's bins (candles). Kids' area: shelving rows (sweets).
  - Cafeteria hot food and dessert counters, Aquarium tanks, the Camping ground's two stands, a Lost and found's labelled boxes and cart.
- The Cafeteria meals, Aquarium fish and Camping ground goods keep their own lists (`CounterMeals`, `TankFish`, `CampGoods`, which the
  renderer reads) and are also in `Stock` as Meatballs/AlmondCake, LiveFish, Marshmallows/TinnedFood. The supplies stand's
  backpacks are Gear, not Goods, so they are not in `Stock`.
- **Left-behind finds**: a share (`LeftBehindShare`, 2 %, rounded down) of the Display fridges, freezers, kitchen cabinets,
  sideboards and display cabinets holds one unit of a random shop kind, flagged `LeftBehind`. A fixed count instead of a
  per-piece chance keeps the finds under the share in every Store.
- Each Lost and found holds 2–4 finds of 1–2 units of any shop kind, each on its own labelled box or cart.
- Removing Furniture through the scenario builder also drops the Goods on it.

## Tests

`StockTests` (17 facade-level tests, Release): every entry on Stock Furniture except left-behind finds, all Display Stock Furniture
stocked; showroom fridges and kitchen cabinets empty but for under the share across seeds 3008, 42, 7; share tunable (0 and 0.2);
every kind in one of four classes; each Department type stocks only its own kinds; each Lost and found holds a few Goods of any
class; same seed same stock; meals, fish and camping goods in the listing; amounts from configuration; stock count per class printed.

Full suite: 616 passed, 0 failed (Release). Godot headless build: no errors; `dotnet build` of the game: 0 warnings, 0 errors.
A windowed run of the game (seed 3008) starts and draws the Store as before.

Stock count per Goods class (printed by `Prints_the_stock_count_per_goods_class`, default configuration):

| Seed | Stocked pieces | Food | Drink | Comfort | Supplies | Left-behind finds |
|------|---------------:|-----:|------:|--------:|---------:|------------------:|
| 3008 | 39,835 | 262,239 | 20,653 | 199,578 | 259,483 | 642 |
| 42   | 37,082 | 247,442 | 14,435 | 175,851 | 252,977 | 725 |
| 7    | 37,467 | 228,722 | 17,864 | 217,997 | 231,769 | 684 |

## Needs human review

- The balance of the totals: drink is an order of magnitude scarcer than food, and comfort goods (mostly candles in the
  Textiles & Decor bins) nearly as plentiful as food. Tune with `StockConfig` or the stock table in `GoodsCatalog`.
- Nothing is drawn yet: the shelves do not show their Goods (the Restock ticket makes them visibly fill and empty).

No models, props or textures were added; nothing is generated procedurally.
