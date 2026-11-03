# Ticket 27a verification: Killing the Staff and Staff remains

## Automated

- `StaffKillTests` (core, facade level): lone spear only drives back; four Villagers with metal armour and Halberds kill a Staff at Closing time with fight noise and a wound; the same group cannot harm a dormant Staff while the Store is open; remains near a Village draw a Horde only after a few hours, with a growing pull; Staff carry remains off at Closing time, not while open; a Village hauls remains away and the Horde they draw stays away from it; a Sector's Staff population recovers through the next night's growth; remains and a fight survive save/load.
- Rendered check, in a window: `--villager-sandbox --sandbox-preset=empty --pause --staff-kill-check=<dir>` asserts the four Halberds and metal armours are attached, the kill happens, the remains appear where the Staff fell and the kill is in the chronicle; it writes `staff-fight-before.png`, `staff-killed-remains.png` and `staff-remains-daylight.png`.

## Decisions (unattended run)

- Fights are resolved in blow exchanges every Store minute: the Staff strikes the fighter in front first (metal armour takes the blow for 50 durability, otherwise a wound, then Taken), then each fighter in reach strikes (Halberd: 0.09 health; any other hand Gear: drives the Staff back 2 m and fells it for 20 s). Fights make noise (radius 12) that draws other Hordes.
- Fights start from `ScenarioBuilder.FightStaff` and on their own in a Siege when three or more unwounded Guards hold Halberds.
- Remains are quiet for 3 Store hours, then draw a Horde from 15 m more per hour (at most 200 m); a Horde already following a noise or a chase is not drawn. A Staff that reaches them at Closing time carries them off (they vanish at once; no carrying animation yet).
- A Village hauls remains lying within 40 m of it while the Store is open: its nearest free member carries them 60 m (at most half the Store across) directly away from the Village. That same haul is the bait: the Horde they draw goes there instead of to the Village. Choosing a bait spot (towards a Horde's route or into a trap) is left for later.
- Late Recipes `Metal armour` and `Halberd` are workbench Recipes learned like the middle ones (experiment, assembly instructions): `Recipes.Workshop` = middle + late.

## Models (ADR 0006)

No new model was downloaded; everything is reused from `game/assets/models`: metal armour is the `goods_tinned_food` tins, the Halberd is the `camp_axe` (Poly Haven hatchet) head on the table-leg shaft already used for the spear, and Staff remains are the Staff's mannequin lying down with a dark material. No procedural geometry was added.

## Needs human review

- The look of the Halberd and the tin-can metal armour on a Villager (small at gameplay distance).
- Staff remains at night are nearly invisible in the dark (by design, dread), visible in daylight; check they read as a dead Staff.
- Fighters converge on one spot round the Staff; there is no strike animation beyond the existing work sway.
