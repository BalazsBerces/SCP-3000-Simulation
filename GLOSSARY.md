# SCP-3008 Simulation

An observer-only simulation of survivor communities inside an endless furniture store (inspired by SCP-3008, CC BY-SA 3.0).

## Language

### World

**Store**:
The endless, wrapping furniture-store world in which everything takes place. Always generic, never a real brand.
_Avoid_: IKEA, map, level

**Seam**:
The line where the wrapping Store meets itself; crossing it brings you out on the opposite side. Distances and directions always take the shortest way, across a Seam if that is shorter.
_Avoid_: Edge, border (the Store has none)

**Chunk**:
A 64 m square of the Store's grid (the Store is 21 × 21 Chunks, 1344 × 1344 m, or 30 × 30 Chunks, 1920 × 1920 m, on the large Pillar layouts); the unit of streaming, culling, navigation rebuilding and simulation level of detail.
_Avoid_: Tile (floor tiles are visual), cell, Sector (a Sector is 7 × 7 Chunks on 3 × 3, 10.5 × 10.5 on 2 × 2, 10 × 10 on the large layouts)

**Ghost copy**:
An extra drawing of something near a Seam at its wrapped position on the other side, so it is visible from both sides; there is still only one object.
_Avoid_: Clone, duplicate

**Department**:
A connected region of the Store with its own look, Furniture and resources; acts as a biome. Each Department has one of eleven **Department types** (Restaurant/food market, Showrooms, Lighting & electrical, Self-serve warehouse, Bathrooms & kitchens, Plant section, Kids' area, Workspaces, Textiles & Decor, Outdoor, Dining & Hallway); separate Departments may share a type.
_Avoid_: Biome, zone, area

**Pillar**:
One of the identical, monumental concrete blocks (32 × 32 m, rising out of sight) on the regular grid of the **Pillar layout**: nine on 3 × 3 (448 m apart), four on 2 × 2 (672 m apart), nine on 3 × 3 large (640 m apart), or four on the corners and diamond large layouts, which leave the other Sectors empty. Each stands at the centre of its Sector and is marked with its grid coordinate: column letter, then row number (e.g. B-2). A Furniture-free walkway ring surrounds each one (except where the abandoned scaffold, a Point of interest, climbs one Pillar), and each carries the speakers that play the Closing announcement. Because the Store wraps, a Pillar is seen again in every direction; in 2 × 2 its east and west neighbour is the same Pillar. Visible from far away; used for navigation and as a settlement anchor.
_Avoid_: Column, monolith, tower

**Pillar layout**:
The world setting that chooses the Sector grid, which Sectors have a Pillar, and the Store size: 3 × 3 or 2 × 2 on the 1344 m Store, or, on the larger 1920 m Store, 3 × 3 large (nine Pillars), corners (Pillars in the four corner Sectors) or diamond (the default; Pillars in the four edge-middle Sectors). It is part of the world state, not of the seed, so the same seed gives the same Departments and Furniture under 3 × 3 and 2 × 2, apart from the Pillar footprints and walkway rings; on the larger Store it gives others.
_Avoid_: Pillar mode, grid size, map type

**Sector**:
One of the equal squares the Pillar layout divides the Store into, named by its grid coordinate like the Pillar at its centre (e.g. Sector B-2); on the corners and diamond layouts some Sectors are empty, with no Pillar. Nine 448 × 448 m squares on the 3 × 3 Pillar layout (boundaries on Chunk boundaries), four 672 × 672 m squares on 2 × 2 (boundaries on the 16 m aisle grid, mid-Chunk), nine 640 × 640 m squares on the large layouts (boundaries on Chunk boundaries). Sector boundaries include the Seams; every place in the Store lies in exactly one Sector.
_Avoid_: Zone, region, quadrant, district

**Landmark**:
A huge, far-visible, unique place of the Store that shapes what Villagers do there (the ruined Village, rack canyon, dead zone, clock wall, mattress mountain, fountain court or box fort). Every world has the ruined Village; the seed picks which of the others it has, at most one Landmark per Sector, preferring Sectors without a Pillar. It stands on a rectangle of at most a quarter of its Sector, clear of the Pillar and its walkway ring, which no Display enters, and rises well above the shelving and the Fog banks. It is made entirely of Furniture, which Villages may take and Restock slowly brings back; some also give their ground a lasting property, such as darkness or water, that stays when the Furniture is gone. Pillars are not Landmarks. The climbable ones (rack canyon, mattress mountain) have **Lookouts**: high places on their Furniture to look out from.
_Avoid_: Point of interest (small, seen only up close)

**Landmark showcase**:
A world for looking at the Landmarks rather than a Store to simulate: every Landmark type once, side by side in one row on empty Store floor, with no Displays. The seed still varies how each Landmark is built.
_Avoid_: Test map, gallery level

**Point of interest**:
A small, distinctive place of the Store found only up close, such as a working restroom or a Ball pit; a Sector may hold several.
_Avoid_: Landmark (huge, far-visible)

**Ball pit**:
One of the three modest bouncy castles filled with balls in every Store; a Point of interest. Harmless while the Store is open; at Closing time the Staff pass from any Ball pit to any other. Villagers cannot.
_Avoid_: Ball-pit sea, portal

**Furniture**:
A single physical object in the Store (wardrobe, shelf unit, sofa, lamp…) that can be moved, used, built with or disassembled.
_Avoid_: Prop, object, item

**Lamp**:
A piece of Furniture that gives light (table lamp, floor lamp). Lamps on display are off while the Store is open and come on one by one as the Store lights fade before Closing time, going out again as the Store lights return; none is ever lit in the dead zone. A Lamp on display draws power from the Store, so Villagers may switch it on while the Store is open; a Lamp taken away from its display needs a Village power source (battery or generator).
_Avoid_: Fixture, light (that is what a Lamp gives)

**Display**:
A room or free-standing set of Furniture generated from exactly one Theme, with one coordinated Palette. Every generated piece outside a Landmark belongs to one Display.

**Theme**:
The strict catalogue entry for what a Display shows (bedroom, patio, kitchen, etc.). It lists allowed Furniture in Core, Common and Accent tiers and their Placement roles; each Department type allows only certain Themes, chosen by weight.

**Layout archetype**:
A parameterised way of laying out a 16 m Department cell as rooms, a hall, strips or free-standing Displays. Counts, spacing, groupings and optional pieces vary per cell.

**Room partition**:
How a room-set cell is divided: one large room, two rooms, three rooms, four rooms in two by two, or an open floor of separated sets.

**Placement role**:
A rule for where Furniture stands in its Display: at a wall, corner or centre, loose or in rows, or relative to another piece (around, beside, in front, underneath or on top).

**Variant**:
A version of a Furniture type with its own proportions, silhouette or materials and footprint. A matched set usually shares its Variant.

**Palette**:
Two or three related tint indices shared by the Furniture of a Display. Neighbouring rooms usually use different Palettes; wood and metal retain their textures.

**Goods**:
Small stock on shelves (food, batteries, bulbs, cables) taken as-is rather than placed in the world.
_Avoid_: Loot, items

**Material**:
An abstract unit (panel, metal, fabric, glass, electrics) obtained by disassembling Furniture and counted in a Village's stores.
_Avoid_: Parts, components, scrap

**Restock**:
The Store's slow replenishment of Goods and Furniture, which keeps the world from running dry.
_Avoid_: Respawn, regeneration

**Fog bank**:
A large, soft-edged region of thick fog that forms, grows, drifts with a slowly changing wind across the wrapping Store, then thins out and dissipates over Store hours. Clear stretches remain between Fog banks; the air is otherwise perfectly clear, with no base fog or haze. Fog density affects sight and comes from Weather, independent of the Store seed.
_Avoid_: Fog everywhere, cloud, weather cell

**Edge fade**:
The soft dissolve of content in the last stretch before the draw distance, so the Observer never sees where drawing stops. It is the only distance effect in clear air; nothing nearer than that stretch is fogged. Presentation only.
_Avoid_: Haze, distance fog

**Closing announcement**:
The PA message ("The Store is now closing.") from the speakers on every Pillar that warns of Closing time.
_Avoid_: Alarm, siren

**Closing time**:
The dark phase of the day cycle when the Store lights go out and the Staff turn hostile; it runs from 22:00 until the Store reopens at 08:00 (about 40% of a Store day). The rest of the day the Store is **open**.
_Avoid_: Night (except informally)

**Store lights**:
The Store's own overhead fluorescent lighting: fully on while the Store is open, fading out just before Closing time and back on just before reopening. Distinct from the lights Villages make for themselves.
_Avoid_: Daylight, sun

**Store day**:
One full turn of the Store clock (24 Store hours, starting at midnight), covering the open hours and Closing time; it lasts about 10 real minutes at 1x speed.
_Avoid_: Cycle, round, turn

### People

**Village**:
A survivor community together with the furniture fortification that always surrounds it; the fortification is part of the Village, not a separate thing.
_Avoid_: Castle, stronghold, camp, settlement

**Villager**:
A single survivor belonging to a Village, with needs and a role.
_Avoid_: Survivor, agent, NPC

**Wanderer**:
A person in the Store who belongs to no Village: a newly lost shopper, or a Refugee. Wanderers join Villages or found new ones.
_Avoid_: Newcomer, nomad, stray

**Refugee**:
A Wanderer whose Village has fallen.
_Avoid_: Survivor, exile

**Caravan**:
A group of Villagers physically carrying goods between Villages for barter; it can be ambushed on the way.
_Avoid_: Trade route (that is the path, not the group), convoy

**Expedition**:
A group a Village sends out at Closing time to observe the Staff, scout or reach contested areas; it may return with Beliefs and loot, or not at all.
_Avoid_: Night scout (that is a Role), mission, patrol

**Personality**:
A Village's collective temperament (peaceful traders, isolationists, raiders…), seeded at founding by its Department and founders and drifting with events.
_Avoid_: Culture, alignment, faction type

**Raid**:
An attack by one Village on another to seize stores, territory (including Pillars) or Captives.
_Avoid_: Siege (reserved for Staff), war

**Captive**:
A Villager seized in a Raid who may later be absorbed into the captors' Village.
_Avoid_: Prisoner, slave, hostage

**Role**:
A Villager's primary job (scavenger, builder, cook, guard, trader, farmer, night scout); a Villager may switch to another Role when the Village needs it.
_Avoid_: Job, class, profession

**Blueprint**:
A plan for a structure (wall segment, tower, gate, keep, Pillar scaffold) made of slots that each demand a kind of Furniture by size and sturdiness, so the same Blueprint can be built from different Furniture.
_Avoid_: Template, recipe, design

**Tier**:
A stronger variant of a Blueprint whose slots demand sturdier Furniture plus Material reinforcement; structures are upgraded piece by piece.
_Avoid_: Level, upgrade stage

### Threat

**Staff**:
The faceless, silent entities of the Store: dormant while the Store is open, hostile at Closing time. Light does not affect them.
_Avoid_: Employees, monsters, enemies

**Belief**:
A single claim a Village holds about Staff behaviour, with a confidence; it may be false. A Village's staff knowledge is the set of its Beliefs.
_Avoid_: Fact, knowledge point, intel

**Tidying**:
Staff carrying displaced Furniture back to its original place at Closing time, when it is close and easy to return.
_Avoid_: Cleanup, reset

**Horde**:
A group of Staff moving together at Closing time.
_Avoid_: Pack, swarm, wave

**Siege**:
A Horde assaulting a Village's fortification at Closing time.
_Avoid_: Raid (reserved for Village-on-Village attacks), attack

**Horde night**:
A periodic, escalating Closing time on which large Hordes besiege several Villages at once.
_Avoid_: Blood moon, wave night

**Wounded**:
The state of a Villager after a first Staff hit; a further hit while Wounded means being Taken.
_Avoid_: Injured, hurt

**Taken**:
A Villager removed by the Staff without trace; there is no corpse.
_Avoid_: Killed, dead, died

### Observer

**Observer**:
The person watching the simulation; they cannot intervene in the world.
_Avoid_: Player, god, ruler
