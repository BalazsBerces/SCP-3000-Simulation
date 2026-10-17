# How to run the simulation

Double-click `scripts\run-game.cmd`, or from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1           # build, then start the game
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -Editor   # build, then open the Godot editor
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -NoBuild  # start without rebuilding
```

F4 toggles the Store overview.

# How to run the ticket runners

Run them from the repo root. The working tree must be clean before a ticket starts (`.claude\` excepted). Ctrl+C stops a runner. `-Max N` runs N tickets, and without it the runner works through all of them. Each script's header has the details. Logs go to `.ticket-logs\`.

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
- **Out of quota:** the agent's tickets go to Sonnet for the rest of the run.
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
