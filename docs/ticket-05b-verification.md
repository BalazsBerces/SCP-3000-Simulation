# Ticket 05b: Lamp lighting at every zoom

Display Lamps now stay lit independently of the fluorescent Store lights. Godot ranks a maximum of 48 real clustered lights around the camera's visible floor focus, with at most four shadow casters. Every other visible Lamp gets an instanced depth-projected light pool sized by its catalog `LightRadius`; the pool reconstructs opaque surface positions from the depth buffer, so it brightens nearby Furniture as well as the floor. All Lamps have soft camera-facing halos affected by haze and Fog banks; emissive shades bloom through Environment glow.

## Decisions

- There is no mutable Lamp power state in the current core. Catalog light radii remain available throughout the day; the obsolete Store-power documentation was corrected. No Village power system or Staff behavior was introduced. The facade regression test advances a generated scenario through Closing time and reopening, checking Lamps, Store lighting and phase events.
- Real-light selection uses horizontal distance from the active camera's centre ray intersection with the floor, rather than distance from its eye. Just-offscreen Lamps participate in ranking, preventing frustum entry from abruptly changing the budget cutoff. The 49th candidate defines a smooth spatial fade over the outer 20% of the selection radius; pool weights are its exact complement. A Lamp leaving the real-light budget already has zero real energy.
- Pools use back faces of instanced bounding boxes and screen depth. This is a cheap approximation to lighting, without additional shadow maps or normal-dependent shading; only the existing four nearest real lights cast shadows. Halos use depth-tested billboards. Wrapped positions come from the same Chunk images as Furniture; selection includes a half-diagonal margin around its distant visibility cutoff, and the dev overview draws the Store once.
- Both MultiMeshes upload packed buffers in bulk. A stationary view reuses selection and buffers until the camera moves, the viewport changes or a new world is created. Weather and fluorescent lighting still update normally.
- The benchmark retains the camera over the Lighting & electrical displays, travels from the closest allowed zoom (8 m) to the farthest (220 m), captures near/mid/far in both phases, and separately measures a fixed farthest-zoom Closing-time view. It checks the light/shadow budgets and requires every average to reach 120 fps at 1080p. MSAA 4x and TAA remain enabled.
- No new domain term or ADR: these presentation choices are reversible and follow ADR 0004. The separately supplied workspace edit to the Codex ticket runner is included under the instruction to stage everything except `.claude/`; the implementation did not edit that runner.

## Verification

- `dotnet test Scp3008.sln`: **74 passed**, zero failed or skipped (including the facade Lamp regression).
- Godot **4.7.2 .NET** console executable found under the WinGet Mono package: `--headless --path game --build-solutions --quit` **passed**, exit code 0.
- Rendered benchmark on **RTX 2070 Super / Ryzen 7 3700X / 16 GB**, **1920 x 1080**, **20 seconds per measurement**, fixed 160 m Fog bank, **MSAA 4x**, **TAA enabled**: **PASSED**. No shader or runtime errors. Every measurement stayed within 48 real lights and four shadow casters; the far view covered 5,231 visible Lamps with pools and halos.

| Measurement | Average fps | 1% low fps |
| --- | ---: | ---: |
| Open path | 170.7 | 91.9 |
| Closing-time path | 188.3 | 118.5 |
| Fixed farthest zoom at Closing time | 150.3 | 129.3 |

- A six-second Closing-time pan crossed both Seams together, with a Fog bank present: **170.2 fps**, one camera recentring, **0.071 m** largest crossing-frame step, **1,205 visible Lamps** with pools/halos and Store light **0.00**. Exit code 0; the captured image showed no apparent Lamp/Fog bank discontinuity. [Seam capture](screenshots/05b/seam.png).
- Inspected all six benchmark captures: warm illumination reaches the floor and tables at minimum zoom, pools and source glow remain visible at mid/farthest zoom, and Closing time retains Lamp islands with the fluorescent Store light off. Full-motion transition smoothness remains a human review item.
- Local build, test and rendered-run logs are under `.ticket-logs/05b/` (ignored generated evidence). The benchmark initially exposed a 101.7 fps far view; bulk uploads brought it above target before final verification.

| Zoom | Open | Closing time |
| --- | --- | --- |
| Near (8 m) | [Screenshot](screenshots/05b/open-close.png) | [Screenshot](screenshots/05b/closing-time-close.png) |
| Mid (114 m) | [Screenshot](screenshots/05b/open-mid.png) | [Screenshot](screenshots/05b/closing-time-mid.png) |
| Far (220 m) | [Screenshot](screenshots/05b/open-far.png) | [Screenshot](screenshots/05b/closing-time-far.png) |

## Human visual review

Review the pool-to-real-light transitions during continuous pan/zoom/rotation, shadow changes, preferred warmth/bloom/halo strength, and the balance of Lamp light against thick Fog banks. Check prolonged movement across both Seams and distant Chunk/LOD visibility. Screenshot inspection and the benchmark verify rendered output and measured performance; subjective continuity and frame pacing still need the stage-end visual review.
