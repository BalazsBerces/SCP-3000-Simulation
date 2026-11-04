# Code map

Where things are, so you can open the right file instead of searching. One line per file or group; the file's own
`/// <summary>` has the detail. Keep this current: add a line for every file you add, move or split.

## Layout

- `src/Scp3008.Core/`: the engine-independent simulation core (no Godot). Everything public goes through the `Simulation` facade.
- `src/Scp3008.Core/Generation/`: Store generation from the seed (deterministic, `SeededRandom`).
- `game/`: the Godot 4 .NET presentation layer (`Scp3008.Game.csproj`, `Main.tscn`, `Benchmark.tscn`). It only reads snapshots and events.
- `game/scripts/`: all Godot C# scripts. `game/scenarios/villager-sandbox/*.json`: saved sandbox scenarios.
- `tests/Scp3008.Core.Tests/`: xUnit facade-level tests, one `<Feature>Tests.cs` per feature.
- `scripts/`: ticket runners (`run-tickets.ps1` is current; `-v2..v5`, `-codex`, `-0255` are older variants), `run-game.ps1`, asset fetchers (`fetch-models.py`, `fetch-assets.py`).
- `docs/adr/`: ADRs. `docs/model-workflow.md`: how to find and wire in models (ADR 0006). `docs/ticket-NN-verification.md`: per-ticket manual checks.

## How a feature is usually wired

A gameplay feature typically touches the same set of files; look at an existing feature (e.g. Fridges, Gear, Castle) as the template:

1. Domain types: `src/Scp3008.Core/<Thing>.cs` (records, enums, domain events as `record ...(...) : DomainEvent`).
2. Behaviour: `src/Scp3008.Core/Simulation.<Feature>.cs`, a `partial class Simulation`; per-Villager behaviour in `Villagers` partials (`Villager*.cs`, `Villagers.*.cs`).
3. Test/dev setup: a `partial class ScenarioBuilder` block in the same feature file (base in `ScenarioBuilder.cs`).
4. Read side: new state on `WorldSnapshot.cs`.
5. Saves: `Simulation.Persistence.cs` (`SaveState`, bump `SaveVersion`) plus `Villagers.Persistence.cs` / `Weather.Persistence.cs`; update the version string in `tests/.../PersistenceTests.cs`.
6. Tests: `tests/Scp3008.Core.Tests/<Feature>Tests.cs` (scenario -> advance time -> assert on snapshot/events).
7. Presentation: `game/scripts/VillagerRenderer.<Feature>.cs` or `FurnitureRenderer.*.cs`; an unattended check flag in `game/scripts/DevCapture.<Feature>.cs`.

## Conventions and common APIs

Signatures every ticket ends up needing. Keep them current when you change one.

- **Events in tests:** `var events = new List<DomainEvent>(); using var subscription = sim.Subscribe(events.Add);` then `events.OfType<VillagerDied>()`. A feature's events are records deriving from `DomainEvent`, declared in its feature file.
- **Time:** `sim.Advance(TimeSpan storeTime)`, `sim.AdvanceTo(TimeSpan timeOfDay)`, `sim.AdvanceTicks(long)`; `sim.Scenario.SetTimeOfDay(TimeSpan)`. Closing time is 22:00-08:00 (`StoreHours`, `DayPhase`).
- **Saves:** `private const int SaveVersion` in `Simulation.Persistence.cs`. Bumping it means updating the `"Version":N` strings in the `InlineData` of `PersistenceTests.cs` too. Round-trip test pattern: `sim.Save(stream)`, then `Simulation.Load(stream)`; see `WoundTests` or `StaffTests`.
- **Runtime random streams:** keys 1-4 are constants at the top of `Simulation.cs` (Weather, Restock, StartingBand, Arrival), and 6 is `StaffRouteStream` in `Simulation.Staff.cs`. A new subsystem takes the next free key and adds a comment line next to the others in `Simulation.cs`. The `*Stream` constants in `Generation/` are a separate key space, used only for the seed.
- **Villagers:** `Villagers.Send(int villagerId, Vector2 destination)` (`Villager.cs`), `Send(int id, Vector3)` and `SendToLookout` (`Villagers.Climbing.cs`). `Needs(Hunger = 0, Thirst = 0, Rest = 1, Safety = 1, Morale = 0.7)` and `enum VillagerAction` are in `Needs.cs`.
- **Personality** (`Cautious`, `Builders`, `Bold`, `Traders`, `Isolationists`, `Raiders`): `Blueprint.cs`. It is set on Villages and Bands.

