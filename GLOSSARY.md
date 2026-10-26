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
One of the identical, monumental concrete blocks (32 × 32 m, rising out of sight) on the regular grid of the **Pillar layout**: nine on 3 × 3 (448 m apart), four on 2 × 2 (672 m apart), nine on 3 × 3 large (640 m apart), or four on the corners and diamond large layouts, which leave the other Sectors empty. Each stands at the centre of its Sector and is marked with its grid coordinate: column letter, then row number (e.g. B-2). A Furniture-free walkway ring surrounds each one (except where the abandoned scaffold, a Landmark, climbs one Pillar), and each carries the speakers that play the Closing announcement. Because the Store wraps, a Pillar is seen again in every direction; in 2 × 2 its east and west neighbour is the same Pillar. Visible from far away; used for navigation and as a settlement anchor.
_Avoid_: Column, monolith, tower

**Pillar layout**:
The world setting that chooses the Sector grid, which Sectors have a Pillar, and the Store size: 3 × 3 or 2 × 2 on the 1344 m Store, or, on the larger 1920 m Store, 3 × 3 large (nine Pillars), corners (Pillars in the four corner Sectors) or diamond (the default; Pillars in the four edge-middle Sectors). It is part of the world state, not of the seed, so the same seed gives the same Departments and Furniture under 3 × 3 and 2 × 2, apart from the Pillar footprints and walkway rings; on the larger Store it gives others.
_Avoid_: Pillar mode, grid size, map type

**Sector**:
One of the equal squares the Pillar layout divides the Store into, named by its grid coordinate like the Pillar at its centre (e.g. Sector B-2); on the corners and diamond layouts some Sectors are empty, with no Pillar. Nine 448 × 448 m squares on the 3 × 3 Pillar layout (boundaries on Chunk boundaries), four 672 × 672 m squares on 2 × 2 (boundaries on the 16 m aisle grid, mid-Chunk), nine 640 × 640 m squares on the large layouts (boundaries on Chunk boundaries). Sector boundaries include the Seams; every place in the Store lies in exactly one Sector.
_Avoid_: Zone, region, quadrant, district

