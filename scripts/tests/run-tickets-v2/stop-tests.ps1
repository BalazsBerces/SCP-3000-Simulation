<#
.SYNOPSIS
  Tests scripts/stop-tickets-v2.ps1 against throwaway local repositories and fake runner processes.
  Do not run it while a real ticket runner started with a relative script path is running: the stop script
  accepts relative runner paths, so it would stop that one too.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tests\run-tickets-v2\stop-tests.ps1
#>
param([switch]$Keep)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$stopSrc = (Resolve-Path (Join-Path $here '..\..\stop-tickets-v2.ps1')).Path
$base = Join-Path $env:TEMP ('rt2-stop-{0:yyyyMMdd-HHmmss}' -f (Get-Date))
$results = @()

function Should([bool]$Cond, [string]$Msg) { if (-not $Cond) { $Msg } }

# A repository whose ticket 08i was interrupted after the plan and part of the work.
function New-Fixture([string]$Name) {
  $repo = Join-Path $base $Name
  New-Item -ItemType Directory -Force (Join-Path $repo 'scripts'), (Join-Path $repo 'src') | Out-Null
  git init -q -b main $repo
  git -C $repo config user.name 'Stub Test'; git -C $repo config user.email 'stub@example.invalid'
  git -C $repo config core.autocrlf false
  Set-Content (Join-Path $repo 'src\impl.txt') 'original'
  Set-Content (Join-Path $repo 'src\gone.txt') 'tracked'
  Copy-Item $stopSrc (Join-Path $repo 'scripts\stop-tickets-v2.ps1')
  Set-Content (Join-Path $repo 'scripts\run-tickets-v2.ps1') 'Start-Sleep 60'
  Set-Content (Join-Path $repo 'scripts\ticket-settings-v2.json') '{}'
  Add-Content (Join-Path $repo '.git\info\exclude') ".ticket-logs/`n.ticket-work/"
  git -C $repo add -A; git -C $repo commit -q -m 'fixture'
  $head = (git -C $repo rev-parse HEAD).Trim()

  # Dirty before the ticket: kept.
  Set-Content (Join-Path $repo 'notes-baseline.txt') 'my notes'
  $logs = Join-Path $repo '.ticket-logs'
  New-Item -ItemType Directory -Force (Join-Path $logs '08i'), (Join-Path $repo '.ticket-work\08i') | Out-Null
  Set-Content (Join-Path $logs '08h-old.log') 'an earlier ticket'
  (Get-Item (Join-Path $logs '08h-old.log')).LastWriteTime = (Get-Date).AddDays(-1)
  Set-Content (Join-Path $logs '08i-earlier.log') 'same label, before this ticket'
  (Get-Item (Join-Path $logs '08i-earlier.log')).LastWriteTime = (Get-Date).AddDays(-1)
  Set-Content (Join-Path $logs 'v2-rotation.json') '{"last":"Gemini"}'
  $state = @{ pid = 999999; label = '08i'; issue = 66; title = '08i: Canteen'; started = (Get-Date).AddMinutes(-5).ToString('o')
    head = $head; baseline = @('notes-baseline.txt'); rotationBefore = '{"last":"Nemotron"}' }
  Set-Content (Join-Path $logs 'v2-current.json') ($state | ConvertTo-Json)

  # The ticket's own changes.
  Set-Content (Join-Path $logs '08i\plan.jsonl') '{}'
  Set-Content (Join-Path $logs '08i-spec.md') 'spec copy'
  Set-Content (Join-Path $repo '.ticket-work\08i\PLAN.md') 'plan'
  Set-Content (Join-Path $repo 'src\impl.txt') 'changed by the ticket'
  Remove-Item (Join-Path $repo 'src\gone.txt')
  New-Item -ItemType Directory -Force (Join-Path $repo 'tests\New') | Out-Null
  Set-Content (Join-Path $repo 'tests\New\AcceptanceTests.cs') 'locked test'
  Set-Content (Join-Path $repo 'src\staged.txt') 'staged'
  git -C $repo add src/staged.txt
  New-Item -ItemType Directory -Force (Join-Path $repo '.claude') | Out-Null
  Set-Content (Join-Path $repo '.claude\settings.json') '{}'
  $repo
}

function Invoke-Stop([string]$Repo, [string[]]$StopArgs = @()) {
  $out = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Repo 'scripts\stop-tickets-v2.ps1') @StopArgs 2>&1 | Out-String
  [pscustomobject]@{ Exit = $LASTEXITCODE; Out = $out; Status = @(git -C $Repo status --porcelain -uall) }
}

function Invoke-Case([string]$Name, [scriptblock]$Body) {
  $fails = @(& $Body | Where-Object { $_ })
  if ($fails.Count) {
    Write-Host "FAIL $Name" -ForegroundColor Red
    $fails | ForEach-Object { Write-Host "     - $_" -ForegroundColor Red }
  } else { Write-Host "ok   $Name" -ForegroundColor Green }
  $script:results += [pscustomobject]@{ Name = $Name; Ok = -not $fails.Count }
}

