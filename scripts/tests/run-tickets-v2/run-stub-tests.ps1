<#
.SYNOPSIS
  Drives scripts/run-tickets-v2.ps1 through its state machine with fake CLIs (claude, gh, opencode, agy, codex,
  dotnet, godot) against throwaway local repositories with a bare local "origin". No GitHub issue, no real push,
  no model is touched.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tests\run-tickets-v2\run-stub-tests.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tests\run-tickets-v2\run-stub-tests.ps1 -Only green-approve,quota-rotation
#>
param([string[]]$Only = @(), [switch]$Keep)

$ErrorActionPreference = 'Stop'
# powershell -File passes "a,b" as one string.
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$here = $PSScriptRoot
$runnerSrc = (Resolve-Path (Join-Path $here '..\..\run-tickets-v2.ps1')).Path
$base = Join-Path $env:TEMP ('rt2-stub-{0:yyyyMMdd-HHmmss}' -f (Get-Date))
$bin = Join-Path $base 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
foreach ($f in 'stub-lib.ps1', 'stub-exec.ps1', 'claude.ps1', 'gh.ps1') { Copy-Item (Join-Path $here $f) $bin }
foreach ($t in 'opencode', 'codex', 'agy', 'dotnet', 'godot') {
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
  Set-Content (Join-Path $repo 'tests\Scp3008.Core.Tests\ExecutorTests.cs') 'public class ExecutorTests { }'
  Set-Content (Join-Path $repo 'game\project.godot') 'fake'
  Set-Content (Join-Path $repo 'README.md') 'fixture'
  Copy-Item $runnerSrc (Join-Path $repo 'scripts\run-tickets-v2.ps1')
  Copy-Item (Join-Path $here '..\..\ticket-settings-v2.json') (Join-Path $repo 'scripts\ticket-settings-v2.json')
  git -C $repo add -A; git -C $repo commit -q -m 'fixture'
  git -C $repo remote add origin $origin
  git -C $repo push -q -u origin main
  # A dirty file before the run: the baseline, which must never be committed or removed.
  Set-Content (Join-Path $repo 'notes-baseline.txt') 'my local notes'
  Set-Content (Join-Path $state 'scenario.json') ($Scenario | ConvertTo-Json -Depth 10)
  Set-Content (Join-Path $state 'gh.json') ($Gh | ConvertTo-Json -Depth 10)
  [pscustomobject]@{ Dir = $d; Repo = $repo; Origin = $origin; State = $state; Exec = (Join-Path $d 'exec') }
}

function New-Gh([int]$Count = 1, [bool]$StageEnd = $false) {
  $titles = @('08i: Canteen: a lived-in store canteen', '08j: Second ticket', '08k: Third ticket')
  $issues = @(for ($i = 0; $i -lt $Count; $i++) { @{ number = 66 + $i; title = $titles[$i]; labels = @('ready-for-agent', 'easy'); milestone = 1; state = 'OPEN' } })
  # A closed earlier ticket of the stage, so the stage start is 08h.
  $issues += @{ number = 65; title = '08h: Aquarium aisle'; labels = @('ready-for-agent'); milestone = 1; state = 'CLOSED' }
  if (-not $StageEnd) { $issues += @{ number = 90; title = '09: Next stage ticket'; labels = @('ready-for-agent'); milestone = 1; state = 'OPEN'; blockedBy = @(66) } }
  @{ issues = $issues; milestones = @(@{ number = 1; title = 'Realistic Points of interest II'; state = 'open' }); comments = @() }
}