**Landmark**:
A huge, far-visible, unique place of the Store that shapes what Villagers do there (the ruined Village, Abandoned scaffold, rack canyon, dead zone, clock wall, mattress mountain, fountain court, box fort or Camping ground). Every world has the ruined Village; the seed picks which of the others it has, at most one Landmark per Sector, preferring Sectors without a Pillar. It stands on a rectangle of at most a quarter of its Sector, clear of the Pillar and its walkway ring, which no Display enters, and rises well above the shelving and the Fog banks. It is made entirely of Furniture, which Villages may take and Restock slowly brings back (the one exception is the Camping ground's tree house shell, a fixed part of the Landmark); some also give their ground a lasting property, such as darkness or water, that stays when the Furniture is gone. Pillars are not Landmarks. The climbable ones (rack canyon, mattress mountain, Camping ground) have **Lookouts**: high places on their Furniture to look out from.
_Avoid_: Point of interest (small, seen only up close)

**Landmark showcase**:
A world for looking at the Landmarks rather than a Store to simulate: every Landmark type once, side by side in one row on empty Store floor (the abandoned scaffold on the Pillar nearest the row), with no Displays and no Points of interest. The seed still varies how each Landmark is built.
_Avoid_: Test map, gallery level

**Villager sandbox**:
A tiny wrapping Store of 2 × 2 Chunks (128 × 128 m), with one Pillar and hand-placed Furniture, Goods, Gear and people, for developing and checking behaviour before checking the full Store at a stage end. A named **preset** is a JSON scenario loaded through the core's Scenario builder both in the game and in facade tests. The default preset is scavenge; empty, starving, closing soon and staff nearby provide other starting situations.

**Point of interest showcase**:
Like the Landmark showcase, for the Points of interest: every kind once, side by side in one row on empty Store floor, with no Displays and no Landmarks. The seed still varies how each is built.
_Avoid_: Test map, gallery level

**Point of interest**:
A small, distinctive place of the Store found only up close, such as a working restroom or a Ball pit; a Sector may hold several.
_Avoid_: Landmark (huge, far-visible)

**Common Points of interest**:
Five kinds, each made only of Furniture and planned before the Displays, which keep clear of them: the **Restroom** (1–3 per Sector), the **Cafeteria** (3 per Store, 48 × 40 m, in Restaurant/food market Departments, about equally far apart across the Seams), the **Set dinner table** (2–3 per Sector), the **Lost and found desk** (about one per Sector) and the **Shopping cart graveyard** (2–4 per Store). At most one per Department cell (a Cafeteria takes a 3 × 3 block of cells of one Department); none overlaps a Landmark, a Pillar or its walkway ring. No real store brand appears in their names, food or signage. The Landmark showcase has none.

**Cafeteria**:
A large self-service dining hall like a Scandinavian furniture store's restaurant, unmistakable without any brand name, logo or brand colours: a serving island in the middle with two tray lines past serving counters and tills, and round dining zones of tables about it. A large food source: its meals (meatballs with mash and lingonberry, almond cake) are Goods.
_Avoid_: Canteen, Restaurant (that is a Department type)

**Restroom**:
A worn, recognisable store restroom: tiled surfaces, stalls with doors (some ajar, one hanging off broken), toilets, a row of washbasins with a long mirror (sometimes cracked), electric hand dryers, paper towel dispensers, a baby-changing table, entrance signage, an out-of-order caution sign and paper litter on the floor. A small water source.
_Avoid_: Bathroom (that is a Department type), WC, toilet (that is a single Furniture piece)

**Lost and found**:
A recognisable store service point: a service counter with a bell, shelves of labelled boxes and bins, a rack of lost coats and umbrellas, a cart of unsorted things, a notice board of lost-item notes, and a generic store sign; left abandoned with spilled boxes, a toppled pile, items on the counter and a hand-written "back in 5 min" note. A source of rare, random Goods.
_Avoid_: Lost and found desk (that is the old single-desk version)

**Ball pit**:
One of the three ball pools in every Store, like a store's kids' play area: padded walls, a net railing, a slide and hundreds of coloured balls; a Point of interest. Harmless while the Store is open; at Closing time the Staff pass from any Ball pit to any other, and new Staff come out of them. Villagers cannot pass.
_Avoid_: Ball-pit sea, portal

**Ball pit network**:
The three Ball pits of a Store recorded as one network in the core: at Closing time the Staff may pass from any of them to any other; while the Store is open, and for Villagers always, it is closed.

**Rare Points of interest**:
Six kinds, planned like the common ones: exactly 3 **Ball pits** (the Ball pit network), 1–2 each of the **Armchair henge** (an unmistakable copy of Stonehenge built only of armchairs and sofas, about 26 m across and bigger than a Department cell: an outer circle of 2.7 m big sofas stood on end under lintel sofas with a few gaps and fallen stones, five **trilithons** (pairs of stacked upright sofas under a lintel, the middle one tallest), an inner horseshoe of armchairs, a sofa altar and a leaning heel stone; its false Belief: sitting in the ring at Closing time is safe), the **EXIT sign** (glows, its arrow leads nowhere), the **Mannequin crowd** (24–42 bare, faceless plastic shop mannequins of human proportions that look like the Staff from afar: five poses and builds, off-white to dingy to dark plastic, most facing one way, one fallen and sometimes one missing an arm), the **Door to nowhere** (a free-standing door frame onto the same aisle; the false "portal" Belief) and the **Aquarium aisle** (a pet-shop row of tanks of different sizes, each with its own kind of live fish; glows in the dark).

**Camping ground**:
A Landmark: a camping showroom staged like an artificial camping advertisement: a flat, even artificial-turf lawn with straight edges and a few winding boardwalk paths, price tags on the displays and banners over them. It holds fake trees and bushes, a tent show of all six kinds of tent (dome, tipi, bell, family cabin, A-frame, pop-up shelter) in two rows along a straight aisle, one with a lantern-lit door, a fake campfire ring (its fire glows only at Closing time) with a marshmallow tasting stand beside it (the marshmallows are Goods), a decorative axe stuck in one tree that can be pulled out (it is Gear), a hammock, a large lake with a kayak, a plush bear, a trail sign pointing nowhere, a tree house (a fixed shell furnished with Furniture) and a retro caravan built of Furniture. Three big, colourful dinosaurs stand in it, each sculpted from large, recognisable Furniture pieces (a T-rex, a Brachiosaurus and a Triceratops, each in its own main colour). A source of Dwellings' tents, bedding and camping Gear. Has a **Lookout** on the tree house deck.
_Avoid_: Camp (a Village is not a camp), campsite

**Abandoned scaffold**:
A Landmark: a half-finished Furniture stair climbing high up one Pillar on its walkway ring, with abandoned, half-furnished rooms bolted to the Pillar's sides, ending abruptly in a Lookout in the topmost room: the one exception to the ring staying Furniture-free. Every world has exactly one, on top of the one Landmark per Sector the others are limited to. A Village can finish it as a Pillar scaffold with fewer Materials.
_Avoid_: Ladder, tower

**Lookout**:
A high place on Furniture from which to look out over the Store: on the climbable Landmarks (rack canyon, mattress mountain, Camping ground) and at the top of the Abandoned scaffold.

**Furniture**:
A single physical object in the Store (wardrobe, shelf unit, sofa, lamp…) that can be moved, used, built with or disassembled.
_Avoid_: Prop, object, item

**Lamp**:
A piece of Furniture that gives light (table lamp, floor lamp). Lamps on display are off while the Store is open and come on one by one as the Store lights fade before Closing time, going out again as the Store lights return; none is ever lit in the dead zone. A Lamp on display draws power from the Store, so Villagers may switch it on while the Store is open; a Lamp taken away from its display needs a Village power source (battery or generator).
_Avoid_: Fixture, light (that is what a Lamp gives)

**Display**:
A room or free-standing set of Furniture generated from exactly one Theme, with one coordinated Palette. Every generated piece outside a Landmark or a Point of interest belongs to one Display.

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
Small stock taken as-is rather than placed in the world, in four **Goods classes**: food (fresh food spoils fast, tinned and dry food slowly), drink (bottled water, soft drinks, coffee), comfort goods (sweets, candles; they keep no one alive but raise morale) and supplies (batteries, bulbs, cables, tape). There are no medicines in the Store. Goods lie only on **Stock Furniture**; the live fish in an Aquarium aisle's tanks and a Cafeteria's meals are Goods too, while the tanks, racks and counters are Furniture.
_Avoid_: Loot, items

**Stock Furniture**:
A piece of Furniture that holds Goods as real store stock: grocery shelves, freezers, warehouse shelving, Cafeteria counters, Aquarium tanks, the Camping ground's stands. Each one holds its own Goods, of a kind and amount, so a single shelf can be found, guarded or raided. Display Furniture such as a showroom fridge or kitchen cabinet is empty, apart from the rare thing left behind; about one table in a hundred has a bit of food left on it.
_Avoid_: Container, loot spot

**Fill step**:
How full a shelf of one Goods kind looks: one of three looks from a few units to full, or bare when none is left. Stock Furniture shows its Goods in these steps, so the Observer reads the economy from the picture.
_Avoid_: Fullness level

**Water point**:
A place where a Villager can drink: a Restroom's washbasins, the fountain court, the Camping ground's lake, and the few showroom taps that, unlike most, still run. Which taps run never changes and is found only by trying them, so a running tap is valuable Known map knowledge.
_Avoid_: Well, water source (informal)

**Limbo**:
The state of Goods untouched on the Store's own shelves and Displays: they never spoil there. Once a Villager takes food, it starts to spoil (fresh food fast, tinned and dry food slowly), and putting it back does not restore Limbo. Villages do not know this at first; they may learn it as a Belief.
_Avoid_: Stasis, freshness, preservation

**Fridge**:
A piece of Furniture that keeps food from spoiling while powered. On display it runs on Store power; taken away from its display (to a Mess hall or Storehouse) it needs a Village power source, and without power it is only a cupboard.
_Avoid_: Refrigerator, cooler

**Material**:
An abstract unit of one of eight kinds (wood/panel, metal, fabric, leather, foam, glass, electrics, plastic) obtained by disassembling Furniture and counted in a Village's stores.
_Avoid_: Parts, components, scrap

**Gear**:
A portable thing a single Villager carries and uses (a tool, backpack, weapon, armour, lantern), made from Materials or found in the Store, and worn out by use. Gear is neither Furniture (it does not stand in the world) nor Goods (it is not store stock). Some Gear is used up in one use, like a bandage made from fabric. Found Gear is scarce and belongs to its Department (kitchen knives, warehouse tools, lighting-department torches); most Gear is made.
_Avoid_: Item, equipment, inventory

**Crop**:
A real potted plant of the Plant section (herbs, vegetables) that a Village tends and that slowly yields food Goods: the only food a Village grows rather than finds.
_Avoid_: Farm, field, plant (the decorative ones are Furniture)

**Surplus** / **Shortage**:
The Goods classes a Sector is rich in or lacks, through the Departments it holds. No Sector has everything, so Villages must trade, send Outposts or raid for what their own Sector lacks.
_Avoid_: Biome resource, specialty

**Recipe**:
A known way to make a piece of Gear or a reinforcement from Materials, needing certain tools and sometimes a Workshop. Basic Recipes are known by everyone; others are discovered by experimenting, learned from assembly instructions found in the Store, brought by people who join, or traded and shared between Villages. A Village's know-how is the set of its Recipes.
_Avoid_: Technology, tech, blueprint (that is a structure plan)

**Restock**:
The Store's slow replenishment of Goods and Furniture, which keeps the world from running dry. Each Goods kind and Furniture restock at their own rate; Furniture reappears only where no Villager can see. Goods restock only on Stock Furniture, and the shelves visibly fill and empty.
_Avoid_: Respawn, regeneration

**Dry spell**:
A stretch of a few Store days in which a Department's Restock slows or stops, while elsewhere it runs faster, so Shortages wander across the Store and Villages must adapt. Villages may form Beliefs about them.
_Avoid_: Famine, drought, shortage (a Sector's lasting lack)

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
A survivor community together with the furniture fortification that always surrounds it; the fortification is part of the Village, not a separate thing. Its wall ring has an **outline**: round, square or freeform (following the ground and folding in shelving rows, Pillars or Landmarks), chosen by its Personality and site.
_Avoid_: Castle, stronghold, camp, settlement

**Villager**:
A single survivor belonging to a Village, with needs and a role.
_Avoid_: Survivor, agent, NPC

**Wanderer**:
A person in the Store who belongs to no Village: a newly lost shopper, or a Refugee. Wanderers join Villages or found new ones.
_Avoid_: Newcomer, nomad, stray

**Needs**:
A Villager's hunger, thirst, rest, safety and morale. Hunger and thirst rise as food and water run out; rest, safety and morale are better when higher. Needs drive eating, drinking, resting, seeking safety and using comfort goods; comfort goods raise only morale.

**Body**:
What a Villager leaves at their position when they die of starvation or thirst. Distinct from Staff remains and from being Taken, which leaves no trace.

**Band**:
A small group of Wanderers travelling together, scavenging and looking for a site, before they found a Village. A run starts with Bands, not Villages.
_Avoid_: Party, group, tribe

**Refugee**:
A Wanderer whose Village has fallen.
_Avoid_: Survivor, exile

**Caravan**:
A group of Villagers physically carrying goods between Villages for barter; it can be ambushed on the way.
_Avoid_: Trade route (that is the path, not the group), convoy

**Expedition**:
A group a Village sends out at Closing time to observe the Staff, scout or reach contested areas; it may return with Beliefs and loot, or not at all.
_Avoid_: Night scout (that is a Role), mission, patrol

**Outpost**:
A small forward barricade a Village sets up near distant resources once its surroundings run low, held by a few of its Villagers; it is part of that Village until it grows large or drifts apart enough to split off as a Village of its own.
_Avoid_: Colony, camp, satellite

**Known map**:
What a Band or Village knows of the Store (held by each Villager before Bands share it): the ground it has explored, known food and water sources, dangerous places and other Villages. Source observations keep when they were last seen; stale observations retain their old amounts until seen again. A showroom tap stays untested until tried, then is remembered as running or dry. Scavenging goes to known places, scouting widens it; it can be traded or passed on, and goes stale as the Store restocks and the Staff move.
_Avoid_: Fog of war, minimap, map (that is the Observer's strategic map)

**Envoy**:
A Villager sent to another Village to carry a message, news or an offer.
_Avoid_: Messenger, diplomat, courier

**Signal**:
A message a Village gives without sending anyone: lamp signals from a tower at Closing time (safe, as light does not draw the Staff) or marks painted on Pillars and along paths.
_Avoid_: Beacon, sign

**Personality**:
The collective temperament of a Band or Village (cautious, bold, builders; peaceful traders, isolationists, raiders…). A Band's comes from its members and shapes how it survives Closing time (hiding, moving on or barricading); a Village inherits its founding Band's, shaped by its Department, and it drifts with events.
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
A plan for a structure (wall segment, tower, gate, keep, Pillar scaffold) made of slots that each demand a kind of Furniture by size and sturdiness, so the same Blueprint can be built from different Furniture. Each piece keeps its own look, so structures are a colourful patchwork, like a mixed Lego build; a Village's own colour comes from banners, draped fabrics and painted marks.
_Avoid_: Template, recipe, design

**Storehouse**:
A structure a Village builds inside its walls to hold its stores (Goods, Materials and unused Gear) up to a capacity. Whatever does not fit lies in an open pile, where food spoils faster and Raiders take it more easily; a Band or a new Village keeps everything in such a pile. A Storehouse is a prime Raid target.
_Avoid_: Warehouse (that is the Self-serve warehouse Department), depot, granary

**Furniture yard**:
The place where a Village keeps Furniture it has gathered but not yet built in. Each Village decides by its Personality and site whether it lies inside or outside the walls, and whether it gets its own barricade; outside, it keeps the inner Village uncrowded but is easier to raid.
_Avoid_: Stockpile, lumber yard, depot

**Keep**:
The central, strongest structure of a Village, stacked from showroom pieces; the Villagers' last refuge in a Siege.
_Avoid_: Tower, citadel, stronghold

**Dwelling**:
A tent (a clothes rack or metal frame hung with curtains, blankets and rugs, quick to put up) or a house (built by Blueprint from Furniture: wardrobes and bookcases as walls, tabletops as a roof, a wardrobe door), with a number of sleeping places. Villagers rest and keep their morale best in a house, then a tent, then a bed in the open, and worst on the bare floor. Whether a Dwelling is shared, personal or a family's follows the Village's Personality. Dwellings stand inside the walls, around the Keep.
_Avoid_: Hut, home, shelter

**Mess hall**:
A Village's shared place for cooking and eating together; eating there together raises morale, cooks feed more people from fewer Goods, and news, Beliefs and Known map knowledge spread there.
_Avoid_: Canteen (an old name for the Cafeteria), kitchen, dining room

**Workshop**:
A station built from Furniture inside a Village where Recipes that need it are made: a workbench (wood, metal), a sewing table (fabric, leather, foam) or an electrics bench (electrics, glass).
_Avoid_: Crafting table, factory

**Tier**:
A stronger variant of a Blueprint whose slots demand sturdier Furniture plus Material reinforcement. Each piece is upgraded on its own, by swapping it for sturdier Furniture, Strapping it or Backfilling behind it, so a structure is patchy, with strong and weak spots; a structure reaches a Tier once all its pieces have.
_Avoid_: Level, upgrade stage

**Strapping**:
Reinforcing a piece of a structure in place with Materials (metal straps, twisted cables, panels nailed on), visibly.
_Avoid_: Weaving, plating

**Backfill**:
Soft or bulky stuff (mattresses, cardboard, foam) packed behind or between the pieces of a wall, absorbing Staff blows and muffling noise.
_Avoid_: Padding, filling, insulation

### Threat

**Staff**:
The faceless, silent entities of the Store: dormant while the Store is open, hostile at Closing time. At Closing time noise draws them from afar and they notice a Villager only at close range; light does not affect them. Few at first, they grow in number over Store days and where more people live; new Staff come out of the Ball pits at Closing time. While the Store is open a Staff cannot be harmed: it gets up from any blow and goes back to standing. Only at Closing time can a Staff be killed, with great effort, and it leaves Staff remains.
_Avoid_: Employees, monsters, enemies

**Staff remains**:
The body a killed Staff leaves behind. After a few hours it starts drawing other Staff, ever more strongly, until Staff carry it off at Closing time; so a Village must haul it away, or can carry it off as bait to lure a Horde elsewhere or into a trap.
_Avoid_: Corpse, carcass

**Belief**:
A single claim a Band or Village holds about the Store's hidden rules (how the Staff behave, Limbo, Restock, the clock wall), with a confidence; it may be false. A Village's knowledge of the Store is the set of its Beliefs.
_Avoid_: Fact, knowledge point, intel

**Tidying**:
Staff carrying displaced Furniture back to its original place at Closing time. Furniture built into a Village structure or lying behind a Village's walls is never Tidied; loose pieces, a Band's quick barricade and an unbarricaded Furniture yard outside the walls are.
_Avoid_: Cleanup, reset

**Horde**:
A group of Staff moving together at Closing time.
_Avoid_: Pack, swarm, wave

**Siege**:
A Horde assaulting a Village's fortification at Closing time.
_Avoid_: Raid (reserved for Village-on-Village attacks), attack

**Horde night**:
A periodic, escalating Closing time on which large Hordes besiege several Villages at once. It is followed by a **calm stretch** of a few Store days with fewer, smaller Hordes and no Sieges.
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
