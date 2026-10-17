<#
.SYNOPSIS
  Stops ticket runner v2 (scripts/run-tickets-v2.ps1) and, with -Clean, undoes the interrupted ticket.

.DESCRIPTION
  Stop: kills every running run-tickets-v2.ps1 of this repository with its whole process tree (Opus, executors,
  dotnet, Godot), and the leftover children of an already stopped runner (claude started with this repository's
  ticket-settings-v2.json, executors working on .ticket-work/<label>/). The calling process chain (this script,
  its shell, the Claude Code session that runs it) is never stopped.

  Then it reports what the interrupted ticket left behind, from .ticket-logs/v2-current.json (written by the
  runner at ticket start, removed when a ticket ends on its own).

  -Clean undoes that ticket:
    - the main tree goes back to its state before the ticket: changes beyond the recorded baseline files are
      restored from HEAD or, when new, deleted (.claude/ is never touched);
    - .ticket-work/<label>/, .ticket-logs/<label>/ and the ticket's .ticket-logs/<label>-* files are removed
      (-KeepLogs keeps the logs);
    - the executor rotation goes back to its value before the ticket.
  It refuses (exit code 2) when HEAD moved since the ticket started, i.e. the reviewer already committed:
  commits are never undone. The executor clone is left alone; the runner resets it at every ticket start.

.EXAMPLE
  .\scripts\stop-tickets-v2.ps1            # stop, then show what -Clean would undo
  .\scripts\stop-tickets-v2.ps1 -Clean     # stop and undo the interrupted ticket
#>
param([switch]$Clean, [switch]$KeepLogs)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
$logRoot = Join-Path $root '.ticket-logs'
$workRoot = Join-Path $root '.ticket-work'
$currentFile = Join-Path $logRoot 'v2-current.json'
$rotationFile = Join-Path $logRoot 'v2-rotation.json'
$runnerFile = Join-Path $PSScriptRoot 'run-tickets-v2.ps1'
$settingsFile = Join-Path $PSScriptRoot 'ticket-settings-v2.json'
$utf8 = New-Object Text.UTF8Encoding $false

function Get-ShortCommand([string]$CommandLine) {
  $s = ($CommandLine -replace '\s+', ' ').Trim()
  if ($s.Length -gt 110) { $s.Substring(0, 107) + '...' } else { $s }
}

# Same as the runner: changed paths relative to the root, forward slashes, .claude/ ignored.
function Get-ChangedPaths {
  $raw = git -C $root -c core.quotePath=false status --porcelain -uall --no-renames -z
  if ($LASTEXITCODE -ne 0) { throw "git status failed in $root" }
  if (-not $raw) { return @() }
  @((@($raw) -join "`0") -split "`0" | Where-Object { $_.Length -gt 3 } | ForEach-Object { $_.Substring(3) } |
    Where-Object { $_ -notlike '.claude/*' })
}

# ---------------------------------------------------------------------------------------------------
# Stop

$all = @(Get-CimInstance Win32_Process)
$byId = @{}
foreach ($p in $all) { $byId[[int]$p.ProcessId] = $p }

$protected = @{}
$id = [int]$PID
while ($byId.ContainsKey($id) -and -not $protected.ContainsKey($id)) { $protected[$id] = $true; $id = [int]$byId[$id].ParentProcessId }

function Test-Runner($p) {
  $cl = "$($p.CommandLine)"
  if ($cl -notmatch '(?i)run-tickets-v2\.ps1') { return $false }
  # An absolute script path must be this repository's (the stub tests run copies elsewhere).
  $m = [regex]::Match($cl, '(?i)"([a-z]:[\\/][^"]*?run-tickets-v2\.ps1)"|([a-z]:[\\/][^"''\s]*run-tickets-v2\.ps1)')
  if ($m.Success) {
    $path = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
    return ([IO.Path]::GetFullPath($path) -eq [IO.Path]::GetFullPath($runnerFile))
  }
  $true
}

