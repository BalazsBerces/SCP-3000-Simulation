<#
.SYNOPSIS
  Codex-only ticket runner: v4 with every ticket on Codex (gpt-6.1-sol, effort medium), whatever its difficulty.
  Each ticket runs in one fresh headless `codex exec` session (the equivalent of /clear between tickets).

.DESCRIPTION
  Loop:
    1. Pick the lowest-numbered open ticket whose "blocked by" issues are all closed.
    2. Run Codex on it with its own skills in ~/.codex/skills. The agent implements, tests, commits to main,
       pushes and closes the issue. A stage-end ticket also reviews the whole stage.
       When Codex runs out of quota, the run stops; the ticket's work stays for its next attempt.
    3. Check that the issue is closed and the working tree is clean and pushed. If not, stop.
    4. At the end of each build stage, close its milestone and pause for a manual visual review (unless -NoStagePause).

  Logs: .ticket-logs/ (git-excluded locally). One CSV row per ticket: .ticket-logs/codex-runs.csv.

.EXAMPLE
  .\scripts\run-tickets-codex.ps1 -DryRun          # show the next ticket, run nothing
  .\scripts\run-tickets-codex.ps1 -Max 1           # run exactly one ticket
  .\scripts\run-tickets-codex.ps1 -NoStagePause    # never pause, stop only on failure
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
  # An agent's Godot run is killed after this many seconds (0 = never): the prompt's 5 minutes plus 15 s for the agent to stop it itself.
  [int]$GodotRunSeconds = 315,
  # Overrides for the tool and skill locations (the stub tests point these at fakes).
  [string]$CodexPath = 'codex',
  # The skills Codex needs; it keeps its own adapted copies there (they name Codex's tools), so they are checked, not synced.
  [string]$CodexSkillsDir = $(if ($env:CODEX_HOME) { Join-Path $env:CODEX_HOME 'skills' } else { Join-Path $HOME '.codex\skills' }),
  [string]$ClaudeSkillsDir = (Join-Path $HOME '.claude\skills')
)

# Every ticket, stage ends included, runs on this model and effort.
$Model = 'gpt-6.1-sol'
$Effort = 'medium'

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
# Without a BOM: the Codex prompt goes in on stdin, and a BOM would land in front of its first word.
$OutputEncoding = New-Object Text.UTF8Encoding $false
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
$csvFile = Join-Path $logDir 'codex-runs.csv'
$utf8 = New-Object Text.UTF8Encoding $false

$QuotaPattern = '(?i)quota|resource.?exhausted|usage.?limit|insufficient|billing|credits?\b|limit (has been )?reached|plan limit'
$codexSkillsChecked = $false

# ---------------------------------------------------------------------------------------------------
# Helpers

