# Ticket 23 verification: Staff and Hordes roam at Closing time on regional patrol routes

## Automated (facade-level, `StaffTests`)

- Staff do not act while the Store is open: after a night of patrolling, at noon every Staff outside the dead zone stands still, has no destination and is not hostile.
- A Horde follows the same Patrol route on consecutive nights: the route in the snapshot is unchanged, and its first Staff is sent to the same first three waypoints on both nights.
- The Staff grow over Store days, in a quiet Sector too, and a Sector with twelve Villagers has more Staff than a quiet one on every night.
- New Staff appear only at a Ball pit, and only when Closing time begins (none at 21:59, none later in the night).
- A dormant Staff hit while the Store is open falls, takes no damage (Health stays 1), is not hostile, gets up about ten Store minutes later where it stood, and stays dormant.
- Staff in the dead zone walk and are hostile at noon; a Staff outside it stands still.
- At Closing time a Staff at each Ball pit, sent to each other Ball pit, comes out there at once; the same Staff stand still while the Store is open, and a Villager sent from one Ball pit to another only walks.
- Hordes, Staff (including a fallen one), the night count and the Staff positions survive a save/load round-trip, and both worlds continue identically. The save version is now 17; as in earlier tickets, older saves are refused.

## Decisions

- "Region" in the ticket is a **Sector** (the glossary avoids "region"). Each Sector gets a **Patrol route** (new glossary term): six waypoints about 15% of the Sector's side from its centre, fixed by the Store seed and Sector id (random stream 6, not the runtime seed).
- Growth: before each night, each Sector's Staff grow towards `min(12, 1 + (night − 1) / 2 + residents / 4)`, where residents are the Villagers standing in the Sector at that moment. New Staff join the Sector's last Horde with room (at most 6) or start a new one. Growth is deterministic, so no runtime random stream is used. A Store without Ball pits (the Villager sandbox) gets no new Staff.
- New Staff come out of the Ball pit nearest their Sector's centre, at the nearest free floor that is not shut in among the pit's Furniture (one generated pit's centre snaps into such a pocket).
- Footprint paths only reach a few hundred metres, so a Staff far from its waypoint walks 80 m legs towards it. Standing at a Ball pit at Closing time, it first passes to whichever other pit is nearer its goal. If no Staff in a Horde finds a way, the Horde tries its next waypoint a Store minute later rather than search again every tick.
- Each Closing time a Sector's Hordes start again from their own start waypoint. A hand-placed sandbox Staff is a Horde of one with its own route and carries on from where it stopped. At reopening, Staff stop where they are.
- A hit at Closing time does nothing yet. Harming and killing Staff is ticket #77; the shortcut is marked in `Simulation.Staff.cs`.

## Rendered check

- Godot headless `--build-solutions` and `dotnet build` of the game project: 0 warnings, 0 errors.
- Windowed `--villager-sandbox "--sandbox-preset=staff nearby" --capture=… --capture-after=8` (1280×720, with a 300 s limit) ran and exited. The debug panel shows "1 Staff". At 23:28 the figure is lost in the Closing-time dark.
- Presentation: Staff keep the existing faceless Mannequin mesh. A fallen Staff lies flat like a body. Staff make no sound of their own; `AmbientAudio` plays the footsteps of the nearest hostile walking Staff within 45 m, where it walks and along its heading, heavy and steady, in place of a random distant walk.

## Models

No new model, prop or texture: Staff reuse the Mannequin Furniture mesh (ADR 0006, reuse). No procedural geometry was added.

## Needs human review

- Whether the Mannequin reads as a faceless Staff figure in the dark, and the fallen pose.
- How the Staff footsteps sound and where they seem to come from (pacing, loudness, the 45 m range).
- How the Hordes move in a full Store at Closing time: the legs towards a Patrol route, and Staff appearing at Ball pits.
- No fps benchmark was run; up to 12 Staff per Sector walk at Closing time.