function Test-RunnerChild($p) {
  $cl = "$($p.CommandLine)"
  ($cl.IndexOf($settingsFile, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or ($cl -match '\.ticket-work[\\/]\d\d[a-z]?[\\/]')
}

# Children first; a "child" older than its parent belongs to a reused process id and is skipped.
function Get-Tree($p, $seen) {
  $pidNum = [int]$p.ProcessId
  if ($seen.ContainsKey($pidNum)) { return }
  $seen[$pidNum] = $true
  foreach ($c in $all) {
    if ([int]$c.ParentProcessId -eq $pidNum -and $c.CreationDate -ge $p.CreationDate) { Get-Tree $c $seen }
  }
  $p
}

$roots = @($all | Where-Object { -not $protected.ContainsKey([int]$_.ProcessId) -and ((Test-Runner $_) -or (Test-RunnerChild $_)) })
$seen = @{}
$targets = @(@(foreach ($r in $roots) { Get-Tree $r $seen }) | Where-Object { -not $protected.ContainsKey([int]$_.ProcessId) })

if ($targets.Count) {
  Write-Host "Stopping $($targets.Count) process(es):" -ForegroundColor Yellow
  foreach ($p in $targets) {
    Write-Host ("  {0,6} {1}  {2}" -f $p.ProcessId, $p.Name, (Get-ShortCommand $p.CommandLine)) -ForegroundColor DarkGray
    Stop-Process -Id ([int]$p.ProcessId) -Force -ErrorAction SilentlyContinue
  }
  $deadline = (Get-Date).AddSeconds(20)
  do {
    $alive = @($targets | Where-Object { Get-Process -Id ([int]$_.ProcessId) -ErrorAction SilentlyContinue })
    if ($alive.Count) { Start-Sleep -Milliseconds 500 }
  } while ($alive.Count -and (Get-Date) -lt $deadline)
  if ($alive.Count) {
    Write-Host "Still running: $(($alive | ForEach-Object { "$($_.ProcessId) $($_.Name)" }) -join ', ')" -ForegroundColor Red
    if ($Clean) { Write-Host 'Not cleaning while runner processes are alive.' -ForegroundColor Red; exit 2 }
  } else { Write-Host 'Runner stopped.' -ForegroundColor Green }
} else {
  Write-Host 'No ticket runner process is running.' -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------
# What the interrupted ticket left behind

if (-not (Test-Path $currentFile)) {
  Write-Host 'No interrupted ticket (.ticket-logs/v2-current.json is missing).'
  $changes = @(Get-ChangedPaths)
  if ($changes.Count) { Write-Host "Uncommitted changes in the main tree (not attributable to a ticket, left alone): $($changes -join ', ')" }
  exit 0
}

$state = Get-Content $currentFile -Raw -Encoding UTF8 | ConvertFrom-Json
$label = "$($state.label)"
if ($label -notmatch '^\d\d[a-z]?$') { throw "Unexpected ticket label '$label' in $currentFile." }
$started = if ($state.started -is [datetime]) { $state.started }
  else { [datetime]::Parse($state.started, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind) }
$known = @{}
foreach ($p in @($state.baseline)) { if ($p) { $known[$p.ToLowerInvariant()] = $true } }
$foreign = @(Get-ChangedPaths | Where-Object { -not $known.ContainsKey($_.ToLowerInvariant()) })
$head = (git -C $root rev-parse HEAD).Trim()
$headMoved = $head -ne $state.head
$logFiles = @(Get-ChildItem $logRoot -File -Filter "$label-*" -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $started.ToLocalTime() })
$dirs = @((Join-Path $workRoot $label), (Join-Path $logRoot $label) | Where-Object { Test-Path $_ })
$rotationText = if ($state.rotationBefore) { $state.rotationBefore } else { '(none: the file is removed)' }

Write-Host "`nInterrupted ticket: $($state.title) (#$($state.issue)), started $($started.ToLocalTime().ToString('yyyy-MM-dd HH:mm'))"
Write-Host "  Baseline files (dirty before the ticket, kept): $(if ($known.Count) { @($state.baseline) -join ', ' } else { '(none)' })"
Write-Host "  Ticket changes in the main tree: $(if ($foreign.Count) { $foreign -join ', ' } else { '(none)' })"
Write-Host "  Work and log folders: $(if ($dirs.Count) { ($dirs | ForEach-Object { $_.Substring($root.Length + 1) }) -join ', ' } else { '(none)' })"
Write-Host "  Log files of this ticket: $(if ($logFiles.Count) { ($logFiles | ForEach-Object { $_.Name }) -join ', ' } else { '(none)' })"
Write-Host "  Rotation before the ticket: $rotationText"
if ($headMoved) {
  Write-Host "  HEAD moved since the ticket started ($($state.head.Substring(0, 7)) -> $($head.Substring(0, 7))): the reviewer already committed." -ForegroundColor Yellow
}

if (-not $Clean) { Write-Host "`nRun with -Clean to undo this ticket." -ForegroundColor Cyan; exit 0 }
if ($headMoved) {
  Write-Host "`nNot cleaning: commits are never undone. Check git log, origin and issue #$($state.issue) by hand." -ForegroundColor Red
  exit 2
}

# ---------------------------------------------------------------------------------------------------
# Clean

foreach ($p in $foreign) {
  $full = Join-Path $root $p
  if (git -C $root ls-tree --name-only HEAD -- $p) { git -C $root checkout -q HEAD -- $p }
  else {
    git -C $root rm -q --cached --ignore-unmatch -- $p | Out-Null
    Remove-Item -LiteralPath $full -Force -ErrorAction SilentlyContinue
    # Empty folders the ticket created go too.
    $dir = Split-Path -Parent $full
    while ($dir.Length -gt $root.Length -and (Test-Path $dir) -and -not (Get-ChildItem -LiteralPath $dir -Force)) {
      Remove-Item -LiteralPath $dir -Force
      $dir = Split-Path -Parent $dir
    }
  }
}
if (-not $KeepLogs) {
  foreach ($d in $dirs) { Remove-Item -LiteralPath $d -Recurse -Force }
  foreach ($f in $logFiles) { Remove-Item -LiteralPath $f.FullName -Force }
} else {
  $work = Join-Path $workRoot $label
  if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
if ($state.rotationBefore) { [IO.File]::WriteAllText($rotationFile, $state.rotationBefore, $utf8) }
else { Remove-Item $rotationFile -Force -ErrorAction SilentlyContinue }
Remove-Item $currentFile -Force

$left = @(Get-ChangedPaths | Where-Object { -not $known.ContainsKey($_.ToLowerInvariant()) })
if ($left.Count) { Write-Host "`nCleaned, but these changes remain: $($left -join ', ')" -ForegroundColor Red; exit 2 }
Write-Host "`nTicket $label undone: main tree back to the baseline$(if ($KeepLogs) { ', logs kept' } else { ', logs removed' }), rotation restored." -ForegroundColor Green