function Get-LastLine([string]$Text) {
  if (-not $Text) { return '' }
  $lines = @($Text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
  if ($lines.Count) { $lines[-1] } else { '' }
}

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

# What a Codex tool call touches: the file or the command.
function Get-ToolDetail($Parameters) {
  if (-not $Parameters) { return '' }
  $props = @($Parameters.PSObject.Properties)
  $p = @($props | Where-Object { $_.Name -in 'AbsolutePath', 'TargetFile', 'CommandLine' }) + @($props | Where-Object { $_.Value -is [string] })
  if (-not $p.Count) { return '' }
  $v = ("$($p[0].Value)" -replace '\s+', ' ').Trim()
  # Paths relative to the repository root.
  if ($p[0].Name -ne 'CommandLine') {
    $v = $v.Replace('/', '\')
    $prefix = $root.TrimEnd('\') + '\'
    if ($v.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $v = $v.Substring($prefix.Length) }
  }
  if ($v.Length -gt 110) { $v = $v.Substring(0, 107) + '...' }
  "  $v"
}

# ---------------------------------------------------------------------------------------------------
# Picking the ticket

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

# ---------------------------------------------------------------------------------------------------
# Prompt

function Get-ReviewRule($t) {
  if (-not $t.IsStageEnd) {
    return "6. Skip the /spec-review step for this ticket; the code is reviewed once at the end of each build stage."
  }
  # The stage starts at the lowest-numbered ticket of its milestone, closed ones included.
  $first = gh issue list -R $Repo --state all --milestone $t.Stage.title --limit 200 --json title | ConvertFrom-Json |
    ForEach-Object { $_ } | Where-Object { $_.title -match '^(\d\d[a-z]?): ' } |
    ForEach-Object { $null = $_.title -match '^(\d\d[a-z]?): '; $Matches[1] } | Sort-Object | Select-Object -First 1
  # Codex's code-review skill is a different (two-axis) review; the correctness pass is spelled out instead.
  "6. This ticket completes the build stage '$($t.Stage.title)' (tickets $first-$($t.Label)). Instead of reviewing only this ticket, run /spec-review on the whole stage: the diff from the parent of ticket $first's commit (find it with git log --grep '^${first}: ') up to and including your uncommitted changes. Then review the same diff yourself for correctness bugs only (logic errors, crashes, broken edge cases, regressions; not style), as Claude Code's /code-review medium would. Fix the real findings of both, rerun the tests, and include the fixes in this ticket's commit."
}

function Get-Prompt($t) {
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
  $rulesPath = Join-Path $root $ProjectRules
  $projectRules = if (Test-Path $rulesPath) {
    "Rules of this project (from $ProjectRules; they add to the rules above):`n" +
    ((Get-Content $rulesPath -Raw -Encoding UTF8).Replace('{GodotRunSeconds}', "$GodotRunSeconds").Trim()) + "`n"
  } else { '' }
@"
Use the implement skill to implement ticket $($t.Label) of the ${Project}: GitHub issue #$($t.Issue) in $Repo.
Read the implement skill's SKILL.md in $CodexSkillsDir and follow it. A /name in these instructions or in a skill (/tdd, /spec-review) means: read $CodexSkillsDir\<name>\SKILL.md and follow it.
The spec is issue #$SpecIssue. Read the ticket (gh issue view $($t.Issue) --comments), $GlossaryFile, every ADR in $AdrDir/, and the existing code.

Rules for this run:
1. This is an unattended run. Nobody will answer questions, so make reasonable decisions yourself and record them.
2. Implement ONLY this ticket so that every acceptance criterion is met. Follow the spec's architecture.
3. Test through the project's public seam. Run the whole test suite once, just before you commit. Also run the project's build. Everything must pass.
4. For criteria that cannot be checked automatically (visuals, audio, fps), verify what you can (for example, run the app or the benchmark and capture a screenshot or fps numbers), and list what still needs human review. A check that fails is a failure to fix, never "could not run".
5. Use the glossary vocabulary. If a new domain term is settled, add it to $GlossaryFile. Write an ADR in $AdrDir/ only for decisions that are hard to reverse, surprising, and the result of a real trade-off. Keep files UTF-8 and leave existing non-ASCII characters intact.
$(Get-ReviewRule $t)
7. On success:
   - $stageRule;
   - commit to main with the message "$($t.Title)", a blank line, "Closes #$($t.Issue)", a blank line, and "Co-Authored-By: Codex ($Model) <noreply@openai.com>";
   - push to origin main;
   - make sure issue #$($t.Issue) is closed;
   - post a short closing comment: what was built, the test results, and what needs human visual review.
8. If you cannot finish (an unresolvable ambiguity, or a failure you cannot fix), do NOT commit broken code and do NOT close the issue. Comment on issue #$($t.Issue) explaining the blocker and what is done so far.
9. Keep the context small; everything in it is re-read on every later turn:
   - write the full output of test runs, builds, Godot runs and benchmarks to a file under .ticket-logs/ and print only the summary and failures;
   - capture screenshots at most 1280 px wide, look at each one once, and do not reread images you already judged.
${projectRules}${preRule}If you hit a problem that cost you real time and that another ticket would likely hit too (you worked around a tool, a path, a missing step), write the rule that would have prevented it: at most two sentences, the cause and the fix. Append it to $ProposedRules as a line "- <ticket>: <rule>" and repeat it in your closing comment as a line starting with "Suggested rule:". Do not edit $ProjectRules; a human decides what moves there. Skip this if nothing like that happened.
Never modify issue #$SpecIssue or other tickets, never force-push, and never rewrite history.
End your final message with exactly one line: TICKET_DONE or TICKET_BLOCKED: <reason>.
"@
}

# ---------------------------------------------------------------------------------------------------
# Codex

# Codex keeps its own copies of the skills (adapted to its tools), so they are only checked: missing, or older
# than the Claude version they were adapted from, means the copy needs updating by hand.
function Test-CodexSkills {
  if ($script:codexSkillsChecked) { return }
  $script:codexSkillsChecked = $true
  foreach ($name in 'implement', 'tdd', 'spec-review', 'codebase-design', 'domain-modeling') {
    $codex = Join-Path $CodexSkillsDir "$name\SKILL.md"
    $claude = Join-Path $ClaudeSkillsDir "$name\SKILL.md"
    if (-not (Test-Path $codex)) { Write-Warning "Codex skill '$name' not found in $CodexSkillsDir; Codex runs without it." }
    elseif ((Test-Path $claude) -and (Get-Item $claude).LastWriteTime -gt (Get-Item $codex).LastWriteTime) {
      Write-Warning "Codex skill '$name' is older than the Claude one; check that its adaptation is up to date."
    }
  }
}

# One codex exec session. Returns the last message and whether Codex ran out of quota.
function Invoke-Codex($stats, [string]$Log, [string]$Prompt) {
  Test-CodexSkills
  # Ponytail is off in ~/.codex/config.toml and on only for ticket runs, as ticket-settings.json does for Claude.
  # Its hooks run only once trusted: start codex with the same --config once and trust them in /hooks.
  # Compact at 200k tokens instead of near the full context window, which keeps long tickets' turns cheaper.
  $cliArgs = @('exec', '--dangerously-bypass-approvals-and-sandbox', '--json', '--model', $Model,
    '--config', "model_reasoning_effort=$Effort", '--config', 'plugins."ponytail@ponytail".enabled=true',
    '--config', 'model_auto_compact_token_limit=200000', '-')
  $s = @{ Final = ''; Errors = @(); In = 0.0; Cache = 0.0; Out = 0.0 }
  $started = Get-Date
  # The prompt goes in on stdin, which keeps its quotes and line breaks in PowerShell 5.1.
  $Prompt | & $CodexPath @cliArgs | ForEach-Object {
    $line = "$_"
    Add-Content -Path $Log -Value $line -Encoding UTF8
    try { $ev = $line | ConvertFrom-Json } catch { return }
    if ($ev.type -eq 'item.started' -and $ev.item.type -eq 'command_execution') {
      # Codex wraps each command as "...\powershell.exe" -Command '<command>'; show only the command.
      $cmd = "$($ev.item.command)" -replace '^"?[^"]*powershell\.exe"?\s+-Command\s+''(.*)''$', '$1'
      Write-Host "  - shell$(Get-ToolDetail ([pscustomobject]@{ CommandLine = $cmd }))" -ForegroundColor DarkGray
    } elseif ($ev.type -eq 'item.completed' -and $ev.item.type -eq 'file_change') {
      foreach ($c in @($ev.item.changes)) { Write-Host "  - $($c.kind)$(Get-ToolDetail ([pscustomobject]@{ TargetFile = "$($c.path)" }))" -ForegroundColor DarkGray }
    } elseif ($ev.type -eq 'item.completed' -and $ev.item.type -eq 'agent_message') {
      $s.Final = $ev.item.text
      if ($ev.item.text.Trim()) { Write-Host ('  > ' + ($ev.item.text.Trim() -split "`n")[0]) -ForegroundColor Gray }
    } elseif ($ev.type -eq 'turn.completed') {
      $s.In += [double]$ev.usage.input_tokens - [double]$ev.usage.cached_input_tokens
      $s.Cache += [double]$ev.usage.cached_input_tokens; $s.Out += [double]$ev.usage.output_tokens
    } elseif ($ev.type -eq 'turn.failed' -or $ev.type -eq 'error') {
      $msg = if ($ev.error.message) { $ev.error.message } elseif ($ev.message) { $ev.message } else { $line }
      $s.Errors += "$msg"; Write-Host "  Codex error: $msg" -ForegroundColor Red
    }
  }
  $exit = $LASTEXITCODE
  $stats.CodexIn += $s.In; $stats.CodexCache += $s.Cache; $stats.CodexOut += $s.Out
  Write-Host ('  [Codex] exit {0}, {1:N0} min, tokens: {2:N1}M in, {3:N0}k out, {4:N1}M cache read' -f $exit,
    ((Get-Date) - $started).TotalMinutes, ($s.In / 1e6), ($s.Out / 1e3), ($s.Cache / 1e6)) -ForegroundColor DarkCyan
  $exhausted = (Get-LastLine $s.Final) -notmatch '^TICKET_' -and (($s.Errors -join "`n") -match $QuotaPattern)
  if ($exit -ne 0 -and -not $exhausted) { throw "Codex exited with code $exit. Log: $Log" }
  [pscustomobject]@{ Final = $s.Final; Exhausted = $exhausted }
}

# ---------------------------------------------------------------------------------------------------
# Bookkeeping

function Add-CsvRow($t, $stats, [double]$Minutes, [string]$Result) {
  [pscustomobject][ordered]@{
    date = (Get-Date).ToString('yyyy-MM-dd HH:mm'); ticket = $t.Label; issue = $t.Issue
    difficulty = $(if ($t.Difficulty) { $t.Difficulty } else { 'unlabelled' }); stage_end = $t.IsStageEnd
    model = $Model; effort = $Effort; minutes = [Math]::Round($Minutes, 1)
    codex_in = [long]$stats.CodexIn; codex_out = [long]$stats.CodexOut; codex_cache = [long]$stats.CodexCache
    result = $Result
  } | Export-Csv -Path $csvFile -Append -NoTypeInformation -Encoding UTF8
}

function New-Stats { @{ CodexIn = 0.0; CodexOut = 0.0; CodexCache = 0.0 } }

# ---------------------------------------------------------------------------------------------------
# Godot watchdog

# A Godot started by the agent (any depth below the runner) is killed once it outlives $GodotRunSeconds,
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
        # The topmost Godot of a run only (the console wrapper starts the real one).
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

  $difficulty = if ($t.Difficulty) { $t.Difficulty } else { 'unlabelled' }
  Write-Host "`n=== $($t.Title)  (#$($t.Issue), $difficulty$(if ($t.IsStageEnd) { ', stage end + review' }), Codex $Model, effort $Effort) ===" -ForegroundColor Cyan
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
  $log = Join-Path $logDir ('ticket-{0}-{1:yyyyMMdd-HHmmss}.jsonl' -f $t.Label, (Get-Date))
  $run = Invoke-Codex $stats $log (Get-Prompt $t)
  $final = $run.Final
  # Out of quota: the run stops below, and the ticket's work waits for its next attempt.
  if ($run.Exhausted) { Write-Host '  Codex is out of quota; the run stops.' -ForegroundColor Yellow }

  $state = gh issue view $t.Issue -R $Repo --json state --jq '.state'
  Show-WatchdogNotes
  $dirty = @(Get-TicketPaths $baseline)
  git fetch -q origin
  $unpushed = git rev-list --count origin/main..main
  $minutes = ((Get-Date) - $started).TotalMinutes

  if ($state -ne 'CLOSED' -or $dirty.Count -or [int]$unpushed -gt 0) {
    Add-CsvRow $t $stats $minutes 'unfinished'
    # The half-done work stays uncommitted; the next attempt at this ticket takes it over.
    if ($dirty.Count) { Write-Utf8 $unfinishedFile (($dirty -join "`n") + "`n") }
    Write-Host "Ticket $($t.Label) did not finish cleanly (issue: $state, uncommitted changes: $($dirty.Count -gt 0), unpushed commits: $unpushed)." -ForegroundColor Red
    Write-Host "Agent's last message:`n$final" -ForegroundColor Red
    Write-Host "Logs: $logDir"
    exit 1
  }

  Add-CsvRow $t $stats $minutes 'done'
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
