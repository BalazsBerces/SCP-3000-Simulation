# Ticket 17e: Storehouse, Furniture yard, Limbo and spoilage

Issue: #83. Spec: #1.

## Built

- **Spoilage.** Taken food spoils; fresh food (FreshFood, LiveFish, Meatballs, AlmondCake) at 0.5 units per unit per Store day, tinned and other food at 0.05. Food on a Store shelf does not spoil. Food put back on a shelf (`Scenario.PutBack`) is tracked per shelf and keeps spoiling. A `FoodSpoiled` event reports losses.
- **Storehouse.** A three-shelf Blueprint west of the Village centre; each built shelf holds 12 units (capacity 36). Builders build it after the Workshops and the Mess hall from ShelfUnit, Bookcase, PalletRack, GroceryShelf or Wardrobe pieces. Food in it spoils at the base rate.
- **Open pile.** Stores beyond capacity are derived, not stored: the Storehouse fills with food first (fast spoilers before slow), then other Goods, then Materials and Gear; the rest is the pile, where food spoils 3x faster.
- **Furniture yard.** Radius 2, six pieces. A Cautious Village under 8 people keeps it inside; any other keeps it 16 m east behind its own round barricade. Builders take pieces from the yard first and, when idle, gather loose pieces into it. Home-wall logic ignores Outpost and yard walls.
- **Rendering.** World labels and boxes for the Storehouse, the pile (existing CardboardBox model) and the yard; the Village panel has a Storage section with capacity and spoiling food.

## Decisions for this unattended run

- Rates, shelf capacity, yard size and the 3x pile factor are tuning values, so no ADR. Existing glossary terms (Storehouse, Furniture yard, Limbo) suffice.
- The pile is derived from stores and capacity, so only fractional carry (`Village.SpoilProgress`) and put-back shelf food need saving.
- Save format **11** adds the Storehouse, the yard and `SpoilingShelves`. Invalid references are rejected on load.

## Asset workflow

ADR 0006 followed: the pile reuses the existing CardboardBox model; the Storehouse and yard use existing shelf and chair models. Downloads: none. Generated items: **none**. Sources searched: none needed.

## Verification

Logs are under `.ticket-logs/17e-*.log`.

- `StorehouseTests` (10 facade cases): shelf food does not spoil; taken food spoils and fresh beats tinned; put-back food keeps spoiling; Storehouse food spoils slower than the pile; overflow goes to the pile; Cautious yard inside, Bold yard outside with its own barricade; yard-first building and gathering; save/load round-trip with identical continuation.
- `BlueprintTests` adjusted for the new Storehouse and yard (extra pieces; second-ring check uses `HomeWalls()`). `PersistenceTests` expects version 11.
- Full Release suite: **889 passed, 0 failed, 0 skipped** (5 m 18 s). Godot 4.7.2 headless `--build-solutions`: **passed**.
- Windowed Godot check (`--storage-check`): **passed**; asserts a built Storehouse, pile boxes and labels, yard label and the Village panel text. Screenshot inspected once: [1280 px](screenshots/17e/storage.png).

## Human review and limits

Check label legibility at distance (the labels are small in the capture), the pile and yard look in a populated Store, and spoilage balance over several days. Full-Store FPS and audio were not measured. Save format 10 and earlier cannot be loaded.

Suggested rule: Git Bash heredocs containing quotes or backslashes corrupt multi-line edits; write such scripts and files with the Write tool.
