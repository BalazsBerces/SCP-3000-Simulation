# Ticket 06a: Pillars on a symmetric 3 × 3 grid that divides the Store into Sectors

Implemented issue #41 against spec #1. This replaces the one-Pillar-per-Chunk layout of #7.

## Decisions made during the unattended run

- **Height 1200 m.** "Effectively endless" is a constant far above the Observer camera
  (at most ~190 m high in the normal view), so no top is ever in view. The shader also darkens the
  shaft from 45 m to 300 m, so it reads as rising out of the lit Store into the dark haze. The paint
  is never emissive.
- **Walkway ring = 3 m** (one aisle, `Pillar.WalkwayWidth`). Furniture generation rejects any piece
  whose axis-aligned bounds reach into the 38 × 38 m square (footprint plus ring). A rack or display
  table that is rejected takes the pieces on top of it with it.
- **Sector boundaries are half-open.** A position on a boundary belongs to the Sector east or south
  of it, and `SectorAt` wraps the position first. Sectors and Pillars share ids and order
  (A-1, B-1, C-1, A-2 … C-3, row by row).
- **The layout depends only on the Store geometry, never on the seed.** ADR 0003's list of what
  the seed generates now says that the Pillars and Sectors are a fixed grid.
- **See-through:** a dithered, screen-space disc (20% of the screen height) around the followed
  point. It removes only the Pillar surfaces closer to the camera than that point. Shadow passes
  are unaffected. Dithering keeps the Pillar in the opaque pipeline, and TAA smooths the edge.
- **Labels:** a 5 × 7 stencil font baked into a shared nine-cell atlas. Each label is painted on
  all four faces with its bottom at 2 m, 28 m, 60 m and 100 m. In the F4 overview the Pillars are
  rebuilt as 16 m bases, so the lowest label stays visible. The floor shader draws the Sector
  boundaries, and billboard `Label3D`s name each Sector. All of these appear only in the overview.
- **Camera start:** B-2 stands at the Store centre, where the camera used to start. The focus now
  starts 48 m south of B-2, outside its walkway and facing the Pillar (`CameraRig.StartFocusOffset`).
- **Dev argument:** `--pillar=B-2` (a grid coordinate) focuses just south of that Pillar's walkway.
- The glossary adds **Sector** and updates **Pillar**. The Chunk entry no longer lists "sector"
  as a word to avoid. No new ADR: these choices are reversible.
- An untracked `tests/Scp3008.Core.Tests/LandmarkTests.cs`, left by another run, references a
  `LandmarkType` that does not exist yet (#8). It was moved aside during this run and is not part
  of this commit.

## Automated verification

- Facade tests (`PillarTests`, `SectorTests`) check:
  - 9 Pillars at {224, 672, 1120}², all 32 m wide and the same height, with coordinates A-1 … C-3
    for seeds 3008, −1 and 42, independent of runtime randomness and time; B-2 at the centre;
  - 448 m spacing to the east and south neighbours, including C→A and 3→1 across the Seams, and
    no Pillar or ring touching a Seam;
  - no Furniture in any footprint or walkway ring, for three seeds, with Furniture right up to the
    ring;
  - the Sector list (size, centre = Pillar, chunk-aligned boundaries) and `SectorAt` inside every
    Sector, on boundaries and across Seams (unwrapped positions);
  - Pillars and Sectors unchanged through Closing time and reopening.
- The #7 tests that assumed 441 Pillars, `001`–`441`, bands, damage or 36–48 m heights were replaced.
- `dotnet test Scp3008.sln`: **100 passed**, 0 failed. `dotnet build game/Scp3008.Game.csproj`:
  0 warnings, 0 errors. Godot 4.7.2 .NET console `--headless --build-solutions --quit`: exit 0.

## Rendered verification (RTX 2070 Super, 1600 × 900 captures)

- [Close view of B-2](screenshots/06a/b2-close.png): readable `B-2`, concrete formwork joints, a
  clear walkway ring.
- [Far view, open](screenshots/06a/far-open.png): labels at several heights, shaft fading upwards.
  [Closing time](screenshots/06a/far-closing-time.png): a dark, unlit silhouette.
- [See-through](screenshots/06a/see-through.png): with the focus behind B-2, a dithered hole shows
  the shelving at the followed point.
- [F4 overview](screenshots/06a/overview.png): nine truncated bases, Sector boundary lines and
  labels.
- Seams, with weather cleared and a view translated by one Store length:
  [X](screenshots/06a/seam-x.png) / [ghost](screenshots/06a/seam-x-ghost.png) mean RGB difference
  **0.16/0.10/0.10**, and [Y](screenshots/06a/seam-y.png) / [ghost](screenshots/06a/seam-y-ghost.png)
  **0.29/0.16/0.16** on a 0–255 scale.
- Benchmark (1920 × 1080, MSAA 4x + TAA unchanged, B-2 in view on the path): **PASSED**, exit 0.

| Measurement | Average fps | 1% low fps |
| --- | ---: | ---: |
| Open path | 161.8 | 86.6 |
| Closing-time path | 156.7 | 89.5 |
| Fixed farthest zoom, Closing time | 125.9 | 111.4 |

## Human visual review still needed

- Whether the upward fade reads as "into the darkness/haze" in motion. It darkens the albedo
  rather than blending into the haze colour.
- How the see-through disc feels while panning and rotating, and whether its size suits all zooms.
- Label readability from far zoom and above Fog banks in live weather.
- Continuous panning across the Seams, and the F4 overview's legibility on other displays.
