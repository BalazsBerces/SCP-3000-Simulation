<#
.SYNOPSIS
  Drives scripts/run-tickets-v3.ps1 with the fake CLIs of the v2 tests (claude, gh, agy, dotnet, godot) against
  throwaway local repositories with a bare local "origin". No GitHub issue, no real push, no model is touched.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tests\run-tickets-v3\run-stub-tests.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tests\run-tickets-v3\run-stub-tests.ps1 -Only gemini-green
#>
param([string[]]$Only = @())

$ErrorActionPreference = 'Stop'
# powershell -File passes "a,b" as one string.
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$here = $PSScriptRoot
$stubs = Join-Path $here '..\run-tickets-v2'
$runnerSrc = (Resolve-Path (Join-Path $here '..\..\run-tickets-v3.ps1')).Path
$base = Join-Path $env:TEMP ('rt3-stub-{0:yyyyMMdd-HHmmss}' -f (Get-Date))
$bin = Join-Path $base 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
foreach ($f in 'stub-lib.ps1', 'stub-exec.ps1', 'claude.ps1', 'gh.ps1') { Copy-Item (Join-Path $stubs $f) $bin }
foreach ($t in 'agy', 'dotnet', 'godot') {
  Set-Content -Path (Join-Path $bin "$t.cmd") -Value "@powershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0stub-exec.ps1`" $t %*" -Encoding ASCII
}

function New-Fixture([string]$Name, $Scenario, $Gh) {
  $d = Join-Path $base $Name
  $repo = Join-Path $d 'repo'; $origin = Join-Path $d 'origin.git'; $state = Join-Path $d 'state'
  New-Item -ItemType Directory -Force $repo, $state | Out-Null
  git init -q --bare -b main $origin
  git init -q -b main $repo
  git -C $repo config user.name 'Stub Test'; git -C $repo config user.email 'stub@example.invalid'
  git -C $repo config core.autocrlf false
  New-Item -ItemType Directory -Force (Join-Path $repo 'src'), (Join-Path $repo 'tests\Scp3008.Core.Tests'), (Join-Path $repo 'game'), (Join-Path $repo 'scripts') | Out-Null
  Set-Content (Join-Path $repo 'Scp3008.sln') 'fake solution'
  Set-Content (Join-Path $repo 'src\impl.txt') 'empty'
  Set-Content (Join-Path $repo 'game\project.godot') 'fake'
  Copy-Item $runnerSrc (Join-Path $repo 'scripts\run-tickets-v3.ps1')
  Copy-Item (Join-Path $here '..\..\ticket-settings.json') (Join-Path $repo 'scripts\ticket-settings.json')
  git -C $repo add -A; git -C $repo commit -q -m 'fixture'
  git -C $repo remote add origin $origin
  git -C $repo push -q -u origin main
  # Skills: the Claude ones, and a stale Gemini copy the runner must replace.
  $claudeSkills = Join-Path $d 'claude-skills'; $geminiSkills = Join-Path $d 'gemini-skills'
  foreach ($s in 'implement', 'tdd') { New-Item -ItemType Directory -Force (Join-Path $claudeSkills $s) | Out-Null; Set-Content (Join-Path $claudeSkills "$s\SKILL.md") "current $s" }
  New-Item -ItemType Directory -Force (Join-Path $geminiSkills 'implement') | Out-Null
  Set-Content (Join-Path $geminiSkills 'implement\SKILL.md') 'stale implement'
  Set-Content (Join-Path $geminiSkills 'implement\leftover.md') 'stale file'
  Set-Content (Join-Path $state 'scenario.json') ($Scenario | ConvertTo-Json -Depth 10)
  Set-Content (Join-Path $state 'gh.json') ($Gh | ConvertTo-Json -Depth 10)
  [pscustomobject]@{ Dir = $d; Repo = $repo; Origin = $origin; State = $state; ClaudeSkills = $claudeSkills; GeminiSkills = $geminiSkills }
}

function New-Gh([string[]]$Labels = @('easy'), [bool]$StageEnd = $false, [int]$Count = 1) {
  $titles = @('08j: Restroom: tiled', '08k: Lost and found', '08l: Realistic models')
  $issues = @(for ($i = 0; $i -lt $Count; $i++) { @{ number = 67 + $i; title = $titles[$i]; labels = @('ready-for-agent') + $Labels; milestone = 1; state = 'OPEN' } })
  $issues += @{ number = 66; title = '08i: Cafeteria'; labels = @('ready-for-agent'); milestone = 1; state = 'CLOSED' }
  if (-not $StageEnd) { $issues += @{ number = 90; title = '09: Next stage ticket'; labels = @('ready-for-agent'); milestone = 1; state = 'OPEN'; blockedBy = @(67) } }
  @{ issues = $issues; milestones = @(@{ number = 1; title = 'Realistic Points of interest II'; state = 'open' }); comments = @() }
}

