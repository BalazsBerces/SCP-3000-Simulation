<#
.SYNOPSIS
  Ticket runner v5: the Sonnet-only version of v4. Every ticket, easy or hard, runs in one fresh headless
  Claude Code session on -Model (Sonnet 5.5).

.DESCRIPTION
  Loop:
    1. Pick the lowest-numbered open ticket whose "blocked by" issues are all closed.
    2. Run it on Claude ($Model, -Effort; -StageEffort for the last ticket of a build stage, which also
       reviews the whole stage). The agent implements, tests, commits to main, pushes and closes the issue.
       When the subscription limit stops the session, the runner waits for the reset and resumes it.
    3. Check that the issue is closed and the working tree is clean and pushed. If not, stop.
    4. At the end of each build stage, close its milestone and pause for a manual visual review (unless -NoStagePause).

  Logs: .ticket-logs/ (git-excluded locally). One CSV row per ticket: .ticket-logs/v5-runs.csv.

.EXAMPLE
  .\scripts\run-tickets-v5.ps1 -DryRun          # show the next ticket, run nothing
  .\scripts\run-tickets-v5.ps1 -Max 1           # run exactly one ticket
  .\scripts\run-tickets-v5.ps1 -NoStagePause    # never pause, stop only on failure
#>
param(
  [string]$Repo = 'BalazsBerces/SCP-3000-Simulation',
  # Project name in the prompt, the spec issue, and the files the project keeps its language and decisions in.
  [string]$Project = 'SCP-3008 simulation',
  [int]$SpecIssue = 1,
  [string]$GlossaryFile = 'GLOSSARY.md',
  [string]$AdrDir = 'docs/adr',
  # Project-specific rules (architecture, test and build commands, tools), added to the prompt as they are.
  # {GodotRunSeconds} in it is replaced by that parameter.
  [string]$ProjectRules = 'docs/ticket-rules.md',
  # Where agents propose new project rules; the runner never adds this file to the prompt.
  [string]$ProposedRules = 'docs/ticket-rules-proposed.md',
  [int]$Max = 0,
  [switch]$NoStagePause,
  [switch]$DryRun,
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$Effort = 'medium',
  # Stage-end tickets, which also review the whole stage.
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$StageEffort = 'medium',
  # Every ticket runs on this model.
  [string]$Model = 'claude-sonnet-5-5',
  # An agent's Godot run is killed after this many seconds (0 = never): the prompt's 5 minutes plus 15 s for the agent to stop it itself.
  [int]$GodotRunSeconds = 315,
  # How long after a usage-limit reset a Claude session is resumed.
  [int]$LimitResumeDelaySeconds = 120,
  [int]$MaxLimitResumes = 3,
  [double]$MaxLimitWaitHours = 8,
  # Context size (tokens) at which a Claude session compacts; it compacts about 35-40k below it.
  [string]$CompactWindow = '155000'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = New-Object Text.UTF8Encoding $false
# Inherited by the claude child processes only through this script.
$env:CLAUDE_CODE_AUTO_COMPACT_WINDOW = $CompactWindow
# No lingering build servers: they would inherit redirected handles and keep builds "running".
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$logDir = Join-Path $root '.ticket-logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$exclude = Join-Path $root '.git\info\exclude'
if (-not (Select-String -Path $exclude -Pattern '^\.ticket-logs/' -Quiet)) { Add-Content $exclude '.ticket-logs/' }
$emptyInput = Join-Path $logDir 'empty-stdin.txt'
if (-not (Test-Path $emptyInput)) { New-Item -ItemType File $emptyInput | Out-Null }
$csvFile = Join-Path $logDir 'v5-runs.csv'
$utf8 = New-Object Text.UTF8Encoding $false

# ---------------------------------------------------------------------------------------------------
# Helpers

function Write-Utf8([string]$Path, [string]$Text) {
  New-Item -ItemType Directory -Force (Split-Path -Parent $Path) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, $utf8)
}

