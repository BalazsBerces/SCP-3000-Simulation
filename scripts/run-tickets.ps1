<#
.SYNOPSIS
  Works through the ready-for-agent tickets one by one, each in a fresh headless Claude Code session
  (the equivalent of /clear between tickets).

.DESCRIPTION
  Loop:
    1. Pick the lowest-numbered open ticket whose "blocked by" issues are all closed.
    2. Run `claude -p` on it with full permissions. The agent implements it, runs the tests,
       commits to main, pushes, and closes the issue.
    3. Check that the issue is closed and the working tree is clean and pushed. If not, stop.
    4. At the end of each build stage, close its milestone and pause for a manual visual review (unless -NoStagePause).

  Build stages are GitHub milestones. A ticket ends its stage when it is the last open issue of an open
  milestone; that ticket reviews the whole stage. A closed milestone is already reviewed, so tickets added
  to it later run without a stage review.

  Logs are written to .ticket-logs/ (git-excluded locally).

.EXAMPLE
  .\scripts\run-tickets.ps1                 # run until done, pausing at stage boundaries
  .\scripts\run-tickets.ps1 -Max 1          # run exactly one ticket
  .\scripts\run-tickets.ps1 -NoStagePause   # never pause, stop only on failure
  .\scripts\run-tickets.ps1 -Effort high    # more effort for every ticket (stage ends use -StageEffort, default high)
