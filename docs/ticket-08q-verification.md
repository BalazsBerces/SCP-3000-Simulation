# Ticket 08q verification: Sector Surplus and Shortage

## What was built

- `WorldSnapshot.SectorStocks`: a `SectorStock(SectorId, ByClass, Surplus, Shortage)` per Sector, summed from `Stock` over the
  Furniture in each Sector (`SectorAt`, wrapped).
- The stock generator reshapes the Display stock's amounts after drawing it (`StockConfig.SectorGuarantee`, on by default):
  1. each Goods class's Store total is scaled into `ClassTotalBand` (20 %) of its target (`TargetPerHectare`);
  2. a Sector with no Surplus has its relatively richest class raised, taken proportionally from the other Sectors;
  3. a Sector with no Shortage has its relatively poorest class lowered, given to Sectors not short of it.
  Moves keep each class's total, givers keep their own Surplus, and every shelf keeps at least one unit (scarce, not absent).
- Sector inspection on the Store overview (F4): the Inspect Sectors button, then a left click highlights the Sector under the
  cursor (floor ray hit, wrapped by `SectorAt`) and shows its coordinate, Surplus, Shortage, stock per class, Water points
  (showroom taps with how many run), Departments by type, Pillar, Landmark and Points of interest (`SectorInspector`, reads the
  snapshot only). Dev argument `--inspect=x,y`.

## Decisions

- Surplus: a Sector holds at least `SurplusRatio` (1.3) times the Store's mean per Sector of the class; Shortage: at most
  `ShortageRatio` (0.6) times it. Sectors are equal-sized, so this is density against the Store average.
- Targets per hectare (Food 1470, Drink 105, Comfort 1050, Supplies 1470) are the measured averages of the 08o stock, so the band
  step changes little; tune them in `StockConfig`.
- Only Display stock is reshaped: meals, fish, camp goods, Lost-and-found and left-behind finds keep their amounts.
- The summary is computed once at generation: stock is static until Villagers take it.
- The 08o test of raw configured amounts turns the guarantee off.
- No models: the panel is UI and the highlight a translucent plane, so no search was needed and nothing was generated.

## Tests

`SectorStockTests` (5 layouts × 3 seeds). Full suite: 672 passed, 0 failed. Godot headless build: no errors.
Windowed captures (`--overview --overview-distance=1800 --inspect=10,10` and `--inspect=-20,960`): A-1 and C-2 are highlighted
and their panels filled; the second click, 20 m west of the west Seam, picked the east column's C-2.

## Needs human review

The panel layout and the highlight colour; clicking by hand (the captures click programmatically through the same ray path).
