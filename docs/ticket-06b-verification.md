# Ticket 06b: Switchable Pillar layout (3 × 3 or 2 × 2) with solid Pillars

Implemented issue #42 against spec #1, on top of the 3 × 3 grid of #41.

## Decisions made during the unattended run

- **`PillarLayout` enum** (`ThreeByThree`, `TwoByTwo`) on `WorldConfig` (default `ThreeByThree`) and
  on `WorldSnapshot`. `PillarLayouts` gives the short names `3x3`/`2x2` (used by the dev flags) and
  the grid size. The new config parameter comes last, so existing callers are unchanged.
- **"Saved and restored with the world state":** there is no save/load system yet. The layout is
  part of the world config and of every snapshot, so a future save carries it. A facade test checks
  that a world rebuilt from a snapshot's seed and layout has the same Pillars, Sectors and Furniture.
  ADR 0003 now lists the Pillar layout in the world state and says the layout, not the seed, fixes
  the Pillar grid.
- **2 × 2 Sectors** are 672 m squares from the Seam. They sit on the 16 m aisle grid but not on Chunk
  boundaries. The old "Store must be a multiple of 3 Chunks" check is gone; Sector size is the Store
  size divided by the Pillars per side. Boundary rule unchanged: a position on a boundary belongs to
  the Sector east or south of it.
- **New facade queries:** `NearestPillar(position)` (shortest wrapped distance, on the snapshot and
  the facade) and `WorldSnapshot.OutsidePillars(position)`. A position inside a Pillar footprint
  moves straight out through the nearest face to the middle of the walkway ring, 17.5 m from the
  centre. The camera rig uses it when a new world is shown. #8 can reuse both.
- **Layout switch:** the top-right panel gets the row `Pillars: [3×3] [2×2]` (toggle buttons in a
  group) under the seed controls. Choosing the other layout calls `SimulationHost.CreateWorld` with
  the current Store's seed (not the text in the seed box) and the new layout. Generate and Random keep
  the active layout. The Store clock restarts, as with Generate.
- **Solid Pillars:** the see-through disc, its uniforms and the dither are removed from
  `pillar.gdshader`. The upward fade and painted coordinates stay.
- **Overview:** the floor's Sector-line spacing now comes from each new world's snapshot (it was set
  once at start-up), so the F4 overview follows a layout switch.
- **Dev flags:** `--pillars=3x3|2x2` picks the start-up layout. `--choose-pillars=…` presses the
  panel button after one second, for unattended checks of the switch. `--pillar=<coordinate>` works
  in both layouts.
- **Performance (render-only, no visual change intended):** with no Pillar in front of the
  benchmark path, 2 × 2 renders more of the dense Store than 3 × 3, where B-2 fills much of the mid
  and far views. The first 2 × 2 runs missed 120 fps on the far view (114–117 fps; GPU-bound, about
  7.5 ms GPU, Furniture about 4.5 ms of it). Coarse Furniture meshes (drawn only beyond about 110 m)
  now drop all downward faces, the bottom caps of cylinders, and parts thinner than 1 cm (the tape
  strip on flat-pack boxes). That cuts about 20% of coarse triangles. MSAA 4x and TAA are unchanged.
- **Spec review follow-ups:**
  - the docs use "Ghost copy" for the repeated Pillars;
  - `PillarLayouts.Parse` replaces the two parse-or-throw copies;
  - `--choose-pillars` waits for a seed entered in the same run.
  The camera is kept out of Pillars only when a new world is shown, as the ticket asks. Panning
  into a Pillar stays possible, because the camera has no collision.
- No new ADR: everything here is reversible. The glossary adds **Pillar layout** and updates
  **Pillar**, **Sector** and the Chunk note.

## Automated verification