#>
param(
  [string]$Repo = 'BalazsBerces/SCP-3000-Simulation',
  [int]$Max = 0,
  [switch]$NoStagePause,
  # Overrides the per-ticket model choice when set.
  [string]$Model = '',
  # Tickets labelled 'easy' run on $EasyModel; 'hard', unlabelled and stage-end tickets on $HardModel.
  [string]$HardModel = 'opus',
  [string]$EasyModel = 'sonnet',
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$Effort = 'medium',
  # Stage-end tickets also review the whole stage, so they get more effort.
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$StageEffort = 'high',
  # Context size (tokens) at which a ticket session compacts itself; 'auto' keeps the model default.
  # 300k leaves normal tickets alone (they peak below 250k) and only catches runaway sessions.
  [string]$CompactWindow = '300k'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
# Inherited by every claude child process, so it affects only the ticket runs.
$env:CLAUDE_CODE_AUTO_COMPACT_WINDOW = $CompactWindow

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$logDir = Join-Path $root '.ticket-logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$exclude = Join-Path $root '.git\info\exclude'
if (-not (Select-String -Path $exclude -Pattern '^\.ticket-logs/' -Quiet)) { Add-Content $exclude '.ticket-logs/' }

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

function Get-ReviewRule($t) {
  if (-not $t.IsStageEnd) {
    return "6. Skip the /spec-review step for this ticket; the code is reviewed once at the end of each build stage."
  }
  # The stage starts at the lowest-numbered ticket of its milestone, closed ones included.
  $first = gh issue list -R $Repo --state all --milestone $t.Stage.title --limit 200 --json title | ConvertFrom-Json |
    ForEach-Object { $_ } | Where-Object { $_.title -match '^(\d\d[a-z]?): ' } |
    ForEach-Object { $null = $_.title -match '^(\d\d[a-z]?): '; $Matches[1] } | Sort-Object | Select-Object -First 1
  "6. This ticket completes the build stage '$($t.Stage.title)' (tickets $first-$($t.Label)). Instead of reviewing only this ticket, run /spec-review on the whole stage: the diff from the parent of ticket $first's commit (find it with git log --grep '^${first}: ') up to and including your uncommitted changes. Fix the real findings, rerun the tests, and include the fixes in this ticket's commit."
}

function Get-Prompt($t) {
@"
/implement ticket $($t.Label) of the SCP-3008 simulation: GitHub issue #$($t.Issue) in $Repo.
The spec is issue #1. Read the ticket (gh issue view $($t.Issue) --comments), GLOSSARY.md, every ADR in docs/adr/, and the existing code.

Project rules for this run:
1. This is an unattended run. Nobody will answer questions, so make reasonable decisions yourself and record them.
2. Implement ONLY this ticket so that every acceptance criterion is met. Follow the spec's architecture: the engine-independent C# simulation core behind one facade, and the Godot presentation layer that only reads from it.
3. Test seam: facade-level tests (scenario -> advance time -> assert on snapshot/events). Also build the Godot project with the console executable in headless mode. Godot 4.7.2 .NET is NOT on PATH; find it with a glob under %LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\ (use the *_console.exe). Everything must pass.
4. For criteria that cannot be checked automatically (visuals, audio, fps), verify what you can (for example, run the game or the benchmark and capture a screenshot or fps numbers), and list what still needs human review.
5. Use the glossary vocabulary. If a new domain term is settled, add it to GLOSSARY.md. Write an ADR only for decisions that are hard to reverse, surprising, and the result of a real trade-off.
$(Get-ReviewRule $t)
7. On success:
   - stage everything except the .claude/ directory;
   - commit to main with the message "$($t.Title)", a blank line, "Closes #$($t.Issue)", a blank line, and "Co-Authored-By: Claude $(if ((Get-TicketModel $t) -match 'sonnet') { 'Sonnet' } else { 'Opus' }) 5.5 <noreply@anthropic.com>";
   - push to origin main;
   - make sure issue #$($t.Issue) is closed;
   - post a short closing comment: what was built, the test results, and what needs human visual review.
8. If you cannot finish (an unresolvable ambiguity, or a failure you cannot fix), do NOT commit broken code and do NOT close the issue. Comment on issue #$($t.Issue) explaining the blocker and what is done so far.
9. Keep the context small; everything in it is re-read on every later turn:
   - write the full output of test runs, builds, Godot runs and benchmarks to a file under .ticket-logs/ and print only the summary and failures;
   - capture screenshots at most 1280 px wide, look at each one once, and do not reread images you already judged.
Never modify issue #1 or other tickets, never force-push, and never rewrite history.
End your final message with exactly one line: TICKET_DONE or TICKET_BLOCKED: <reason>.
"@
}

function Get-TicketModel($t) {
  if ($Model) { return $Model }
  if ($t.Difficulty -eq 'easy' -and -not $t.IsStageEnd) { return $EasyModel }
  return $HardModel
}

function Invoke-Claude {
  # The ForEach-Object block below runs in this scope, so its assignments do reach $final, $sessionId and $resetsAt.
  [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '')]
  param($t, $log, $prompt, $resumeId)
  $cliArgs = @('-p', '--dangerously-skip-permissions', '--output-format', 'stream-json', '--verbose')
  # Plugins and claude.ai connectors the ticket runs never use stay off; only this process is affected.
  $cliArgs += @('--settings', (Join-Path $PSScriptRoot 'ticket-settings.json'))
  $cliArgs += @('--model', (Get-TicketModel $t))
  $cliArgs += @('--effort', $(if ($t.IsStageEnd) { $StageEffort } else { $Effort }))
  if ($resumeId) { $cliArgs += @('--resume', $resumeId) }
  $final = ''; $sessionId = $resumeId; $resetsAt = $null
  $firstLimits = $null; $lastLimits = $null
  # The prompt goes in as an argument: piped stdin does not expand the /implement slash command.
  # PS 5.1 does not escape embedded double quotes in native arguments, so swap them for single quotes.
  & claude @cliArgs $prompt.Replace('"', "'") | ForEach-Object {
    $line = "$_"
    Add-Content -Path $log -Value $line -Encoding UTF8
    try { $ev = $line | ConvertFrom-Json } catch { return }
    if ($ev.session_id) { $sessionId = $ev.session_id }
    if ($ev.type -eq 'rate_limit_event' -and $ev.rate_limit_info.status -eq 'rejected') { $resetsAt = $ev.rate_limit_info.resetsAt }
    if ($ev.type -eq 'assistant') {
      foreach ($c in $ev.message.content) {
        if ($c.type -eq 'text' -and $c.text.Trim()) { Write-Host ("  > " + ($c.text.Trim() -split "`n")[0]) -ForegroundColor Gray }
        elseif ($c.type -eq 'tool_use') { Write-Host "  - $($c.name)" -ForegroundColor DarkGray }
      }
    } elseif ($ev.type -eq 'rate_limit_event' -and $ev.rate_limit_info.unifiedWindows) {
      $lastLimits = $ev.rate_limit_info.unifiedWindows
      if (-not $firstLimits) { $firstLimits = $lastLimits }
    } elseif ($ev.type -eq 'result') {
      $final = $ev.result
      # On a subscription, total_cost_usd is only an estimate at API list prices; nothing is billed.
      Write-Host ("  turns: {0}, {1:N0} min, API-price equivalent: `${2:N2}" -f $ev.num_turns, ($ev.duration_ms / 60000), $ev.total_cost_usd) -ForegroundColor DarkCyan
      if ($lastLimits) {
        Write-Host ("  subscription limits: 5-hour {0:P0} -> {1:P0}, weekly {2:P0} -> {3:P0}" -f `
          $firstLimits.five_hour.utilization, $lastLimits.five_hour.utilization,
          $firstLimits.seven_day.utilization, $lastLimits.seven_day.utilization) -ForegroundColor DarkCyan
      }
      # modelUsage covers every model in the run (subagents included); usage covers only the main model.
      $models = @($ev.modelUsage.PSObject.Properties | ForEach-Object { $_.Value })
      $sum = { param($name) ($models | Measure-Object -Property $name -Sum).Sum }
      $total = (& $sum inputTokens) + (& $sum cacheCreationInputTokens) + (& $sum cacheReadInputTokens) + (& $sum outputTokens)
      Write-Host ("  tokens: {0:N1}M total, {1:N0}k output" -f ($total / 1e6), ((& $sum outputTokens) / 1e3)) -ForegroundColor DarkCyan
    }
  }
  if ($LASTEXITCODE -ne 0 -and -not $resetsAt) { throw "Claude exited with code $LASTEXITCODE. Log: $log" }
  [pscustomobject]@{ Final = $final; SessionId = $sessionId; ResetsAt = $resetsAt }
}

# Runs the ticket; when the subscription limit stops it, waits for the reset and resumes the same session.
function Invoke-Agent($t, $log) {
  $run = Invoke-Claude $t $log (Get-Prompt $t) $null
  for ($i = 1; $run.ResetsAt -and $i -le $MaxLimitResumes; $i++) {
    $resume = [DateTimeOffset]::FromUnixTimeSeconds([long]$run.ResetsAt).LocalDateTime.AddMinutes(2)
    $wait = $resume - (Get-Date)
    if ($wait.TotalHours -gt $MaxLimitWaitHours) {
      Write-Host "  Usage limit resets at $resume, more than $MaxLimitWaitHours h away; not waiting." -ForegroundColor Yellow
      break
    }
    Write-Host "  Usage limit hit. Waiting until $resume to resume session $($run.SessionId) ($i/$MaxLimitResumes)." -ForegroundColor Yellow
    if ($wait.TotalSeconds -gt 0) { Start-Sleep -Seconds ([int]$wait.TotalSeconds) }
    $run = Invoke-Claude $t $log 'The usage limit interrupted you. Continue this ticket from where you stopped, following the same rules.' $run.SessionId
  }
  return $run.Final
}

$done = 0
while ($true) {
  if ($Max -gt 0 -and $done -ge $Max) { Write-Host "Reached -Max $Max." -ForegroundColor Cyan; break }

  $t = Get-NextTicket
  if ($null -eq $t) { Write-Host 'All tickets are closed. Done.' -ForegroundColor Green; break }
  if ($t -eq 'ALL_BLOCKED') { Write-Host 'Open tickets remain, but all are blocked. Stopping.' -ForegroundColor Yellow; exit 1 }

  $log = Join-Path $logDir ('ticket-{0}-{1:yyyyMMdd-HHmmss}.jsonl' -f $t.Label, (Get-Date))
  $runEffort = if ($t.IsStageEnd) { "$StageEffort, stage end + review" } else { $Effort }
  $difficulty = if ($t.Difficulty) { $t.Difficulty } else { 'unlabelled' }
  Write-Host "`n=== $($t.Title)  (#$($t.Issue), $difficulty, model: $(Get-TicketModel $t), effort: $runEffort) ===" -ForegroundColor Cyan
  $final = Invoke-Agent $t $log

  $state = gh issue view $t.Issue -R $Repo --json state --jq '.state'
  $dirty = git status --porcelain -- . ':!.claude'
  git fetch -q origin
  $unpushed = git rev-list --count origin/main..main

  if ($state -ne 'CLOSED' -or $dirty -or [int]$unpushed -gt 0) {
    Write-Host "Ticket $($t.Label) did not finish cleanly (issue: $state, uncommitted changes: $([bool]$dirty), unpushed commits: $unpushed)." -ForegroundColor Red
    Write-Host "Agent's last message:`n$final" -ForegroundColor Red
    Write-Host "Log: $log"
    exit 1
  }

  Write-Host "Ticket $($t.Label) done." -ForegroundColor Green
  $done++

  # Closing the milestone marks the stage as reviewed, so tickets added to it later skip the stage review.
  if ($t.IsStageEnd) { gh api -X PATCH "repos/$Repo/milestones/$($t.Stage.number)" -f state=closed | Out-Null }

  if (-not $NoStagePause -and $t.IsStageEnd) {
    Write-Host "`nStage '$($t.Stage.title)' is complete. Check it visually (see the closing comments on the issues)." -ForegroundColor Yellow
    $answer = Read-Host 'Continue with the next stage? (Y/n)'
    if ($answer -match '^[nN]') { break }
  }
}
