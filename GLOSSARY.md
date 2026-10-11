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
A 64 m square of the Store's grid (the Store is 21 × 21 Chunks, 1344 × 1344 m); the unit of streaming, culling, navigation rebuilding and simulation level of detail.
_Avoid_: Tile (floor tiles are visual), cell, Sector (a Sector is 7 × 7 Chunks)

**Ghost copy**:
An extra drawing of something near a Seam at its wrapped position on the other side, so it is visible from both sides; there is still only one object.
_Avoid_: Clone, duplicate

**Department**:
A connected region of the Store with its own look, Furniture and resources; acts as a biome. Each Department has one of seven **Department types** (Restaurant/food market, Showrooms, Lighting & electrical, Self-serve warehouse, Bathrooms & kitchens, Plant section, Kids' area); separate Departments may share a type.
_Avoid_: Biome, zone, area

**Pillar**:
One of nine identical, monumental concrete blocks (32 × 32 m, rising out of sight) on a regular 3 × 3 grid, 448 m apart, each standing at the centre of its Sector and marked with its grid coordinate: column letter A–C, then row number 1–3 (e.g. B-2). A Furniture-free walkway ring surrounds each one. Visible from far away; used for navigation and as a settlement anchor.
_Avoid_: Column, monolith, tower

**Sector**:
One of the nine equal 448 × 448 m squares the Pillars divide the Store into, centred on its Pillar and named after it (e.g. Sector B-2). Sector boundaries run along Chunk boundaries and the Seams; every place in the Store lies in exactly one Sector.
_Avoid_: Zone, region, quadrant, district

**Landmark**:
Any huge, far-visible feature of the Store (Pillars, the till wall, the flooded Department, the ball-pit sea, the clock wall, etc.).

**Furniture**:
A single physical object in the Store (wardrobe, shelf unit, sofa, lamp…) that can be moved, used, built with or disassembled.
_Avoid_: Prop, object, item

**Lamp**:
A piece of Furniture that gives light (table lamp, floor lamp). Lamps on display stay lit at all times, including Closing time; a Lamp taken away from its display needs a Village power source (battery or generator).
_Avoid_: Fixture, light (that is what a Lamp gives)

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
A large, soft-edged region of thick fog that forms, grows, drifts with a slowly changing wind across the wrapping Store, then thins out and dissipates over Store hours. Clear stretches remain between Fog banks; a thin haze stays everywhere. Fog density affects sight and comes from Weather, independent of the Store seed.
_Avoid_: Fog everywhere, cloud, weather cell

**Closing announcement**:
The PA message ("The Store is now closing.") from the speaker towers that warns of Closing time.
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