# ---------------------------------------------------------------------------------------------------
Invoke-Case 'clean-undoes-the-ticket' {
  $repo = New-Fixture 'clean'
  $r = Invoke-Stop $repo @('-Clean')
  $logs = Join-Path $repo '.ticket-logs'
  Should ($r.Exit -eq 0) "exit code $($r.Exit): $($r.Out)"
  Should (($r.Status -join ' ') -eq '?? .claude/settings.json ?? notes-baseline.txt') "main tree not back to the baseline: $($r.Status -join ', ')"
  Should ((Get-Content (Join-Path $repo 'src\impl.txt') -Raw).Trim() -eq 'original') 'modified file not restored'
  Should (-not (Test-Path (Join-Path $repo 'tests'))) 'the empty folder of a new file was left behind'
  Should (-not (Test-Path (Join-Path $repo '.ticket-work\08i'))) '.ticket-work/08i not removed'
  Should (-not (Test-Path (Join-Path $logs '08i'))) '.ticket-logs/08i not removed'
  Should (-not (Test-Path (Join-Path $logs '08i-spec.md'))) 'the ticket log file was not removed'
  Should (Test-Path (Join-Path $logs '08i-earlier.log')) 'a log file older than the ticket was removed'
  Should (Test-Path (Join-Path $logs '08h-old.log')) 'another ticket''s log was removed'
  Should ((Get-Content (Join-Path $logs 'v2-rotation.json') -Raw).Trim() -eq '{"last":"Nemotron"}') 'rotation not restored'
  Should (-not (Test-Path (Join-Path $logs 'v2-current.json'))) 'v2-current.json not removed'
}

Invoke-Case 'preview-changes-nothing' {
  $repo = New-Fixture 'preview'
  $before = @(git -C $repo status --porcelain -uall)
  $r = Invoke-Stop $repo
  Should ($r.Exit -eq 0) "exit code $($r.Exit)"
  Should (($r.Status -join '|') -eq ($before -join '|')) 'the preview changed the main tree'
  Should ($r.Out -match 'src/impl\.txt' -and $r.Out -match 'tests/New/AcceptanceTests\.cs') 'the preview does not list the ticket changes'
  Should (Test-Path (Join-Path $repo '.ticket-logs\v2-current.json')) 'the preview removed v2-current.json'
}

Invoke-Case 'keep-logs' {
  $repo = New-Fixture 'keeplogs'
  $r = Invoke-Stop $repo @('-Clean', '-KeepLogs')
  Should ($r.Exit -eq 0) "exit code $($r.Exit)"
  Should (Test-Path (Join-Path $repo '.ticket-logs\08i\plan.jsonl')) 'logs removed despite -KeepLogs'
  Should (-not (Test-Path (Join-Path $repo '.ticket-work\08i'))) '.ticket-work/08i not removed'
}

Invoke-Case 'head-moved-refuses' {
  $repo = New-Fixture 'headmoved'
  git -C $repo commit -q -m 'reviewer commit' -- src/staged.txt
  $before = @(git -C $repo status --porcelain -uall)
  $r = Invoke-Stop $repo @('-Clean')
  Should ($r.Exit -eq 2) "exit code $($r.Exit), expected 2"
  Should (($r.Status -join '|') -eq ($before -join '|')) 'the refused clean changed the main tree'
  Should (Test-Path (Join-Path $repo '.ticket-logs\v2-current.json')) 'v2-current.json removed on refusal'
}

Invoke-Case 'no-interrupted-ticket' {
  $repo = New-Fixture 'nostate'
  Remove-Item (Join-Path $repo '.ticket-logs\v2-current.json')
  $before = @(git -C $repo status --porcelain -uall)
  $r = Invoke-Stop $repo @('-Clean')
  Should ($r.Exit -eq 0) "exit code $($r.Exit)"
  Should (($r.Status -join '|') -eq ($before -join '|')) 'changes without a ticket state were touched'
}

Invoke-Case 'stops-runner-tree-and-orphans' {
  $repo = New-Fixture 'procs'
  $runnerPath = Join-Path $repo 'scripts\run-tickets-v2.ps1'
  # The fake runner starts a child, like the real one starts claude or an executor.
  Set-Content $runnerPath "Start-Process powershell -ArgumentList '-NoProfile -Command Start-Sleep 120' -NoNewWindow; Start-Sleep 120"
  $runner = Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$runnerPath`"" -PassThru -WindowStyle Hidden
  $settings = Join-Path $repo 'scripts\ticket-settings-v2.json'
  $orphan = Start-Process powershell -ArgumentList "-NoProfile -Command `"Start-Sleep 120 # --settings $settings`"" -PassThru -WindowStyle Hidden
  $decoy = Start-Process powershell -ArgumentList '-NoProfile -Command "Start-Sleep 120 # unrelated"' -PassThru -WindowStyle Hidden
  Start-Sleep 3
  $child = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($runner.Id)")
  $r = Invoke-Stop $repo
  Start-Sleep 1
  Should ($r.Exit -eq 0) "exit code $($r.Exit): $($r.Out)"
  Should ($child.Count -ge 1) 'the fake runner did not start its child'
  Should (-not (Get-Process -Id $runner.Id -ErrorAction SilentlyContinue)) 'the runner is still running'
  Should (-not @($child | Where-Object { Get-Process -Id ([int]$_.ProcessId) -ErrorAction SilentlyContinue }).Count) 'the runner''s child is still running'
  Should (-not (Get-Process -Id $orphan.Id -ErrorAction SilentlyContinue)) 'the orphaned claude-like process is still running'
  Should ([bool](Get-Process -Id $decoy.Id -ErrorAction SilentlyContinue)) 'an unrelated process was stopped'
  Stop-Process -Id $decoy.Id -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------------------------------
$failed = @($results | Where-Object { -not $_.Ok })
Write-Host ("`n{0} passed, {1} failed" -f ($results.Count - $failed.Count), $failed.Count) -ForegroundColor $(if ($failed.Count) { 'Red' } else { 'Green' })
if (-not $Keep -and -not $failed.Count) { Remove-Item $base -Recurse -Force -ErrorAction SilentlyContinue }
else { Write-Host "Fixtures: $base" }
exit $failed.Count
