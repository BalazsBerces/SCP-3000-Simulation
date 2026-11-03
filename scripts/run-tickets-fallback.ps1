<#
.SYNOPSIS
  Runs the tickets on Claude (run-tickets.ps1) until its usage limit runs out, then on Codex
  (run-tickets-codex.ps1) until its quota runs out too.

.DESCRIPTION
  Claude runs with -MaxLimitResumes 0, so it does not wait for the limit reset: when the limit stops a ticket,
  the runner exits with code 2 and Codex takes over. The half-done ticket's work stays uncommitted, and the
  Codex runner picks it up as an unfinished attempt (both runners share .ticket-logs/).
  Any other failure (a blocked ticket, a crash) stops the whole run without switching.
  Each runner runs in its own PowerShell process, so stage pauses (Read-Host) still work.

.EXAMPLE
  .\scripts\run-tickets-fallback.ps1                              # Claude, then Codex
  .\scripts\run-tickets-fallback.ps1 -NoStagePause                # never pause at stage ends
  .\scripts\run-tickets-fallback.ps1 -ClaudeArgs '-Model','opus'  # extra arguments for one runner
#>
param(
  [switch]$NoStagePause,
  [string[]]$ClaudeArgs = @(),
  [string[]]$CodexArgs = @()
)

$ErrorActionPreference = 'Stop'

function Invoke-Runner([string]$Name, [string]$Script, [string[]]$Extra) {
  $runnerArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f (Join-Path $PSScriptRoot $Script))) + $Extra
  if ($NoStagePause) { $runnerArgs += '-NoStagePause' }
  Write-Host "`n##### $Name runner: $Script $($Extra -join ' ')" -ForegroundColor Magenta
  # In the same console, not through the pipeline: a captured stdout would swallow the runner's live output
  # (and its colours) into this function's return value.
  $p = Start-Process powershell.exe -ArgumentList $runnerArgs -NoNewWindow -Wait -PassThru
  $p.ExitCode
}

$exit = Invoke-Runner 'Claude' 'run-tickets.ps1' (@('-MaxLimitResumes', '0') + $ClaudeArgs)
if ($exit -eq 0) { Write-Host 'Claude finished; Codex is not needed.' -ForegroundColor Green; exit 0 }
if ($exit -ne 2) { Write-Host "Claude runner failed (exit $exit); not switching to Codex." -ForegroundColor Red; exit $exit }

Write-Host 'Claude usage limit reached; switching to Codex.' -ForegroundColor Yellow
$exit = Invoke-Runner 'Codex' 'run-tickets-codex.ps1' $CodexArgs
switch ($exit) {
  0 { Write-Host 'Codex finished.' -ForegroundColor Green }
  2 { Write-Host 'Codex is out of quota too. Stopping.' -ForegroundColor Yellow }
  default { Write-Host "Codex runner failed (exit $exit)." -ForegroundColor Red }
}
exit $exit