- `PillarLayoutTests` (new) plus the existing `PillarTests`/`SectorTests` (3 × 3) cover:
  - the default layout, the layout on the snapshot, and a rebuild from seed + layout giving the same
    Store, for both layouts;
  - 2 × 2 positions (336/1008 m), coordinates A-1, B-1, A-2, B-2, identical size and height, and no
    Pillar or ring touching a Seam; the seed never moves Pillars, and Departments are the same under
    both layouts;
  - 2 × 2 Sectors (672 m, centred on Pillars, on the aisle grid, covering the Store) and `SectorAt`
    inside Sectors, on boundaries and across Seams;
  - no Furniture in any footprint or walkway ring (3 × 3 seed 3008; 2 × 2 seeds 3008, −1, 42);
  - in 2 × 2, the east and west (and north and south) neighbour of A-1 is the same Pillar, 672 m away
    both ways; all pairwise wrapped distances are 672 m or 950 m;
  - on a 4 m grid over the whole Store (plus unwrapped margins), no point is farther from its nearest
    Pillar than 316.8 m (3 × 3) / 475.2 m (2 × 2), and these maxima are reached at the Sector corners;
  - `OutsidePillars` moves points inside a footprint just outside it, through the nearest face, and
    leaves other points alone, in both layouts.
- `dotnet test Scp3008.sln`: **132 passed**, 0 failed. `dotnet build game/Scp3008.Game.csproj`:
  0 warnings, 0 errors. Godot 4.7.2 .NET console `--headless --build-solutions --quit`: exit 0.

## Rendered verification (RTX 2070 Super; captures at 1600 × 900, benchmark at 1920 × 1080)

- Overview: [2 × 2](screenshots/06b/2x2-overview.png), [3 × 3](screenshots/06b/3x3-overview.png), and
  [3 × 3 switched to 2 × 2 from the panel](screenshots/06b/switch-to-2x2-overview.png): four Sectors,
  lines at 0/672 m, four truncated bases and labels; the panel marks `2×2`.
- Switch with the focus inside the future Pillar (`--focus=1008,1008 --choose-pillars=2x2`): the log
  shows "Camera focus moved out of Pillar B-2 to (1008.0, 1025.5)".
  [Screenshot](screenshots/06b/switch-pushout.png).
- Solid Pillars with the focus behind them: [2 × 2 A-1](screenshots/06b/2x2-solid-behind-a1.png),
  [3 × 3 B-2](screenshots/06b/3x3-solid-behind-b2.png): no hole. [A-1 close up](screenshots/06b/2x2-a1-close.png).
- Repetition: [2 × 2 at eye level](screenshots/06b/2x2-eye-level.png) shows A-1 and B-1 about 900 m
  ahead. [3 × 3 at eye level](screenshots/06b/3x3-eye-level.png) for comparison.
- Seams in 2 × 2, comparing views one Store length apart (weather cleared):
  [X](screenshots/06b/2x2-seam-x.png)/[ghost](screenshots/06b/2x2-seam-x-ghost.png) mean RGB difference
  **0.16/0.09/0.08**; [Y](screenshots/06b/2x2-seam-y.png)/[ghost](screenshots/06b/2x2-seam-y-ghost.png)
  **0.02/0.01/0.01** on a 0–255 scale.
- Benchmark (MSAA 4x + TAA on, 20 s per measurement), after the coarse-mesh change. The averages
  below are from runs with no other benchmark running. Three earlier runs overlapped with a stray
  benchmark process and dropped to about 70 fps in both layouts; they are not counted.

| Layout | Open avg / 1% low | Closing time avg / 1% low | Closing time far avg / 1% low | Result |
| --- | ---: | ---: | ---: | --- |
| 3 × 3 | 155.2 / 87.4 | 151.3 / 89.5 | 127.4 / 111.1 | PASSED |
| 2 × 2 (run 1) | 132.9 / 81.7 | 136.5 / 92.2 | 125.3 / 109.9 | PASSED |
| 2 × 2 (run 2) | 132.0 / 83.3 | 141.2 / 94.5 | 129.5 / 102.4 | PASSED |

  Benchmark screenshots: [3 × 3](screenshots/06b/benchmark-3x3/), [2 × 2](screenshots/06b/benchmark-2x2/).
  The far view still has only about 5–8% headroom over 120 fps on this machine, with other desktop
  apps (Discord, Firefox) running.

## Human visual review still needed

- Which layout feels better: Pillar density, the 2 × 2 repetition (the same Pillar seen in two
  directions) and how navigation feels in motion.
- Pillars as solid walls when the followed point is behind one, now that the see-through hole is gone.
- The panel row's look and the moment of regeneration when switching (the Store clock restarts).
- Coarse Furniture at middle and far zoom after the triangle cuts. They should not be visible, but
  check that shelves and racks look unchanged.
- Continuous panning across the Seams in 2 × 2.
