# Ticket 06: Pillars

Implemented issue #7 against spec #1. The engine-independent facade publishes static `Pillars`
with positions, dimensions and unique painted numbers, colour bands and damage markings. Godot
reads that snapshot and draws concrete Pillars and their Ghost copies. They have no light,
emission or power behaviour.

## Decisions made during the unattended run

- One Pillar per Chunk: **441** throughout the 1344 m square Store. An independent Store-seed
  stream selects an aisle intersection in each Chunk, height, band colour and damage marking.
  Width **2.4 m** fits the existing 3 m aisles without changing Furniture generation. This also
  permits Pillars directly on either Seam. Heights **36–48 m** rise above **18 m** Fog banks.
- Painted numbers **001–441** are unique within a Store. Bands and damage can repeat; the whole
  marking remains identifiable by its number. Numbers are painted on all four sides, both low
  on the concrete and above the fog. A shared procedural digit atlas and one instanced box mesh
  keep rendering inexpensive. Cracks and chipped-corner markings are surface shading rather
  than dynamic structural damage, which belongs to later tickets.
- Pillar visibility reaches **1200 m**, beyond Furniture's 600 m culling distance. The existing
  wrapped Chunk renderer supplies Ghost copies and refreshes them in the camera's recentring
  frame. Concrete uses ordinary scene lighting and fog; no emissive shader output or Light3D
  nodes are created. Display Lamps may illuminate nearby concrete, as they do other surfaces.
- Added `--pillar=NNN` and `--pause` for repeatable visual captures. Pause is applied before the
  simulation thread starts. No new domain term or ADR: the glossary already defines Pillars,
  Landmarks, Seams and Ghost copies; these are reversible generation/rendering choices under
  the existing ADRs. No code-review step was run, as requested.

## Automated and rendered verification

- TDD began with the facade test failing because the snapshot did not yet expose Pillars.
  The completed Pillar tests cover generation independent of runtime randomness/time,
  different Store seeds, unique markings, coverage, heights, canonical positions, no overlap
  with Furniture across Seams, and unchanged Landmarks through Closing time/reopening with
  their domain events. Tests only use the facade and its snapshot/events.
- `dotnet test Scp3008.sln --no-restore`: **81 passed**, zero failed/skipped, including **7**
  Pillar cases and the core's Godot dependency check.
- `dotnet build game/Scp3008.Game.csproj --no-restore`: zero warnings/errors.
- Godot **4.7.2 .NET**, located under the WinGet `GodotEngine.GodotEngine.Mono*` package:
  `Godot_v4.7.2-stable_mono_win64_console.exe --headless --path game --build-solutions --quit`
  succeeded. Rendered captures and the benchmark exited **0**, with no shader/runtime errors.
- Inspected the [Pillar 221 marking](screenshots/06/marking-221.png): painted number, ochre
  bands and a vertical crack. The [dense Fog bank view](screenshots/06/open-fog.png) shows
  exposed upper Pillars. The [Closing-time far view](screenshots/06/benchmark/closing-time-far.png)
  shows dark silhouettes above the fog with lit display Lamps below.
- Equivalent views translated by one Store length retain Pillars and their surroundings:
  [X Seam](screenshots/06/seam-x.png), [X Ghost copies](screenshots/06/seam-x-ghost.png),
  [Y Seam](screenshots/06/seam-y.png), [Y Ghost copies](screenshots/06/seam-y-ghost.png).
  Mean absolute RGB differences on a 0–255 scale were **0.077/0.058/0.052** for X and
  **0.108/0.067/0.060** for Y. Tiny rendering/temporal differences remain; the images are not
  required to be bit-identical. No missing Pillars or visible discontinuity was observed.
- A [Closing-time corner crossing](screenshots/06/corner-crossing-closing.png), with a fixed
  160 m Fog bank at (0,0), panned at (15,15) m/s from (1335,1335). One camera recentring crossed
  both Seams together; the focus moved **0.183 m** on that frame, without a whole-Store jump.
  Store lights reported **0.00**; Pillars remained dark.

## Performance

Fixed benchmark, **1920 × 1080**, **RTX 2070 Super / Ryzen 7 3700X / 16 GB**, 20 seconds per
measurement, MSAA 4x / TAA, fixed 160 m Fog bank: **PASSED**, exit **0**. The Lamp budget stayed
within **48 real lights / four shadow casters**. All three averages exceeded 120 fps.

| Measurement | Average fps | 1% low fps |
| --- | ---: | ---: |
| Open path | 137.9 | 78.4 |
| Closing-time path | 142.5 | 75.2 |
| Fixed farthest zoom, Closing time | 128.7 | 108.2 |

Benchmark [near](screenshots/06/benchmark/open-close.png),
[mid](screenshots/06/benchmark/open-mid.png) and [far](screenshots/06/benchmark/open-far.png)
captures are retained alongside their Closing-time counterparts. Console logs are retained
locally in `.ticket-logs/06/` (ignored by Git).

## Human visual review still needed

Review continuous pan/rotation/zoom across both Seams and the corner, distant number readability,
concrete/paint/damage appearance, preferred haze contrast and dark silhouettes at Closing time.
Captures verify visible geometry and lack of emission but cannot establish subjective realism or
full-motion continuity. Review frame pacing as well: the benchmark passes the average-fps target,
while the 1% lows are reported above. This ticket adds no audio; atmospheric audio quality remains
part of the stage's human review.
