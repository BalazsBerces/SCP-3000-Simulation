# Ticket 03c verification

Implemented [issue #47](https://github.com/BalazsBerces/SCP-3000-Simulation/issues/47). Seeded Store generation now reserves a small share of the floor for Lighting & electrical, retains scattered Lamps in themed Displays, and favours Armchairs, Sofas and tables. The engine-independent core remains behind `Simulation`; Godot renders snapshots using the existing instancing and lighting.

## Decisions for this unattended run

- Review fixed point: `7609e409964fc678161b145e1db46a6ef3c3d8d3`, on `main`. Implement and TDD skills followed at the pre-agreed facade seam. Spec-review skipped under the user's stage-boundary review policy.
- Lighting & electrical has fewer generating sites, separated by at least 240 m. Its cells are ranked by the ratio of distance to the nearest weighted electrical site and nearest weighted ordinary site. Reserving the first 2.5% is equivalent to calibrating a shared electrical Voronoi size multiplier. This retains seeded positions, relative sizes and irregular connected regions while preventing large seed-dependent swings in rare floor coverage. Existing sliver absorption remains. Both Store sizes retain 16 m cells; larger Stores have more sites in proportion to floor area.
- Released Department share favours Showrooms and Dining & Hallway. Showrooms favour Living-room and Dining-room Themes; Workspaces include more Meeting-room and Office-lounge Displays. Living rooms centre on Coffee tables, with Sofas and paired Armchairs, optional tables beside reading seats, and TV benches at walls. The main seating group only anchors on a centre table, so a side table does not recursively spawn another seating group.
- Bedroom, Living-room, Dining-room and office Lamps have reduced opportunities. Electrical Lamp-display content remains Lamp-rich. Tables and seats are placed through whole Themes and their normal floor Placement roles, never substituted at an elevated Lamp position. Garden-stock plant Accent odds were reduced slightly to retain the existing rarity bound after the Department redistribution.
- The old Lamp-count preservation assertions are replaced with the 3-4% piece share and the 410,806-piece density baseline; neither test was deleted. No catalogue or Department type is removed. Lamp radii, Closing-time behaviour, light budgets, Staff rules and camera-dependent lighting are unchanged.
- `--clear-weather` is a dev capture option using the existing scenario facade, allowing matching paused captures with thin haze and no Fog banks. It does nothing unless explicitly requested.
- No Goods economy, disassembly yields, Village power or construction mechanics are added. No new domain term or terminology rule is settled, so the glossary is unchanged. No ADR is needed for these reversible generation and content choices.

## Measurements through the facade

Every world is created with `Simulation.Create`, fixed runtime RNG seed 1, advanced by one simulated second and read through its snapshot. Lamp share is **piece count**, identified by `FurnitureCatalog.Spec(piece).IsLamp`. Electrical share is **Department area divided by total Store floor area**, including Pillar clear zones; it is not illuminated area or furnished footprint. Full counts, per-type counts, per-type occupied floor footprints and Lamp densities are in [raw measurements](screenshots/03c/measurements.jsonl); [CSV summary](screenshots/03c/measurements.csv).

| Seed | Pillar layout | Furniture | Lamps | Lamp pieces | Electrical floor | Electrical regions |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 3008 | 3x3 | 395175 | 14617 | 3.6989% | 2.4943% | 2 |
| 3008 | 2x2 | 395553 | 14664 | 3.7072% | 2.4943% | 2 |
| 3008 | 3x3L | 821631 | 27967 | 3.4038% | 2.5000% | 3 |
| 3008 | 3x3L-corners | 823492 | 28183 | 3.4224% | 2.5000% | 3 |
| 3008 | 3x3L-diamond | 823083 | 27995 | 3.4012% | 2.5000% | 3 |
| 42 | 3x3 | 402473 | 14432 | 3.5858% | 2.4943% | 2 |
| 42 | 2x2 | 403366 | 14529 | 3.6019% | 2.4943% | 2 |
| 42 | 3x3L | 839305 | 27443 | 3.2697% | 2.5000% | 3 |
| 42 | 3x3L-corners | 840542 | 27478 | 3.2691% | 2.5000% | 3 |
| 42 | 3x3L-diamond | 841519 | 27462 | 3.2634% | 2.5000% | 3 |
| 1 | 3x3 | 394067 | 14374 | 3.6476% | 2.4943% | 2 |
| 1 | 2x2 | 395299 | 14406 | 3.6443% | 2.4943% | 2 |
| 1 | 3x3L | 823392 | 28041 | 3.4055% | 2.5000% | 3 |
| 1 | 3x3L-corners | 824563 | 28060 | 3.4030% | 2.5000% | 3 |
| 1 | 3x3L-diamond | 825466 | 28146 | 3.4097% | 2.5000% | 3 |
| -1 | 3x3 | 397034 | 12011 | 3.0252% | 2.4943% | 2 |
| -1 | 2x2 | 397986 | 12036 | 3.0242% | 2.4943% | 2 |
| -1 | 3x3L | 815581 | 29461 | 3.6123% | 2.5000% | 3 |
| -1 | 3x3L-corners | 816841 | 29587 | 3.6221% | 2.5000% | 3 |
| -1 | 3x3L-diamond | 817303 | 29469 | 3.6056% | 2.5000% | 3 |
| 2026 | 3x3 | 405806 | 15630 | 3.8516% | 2.4943% | 2 |
| 2026 | 2x2 | 406769 | 15661 | 3.8501% | 2.4943% | 2 |
| 2026 | 3x3L | 819899 | 26546 | 3.2377% | 2.5000% | 2 |
| 2026 | 3x3L-corners | 821772 | 26745 | 3.2546% | 2.5000% | 2 |
| 2026 | 3x3L-diamond | 821739 | 26969 | 3.2819% | 2.5000% | 2 |

All 25 worlds meet both shares, retain all four Lamp types and have Lamps outside electrical Departments. Lighting & electrical has the highest Lamp density per square metre in every world; the facade tests check this separately from its floor share. Regions are connected Departments, rather than generating-site counts.

## Before/after availability: seed 3008 / 3x3

[Baseline measurements](screenshots/03c/baseline.jsonl) come from the fixed-point core assembly. All named table types are included, including the electrical Display tables that became rarer.

| Furniture | Before | After | Change |
| --- | ---: | ---: | ---: |
| All Furniture | 410,806 | 395,175 | -3.80% |
| Lamps | 30,736 | 14,617 | -52.44% |
| Armchairs | 3,240 | 7,029 | +116.94% |
| Sofas | 2,632 | 3,974 | +50.99% |
| Tables, combined | 17,915 | 20,431 | +14.04% |
| Coffee tables | 1,226 | 6,613 | +439.40% |
| Dining tables | 4,121 | 5,054 | +22.64% |
| Meeting tables | 156 | 235 | +50.64% |
| Garden tables | 3,063 | 2,690 | -12.18% |
| Kids' tables | 1,183 | 1,000 | -15.47% |
| Display tables | 4,650 | 1,624 | -65.08% |
| Plant tables | 3,516 | 3,215 | -8.56% |

Armchairs, Sofas and tables account for **52.09% of all positive increases in occupied floor footprint** (10,879 of 20,886 square metres). This measure uses each actual Variant's width and depth, sums only floor-standing pieces excluding floor coverings, and compares gains per type against the baseline. It demonstrates the main replacement emphasis without forcing larger Furniture into one-for-one small-Lamp replacements. Lamp piece share changes from 7.48% to 3.70%; electrical floor share changes from 7.17% to 2.49%.

## Captures

All captures use seed 3008, 1920 x 1080, the same camera position and heading before/after, distance 45 m, paused Store time 12:00 or 23:00, clear Weather with thin haze. The before captures use the fixed-point core with the final capture helper; after captures use the final core. Each chosen cell belongs to the labelled Department type in both generated Stores, allowing comparison even though the seed's Department regions change. Normal Lamps retain their pools and halos at Closing time.

| Pillar layout | Department | Focus (m) | Open before / after | Closing time before / after |
| --- | --- | --- | --- | --- |
| 3x3 | LightingAndElectrical | 408, 936 | [Before](screenshots/03c/before/3x3-LightingAndElectrical-open.png) / [After](screenshots/03c/after/3x3-LightingAndElectrical-open.png) | [Before](screenshots/03c/before/3x3-LightingAndElectrical-closing.png) / [After](screenshots/03c/after/3x3-LightingAndElectrical-closing.png) |
| 3x3 | Showrooms | 232, 888 | [Before](screenshots/03c/before/3x3-Showrooms-open.png) / [After](screenshots/03c/after/3x3-Showrooms-open.png) | [Before](screenshots/03c/before/3x3-Showrooms-closing.png) / [After](screenshots/03c/after/3x3-Showrooms-closing.png) |
| 3x3 | Workspaces | 1272, 1208 | [Before](screenshots/03c/before/3x3-Workspaces-open.png) / [After](screenshots/03c/after/3x3-Workspaces-open.png) | [Before](screenshots/03c/before/3x3-Workspaces-closing.png) / [After](screenshots/03c/after/3x3-Workspaces-closing.png) |
| 3x3L-diamond | LightingAndElectrical | 456, 552 | [Before](screenshots/03c/before/3x3L-diamond-LightingAndElectrical-open.png) / [After](screenshots/03c/after/3x3L-diamond-LightingAndElectrical-open.png) | [Before](screenshots/03c/before/3x3L-diamond-LightingAndElectrical-closing.png) / [After](screenshots/03c/after/3x3L-diamond-LightingAndElectrical-closing.png) |
| 3x3L-diamond | Showrooms | 1816, 1880 | [Before](screenshots/03c/before/3x3L-diamond-Showrooms-open.png) / [After](screenshots/03c/after/3x3L-diamond-Showrooms-open.png) | [Before](screenshots/03c/before/3x3L-diamond-Showrooms-closing.png) / [After](screenshots/03c/after/3x3L-diamond-Showrooms-closing.png) |
| 3x3L-diamond | Workspaces | 1672, 120 | [Before](screenshots/03c/before/3x3L-diamond-Workspaces-open.png) / [After](screenshots/03c/after/3x3L-diamond-Workspaces-open.png) | [Before](screenshots/03c/before/3x3L-diamond-Workspaces-closing.png) / [After](screenshots/03c/after/3x3L-diamond-Workspaces-closing.png) |

## Automated verification

- `dotnet test Scp3008.sln --verbosity minimal`: **246 passed, 0 failed, 0 skipped**. [Core test log](screenshots/03c/core-tests.txt). The 51 added facade cases cover the two separate share requirements across all 25 worlds and the benchmark's increased seat/table availability. Existing determinism, strict Themes, catalogue coverage, Layout archetypes, Room partitions, Variants, anchor/support validity, free placement, floor overlap prevention, Pillar clear zones, architecture and lighting/event tests pass.
- TDD evidence: the electrical share cases failed against the baseline's 7-10% coverage; the Lamp share cases then failed after reducing electrical floor alone (roughly 4.8-6%). The availability test also failed when electrical Display-table losses outweighed the initial table gains. Those failures drove the quota, reduced non-electrical Lamp opportunities and additional floor table placements.
- Godot **4.7.2 .NET**, found under `%LOCALAPPDATA%/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono*/`, console executable: `--headless --path game --editor --build-solutions --quit` succeeds. [Headless build log](screenshots/03c/headless-build.txt).
- All 24 matching game captures completed with no stderr errors. [Before capture log](screenshots/03c/before-captures.txt), [after capture log](screenshots/03c/after-captures.txt). Final Godot core assembly hash matches the tested core after restoring it from the baseline capture run.

To repeat one of the final captures, use its focus from the table:

```text
<Godot Mono console> --path game -- --seed=3008 --pillars=3x3L-diamond --pause --clear-weather --focus=1816,1880 --distance=45 --start-time=23:00 --capture-after=3 --capture=<absolute PNG path>
```

Representative images were inspected: Showrooms retain arranged seating with more tables and fewer Lamp points; Workspaces retain desk islands, cabinets, meeting tables and office lounges with occasional Desk lamps; electrical Displays remain bright and Lamp-rich at Closing time. The diamond Showroom Closing-time view shows darker stretches between the remaining pools. Human stage review remains for the aesthetic balance of those dark stretches, recognisable Department identities, the perceived rarity and scale of electrical regions, natural seating/table arrangements, and movement through full/coarse instancing, Chunk streaming, Fog banks and Seams. Audio and its subjective review are unchanged by this ticket.

## Reference-machine benchmark

RTX 2070 Super, Ryzen 7 3700X, 16 GB; Godot 4.7.2 Forward+, **1920 x 1080**, **MSAA 4x and TAA**, the existing unchanged `Benchmark.tscn` and camera path on seed 3008 / `3x3`, fixed 160 m Fog bank, 20 seconds per measurement, up to **256 real Lamp lights / 4 shadowed**. The final isolated run passes every gate. [Final benchmark log](screenshots/03c/benchmark.txt).

| Measurement | Average fps | 1% low fps | Result |
| --- | ---: | ---: | --- |
| Open | 199.3 | 82.2 | PASS |
| Closing time | 196.1 | 83.0 | PASS |
| Closing time, far zoom | 137.7 | 97.4 | PASS |

The initial run saved the six benchmark PNGs between measurement phases and passed all averages, but missed the Closing-time 1% low at 68.4 fps. It is retained in the [capture-enabled benchmark log](screenshots/03c/benchmark-with-captures.txt). The final run uses the same scene, settings, warm-up, measurement duration and gates, with no image saves between phases. No renderer or benchmark code, budgets, camera path, light radii or antialiasing settings were changed to obtain the pass. Capture/GC/background stalls are a possible explanation for the initial low, not a proven diagnosis; the isolated passing results and the first run are both recorded for stage review.

[Open close](screenshots/03c/benchmark/open-close.png), [open mid](screenshots/03c/benchmark/open-mid.png), [open far](screenshots/03c/benchmark/open-far.png), [Closing time close](screenshots/03c/benchmark/closing-time-close.png), [Closing time mid](screenshots/03c/benchmark/closing-time-mid.png), [Closing time far](screenshots/03c/benchmark/closing-time-far.png).

Reproduce the final isolated benchmark with:

```text
<Godot Mono console> --path game res://Benchmark.tscn -- --benchmark-seconds=20
```
