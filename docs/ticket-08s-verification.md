# Ticket 08s verification: Performance pass of stage 08

Machine: RTX 2070 Super, 1920 × 1080, MSAA 4×, TAA, Store 3008. Gate (ADR 0004): 90 fps average and 60 fps 1% low in every
lighting; target 120 fps. Full logs of every run below were kept under `.ticket-logs/08s-*.txt` (not committed).

## What was changed

- **Fix: the game assemblies are built optimised.** Godot builds and runs the Debug configuration, so the simulation core and the
  game scripts ran unoptimised. Store generation took 8.2 s in the game against 1.4–2.5 s for the same `Simulation.Create` in a Release
  console. `game/Scp3008.Game.csproj` now sets `<Optimize>true</Optimize>` and references the core with
  `SetConfiguration="Configuration=Release"`. Debugging in the editor still works, but locals in the core may be optimised away.
- **Profiling aids kept:** the benchmark prints where on the camera path its slowest 1% of frames fall (path progress in tenths,
  with the worst frame time). `SimulationHost` logs the Store generation time and the time from engine start to the first drawn frame.

## Benchmark, every shelf stocked (3 consecutive 20 s runs each)

Average / 1% low in fps.

| Run | Open | Closing time | Closing time far | At LOD switch | Result |
|---|---|---|---|---|---|
| before 1 | 124.2 / 62.7 | 150.8 / 79.3 | 119.6 / 89.6 | 108.5 / 77.9 | PASSED |
| before 2 | 119.8 / 63.2 | 149.5 / 76.8 | 120.8 / 93.0 | 99.7 / 71.5 | PASSED |
| before 3 | 119.8 / 61.7 | 151.2 / 78.9 | 112.3 / 90.7 | 112.2 / 89.4 | PASSED |
| after 1 | 122.7 / 67.8 | 151.8 / 75.8 | 117.4 / 94.4 | 97.2 / 74.1 | PASSED |
| after 2 | 120.1 / 61.8 | 151.2 / 77.4 | 111.4 / 81.4 | 99.3 / 78.3 | PASSED |
| after 3 | 118.6 / 64.0 | 157.3 / 74.0 | 118.0 / 91.2 | 98.0 / 75.4 | PASSED |

All 3 runs pass after the fix. The frame rate is unchanged within the noise of about ±5 fps: the fix is on the load side, and the
frame is GPU-bound. The average GPU time is 7.2 ms in Open, 5.2 ms at Closing time, 7.6 ms far and 9.2 ms at the LOD switch, against
about 3 ms of CPU render time.

## GPU breakdown per pass (ms per frame, passes of 0.1 ms or more)

From separate `--benchmark-passes` runs with `--gpu-profile`. The profiler costs 1% lows of 33–40 fps itself, so those runs fail the
gate and are used only for the breakdown.

| Lighting | | Opaque | Depth pre-pass | Shadows | TAA | Glow | Resolve | Tonemap | SSAO | Other ≥ 0.1 |
|---|---|---|---|---|---|---|---|---|---|---|
| Open | before | 3.94 | 1.16 | 0.70 | 0.45 | 0.45 | 0.19 | 0.17 | 0.16 | MSAA depth resolve 0.12 |
| | after | 3.76 | 1.14 | 0.66 | 0.44 | 0.42 | 0.18 | 0.16 | 0.16 | MSAA depth resolve 0.12 |
| Closing time | before | 3.18 | 0.35 | 0.46 | 0.47 | 0.37 | 0.17 | 0.16 | – | |
| | after | 3.10 | 0.37 | 0.45 | 0.46 | 0.37 | 0.17 | 0.17 | – | |
| Closing time far | before | 5.36 | 0.76 | – | 0.47 | 0.36 | 0.19 | 0.12 | – | |
| | after | 5.57 | 0.76 | – | 0.48 | 0.40 | 0.25 | 0.16 | – | |
| At LOD switch | before | 5.85 | 1.03 | – | 0.40 | 0.36 | 0.18 | 0.12 | – | cluster bake 0.12 |
| | after | 6.44 | 0.97 | – | 0.39 | 0.49 | 0.19 | 0.18 | – | cluster bake 0.19 |

The opaque pass is half to two thirds of every frame. The differences between before and after are run-to-run noise: the fix
changes no rendering.

