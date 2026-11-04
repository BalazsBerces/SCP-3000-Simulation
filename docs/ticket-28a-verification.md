# Ticket 28a verification: noise lures, alarms and traps

## Automated

- `DeviceTests` (core, facade level): a noise lure draws a Horde off its course (with a control run that besieges); an alarm wakes the Guards earlier than a Siege does; a Furniture trap's stack collapses on a Staff, hurting it and holding it back at least 8 Store minutes; remains a Village hauls onto its pit draw a Horde into it; Villages with late know-how set more Devices after each Horde night, on the side of their Horde Belief; Devices and Beliefs survive save/load.
- `--villager-sandbox --sandbox-preset=empty --pause --device-check=<dir>` (window): the radio, music box, alarm and pit are drawn, the trap's stack stands, a Staff springs it, the chronicle records it, a set-off radio plays; captures `devices-set`, `devices-trap-sprung-night`, `devices-trap-sprung-day`.

## Decisions (unattended run)

- The Devices are late know-how, not Gear Recipes: a Village that knows a late Recipe (metal armour or Halberd) sets them, paying Materials (lure: 2 electrics, alarm: 1 metal, pit: 2 wood panels, Furniture trap: 4 wood panels for its three pieces, which are placed new). They appear in place at once (shortcut: no Villager walks out to set them).
- Beliefs did not exist in the core; the Village gets one, its Horde Belief, which places alarms and traps (lures go off to the side).
- "Wakes the Guards": Guards stand guard (`VillagerAction.StandingGuard`) from the alarm, or else from the Siege's start, until the Store opens.
- A trap never kills (ADR 0007: killing takes a fight); it leaves a Staff at least 10% health.
- A Horde on its way to a playing lure does not start a Siege; one already besieging is not drawn off.
- A playing lure drowns out other noises for the Horde it drew; a sprung Furniture trap is set again when the Store opens if Tidying has stacked it back up.
- Stage fix (from ticket 23): a Horde with a member that finds no way to its waypoint now waits 10 Store minutes before trying again, and that member no longer holds the rest at the waypoint. Before, each failed search (a full A* run, ~57 ms) repeated every Store minute or every time the rest arrived, and `DrySpellTests.Long_run_visits_the_restocked_Departments_without_stopping_all_food` hung the full suite.
- Stage review fixes: Guards standing guard are not taken for other jobs, and stop standing guard when their Village falls; saves with a broken Horde Belief or a trap pointing at missing Furniture are rejected.

## Models (ADR 0006)

- Noise lure, radio: Poly Haven `boombox` (CC0). Noise lure, music box: Sketchfab "Music Box Prop" by Nabbakadnashr (CC-BY). Both in `fetch-models.py` (`GOODS_MODELS`, the list for small props loaded with `FurnitureModels.Product`) and `game/CREDITS.md`.
- Alarm cans: reused `goods_tinned_food`. Furniture trap: existing Furniture (flat-pack box, dresser, bookcase).
- Generated: the trip-wire string (a thin box) and the pit's hole (a dark disc). Searched: Poly Haven models (no wire, rope, hole or pit), Sketchfab (CC0/CC-BY downloadable "floor hole pit": no results), OpenGameArt ("trip wire", "rope", "hole": only a rope fence and a tree with a hole); a string and a hole have no model to download.

## Needs a human look

- The scale of the tin cans and lures next to the Furniture in `devices-set.png` (the cans read large at that camera distance).
- Whether the pit reads as a hole and the lure's shaking reads as playing.
- Fps with many Devices was not measured (they are a handful of meshes per Village).