# Uncommitted paths outside .claude/, each with a fingerprint ('deleted' when it is gone). A tracked file's
# fingerprint is its uncommitted change (the added and removed lines against HEAD), not its content, so a
# file that already had someone else's edits stays unchanged when the ticket commits only its own hunks.
function Get-DirtyState {
  $state = @{}
  git -c core.quotepath=false status --porcelain -uall -- . ':!.claude' | Where-Object { $_ } | ForEach-Object {
    $p = $_.Substring(3)
    if ($p -match ' -> (.+)$') { $p = $Matches[1] }
    $p = $p.Trim('"')
    $state[$p] = if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { 'deleted' }
      elseif ($_.StartsWith('??')) { (Get-FileHash -LiteralPath $p -Algorithm SHA1).Hash }
      else {
        $change = (git diff -U0 --no-color HEAD -- $p | Where-Object { $_ -match '^[-+]' -and $_ -notmatch '^(---|\+\+\+) ' }) -join "`n"
        $sha = [System.Security.Cryptography.SHA1]::Create()
        [BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($change)))
      }
  }
  $state
}

# The paths the ticket changed: uncommitted now, and not uncommitted with the same content when it started.
function Get-TicketPaths($Baseline) {
  $now = Get-DirtyState
  @($now.Keys | Where-Object { -not $Baseline.ContainsKey($_) -or $Baseline[$_] -ne $now[$_] } | Sort-Object)
}

# ---------------------------------------------------------------------------------------------------
# Picking the ticket and its agent

function Get-NextTicket {
  $open = gh issue list -R $Repo --state open --label ready-for-agent --limit 200 --json number,title,labels,milestone | ConvertFrom-Json
  $milestones = @{}
  gh api "repos/$Repo/milestones?state=all&per_page=100" | ConvertFrom-Json | ForEach-Object { $_ } | ForEach-Object { $milestones[[int]$_.number] = $_ }
  # A ticket (it has a stage or a difficulty, unlike the spec) without an 'NN: ' title would never run; say so.
  $open | ForEach-Object { $_ } | Where-Object { $_.title -notmatch '^\d\d[a-z]?: ' -and
      ($_.milestone -or @($_.labels | Where-Object { $_.name -in 'hard', 'easy' }).Count) } |
    ForEach-Object { Write-Warning "Skipping #$($_.number) '$($_.title)': its title needs an 'NN: ' or 'NNa: ' prefix to be scheduled." }
  # Titles start with 'NN: ', or 'NNa: ' for a ticket inserted after NN (it runs after NN, before NN+1).
  $tickets = $open | ForEach-Object { $_ } | Where-Object { $_.title -match '^(\d\d)([a-z]?): ' } |
    ForEach-Object {
      $null = $_.title -match '^(\d\d)([a-z]?): '
      $n = [int]$Matches[1]; $suffix = $Matches[2]
      $difficulty = @($_.labels | ForEach-Object { $_.name } | Where-Object { $_ -in 'hard', 'easy' })
      $stage = if ($_.milestone) { $milestones[[int]$_.milestone.number] }
      # The last open issue of a still-open milestone ends the stage; a closed milestone is already reviewed.
      $isStageEnd = [bool]($stage -and $stage.state -eq 'open' -and $stage.open_issues -eq 1)
      [pscustomobject]@{ Issue = $_.number; Ticket = $n; Label = $Matches[1] + $suffix; Title = $_.title
        IsStageEnd = $isStageEnd; Stage = $stage; Difficulty = ($difficulty | Select-Object -First 1) }
    } |
    Sort-Object Ticket, Label
  if (-not $tickets) { return $null }
  foreach ($t in $tickets) {
    # PS 5.1's ConvertFrom-Json emits a JSON array as one object; ForEach-Object unrolls it.
    $openBlockers = @(gh api "repos/$Repo/issues/$($t.Issue)/dependencies/blocked_by" | ConvertFrom-Json |
      ForEach-Object { $_ } | Where-Object { $_.state -eq 'open' })
    if ($openBlockers.Count -eq 0) { return $t }
  }
  return 'ALL_BLOCKED'
}

