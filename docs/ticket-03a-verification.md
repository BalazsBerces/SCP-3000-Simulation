# Ticket 03a verification

Implemented all 30 Furniture types named in [issue #45](https://github.com/BalazsBerces/SCP-3000-Simulation/issues/45), and Workspaces, Textiles & Decor, Outdoor and Dining & Hallway. The core owns types, specs and generation; Godot reads snapshots to build full/coarse box-and-cylinder meshes, occluders and materials. DeskLamp and ArcLamp use the existing 05d light pools, real lights and halos. ArcLamp uses a rotated horizontal bulb offset so its light follows its shade.

## Decisions for this unattended run

- The existing catalog contained **31**, rather than 30, Furniture types. All were retained; the 30 requested additions make **61**.
- Every new Department has selection weight 1.0 and relative site size 1.1. Weighted site quotas replace independent type rolls to avoid large accidental density changes when expanding the catalog. The seed still determines the shuffled site types, site positions, site sizes and Furniture layout. As permitted by the ticket, existing seeds now produce different Stores.
- New layouts occupy existing Department cells, and new types in existing Departments replace layout slots. Lighting display rolls now devote 85% of their cells to Lamp displays (15% to shelves), preserving the benchmark's Lamp count despite the changed floor shares. No Furniture is added on top of old layouts.
- Added five CC0 Furniture materials (coated pine, teak, linen, painted metal and wool) and four CC0 floor materials from Poly Haven; source links are in [CREDITS.md](../game/assets/textures/CREDITS.md). Every material map/floor layer is 512 ? 512; the floor atlases have eleven layers. New Furniture textures use mipmaps; coarse materials omit normal mapping, legs/arms thinner than 9 cm, and use five-sided round parts. The first-wave coarse meshes retain their existing settings.
- The old benchmark focus (664,637) now lies in Showrooms. The benchmark focus is moved to (676,710), in the nearest Lighting & electrical display, retaining the same fly/zoom path, Fog bank, lighting conditions, duration, resolution and frame-rate gates.
- Occluders follow the first wave's rule: solid cabinet/appliance bodies and tall cabinet backs occlude; low, open and small/thin furnishings do not. Adding occluders to the narrow mirror/frame panels caused the Godot render startup to stall repeatedly on this machine; removing those panels restored rendering. Their full and coarse meshes remain visible.
- No ADR was warranted: these are reversible content/generation tuning choices within the established architecture.

## Automated verification

- Full core suite: **178 passed, 0 failed, 0 skipped** (`dotnet test tests/Scp3008.Core.Tests --no-restore`).
- Facade tests cover each new type in its specified Department(s), eleven distinct Department Furniture sets, new Department floor share across five seeds, seeded generation, no floor overlaps across three seeds, all Pillar walkway clearances/layouts, unchanged display Lamps through Closing time/reopening and highest Lamp density in Lighting & electrical.
- Godot **4.7.2 .NET** console: `--headless --editor --path game --build-solutions --quit` succeeds. Console executable found under the WinGet GodotEngine.GodotEngine.Mono package.
- The rendered Furniture tour confirms nonempty full and coarse meshes for all 30 additions; [tour log](screenshots/03a/furniture-tour.txt).
- Benchmark seed 3008, standard 3x3 Pillar layout: before **406,313 Furniture / 31,757 Lamps**; after **428,928 Furniture / 30,411 Lamps** (**+5.6% / ?4.2%**). New Department floor shares: Workspaces 6.2%, Textiles & Decor 5.6%, Outdoor 6.6%, Dining & Hallway 5.9%; combined **24.4%**.

## Rendered evidence and remaining human review

Captures are generated from the actual seed-3008 Store at 1080p with paused noon lighting and clear Weather. Close captures face the selected Furniture's front; Department captures show the larger arrangement. Reproduce with `--pillars=3x3 --pause --furniture-tour=<absolute directory>`.

Human review remains for aesthetic quality, material balance, recognition of each silhouette, layout variety, and smooth full/coarse transitions in motion, especially ArcLamp's stepped arm and the simplified appliance fronts. These are procedural meshes as requested, not imported models. Screenshots establish rendering but do not replace that judgement.

| New Department | Capture |
| --- | --- |
| Workspaces | [Capture](screenshots/03a/furniture/Department-Workspaces.png) |
| Textiles & Decor | [Capture](screenshots/03a/furniture/Department-TextilesAndDecor.png) |
| Outdoor | [Capture](screenshots/03a/furniture/Department-Outdoor.png) |
| Dining & Hallway | [Capture](screenshots/03a/furniture/Department-DiningAndHallway.png) |

All 30 new Furniture captures:

- [Desk](screenshots/03a/furniture/Desk.png)
- [OfficeChair](screenshots/03a/furniture/OfficeChair.png)
- [FilingCabinet](screenshots/03a/furniture/FilingCabinet.png)
- [MeetingTable](screenshots/03a/furniture/MeetingTable.png)
- [DeskLamp](screenshots/03a/furniture/DeskLamp.png)
- [CushionBin](screenshots/03a/furniture/CushionBin.png)
- [BlanketStack](screenshots/03a/furniture/BlanketStack.png)
- [CurtainRack](screenshots/03a/furniture/CurtainRack.png)
- [FloorMirror](screenshots/03a/furniture/FloorMirror.png)
- [FrameDisplay](screenshots/03a/furniture/FrameDisplay.png)
- [GardenTable](screenshots/03a/furniture/GardenTable.png)
- [GardenChair](screenshots/03a/furniture/GardenChair.png)
- [SunLounger](screenshots/03a/furniture/SunLounger.png)
- [Parasol](screenshots/03a/furniture/Parasol.png)
- [Grill](screenshots/03a/furniture/Grill.png)
- [DisplayCabinet](screenshots/03a/furniture/DisplayCabinet.png)
- [Sideboard](screenshots/03a/furniture/Sideboard.png)
- [ShoeCabinet](screenshots/03a/furniture/ShoeCabinet.png)
- [CoatRack](screenshots/03a/furniture/CoatRack.png)
- [HallBench](screenshots/03a/furniture/HallBench.png)
- [ArcLamp](screenshots/03a/furniture/ArcLamp.png)
- [TvBench](screenshots/03a/furniture/TvBench.png)
- [Dresser](screenshots/03a/furniture/Dresser.png)
- [Nightstand](screenshots/03a/furniture/Nightstand.png)
- [WashingMachine](screenshots/03a/furniture/WashingMachine.png)
- [Stove](screenshots/03a/furniture/Stove.png)
- [BoxCart](screenshots/03a/furniture/BoxCart.png)
- [BarStool](screenshots/03a/furniture/BarStool.png)
- [PlanterBox](screenshots/03a/furniture/PlanterBox.png)
- [ToyChest](screenshots/03a/furniture/ToyChest.png)

## Reference-machine benchmark

RTX 2070 Super, Ryzen 7 3700X, 16 GB RAM; Godot 4.7.2 Forward+, **1920 ? 1080**, **MSAA 4x and TAA**, 20 seconds per measurement, fixed 160 m Fog bank. Budget remained **256 real Lamp lights / 4 shadowed**. [Full log](screenshots/03a/benchmark.txt).

| Measurement | Average fps | 1% low fps | Result |
| --- | ---: | ---: | --- |
| Open | 162.0 | 76.3 | PASS |
| Closing time | 176.1 | 99.6 | PASS |
| Closing time, far zoom | 137.1 | 105.3 | PASS |

The initial far-zoom measurement missed 120 fps. Removing thin parts from the new coarse meshes and enabling mipmaps on their new materials restored the required performance; no resolution, antialiasing, light budget or benchmark gates were reduced.

[Open close](screenshots/03a/benchmark/open-close.png), [open mid](screenshots/03a/benchmark/open-mid.png), [open far](screenshots/03a/benchmark/open-far.png), [Closing time close](screenshots/03a/benchmark/closing-time-close.png), [Closing time mid](screenshots/03a/benchmark/closing-time-mid.png), [Closing time far](screenshots/03a/benchmark/closing-time-far.png).

An external edit to `scripts/run-tickets.ps1` appeared during this run (fail when Claude exits unsuccessfully). It was preserved and staged under the instruction to stage everything except `.claude/`; no other ticket or the parent spec was changed.
