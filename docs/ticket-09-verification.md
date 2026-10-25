# Ticket 09 verification: First Villager walks

Full logs are kept under `.ticket-logs/09-*` (not committed).

## What was built

- **Core:** `Villager` (public record in the snapshot) and walking along a path found by `Navigation`, an internal 0.5 m grid
  per chunk built lazily: a cell is blocked when Furniture or a Pillar stands within 0.4 m of it. Furniture whose underside is at
  least 1.8 m above the floor is walked under, and floor coverings are walked over. Paths come from A* (8 neighbours, no corner
  cutting) across the seams by the shortest wrapped way, then string-pulled. A chunk's grid is rebuilt when Furniture in it is
  placed, moved or removed, and every walking Villager finds their way again.
- **Scenario builder:** `PlaceVillager`, `SendVillager`, `SetTimeOfDay`, `PlaceFurniture`, `MoveFurniture`.
- **Game:** `VillagerRenderer` draws each Villager at the image nearest the camera, facing their walk, blended between snapshots.
  The camera follows a Villager (V cycles through them; a pan lets go). Debug panel (debug builds only, `OS.IsDebugBuild()`):
  F7 places a Villager at the focus, F8 sends the last one there, F9 simulates one Store hour; `--villager=x,y`,
  `--villager-to=x,y`, `--follow-villager`.

## Decisions

- **Walking speed 60 m per Store hour** (2.4 m per real second at 1x). A real 5 km/h pace would cross a Display in a blink, since
  a Store day lasts 10 real minutes; this keeps a walk readable. Easy to change: `Villager.WalkingSpeed`.
- **`SetTimeOfDay` jumps the clock** forward to the next such time without simulating the skipped ticks (no events). The F9 key
  instead simulates the hour through `Advance`.
- **Unreachable destination:** the Villager stays where they are and `Destination` is null. A destination on Furniture becomes the
  nearest free floor beside it. The A* search stops after 250 000 cells (a `shortcut:` in `Navigation.cs`), far beyond any walk
  in this ticket.
- No ADR: all of this is internal to the core and cheap to change.

## Model (ADR 0006, docs/model-workflow.md)

- Reuse: no person in the repo. Poly Haven has no people models.
- Sketchfab search (downloadable, CC-BY / CC0, people in casual clothes): three candidates, all in T-pose and none animated in a
  usable way. Chosen: "Male Character In Casual Clothing" (Sketchfab 24ad029f…, CC-BY), added to `PEOPLE_MODELS` in
  `scripts/fetch-models.py`, downloaded once, credited in `game/CREDITS.md`.
- Fitted to 1.75 m tall, at most 4000 triangles, through `FurnitureModels.Product`.
- Procedurally generated items: none.

## Tests

- `VillagerNavigationTests` (13 facade tests): placing, placing on Furniture, time of day, a straight walk and its duration, a
  walk around a wall, across a seam by the shortest wrapped path, around a wall on the seam, navigation rebuilt when Furniture
  moves in and when it is removed, turning aside from a wall placed mid-walk, a destination on Furniture, around a Pillar, and a
  walk through a generated Store.
- Full suite (Release): 736 passed, 0 failed (5 min 45 s).
- Godot headless build: no errors.

## Capture

`--pillars=3x3 --clear-weather --villager=206,200 --villager-to=206,260 --follow-villager --distance=6 --yaw=180 --capture-after=6`:
the camera follows the Villager, who walked about 21 m south beside the Pillar, textured and to scale.

## Needs human review

- The figure is a static T-pose that slides along: no walk animation (none of the downloadable candidates had a usable one).
- Which way the figure faces while walking: seen from above, front and back are hard to tell apart (`ModelYaw` in
  `VillagerRenderer.cs` turns it).
- The walking speed and the feel of the follow camera, and the F7/F8/F9 and V keys in play.
