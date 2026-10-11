Build a 3D top-down simulation game inspired by SCP-3008 (the infinite furniture store). Use a generic store name ("the Store") rather than any real brand, and credit SCP-3008 (CC BY-SA). This is a pure simulation: the player is an observer who watches the world develop, not a ruler of any village.

TECH
- [Your choice: e.g. Three.js in the browser / Godot / Unity]

CAMERA & PRESENTATION
- Angled top-down 3D camera (roughly 50–60° tilt), with smooth pan, zoom and rotate. No sudden swooping or shaky camera moves.
- Short view distance at ground level (haze, dim light, endless shelving rows). Only giant landmarks rise above this and can be seen from far away.
- Strong lighting contrast between "store open" (bright, fluorescent) and "closing time" (dark, with only village lamps and landmark lights).

WORLD
- A large map that wraps in all directions (toroidal): leaving one edge brings you out on the opposite edge, so the store feels endless. Landmarks and objects near the seams must render correctly across the wrap.
- Procedurally generated from a seed, so each run is a new store.
- The store is divided into departments that act as biomes, each with its own look and resources:
  - Restaurant / food market: food. The most contested area.
  - Showrooms: beds, sofas, wardrobes (housing and wall material).
  - Lighting & electrical: lamps, cables, batteries (needed to keep villages lit at night).
  - Self-serve warehouse: bulk flat-pack materials. Rich, but dark, with tall aisles that make it dangerous.
  - Bathrooms & kitchens: water from sinks; cabinets and countertops.
  - Plant section: the only renewable food (farming).
  - Kids' area: low-value but safe.
- Shelves restock slowly over time. Restock rate is a key tuning value that drives trade, migration and conflict.

LANDMARKS
- Giant concrete pillar monoliths spread across the map. They are tall enough to be seen from far away and each has unique markings (painted numbers, color bands, damage). Villagers navigate by them, villages settle near them, and some have lights that stay on after closing, acting as safe beacons.
- Other huge landmarks: an escalator rising into the dark ceiling; a canyon of collapsed warehouse racks; a flooded bathroom department forming a shallow indoor lake; an endless row of checkout tills forming a wall with only a few gaps; a dead zone where the lights never come on and staff are always active; a multi-story stacked mega-showroom.

VILLAGES
- Each village is built from the furniture and materials of the department it sits in, so villages look and play differently (kitchen villages are strong cooks, lighting villages glow and repel staff, bedroom villages have high morale but weak defense, warehouse villages are material-rich, plant villages grow food, restaurant villages are trade hubs).
- Every village is a medieval-style castle built from furniture:
  - Curtain walls of wardrobes and bookcases, with battlements made by alternating tall and short units.
  - Corner towers of stacked shelving, with watch-lamps on top.
  - A gatehouse made from a double-door wardrobe.
  - Ditches or moats (water-filled near the flooded area).
  - A central keep built from stacked showroom pieces, used as the last refuge.
  - Banners made of curtains, rugs and duvet covers; each village has heraldry based on its local fabrics.
  - Wall upgrade tiers: cardboard/pallet barriers, then flat-pack wood, then reinforced cabinet-and-countertop walls.
  - Walls take visible damage from hordes and are repaired by villagers during the day.
- Villagers are AI agents with needs (hunger, thirst, rest, safety, morale) and roles (scavenger, builder, cook, guard, trader, farmer, night scout).
- Each village has a personality (peaceful traders, isolationists, raiders, etc.).
- Village interactions: trade caravans and routes, alliances, raids and disputes over resources and pillars, refugees after a village falls, new villages founded by groups that split off.

THE STAFF
- Faceless, silent staff entities. They are passive while the store lights are on.
- At closing time the lights go out and the staff become hostile, roaming and attacking.
- Hordes escalate over time, with periodic large "horde nights."
- Light repels or slows staff: lit villages and lit pillars are safer, which makes electrical resources strategically important.
- Staff behaviour follows learnable rules rather than pure randomness (e.g. patrol routes, light avoidance, gathering around certain departments, reacting to noise). Patterns can vary by region.

NIGHT EXPEDITIONS & STAFF RESEARCH
- Each village maintains its own knowledge of staff behaviour, starting near zero.
- Villages can send risky night expeditions to observe staff, scout, or reach areas that are too contested during the day. Expeditions may return with knowledge and rare loot, return with incomplete or wrong conclusions, or not return at all.
- Knowledge has gameplay effects: safer caravan routes, better scavenging timing, smarter lamp placement and fewer losses during hordes.
- Knowledge can be traded between villages or kept secret, and becomes a source of power and rivalry.

OBSERVER TOOLS
- Time controls (pause, 1x, fast-forward).
- Click any village to see population, resources, wall condition, staff knowledge, relations and history.
- Follow a single villager, caravan or night expedition.
- A world chronicle / event log recording major events in a short, story-like voice (village founded, alliance formed, horde breached walls, village fell, expedition returned or vanished).
- World-level stats graphs over time (population, resources, number of villages).
- Seed input for generating new worlds.

BUILD APPROACH
Build in working stages, making sure each stage runs before moving to the next:
1. Wrapping procedural world with departments, shelving and the day/night light cycle.
2. Landmarks and pillars with long-distance visibility.
3. Villagers with needs, roles and scavenging.
4. Village castles with walls, towers, gates, keeps and the upgrade/repair system.
5. Staff behavior (learnable patterns) and escalating night hordes.
6. Inter-village relations (trade, alliances, raids, refugees, splits).
7. Observer tools and the chronicle.
8. Night expeditions and the staff-knowledge system.