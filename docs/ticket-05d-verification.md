# Ticket 05d: seamless Lamp lighting

Lamp pools now use the receiving opaque material's actual albedo, mapped normal, roughness and metallic with Godot's omni attenuation and BRDF. The floor, Furniture and Pillars share one pool shader and a wrapping bulb grid. Godot applies fog once at the receiving surface. Real lights rank by distance from the camera eye, with complementary pool weights; four shadow casters fade their opacity before a budget handover. Display Lamps stay lit independently of fluorescent Store lights.

## Decisions

- The workspace already contained the main 05d implementation, ADR drafts and before/after artifacts. This run reviewed, completed and validated that work. Retained baseline composites are explicitly distinguished below from newly generated evidence.
- [ADR 0005](adr/0005-lamp-pools-in-materials.md) records the trade-off against the ticket's suggested screen-space normal/albedo reconstruction: using material data avoids estimating albedo through fog and removes thousands of overlapping projection passes. This introduces a shared include requirement for future opaque materials, so it deserves an ADR.
- [ADR 0004](adr/0004-120-fps-render-budget.md) records the final **256 real lights, four shadow casters**. Performance permits the upper end of the requested range.
- No new domain terms were settled; the glossary needs no additions. The core and snapshots have no changes. Existing facade scenarios cover the light cycle, Weather, wrapping and thread behavior. As the spec prescribes no presentation unit tests, rendering is verified with the benchmark and controlled captures.
- Final verification work added `--lamp-shadows=0` for comparisons and made the benchmark fail if MSAA 4x or TAA is absent, in addition to its resolution and fps gates.

## Build and tests

- `dotnet test Scp3008.sln --no-restore`: **163 passed, zero failed/skipped**, repeated after final code changes.
- Godot **4.7.2 .NET**, discovered under `%LOCALAPPDATA%/Microsoft/WinGet/Packages/GodotEngine.GodotEngine.Mono*/`, using `*_console.exe --headless --path game --build-solutions --quit`: **exit 0**, repeated after final code changes.
- Final rendered benchmark, motion capture and four comparison runs: **exit 0**, no shader/runtime errors observed. `git diff --check` passes.

## Performance

Reference hardware: **RTX 2070 Super, Ryzen 7 3700X, 16 GB RAM**. Final run: Store 3008, `3x3`, paused clock, fixed 160 m Fog bank at (714, 657), **1920 x 1080**, vsync off, **MSAA 4x / TAA**, **20 seconds per measurement**. All three pass **120 fps average / 75 fps 1% low**. Maximum counts: **256 real lights / four shadow casters**, **5,606 visible Lamps** (5,197 in the fixed far view).

| Measurement | Average fps | 1% low fps |
| --- | ---: | ---: |
| Open path | 177.3 | 85.1 |
| Closing-time path | 186.8 | 95.9 |
| Fixed farthest zoom, Closing time | 136.7 | 104.1 |

[Final console log and per-frame reproduction diagnostics](screenshots/05d/benchmark-final.txt). A separate initial run also passed all three measurements at 186.8/84.3, 183.9/96.8 and 134.0/106.8 fps and generated the six benchmark views below.

## Controlled visual evidence

Fresh motion captures pan focus from (640, 637) to (688, 637), rotate 10 to 50 degrees and zoom from **8 to 220 m** over 240 rendered frames, with 30 settling frames before capture. Both phases use the same fixed Fog bank and paused clock. Each sequence contains 25 full-resolution captures, sampled every ten frames; the GIFs present those frames at 960 x 540.

Stationary **Lamp 198415 at (664, 1.15, 637)** moves from real/pool **1/0**, through **0.582/0.418**, **0.196/0.804**, **0.015/0.985**, to **0/1** as the camera pulls out. Shadow diagnostics show fractional opacities near zoom, and zero at far zoom. Still-image inspection found no real-light circle, additive fog colour patches or representation-dependent source loss. Human review must assess full-motion continuity and subtle shadow changes.

| Phase | Fresh animation | Full-resolution sequence |
| --- | --- | --- |
| Open | [GIF](screenshots/05d/after-open.gif) | [First frame](screenshots/05d/motion-final/open-00.png) |
| Closing time | [GIF](screenshots/05d/after-closing-time.gif) | [First frame](screenshots/05d/motion-final/closing-time-00.png) |

