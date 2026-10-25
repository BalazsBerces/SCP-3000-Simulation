# How to run the simulation

Double-click `scripts\run-game.cmd`, or from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1           # build, then start the game
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -Editor   # build, then open the Godot editor
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -NoBuild  # start without rebuilding
```

F4 toggles the Store overview.

# How to run the ticket runners

Run them from the repo root. Ctrl+C stops a runner. Uncommitted work may be in the tree: a ticket leaves it alone and keeps it out of its commit. A ticket that stops unfinished leaves its own work uncommitted, and the next attempt at that ticket takes it over (no reset or commit needed). `-Max N` runs N tickets, and without it the runner works through all of them. Each script's header has the details. Logs go to `.ticket-logs\`.

## v4 (current): Gemini on easy tickets, Codex on hard and stage-end tickets

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -DryRun            # show the next ticket and its agent
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -Max 1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -NoStagePause      # don't pause at stage ends
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -HardAgent claude  # hard tickets on Sonnet (as in v3)
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -EasyAgent claude  # easy tickets on Sonnet
```

- **Codex:** implements, commits, pushes and closes the issue itself. It uses its own skills in `~/.codex/skills`, and the runner warns if one is missing or older than the Claude version.
- **Gemini:** never commits. The runner checks its work first (build, the core tests in Release, the headless Godot build), then commits it.
- **Out of quota:** the run stops; the ticket's work stays uncommitted and its next attempt takes it over.
- **Logs:** one CSV row per ticket in `.ticket-logs\v4-runs.csv`.
- **Tests:** `scripts\tests\run-tickets-v4\run-stub-tests.ps1`.

## v3: same as v4, but hard tickets go to Sonnet

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v3.ps1 -Max 1
```

Same switches as v4, apart from `-HardAgent`. One CSV row per ticket goes to `.ticket-logs\v3-runs.csv`. Tests: `scripts\tests\run-tickets-v3\run-stub-tests.ps1`.

## Older runners

| Script | What it does |
|---|---|
| `run-tickets-v2.ps1` | Opus plans and reviews, and Nemotron, Gemini or Codex implement. Stop it with `stop-tickets-v2.ps1`; add `-Clean` to also undo the unfinished ticket. |
| `run-tickets.ps1` | v1: Claude Code only. |
| `run-tickets-0255.ps1` | v1, starting at the next 02:55 Budapest time. Keep the computer awake; Ctrl+C cancels. |
| `run-tickets-codex.ps1` | v1: Codex only. |

## Villager sandbox (09a)

```powershell
.\scripts\run-game.ps1 -- --villager-sandbox
.\scripts\run-game.ps1 -- --villager-sandbox --sandbox-preset=starving
.\scripts\run-game.ps1 -- --villager-sandbox '--sandbox-preset=closing soon'
.\scripts\run-game.ps1 -- --villager-sandbox '--sandbox-preset=staff nearby'
```

The sandbox is a 128 × 128 m wrapping Store with one Pillar. It defaults to `scavenge`.
The dev debug panel selects `empty`, `scavenge`, `starving`, `closing soon` or `staff nearby`;
selection replaces the world and resets its people, Furniture, Goods and clock, retaining time speed.
Space pauses; 1–5 select 1x–16x, 6 selects 60x, and the Step button or period pauses and advances
one tick (7.2 Store seconds). V cycles camera follow; F7 places a Villager, F8 sends the last
Villager to the camera focus, F9 simulates one Store hour. The debug panel requires a dev build.

Presets live in `game/scenarios/villager-sandbox/*.json`, embedded in the core at build time.
To add one, copy a JSON file, rename it, edit its contents and rebuild. It appears automatically
in the debug selector and the facade test that runs every preset for one Store day at 60x:

```powershell
dotnet test tests/Scp3008.Core.Tests -c Release --filter FullyQualifiedName~VillagerSandboxTests
```

Each file has `time` (`HH:mm`), `furniture` (type, `[x,y]` position, optional rotation in radians
and stock entries with Goods kind and amount), `bands` (arrays of member positions), `gear`
(kind and position), and `staff` (position and a non-empty `patrol` array of destinations).
Collection fields default to empty. Coordinates are metres and wrap; names match the core enums.
Only Grocery shelves can hold stock in the sandbox. Found tools currently support `HexKey` and
`Screwdriver`, shown in the debug panel. Bands record membership; needs and autonomous scavenging
arrive in later tickets. Staff follow their looping patrol at Closing time and stand while open;
their sandbox figure reuses the existing faceless mannequin pending the Staff presentation ticket.

Headless fixtures use `ScenarioBuilder.LoadVillagerSandbox("scavenge")` and `Simulation.Advance(...)`.
For an unembedded custom file, pass `File.ReadAllText(path)` to `ScenarioBuilder.LoadVillagerSandboxJson`.
The core has no Godot dependency. An unattended windowed UI check is available after Godot's `--`:
`--villager-sandbox --sandbox-check=<report.txt>` (quits with nonzero status on failure).
