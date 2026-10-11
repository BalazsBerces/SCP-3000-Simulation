# SCP-3008 Simulation

An observer-only simulation of survivor communities inside an endless furniture store (inspired by SCP-3008, CC BY-SA 3.0). The spec is GitHub issue #1; domain terms are in [GLOSSARY.md](GLOSSARY.md) and decisions in [docs/adr](docs/adr).

## Layout

- `src/Scp3008.Core`: the engine-independent simulation core (plain C#, no Godot). `Simulation` is its single facade: create a world from a seed, advance time, read snapshots, subscribe to domain events, and run the clock on its own thread. `StoreSpace` (on the snapshot) is the wrapping Store geometry: wrapped positions, shortest-wrapped distance and direction, and chunks. The seed generates the Store (`Generation/`): `Departments` on the snapshot says which Department covers each part of the floor, and `Furniture` lists every piece of Furniture with its type, position, elevation and rotation (sizes in `FurnitureCatalog`, where a `LightRadius` marks the Lamps). `StoreHours` defines the day cycle: Closing time from 22:00 to 08:00 and the Store light level at any time of day (`Phase` and `StoreLightLevel` on the snapshot); the core emits `ClosingTimeBegan` and `StoreReopened` events.
- `tests/Scp3008.Core.Tests`: headless xUnit tests that drive the facade.
- `game`: the Godot 4 (.NET, Forward+) presentation layer, which only reads from the core. Furniture meshes are built in code from simple parts (`FurnitureMeshes`) and drawn with one MultiMesh per Furniture type and chunk; chunks far from the camera draw coarse meshes (level of detail), chunks beyond the haze are not drawn, and tall solid Furniture (wardrobes, fridges, shelving back panels) feeds occlusion culling near the camera. Each Department type has its own floor texture. `LampLights` gives the Lamps nearest the camera real lights within the render budget of ADR 0004: at most 48 unshadowed clustered lights, of which only the 4 nearest cast (dual-paraboloid) shadows; the rest of the Lamps only glow. `StoreAtmosphere` blends light, distance fog and a low volumetric haze layer (thick at floor level, thin above) between the open Store and Closing time; `AmbientAudio` synthesises the fluorescent hum, the Closing-time drone and distant footsteps in code.
- `game/assets/textures`: CC0 PBR textures from Poly Haven and ambientCG (see `CREDITS.md`), downscaled to 512 px by `scripts/fetch-assets.py` (needs Pillow).

## Build and test

Requires the .NET 8+ SDK and Godot 4.7 .NET.

```sh
dotnet test Scp3008.sln
godot --headless --path game --build-solutions --quit   # build the Godot project
godot --path game                                        # run the game
```

Benchmark (ADR 0004): `godot --path game res://Benchmark.tscn` flies a fixed camera path over Store 3008 with the clock paused, once in open lighting (12:00) and once at Closing time (23:00), with vsync off at 1920×1080. It prints the average and 1%-low fps (the mean rate over the slowest 1% of frames) for each lighting and exits with code 0 if both averages reach 120 fps, 1 if not. Add `-- --benchmark-seconds=s` to change the measuring time per lighting (default 20) and `--benchmark-shots=dir` to save screenshots of the path's close and far views.

Unattended check: `godot --path game -- --capture=shot.png --capture-after=5 --fps-report` saves a screenshot, prints the average fps and quits. More dev arguments: `--focus=x,y` starts the camera at a Store position, `--pan=vx,vy` pans it (metres per second) and reports seam crossings and the largest per-frame step, `--distance=m` sets the zoom distance, `--wrap-debug` turns on the wrap overlay, `--no-focus-wrap` keeps the camera off-floor (to compare two images of the same place), `--seed=N` sets the first Store's seed, `--start-time=HH:MM` starts the Store clock at that time (e.g. `23:00` for Closing time), `--eye-level` captures from a Villager's eye height to check the ground-level haze, and `--enter-seed=text` types a seed into the seed input after a second (the capture timer restarts when the new Store is shown).

## Weather

Weather: the core exposes `FogBanks`, `Wind` and `FogDensityAt(position)` through the facade's snapshots; `Simulation.FogDensityAt` reads the latest snapshot. Banks grow, drift across Seams and dissipate with formed/dissipated domain events. Runtime randomness is independent of Store generation; tests supply `RuntimeRngSeed`. Natural formation reserves at most 45% of the floor at the banks' largest sizes (including spreading during dissipation), leaving clear stretches even if banks overlap. A new Store starts with 12 Store hours of weather warm-up. These are reversible tuning choices; no new ADR is needed.

The presentation interpolates each bank's position along the shortest wrapped path and draws volumetric ghost copies. Thin global haze supplies depth and hides distant chunk culling; low fog domes cover Furniture while leaving room for future giant Landmarks. The benchmark places the same 160 m radius Fog bank over its camera path after each lighting change, with MSAA 4x and TAA enabled.

Dev weather tools (debug builds): F6 places a Fog bank at the camera focus. `--clear-weather` removes banks and disables natural formation; `--fog-bank=x,y[,radius]` places one through the facade's scenario builder. For a fixed capture, pass both arguments in that order. Scenario placement deliberately permits arbitrary coverage for testing; the coverage budget applies to natural formation.

## Controls

- Camera: WASD or arrow keys pan, Q/E rotate, mouse wheel or R/F zoom; middle-drag pans, right-drag rotates. The Store wraps: pan in any direction forever.
- F3: wrap overlay (chunk lines in blue, seams in orange, marker posts on the seams with their ghost copies, and the focus position and chunk).
- Seed (top right): type a number or any text and press Enter or Generate for a new Store (the same seed always gives the same Store); Random picks one. The panel also names the Department under the camera.
- Time: Space toggles pause; 1-5 select 1x, 2x, 4x, 8x, 16x (or use the buttons). One Store day lasts 10 minutes at 1x.