$results = @()
function Invoke-Scenario {
  param([string]$Name, $Scenario, $Gh = (New-Gh), [string[]]$RunnerArgs = @(), [scriptblock]$Assert)
  if ($Only.Count -and $Name -notin $Only) { return }
  $fx = New-Fixture $Name $Scenario $Gh
  $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($fx.Repo)\scripts\run-tickets-v2.ps1`"",
    '-Repo', 'test/repo', '-NoStagePause', '-ProviderRetrySeconds', '1', '-LimitResumeDelaySeconds', '1',
    '-GodotConsole', "`"$bin\godot.cmd`"", '-AgyPath', "`"$bin\agy.cmd`"", '-ExecDir', "`"$($fx.Exec)`"") + $RunnerArgs
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
    Csv = @($csv = Join-Path $fx.Repo '.ticket-logs\v2-runs.csv'; if (Test-Path $csv) { Import-Csv $csv })
    Gh = (Get-Content (Join-Path $fx.State 'gh.json') -Raw | ConvertFrom-Json)
    OriginLog = @(git -C $fx.Origin log --format=%B main)
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

# Assertion helpers: each returns a failure message or nothing.
function Should([bool]$Cond, [string]$Msg) { if (-not $Cond) { $Msg } }
function Get-Issue($r, [int]$N) { @($r.Gh.issues | Where-Object { $_.number -eq $N })[0] }
# The leading comma keeps a one-element result an array (PowerShell unrolls function output).
function Get-ExecCalls($r) { , @($r.Calls | Where-Object { $_ -like 'exec *' }) }
function Test-Approved($r, [string]$Verdict = 'APPROVE', [string]$By = 'Nemotron') {
  Should ($r.Exit -eq 0) "exit code $($r.Exit), expected 0"
  Should ((Get-Issue $r 66).state -eq 'CLOSED') 'issue #66 is not closed'
  Should (($r.OriginLog -join "`n") -match 'Closes #66') 'no commit with Closes #66 on origin'
  Should (($r.OriginLog -join "`n") -match "Co-Authored-By: $By") "commit lacks the $By co-author line"
  Should (($r.OriginLog -join "`n") -match 'Co-Authored-By: Claude Opus') 'commit lacks the Opus co-author line'
  Should ($r.Csv.Count -ge 1 -and $r.Csv[-1].verdict -eq $Verdict) "CSV verdict is '$($r.Csv[-1].verdict)', expected $Verdict"
  Should ($r.Csv.Count -ge 1 -and $r.Csv[-1].finished_by -eq $By) "CSV finished_by is '$($r.Csv[-1].finished_by)', expected $By"
  Should (($r.Status -join ' ') -eq '?? notes-baseline.txt') "main tree is not back to the baseline: $($r.Status -join ', ')"
  Should (Test-Path (Join-Path $r.Fx.Repo 'notes-baseline.txt')) 'the baseline file was removed'
  Should (-not (git -C $r.Fx.Origin ls-tree -r --name-only main | Where-Object { $_ -eq 'notes-baseline.txt' })) 'the baseline file was committed'
  Should ([bool](git -C $r.Fx.Origin ls-tree -r --name-only main | Where-Object { $_ -eq 'tests/Scp3008.Core.Tests/AcceptanceTests.cs' })) 'the locked acceptance test was not committed'
  Should (-not (Test-Path (Join-Path $r.Fx.Repo '.ticket-logs\v2-current.json'))) 'v2-current.json left behind after a finished ticket'
}
function Test-Blocked($r, [switch]$BeforeExecution) {
  Should ($r.Exit -ne 0) 'exit code 0, expected non-zero'
  Should ((Get-Issue $r 66).state -eq 'OPEN') 'issue #66 is not open'
  Should (@($r.Gh.comments | Where-Object { $_.number -eq 66 -and $_.body -match 'blocked' }).Count -eq 1) 'no blocked comment on #66'
  Should ($r.Csv.Count -ge 1 -and $r.Csv[-1].verdict -eq 'blocked') "CSV verdict is '$($r.Csv[-1].verdict)', expected blocked"
  Should (($r.Status -join ' ') -eq '?? notes-baseline.txt') "main tree is not back to the baseline: $($r.Status -join ', ')"
  Should (@($r.OriginLog | Where-Object { $_ -match 'Closes #66' }).Count -eq 0) 'something was committed'
  Should (-not (Test-Path (Join-Path $r.Fx.Repo '.ticket-logs\v2-current.json'))) 'v2-current.json left behind after a blocked ticket'
  # Before any executor ran there is no workspace state to save.
  if (-not $BeforeExecution) {
    Should (@(Get-ChildItem (Join-Path $r.Fx.Repo '.ticket-logs\08i') -Filter 'blocked-*.patch' -ErrorAction SilentlyContinue).Count -ge 1) 'no blocked patch saved'
  }
}