## Load time to the first frame (game, DiamondLarge Store, 861,028 pieces)

The model cache stamp includes the assembly id, so the first run after any build is cold: every Furniture model is rebuilt into
`user://model_cache`. The warm run is the second run after the same build.

| | Before cold | Before warm | After cold | After warm |
|---|---|---|---|---|
| Store generated | 8.2 s | 8.3 s | 2.5 s | 2.3 s |
| Furniture placed (with model builds) | 9.8 s | 3.8 s | 9.1 s | 3.6 s |
| **First frame drawn** | **27.1 s** | **20.6 s** | **18.3 s** | **12.1 s** |

Benchmark scene (3 × 3 Store, 413,481 pieces): first frame 21.9 s cold before, 16.3 s cold and 10.5 s warm after.

## Slowdowns found

| Slowdown | Cause (confirmed by) | Done |
|---|---|---|
| Load: Store generation 8 s | Unoptimised Debug core (Release console A/B: 1.4–2.5 s; after the fix 2.3–2.5 s in the game) | **Fixed** |
| Load: ~3 s of node `_Ready`s between StoreFloor and CameraRig | Same, unoptimised game scripts (temporary `_Ready` timestamps: 2.9 s before, 0.7 s after) | **Fixed** by the same change |
| Load: cold model cache, +5.5 s | Every build changes the assembly id in the cache stamp, so every Furniture model is rebuilt | Left: only the first run after a build pays it, and a player runs one build. A source hash stamp would risk a stale cache whenever a model's build code changes. |
| Load: ~3.5 s from the last `_Ready` to the first frame | Shader and pipeline compilation for the first view | Left: it is the engine's own work; the Godot shader cache already holds it, and the time is the same cold and warm. |
| Frame: GPU-bound opaque pass (3–6.4 ms) | Instanced Furniture and Goods, 1,300–2,300 draw calls, 2.5–4.1 M primitives | Left: it is within the gate. Goods cost 0.4–0.5 ms in Open (A/B with the Goods hidden: 130.5 against about 120 fps). Fewer would mean sparser shelves, which 08r settled. |
| Frame: LOD switch view at 97–112 fps, below the 120 target | Full Furniture out to the switch zoom: the most primitives of any view (4.1 M) | Left: it passes the 90 fps gate. Moving the switch closer is a visible trade the LOD tickets settled. |
| Frame: Open depth pre-pass 1.1 ms | SSAO in the Open lighting needs the full depth pre-pass | Left: SSAO is part of the Open look. |
| 1% lows: slowest frames during the pull-out (path progress 0.4–0.6, up to 15–20 ms) | Chunks entering view: placement of wrapped images and first draws | Left: the 1% low stays above 60 in all 6 runs (lowest 61.7). The game hitch report (`--hitch-report`, 1894 frames) found 1 hitch, a 22 ms GC. |
| Volumetric fog | – | Not a cause: `--bench-no-vfog` gave no gain. |

## Screenshots

- **Fix A/B:** the fix is a compiler setting and changes no rendering code. The before/after pass breakdown and draw counts are equal
  within noise. No visible loss.
- **Zooming from the nearest (8 m) to the farthest zoom (220 m)**, mid-motion (`--distance=8 --zoom-to=220`, captured 3.1, 3.3, 3.8 and
  4.6 s): `docs/screenshots/08s/zoom-0.jpg`, `zoom-a.jpg`, `zoom-b.jpg`, `zoom-c.jpg`. No fog band, no LOD pop and no holes in the
  Furniture.
- **Panning across the west seam** (focus x = 20 → −27 m at 20 m/s; and at Closing time at 160 m, 40 m/s): `pan-a.jpg` and `pan-b.jpg`.
  Furniture is continuous across the seam, with no gap or doubled pieces. The pan report's largest focus step (17 and 34 m) is the
  single first frame after loading, not a seam jump: its step on seam frames is 0.

## Checks

- `--model-check`: 60 models, 0 checks failed.
- Core tests: `dotnet test tests/Scp3008.Core.Tests -c Release`, 723 passed, 0 failed.
- Godot headless build: 0 errors.

## Models

None added or generated.

## Needs human review

- Look over the zoom and seam captures above in motion in the game. A still capture cannot show popping between frames.
- Check that editor debugging is acceptable with the optimised build.
