# Ticket 06d verification

Furniture keeps its shape at a distance (issue #49). Presentation only; the core is unchanged.

## Decisions made during the unattended run

- **Coarse meshes keep the silhouette.** A part is left out of a coarse mesh only if it is a
  detail: a paper-thin strip narrower than 30 cm (tape, trim lines), or a thin part (under 6 cm,
  9 cm for the wave-two types) shorter than 15 cm that does not stand on the floor (handles, knobs).
  Legs, poles, frames, slats and lamp arms stay. Thin round parts become plain box posts, and other
  round parts get fewer sides drawn around the circle, not inside it, so they keep their width.
  The Chair ladder back keeps all four slats.
- **Cheaper meshes without visible loss.** Full and coarse meshes both leave out box faces that are
  hidden against another part of the same piece (leg tops under a table top, beam ends against
  uprights). Coarse cylinder caps use two triangles fewer. Over Store 3008 this cuts the full meshes
  from 75.9 M to 67.1 M triangles. The coarse meshes go from 32.4 M (old) to 41.4 M; a first
  version that kept every part was at 51.4 M.
- **LOD distance 210 m, not beyond 220 m.** Frame rate wins, as the ticket allows. A switch past
  the farthest zoom costs the far view too much. The coarse meshes alone (drawn at every
  distance) cost about 10 fps there, because the four legs of every seat are inherent to keeping
  silhouettes. Each extra detailed chunk costs more on top. 210 m is the largest distance tried
  that passed every run. The floor under the focus stays detailed up to a zoom of about 165 m. At
  the farthest zoom the camera is about 180 m above the floor, so only the nearest chunks are
  detailed. Because the coarse meshes keep their silhouettes, the remaining switch shows mostly as
  a change of material.
- Tried and dropped: collapsing thin coarse parts in the shader beyond 400 m. It brought no
  measurable gain, so the far-view cost is not the sub-pixel legs. Lamp shadows are not a factor
  either: the far view draws the same with `--lamp-shadows=0`.
- No ADR (a reversible tuning) and no new glossary term (presentation only).

## Silhouette check

`--silhouette-check=<file>` (DevCapture, runs headless) draws every Furniture type and variant's
full and coarse mesh as flat silhouettes from the front, the side and above on a 5 mm grid. It
requires the coarse mesh to cover at least 90% of the full silhouette in each view and 90% of its
floor contact (bottom 2 cm, front and side), and to start at the same height (no floating). Exit
code 3 on any failure.

- Before (HEAD `ae59f38`): [51 of 81 failed](screenshots/06d/silhouette-before.txt). Examples:
  BarStool base 0.73 m (floats), Chair-1 front 24%, Dresser floor contact 0%.
- After: [81 of 81 pass](screenshots/06d/silhouette-after.txt); the lowest view coverage is 95%.

## Benchmark (RTX 2070 Super, 1920×1080, MSAA 4x + TAA)

The benchmark now also prints the average GPU and CPU render time, draw calls and primitives.
Far = "Closing time far" (held at the farthest zoom). All runs used the final meshes unless noted.

| LOD distance | Far average fps (runs) | Result |
|---|---|---|
| HEAD (old coarse, 110 m) | 138.4, 136.7, 135.2 | pass |
| 110 m | 125.6 | pass |
| 200 m | 125.5, 124.7 | pass, pass |
| **210 m** | 122.9, 124.3, 123.8 (final) | pass ×3 |
| 220 m | 121.0, 112.6, 116.0 | 2 of 3 fail |
| 240 m | 126.6 (1% low 67.9 in Closing time), 120.9, 112.0 | 2 of 3 fail |
| 265 m | 117.2, 115.5, 117.3 | fail |
| 525 m (switch inside the Edge fade) | 94.9 (first coarse version) | fail |
| coarse meshes everywhere | 125.9 | pass |

Final run ([log](screenshots/06d/benchmark.txt)): open 198.6 / 86.1, Closing time 192.4 / 81.8,
far 123.8 / 91.5 fps (average / 1% low). **PASSED.** The margin in the far view is about 4 fps,
and runs varied by up to ±5 fps.

## Screenshots

Showrooms at seed 3008, focus (1816, 1880), 12:00, clear weather, paused; zoom 45, 120 and 220 m:
`docs/screenshots/06d/before/` and `docs/screenshots/06d/after/` (`showrooms-zoom-*.jpg`).

## Other checks

- Core tests: 246 passed. Solution build: 0 warnings, 0 errors. Godot 4.7.2 .NET console
  `--headless --build-solutions --quit`: exit 0 ([log](screenshots/06d/headless-build.txt)).

## Needs human review

- Whether the LOD switch at 210 m (a change of material, with the silhouette kept) is acceptable
  while zooming between about 165 m and 220 m. A switch past 220 m did not pass the benchmark.
- How Furniture looks at a distance in the after screenshots, and in motion while zooming.
- The thin margin in the far view (about 4 fps) on the reference machine.