# ---------------------------------------------------------------------------------------------------
Invoke-Scenario 'green-approve' @{ exec = @('done'); review = @('APPROVE'); captures = $true } -Assert {
  param($r)
  Test-Approved $r
  Should ((Get-ExecCalls $r).Count -eq 1) "expected 1 executor call, got $((Get-ExecCalls $r).Count)"
  Should ((Get-ExecCalls $r)[0] -like 'exec Nemotron session= *') 'first ticket did not start with Nemotron'
  Should (@($r.Calls | Where-Object { $_ -like 'dotnet test' }).Count -eq 1) 'the gate did not run the test suite once'
  Should (@($r.Calls | Where-Object { $_ -like 'godot *--build-solutions*' }).Count -eq 1) 'the gate did not run the Godot build'
  Should (@($r.Calls | Where-Object { $_ -like 'claude resume=sess-0 *prompt=/review-ticket 08i 66' }).Count -eq 1) 'the review did not resume the planning session'
  Should ((Get-Content (Join-Path $r.Fx.Repo '.ticket-logs\v2-rotation.json') -Raw) -match 'Nemotron') 'rotation not saved as Nemotron'
  Should ([int]$r.Csv[-1].exec_tokens_in -eq 100) "executor tokens not recorded: $($r.Csv[-1].exec_tokens_in)"
  # Plan + review, 6200 tokens each; the review resumes the plan session, whose totals include the plan.
  Should ([int]$r.Csv[-1].opus_tokens -eq 12400) "Opus tokens $($r.Csv[-1].opus_tokens), expected 12400 (plan + review, not counted twice)"
}

Invoke-Scenario 'done-with-exit-code-1' @{ exec = @('done-exit1') } -Assert {
  param($r)
  Test-Approved $r
  Should ((Get-ExecCalls $r).Count -eq 1) 'TICKET_DONE with exit code 1 was not accepted as delivered'
}

Invoke-Scenario 'red-then-green' @{ exec = @('red', 'done') } -Assert {
  param($r)
  Test-Approved $r
  $c = Get-ExecCalls $r
  Should ($c.Count -eq 2) "expected 2 executor calls, got $($c.Count)"
  Should ($c[1] -like 'exec Nemotron session=opencode-ses-0 *FIX-1.md*') "the fix round did not continue the session: $($c[1])"
  Should ($r.Csv[-1].fix_rounds -eq '1') "fix_rounds $($r.Csv[-1].fix_rounds), expected 1"
  $fix = Get-Content (Join-Path $r.Fx.Exec '.ticket-work\08i\FIX-1.md') -Raw
  Should ($fix -match 'ExecutorTests.Works') 'the fix round did not get the failing test name'
}

Invoke-Scenario 'compile-error-then-green' @{ exec = @('compile', 'done') } -Assert {
  param($r)
  Test-Approved $r
  $fix = Get-Content (Join-Path $r.Fx.Exec '.ticket-work\08i\FIX-1.md') -Raw
  Should ($fix -match 'error CS0103') 'the fix round did not get the compile error'
}

Invoke-Scenario 'missing-capture-then-green' @{ exec = @('done-nocapture', 'done'); captures = $true } -Assert {
  param($r)
  Test-Approved $r
  Should ($r.Csv[-1].fix_rounds -eq '1') 'a missing capture did not cost a fix round'
}

Invoke-Scenario 'red-exhausts-fix-rounds-falls-back' @{ exec = @('red', 'red', 'red', 'done') } -Assert {
  param($r)
  Test-Approved $r -By 'Gemini'
  $c = Get-ExecCalls $r
  Should ($c.Count -eq 4 -and $c[3] -like 'exec Gemini session= *') "expected 3 Nemotron calls then a fresh Gemini run: $($c -join ' | ')"
  Should ($r.Csv[-1].attempts -eq '2' -and $r.Csv[-1].primary -eq 'Nemotron') 'attempts/primary not recorded'
  Should ((Get-Content (Join-Path $r.Fx.Repo '.ticket-logs\v2-rotation.json') -Raw) -match 'Nemotron') 'a fallback changed the rotation'
}

Invoke-Scenario 'provider-error-retry-succeeds' @{ exec = @('provider', 'done') } -Assert {
  param($r)
  Test-Approved $r
  $c = Get-ExecCalls $r
  Should ($c.Count -eq 2 -and $c[1] -like 'exec Nemotron session=opencode-ses-0 *provider error*') "the retry did not continue the session: $($c -join ' | ')"
}

