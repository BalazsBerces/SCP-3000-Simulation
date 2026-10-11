# Ticket 05a: Fog bank weather

Implemented issue #38 through the engine-independent `Simulation` facade: runtime-random Weather, growing/drifting/dissipating Fog banks, wrapped density queries, read-only snapshots, domain events and scenario placement. Godot reads and interpolates those snapshots, draws low volumetric domes with ghost copies, and retains thin global haze. F6 and debug command-line arguments place banks through the scenario builder.

## Decisions

- Store generation retains its existing seed streams; Weather gets an independent runtime stream. The optional runtime seed is only a test aid.
- Natural formation reserves no more than 45% of the Store floor, calculated from every bank's maximum eventual area, including its dissipating spread. Overlap cannot increase that bound. Scenario placement can override coverage for test situations.
- New worlds warm up Weather for 12 Store hours without emitting historical events. Banks last several Store hours; wind turns at most 12 degrees per Store hour. These are reversible tuning values.
- Banks reach 18 m at their centres, above existing Furniture and below future giant Landmarks. Noise repeats seven times per Store length so ghost copies and camera recentring share the same texture phase. Bank colour follows the Store light cycle; Closing-time emission was adjusted after captures showed the initial setting almost black.
- The fixed benchmark places a 160 m radius bank over its path after each time skip, making open and Closing-time measurements comparable. It retains MSAA 4x, TAA and the existing reduced volumetric resolution.
- No new ADR: these choices are local and reversible, within ADRs 0001, 0003 and 0004. Existing workspace work was completed; the pre-existing Codex ticket runner is included as requested by the instruction to stage everything except `.claude/`.

## Verification (2026-10-12)

- `dotnet test Scp3008.sln`: **73 passed**, zero failed or skipped. The Weather test file has 13 facade-level cases covering density/falloff/overlap, wrapped queries and drift, scenario placement, growth/dissipation/events, clear stretches, wind changes, reproducibility and independence from the Store seed. A new read-only snapshot regression test was run red before the fix, then green.
- Godot **4.7.2 .NET**, discovered under the WinGet Mono package: `--headless --path game --build-solutions --quit` passed after the final presentation change.
- Rendered `Benchmark.tscn`, **1920 x 1080**, RTX 2070 Super / Ryzen 7 3700X, **20 seconds per lighting**, MSAA **4x**, TAA **on**, fixed Fog bank in view: **PASSED**.

| Lighting | Average fps | 1% low fps |
| --- | ---: | ---: |
| Open | 244.5 | 121.9 |
| Closing time | 306.5 | 154.0 |

- Camera capture crossing both Seams with a bank at (1330, 1330): **218.7 fps**, one recentring, **0.126 m** largest crossing-frame step. Screenshot inspection found no visible Fog bank discontinuity.
- Open close/far, Closing-time close/far, clear-weather maximum zoom, and Seam screenshots were captured and inspected. The clear-weather view retains distance haze without an obvious empty chunk boundary. The final Closing-time bank is visibly dark blue.
- Local evidence: `.ticket-logs/05a/benchmark.log`, `seam.log`, `clear-far.log`, `closing-bank.log` and the corresponding PNG captures. These generated artifacts are ignored by Git.

## Human visual review

Review the moving bank edges and interpolation at normal speed and fast-forward, Seam continuity during prolonged camera movement, the 600 m cut-off and far LOD changes at all zooms, and the preferred balance of grey open fog versus dark blue Closing-time fog. Giant Landmark/Pillar visibility must be checked when those later tickets add them. The benchmark passes its configured average-fps criterion; subjective frame pacing still merits review.