**ScenarioBuilder** (`sim.Scenario`), the public setup methods by file:
- `ScenarioBuilder.cs`: `PlaceVillager(pos, needs?)`, `SendVillager`, `SetTimeOfDay`, `PlaceFurniture(type, pos, rotation, variant, tint, elevation, ...)`, `MoveFurniture`, `RemoveFurniture(match)`, `SetStock`, `PlaceFogBank`, `ClearFogBanks`, `SetFogBankFormation`, `HoldWind`.
- Bands and Villages: `PlaceBand(positions, needs?, knownMap?, recipes?, personality?, ...)` and `PlaceWaterPoint` (`Simulation.Bands.cs`); `PlaceVillage(pos, villagerIds, personality)`, `CarryFurniture` and `DisassembleFurniture` (`FurnitureWork.cs`); `SetPersonality` and `PlanDwelling` (`Dwelling.cs`); `AssignRole` and `SetNeeds` (`Simulation.Meals.cs`).
- Building: `PlanBarricade` (`Blueprint.cs`), `CompleteWall` (`Simulation.Tidying.cs`), `StrikeWall` (`Simulation.Tiers.cs`), `BuildKeep` (`Simulation.Sieges.cs`), `PlanStorehouse`, `PlanWorkshop`, `PlanFurnitureYard`, `PlanPillarScaffold`, `DamageFurniture` (`Simulation.Stability.cs`).
- Goods, Gear and skills: `SetMaterials`, `SetStores`, `SetSkill`, `MakeGear` and `UseBandage` (`Recipes.cs`); `PlaceGear`, `EquipGear` and `SetCarriedLoad` (`Gear.cs`); `PutBack` (`Spoilage.cs`); `WaterCrop` (`Crop.cs`); `PlaceAssemblyInstructions` (`Simulation.KnowHow.cs`).
- Devices: `SetDevice(villageId, kind, pos, variant)`, `SetOffLure(deviceId)`, `TeachRecipe(villageId, gear)` (`Simulation.Devices.cs`).
- Staff: `PlaceStaff(pos, patrol)` (`SandboxScenario.cs`); `HitStaff` and `MakeNoise(pos, radius)` (`Simulation.Staff.cs`); `FightStaff(staffId, villagerIds)` and `PlaceStaffRemains(pos, age)` (`Simulation.StaffKills.cs`); `StaffHitsVillager` (`Simulation.Wounds.cs`).
- Sandbox: `ScenarioBuilder.LoadVillagerSandbox(preset)` and `LoadVillagerSandboxJson` (`SandboxScenario.cs`).

## Core: facade and time