Invoke-Scenario 'provider-error-twice-falls-back' @{ exec = @('provider', 'provider', 'done') } -Assert {
  param($r)
  Test-Approved $r -By 'Gemini'
}

Invoke-Scenario 'timeout-falls-back' @{ exec = @('timeout', 'done') } -RunnerArgs @('-ExecutorMinutes', '0.15') -Assert {
  param($r)
  Test-Approved $r -By 'Gemini'
  Should ($r.Out -match 'Time limit') 'no time-limit message'
}

Invoke-Scenario 'nomarker-is-gated' @{ exec = @('nomarker') } -Assert {
  param($r)
  Test-Approved $r
}

Invoke-Scenario 'touched-locked-test-falls-back' @{ exec = @('touchlocked', 'done', 'done', 'done') } -Assert {
  param($r)
  Test-Approved $r -By 'Gemini'
  $fix = Get-Content (Join-Path $r.Fx.Dir 'exec\.ticket-work\08i\START.md') -Raw -ErrorAction SilentlyContinue
  Should ($r.Out -match 'Locked acceptance test files were modified') 'the gate did not catch the modified locked test'
}

Invoke-Scenario 'commit-in-clone-falls-back' @{ exec = @('commit', 'done', 'done', 'done') } -Assert {
  param($r)
  Test-Approved $r -By 'Gemini'
  Should ($r.Out -match 'Commits were made in the workspace') 'the gate did not catch the commit'
}

Invoke-Scenario 'quota-rotation' @{ exec = @('quota', 'done', 'done', 'done') } -Gh (New-Gh 3 $true) -RunnerArgs @('-Max', '3', '-StartWith', 'Nemotron') -Assert {
  param($r)
  $c = Get-ExecCalls $r
  Should ($r.Exit -eq 0) "exit code $($r.Exit)"
  Should ($c.Count -eq 4) "expected 4 executor calls, got $($c.Count): $($c -join ' | ')"
  Should (@($c | Where-Object { $_ -like 'exec Nemotron*' }).Count -eq 1) 'an exhausted Nemotron was called again'
  Should ($c[1] -like 'exec Gemini*' -and $c[2] -like 'exec Gemini*' -and $c[3] -like 'exec Gemini*') "Gemini did not take over: $($c -join ' | ')"
  Should ($r.Csv.Count -eq 3) "expected 3 CSV rows, got $($r.Csv.Count)"
  Should (($r.Csv | ForEach-Object primary) -join ',' -eq 'Nemotron,Gemini,Nemotron') "turns were $(($r.Csv | ForEach-Object primary) -join ',')"
  Should ((Get-Issue $r 68).state -eq 'CLOSED') 'the third ticket is not closed'
}

Invoke-Scenario 'rotation-alternates' @{ exec = @('done', 'done') } -Gh (New-Gh 2 $true) -RunnerArgs @('-Max', '2') -Assert {
  param($r)
  $c = Get-ExecCalls $r
  Should ($r.Exit -eq 0) "exit code $($r.Exit)"
  Should ($c[0] -like 'exec Nemotron*' -and $c[1] -like 'exec Gemini*') "no alternation: $($c -join ' | ')"
  Should (($r.OriginLog -join "`n") -match 'Co-Authored-By: Gemini') 'second commit lacks the Gemini co-author line'
}

Invoke-Scenario 'start-with-gemini' @{ exec = @('done') } -RunnerArgs @('-StartWith', 'Gemini') -Assert {
  param($r)
  Test-Approved $r -By 'Gemini'
}

Invoke-Scenario 'ratelimit-twice-exhausts' @{ exec = @('ratelimit', 'ratelimit', 'done') } -Assert {
  param($r)
  Test-Approved $r -By 'Gemini'
  Should ($r.Out -match 'Nemotron hit a quota or usage limit') 'a repeated rate limit did not mark the executor exhausted'
}

Invoke-Scenario 'codex-last-fallback' @{ exec = @('blocked', 'blocked', 'done') } -Assert {
  param($r)
  Test-Approved $r -By 'Codex'
  Should ((Get-ExecCalls $r)[2] -like 'exec Codex session= outcome=done msg=Read .ticket-work/08i/START.md*') "Codex got wrong arguments: $((Get-ExecCalls $r)[2])"
}

