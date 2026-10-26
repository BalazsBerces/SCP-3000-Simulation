<#
.SYNOPSIS
  Ticket runner v3: v1 with more than one model. Each ticket runs in one fresh headless session, either Claude
  Code (Sonnet) or Gemini (agy), chosen by the ticket's difficulty label.

.DESCRIPTION
  Loop:
    1. Pick the lowest-numbered open ticket whose "blocked by" issues are all closed.
    2. Choose the agent:
         easy, not a stage end      -> Gemini ($GeminiModel) with the Claude skills synced to ~/.gemini/config/skills
         hard, unlabelled           -> Claude ($HardModel, -Effort)
         stage end (any difficulty) -> Claude ($HardModel, -StageEffort); it also reviews the whole stage
       When Gemini is out of quota, its tickets go to Claude ($EasyModel) for the rest of the run.
    3a. Claude: like v1, the agent implements, tests, commits to main, pushes and closes the issue.
    3b. Gemini: the agent implements and tests but never commits (its global settings deny git). Then this
        script checks the work (dotnet build, the whole core test suite in Release, the headless Godot build);
        on red it sends the failures back to the same conversation, up to -FixRounds times. On green it commits,
        pushes, posts the agent's closing comment and closes the issue.
    4. Check that the issue is closed and the working tree is clean and pushed. If not, stop.
    5. At the end of each build stage, close its milestone and pause for a manual visual review (unless -NoStagePause).

  Logs: .ticket-logs/ (git-excluded locally). One CSV row per ticket: .ticket-logs/v3-runs.csv.

.EXAMPLE
  .\scripts\run-tickets-v3.ps1 -DryRun          # show the next ticket and its agent, run nothing
  .\scripts\run-tickets-v3.ps1 -Max 1           # run exactly one ticket
  .\scripts\run-tickets-v3.ps1 -NoStagePause    # never pause, stop only on failure
  .\scripts\run-tickets-v3.ps1 -EasyAgent claude  # easy tickets on Claude ($EasyModel) instead of Gemini
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
  # Hard, unlabelled and stage-end tickets.
  [string]$HardModel = 'sonnet',
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$Effort = 'medium',
  # Stage-end tickets, which also review the whole stage.
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$StageEffort = 'medium',
  # Easy tickets: 'gemini', or 'claude' to run them on $EasyModel.
  [ValidateSet('gemini', 'claude')] [string]$EasyAgent = 'gemini',
  [string]$EasyModel = 'sonnet',
  [string]$GeminiModel = 'gemini-3.8-flash-high',
  # Gate fix rounds for a Gemini ticket before the run stops.
  [int]$FixRounds = 1,
  [double]$GeminiMinutes = 90,
  [double]$TestMinutes = 25,
  [double]$GodotMinutes = 20,
  # An agent's Godot run is killed after this many seconds (0 = never): the prompt's 5 minutes plus 15 s for the agent to stop it itself.
  [int]$GodotRunSeconds = 315,
  [int]$ProviderRetrySeconds = 120,
  # How long after a usage-limit reset a Claude session is resumed.
  [int]$LimitResumeDelaySeconds = 120,
  [int]$MaxLimitResumes = 3,
  [double]$MaxLimitWaitHours = 8,
  # Context size (tokens) at which a Claude session compacts; it compacts about 35-40k below it.
  [string]$CompactWindow = '155000',
  # The skills synced to Gemini before its first ticket of the run (the /implement chain).
  [string[]]$GeminiSkills = @('implement', 'tdd', 'spec-review', 'codebase-design', 'domain-modeling'),
  # Overrides for the tool and skill locations (the stub tests point these at fakes).
  [string]$AgyPath = (Join-Path $HOME '.gemini\bin\agy.exe'),
  [string]$GodotConsole = '',
  [string]$ClaudeSkillsDir = (Join-Path $HOME '.claude\skills'),
  [string]$GeminiSkillsDir = (Join-Path $HOME '.gemini\config\skills')
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8
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
$csvFile = Join-Path $logDir 'v3-runs.csv'
$utf8 = New-Object Text.UTF8Encoding $false

if (-not $GodotConsole) {
  $GodotConsole = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\" -Recurse -Filter '*_console.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
}

$QuotaPattern = '(?i)quota|resource.?exhausted|usage.?limit|insufficient|billing|credits?\b|limit (has been )?reached|plan limit'
$geminiOut = $false
$skillsSynced = $false

