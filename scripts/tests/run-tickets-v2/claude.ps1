# Fake claude -p (stream-json): plays the architect (/plan-ticket, --amend) and the reviewer (/review-ticket)
# as the scenario says, including one usage-limit rejection when scenario.limitOnPlan is set.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stub-lib.ps1')
$a = @($args | ForEach-Object { "$_" })
$prompt = $a[-1]
$resume = Get-ArgAfter $a '--resume'
$sc = Get-Scenario
Add-Content -Path (Join-Path $StubState 'claude-prompts.txt') -Value $prompt
Write-Call ("claude resume=$resume effort=$(Get-ArgAfter $a '--effort') model=$(Get-ArgAfter $a '--model') prompt=" + (($prompt -split "`n")[0]))

function Emit($o) { $o | ConvertTo-Json -Compress -Depth 10 }
$session = if ($resume) { $resume } else { 'sess-' + (Get-NextIndex 'claude') }
Emit @{ type = 'system'; subtype = 'init'; session_id = $session }
Emit @{ type = 'rate_limit_event'; rate_limit_info = @{ status = 'allowed'; unifiedWindows = @{ five_hour = @{ utilization = 0.1 }; seven_day = @{ utilization = 0.2 } } } }

$pending = Join-Path $StubState 'pending-prompt.txt'
if ($prompt -like 'The usage limit interrupted you*') { $prompt = Get-Content $pending -Raw }
elseif ((($sc.limitOnPlan -and $prompt -like '/plan-ticket*') -or ($sc.limitOnImplement -and $prompt -like '/implement*')) -and
  -not (Test-Path (Join-Path $StubState 'limited'))) {
  Set-Content -Path $pending -Value $prompt -NoNewline
  Set-Content -Path (Join-Path $StubState 'limited') -Value 1
  Emit @{ type = 'rate_limit_event'; rate_limit_info = @{ status = 'rejected'; resetsAt = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() } }
  exit 1
}

$first = ($prompt -split "`n")[0].Trim()
$tok = @($first -split '\s+')
$label = $tok[1]; $issue = $tok[2]
$work = Join-Path (Get-Location) ".ticket-work\$label"
$locked = Join-Path (Get-Location) ($LockedTest -replace '/', '\')

function Write-LockedTest([bool]$Wrong) {
  New-Item -ItemType Directory -Force (Split-Path -Parent $locked) | Out-Null
  $mark = if ($Wrong) { 'WRONG' } else { 'ok' }
  Set-Content -Path $locked -Value "public class AcceptanceTests { /* expects good: $mark */ }"
}

if ($first -like '/implement*') {
  # v3: the whole ticket in one session, committed, pushed and closed by the agent.
  $label = $tok[2]
  $issue = if ($prompt -match 'GitHub issue #(\d+)') { $Matches[1] }
  if ((Get-Step 'implement' 'done') -eq 'done') {
    Set-Content -Path (Join-Path (Get-Location) 'src\impl.txt') -Value "good $label claude"
    # The runner swaps double quotes for single quotes in the prompt.
    $title = if ($prompt -match "message '([^']+)'") { $Matches[1] } else { 'untitled' }
    $co = if ($prompt -match "'(Co-Authored-By: [^']+)'") { $Matches[1] } else { '' }
    $msgFile = Join-Path $StubState 'commit-msg.txt'
    Set-Content -Path $msgFile -Value "$title`n`nCloses #$issue`n`n$co"
    git add -A -- . ':!.claude'
    # Like a real agent, leave the uncommitted work that was there before the ticket out of its commit.
    if ($prompt -match '(\.ticket-logs/\S+-preexisting\.txt)') { $pre = @(Get-Content $Matches[1] | Where-Object { $_ }); if ($pre.Count) { git reset -q -- $pre } }
    git commit -q -F $msgFile
    git push -q origin main
    & (Join-Path $PSScriptRoot 'gh.ps1') issue close $issue | Out-Null
    $final = "Implemented.`nTICKET_DONE"
  } else { $final = "Cannot do it.`nTICKET_BLOCKED: stub blocker" }
} elseif ($first -like '/plan-ticket*--amend*') {
  if ($sc.amendFix) { Write-LockedTest $false; $final = "I corrected the acceptance test.`nAMEND_CHANGED: fixed the expected value" }
  else { $final = "The test is right; implement the plan.`nAMEND_CONFIRMED: the test is right" }
} elseif ($first -like '/plan-ticket*') {
  New-Item -ItemType Directory -Force $work | Out-Null
  Set-Content -Path (Join-Path $work 'PLAN.md') -Value "# Plan for $label`nWrite good into src/impl.txt."
  Set-Content -Path (Join-Path $work 'locked.txt') -Value $LockedTest
  $captures = if ($sc.captures) { ".ticket-work/$label/shot.png" } else { '' }
  Set-Content -Path (Join-Path $work 'captures.txt') -Value $captures
  Write-LockedTest ([bool]($sc.planLocked -eq 'WRONG'))
  if ($sc.planTouchesProduction) { Set-Content -Path (Join-Path (Get-Location) 'src\impl.txt') -Value 'architect' }
  $final = if ($sc.planFinal) { $sc.planFinal } else { "Plan written.`nPLAN_DONE" }
} elseif ($first -like '/review-ticket*') {
  $verdict = Get-Step 'review' 'APPROVE'
  if ($verdict -in 'APPROVE', 'FIX') {
    if ($verdict -eq 'FIX') { Add-Content -Path (Join-Path (Get-Location) 'src\impl.txt') -Value 'reviewer fix' }
    $title = if ($prompt -match 'title: ([^\r\n]+)') { $Matches[1].Trim() } else { 'untitled' }
    $co = @($prompt -split "`r?`n" | Where-Object { $_ -match '^\s+Co-Authored-By:' } | ForEach-Object { $_.Trim() })
    $msgFile = Join-Path $StubState 'commit-msg.txt'
    Set-Content -Path $msgFile -Value ("$title`n`nCloses #$issue`n`n" + ($co -join "`n"))
    git add -- src tests
    git commit -q -F $msgFile
    git push -q origin main
    & (Join-Path $PSScriptRoot 'gh.ps1') issue close $issue | Out-Null
    $final = "Reviewed.`n$verdict"
  } elseif ($verdict -eq 'REJECT') { $final = "The canteen has no tables.`nREJECT: add the tables" }
  else { $final = "The locked test pins the wrong number.`nREJECT-PLAN: the expected value is wrong" }
} else { $final = 'unknown prompt' }

Emit @{ type = 'assistant'; session_id = $session; message = @{ content = @(@{ type = 'text'; text = $final }) } }
# Like the real CLI, a resumed session reports the totals of the whole session, not just this call.
$usageFile = Join-Path $StubState "usage-$session.txt"
$k = if (Test-Path $usageFile) { [int](Get-Content $usageFile) + 1 } else { 1 }
Set-Content -Path $usageFile -Value $k
Emit @{ type = 'result'; session_id = $session; result = $final; num_turns = 3; duration_ms = 60000; total_cost_usd = 0.5 * $k
  modelUsage = @{ 'claude-opus-5-5' = @{ inputTokens = 1000 * $k; cacheCreationInputTokens = 0; cacheReadInputTokens = 5000 * $k; outputTokens = 200 * $k } } }
exit 0
