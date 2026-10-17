# How to run the simulation

Double-click `scripts\run-game.cmd`, or from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1           # build, then start the game
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -Editor   # build, then open the Godot editor
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -NoBuild  # start without rebuilding
```

F4 toggles the Store overview.

# How to run the ticket runners

From the repo root. `-Max N` runs N tickets; without it, all of them. Details are in each script's header.

## v3 (v1 with more models: easy tickets on Gemini, hard and stage-end tickets on Sonnet)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v3.ps1 -DryRun            # show the next ticket and its agent
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v3.ps1 -Max 1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v3.ps1 -NoStagePause      # don't pause at stage ends
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v3.ps1 -EasyAgent claude  # easy tickets on Sonnet too
```

Ctrl+C stops it. The working tree must be clean before it starts. Per ticket: one CSV row in `.ticket-logs\v3-runs.csv`.
Tests: `scripts\tests\run-tickets-v3\run-stub-tests.ps1`.

## v2 (Opus plans and reviews, Nemotron/Gemini/Codex implement)

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v2.ps1 -DryRun         # show the next ticket, run nothing
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v2.ps1 -Max 1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-v2.ps1 -NoStagePause   # don't pause at stage ends
powershell -ExecutionPolicy Bypass -File .\scripts\stop-tickets-v2.ps1                # stop it
powershell -ExecutionPolicy Bypass -File .\scripts\stop-tickets-v2.ps1 -Clean         # stop and undo the unfinished ticket
```

Logs: `.ticket-logs\`. Tests: `scripts\tests\run-tickets-v2\run-stub-tests.ps1` and `stop-tests.ps1`.

## v1

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets.ps1 -Max 1        # Claude Code
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-0255.ps1          # Claude Code, starting at 02:55
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-codex.ps1 -Max 1  # Codex
```

The 02:55 variant waits for the next 02:55 Budapest time; keep the computer awake, Ctrl+C cancels.