# ---------------------------------------------------------------------------------------------------
# Helpers

function Get-LastLine([string]$Text) {
  if (-not $Text) { return '' }
  $lines = @($Text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
  if ($lines.Count) { $lines[-1] } else { '' }
}

function Get-Tail([string]$Path, [int]$Count) {
  if (-not (Test-Path $Path)) { return '' }
  (Get-Content -Path $Path -Encoding UTF8 -Tail $Count) -join "`n"
}

function Write-Utf8([string]$Path, [string]$Text) {
  New-Item -ItemType Directory -Force (Split-Path -Parent $Path) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, $utf8)
}

# Windows command-line quoting for Start-Process, which joins arguments without quoting them.
function ConvertTo-ArgString([string[]]$Arguments) {
  (@($Arguments) | ForEach-Object {
    if ($_ -ne '' -and $_ -notmatch '[\s"]') { $_ }
    else { '"' + (($_ -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"' }
  }) -join ' '
}

function Stop-ProcessTree([int]$ProcessId) {
  try {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue |
      ForEach-Object { Stop-ProcessTree ([int]$_.ProcessId) }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
  } catch { }
}

# Runs a process with stdout to a file, feeds each complete stdout line to $OnLine while it runs,
# and kills the whole process tree after $TimeoutMinutes (0 = no limit).
function Invoke-Logged {
  param([string]$File, [string[]]$Arguments, [string]$OutFile, [double]$TimeoutMinutes = 0, [scriptblock]$OnLine)
  $sp = @{ FilePath = $File; WorkingDirectory = $root; RedirectStandardOutput = $OutFile; RedirectStandardError = "$OutFile.err"
    RedirectStandardInput = $emptyInput; NoNewWindow = $true; PassThru = $true }
  $argString = ConvertTo-ArgString $Arguments
  if ($argString) { $sp.ArgumentList = $argString }
  $started = Get-Date
  $p = Start-Process @sp
  $null = $p.Handle  # keeps the exit code readable after the process ends
  $state = @{ Pos = 0L }
  $drain = {
    param([bool]$Final)
    if (-not (Test-Path $OutFile)) { return }
    $fs = [IO.File]::Open($OutFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
      $len = [int]($fs.Length - $state.Pos)
      if ($len -le 0) { return }
      $null = $fs.Seek($state.Pos, [IO.SeekOrigin]::Begin)
      $buf = New-Object byte[] $len
      $got = $fs.Read($buf, 0, $len)
      if ($got -le 0) { return }
      # Decode only complete lines, so a multi-byte character is never split.
      $end = if ($Final) { $got - 1 } else { [Array]::LastIndexOf($buf, [byte]10, $got - 1) }
      if ($end -lt 0) { return }
      $text = [Text.Encoding]::UTF8.GetString($buf, 0, $end + 1)
      $state.Pos += $end + 1
      if ($OnLine) { foreach ($line in ($text -split "`r?`n")) { if ($line) { & $OnLine $line } } }
    } finally { $fs.Dispose() }
  }
  $timedOut = $false
  while (-not $p.HasExited) {
    $null = $p.WaitForExit(2000)
    & $drain $false
    if ($TimeoutMinutes -gt 0 -and ((Get-Date) - $started).TotalMinutes -gt $TimeoutMinutes) {
      $timedOut = $true
      Write-Host "  Time limit of $TimeoutMinutes min reached; killing the process tree." -ForegroundColor Yellow
      Stop-ProcessTree $p.Id
      $null = $p.WaitForExit(30000)
      break
    }
  }
  if (-not $timedOut) { $p.WaitForExit() }
  & $drain $true
  [pscustomobject]@{ ExitCode = $(if ($timedOut) { -1 } else { $p.ExitCode }); TimedOut = $timedOut
    Minutes = ((Get-Date) - $started).TotalMinutes; ErrFile = "$OutFile.err" }
}

function Resolve-Tool([string]$Name) {
  $c = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $c) { throw "$Name not found on PATH." }
  $c.Source
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

# Agent 'gemini' or 'claude', with the model and effort.
function Get-Choice($t) {
  $isEasy = $t.Difficulty -eq 'easy' -and -not $t.IsStageEnd
  if ($isEasy -and $EasyAgent -eq 'gemini' -and -not $script:geminiOut) {
    return [pscustomobject]@{ Agent = 'gemini'; Model = $GeminiModel; Effort = '' }
  }
  $model = if ($isEasy) { $EasyModel } else { $HardModel }
  $eff = if ($t.IsStageEnd) { $StageEffort } else { $Effort }
  [pscustomobject]@{ Agent = 'claude'; Model = $model; Effort = $eff }
}

function Get-ClaudeName([string]$Model) {
  if ($Model -match 'opus') { 'Opus' } elseif ($Model -match 'haiku') { 'Haiku' } else { 'Sonnet' }
}

# ---------------------------------------------------------------------------------------------------
# Prompts

function Get-ReviewRule($t) {
  if (-not $t.IsStageEnd) {
    return "6. Skip the /spec-review step for this ticket; the code is reviewed once at the end of each build stage."
  }
  # The stage starts at the lowest-numbered ticket of its milestone, closed ones included.
  $first = gh issue list -R $Repo --state all --milestone $t.Stage.title --limit 200 --json title | ConvertFrom-Json |
    ForEach-Object { $_ } | Where-Object { $_.title -match '^(\d\d[a-z]?): ' } |
    ForEach-Object { $null = $_.title -match '^(\d\d[a-z]?): '; $Matches[1] } | Sort-Object | Select-Object -First 1
  "6. This ticket completes the build stage '$($t.Stage.title)' (tickets $first-$($t.Label)). Instead of reviewing only this ticket, run /spec-review on the whole stage: the diff from the parent of ticket $first's commit (find it with git log --grep '^${first}: ') up to and including your uncommitted changes. Then run /code-review medium on the same diff for correctness bugs. Fix the real findings of both, rerun the tests, and include the fixes in this ticket's commit."
}

function Get-Prompt($t, $choice, [string]$Head) {
  $closing = ".ticket-logs/$($t.Label)-closing.md"
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
  if ($choice.Agent -eq 'claude') {
    $finish = @"
7. On success:
   - $stageRule;
   - commit to main with the message "$($t.Title)", a blank line, "Closes #$($t.Issue)", a blank line, and "Co-Authored-By: Claude $(Get-ClaudeName $choice.Model) 5.5 <noreply@anthropic.com>";
   - push to origin main;
   - make sure issue #$($t.Issue) is closed;
   - post a short closing comment: what was built, the test results, and what needs human visual review.
8. If you cannot finish (an unresolvable ambiguity, or a failure you cannot fix), do NOT commit broken code and do NOT close the issue. Comment on issue #$($t.Issue) explaining the blocker and what is done so far.
"@
  } else {
    $finish = @"
7. Never run git commit, git push, git reset, git checkout, git stash or git clean, and never change the issue with gh.
   The runner does that after you: it builds the solution, runs the whole core test suite in Release, builds the Godot
   project headless, and only then commits, pushes and closes the issue. The review fixed point is commit $Head.
   On success, write the closing comment to ${closing}: what was built, the test results, and what needs human
   visual review.
8. If you cannot finish (an unresolvable ambiguity, or a failure you cannot fix), end with TICKET_BLOCKED: <reason>,
   naming the blocker and what is done so far; the runner posts it on the issue.
"@
  }
  # The gate runs the whole suite after Gemini; Claude commits itself, so it runs it once before that.
  $suiteRule = if ($choice.Agent -eq 'gemini') {
    'Run only the test classes this ticket adds or touches; never the whole suite, which the runner runs after you.'
  } else { 'Run the whole test suite once, just before you commit.' }
  $rulesPath = Join-Path $root $ProjectRules
  $projectRules = if (Test-Path $rulesPath) {
    "Rules of this project (from $ProjectRules; they add to the rules above):`n" +
    ((Get-Content $rulesPath -Raw -Encoding UTF8).Replace('{GodotRunSeconds}', "$GodotRunSeconds").Trim()) + "`n"
  } else { '' }
@"
/implement ticket $($t.Label) of the ${Project}: GitHub issue #$($t.Issue) in $Repo.
The spec is issue #$SpecIssue. Read the ticket (gh issue view $($t.Issue) --comments), $GlossaryFile, every ADR in $AdrDir/, and the existing code.

Rules for this run:
1. This is an unattended run. Nobody will answer questions, so make reasonable decisions yourself and record them.
2. Implement ONLY this ticket so that every acceptance criterion is met. Follow the spec's architecture.
3. Test through the project's public seam. $suiteRule Also run the project's build. Everything must pass.
4. For criteria that cannot be checked automatically (visuals, audio, fps), verify what you can (for example, run the app or the benchmark and capture a screenshot or fps numbers), and list what still needs human review. A check that fails is a failure to fix, never "could not run".
5. Use the glossary vocabulary. If a new domain term is settled, add it to $GlossaryFile. Write an ADR in $AdrDir/ only for decisions that are hard to reverse, surprising, and the result of a real trade-off. Keep files UTF-8 and leave existing non-ASCII characters intact.
$(Get-ReviewRule $t)
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
# Gemini

# Copies the /implement chain from the Claude skills, so both agents follow the same procedure.
function Sync-GeminiSkills {
  if ($script:skillsSynced) { return }
  New-Item -ItemType Directory -Force $GeminiSkillsDir | Out-Null
  foreach ($name in $GeminiSkills) {
    $src = Join-Path $ClaudeSkillsDir $name
    if (-not (Test-Path (Join-Path $src 'SKILL.md'))) { Write-Warning "Skill '$name' not found in $ClaudeSkillsDir; Gemini runs without it."; continue }
    $dst = Join-Path $GeminiSkillsDir $name
    if (Test-Path $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
    Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
  }
  Write-Host "  Synced skills to Gemini: $($GeminiSkills -join ', ')" -ForegroundColor DarkCyan
  $script:skillsSynced = $true
}

# agy streams no text while it works, only tool calls; show what each one touches: the file or the command.
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

# One agy process. Status: done | blocked | timeout | exhausted | provider | nomarker.
function Invoke-Gemini($t, $stats, [string]$Message) {
  $stats.GeminiRuns++
  $log = Join-Path $logDir ('ticket-{0}-{1:yyyyMMdd-HHmmss}-gemini-{2}.jsonl' -f $t.Label, (Get-Date), $stats.GeminiRuns)
  $a = @('-p', $Message, '--dangerously-skip-permissions', '--model', $GeminiModel, '--output-format', 'stream-json')
  if ($stats.GeminiSession) { $a += @('--conversation', $stats.GeminiSession) }
  $s = @{ Final = ''; In = 0.0; Out = 0.0; Cache = 0.0; Errors = @() }
  $onLine = {
    param($line)
    try { $ev = $line | ConvertFrom-Json } catch { return }
    if ($ev.conversation_id) { $stats.GeminiSession = $ev.conversation_id }
    if ($ev.event -eq 'step_update') {
      $u = $ev.step_update
      if ($u.conversation_id) { $stats.GeminiSession = $u.conversation_id }
      if ($u.step_type -eq 'tool' -and $u.state -eq 'ACTIVE') { Write-Host "  - $($u.tool_name)$(Get-ToolDetail $u.tool_info.parameters)" -ForegroundColor DarkGray }
    } elseif ($ev.event -eq 'result') {
      $r = $ev.result
      if ($r.conversation_id) { $stats.GeminiSession = $r.conversation_id }
      $s.Final = $r.response
      if ($r.response) { Write-Host ('  > ' + (Get-LastLine $r.response)) -ForegroundColor Gray }
      $s.In += [double]$r.usage.input_tokens; $s.Out += [double]$r.usage.output_tokens + [double]$r.usage.thinking_tokens
      $s.Cache += [double]$r.usage.cache_read_tokens
      if ($r.status -and $r.status -ne 'SUCCESS') { $s.Errors += "agy status $($r.status): $($line.Substring(0, [Math]::Min(400, $line.Length)))" }
    } elseif ($ev.event -eq 'error' -or $ev.error) {
      $s.Errors += "agy error: $($line.Substring(0, [Math]::Min(400, $line.Length)))"
      Write-Host "  Gemini error: $($line.Substring(0, [Math]::Min(200, $line.Length)))" -ForegroundColor Red
    }
  }
  $run = Invoke-Logged -File $AgyPath -Arguments $a -OutFile $log -TimeoutMinutes $GeminiMinutes -OnLine $onLine
  $stats.GeminiIn += $s.In; $stats.GeminiOut += $s.Out; $stats.GeminiCache += $s.Cache
  Write-Host ('  [Gemini] exit {0}, {1:N0} min, tokens: {2:N1}M in, {3:N0}k out, {4:N1}M cache read' -f $run.ExitCode, $run.Minutes,
    ($s.In / 1e6), ($s.Out / 1e3), ($s.Cache / 1e6)) -ForegroundColor DarkCyan
  $marker = Get-LastLine $s.Final
  $errText = (@($s.Errors) + @(Get-Tail $run.ErrFile 30)) -join "`n"
  $status =
    if ($run.TimedOut) { 'timeout' }
    elseif ($marker -match '^TICKET_BLOCKED') { 'blocked' }
    elseif ($marker -eq 'TICKET_DONE') { 'done' }      # an exit code 1 after TICKET_DONE still delivered the work
    elseif ($errText -match $QuotaPattern) { 'exhausted' }
    elseif ($s.Errors.Count -or $run.ExitCode -ne 0) { 'provider' }
    else { 'nomarker' }
  # Not green: the agent's TICKET_DONE is only a claim until the gate has passed and the runner has committed.
  $note = if ($status -eq 'done') { ' - agent claims it is done; the runner has not checked it yet' } else { '' }
  Write-Host "  [Gemini] status: $status$(if ($marker) { " ($marker)" })$note" -ForegroundColor $(if ($status -eq 'done') { 'Cyan' } else { 'Yellow' })
  [pscustomobject]@{ Status = $status; Marker = $marker; ErrorText = $errText; Final = $s.Final; Log = $log }
}

# A Gemini run with the one retry after a provider error.
function Invoke-GeminiRun($t, $stats, [string]$Message) {
  $r = Invoke-Gemini $t $stats $Message
  if ($r.Status -eq 'provider') {
    Write-Host "  Provider error; one retry in $ProviderRetrySeconds s." -ForegroundColor Yellow
    Start-Sleep -Seconds $ProviderRetrySeconds
    $retry = if ($stats.GeminiSession) { 'A provider error interrupted you. Continue this ticket from where you stopped, following the same rules.' } else { $Message }
    $r = Invoke-Gemini $t $stats $retry
  }
  $r
}

# The checks the runner makes before it commits Gemini's work. Returns $null when green, else the failures.
function Invoke-Gate($t, $stats) {
  $stats.GateRuns++
  $n = $stats.GateRuns
  $prefix = Join-Path $logDir "$($t.Label)-gate-$n"
  $dotnet = Resolve-Tool 'dotnet'
  Write-Host "  [gate $n] dotnet build, core tests (Release), Godot build" -ForegroundColor DarkCyan

  $b = Invoke-Logged -File $dotnet -Arguments @('build', 'Scp3008.sln', '-nologo', '-v:minimal') -OutFile "$prefix-build.log" -TimeoutMinutes 20
  if ($b.ExitCode -ne 0) {
    $errors = @(Get-Content "$prefix-build.log" -Encoding UTF8 | Where-Object { $_ -match ': error ' } | ForEach-Object { $_.Trim() } | Select-Object -Unique -First 40)
    if (-not $errors.Count) { $errors = @((Get-Tail "$prefix-build.log" 30) -split "`n") }
    return "dotnet build failed:`n" + ($errors -join "`n")
  }

  $trxDir = "$prefix-results"
  $tr = Invoke-Logged -File $dotnet -Arguments @('test', 'tests/Scp3008.Core.Tests', '-c', 'Release', '-nologo', '--logger', "trx;LogFileName=gate-$n.trx", '--results-directory', $trxDir) `
    -OutFile "$prefix-test.log" -TimeoutMinutes $TestMinutes
  if ($tr.TimedOut) { return "The core test suite did not finish within $TestMinutes minutes (a hang counts as a failure).`n$(Get-Tail "$prefix-test.log" 20)" }
  if ($tr.ExitCode -ne 0) {
    $failures = @()
    $trx = Get-ChildItem $trxDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($trx) {
      [xml]$x = Get-Content $trx.FullName -Raw -Encoding UTF8
      foreach ($r in @($x.TestRun.Results.UnitTestResult | Where-Object { $_ -and $_.outcome -eq 'Failed' } | Select-Object -First 30)) {
        $msg = "$($r.Output.ErrorInfo.Message)".Trim() -split "`r?`n" | Select-Object -First 3
        $failures += "FAILED $($r.testName)`n    " + ($msg -join "`n    ")
      }
    }
    if (-not $failures.Count) { return "dotnet test failed (exit $($tr.ExitCode)):`n$(Get-Tail "$prefix-test.log" 30)" }
    return "$($failures.Count) test(s) failed:`n" + ($failures -join "`n")
  }

  if (-not $GodotConsole) { return 'Godot console executable not found under %LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\.' }
  $g = Invoke-Logged -File $GodotConsole -Arguments @('--headless', '--path', 'game', '--build-solutions', '--quit') -OutFile "$prefix-godot.log" -TimeoutMinutes $GodotMinutes
  if ($g.ExitCode -ne 0) {
    $errors = @(@(Get-Content "$prefix-godot.log", "$prefix-godot.log.err" -Encoding UTF8 -ErrorAction SilentlyContinue) | Where-Object { $_ -match '(?i)error' } | Select-Object -Unique -First 40)
    return "The headless Godot build failed (exit $($g.ExitCode)):`n" + ($errors -join "`n")
  }
  Write-Host "  [gate $n] green" -ForegroundColor Green
  $null
}

function Add-IssueComment($t, [string]$Body) {
  $f = Join-Path $logDir "$($t.Label)-comment.md"
  Write-Utf8 $f $Body
  gh issue comment $t.Issue -R $Repo --body-file $f | Out-Null
}

# Commits Gemini's work (after a green gate), pushes, comments and closes the issue.
function Complete-GeminiTicket($t, [string]$Head) {
  # Commits made meanwhile stay as they are: someone may work in the same tree while the ticket runs.
  if ((git rev-parse HEAD).Trim() -ne $Head) { Write-Host '  HEAD moved during the ticket; the ticket commit goes on top.' -ForegroundColor Yellow }
  $msgFile = Join-Path $logDir "$($t.Label)-commit-msg.txt"
  Write-Utf8 $msgFile "$($t.Title)`n`nCloses #$($t.Issue)`n`nCo-Authored-By: Gemini ($GeminiModel) <noreply@google.com>`n"
  # Only the ticket's own paths: uncommitted work that was there before it stays uncommitted.
  $paths = @(Get-TicketPaths $script:baseline)
  if ($paths.Count) {
    $specFile = Join-Path $logDir "$($t.Label)-paths.txt"
    Write-Utf8 $specFile (($paths -join "`n") + "`n")
    git -c core.safecrlf=false --literal-pathspecs add -A --pathspec-from-file=$specFile
    if ($LASTEXITCODE -ne 0) { throw 'git add failed.' }
    git -c core.safecrlf=false --literal-pathspecs commit -q -F $msgFile --pathspec-from-file=$specFile
    if ($LASTEXITCODE -ne 0) { throw 'git commit failed.' }
  } else { Write-Host '  The ticket left no uncommitted change of its own; nothing to commit.' -ForegroundColor Yellow }
  git push -q origin main
  if ($LASTEXITCODE -ne 0) { throw 'git push failed.' }
  $closing = Join-Path $logDir "$($t.Label)-closing.md"
  $body = if (Test-Path $closing) { Get-Content $closing -Raw -Encoding UTF8 } else { "Done by Gemini ($GeminiModel). The runner's checks passed: dotnet build, the core test suite, the headless Godot build." }
  Add-IssueComment $t $body
  if ((gh issue view $t.Issue -R $Repo --json state --jq '.state') -ne 'CLOSED') { gh issue close $t.Issue -R $Repo | Out-Null }
}

# Returns the agent's last message; the caller checks the result like for a Claude ticket.
function Invoke-GeminiTicket($t, $stats, [string]$Prompt, [string]$Head) {
  Sync-GeminiSkills
  $r = Invoke-GeminiRun $t $stats $Prompt
  if ($r.Status -eq 'exhausted') { $stats.GeminiQuota = $true; return $r.Final }
  if ($r.Status -ne 'done') {
    Add-IssueComment $t "Gemini ($GeminiModel) did not finish this ticket ($($r.Status)).`n`n$(if ($r.Marker) { $r.Marker } else { $r.ErrorText })"
    return $r.Final
  }
  for ($round = 0; ; $round++) {
    $failure = Invoke-Gate $t $stats
    if (-not $failure) { Complete-GeminiTicket $t $Head; return $r.Final }
    if ($round -ge $FixRounds) {
      Add-IssueComment $t "Gemini ($GeminiModel) finished, but the runner's checks are still red after $FixRounds fix round(s). Nothing is committed.`n`n$failure"
      return $r.Final
    }
    $stats.FixRounds++
    Write-Host "  Gate red; fix round $($round + 1)/$FixRounds." -ForegroundColor Yellow
    # A one-line message pointing at a file: multi-line arguments do not survive every launcher.
    $fixFile = ".ticket-logs/$($t.Label)-fix-$($round + 1).md"
    Write-Utf8 (Join-Path $root $fixFile) @"
# The runner's checks failed after your TICKET_DONE

Fix the cause, rerun the affected tests in Release and the Godot build, update .ticket-logs/$($t.Label)-closing.md,
and end with TICKET_DONE or TICKET_BLOCKED: <reason>. The rules of your first prompt still apply.

## Failures

$failure
"@
    $r = Invoke-GeminiRun $t $stats "The runner's checks failed. Read $fixFile and follow it."
    if ($r.Status -eq 'exhausted') { $stats.GeminiQuota = $true; return $r.Final }
    if ($r.Status -ne 'done') {
      Add-IssueComment $t "Gemini ($GeminiModel) did not finish the fix round ($($r.Status)). Nothing is committed.`n`n$(if ($r.Marker) { $r.Marker } else { $r.ErrorText })"
      return $r.Final
    }
  }
}

# ---------------------------------------------------------------------------------------------------
# Bookkeeping

function Add-CsvRow($t, $choice, $stats, [double]$Minutes, [string]$Result) {
  $lb = $stats.LimitsBefore; $la = $stats.LimitsAfter
  [pscustomobject][ordered]@{
    date = (Get-Date).ToString('yyyy-MM-dd HH:mm'); ticket = $t.Label; issue = $t.Issue
    difficulty = $(if ($t.Difficulty) { $t.Difficulty } else { 'unlabelled' }); stage_end = $t.IsStageEnd
    agent = $choice.Agent; model = $choice.Model; effort = $choice.Effort; minutes = [Math]::Round($Minutes, 1)
    fix_rounds = $stats.FixRounds; claude_usd = [Math]::Round($stats.ClaudeCost, 2); claude_tokens = [long]$stats.ClaudeTokens
    gemini_in = [long]$stats.GeminiIn; gemini_out = [long]$stats.GeminiOut; gemini_cache = [long]$stats.GeminiCache
    claude_5h_before = $(if ($lb) { $lb.five_hour.utilization }); claude_5h_after = $(if ($la) { $la.five_hour.utilization })
    claude_week_before = $(if ($lb) { $lb.seven_day.utilization }); claude_week_after = $(if ($la) { $la.seven_day.utilization })
    result = $Result
  } | Export-Csv -Path $csvFile -Append -NoTypeInformation -Encoding UTF8
}

function New-Stats {
  @{ ClaudeTokens = 0.0; ClaudeCost = 0.0; LimitsBefore = $null; LimitsAfter = $null
    GeminiSession = $null; GeminiRuns = 0; GeminiIn = 0.0; GeminiOut = 0.0; GeminiCache = 0.0; GeminiQuota = $false
    GateRuns = 0; FixRounds = 0 }
}

# ---------------------------------------------------------------------------------------------------
# Godot watchdog

# A Godot started by an agent (any depth below the runner) is killed once it outlives $GodotRunSeconds,
# whether or not the agent kept to its own limit. The gate's Godot build is the runner's direct child
# and has its own limit, so it is left alone; the user's own editor is not below the runner at all.
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
  $what = if ($choice.Agent -eq 'gemini') { "Gemini $($choice.Model)" } else { "Claude $($choice.Model), effort $($choice.Effort)" }
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
  if ($choice.Agent -eq 'gemini') {
    $final = Invoke-GeminiTicket $t $stats (Get-Prompt $t $choice $head) $head
    if ($stats.GeminiQuota) {
      # Out of quota: this ticket and the rest of the run's easy tickets go to Claude.
      $script:geminiOut = $true
      $choice = Get-Choice $t
      Write-Host "  Gemini is out of quota; the ticket continues on Claude $($choice.Model) (effort $($choice.Effort))." -ForegroundColor Yellow
      $prompt = (Get-Prompt $t $choice $head) + "`nGemini started this ticket and ran out of quota; the working tree may already hold part of its work. Check it and build on it or replace it."
      $final = Invoke-ClaudeTicket $t $choice $stats $log $prompt
    }
  } else {
    $final = Invoke-ClaudeTicket $t $choice $stats $log (Get-Prompt $t $choice $head)
  }

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
