# SCP-3008 Simulation

An observer-only simulation of survivor communities inside an endless furniture store (inspired by SCP-3008, CC BY-SA 3.0). The spec is GitHub issue #1; domain terms are in [GLOSSARY.md](GLOSSARY.md) and decisions in [docs/adr](docs/adr).

## Layout

- `src/Scp3008.Core`: the engine-independent simulation core (plain C#, no Godot). `Simulation` is its single facade: create a world from a seed, advance time, read snapshots, subscribe to domain events, and run the clock on its own thread. `StoreSpace` (on the snapshot) is the wrapping Store geometry: wrapped positions, shortest-wrapped distance and direction, and chunks.
- `tests/Scp3008.Core.Tests`: headless xUnit tests that drive the facade.
- `game`: the Godot 4 (.NET, Forward+) presentation layer, which only reads from the core.

## Build and test

Requires the .NET 8+ SDK and Godot 4.7 .NET.

```sh
dotnet test Scp3008.sln
godot --headless --path game --build-solutions --quit   # build the Godot project
godot --path game                                        # run the game
```

Unattended check: `godot --path game -- --capture=shot.png --capture-after=5 --fps-report` saves a screenshot, prints the average fps and quits. More dev arguments: `--focus=x,y` starts the camera at a Store position, `--pan=vx,vy` pans it (metres per second) and reports seam crossings and the largest per-frame step, `--wrap-debug` turns on the wrap overlay, and `--no-focus-wrap` keeps the camera off-floor (to compare two images of the same place).

## Controls

- Camera: WASD or arrow keys pan, Q/E rotate, mouse wheel or R/F zoom; middle-drag pans, right-drag rotates. The Store wraps: pan in any direction forever.
- F3: wrap overlay (chunk lines in blue, seams in orange, marker posts on the seams with their ghost copies, and the focus position and chunk).
- Time: Space toggles pause; 1-5 select 1x, 2x, 4x, 8x, 16x (or use the buttons). One Store day lasts 10 minutes at 1x.
