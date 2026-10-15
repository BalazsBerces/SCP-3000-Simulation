# SCP-3008 Simulation

An observer-only simulation of survivor communities inside an endless furniture store (inspired by SCP-3008, CC BY-SA 3.0). The spec is GitHub issue #1; domain terms are in [GLOSSARY.md](GLOSSARY.md) and decisions in [docs/adr](docs/adr).

## Layout

- `src/Scp3008.Core`: the engine-independent simulation core (plain C#, no Godot). `Simulation` is its single facade: create a world from a seed, advance time, read snapshots, subscribe to domain events, and run the clock on its own thread. `StoreSpace` (on the snapshot) is the wrapping Store geometry: wrapped positions, shortest-wrapped distance and direction, and chunks. The seed generates the Store (`Generation/`): `Departments` on the snapshot says which Department covers each part of the floor, and `Furniture` lists every piece of Furniture with its type, position, elevation and rotation (sizes in `FurnitureCatalog`, where a `LightRadius` marks the Lamps). `StoreHours` defines the day cycle: Closing time from 22:00 to 08:00 and the Store light level at any time of day (`Phase` and `StoreLightLevel` on the snapshot); the core emits `ClosingTimeBegan` and `StoreReopened` events.
- `tests/Scp3008.Core.Tests`: headless xUnit tests that drive the facade.
- `game`: the Godot 4 (.NET, Forward+) presentation layer, which only reads from the core. Furniture meshes are built in code from simple parts (`FurnitureMeshes`) and drawn with one MultiMesh per Furniture type and chunk; beyond a zoom of 160 m every piece draws its coarse mesh, one level of detail for the whole view (`FurnitureDetail`, switched through render layers with hysteresis), chunks beyond the haze are not drawn, and tall solid Furniture (wardrobes, fridges, shelving back panels) feeds occlusion culling near the camera. Each Department type has its own floor texture. `LampLights` gives real lights to the Lamps nearest the camera within the render budget of ADR 0004: at most 256 clustered lights, of which the 64 nearest cast (dual-paraboloid) shadows from a uniform 8192 px atlas of 1024 px slots; a Lamp's shadow fades in or out over 0.4 s when it gains or loses a slot, and the slot passes on only once the shadow has faded out. Every Lamp's remaining light is a pool computed inside the opaque materials (the floor, Furniture and Pillar shaders include `lamp_pools.gdshaderinc`; ADR 0005): `LampPools` shares a 4 m grid of the Lamps' bulbs as global shader parameters, and each material shades the pools with its own albedo, normal map, roughness and metallic using Godot's omni attenuation and BRDF, so real lights and pools blend without a visible boundary, and Godot fogs the pools at the surface like any lit colour. Real lights near the edge of the budget fade out while their pools take over the same share. Real Lamps illuminate surfaces only (volumetric energy is zero), so moving the camera cannot change the fog illumination by changing the selected lights. Halos integrate attenuation at the bulb, using the same Fog bank density/noise as the native volumes and adding no repeated fog scattering. A shared `StoreVisibility` resource in `Main.tscn` supplies Furniture distance and derived Lamp/Fog bank reach; change its `FurnitureDistance` to extend coverage together. Every Lamp has a soft halo, and shades bloom. Display Lamps are off while the Store is open and come on one by one (scattered per-Lamp ramps decided by the core) as the Store lights fade before Closing time, going out the same way as they return. The Store light shines 12° from vertical and casts soft, short PSSM shadows (filtered blur, no PCSS) out to the far edge of the view at the current zoom (at most 180 m), fading beyond it; screen-space ambient occlusion (low quality) darkens contact areas, with the open ambient lowered from 0.6 to 0.4. Both fade with the Store lights and are off at Closing time; the overview has no shadows. Pillars cast shadows only through a 6 m proxy at their foot. Reference machine (ticket 06i, 64 Lamp shadows): Open 152.7 / 75.6 fps (average / 1% low), Closing time 155.8 / 66.9, Closing time far 124.3 / 97.0, at the LOD switch 121.2 / 90.4: all above the 90 / 60 gate and the 120 target. `StoreAtmosphere` blends light, distance fog and a low volumetric haze layer (thick at floor level, thin above) between the open Store and Closing time; `AmbientAudio` synthesises the fluorescent hum, the Closing-time drone and distant footsteps in code.
- `game/assets/textures`: CC0 PBR textures from Poly Haven and ambientCG (see `CREDITS.md`), downscaled to 512 px by `scripts/fetch-assets.py` (needs Pillow).

## Build and test

Furniture generation exposes `Displays` and `CellLayouts` alongside the individual pieces in the facade snapshot. Every Display has one strict `Theme`, a coordinated Palette and a Layout archetype for its Department cell. The Theme catalogue defines Core, Common and Accent slots with Placement roles; rooms vary their partitions and size. The ten most repeated Furniture types have three Variants each, with variant-specific specs, full/coarse meshes and solid occluders. Verification and matching before/after captures are in [ticket 03b verification](docs/ticket-03b-verification.md).

For an unattended visual tour, run `godot --path game -- --pillars=3x3 --pause --furniture-tour=<absolute directory>`: it saves every Department at the same 45 m camera convention used for 03a, every Layout archetype at 24 m and all Variants at 6 m, with paused noon lighting and clear Weather.

Lighting & electrical occupies about 2.5% of the Store floor in a few smaller seeded regions. Lamps remain occasional in other themed Displays, targeting 3–4% of all Furniture pieces, while Armchairs, Sofas and tables receive more display space. Counts across five seeds and all Pillar layouts, matching captures and benchmark results are in [ticket 03c verification](docs/ticket-03c-verification.md). Add `--clear-weather` to a paused dev capture to remove Fog banks; the air has no base fog or haze.

Requires the .NET 8+ SDK and Godot 4.7 .NET.

```sh
dotnet test Scp3008.sln
godot --headless --path game --build-solutions --quit   # build the Godot project
godot --path game                                        # run the game
```

Benchmark (ADR 0004): `godot --path game res://Benchmark.tscn` flies a fixed camera path over Store 3008 (always on the `3x3` layout) with the clock paused, once in open lighting (12:00) and once at Closing time (23:00), with vsync off at 1920×1080. It prints the average and 1%-low fps (the mean rate over the slowest 1% of frames) for each lighting and also measures a fixed farthest-zoom Closing-time view over Lighting & electrical and exits with code 0 if all three reach at least 90 fps average and 60 fps 1% low at 1080p with MSAA 4x and TAA, 1 if not; the target is still 120 fps average, and each measurement says whether it met it. Add `-- --benchmark-seconds=s` to change the measuring time per lighting (default 20) and `--benchmark-shots=dir` to save screenshots of the path's near, mid and far views in both lighting phases. Add `--lamp-reproduction=dir` to capture a continuous 240-frame pan, rotation and zoom from the nearest to the farthest zoom (25 frames per phase) over the same displays, with a fixed bank and paused clock; the log tracks one stationary Lamp through the real-light/pool boundary and the shadow opacities. `--lamp-lights=n` (also in the game) sets the real-light budget; `0` shows pools only, to compare them with real lights. Use `--lamp-shadows=0` for both comparison runs to isolate surface shading from shadows. See [ticket 05d verification](docs/ticket-05d-verification.md) for captures and stage review.

Unattended check: `godot --path game -- --capture=shot.png --capture-after=5 --fps-report` saves a screenshot, prints the average fps and quits. More dev arguments: `--focus=x,y` starts the camera at a Store position, `--pan=vx,vy` pans it (metres per second) and reports seam crossings and the largest per-frame step, `--distance=m` sets the zoom distance, `--wrap-debug` turns on the wrap overlay, `--no-focus-wrap` keeps the camera off-floor (to compare two images of the same place), `--seed=N` sets the first Store's seed, `--start-time=HH:MM` starts the Store clock at that time (e.g. `23:00` for Closing time), `--eye-level` captures from a Villager's eye height to check the ground-level view, and `--enter-seed=text` types a seed into the seed input after a second (the capture timer restarts when the new Store is shown). `--landmark-showcase` starts in the Landmark showcase (every Landmark side by side, no Displays); in the game the seed panel's Landmark showcase button switches to it and back, and its Landmark buttons fly the camera to each one.

## Pillars and Sectors

The world config's **Pillar layout** (`WorldConfig.PillarLayout`, also on the snapshot) chooses
the Sector grid, which Sectors have a Pillar, and the Store size: `3x3` (the `WorldConfig` default),
`2x2`, `3x3L`, `3x3L-corners` or `3x3L-diamond` (what the game starts with). It is part of the
world state and never comes from the seed.
`Pillars` on the facade snapshot lists the identical concrete Pillars: 32 × 32 m blocks, 1200 m
tall (beyond any camera, so their tops are never seen), each marked with its grid coordinate,
column letter along x and row number along y.

- `3x3`: nine Pillars A-1 … C-3 at 224, 672 and 1120 m on each axis, 448 m apart, B-2 at the Store
  centre; nine 448 × 448 m Sectors whose boundaries run on Chunk boundaries.
- `2x2`: four Pillars A-1, B-1, A-2, B-2 at 336 and 1008 m on each axis, 672 m apart; four
  672 × 672 m Sectors with boundaries at 0 m (the Seam) and 672 m, on the 16 m aisle grid but
  mid-Chunk. Because the Store wraps, a Pillar's east and west neighbour is the same Pillar.
- `3x3L` (3 × 3 large): the larger 1920 m Store (30 × 30 Chunks, `StoreSpace.Large`); nine
  Pillars at 320, 960 and 1600 m on each axis, 640 m apart (close to the 2 × 2 spacing), B-2 at
  the Store centre; nine 640 × 640 m Sectors on Chunk boundaries. Department sites and Fog bank
  formation scale with the floor area, so Departments keep their size and the Store is as foggy;
  the same seed gives other Departments and Furniture than on the 1344 m Store.
- `3x3L-corners`: the `3x3L` Store and Sectors with Pillars only in A-1, C-1, A-3 and C-3. Across
  the Seams the four stand together as a 640 m square; the five empty Sectors form 640 m wide
  corridors between such squares, and no place is more than ~905 m from a Pillar.
- `3x3L-diamond`: the `3x3L` Store and Sectors with Pillars only in B-1, A-2, C-2 and B-3, around
  the empty B-2; the four empty corner Sectors join across the Seams into one 1280 m empty square,
  whose middle (the Seam corner) is ~1012 m from the nearest Pillar.

Empty Sectors keep their grid name and are furnished like any other floor. Pillar ids count the
Pillars, so on the four-Pillar layouts they are 0–3, in Sector order.

Furniture generation keeps each footprint and a 3 m (one-aisle) walkway ring around it clear.
`Sectors` lists the Sectors, named by grid coordinate like the Pillars at their centres, and `SectorAt(position)` (on
the snapshot and the facade) answers which Sector a wrapped position is in; a position on a
boundary belongs to the Sector east or south of it. `NearestPillar(position)` uses the shortest
wrapped distance (never more than ~317 m in `3x3`, ~475 m in `2x2`, ~453 m in `3x3L`, ~905 m in `3x3L-corners`, ~1012 m in `3x3L-diamond`), and `OutsidePillars(position)`
moves a position that lies inside a Pillar onto its walkway.

`PillarRenderer` draws the Pillars and every Ghost copy of them within 1200 m, with no emission or lights.
Pillars are always solid. The coordinate is painted on all four sides at 2, 28, 60 and 100 m, and
the shaft darkens upwards into the haze. In the F4 Store overview the Pillars are cut to 16 m bases
and the floor shows the active layout's Sector boundaries and labels; the normal view shows no
boundaries. The top-right panel's **Pillars: 3×3 | 2×2 | 3×3 large | Corners | Diamond** row marks the active layout;
choosing another one regenerates the Store with the same seed (the Store clock restarts, the camera
stays, wrapping back onto a smaller Store, and a focus that would end up inside a Pillar moves onto
its walkway).

For fixed visual checks, add `--pause --pillar=B-2 --distance=60 --clear-weather` to a dev capture;
`--pillars=<short name>` picks the start-up layout, `--pillar` takes a grid coordinate and focuses
just south of that Pillar's walkway (B-2 has none on the corners and diamond layouts), and `--choose-pillars=2x2` (or another layout) presses the
panel button after a second.
Evidence and remaining human checks are in [ticket 06a verification](docs/ticket-06a-verification.md)
and [ticket 06b verification](docs/ticket-06b-verification.md).

## Landmarks and Points of interest

`Landmarks` on the facade snapshot lists the seed's Landmarks (each with its rectangle, Sector and `Lookouts`); their
Furniture carries a `LandmarkId`. `GroundAt(position)` gives a ground property a Landmark leaves (darkness in the
dead zone, water in the fountain court), `StoreLightLevelAt(position)` the Store light there, `LampLevel(lamp)` how
bright a Lamp is now, and `ClockReading(clock)` what a clock on the clock wall shows. `PointsOfInterest` lists the
common and rare Points of interest, whose Furniture carries a `PointOfInterestId`; the Abandoned scaffold also records
its `PillarId` and its `Lookout`. `BallPitNetwork` holds the three Ball pits, and `StaffExitsFrom(id)` returns the
Ball pits the Staff can pass to from one: the other two at Closing time, none while the Store is open.
`WorldConfig.LandmarkShowcase` builds a Store with every Landmark side by side and no Points of interest.
`ScenarioBuilder.RemoveFurniture` lets tests take Furniture away.

## Weather

Weather: the core exposes `FogBanks`, `Wind` and `FogDensityAt(position)` through the facade's snapshots; `Simulation.FogDensityAt` reads the latest snapshot. Banks grow, drift across Seams and dissipate with formed/dissipated domain events. Runtime randomness is independent of Store generation; tests supply `RuntimeRngSeed`. Natural formation reserves at most 45% of the floor at the banks' largest sizes (including spreading during dissipation), leaving clear stretches even if banks overlap. A new Store starts with 12 Store hours of weather warm-up. These are reversible tuning choices; no new ADR is needed.

The presentation interpolates each bank's position along the shortest wrapped path and draws volumetric ghost copies. The air has no base fog or haze; only the Edge fade (a depth fade over roughly 480–550 m, none in the overview) hides distant chunk culling, and low fog domes cover Furniture while leaving room for future giant Landmarks. The benchmark places the same 160 m radius Fog bank over its camera path after each lighting change, with MSAA 4x and TAA enabled.

Dev weather tools (debug builds): F6 places a Fog bank at the camera focus. `--clear-weather` removes banks and disables natural formation; `--fog-bank=x,y[,radius]` places one through the facade's scenario builder. For a fixed capture, pass both arguments in that order. Scenario placement deliberately permits arbitrary coverage for testing; the coverage budget applies to natural formation.

## Controls

- Camera: WASD or arrow keys pan, Q/E rotate, mouse wheel or R/F zoom; middle-drag pans, right-drag rotates. The Store wraps: pan in any direction forever.
- F3: wrap overlay (chunk lines in blue, seams in orange, marker posts on the seams with their ghost copies, and the focus position and chunk).
- Seed (top right): type a number or any text and press Enter or Generate for a new Store (the same seed always gives the same Store); Random picks one. The panel also names the Department under the camera.
- Time: Space toggles pause; 1-5 select 1x, 2x, 4x, 8x, 16x (or use the buttons). One Store day lasts 10 minutes at 1x.
