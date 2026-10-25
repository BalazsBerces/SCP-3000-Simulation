# Ticket 08r verification: Shelves visibly fill and empty

## What was built

- Core: `WorldSnapshot.FillStep(goods)` (1 a few units .. `FillSteps` = 3 full, at `StockConfig.ShelfCapacity` = 30 or more; a kind
  with none left has no entry and looks bare). `ScenarioBuilder.SetStock` sets the amount of a Goods kind on Stock Furniture (single
  or many), refreshes the Sector stock and the snapshot; Display Furniture is refused.
  Fix found on the way: removing Furniture left `SectorStocks` stale, and the Sector summary indexed Furniture by list position
  instead of id.
- Game: `ShelfGoods` draws copies of each Goods kind's product model along the shelves of grocery shelves, shelf units, pallet racks,
  toy shelves, cushion bins and chest freezers, one instance group per Furniture type, kind and step. A lower step leaves
  gaps in a fixed shuffled order. Display Furniture, fridges and counters draw none. Goods show within 50 m, cast no shadow.
  The chest freezer became an open tub under a clear lid so its Goods show.
- Dev tools: `--shelf-stock=amount` (DevCapture), `SimulationHost.SetShelfStock`; the benchmark stocks every shelf to capacity.

## Models (ADR 0006, model workflow)

Nothing generated: every product is downloaded (`scripts/fetch-models.py`, credited in `game/CREDITS.md`, "Goods models"):
tinned food, fresh food (Poly Haven Long Life Food), dry food (Russian Food Cans), bottled water (Plastic Bottle Gallon), soft
drinks, coffee, sweets, candles, batteries, cables (Sketchfab CC-BY), bulbs, tape (Poly Haven). No real store brand. Candidates were picked per item from the ADR 0006 sources in order (Poly Haven, then Sketchfab); the picks are in
`MODELS` in `scripts/fetch-models.py`. The only generated geometry is the chest freezer's open tub, an existing procedural Furniture
type changed (not a Goods item), so no search applies.

## Tests

`ShelfFillTests` (4). Full core suite in Release: 676 passed, 0 failed. Godot headless build: no errors. `--model-check`: 60 models, 0 checks failed (Furniture models only; Goods are not Furniture and
are wired in `ShelfGoods.Products`). Texture import check (`grep -L mipmaps/generate=true`) on the 12 Goods folders: prints nothing.
Captures (`--shelf-stock=30/12/3/0 --distance=5`, same grocery shelf): full, two-thirds, sparse and bare rows follow the amount.
Benchmark (8 s per lighting, 1920x1080, every shelf stocked): Open 124.6 fps, Closing time 149.3, far 117.4, LOD switch 113.7;
PASSED (minimum 90; 1% lows 72.7, 79.6, 103.1 and 69.2, all over the 60 floor). Without Goods (`NOGOODS=1`): 124.9, 147.6, 118.1, 112.8, so the Goods cost nothing measurable; the two
views under 120 are the existing baseline.

## Needs human review

How each product reads on its shelf (shape, colour, scale) in the editor view and at close range; freezer look; whether 117 and 114
fps in the two far views, already under 120 before this ticket, are acceptable.
