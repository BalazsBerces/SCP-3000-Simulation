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

Runtime random streams: keys are constants at the top of `Simulation.cs` (one stream per subsystem; next free key: check there).

## Core: facade and time

- `Simulation.cs`: the facade. `Create(WorldConfig)`, `Snapshot`, `Scenario`, `AdvanceRealTime`, `Advance`, `AdvanceTo`, `AdvanceTicks`; the tick loop (`RunTicks` / `Step` / `StepCore`); random stream keys.
- `Simulation.*.cs` partials, one per subsystem: `Arrivals` (new Wanderers), `Bands`, `Castle` (heraldry, castle building), `Chronicle`, `Construction` (Blueprint building), `Fridges`, `Gear`, `KnowHow` (assembly instructions, experiments, recipes learned), `Meals` (shared meals), `Needs`, `Outposts`, `Persistence` (save/load JSON, `SaveVersion`), `Power` (lamps, batteries, generators, lit areas), `Restock`, `Stability`, `Staff` (Staff, Hordes, patrols at Closing time), `Tiers` (wall upgrades, repairs, blows), `Wounds` (Wounded, Taken, healing; `Wound`).
- `WorldConfig.cs`: settings for a new world (seed, runtime seed, Pillar layout, showcases, Starting Bands).
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
- Checks and tools: `DevCapture.cs` (command-line flags after `--`: `--capture=`, `--fps-report`, `--sandbox-check`, `--save-load-check`, `--model-check`, ...; one `DevCapture.<Feature>.cs` per feature check), `Benchmark.cs`, `ModelCheck.cs`, `SilhouetteCheck.cs`, `CoarseColourCheck.cs`.

## Tests (`tests/Scp3008.Core.Tests/`)

- `TestStores.cs`: cached generated Stores (`TestStores.Get(seed, layout, ...)`); reuse it, generation dominates test time.
- `ArchitectureTests.cs`: the core must not depend on Godot.
- One `<Feature>Tests.cs` per feature, named after the core file or subsystem (e.g. `FridgeTests`, `StaffTests`, `PersistenceTests`).
