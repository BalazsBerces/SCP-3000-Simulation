# SCP-3008 Simulation

An observer-only simulation of survivor communities inside an endless furniture store (inspired by SCP-3008, CC BY-SA 3.0). The spec is GitHub issue #1; domain terms are in [GLOSSARY.md](GLOSSARY.md) and decisions in [docs/adr](docs/adr).

## Layout

- `src/Scp3008.Core`: the engine-independent simulation core (plain C#, no Godot). `Simulation` is its single facade: create a world from a seed, advance time, read snapshots, subscribe to domain events, and run the clock on its own thread. `StoreSpace` (on the snapshot) is the wrapping Store geometry: wrapped positions, shortest-wrapped distance and direction, and chunks. The seed generates the Store (`Generation/`): `Departments` on the snapshot says which Department covers each part of the floor, and `Furniture` lists every piece of Furniture with its type, position, elevation and rotation (sizes in `FurnitureCatalog`). `StoreHours` defines the day cycle: Closing time from 22:00 to 08:00 and the Store light level at any time of day (`Phase` and `StoreLightLevel` on the snapshot); the core emits `ClosingTimeBegan` and `StoreReopened` events.
- `tests/Scp3008.Core.Tests`: headless xUnit tests that drive the facade.
- `game`: the Godot 4 (.NET, Forward+) presentation layer, which only reads from the core. Furniture meshes are built in code from simple parts (`FurnitureMeshes`) and drawn with one MultiMesh per Furniture type and chunk; each Department type has its own floor texture. `StoreAtmosphere` blends light, distance fog and a low volumetric haze layer (thick at floor level, thin above) between the open Store and Closing time; `AmbientAudio` synthesises the fluorescent hum, the Closing-time drone and distant footsteps in code.
- `game/assets/textures`: CC0 PBR textures from Poly Haven and ambientCG (see `CREDITS.md`), downscaled to 512 px by `scripts/fetch-assets.py` (needs Pillow).

## Build and test

Requires the .NET 8+ SDK and Godot 4.7 .NET.

```sh
dotnet test Scp3008.sln
godot --headless --path game --build-solutions --quit   # build the Godot project
godot --path game                                        # run the game
```

Unattended check: `godot --path game -- --capture=shot.png --capture-after=5 --fps-report` saves a screenshot, prints the average fps and quits. More dev arguments: `--focus=x,y` starts the camera at a Store position, `--pan=vx,vy` pans it (metres per second) and reports seam crossings and the largest per-frame step, `--distance=m` sets the zoom distance, `--wrap-debug` turns on the wrap overlay, `--no-focus-wrap` keeps the camera off-floor (to compare two images of the same place), `--seed=N` sets the first Store's seed, `--start-time=HH:MM` starts the Store clock at that time (e.g. `23:00` for Closing time), `--eye-level` captures from a Villager's eye height to check the ground-level haze, and `--enter-seed=text` types a seed into the seed input after a second (the capture timer restarts when the new Store is shown).

## Controls

- Camera: WASD or arrow keys pan, Q/E rotate, mouse wheel or R/F zoom; middle-drag pans, right-drag rotates. The Store wraps: pan in any direction forever.
- F3: wrap overlay (chunk lines in blue, seams in orange, marker posts on the seams with their ghost copies, and the focus position and chunk).
- Seed (top right): type a number or any text and press Enter or Generate for a new Store (the same seed always gives the same Store); Random picks one. The panel also names the Department under the camera.
- Time: Space toggles pause; 1-5 select 1x, 2x, 4x, 8x, 16x (or use the buttons). One Store day lasts 10 minutes at 1x.
