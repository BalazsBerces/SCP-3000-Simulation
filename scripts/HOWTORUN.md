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
