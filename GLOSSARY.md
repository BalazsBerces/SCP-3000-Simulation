# SCP-3008 Simulation

An observer-only simulation of survivor communities inside an endless furniture store (inspired by SCP-3008, CC BY-SA 3.0).

## Language

### World

**Store**:
The endless, wrapping furniture-store world in which everything takes place. Always generic, never a real brand.
_Avoid_: IKEA, map, level

**Department**:
A region of the Store with its own look and resources (e.g. Showrooms, Self-serve warehouse); acts as a biome.
_Avoid_: Biome, zone, area

**Pillar**:
A giant concrete monolith with unique markings, visible from far away; used for navigation and as a settlement anchor.
_Avoid_: Column, monolith, tower

**Landmark**:
Any huge, far-visible feature of the Store (Pillars, the till wall, the flooded Department, the ball-pit sea, the clock wall, etc.).

**Furniture**:
A single physical object in the Store (wardrobe, shelf unit, sofa, lamp…) that can be moved, used, built with or disassembled.
_Avoid_: Prop, object, item

**Goods**:
Small stock on shelves (food, batteries, bulbs, cables) taken as-is rather than placed in the world.
_Avoid_: Loot, items

**Material**:
An abstract unit (panel, metal, fabric, glass, electrics) obtained by disassembling Furniture and counted in a Village's stores.
_Avoid_: Parts, components, scrap

**Restock**:
The Store's slow replenishment of Goods and Furniture, which keeps the world from running dry.
_Avoid_: Respawn, regeneration

**Closing announcement**:
The PA message ("The Store is now closing.") from the speaker towers that warns of Closing time.
_Avoid_: Alarm, siren

**Closing time**:
The dark phase of the day cycle when the Store lights go out and the Staff turn hostile.
_Avoid_: Night (except informally)

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
The faceless, silent entities of the Store: passive while the lights are on, hostile at Closing time.
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
