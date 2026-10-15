# How to run the simulation

Double-click `scripts\run-game.cmd`, or from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1           # build, then start the game
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -Editor   # build, then open the Godot editor
powershell -ExecutionPolicy Bypass -File .\scripts\run-game.ps1 -NoBuild  # start without rebuilding
```

F4 toggles the Store overview.

# How to run the ticket runners

Run from the repo root, with a clean working tree. `-Max` sets how many tickets to run; without
it, the runner continues until every ticket is done.

Claude Code:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets.ps1 -Max 1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets.ps1
```

To start Claude Code at the next 02:55 in Europe/Budapest:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-0255.ps1
```

This copy accepts the same parameters as `run-tickets.ps1`. Keep the terminal open and the
computer awake until it starts; Ctrl+C cancels the wait. If today's 02:55 has passed, it waits
until tomorrow. Daylight saving time is handled automatically: a missing spring 02:55 is
skipped, and during the autumn repeated hour it uses the next occurrence of 02:55.

Codex:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-codex.ps1 -Max 1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-codex.ps1
```