Invoke-Scenario 'codex-fix-round-resumes' @{ exec = @('blocked', 'blocked', 'red', 'done') } -Assert {
  param($r)
  Test-Approved $r -By 'Codex'
  Should ((Get-ExecCalls $r)[3] -like 'exec Codex session=codex-ses-* msg=Read .ticket-work/08i/FIX-1.md*') "Codex did not resume its session: $((Get-ExecCalls $r)[3])"
}

Invoke-Scenario 'chain-used-up-blocks' @{ exec = @('blocked', 'blocked', 'blocked') } -Assert {
  param($r)
  Test-Blocked $r
  Should ($r.Out -match 'fallback chain is used up') 'no chain-used-up message'
}

Invoke-Scenario 'all-executors-out' @{ exec = @('quota', 'quota', 'quota') } -Assert {
  param($r)
  Should ($r.Exit -ne 0) 'exit code 0'
  Should ($r.Out -match 'Every executor is out') 'no every-executor-out message'
  Should (($r.Status -join ' ') -eq '?? notes-baseline.txt') "main tree not clean: $($r.Status -join ', ')"
}

Invoke-Scenario 'suspect-amend-fixes' @{ exec = @('suspect', 'done'); planLocked = 'WRONG'; amendFix = $true } -Assert {
  param($r)
  Test-Approved $r
  $c = Get-ExecCalls $r
  Should ($c.Count -eq 2 -and $c[1] -like '*session=opencode-ses-0*AMEND-1.md*') "the executor did not continue with the amend: $($c -join ' | ')"
  Should (@($r.Calls | Where-Object { $_ -like 'claude resume=sess-0 *--amend*' }).Count -eq 1) 'the amend did not resume the planning session'
  Should ($r.Csv[-1].amends -eq '1') 'amends not recorded'
  $committed = git -C $r.Fx.Origin show main:tests/Scp3008.Core.Tests/AcceptanceTests.cs
  Should (($committed -join '') -notmatch 'WRONG') 'the amended locked test was not the one committed'
}

Invoke-Scenario 'locked-only-failures-amend' @{ exec = @('done', 'done', 'done', 'done'); planLocked = 'WRONG'; amendFix = $true } -Assert {
  param($r)
  Test-Approved $r
  Should ($r.Csv[-1].fix_rounds -eq '2' -and $r.Csv[-1].amends -eq '1') "fix_rounds/amends: $($r.Csv[-1].fix_rounds)/$($r.Csv[-1].amends)"
  Should ((Get-ExecCalls $r).Count -eq 4) "expected 4 executor calls, got $((Get-ExecCalls $r).Count)"
}

Invoke-Scenario 'second-suspect-blocks' @{ exec = @('suspect', 'suspect'); planLocked = 'WRONG'; amendFix = $false } -Assert {
  param($r)
  Test-Blocked $r
  Should ($r.Out -match 'second locked-test suspect') 'no second-suspect message'
}

Invoke-Scenario 'review-fix' @{ exec = @('done'); review = @('FIX') } -Assert {
  param($r)
  Test-Approved $r -Verdict 'FIX'
}

Invoke-Scenario 'review-reject-then-approve' @{ exec = @('done', 'done'); review = @('REJECT', 'APPROVE') } -Assert {
  param($r)
  Test-Approved $r
  $c = Get-ExecCalls $r
  Should ($c.Count -eq 2 -and $c[1] -like '*session=opencode-ses-0*REVIEW-NOTES.md*') "the notes did not go to the same executor session: $($c -join ' | ')"
  Should ($r.Csv[-1].rejects -eq '1') 'rejects not recorded'
  Should (@($r.Calls | Where-Object { $_ -like 'dotnet test' }).Count -eq 2) 'the gate did not run again after the reject'
}

Invoke-Scenario 'review-reject-twice-blocks' @{ exec = @('done', 'done'); review = @('REJECT', 'REJECT') } -Assert {
  param($r)
  Test-Blocked $r
  Should ($r.Out -match 'Rejected twice') 'no rejected-twice message'
}

Invoke-Scenario 'review-reject-plan-amends' @{ exec = @('done', 'done'); review = @('REJECT-PLAN', 'APPROVE'); amendFix = $true } -Assert {
  param($r)
  Test-Approved $r
  Should ($r.Csv[-1].amends -eq '1') 'REJECT-PLAN did not count as the amend'
  Should ((Get-ExecCalls $r)[1] -like '*AMEND-1.md*') 'the executor did not get the amended plan'
}