- `Simulation.cs`: the facade. `Create(WorldConfig)`, `Snapshot`, `Scenario`, `AdvanceRealTime`, `Advance`, `AdvanceTo`, `AdvanceTicks`; the tick loop (`RunTicks` / `Step` / `StepCore`); random stream keys.
- `Simulation.*.cs` partials, one per subsystem: `Arrivals` (new Wanderers), `Bands`, `BandNights` (Band strategies at Closing time: hide, move on, barricade; quiet and noisy work), `Castle` (heraldry, castle building), `Chronicle`, `Construction` (Blueprint building), `Fridges`, `Gear`, `HordeNights` (periodic mass Sieges sized by the world's development up to `HordeNightConfig.Ceiling`, calm stretch after; Horde-night Hordes withdraw when the Store opens; `HordeNightTests`), `KnowHow` (assembly instructions, experiments, recipes learned), `Meals` (shared meals), `Needs`, `Outposts`, `Persistence` (save/load JSON, `SaveVersion`), `Power` (lamps, batteries, generators, lit areas), `Restock`, `Sieges` (Hordes besiege fortified Villages: weakest wall piece, then the Keep, Guards; falls and Refugees; `ScenarioBuilder.BuildKeep`), `Stability`, `Staff` (Staff, Hordes, patrols at Closing time), `Tidying` (displaced Furniture carried back at Closing time; `ScenarioBuilder.CompleteWall`), `Tiers` (wall upgrades, repairs, blows), `Wounds` (Wounded, Taken, healing; `Wound`).
- `Simulation.Devices.cs`: Devices (noise lures, alarms, Furniture traps, pits), the Village's Horde `Belief`, remains bait, Guards woken (`VillagerAction.StandingGuard`); `ScenarioBuilder.SetDevice`, `SetOffLure`, `TeachRecipe`; `DeviceTests`.
- `Simulation.Caravans.cs`: Caravans (Villagers carrying cargo between Villages, barter prices from both Villages' needs, Goods taken only up to need unless they are the Village's currency, Recipes and Known map trade, ambush by the Staff, detour round the dead zone; Traders Villages dispatch them); `ScenarioBuilder.SendCaravan`, `Simulation.BarterPrice`; `CaravanTests`.
- `Simulation.Currency.cs`: emergent currency (`Village.Currency`: a kind that keeps well and that most known Villages lack is taken in Caravan trade beyond need, worth what those Villages would give; `CurrencyChanged`); `CurrencyTests`. `DevCapture.Currency.cs`: `--currency-check=` (map markers and Village panel name the currency).
- `Simulation.Envoys.cs`: Envoys (a Villager walks an offer to another Village and returns with the answer), first contacts between Villages, lamp signals from a besieged Village's Tower (seen along a clear line, help sent), marks painted on Pillars (read by passers-by); `ScenarioBuilder.SendEnvoy`, `PaintMark`, `BuildTower`; `EnvoyTests`.
- `Simulation.Raids.cs`: Raids (Raiders walk to another Village for its stores, a Holding or Captives; outside yard, open pile and Storehouse guarded ever harder; blows wound and kill), Holdings (Pillars, fountain court, Cafeterias, food shelves a Village holds), Captives absorbed after `CaptiveAbsorbTicks`, dispatch by `RaidChance(Personality)` (runtime stream 5); `ScenarioBuilder.SendRaid`; `RaidTests`.
- `Simulation.Alliances.cs`: Alliances (`Village.Allies`; formed by an accepted Alliance Envoy; allied Guards sent as `Village.Reinforcements` during Sieges and Raids, Recipe/Belief sharing, barter bonus `AllyBarterBonus` in `Trade`, Refugees taken in by an ally in `Fall`); `ScenarioBuilder.FormAlliance`; `AllianceTests`.
- `Simulation.Flavour.cs`: Village Personality drift (famine -> Raiders, `PersonalityChanged`) and Department flavour (`Village.Department`, kitchen/lighting/bedroom/warehouse/plant/restaurant effects; `ScenarioBuilder.SetDepartment`); `PersonalityTests`.
- `Simulation.StaffKills.cs`: fights with a Staff (Halberd harms, other weapons drive back, metal armour takes blows), killing, Staff remains (pull, carried off at Closing time, hauled away by Villages); `StaffKillTests`.
- `WorldConfig.cs`: settings for a new world (seed, runtime seed, Pillar layout, showcases, Starting Bands, Arrivals, Horde nights; the hidden tunings are off in sandboxes unless given).
- `WorldSnapshot.cs`: immutable read-only view of the world; everything the presentation and tests read.
- `ScenarioBuilder.cs`: set up situations in a running world (tests, dev debug panel); feature partials extend it.
- `SandboxScenario.cs`: the villager-sandbox scenario files (Bands, Staff) in `game/scenarios/villager-sandbox/`.
- `DomainEvents.cs`: base `DomainEvent` and the common events; feature events live in their feature file.
- `StoreClock.cs` (ticks, Store day = 10 real min at 1x), `StoreHours.cs` (`DayPhase`, Closing time 22:00-08:00, light fades), `SimulationSpeed.cs`.
- `SeededRandom.cs`: SplitMix64, used for everything seeded.

## Core: space and navigation

- `StoreSpace.cs`: wrapping (torus) floor geometry, chunks, wrapped distances.
- `PillarLayout.cs`, `Pillar.cs`, `Sector.cs`: Pillar grid and the Sectors it makes (seed-independent).
- `Departments.cs`: `DepartmentType`, `Department`, `DepartmentMap`.
- `Navigation.cs`: walkable grid and pathfinding (`FindPath`; gives up on very long paths). `Navigation.Climbing.cs`: vertical navigation.
- `Landmark.cs`: `LandmarkType`, `GroundProperty` (darkness, water...), `Landmark`.
- `PointOfInterest.cs`: Points of interest (canteens, restrooms, ball pits with `BallPitNetwork`, aquariums, lost and found...).
- `WaterPoint.cs`: drinking places; which taps run is hidden truth.
- `Weather.cs`, `FogBank.cs`, `DrySpell.cs`: fog banks and dry spells.
- `LitAreaGrid.cs`: spatial lookup of lit areas for brightness.
- `RestockSight.cs`: sight-line blockers for Restock (Restock happens only unseen).

## Core: Furniture and Goods

- `Furniture.cs`: `FurnitureType`, `FurniturePiece`, `FurnitureSpec`, `FurnitureCatalog` (sizes, weights, sections), palettes and tints.
- `FurnitureList.cs`: immutable Furniture list with change sets and an index.
- `FurnitureResources.cs`: materials and tool levels a piece yields.
- `FurnitureWork.cs`: disassembly and other Furniture tasks, noise.
- `FurnitureYard.cs`: where a Village keeps gathered Furniture.
- `Displays.cs`, `Themes.cs`: Displays and the themes they are furnished from.
- `Goods.cs`: `GoodsKind`, `GoodsClass`, `StockedGoods`, `GoodsCatalog`, sector stock. `StockList.cs`: immutable Goods list. `RestockConfig.cs`.
- `Spoilage.cs`: food spoiling (Limbo), food put back.
- `Stability.cs`: load and support rules, collapses (ADR 0002).
- `CampingGround.cs`: dinosaurs and camp goods.

## Core: Villagers and Villages

- `Villager.cs`: `Villager` record (walk speed, position, state) and the `Villagers` subsystem core (`Send`, movement).
- `VillagerNeeds.cs`, `Needs.cs`: hunger, thirst, sleep, death causes, `VillagerAction`.
- `VillagerSleep.cs`, `Dwelling.cs`: sleeping places and dwellings.
- `VillagerCommunities.cs`: joining, founding Villages, Bands.
- `Villagers.Climbing.cs`: climbing. `Villagers.Persistence.cs`: Villager save state.
- `KnownMap.cs`: what each Villager knows (goods, water, danger, villages) and whether a trip is worth it.
- `Village.cs`: `Role`, `Village`, `Outpost`, the open pile.
- `Storehouse.cs`, `MessHall.cs`, `Workshop.cs`, `Crop.cs`: Village buildings and farming.
- `Blueprint.cs` (walls, slots, personality), `Castle.cs` (castle parts, moat, heraldry), `PillarScaffold.cs` (climbing scaffolds).
- `Gear.cs`, `Recipes.cs`: Gear slots and crafting.

## Core: generation (`Generation/`)

- `DepartmentGenerator.cs`: weighted Voronoi split into Departments.
- `FurnitureGenerator.cs`, `LayoutArchetypes.cs`, `DisplayFurnisher.cs`: cells, layouts and Display furnishing.
- `PillarGenerator.cs`: Pillar grid and Sectors.
- `LandmarkGenerator.cs`: Landmarks (ruined Village, rack canyon, box fort, mattress mountain, dead zone, clock wall, fountain court...).
- `PointOfInterestGenerator.cs`: Points of interest (canteens, cart graveyards, restrooms, ball pits, ...).
- `CampingGroundGenerator.cs`, `DinosaurBuilder.cs`: the Camping ground and its Furniture dinosaurs.
- `StockGenerator.cs`: initial Goods on Stock Furniture. `WaterGenerator.cs`: Water points.

## Presentation (`game/scripts/`)

- `SimulationHost.cs`: owns the core, runs it on its own thread, hands snapshots/events to the main thread. `SnapshotInterpolator.cs`: smooth playback between snapshots.
- `StoreCoordinates.cs`: core position <-> Godot space.
- Camera and UI: `CameraRig.cs`, `TimeControls.cs` (+ `.Persistence.cs`: save/load buttons), `SeedPanel.cs`, `DebugPanel.cs` (F6-F8 scenario keys), `SectorInspector.cs`, `OverviewHeatmap.cs`, `PointOfInterestDetails.cs`.
- Furniture drawing: `FurnitureRenderer.cs` (+ `.Collapse.cs`, `.Work.cs`), `FurnitureMeshes.cs` (procedural meshes), `FurnitureModels.cs` (glTF hero models), `FurnitureDetail.cs` (LOD), `FurnitureMap.cs`, `WrappedInstances.cs` (wrapped MultiMesh per chunk), `ShelfGoods.cs`, `CafeteriaFood.cs`.
- Villagers: `VillagerRenderer.cs` plus feature partials (`Castle`, `Crops`, `Dwellings`, `Gear`, `Meals`, `Storage`, `VillagePanel`, `Walls`, `Workshops`).
- World and light: `StoreFloor.cs`, `CampTurf.cs`, `GroundRenderer.cs`, `PillarRenderer.cs`, `LandmarkMarkers.cs`, `StoreAtmosphere.cs`, `LampLights.cs`, `LampPools.cs`, `LampHalos.cs`, `FogBankRenderer.cs`, `AmbientAudio.cs` (synthesised sound).
- Views: `StoreOverview.cs` (F4 whole-Store view), `StoreVisibility.cs`, `WrapDebugOverlay.cs` (F3).
- `DevCapture.BandNights.cs`: `--band-night-check=` (Bands hiding, barricading at Closing time).
- `VillagerRenderer.Signals.cs`: Envoy flags, lamp-signal beams and Pillar marks (primitive glyphs).
- `Simulation.Hauling.cs`: pulleys and winches (Village Gear; `HoistFactor` x2/x4 on what workers carry, `LiftersNeeded`), `Villagers.SetPace`; cart trains are `Caravan.Carts` (`Simulation.Caravans.cs`: load, pace, noise); `ScenarioBuilder.GiveVillageGear`; `HaulingTests`.
- `VillagerRenderer.Hauling.cs`: hoist rigs at Villages and linked platform carts behind Caravans.
- `VillagerRenderer.Devices.cs`: noise lures (boombox, music box models), alarms (tin cans on a string), pits. `DevCapture.Devices.cs`: `--device-check=`.
- `VillagerRenderer.StaffRemains.cs`: Staff remains (the Staff mannequin lying, darkened). `DevCapture.StaffKills.cs`: `--staff-kill-check=` (fight, kill, remains captures).
- Checks and tools: `DevCapture.cs` (command-line flags after `--`: `--capture=`, `--fps-report`, `--sandbox-check`, `--save-load-check`, `--model-check`, `--band-night-check`, ...; one `DevCapture.<Feature>.cs` per feature check), `Benchmark.cs`, `ModelCheck.cs`, `SilhouetteCheck.cs`, `CoarseColourCheck.cs`.

## Tests (`tests/Scp3008.Core.Tests/`)

- `TestStores.cs`: cached generated Stores (`TestStores.Get(seed, layout, ...)`); reuse it, generation dominates test time.
- `ArchitectureTests.cs`: the core must not depend on Godot.
- One `<Feature>Tests.cs` per feature, named after the core file or subsystem (e.g. `FridgeTests`, `StaffTests`, `PersistenceTests`).