$results = @()
function Invoke-Scenario {
  param([string]$Name, $Scenario = @{}, $Gh = (New-Gh), [string[]]$RunnerArgs = @(), [scriptblock]$Setup, [scriptblock]$Assert)
  if ($Only.Count -and $Name -notin $Only) { return }
  $fx = New-Fixture $Name $Scenario $Gh
  if ($Setup) { & $Setup $fx }
  $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($fx.Repo)\scripts\run-tickets-v3.ps1`"",
    '-Repo', 'test/repo', '-NoStagePause', '-ProviderRetrySeconds', '1', '-LimitResumeDelaySeconds', '1',
    '-GodotConsole', "`"$bin\godot.cmd`"", '-AgyPath', "`"$bin\agy.cmd`"",
    '-ClaudeSkillsDir', "`"$($fx.ClaudeSkills)`"", '-GeminiSkillsDir', "`"$($fx.GeminiSkills)`"") + $RunnerArgs
  if (-not ($RunnerArgs -contains '-Max')) { $argList += @('-Max', '1') }
  $oldPath = $env:PATH; $env:PATH = "$bin;$oldPath"; $env:STUB_STATE = $fx.State
  $started = Get-Date
  try {
    $p = Start-Process powershell -ArgumentList ($argList -join ' ') -WorkingDirectory $fx.Repo -NoNewWindow -PassThru -Wait `
      -RedirectStandardOutput (Join-Path $fx.Dir 'runner.out') -RedirectStandardError (Join-Path $fx.Dir 'runner.err')
  } finally { $env:PATH = $oldPath }
  $r = [pscustomobject]@{
    Exit = $p.ExitCode
    Calls = @(Get-Content (Join-Path $fx.State 'calls.log') -ErrorAction SilentlyContinue)
    Csv = @($csv = Join-Path $fx.Repo '.ticket-logs\v3-runs.csv'; if (Test-Path $csv) { Import-Csv $csv })
    Gh = (Get-Content (Join-Path $fx.State 'gh.json') -Raw | ConvertFrom-Json)
    OriginLog = @(git -C $fx.Origin log --format=%B main)
    OriginCount = [int](git -C $fx.Origin rev-list --count main)
    Status = @(git -C $fx.Repo status --porcelain -uall)
    Out = (Get-Content (Join-Path $fx.Dir 'runner.out') -Raw -ErrorAction SilentlyContinue) + (Get-Content (Join-Path $fx.Dir 'runner.err') -Raw -ErrorAction SilentlyContinue)
    Fx = $fx
  }
  $fails = @(& $Assert $r | Where-Object { $_ })
  $secs = [int]((Get-Date) - $started).TotalSeconds
  if ($fails.Count) {
    Write-Host "FAIL $Name (${secs}s)" -ForegroundColor Red
    $fails | ForEach-Object { Write-Host "     - $_" -ForegroundColor Red }
    Write-Host "     runner output: $($fx.Dir)\runner.out" -ForegroundColor DarkGray
  } else { Write-Host "ok   $Name (${secs}s)" -ForegroundColor Green }
  $script:results += [pscustomobject]@{ Name = $Name; Ok = -not $fails.Count }
}

function Should([bool]$Cond, [string]$Msg) { if (-not $Cond) { $Msg } }
function Get-Issue($r, [int]$N) { @($r.Gh.issues | Where-Object { $_.number -eq $N })[0] }
# Whether a comment on issue N matches the pattern.
function Test-Comment($r, [int]$N, [string]$Pattern) { [bool]@($r.Gh.comments | Where-Object { $_.number -eq $N -and $_.body -match $Pattern }).Count }
function Get-Calls($r, [string]$Like) { , @($r.Calls | Where-Object { $_ -like $Like }) }

function Assert-Committed($r, [string]$CoAuthor) {
  Should ($r.Exit -eq 0) "runner exit $($r.Exit)"
  Should ((Get-Issue $r 67).state -eq 'CLOSED') 'issue 67 not closed'
  Should ($r.OriginCount -eq 2) "origin has $($r.OriginCount) commits, expected the fixture + one ticket commit"
  Should (($r.OriginLog -join "`n") -match 'Closes #67') 'commit lacks Closes #67'
  Should (($r.OriginLog -join "`n") -match [regex]::Escape($CoAuthor)) "commit lacks the co-author '$CoAuthor'"
  Should (-not $r.Status.Count) "working tree not clean: $($r.Status -join '; ')"
  Should ($r.Csv.Count -eq 1 -and $r.Csv[-1].result -eq 'done') "CSV result '$($r.Csv[-1].result)', expected done"
}

# ---------------------------------------------------------------------------------------------------

Invoke-Scenario 'gemini-green' @{ exec = @('done') } -Assert {
  param($r)
  Assert-Committed $r 'Co-Authored-By: Gemini (gemini-3.8-flash-high)'
  Should ((Get-Calls $r 'exec Gemini *msg=/implement ticket 08j*').Count -eq 1) 'Gemini did not get the /implement prompt'
  Should ((Get-Calls $r 'claude *').Count -eq 0) 'Claude ran on an easy ticket'
  Should ((Get-Calls $r 'dotnet build').Count -eq 1 -and (Get-Calls $r 'dotnet test').Count -eq 1 -and (Get-Calls $r 'godot *').Count -eq 1) 'the gate did not run build, tests and Godot once each'
  Should (Test-Comment $r 67 'Stub closing comment for 08j') "Gemini's closing comment was not posted"
  Should ($r.Csv[-1].agent -eq 'gemini' -and [long]$r.Csv[-1].gemini_in -eq 100) "CSV agent/tokens: $($r.Csv[-1].agent)/$($r.Csv[-1].gemini_in)"
  Should ((Get-Content (Join-Path $r.Fx.GeminiSkills 'implement\SKILL.md')) -eq 'current implement') 'the implement skill was not synced'
  Should (-not (Test-Path (Join-Path $r.Fx.GeminiSkills 'implement\leftover.md'))) 'a stale skill file survived the sync'
  Should (Test-Path (Join-Path $r.Fx.GeminiSkills 'tdd\SKILL.md')) 'the tdd skill was not synced'
}

Invoke-Scenario 'hard-sonnet-medium' -Gh (New-Gh @('hard')) -Assert {
  param($r)
  Assert-Committed $r 'Co-Authored-By: Claude Sonnet 5.5'
  Should ((Get-Calls $r 'claude resume= effort=medium model=sonnet prompt=/implement ticket 08j*').Count -eq 1) "Claude call: $((Get-Calls $r 'claude *') -join '; ')"
  Should ((Get-Calls $r 'exec *').Count -eq 0) 'Gemini ran on a hard ticket'
  Should ($r.Csv[-1].agent -eq 'claude' -and [long]$r.Csv[-1].claude_tokens -eq 6200) "CSV agent/tokens: $($r.Csv[-1].agent)/$($r.Csv[-1].claude_tokens)"
}

Invoke-Scenario 'easy-stage-end-claude-medium' -Gh (New-Gh @('easy') $true) -Assert {
  param($r)
  Assert-Committed $r 'Co-Authored-By: Claude Sonnet 5.5'
  Should ((Get-Calls $r 'claude * effort=medium model=sonnet *').Count -eq 1) "stage end call: $((Get-Calls $r 'claude *') -join '; ')"
  Should ((Get-Calls $r 'exec *').Count -eq 0) 'Gemini ran on a stage end'
  Should ((Get-Calls $r 'gh api -X PATCH repos/test/repo/milestones/1*').Count -eq 1) 'the milestone was not closed'
}

Invoke-Scenario 'easy-agent-claude' -RunnerArgs @('-EasyAgent', 'claude') -Assert {
  param($r)
  Assert-Committed $r 'Co-Authored-By: Claude Sonnet 5.5'
  Should ((Get-Calls $r 'exec *').Count -eq 0) 'Gemini ran despite -EasyAgent claude'
}

Invoke-Scenario 'gemini-red-then-fixed' @{ exec = @('red', 'done') } -Assert {
  param($r)
  Assert-Committed $r 'Co-Authored-By: Gemini'
  $ex = Get-Calls $r 'exec Gemini *'
  Should ($ex.Count -eq 2) "Gemini ran $($ex.Count) times, expected 2"
  Should ($ex.Count -eq 2 -and $ex[1] -like "exec Gemini session=agy-ses-* msg=The runner's checks failed. Read .ticket-logs/08j-fix-1.md*") "the fix round did not continue the conversation: $($ex[1])"
  Should ($r.Csv[-1].fix_rounds -eq '1') "fix_rounds $($r.Csv[-1].fix_rounds)"
}

Invoke-Scenario 'gemini-red-twice-stops' @{ exec = @('red', 'red') } -Assert {
  param($r)
  Should ($r.Exit -ne 0) 'runner did not stop'
  Should ($r.OriginCount -eq 1) 'red work was pushed'
  Should ((Get-Issue $r 67).state -eq 'OPEN') 'issue closed on red work'
  Should (Test-Comment $r 67 'still red') 'no blocker comment'
  Should ($r.Csv[-1].result -eq 'unfinished') "CSV result $($r.Csv[-1].result)"
}

Invoke-Scenario 'gemini-blocked' @{ exec = @('blocked') } -Assert {
  param($r)
  Should ($r.Exit -ne 0) 'runner did not stop'
  Should (Test-Comment $r 67 'TICKET_BLOCKED: too hard') 'the blocker was not posted'
  Should ((Get-Calls $r 'dotnet *').Count -eq 0) 'the gate ran on a blocked ticket'
}

Invoke-Scenario 'gemini-commits-itself' @{ exec = @('commit') } -Assert {
  param($r)
  Assert-Committed $r 'Co-Authored-By: Gemini'
  Should (-not (($r.OriginLog -join "`n") -match 'executor commit')) "Gemini's own commit reached origin"
}

Invoke-Scenario 'gemini-provider-retry' @{ exec = @('provider', 'done') } -Assert {
  param($r)
  Assert-Committed $r 'Co-Authored-By: Gemini'
  Should ((Get-Calls $r 'exec Gemini *').Count -eq 2) 'no retry after the provider error'
}

Invoke-Scenario 'gemini-quota-falls-back-to-claude' @{ exec = @('quota') } -RunnerArgs @('-Max', '2') -Gh (New-Gh @('easy') $false 2) -Assert {
  param($r)
  Should ($r.Exit -eq 0) "runner exit $($r.Exit)"
  Should ((Get-Issue $r 67).state -eq 'CLOSED' -and (Get-Issue $r 68).state -eq 'CLOSED') 'tickets not closed'
  Should ((Get-Calls $r 'exec Gemini *').Count -eq 1) 'Gemini was tried again after running out of quota'
  Should ((Get-Calls $r 'claude * model=sonnet prompt=/implement ticket 08j*').Count -eq 1) '08j did not fall back to Claude'
  Should ((Get-Calls $r 'claude * model=sonnet prompt=/implement ticket 08k*').Count -eq 1) '08k did not go to Claude'
}

Invoke-Scenario 'claude-usage-limit-resume' @{ limitOnImplement = $true } -Gh (New-Gh @('hard')) -Assert {
  param($r)
  Assert-Committed $r 'Co-Authored-By: Claude Sonnet'
  Should ((Get-Calls $r 'claude resume=sess-0 *usage limit interrupted*').Count -eq 1) 'the session was not resumed after the limit'
}

Invoke-Scenario 'dirty-tree-refuses' -Setup { param($fx) Set-Content (Join-Path $fx.Repo 'leftover.txt') 'x' } -Assert {
  param($r)
  Should ($r.Exit -ne 0) 'runner started on a dirty tree'
  Should ((Get-Calls $r 'exec *').Count -eq 0 -and (Get-Calls $r 'claude *').Count -eq 0) 'an agent ran on a dirty tree'
}

Invoke-Scenario 'dry-run' -RunnerArgs @('-DryRun') -Assert {
  param($r)
  Should ($r.Exit -eq 0) "dry run exit $($r.Exit)"
  Should ($r.Out -match 'Gemini gemini-3.8-flash-high') 'dry run did not name the agent'
  Should ((Get-Calls $r 'exec *').Count -eq 0 -and (Get-Calls $r 'claude *').Count -eq 0 -and (Get-Calls $r 'dotnet *').Count -eq 0) 'the dry run ran something'
}

$failed = @($results | Where-Object { -not $_.Ok })
Write-Host "`n$($results.Count) scenario(s), $($failed.Count) failed. Fixtures: $base"
exit $(if ($failed.Count) { 1 } else { 0 })
