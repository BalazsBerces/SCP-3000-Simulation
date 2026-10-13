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

Codex:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-codex.ps1 -Max 1
powershell -ExecutionPolicy Bypass -File .\scripts\run-tickets-codex.ps1
```