Fresh real-versus-pool comparisons use seed 3008, `3x3`, focus (664, 637), distance 45 m, paused 12:00 or 23:00, clear Weather and three seconds settling. Compare `--lamp-lights=256` to `--lamp-lights=0`, both with `--lamp-shadows=0`. Floor, upright Furniture faces, light tables and dark Furniture remain consistent. Excluding the top 60 UI rows, absolute RGB channel error has **99th percentile 2/255** in both phases (mean 0.184/255 open, 0.124/255 Closing time). Maxima are 21/255 and 57/255; screenshots with TAA/bloom are not bit-identical and these aggregate numbers do not establish subjective visual quality.

| Phase | Fresh real/pool side by side |
| --- | --- |
| Open | [Comparison](screenshots/05d/compare-open-side-by-side.png) |
| Closing time | [Comparison](screenshots/05d/compare-closing-time-side-by-side.png) |

Retained before/after composites from the supplied workspace: **left before, right after**. These show the original broad warm patches against pools that now follow material shading.

| Zoom / Weather | Open | Closing time |
| --- | --- | --- |
| Near | [Before/after](screenshots/05d/before-after-open-near.png) | [Before/after](screenshots/05d/before-after-closing-time-near.png) |
| Mid | [Before/after](screenshots/05d/before-after-open-mid.png) | [Before/after](screenshots/05d/before-after-closing-time-mid.png) |
| Far | [Before/after](screenshots/05d/before-after-open-far.png) | [Before/after](screenshots/05d/before-after-closing-time-far.png) |
| Fog bank, mid | [Before/after](screenshots/05d/before-after-fog-bank-open-mid.png) | [Before/after](screenshots/05d/before-after-fog-bank-closing-time-mid.png) |

Fresh benchmark views with the fixed Fog bank:

| Zoom | Open | Closing time |
| --- | --- | --- |
| Near | [Capture](screenshots/05d/benchmark-final/open-close.png) | [Capture](screenshots/05d/benchmark-final/closing-time-close.png) |
| Mid | [Capture](screenshots/05d/benchmark-final/open-mid.png) | [Capture](screenshots/05d/benchmark-final/closing-time-mid.png) |
| Far | [Capture](screenshots/05d/benchmark-final/open-far.png) | [Capture](screenshots/05d/benchmark-final/closing-time-far.png) |

Comparison logs confirm Store light **1.00** open and **0.00** at Closing time, while display Lamps still illuminate Furniture. Audio loops initialize and report playing; hum changes from -6.9 dB to -80 dB and the Closing-time drone from -80 dB to -5.2 dB.

## Standards

Parallel Standards review used `git diff 773a8110362266726df42b980a508fae2748f447` through all uncommitted/new files. This fixed point is the parent of ticket 01 commit `7ef5669`, found with `git log --grep '^01: '`. The inclusive review also sees the already committed 06/06a/06b/06c work following Atmosphere; none was reverted or rewritten. Sources: README, glossary, all five ADRs, and the code-review skill's complete Fowler smell baseline; no AGENTS, CONTRIBUTING or CODING_STANDARDS file exists.

Documented-standard breaches: **none**. Engine-independent core, facade and snapshots conform. Judgement-call smells: **no actionable findings**. Repeated FurnitureType switches describe different rendering concerns in one module; reorganizing them would add abstraction without a demonstrated benefit.

## Spec

Independent Spec review used the same inclusive stage diff against issues #1 and #44 and the earlier Atmosphere tickets. **No confirmed code findings**. Camera-eye ranking, complementary weights, four fading shadows, Closing-time Lamps, fog treatment and fps gates match the requirements. Material data instead of depth reconstruction is a justified implementation adjustment documented in ADR 0005. The reviewer rechecked the final antialiasing gate and comparison option: no findings.

Standards: **0 findings**; Spec: **0 findings**; neither axis has an outstanding worst issue. Final core tests, headless build and benchmark passed after review.

## Human review remaining

Watch continuous pan/rotation/zoom at near, mid and far distances in both phases, especially transitions between real lights and pools and shadow caster changes. Inspect vertical faces and dark/light materials, muted Lamps through Fog banks, temporal reprojection/bloom, both Seams, preferred warmth and frame pacing. Listen to fluorescent hum, drone and distant footsteps for the Atmosphere stage's subjective audio quality. This unattended run records rendered and numeric evidence; it does not claim a human has approved visual or audio quality.
