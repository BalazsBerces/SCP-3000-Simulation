# How to run

From the repo root. Ctrl+C stops anything. Ticket logs go to `.ticket-logs\`.

## Game

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1           # build, then start (or double-click run-game.cmd)
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -Editor   # build, then open the Godot editor
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -NoBuild  # start without rebuilding
```

F4: Store overview.

## Ticket runners

Every runner takes `-Max N` (run N tickets) and `-NoStagePause` (don't pause at stage ends); v4, v5 and Codex also take `-DryRun` (show the next ticket only).

```powershell
# v4: Gemini on easy tickets, Codex on hard and stage-end ones
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -Max 1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -HardAgent claude  # hard tickets on Sonnet
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v4.ps1 -EasyAgent claude  # easy tickets on Sonnet

# Codex only: every ticket on gpt-6.1-sol, effort medium, with Ponytail
# (one-time: codex -c 'plugins."ponytail@ponytail".enabled=true', then /hooks and trust Ponytail's hooks)
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-codex.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-codex.ps1 -Max 1

# v5: Sonnet only
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v5.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v5.ps1 -Max 1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v5.ps1 -Effort high -StageEffort high

# v3: like v4, hard tickets on Sonnet
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v3.ps1 -Max 1

# Older
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v2.ps1         # stop: stop-tickets-v2.ps1 [-Clean]
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets.ps1            # v1, Claude Code only
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-0255.ps1       # v1, starts at 02:55 Budapest time
```

## Runner tests

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\tests\run-tickets-v4\run-stub-tests.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\tests\run-tickets-v3\run-stub-tests.ps1
```

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
