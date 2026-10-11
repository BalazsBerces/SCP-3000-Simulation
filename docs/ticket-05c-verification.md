# Ticket 05c: camera-independent Lamp fog treatment

Pools now integrate extinction at the reconstructed opaque receiving surface; halos integrate it at the bulb. Both add only attenuated warm light, with automatic fog composition disabled for these additive contributions. The opaque scene supplies fog scattering once. Thick Fog banks still attenuate both contributions.

## Decisions

- `fog_density.gdshaderinc` is shared by native FogVolumes and Lamp attenuation: the same dome, radial falloff, density, seamless noise texture and rolling offset. Twelve midpoint samples cover only the ray segment inside the low fog layer. Distance haze uses Euclidean distance; volumetric haze and banks use camera-depth distance, matching [Godot's volumetric integration](https://github.com/godotengine/godot/blob/4.7/servers/rendering/renderer_rd/shaders/environment/volumetric_fog_process.glsl). Bank records use a floating-point texture rather than truncating a fixed-size array. Wrapped density is sampled along the ray, including intervening banks. A conservative distance bound skips banks that cannot touch it.
- Real Lamps illuminate opaque surfaces with **zero volumetric fog energy**. Every visible Lamp retains an attenuated halo and emissive shade. This deliberately sacrifices local Lamp shafts inside fog to remove camera-selected volumetric illumination that surface pools cannot replace. Fog scattering/emission and fluorescent Store lighting retain the cooler Closing-time atmosphere. Pools, halos and real lights share the warm source colour. The complementary real/pool spatial fade and four shadow casters remain.
- The shared `StoreVisibility` Resource in `Main.tscn` sets `FurnitureDistance` (600 m by default). Furniture placement extends by maximum camera distance plus a Chunk; Lamps gather that placement plus the renderer's one-Chunk placement padding. Lamp culling adds a conservative Chunk diagonal to account for the MultiMesh AABB centre differing from the geometric Chunk centre. Fog bank placement derives from the same reach. Changing the resource before scene initialization extends all three together. The real-light selection radius remains a budget choice; pools cover eligible Lamps beyond it.
- No simulation changes, domain terms or ADR: these are reversible presentation choices within ADR 0004. Existing facade tests provide core regressions; shader behavior is checked through rendered evidence, not new tests at presentation internals.
- The supplied ticket-runner edits present at the start are included under the instruction to stage everything except `.claude/`. They make stage stopping account for suffix tickets; this implementation did not alter those edits.

## Controlled reproduction

Store **3008**, paused at **12:00** or **23:00**, one **160 m radius** Fog bank at **(714, 657)** over the Lighting & electrical displays. The camera pans from **(640, 637)** to **(688, 637)** while rotating **10 to 50 degrees**, at **45 m** distance, over 240 rendered frames. Thirteen captures are made per phase, with a 30-frame settling period in the final reproducer. Reproduction does not count towards benchmark measurements.

The log tracks stationary **Lamp 199520 at (664, 1.15, 637)**: real/pool weights move from **0/1**, through **0.948/0.052**, to **1/0** as it approaches the centre. This confirms crossing the representation boundary. Baseline captures expose bright blue/grey proxy patches outside the real-light region and a warmer clearing at the centre. Final captures remove those additive fog patches; muted sources remain visible through blue fog. Screenshot inspection found no source disappearance at that boundary. The Observer's exact subjective sequence still deserves full-motion human comparison.

| Phase | Before | After |
| --- | --- | --- |
| Open | [Moving capture](screenshots/05c/before-open.gif) | [Moving capture](screenshots/05c/after-open.gif) |
| Closing time | [Moving capture](screenshots/05c/before-closing-time.gif) | [Moving capture](screenshots/05c/after-closing-time.gif) |

GIFs sample frames 20 through 240 (first capture omitted to exclude baseline initialization), scaled to 960 x 540. Full-resolution sequences and console logs are retained locally under `.ticket-logs/05c/`.

## Verification

- `dotnet test Scp3008.sln --no-restore`: **74 passed**, zero failed/skipped, after the stage review correction. Existing Lamp tests advance through Closing time and reopening and assert snapshots/events; Weather tests cover scenarios and wrapped Fog banks.
- Godot **4.7.2 .NET** WinGet console executable: `--headless --path game --build-solutions --quit` passed, exit **0**. C# build: zero warnings/errors. Final rendered runs: no shader or runtime errors.
- Final benchmark on **RTX 2070 Super / Ryzen 7 3700X / 16 GB**, **1920 x 1080**, **20 seconds per measurement**, fixed Fog bank, **MSAA 4x / TAA enabled**: **PASSED**, exit **0**. All measurements stayed within **48 real lights / four shadow casters**, with up to **5,641** visible Lamps (5,231 in the fixed far view).

| Measurement | Average fps | 1% low fps |
| --- | ---: | ---: |
| Open path | 141.0 | 81.9 |
| Closing-time path | 140.8 | 89.1 |
| Fixed farthest zoom, Closing time | 128.4 | 106.6 |

- Inspected six near/mid/far captures: warm sources in clearer air, muted sources through dense fog, lit display Lamps at Closing time, and open/Closing contrast. Fluorescent Store light remains **0.00** at Closing time.
- Raised the shared Furniture visibility distance to **800 m** temporarily, ran at 220 m zoom through a Fog bank, and restored the default: no errors, **3,678 eligible visible Lamps**, **48 real lights / four shadows**. [Extended visibility capture](screenshots/05c/visibility-800.png). This startup capture is a configuration smoke check, not the fixed benchmark.

| Zoom | Open | Closing time |
| --- | --- | --- |
| Near (8 m) | [Screenshot](screenshots/05c/open-close.png) | [Screenshot](screenshots/05c/closing-time-close.png) |
| Mid (114 m) | [Screenshot](screenshots/05c/open-mid.png) | [Screenshot](screenshots/05c/closing-time-mid.png) |
| Far (220 m) | [Screenshot](screenshots/05c/open-far.png) | [Screenshot](screenshots/05c/closing-time-far.png) |

## Standards

Whole Atmosphere review pinned to **773a811**, parent of ticket 01 commit **7ef5669**, through HEAD and this ticket's uncommitted/new files. The parallel Standards reviewer checked GLOSSARY.md, README.md, all four ADRs and the skill's Fowler smell baseline: **0 actionable findings**. Core/presentation separation, randomness, naming and rendering budgets conform.

## Spec

The independent Spec reviewer checked issues #1 and #40 over the same stage: **1 finding**, volumetric attenuation using ray length instead of camera-depth increments. Fixed haze and bank integration; retained ray length for distance fog. Recheck: **0 remaining actionable findings**. Facade tests and rendered benchmark passed after the correction.

Standards: 0 findings; Spec: 1 resolved, 0 remaining; no outstanding worst issue in either axis.

## Human visual review

Review continuous pan/rotation/zoom at the representation boundary, shadow transitions, both Seams, preferred warmth/bloom and muted Lamps against dense Closing-time Fog banks. The analytic twelve-sample attenuation approximates Godot's filtered 48-depth fog grid; subtle differences, temporal reprojection and subjective continuity need human viewing. Review frame pacing (the requirement is average fps; 1% lows are reported), and listen to the stage's ambient audio. Captures and core tests cannot establish subjective visual/audio quality.