# Always Claude on $Model; only the effort depends on the ticket.
function Get-Choice($t) {
  [pscustomobject]@{ Agent = 'claude'; Model = $Model; Effort = $(if ($t.IsStageEnd) { $StageEffort } else { $Effort }) }
}

# ---------------------------------------------------------------------------------------------------
# Prompts

function Get-ReviewRule($t, $choice) {
  if (-not $t.IsStageEnd) {
    return "6. Skip the /spec-review step for this ticket; the code is reviewed once at the end of each build stage."
  }
  $bugReview = 'Then run /code-review medium on the same diff for correctness bugs'
  # The stage starts at the lowest-numbered ticket of its milestone, closed ones included.
  $first = gh issue list -R $Repo --state all --milestone $t.Stage.title --limit 200 --json title | ConvertFrom-Json |
    ForEach-Object { $_ } | Where-Object { $_.title -match '^(\d\d[a-z]?): ' } |
    ForEach-Object { $null = $_.title -match '^(\d\d[a-z]?): '; $Matches[1] } | Sort-Object | Select-Object -First 1
  "6. This ticket completes the build stage '$($t.Stage.title)' (tickets $first-$($t.Label)). Instead of reviewing only this ticket, run /spec-review on the whole stage: the diff from the parent of ticket $first's commit (find it with git log --grep '^${first}: ') up to and including your uncommitted changes. $bugReview. Fix the real findings of both, rerun the tests, and include the fixes in this ticket's commit."
}

function Get-Prompt($t, $choice, [string]$Head) {
  $preFile = ".ticket-logs/$($t.Label)-preexisting.txt"
  $stageRule = if ($script:preexisting.Count) { "stage only the files you changed for this ticket, by name (never git add -A or git add .)" }
    else { 'stage everything except the .claude/ directory' }
  # Uncommitted work already in the tree is someone else's; the runner keeps it out of the ticket's commit.
  $preRule = if ($script:preexisting.Count) {
    "`nThe uncommitted paths listed in $preFile were already there when you started: someone else's work in progress. Never stage, commit, revert, stash or delete them; if this ticket must change one of them, it becomes part of the ticket.`n"
  } else { "`n" }
  if ($script:resumed.Count) {
    $preRule += "An earlier attempt at this ticket stopped unfinished and left its work uncommitted in the paths listed in .ticket-logs/$($t.Label)-unfinished.txt. Check that work first, then build on it or replace it; it is part of this ticket.`n"
  }
  $coAuthor = 'Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>'
  $finish = @"
7. On success:
   - $stageRule;
   - commit to main with the message "$($t.Title)", a blank line, "Closes #$($t.Issue)", a blank line, and "$coAuthor";
   - push to origin main;
   - make sure issue #$($t.Issue) is closed;
   - post a short closing comment: what was built, the test results, and what needs human visual review.
8. If you cannot finish (an unresolvable ambiguity, or a failure you cannot fix), do NOT commit broken code and do NOT close the issue. Comment on issue #$($t.Issue) explaining the blocker and what is done so far.
"@
  $suiteRule = 'Run the whole test suite once, just before you commit.'
  $rulesPath = Join-Path $root $ProjectRules
  $projectRules = if (Test-Path $rulesPath) {
    "Rules of this project (from $ProjectRules; they add to the rules above):`n" +
    ((Get-Content $rulesPath -Raw -Encoding UTF8).Replace('{GodotRunSeconds}', "$GodotRunSeconds").Trim()) + "`n"
  } else { '' }
  $start = "/implement ticket $($t.Label) of the ${Project}: GitHub issue #$($t.Issue) in $Repo."
@"
$start
The spec is issue #$SpecIssue. Read the ticket (gh issue view $($t.Issue) --comments), $GlossaryFile, every ADR in $AdrDir/, and the existing code.

Rules for this run:
1. This is an unattended run. Nobody will answer questions, so make reasonable decisions yourself and record them.
2. Implement ONLY this ticket so that every acceptance criterion is met. Follow the spec's architecture.
3. Test through the project's public seam. $suiteRule Also run the project's build. Everything must pass.
4. For criteria that cannot be checked automatically (visuals, audio, fps), verify what you can (for example, run the app or the benchmark and capture a screenshot or fps numbers), and list what still needs human review. A check that fails is a failure to fix, never "could not run".
5. Use the glossary vocabulary. If a new domain term is settled, add it to $GlossaryFile. Write an ADR in $AdrDir/ only for decisions that are hard to reverse, surprising, and the result of a real trade-off. Keep files UTF-8 and leave existing non-ASCII characters intact.
$(Get-ReviewRule $t $choice)
$finish
9. Keep the context small; everything in it is re-read on every later turn:
   - write the full output of test runs, builds, Godot runs and benchmarks to a file under .ticket-logs/ and print only the summary and failures;
   - capture screenshots at most 1280 px wide, look at each one once, and do not reread images you already judged.
${projectRules}${preRule}If you hit a problem that cost you real time and that another ticket would likely hit too (you worked around a tool, a path, a missing step), write the rule that would have prevented it: at most two sentences, the cause and the fix. Append it to $ProposedRules as a line "- <ticket>: <rule>" and repeat it in your closing comment as a line starting with "Suggested rule:". Do not edit $ProjectRules; a human decides what moves there. Skip this if nothing like that happened.
Never modify issue #$SpecIssue or other tickets, never force-push, and never rewrite history.
End your final message with exactly one line: TICKET_DONE or TICKET_BLOCKED: <reason>.
"@
}

