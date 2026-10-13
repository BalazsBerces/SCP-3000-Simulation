# Ticket 06f verification

Coarse Furniture matches the full look, with one level of detail for the whole view (issue #51).
Presentation only; the core is unchanged.

## Cause of the wrong colours

The coarse material's part table (`part_albedo`) was passed to the shader as a `Color[]`. Godot
converts a Color array from sRGB to linear itself, and `CoarseMaterial` had already converted
each colour, so every coarse part went through the conversion twice. The result was far too dark
and too saturated (about 0.15 to 0.6 times the brightness of the full part). The table is now a
`Vector4[]`, which Godot passes unchanged. Texture averages are also taken in linear space, as the
texture's smallest mip level shows them, instead of in sRGB. Tints and the Lamp shade glow were
not affected: both levels apply the same instance colour and emission.

Two meshes also lost the colour of a whole face in their coarse form:
- **KitchenCabinet-1**: the coarse mesh left out the Pine drawer fronts, so the Maple body showed
  instead. It now keeps one Pine panel across the front.
- **Stove and WashingMachine**: the 1 cm control panel counted as a paper-thin strip (06d rule) and
  was dropped. It is now 1.2 cm deep, so the coarse mesh keeps it.

## Colour check

`--coarse-colour-check=<dir>` (DevCapture, needs a GPU) draws every Furniture type and variant as
an instance, full and coarse, under the same light in a separate world. Tinted types are drawn in
three tints (cream, navy, terracotta). Unlit part masks (`part_mask.gdshader`) mark which pixels
each part covers in each render, minus a one-pixel edge. For each part seen in both renders, the
average linear colour must match: brightness within ×1.25 (or within 0.01 linear luminance), and
chromaticity within 0.04. Parts that are near black in both renders always pass (chrome reflecting
the dark backdrop). The check writes `report.txt` and contact sheets (full left, coarse right) and
exits with code 3 on any failure.

- Before (HEAD `9fd34fd` colour logic): [117 of 127 failed](screenshots/06f/before/report.txt),
  sheets in `screenshots/06f/before/`.
- After: [0 of 127 failed](screenshots/06f/after/report.txt), sheets in `screenshots/06f/after/`.
  The worst lit part is within ×0.86 to ×1.16 brightness and 0.021 chromaticity. Parts the coarse
  mesh leaves out by design (06d: tape, handles, chrome trim) are listed as not compared.

## One level of detail for the whole view

`FurnitureDetail` picks the level from the Observer camera's zoom (`CameraRig.Distance`, the
camera's distance from the focus). Up to 160 m (about 135 m above the floor) every piece is full.
Beyond that every piece is coarse. Zooming back in switches to full only below 152 m (8 m
hysteresis). The F4 overview is always coarse. Chunks beyond `FarDistance` are still not drawn.

Decisions:
- **Switching through render layers, not per chunk.** Full meshes are on render layer 2 and coarse
  meshes on layer 3. A switch changes only the cull mask of the Observer camera and of the Lamp
  lights (and their shadow caster mask). That costs the same however many chunk images are placed,
  where toggling tens of thousands of instances would stall the switching frame. The eye-level dev
  camera always sees the full layer.
- **Both levels drawn for 3 frames after a world is built.** Otherwise the first switch to coarse
  compiles the coarse material's pipelines mid-flight. That stall took the Open 1% low to 45 fps.
- **Threshold 160 m.** The farthest zoom that passed every run with everything full (see below).
  The ticket asks for "the closest zoom at which the benchmark still passes with everything full".
  I read that as the farthest zoom that still passes, so the switch happens as far out as the
  budget allows.
- No ADR (a reversible tuning) and no new glossary term (presentation only).

Verification:
- Captures at the switch with `--lod-debug` (coarse meshes drawn magenta), Showrooms at seed 3008,
  focus (1816, 1880), 12:00, clear weather, paused: at
  [159 m](screenshots/06f/lod-debug-zoom-159.jpg) no piece is magenta; at
  [161 m](screenshots/06f/lod-debug-zoom-161.jpg) every piece is magenta, at the centre, the edges
  and the top of the view. Same views without the debug colour:
  [159 m](screenshots/06f/showrooms-zoom-159.jpg), [161 m](screenshots/06f/showrooms-zoom-161.jpg).
- `--zoom-sweep=30` zooms from 140 m to 180 m and back over 30 s with a 1.5 m wobble:
  [2 switches](screenshots/06f/zoom-sweep.txt) (out and back in), so no flicker.

## Benchmark (RTX 2070 Super, 1920×1080, MSAA 4x + TAA)

The benchmark gained a segment "Closing time at LOD switch". It holds the far view at the switch
zoom, approached from closer, with every piece full, and fails if Furniture is coarse there.
`--lod-zoom=<metres>` overrides the switch zoom.

| Switch zoom | At switch, all full (average / 1% low) | Result |
|---|---|---|
| 219 m (no switch in range) | 101.2 / 63.3 | fail |
| 190 m | 113.3 / 79.9 | fail |
| 170 m | 120.4 / 84.6 | pass, 0.4 fps margin |
| 165 m | 136.3 / 98.1; 123.8 / 73.1 | pass, fail |
| **160 m** | 140.1 / 101.8; 130.4 / 91.1; 136.8 / 100.8 | pass ×3 |
| 150 m | 145.5 / 101.1 | pass |

Final runs at 160 m ([a](screenshots/06f/benchmark-a.txt), [b](screenshots/06f/benchmark-b.txt)),
average / 1% low fps: Open 202.6 / 90.2 and 192.3 / 90.9, Closing time 209.6 / 100.5 and
196.3 / 91.0, Closing time far (coarse) 133.3 / 99.3 and 124.0 / 92.5, at the LOD switch (full)
130.4 / 91.1 and 136.8 / 100.8. **PASSED** both times.

## Other checks

- Silhouette check (06d): [81 of 81 pass](screenshots/06f/silhouette.txt) after the mesh changes.
- Core tests: 251 passed. Solution build: 0 warnings, 0 errors. Godot 4.7.2 .NET console
  `--headless --build-solutions --quit`: exit 0 ([log](screenshots/06f/headless-build.txt)).

## Needs human review

- The before/after contact sheets: whether every coarse piece now reads as the same colour as its
  full form under the Store's own light (the check uses a neutral studio light).
- The switch at 160 m in motion: with the colours matched, it should show only as a loss of small
  detail (handles, trim, cushion seams).
- The far view (coarse) keeps the thin margin from 06d: 124 to 141 fps over runs.