Invoke-Scenario 'reject-plan-after-amend-blocks' @{ exec = @('suspect', 'done'); planLocked = 'WRONG'; amendFix = $true; review = @('REJECT-PLAN') } -Assert {
  param($r)
  Test-Blocked $r
  Should ($r.Out -match 'REJECT-PLAN after the one amend') 'no REJECT-PLAN-after-amend message'
}

Invoke-Scenario 'usage-limit-resume' @{ exec = @('done'); limitOnPlan = $true } -Assert {
  param($r)
  Test-Approved $r
  Should (@($r.Calls | Where-Object { $_ -like 'claude resume=sess-0 *usage limit interrupted*' }).Count -eq 1) 'the session was not resumed after the limit'
  Should ($r.Out -match 'Usage limit hit') 'no usage-limit message'
}

Invoke-Scenario 'architect-touches-production-blocks' @{ exec = @('done'); planTouchesProduction = $true } -Assert {
  param($r)
  Test-Blocked $r -BeforeExecution
  Should ((Get-ExecCalls $r).Count -eq 0) 'an executor ran although the plan broke the rules'
  Should ((Get-Content (Join-Path $r.Fx.Repo 'src\impl.txt') -Raw).Trim() -eq 'empty') 'the architect''s production change was not undone'
}

Invoke-Scenario 'plan-blocked-stops' @{ planFinal = "Contradicts the spec.`nPLAN_BLOCKED: contradiction" } -Assert {
  param($r)
  Should ($r.Exit -ne 0) 'exit code 0'
  Should ((Get-ExecCalls $r).Count -eq 0) 'an executor ran'
  Should ($r.Csv[-1].verdict -eq 'blocked') 'CSV verdict not blocked'
}

Invoke-Scenario 'stage-end' @{ exec = @('done') } -Gh (New-Gh 1 $true) -Assert {
  param($r)
  Test-Approved $r
  Should (@($r.Calls | Where-Object { $_ -like 'claude * effort=high *' -or $_ -like 'claude *effort=high*' }).Count -ge 2) 'stage end did not use -StageEffort'
  Should ($r.Gh.milestones[0].state -eq 'closed') 'the milestone was not closed'
  Should ($r.Out -match 'stage review from 08h') "the stage start is not 08h"
}

Invoke-Scenario 'dry-run' @{ exec = @('done') } -RunnerArgs @('-DryRun') -Assert {
  param($r)
  Should ($r.Exit -eq 0) "exit code $($r.Exit)"
  Should (@($r.Calls | Where-Object { $_ -like 'claude*' -or $_ -like 'exec*' -or $_ -like 'dotnet*' }).Count -eq 0) 'the dry run ran something'
  Should ($r.Out -match '/plan-ticket 08i 66' -and $r.Out -match '/review-ticket 08i 66' -and $r.Out -match 'EXEC-PROMPT') 'the dry run did not print the prompts'
  Should (-not (Test-Path $r.Fx.Exec)) 'the dry run created the executor workspace'
}

Invoke-Scenario 'all-blocked-tickets' @{ } -Gh @{ issues = @(@{ number = 66; title = '08i: Canteen'; labels = @('ready-for-agent'); milestone = 1; state = 'OPEN'; blockedBy = @(65) },
    @{ number = 65; title = '08h: Open blocker'; labels = @(); milestone = $null; state = 'OPEN' }); milestones = @(@{ number = 1; title = 'S'; state = 'open' }); comments = @() } -Assert {
  param($r)
  Should ($r.Exit -ne 0) 'exit code 0 although every candidate is blocked'
  Should ($r.Out -match 'all are blocked') 'no all-blocked message'
}

# ---------------------------------------------------------------------------------------------------
$failed = @($results | Where-Object { -not $_.Ok })
Write-Host ("`n{0} scenario(s), {1} failed. Fixtures: {2}" -f $results.Count, $failed.Count, $base) -ForegroundColor $(if ($failed.Count) { 'Red' } else { 'Green' })
if (-not $failed.Count -and -not $Keep) { Remove-Item $base -Recurse -Force -ErrorAction SilentlyContinue }
exit $failed.Count
