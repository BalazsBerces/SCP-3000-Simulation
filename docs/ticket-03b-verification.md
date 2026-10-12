# Ticket 03b verification

Implemented [issue #46](https://github.com/BalazsBerces/SCP-3000-Simulation/issues/46): 45 parameterised Layout archetypes across all eleven Department types; a strict, data-driven catalogue of 28 Themes; varied Room partitions and open-floor Displays; Placement roles, sold-out gaps, organic turns and offsets; coordinated Palettes; and three Variants for each of the ten requested Furniture types. Generation stays in the engine-independent core behind `Simulation`; Godot reads the snapshot to render each type/Variant with full/coarse instanced meshes and appropriate solid occluders.

## Decisions for this unattended run

- Review fixed point: `e21473e429313c2809f5b7a78bb5b54d03352f3b`. Substantial uncommitted #46 implementation and before/after captures existed at startup. They were preserved, completed and verified. Pre-existing changes to the two ticket-runner scripts are also preserved under the instruction to stage everything except `.claude/`.
- The existing catalogue has **61**, rather than 60, Furniture types (already documented in ticket 03a). Every existing type is retained and appears in at least one Theme.
- Each 16 m Department cell retains its own seed stream and 1.5 m outer margin, leaving the existing 3 m aisle grid clear. Optional cell merging is omitted. Rooms vary in number, extent and orientation; open-floor sets occupy uneven rectangles in separate quarters of a cell, with overlap checks and occasional empty quarters.
- Theme alternatives mean one of the listed types, rather than all alternatives. Mixed shelving picks the Department's own shelving; other mixed slots can pick several types. Core slots must meet their minimum counts before a Display is committed; an incomplete Display retries three times and then stays empty. A failing facade test demonstrated the previous one-Chair Dining-room defect before this was fixed.
- Counts scale with room size. Common and Accent odds are tuned by Theme; companion pieces can have high conditional odds (a lamp on an occasional desk), while their frequency across all Displays of that Theme remains within the tested Accent limit. A Department's own pieces are favoured, preserving its identity while mixing at least six foreign types.
- Floor overlap and Pillar clear-zone checks use each Variant's spec. Elevated pieces and floor coverings retain the existing exceptions. Matching chair/table sets share Variants and Palette tints; cabinets and aligned rows retain their alignment.
- Coarse meshes combine material parts in one surface, with a per-vertex part index and material tables, retaining colours, roughness, metallic and Lamp emission while reducing draw calls. Wrapped chunk images use rendering-server instances, cached visibility/transforms and shared per-frame creation/placement budgets to accommodate the additional Variant groups.
- No new ADR: these are reversible generation/content and rendering implementation choices within the existing architecture. New vocabulary is recorded in [GLOSSARY.md](../GLOSSARY.md). Spec-review was skipped as requested.

## Automated verification

- `dotnet test Scp3008.sln --verbosity minimal`: **195 passed, 0 failed, 0 skipped**. [Full log](screenshots/03b/core-tests.txt).
- Facade tests cover archetype shares, strict Themes and allowed Department weights, Core presence and minimum counts, Common/Accent coverage and rarity, foreign catalogue mixing and own-type majority, the old Department gaps, partitions, cell/room repetition under all quarter turns, loose rotation jitter, every Variant, Palettes, density, seed repeatability, floor overlaps and all Pillar clear zones. Existing generation and architecture tests pass.
- Godot **4.7.2 .NET** console executable: `--headless --path game --editor --build-solutions --quit` succeeds. [Build log](screenshots/03b/headless-build.txt).
- Benchmark seed 3008, `3x3`: **410,806 Furniture / 30,736 Lamps / 14,773 Displays**. Compared with 03a's 428,928 / 30,411, Furniture changes by **-4.2%**, Lamps by **+1.1%**. Lighting & electrical retains the highest Lamp density. [Rendered tour log](screenshots/03b/furniture-tour.txt).

## Reference-machine benchmark

RTX 2070 Super, Ryzen 7 3700X, 16 GB; Godot 4.7.2 Forward+, **1920 × 1080**, **MSAA 4x and TAA**, fixed 160 m Fog bank, 20 seconds per measurement, **256 real Lamp lights / 4 shadowed**. All three gates pass. [Final log](screenshots/03b/benchmark.txt).

| Measurement | Average fps | 1% low fps | Result |
| --- | ---: | ---: | --- |
| Open | 144.1 | 87.6 | PASS |
| Closing time | 141.5 | 93.3 | PASS |
| Closing time, far zoom | 124.3 | 99.3 | PASS |

Earlier measurements missed the open-view 1% low or the far-view average. The final build reduces the shared chunk-placement budget to 1 ms per frame and omits redundant cushion subdivisions, drawer fronts and intermediate ladder-back slats from coarse Variants, retaining their main silhouettes. Resolution, antialiasing, haze reach, light budget and benchmark gates remain the same. No core generation changes followed the passing 195-test suite.

[Open close](screenshots/03b/benchmark/open-close.png), [open mid](screenshots/03b/benchmark/open-mid.png), [open far](screenshots/03b/benchmark/open-far.png), [Closing time close](screenshots/03b/benchmark/closing-time-close.png), [Closing time mid](screenshots/03b/benchmark/closing-time-mid.png), [Closing time far](screenshots/03b/benchmark/closing-time-far.png).

## Captures and human review

The existing `before/` captures contain all eleven Department types; `after/` is refreshed from the final generation build. Both use seed 3008, `3x3`, paused noon, clear Weather, the largest Department of each type's anchor, distance 45 m and yaw 30 degrees, matching the 03a Department camera convention. There are also **45 archetype views** at 24 m and **30 Variant close-ups** at 6 m, plus the six benchmark views. The tour checks that every Variant has nonempty full and coarse meshes and reports its occluder vertices.

Reproduce the final tour with:

```text
<Godot 4.7.2 Mono console> --path game -- --pillars=3x3 --pause --furniture-tour=<absolute output directory>
```

| Department | Before | After |
| --- | --- | --- |
| Showrooms | [Before](screenshots/03b/before/Department-Showrooms.jpg) | [After](screenshots/03b/after/Department-Showrooms.jpg) |
| Bathrooms & kitchens | [Before](screenshots/03b/before/Department-BathroomsAndKitchens.jpg) | [After](screenshots/03b/after/Department-BathroomsAndKitchens.jpg) |
| Restaurant/food market | [Before](screenshots/03b/before/Department-RestaurantAndFoodMarket.jpg) | [After](screenshots/03b/after/Department-RestaurantAndFoodMarket.jpg) |
| Plant section | [Before](screenshots/03b/before/Department-PlantSection.jpg) | [After](screenshots/03b/after/Department-PlantSection.jpg) |
| Kids' area | [Before](screenshots/03b/before/Department-KidsArea.jpg) | [After](screenshots/03b/after/Department-KidsArea.jpg) |
| Lighting & electrical | [Before](screenshots/03b/before/Department-LightingAndElectrical.jpg) | [After](screenshots/03b/after/Department-LightingAndElectrical.jpg) |
| Self-serve warehouse | [Before](screenshots/03b/before/Department-SelfServeWarehouse.jpg) | [After](screenshots/03b/after/Department-SelfServeWarehouse.jpg) |
| Workspaces | [Before](screenshots/03b/before/Department-Workspaces.jpg) | [After](screenshots/03b/after/Department-Workspaces.jpg) |
| Textiles & Decor | [Before](screenshots/03b/before/Department-TextilesAndDecor.jpg) | [After](screenshots/03b/after/Department-TextilesAndDecor.jpg) |
| Outdoor | [Before](screenshots/03b/before/Department-Outdoor.jpg) | [After](screenshots/03b/after/Department-Outdoor.jpg) |
| Dining & Hallway | [Before](screenshots/03b/before/Department-DiningAndHallway.jpg) | [After](screenshots/03b/after/Department-DiningAndHallway.jpg) |

Representative images were inspected: the stamped workspace layout has become varied office islands/rooms; Outdoor contains distinct patios, lounger displays and stock rows; round tables have distinct silhouettes. Human review remains for aesthetic balance, Department/theme readability, natural placement, recognition of every Variant, and smooth full/coarse transitions, chunk streaming and Seams while moving the camera. Audio was not changed by this ticket.