# ---------------------------------------------------------------------------------------------------
# Claude

# A resumed session reports the totals of the whole session; only the growth since the last report is new.
$sessionTotals = @{}

function Get-ClaudeUsage([string]$SessionId, $result) {
  $models = @($result.modelUsage.PSObject.Properties | ForEach-Object { $_.Value })
  $sum = { param($name) [double]($models | Measure-Object -Property $name -Sum).Sum }
  $now = @{ Tokens = (& $sum inputTokens) + (& $sum cacheCreationInputTokens) + (& $sum cacheReadInputTokens) + (& $sum outputTokens)
    Output = (& $sum outputTokens); Cost = [double]$result.total_cost_usd }
  $before = $null
  if ($SessionId) { $before = $sessionTotals[$SessionId]; $sessionTotals[$SessionId] = $now }
  if (-not $before -or $now.Tokens -lt $before.Tokens) { return $now }
  @{ Tokens = $now.Tokens - $before.Tokens; Output = $now.Output - $before.Output; Cost = $now.Cost - $before.Cost }
}

function Invoke-Claude {
  # The ForEach-Object block below runs in this scope, so its assignments reach $final, $sessionId and the stats.
  [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '')]
  param($stats, $choice, [string]$Log, [string]$Prompt, [string]$ResumeId)
  $cliArgs = @('-p', '--dangerously-skip-permissions', '--output-format', 'stream-json', '--verbose',
    '--settings', (Join-Path $PSScriptRoot 'ticket-settings.json'), '--model', $choice.Model, '--effort', $choice.Effort)
  if ($ResumeId) { $cliArgs += @('--resume', $ResumeId) }
  $final = ''; $sessionId = $ResumeId; $resetsAt = $null
  $firstLimits = $null; $lastLimits = $null; $lastResult = $null; $turns = 0; $minutes = 0.0
  # The prompt goes in as an argument: piped stdin does not expand the /implement slash command.
  # PS 5.1 does not escape embedded double quotes in native arguments, so swap them for single quotes.
  & claude @cliArgs $Prompt.Replace('"', "'") | ForEach-Object {
    $line = "$_"
    Add-Content -Path $Log -Value $line -Encoding UTF8
    try { $ev = $line | ConvertFrom-Json } catch { return }
    if ($ev.session_id) { $sessionId = $ev.session_id }
    if ($ev.type -eq 'rate_limit_event' -and $ev.rate_limit_info.status -eq 'rejected') { $resetsAt = $ev.rate_limit_info.resetsAt }
    if ($ev.type -eq 'assistant') {
      foreach ($c in $ev.message.content) {
        if ($c.type -eq 'text' -and $c.text.Trim()) { Write-Host ('  > ' + ($c.text.Trim() -split "`n")[0]) -ForegroundColor Gray }
        elseif ($c.type -eq 'tool_use') { Write-Host "  - $($c.name)" -ForegroundColor DarkGray }
      }
    } elseif ($ev.type -eq 'rate_limit_event' -and $ev.rate_limit_info.unifiedWindows) {
      $lastLimits = $ev.rate_limit_info.unifiedWindows
      if (-not $firstLimits) { $firstLimits = $lastLimits }
    } elseif ($ev.type -eq 'result') {
      # A session that waits for background agents emits a result each time it stops; turns and time add up.
      $final = $ev.result; $lastResult = $ev
      $turns += $ev.num_turns; $minutes += $ev.duration_ms / 60000
    }
  }
  $exit = $LASTEXITCODE
  if ($lastResult) {
    $usage = Get-ClaudeUsage $sessionId $lastResult
    $stats.ClaudeTokens += $usage.Tokens; $stats.ClaudeCost += $usage.Cost
    # On a subscription, the cost is only an estimate at API list prices; nothing is billed.
    Write-Host ('  turns: {0}, {1:N0} min, API-price equivalent: ${2:N2}' -f $turns, $minutes, $usage.Cost) -ForegroundColor DarkCyan
    if ($lastLimits) {
      Write-Host ('  subscription limits: 5-hour {0:P0} -> {1:P0}, weekly {2:P0} -> {3:P0}' -f `
        $firstLimits.five_hour.utilization, $lastLimits.five_hour.utilization,
        $firstLimits.seven_day.utilization, $lastLimits.seven_day.utilization) -ForegroundColor DarkCyan
    }
    Write-Host ('  tokens: {0:N1}M total, {1:N0}k output' -f ($usage.Tokens / 1e6), ($usage.Output / 1e3)) -ForegroundColor DarkCyan
  }
  if ($firstLimits -and -not $stats.LimitsBefore) { $stats.LimitsBefore = $firstLimits }
  if ($lastLimits) { $stats.LimitsAfter = $lastLimits }
  if ($exit -ne 0 -and -not $resetsAt) { throw "Claude exited with code $exit. Log: $Log" }
  [pscustomobject]@{ Final = $final; SessionId = $sessionId; ResetsAt = $resetsAt }
}

# Runs the ticket; when the subscription limit stops it, waits for the reset and resumes the same session.
function Invoke-ClaudeTicket($t, $choice, $stats, [string]$Log, [string]$Prompt) {
  $run = Invoke-Claude $stats $choice $Log $Prompt $null
  for ($i = 1; $run.ResetsAt -and $i -le $MaxLimitResumes; $i++) {
    $resume = [DateTimeOffset]::FromUnixTimeSeconds([long]$run.ResetsAt).LocalDateTime.AddSeconds($LimitResumeDelaySeconds)
    $wait = $resume - (Get-Date)
    if ($wait.TotalHours -gt $MaxLimitWaitHours) {
      Write-Host "  Usage limit resets at $resume, more than $MaxLimitWaitHours h away; not waiting." -ForegroundColor Yellow
      break
    }
    Write-Host "  Usage limit hit. Waiting until $resume to resume session $($run.SessionId) ($i/$MaxLimitResumes)." -ForegroundColor Yellow
    if ($wait.TotalSeconds -gt 0) { Start-Sleep -Seconds ([int]$wait.TotalSeconds) }
    $run = Invoke-Claude $stats $choice $Log 'The usage limit interrupted you. Continue this ticket from where you stopped, following the same rules.' $run.SessionId
  }
  $run.Final
}

# ---------------------------------------------------------------------------------------------------
# Bookkeeping

function Add-CsvRow($t, $choice, $stats, [double]$Minutes, [string]$Result) {
  $lb = $stats.LimitsBefore; $la = $stats.LimitsAfter
  [pscustomobject][ordered]@{
    date = (Get-Date).ToString('yyyy-MM-dd HH:mm'); ticket = $t.Label; issue = $t.Issue
    difficulty = $(if ($t.Difficulty) { $t.Difficulty } else { 'unlabelled' }); stage_end = $t.IsStageEnd
    agent = $choice.Agent; model = $choice.Model; effort = $choice.Effort; minutes = [Math]::Round($Minutes, 1)
    claude_usd = [Math]::Round($stats.ClaudeCost, 2); claude_tokens = [long]$stats.ClaudeTokens
    claude_5h_before = $(if ($lb) { $lb.five_hour.utilization }); claude_5h_after = $(if ($la) { $la.five_hour.utilization })
    claude_week_before = $(if ($lb) { $lb.seven_day.utilization }); claude_week_after = $(if ($la) { $la.seven_day.utilization })
    result = $Result
  } | Export-Csv -Path $csvFile -Append -NoTypeInformation -Encoding UTF8
}

function New-Stats {
  @{ ClaudeTokens = 0.0; ClaudeCost = 0.0; LimitsBefore = $null; LimitsAfter = $null }
}

# ---------------------------------------------------------------------------------------------------
# Godot watchdog

# A Godot started by an agent (any depth below the runner) is killed once it outlives $GodotRunSeconds,
# whether or not the agent kept to its own limit. The user's own editor is not below the runner at all.
function Start-GodotWatchdog {
  if ($GodotRunSeconds -le 0) { return $null }
  Start-Job -ArgumentList $PID, $GodotRunSeconds -ScriptBlock {
    param([int]$Runner, [int]$Limit)
    function Stop-Tree([int]$Id, $Children) {
      foreach ($c in @($Children[$Id])) { if ($c) { Stop-Tree $c $Children } }
      Stop-Process -Id $Id -Force -ErrorAction SilentlyContinue
    }
    while (Get-Process -Id $Runner -ErrorAction SilentlyContinue) {
      Start-Sleep -Seconds 5
      $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
      $parent = @{}; $name = @{}; $children = @{}
      foreach ($p in $all) {
        $id = [int]$p.ProcessId; $parent[$id] = [int]$p.ParentProcessId; $name[$id] = [string]$p.Name
        $children[[int]$p.ParentProcessId] = @($children[[int]$p.ParentProcessId]) + $id
      }
      foreach ($g in $all) {
        $id = [int]$g.ProcessId
        # The topmost Godot of a run only (the console wrapper starts the real one), and not the gate's.
        if ($name[$id] -notlike 'Godot*' -or "$($name[$parent[$id]])" -like 'Godot*' -or $parent[$id] -eq $Runner) { continue }
        if (((Get-Date) - $g.CreationDate).TotalSeconds -lt $Limit) { continue }
        $a = $parent[$id]; $below = $false
        for ($depth = 0; $depth -lt 40 -and $a -and $parent.ContainsKey($a); $depth++) {
          if ($a -eq $Runner) { $below = $true; break }
          $a = $parent[$a]
        }
        if (-not $below) { continue }
        Stop-Tree $id $children
        "$(Get-Date -Format HH:mm:ss) killed Godot (pid $id) after $Limit s: $($g.CommandLine)"
      }
    }
  }
}

function Show-WatchdogNotes {
  if ($script:watchdog) { Receive-Job $script:watchdog | ForEach-Object { Write-Host "  [watchdog] $_" -ForegroundColor Yellow } }
}

# ---------------------------------------------------------------------------------------------------
# Main loop

$done = 0
$script:watchdog = Start-GodotWatchdog
while ($true) {
  if ($Max -gt 0 -and $done -ge $Max) { Write-Host "Reached -Max $Max." -ForegroundColor Cyan; break }

  $t = Get-NextTicket
  if ($null -eq $t) { Write-Host 'All tickets are closed. Done.' -ForegroundColor Green; break }
  if ($t -eq 'ALL_BLOCKED') { Write-Host 'Open tickets remain, but all are blocked. Stopping.' -ForegroundColor Yellow; exit 1 }

  $choice = Get-Choice $t
  $difficulty = if ($t.Difficulty) { $t.Difficulty } else { 'unlabelled' }
  $what = "Claude $($choice.Model), effort $($choice.Effort)"
  Write-Host "`n=== $($t.Title)  (#$($t.Issue), $difficulty$(if ($t.IsStageEnd) { ', stage end + review' }), $what) ===" -ForegroundColor Cyan
  if ($DryRun) { Write-Host 'Dry run: nothing started.' -ForegroundColor Yellow; break }

  # Uncommitted work already in the tree stays out of the ticket: out of its commit and its clean-tree check.
  $baseline = Get-DirtyState
  # Paths an earlier, unfinished attempt at this ticket left behind belong to this ticket again.
  $unfinishedFile = Join-Path $logDir "$($t.Label)-unfinished.txt"
  $script:resumed = @(if (Test-Path $unfinishedFile) { Get-Content $unfinishedFile -Encoding UTF8 | Where-Object { $_ -and $baseline.ContainsKey($_) } })
  foreach ($p in $script:resumed) { $baseline.Remove($p) }
  if ($script:resumed.Count) { Write-Host "  An earlier attempt left $($script:resumed.Count) uncommitted path(s); this attempt takes them over." -ForegroundColor DarkCyan }
  $script:preexisting = @($baseline.Keys | Sort-Object)
  if ($script:preexisting.Count) {
    Write-Utf8 (Join-Path $logDir "$($t.Label)-preexisting.txt") (($script:preexisting -join "`n") + "`n")
    Write-Host "  $($script:preexisting.Count) uncommitted path(s) already in the tree; the ticket leaves them alone." -ForegroundColor DarkCyan
  }

  $stats = New-Stats
  $started = Get-Date
  $head = (git rev-parse HEAD).Trim()
  $log = Join-Path $logDir ('ticket-{0}-{1:yyyyMMdd-HHmmss}.jsonl' -f $t.Label, (Get-Date))
  $final = Invoke-ClaudeTicket $t $choice $stats $log (Get-Prompt $t $choice $head)

  $state = gh issue view $t.Issue -R $Repo --json state --jq '.state'
  Show-WatchdogNotes
  $dirty = @(Get-TicketPaths $baseline)
  git fetch -q origin
  $unpushed = git rev-list --count origin/main..main
  $minutes = ((Get-Date) - $started).TotalMinutes

  if ($state -ne 'CLOSED' -or $dirty.Count -or [int]$unpushed -gt 0) {
    Add-CsvRow $t $choice $stats $minutes 'unfinished'
    # The half-done work stays uncommitted; the next attempt at this ticket takes it over.
    if ($dirty.Count) { Write-Utf8 $unfinishedFile (($dirty -join "`n") + "`n") }
    Write-Host "Ticket $($t.Label) did not finish cleanly (issue: $state, uncommitted changes: $($dirty.Count -gt 0), unpushed commits: $unpushed)." -ForegroundColor Red
    Write-Host "Agent's last message:`n$final" -ForegroundColor Red
    Write-Host "Logs: $logDir"
    exit 1
  }

  Add-CsvRow $t $choice $stats $minutes 'done'
  Remove-Item -LiteralPath $unfinishedFile -ErrorAction SilentlyContinue
  Write-Host ("Ticket {0} done in {1:N0} min." -f $t.Label, $minutes) -ForegroundColor Green
  $done++

  # Closing the milestone marks the stage as reviewed, so tickets added to it later skip the stage review.
  if ($t.IsStageEnd) { gh api -X PATCH "repos/$Repo/milestones/$($t.Stage.number)" -f state=closed | Out-Null }

  if (-not $NoStagePause -and $t.IsStageEnd) {
    Write-Host "`nStage '$($t.Stage.title)' is complete. Check it visually (see the closing comments on the issues)." -ForegroundColor Yellow
    $answer = Read-Host 'Continue with the next stage? (Y/n)'
    if ($answer -match '^[nN]') { break }
  }
}
