# Ticket 11: Restock

Issue: #12. Spec: #1. Review fixed point: `94d1d876ceb31beb4105adfc66a532c44b838c27`.

## Scope and decisions

- The engine-independent simulation facade owns Restock; Godot reads changed snapshots. Tests use `Simulation.Create`, `Scenario`, time advancement and snapshots. No new assets, dependencies, domain terminology or irreversible architecture decisions were needed.
- `WorldConfig.Restock` exposes `GoodsPerStoreHour` (default 0.125), `FurnitureHours` (24) and `LandmarkFurnitureHours` (72). Zero disables the corresponding replenishment; negative and non-finite values are rejected. Per-kind rates, Dry spells and balance tuning remain in #85.
- Goods refill in whole-unit pulses at the configured rate, without banking unused units at full shelves. Each holder refills to its generated amount. Initially bare Stock Furniture gets a replenishment plan from the existing stock generator's catalogue, without the initial stocking-share or Sector balancing limits. Display Furniture and left-behind finds never replenish. Scenario stock amounts can raise the replenishment amount for a kind.
- Furniture remembers its original placement, appearance and membership in a Display, Landmark or Point of interest. Removed pieces can return after their delay, while moved pieces leave a new copy at the original spot with a unique id. Moved Furniture receives no Store refill or running Water point. Original generated overlaps (boxes on racks, chairs under tables) are permitted; displaced Furniture and Villagers block occupied spots. Navigation, Water points and stock metadata follow returned Furniture.
- Until shared Villager sight rules exist, Restock uses conservative omnidirectional sight within 40 m from a 1.6 m eye height, sight lines to the eight placement-box corners, rotated Furniture boxes and Pillars as blockers, and metre-spaced fog samples with density at least 0.6 blocking sight. All distances and sight lines wrap across Seams. A ready spot waits until no living Villager can see it; the Observer camera never blocks Restock. These reversible choices are recorded here rather than in an ADR.
- Shelf Goods and live fish follow stock changes. Furniture, Lamp data and the overview refresh on layout changes. Furniture rendering currently rebuilds its instance groups on a changed layout, following the existing rebuild path; incremental Chunk updates can replace this if bulk hauling makes it costly. Existing baked Cafeteria food models retain their existing presentation.

## Automated checks

- Red/green runs first demonstrated missing refill and missing Furniture return. A separate failing regression caught rack boxes being blocked by their original arrangement.
- `RestockTests`: 21 passing cases cover configured rates and caps, disabled rates, invalid configuration, observed/unobserved return, fog across a Seam, tall versus low blockers, occupied spots, moved copies and id uniqueness, navigation rebuilding, Water point restoration, slower Landmark return and original Landmark arrangements, named Point of interest refills, and initially bare shelves versus empty showroom fridges and cabinets.
- The focused Restock/Needs/navigation integration run passed 47 cases before the additional Restock regressions. Existing `NeedsTests.Display_Fridges_are_empty_except_for_actual_left_behind_finds` also checks an eaten left-behind find stays empty over 20 Store hours.
- Final full suite: `dotnet test tests/Scp3008.Core.Tests -c Release` passed **792 / 792**, with zero failures or skips, in 6 min 48 s. It ran once, immediately before committing. `dotnet build Scp3008.sln -c Release` succeeded with **0 warnings and 0 errors**, and Godot 4.7.2 .NET `--headless --path game --build-solutions --quit` exited **0**. Full output stays under `.ticket-logs/11-full-suite.log`, `11-final-build.log`, `11-final-godot-build.log` and `11-final-godot-build-errors.log`. The separately requested `/spec-review` step was skipped as instructed.
- The 20-second-per-view full Store benchmark passed at 1920×1080 with MSAA 4× and TAA on the RTX 2070 Super (exit 0). Average / 1% low fps: Open **135.1 / 77.3**, Closing time **159.0 / 89.6**, Closing time far **129.8 / 99.9**, Closing time at the LOD switch **112.0 / 85.7**. Every view passes ADR 0004's 90 / 60 gate; the LOD-switch view misses the 120 fps average target. Logs: `.ticket-logs/11-benchmark.log` and `11-benchmark-errors.log`. No benchmark screenshots were requested or captured.

## Rendered check

Run the Godot console executable with a 300-second time limit:

```text
--path game -- --villager-sandbox --sandbox-preset=empty --pause --restock-check=<absolute-directory>
```

The check depletes a shelf and removes an Armchair. It checks one Goods unit after eight Store hours, three units and the returned Armchair after 24 hours, and changed scene pixels outside the HUD. It saves two screenshots at no more than 1280 pixels wide and a report, then exits. `.ticket-logs/11-rendered-fixed/report.txt` passed with exit code 0; both images were inspected once: the first shows the bare shelf and no Armchair, the second shows products on the shelf and the returned Armchair. The initial capture attempt aimed away from the Furniture and only detected HUD changes; the corrected check waits for scene readiness and excludes the HUD from its comparison.

## Human review

Review Restock pacing and the visibility approximation in crowded Displays, fog and Landmark arrangements at the stage-end visual review. The sandbox check demonstrates visible shelf refill and Furniture return; it does not judge every Point of interest's artistic appearance or long-run economy balance. No models, props or textures were generated or downloaded for this ticket.

Suggested rule: DevCapture can run before CameraRig._Ready resets the camera; wait for a drawn frame before positioning a scripted capture. Compare scene pixels outside the HUD so clock and counter changes cannot make a rendered check pass.
