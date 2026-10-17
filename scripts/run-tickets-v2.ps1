<#
.SYNOPSIS
  Ticket runner v2: Opus plans and reviews, cheaper models implement.

.DESCRIPTION
  Design: docs/superpowers/specs/2026-10-17-run-tickets-v2-design.md. Per ready ticket:
    0. Pick the lowest-numbered open 'NN: ' / 'NNa: ' ticket whose blockers are all closed.
    1. PLAN    (Opus, /plan-ticket): PLAN.md, locked acceptance tests, locked.txt, captures.txt.
    2. EXECUTE (Nemotron and Gemini alternate per ticket; fallback chain primary -> other -> Codex)
               in a separate clone without a remote, ../<repo>-exec.
    3. GATE    (this script): locked tests unchanged, no commits, dotnet build, whole test suite,
               headless Godot build, REPORT.md and captures. Red: up to -FixRounds fix rounds.
       3a. AMEND (Opus, /plan-ticket --amend): once per ticket, when a locked test is suspect or the
               only remaining failures are in locked tests.
    4. REVIEW  (Opus, /review-ticket, resumes the planning session) on the patch applied to the main tree.
    5. COMMIT  by the reviewer; this script checks closed + clean + pushed.

  Logs: .ticket-logs/<label>/, work files: .ticket-work/<label>/ (both git-excluded locally).
  One CSV row per ticket: .ticket-logs/v2-runs.csv. Executor rotation: .ticket-logs/v2-rotation.json.

.EXAMPLE
  .\scripts\run-tickets-v2.ps1 -DryRun            # pick the ticket, show executor choice and prompts, run nothing
  .\scripts\run-tickets-v2.ps1 -Max 1             # run exactly one ticket
  .\scripts\run-tickets-v2.ps1 -NoStagePause      # never pause at stage ends, stop only on failure
  .\scripts\run-tickets-v2.ps1 -StartWith Gemini  # this run's first ticket goes to Gemini
#>
param(
  [string]$Repo = 'BalazsBerces/SCP-3000-Simulation',
  [int]$Max = 0,
  [switch]$NoStagePause,
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$Effort = 'medium',
  # Stage-end tickets also review the whole stage, so planning and review get more effort.
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$StageEffort = 'high',
  # Context size (tokens) at which Opus sessions compact; they compact about 35-40k below it.
  [string]$CompactWindow = '155000',
  [int]$MaxLimitResumes = 3,
  [double]$MaxLimitWaitHours = 8,
  [double]$ExecutorMinutes = 75,
  [double]$TestMinutes = 25,
  [double]$GodotMinutes = 20,
  [int]$FixRounds = 2,
  [string]$NemotronModel = 'opencode/nemotron-3.5-super-vl-free',
  [string]$GeminiModel = 'gemini-3.8-flash-high',
  [string]$CodexModel = 'gpt-6.1-sol',
  [ValidateSet('', 'Nemotron', 'Gemini')] [string]$StartWith = '',
  [switch]$DryRun,
  # Wait before the one retry after a provider error (HTTP 4xx/5xx, output-limit).
  [int]$ProviderRetrySeconds = 120,
  # How long after a usage-limit reset an Opus session is resumed.
  [int]$LimitResumeDelaySeconds = 120,
  # Overrides for the tool locations (the stub tests point these at fakes).
  [string]$AgyPath = (Join-Path $HOME '.gemini\bin\agy.exe'),
  [string]$GodotConsole = '',
  [string]$ExecDir = ''
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
if (-not $ExecDir) { $ExecDir = Join-Path (Split-Path -Parent $root) ((Split-Path -Leaf $root) + '-exec') }
$logRoot = Join-Path $root '.ticket-logs'
$workRoot = Join-Path $root '.ticket-work'
New-Item -ItemType Directory -Force $logRoot, $workRoot | Out-Null
$exclude = Join-Path $root '.git\info\exclude'
foreach ($pattern in '.ticket-logs/', '.ticket-work/') {
  if (-not (Select-String -Path $exclude -Pattern ('^' + [regex]::Escape($pattern)) -Quiet)) { Add-Content $exclude $pattern }
}
$emptyInput = Join-Path $logRoot 'empty-stdin.txt'
if (-not (Test-Path $emptyInput)) { New-Item -ItemType File $emptyInput | Out-Null }
$rotationFile = Join-Path $logRoot 'v2-rotation.json'
$csvFile = Join-Path $logRoot 'v2-runs.csv'
# The ticket in progress, for scripts/stop-tickets-v2.ps1. Removed when the ticket ends on its own; left behind
# when the runner is interrupted (Ctrl+C, killed) or a ticket ends uncleanly.
$currentFile = Join-Path $logRoot 'v2-current.json'
$settingsFile = Join-Path $PSScriptRoot 'ticket-settings-v2.json'
$utf8 = New-Object Text.UTF8Encoding $false

if (-not $GodotConsole) {
  $GodotConsole = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\" -Recurse -Filter '*_console.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
}
if (-not $GodotConsole -and -not $DryRun) { throw 'Godot console executable not found under %LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\.' }

$OpusCoAuthor = 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
$Executors = @{
  Nemotron = @{ Model = $NemotronModel; CoAuthor = "Co-Authored-By: Nemotron ($NemotronModel) <noreply@nvidia.com>" }
  Gemini   = @{ Model = $GeminiModel; CoAuthor = "Co-Authored-By: Gemini ($GeminiModel) <noreply@google.com>" }
  Codex    = @{ Model = $CodexModel; CoAuthor = "Co-Authored-By: Codex ($CodexModel) <noreply@openai.com>" }
}
# Marked for the rest of the run when an executor hits a quota or usage limit.
$exhausted = @{}
$QuotaPattern = '(?i)quota|resource.?exhausted|usage.?limit|insufficient|billing|credits?\b|limit (has been )?reached|plan limit'
$RatePattern = '(?i)rate.?limit|too many requests|\b429\b'

# ---------------------------------------------------------------------------------------------------
# Small helpers

function Write-Phase([string]$Text, [string]$Color = 'Cyan') { Write-Host $Text -ForegroundColor $Color }

function ConvertTo-RepoPath([string]$Path) { ($Path.Trim() -replace '\\', '/') -replace '^\./', '' }

function Read-Lines([string]$Path) {
  if (-not (Test-Path $Path)) { return @() }
  @(Get-Content -Path $Path -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}

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

function Get-Sha([string]$Path) { (Get-FileHash -Algorithm SHA256 -Path $Path).Hash }

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
  param([string]$File, [string[]]$Arguments, [string]$WorkDir, [string]$OutFile, [double]$TimeoutMinutes = 0, [scriptblock]$OnLine)
  New-Item -ItemType Directory -Force (Split-Path -Parent $OutFile) | Out-Null
  $sp = @{ FilePath = $File; WorkingDirectory = $WorkDir; RedirectStandardOutput = $OutFile; RedirectStandardError = "$OutFile.err"
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

# ---------------------------------------------------------------------------------------------------
# Main working tree state (paths relative to the repository root, forward slashes, .claude/ ignored)

function Get-ChangedPaths([string]$Dir = $root) {
  $raw = git -C $Dir -c core.quotePath=false status --porcelain -uall --no-renames -z
  if ($LASTEXITCODE -ne 0) { throw "git status failed in $Dir" }
  if (-not $raw) { return @() }
  @((@($raw) -join "`0") -split "`0" | Where-Object { $_.Length -gt 3 } | ForEach-Object { $_.Substring(3) } |
    Where-Object { $_ -notlike '.claude/*' })
}

function Get-ForeignChanges($ctx) {
  $known = @{}
  foreach ($p in @($ctx.Baseline) + @($ctx.Locked)) { $known[$p.ToLowerInvariant()] = $true }
  @(Get-ChangedPaths | Where-Object { -not $known.ContainsKey($_.ToLowerInvariant()) })
}

# Undoes everything in the main tree except the baseline files; locked files go back to the architect's version
# (or are removed too with -IncludeLocked).
function Reset-MainTree($ctx, [switch]$IncludeLocked) {
  $paths = @(Get-ForeignChanges $ctx)
  if ($IncludeLocked) { $paths += @($ctx.Locked | Where-Object { Test-Path (Join-Path $root $_) }) }
  foreach ($p in $paths) {
    # ls-tree instead of cat-file -e: a native stderr line under 2>$null throws in PS 5.1 with 'Stop'.
    if (git -C $root ls-tree --name-only HEAD -- $p) { git -C $root checkout -q HEAD -- $p }
    else { Remove-Item -LiteralPath (Join-Path $root $p) -Force -ErrorAction SilentlyContinue }
  }
  if (-not $IncludeLocked) {
    foreach ($p in $ctx.Locked) {
      $snap = Join-Path $ctx.Work "locked-snapshot\$p"
      $dst = Join-Path $root $p
      if ((Test-Path $snap) -and (-not (Test-Path $dst) -or (Get-Sha $dst) -ne $ctx.LockedHash[$p])) {
        New-Item -ItemType Directory -Force (Split-Path -Parent $dst) | Out-Null
        Copy-Item -LiteralPath $snap -Destination $dst -Force
      }
    }
  }
}

# ---------------------------------------------------------------------------------------------------
# 0. Picking the ticket

function Get-NextTicket {
  $open = gh issue list -R $Repo --state open --label ready-for-agent --limit 200 --json number,title,labels,milestone | ConvertFrom-Json
  $milestones = @{}
  gh api "repos/$Repo/milestones?state=all&per_page=100" | ConvertFrom-Json | ForEach-Object { $_ } | ForEach-Object { $milestones[[int]$_.number] = $_ }
  $open | ForEach-Object { $_ } | Where-Object { $_.title -notmatch '^\d\d[a-z]?: ' -and
      ($_.milestone -or @($_.labels | Where-Object { $_.name -in 'hard', 'easy' }).Count) } |
    ForEach-Object { Write-Warning "Skipping #$($_.number) '$($_.title)': its title needs an 'NN: ' or 'NNa: ' prefix to be scheduled." }
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
    $openBlockers = @(gh api "repos/$Repo/issues/$($t.Issue)/dependencies/blocked_by" | ConvertFrom-Json |
      ForEach-Object { $_ } | Where-Object { $_.state -eq 'open' })
    if ($openBlockers.Count -eq 0) { return $t }
  }
  return 'ALL_BLOCKED'
}

# The stage starts at the lowest NN[a] label of its milestone, closed issues included.
function Get-StageStart($t) {
  gh issue list -R $Repo --state all --milestone $t.Stage.title --limit 200 --json title | ConvertFrom-Json |
    ForEach-Object { $_ } | Where-Object { $_.title -match '^(\d\d[a-z]?): ' } |
    ForEach-Object { $null = $_.title -match '^(\d\d[a-z]?): '; $Matches[1] } | Sort-Object | Select-Object -First 1
}

# ---------------------------------------------------------------------------------------------------
# Executor rotation

function Get-ExecutorChain([bool]$FirstOfRun) {
  $last = $null
  if (Test-Path $rotationFile) { try { $last = (Get-Content $rotationFile -Raw | ConvertFrom-Json).last } catch { } }
  $primary = if ($FirstOfRun -and $StartWith) { $StartWith } elseif ($last -eq 'Nemotron') { 'Gemini' } else { 'Nemotron' }
  $other = if ($primary -eq 'Nemotron') { 'Gemini' } else { 'Nemotron' }
  $turn = $primary
  if ($exhausted[$primary]) { $primary, $other = $other, $primary }
  [pscustomobject]@{ Turn = $turn; Chain = @(@($primary, $other, 'Codex') | Where-Object { -not $exhausted[$_] }) }
}

function Save-Rotation([string]$Turn) { Write-Utf8 $rotationFile (@{ last = $Turn } | ConvertTo-Json -Compress) }

# ---------------------------------------------------------------------------------------------------
# Claude sessions (plan, amend, review)

# A resumed session (amend, review and limit resumes continue the plan session) reports the totals of the whole
# session, so per session the last totals are kept and only the growth since then belongs to a call.
$sessionTotals = @{}

function Get-OpusUsage([string]$SessionId, $result) {
  $models = @($result.modelUsage.PSObject.Properties | ForEach-Object { $_.Value })
  $sum = { param($name) [double]($models | Measure-Object -Property $name -Sum).Sum }
  $now = @{ Tokens = (& $sum inputTokens) + (& $sum cacheCreationInputTokens) + (& $sum cacheReadInputTokens) + (& $sum outputTokens)
    Output = (& $sum outputTokens); Cost = [double]$result.total_cost_usd }
  $before = $null
  if ($SessionId) { $before = $sessionTotals[$SessionId]; $sessionTotals[$SessionId] = $now }
  # Totals below the last ones mean the CLI started counting afresh: then all of it is this call's.
  if (-not $before -or $now.Tokens -lt $before.Tokens) { return $now }
  @{ Tokens = $now.Tokens - $before.Tokens; Output = $now.Output - $before.Output; Cost = $now.Cost - $before.Cost }
}

function Add-OpusStats($ctx, $usage, $firstLimits, $lastLimits) {
  if (-not $ctx) { return }
  if ($usage) { $ctx.OpusTokens += $usage.Tokens }
  if ($firstLimits -and -not $ctx.LimitsBefore) { $ctx.LimitsBefore = $firstLimits }
  if ($lastLimits) { $ctx.LimitsAfter = $lastLimits }
}

function Invoke-Claude {
  # The ForEach-Object block below runs in this scope, so its assignments reach $final, $sessionId and the stats.
  [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '')]
  param($ctx, [string]$Log, [string]$Prompt, [string]$ResumeId, [string]$RunEffort)
  $cliArgs = @('-p', '--dangerously-skip-permissions', '--output-format', 'stream-json', '--verbose',
    '--settings', $settingsFile, '--model', 'opus', '--effort', $RunEffort)
  if ($ResumeId) { $cliArgs += @('--resume', $ResumeId) }
  $final = ''; $sessionId = $ResumeId; $resetsAt = $null
  $firstLimits = $null; $lastLimits = $null; $lastResult = $null; $turns = 0; $minutes = 0.0
  # The prompt goes in as an argument: piped stdin does not expand a slash command.
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
  $usage = $null
  if ($lastResult) {
    $usage = Get-OpusUsage $sessionId $lastResult
    Write-Host ('  turns: {0}, {1:N0} min, API-price equivalent: ${2:N2}' -f $turns, $minutes, $usage.Cost) -ForegroundColor DarkCyan
    if ($lastLimits) {
      Write-Host ('  subscription limits: 5-hour {0:P0} -> {1:P0}, weekly {2:P0} -> {3:P0}' -f `
        $firstLimits.five_hour.utilization, $lastLimits.five_hour.utilization,
        $firstLimits.seven_day.utilization, $lastLimits.seven_day.utilization) -ForegroundColor DarkCyan
    }
    Write-Host ('  tokens: {0:N1}M total, {1:N0}k output' -f ($usage.Tokens / 1e6), ($usage.Output / 1e3)) -ForegroundColor DarkCyan
  }
  Add-OpusStats $ctx $usage $firstLimits $lastLimits
  if ($exit -ne 0 -and -not $resetsAt) { throw "Claude exited with code $exit. Log: $Log" }
  [pscustomobject]@{ Final = $final; SessionId = $sessionId; ResetsAt = $resetsAt }
}

# Runs a session; when the subscription limit stops it, waits for the reset and resumes the same session.
function Invoke-Opus($ctx, [string]$Log, [string]$Prompt, [string]$ResumeId) {
  $runEffort = if ($ctx.T.IsStageEnd) { $StageEffort } else { $Effort }
  $run = Invoke-Claude $ctx $Log $Prompt $ResumeId $runEffort
  for ($i = 1; $run.ResetsAt; $i++) {
    if ($i -gt $MaxLimitResumes) { throw "Usage limit hit more than $MaxLimitResumes times in one session. Log: $Log" }
    $resume = [DateTimeOffset]::FromUnixTimeSeconds([long]$run.ResetsAt).LocalDateTime.AddSeconds($LimitResumeDelaySeconds)
    $wait = $resume - (Get-Date)
    if ($wait.TotalHours -gt $MaxLimitWaitHours) { throw "Usage limit resets at $resume, more than $MaxLimitWaitHours h away. Log: $Log" }
    Write-Host "  Usage limit hit. Waiting until $resume to resume session $($run.SessionId) ($i/$MaxLimitResumes)." -ForegroundColor Yellow
    if ($wait.TotalSeconds -gt 0) { Start-Sleep -Seconds ([int]$wait.TotalSeconds) }
    $run = Invoke-Claude $ctx $Log 'The usage limit interrupted you. Continue from where you stopped, following the same rules.' $run.SessionId $runEffort
  }
  $run
}

# Resumes the planning session; falls back to a fresh session if it cannot be resumed.
function Invoke-OpusInPlanSession($ctx, [string]$Log, [string]$Prompt) {
  if ($ctx.PlanSession) {
    try { return Invoke-Opus $ctx $Log $Prompt $ctx.PlanSession }
    catch { Write-Host "  Could not resume the planning session ($($_.Exception.Message)); starting a fresh one." -ForegroundColor Yellow }
  }
  Invoke-Opus $ctx $Log $Prompt $null
}

# ---------------------------------------------------------------------------------------------------
# Prompts

function Get-ContextBlock($ctx) {
  $t = $ctx.T
  $difficulty = if ($t.Difficulty) { $t.Difficulty } else { 'unlabelled' }
  $stage = if ($t.Stage) { "'$($t.Stage.title)'" } else { 'none' }
  $stageEnd = if ($t.IsStageEnd) { "yes: this ticket completes stage $stage, which starts at ticket $($ctx.StageStart)" } else { 'no' }
  @"
Runner context:
- Repository: $Repo. Ticket: GitHub issue #$($t.Issue), title: $($t.Title)
- Difficulty: $difficulty
- Stage: $stage; stage end: $stageEnd
- Work directory: .ticket-work/$($t.Label)/ ; logs: .ticket-logs/$($t.Label)/
- Executors: $($ctx.Chain -join ' -> ') (first one implements; the others are fallbacks)
"@
}

function Get-PlanPrompt($ctx) {
  "/plan-ticket $($ctx.T.Label) $($ctx.T.Issue)`n`n" + (Get-ContextBlock $ctx)
}

function Get-ReviewPrompt($ctx) {
  $baseline = if ($ctx.Baseline.Count) { ($ctx.Baseline | ForEach-Object { "  - $_" }) -join "`n" } else { '  (none)' }
  $round = if ($ctx.Rejects -or $ctx.Amends) { "`n- This is a re-review after: $($ctx.Rejects) reject round(s), $($ctx.Amends) amend(s). Judge the current state again in full." } else { '' }
  @"
/review-ticket $($ctx.T.Label) $($ctx.T.Issue)

$(Get-ContextBlock $ctx)
- Executor that did the work: $($ctx.Executor) ($($Executors[$ctx.Executor].Model))
- Commit co-author lines, in this order:
  $($Executors[$ctx.Executor].CoAuthor)
  $OpusCoAuthor
- Gate: green. Summary log: $($ctx.LastGateLog)
- Executor report: .ticket-work/$($ctx.T.Label)/REPORT.md ; captures listed in .ticket-work/$($ctx.T.Label)/captures.txt
- Locked acceptance tests (part of this ticket's changes): $($ctx.Locked -join ', ')
- Baseline files, dirty before this ticket (never stage, reset or commit them):
$baseline$round
"@
}

function Get-ExecutorPrompt($ctx) {
  $l = $ctx.T.Label
  @"
# Executor instructions: ticket $l ($($ctx.T.Title))

You are the implementer, working unattended in a clone of the repository: nobody will answer questions.
An architect has read the ticket and the code and written an exact plan. Carry it out.

1. Read .ticket-work/$l/PLAN.md and carry it out completely. Where it is silent, make a reasonable decision and
   record it in your report.
2. Project rules:
   - Scope: implement only this ticket, so that every acceptance criterion is met. Architecture: the
     engine-independent C# simulation core (src/Scp3008.Core) behind one facade; the Godot presentation layer
     (game/) only reads from it.
   - Tests: facade-level tests in tests/Scp3008.Core.Tests (scenario -> advance time -> assert on snapshot/events).
     The game project must build headless with the Godot console executable.
   - Use the GLOSSARY.md terms; add a newly settled domain term to it when the plan says so.
   - Keep the context small: write the full output of test runs, builds and Godot runs to a file under
     .ticket-logs/ and print only the summary and failures. Screenshots at most 1280 px wide; look at each once.
3. The Godot console executable is: $GodotConsole
   Headless build: <that exe> --headless --path game --build-solutions --quit
4. Never run git commit, git push, or any gh command. Do not run git reset, git checkout, git stash or git clean.
5. Never modify the files listed in .ticket-work/$l/locked.txt (the locked acceptance tests). If one of them looks
   wrong, write a line
   LOCKED_TEST_SUSPECT: <file>:<test> - <why>
   in REPORT.md and in your final message, and stop.
6. Run only the tests of the test classes you touch, for example
   dotnet test tests/Scp3008.Core.Tests -c Release --filter FullyQualifiedName~<ClassName>
   (Release: the generation tests run about 5x faster than in Debug.)
   Never run the whole test suite: the runner does that after you, with the build and the Godot build.
7. Take the captures the plan lists (the paths are in .ticket-work/$l/captures.txt) and look at each one once.
8. Do not run the benchmark unless the plan says so.
9. Write .ticket-work/$l/REPORT.md with these sections: Changes (per file), Tests (what you ran and the results),
   Captures (each file and what it shows), Decisions (what the plan left open and what you chose),
   Not done (anything missing, or "nothing").
10. The last line of REPORT.md and of your final message must be exactly TICKET_DONE, or
    TICKET_BLOCKED: <reason> if you could not finish.
"@
}

# ---------------------------------------------------------------------------------------------------
# Executor workspace

function Sync-Work($ctx) {
  $dst = Join-Path $ExecDir ".ticket-work\$($ctx.T.Label)"
  New-Item -ItemType Directory -Force $dst | Out-Null
  foreach ($f in 'PLAN.md', 'locked.txt', 'captures.txt') {
    $src = Join-Path $ctx.Work $f
    if (Test-Path $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $dst $f) -Force }
  }
  Write-Utf8 (Join-Path $dst 'EXEC-PROMPT.md') (Get-ExecutorPrompt $ctx)
  foreach ($p in $ctx.Locked) {
    $target = Join-Path $ExecDir $p
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $ctx.Work "locked-snapshot\$p") -Destination $target -Force
  }
}

function Reset-Workspace($ctx) {
  if (-not (Test-Path (Join-Path $ExecDir '.git'))) {
    Write-Host "  Creating the executor workspace $ExecDir" -ForegroundColor DarkGray
    git clone -q $root $ExecDir
    if ($LASTEXITCODE -ne 0) { throw "git clone into $ExecDir failed." }
    # No remote: nothing can be pushed from the executor workspace.
    git -C $ExecDir remote remove origin
  }
  $ex = Join-Path $ExecDir '.git\info\exclude'
  foreach ($pattern in '.ticket-logs/', '.ticket-work/') {
    if (-not ((Test-Path $ex) -and (Select-String -Path $ex -Pattern ('^' + [regex]::Escape($pattern)) -Quiet))) { Add-Content $ex $pattern }
  }
  $head = (git -C $root rev-parse HEAD).Trim()
  git -C $ExecDir fetch -q $root $head
  if ($LASTEXITCODE -ne 0) { git -C $ExecDir fetch -q $root HEAD }
  git -C $ExecDir reset -q --hard $head
  if ($LASTEXITCODE -ne 0) { throw "Could not reset $ExecDir to $head." }
  # The Godot import cache survives, so captures do not reimport every asset.
  git -C $ExecDir clean -q -fdx -e game/.godot/
  $ctx.ExecHead = $head
  $ctx.Sessions = @{}
  Sync-Work $ctx
}

# Stages everything in the clone and writes the ticket's changes (without work files, locked files and
# reimport noise on existing *.import files) as a binary patch. Returns $null when there are no changes.
function New-WorkspacePatch($ctx, [string]$PatchFile, [switch]$All) {
  git -C $ExecDir add -A
  $raw = git -C $ExecDir -c core.quotePath=false diff --cached --no-renames --name-status -z HEAD
  $parts = @((@($raw) -join "`0") -split "`0" | Where-Object { $_ -ne '' })
  $locked = @{}; foreach ($p in $ctx.Locked) { $locked[$p.ToLowerInvariant()] = $true }
  $paths = @()
  for ($i = 0; $i + 1 -lt $parts.Count; $i += 2) {
    $status = $parts[$i]; $path = $parts[$i + 1]
    if (-not $All) {
      if ($path -like '.ticket-work/*' -or $path -like '.ticket-logs/*') { continue }
      if ($locked.ContainsKey($path.ToLowerInvariant())) { continue }
      if ($status -eq 'M' -and $path -like '*.import') { continue }
    }
    $paths += $path
  }
  if (-not $paths.Count) { return $null }
  if (Test-Path $PatchFile) { Remove-Item $PatchFile -Force }
  git -C $ExecDir diff --cached --binary --no-renames "--output=$PatchFile" HEAD -- @paths
  if ($LASTEXITCODE -ne 0) { throw "git diff in $ExecDir failed." }
  $PatchFile
}

function Copy-WorkBack($ctx) {
  $src = Join-Path $ExecDir ".ticket-work\$($ctx.T.Label)"
  if (-not (Test-Path $src)) { return }
  Get-ChildItem -LiteralPath $src | Where-Object { $_.Name -notin 'PLAN.md', 'locked.txt', 'captures.txt' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $ctx.Work -Recurse -Force }
}

# ---------------------------------------------------------------------------------------------------
# 2. Executors

function Get-ExecutorCommand([string]$Exec, [string]$Message, [string]$SessionId) {
  switch ($Exec) {
    'Nemotron' {
      $a = @('run', '--standalone', '--auto', '--format', 'json', '-m', $NemotronModel)
      if ($SessionId) { $a += @('--session', $SessionId) }
      @{ File = (Resolve-Tool 'opencode.cmd'); Args = $a + @($Message) }
    }
    'Gemini' {
      $a = @('-p', $Message, '--dangerously-skip-permissions', '--model', $GeminiModel, '--output-format', 'stream-json')
      if ($SessionId) { $a += @('--conversation', $SessionId) }
      @{ File = $AgyPath; Args = $a }
    }
    'Codex' {
      $a = @(if ($SessionId) { 'exec', 'resume' } else { 'exec' })
      $a += @('--dangerously-bypass-approvals-and-sandbox', '--json', '--model', $CodexModel)
      if ($SessionId) { $a += $SessionId }
      @{ File = (Resolve-Tool 'codex.cmd'); Args = $a + @($Message) }
    }
  }
}

# Reads one JSON event of an executor's stream into $s (session id, final text, tokens, errors) and shows it.
function Read-ExecutorEvent([string]$Exec, [string]$Line, $s) {
  try { $ev = $Line | ConvertFrom-Json } catch { return }
  switch ($Exec) {
    'Nemotron' {
      if ($ev.sessionID) { $s.SessionId = $ev.sessionID }
      switch ($ev.type) {
        'text' { if ($ev.part.text.Trim()) { $s.Final = $ev.part.text; Write-Host ('  > ' + ($ev.part.text.Trim() -split "`n")[0]) -ForegroundColor Gray } }
        'tool_use' { Write-Host "  - $($ev.part.tool)" -ForegroundColor DarkGray }
        'step_finish' {
          $tk = $ev.part.tokens
          $s.In += [double]$tk.input; $s.Out += [double]$tk.output + [double]$tk.reasoning; $s.Cache += [double]$tk.cache.read
        }
        'error' {
          $msg = "$($ev.error.type): $($ev.error.message) (status $($ev.error.status))"
          $s.Errors += $msg; Write-Host "  executor error: $msg" -ForegroundColor Red
        }
      }
    }
    'Gemini' {
      if ($ev.conversation_id) { $s.SessionId = $ev.conversation_id }
      if ($ev.event -eq 'step_update') {
        $u = $ev.step_update
        if ($u.conversation_id) { $s.SessionId = $u.conversation_id }
        if ($u.step_type -eq 'tool' -and $u.state -eq 'ACTIVE') { Write-Host "  - $($u.tool_name)" -ForegroundColor DarkGray }
      } elseif ($ev.event -eq 'result') {
        $r = $ev.result
        if ($r.conversation_id) { $s.SessionId = $r.conversation_id }
        $s.Final = $r.response
        if ($r.response) { Write-Host ('  > ' + (Get-LastLine $r.response)) -ForegroundColor Gray }
        $s.In += [double]$r.usage.input_tokens; $s.Out += [double]$r.usage.output_tokens + [double]$r.usage.thinking_tokens
        $s.Cache += [double]$r.usage.cache_read_tokens
        if ($r.status -and $r.status -ne 'SUCCESS') { $s.Errors += "agy status $($r.status): $($Line.Substring(0, [Math]::Min(400, $Line.Length)))" }
      } elseif ($ev.event -eq 'error' -or $ev.error) {
        $s.Errors += "agy error: $($Line.Substring(0, [Math]::Min(400, $Line.Length)))"
        Write-Host "  executor error: $($Line.Substring(0, [Math]::Min(200, $Line.Length)))" -ForegroundColor Red
      }
    }
    'Codex' {
      if ($ev.type -eq 'thread.started') { $s.SessionId = $ev.thread_id }
      elseif ($ev.type -eq 'item.completed' -and $ev.item.type -eq 'agent_message') {
        $s.Final = $ev.item.text
        if ($ev.item.text.Trim()) { Write-Host ('  > ' + ($ev.item.text.Trim() -split "`n")[0]) -ForegroundColor Gray }
      } elseif ($ev.type -eq 'item.started' -and $ev.item.type -eq 'command_execution') { Write-Host '  - Shell' -ForegroundColor DarkGray }
      elseif ($ev.type -eq 'turn.completed') {
        $s.In += [double]$ev.usage.input_tokens - [double]$ev.usage.cached_input_tokens
        $s.Cache += [double]$ev.usage.cached_input_tokens; $s.Out += [double]$ev.usage.output_tokens
      } elseif ($ev.type -eq 'turn.failed' -or $ev.type -eq 'error') {
        $msg = if ($ev.error.message) { $ev.error.message } elseif ($ev.message) { $ev.message } else { $Line }
        $s.Errors += "codex: $msg"; Write-Host "  executor error: $msg" -ForegroundColor Red
      }
    }
  }
}

# One executor process. Status: done | blocked | suspect | timeout | exhausted | provider | nomarker.
function Start-ExecutorProcess($ctx, [string]$Exec, [string]$Message, [string]$SessionId) {
  $ctx.ExecRuns++
  $log = Join-Path $ctx.Logs ('exec-{0:D2}-{1}.jsonl' -f $ctx.ExecRuns, $Exec.ToLowerInvariant())
  $cmd = Get-ExecutorCommand $Exec $Message $SessionId
  $resumed = if ($SessionId) { ", continuing session $SessionId" } else { '' }
  Write-Phase "  [$Exec] $Message$resumed" 'DarkCyan'
  $s = @{ SessionId = $SessionId; Final = ''; In = 0.0; Out = 0.0; Cache = 0.0; Errors = @() }
  $onLine = { param($line) Read-ExecutorEvent $Exec $line $s }
  $run = Invoke-Logged -File $cmd.File -Arguments $cmd.Args -WorkDir $ExecDir -OutFile $log -TimeoutMinutes $ExecutorMinutes -OnLine $onLine
  $ctx.ExecMinutes += $run.Minutes
  $ctx.ExecIn += $s.In; $ctx.ExecOut += $s.Out; $ctx.ExecCache += $s.Cache
  Write-Host ('  [{0}] exit {1}, {2:N0} min, tokens: {3:N1}M in, {4:N0}k out, {5:N1}M cache read' -f $Exec, $run.ExitCode, $run.Minutes,
    ($s.In / 1e6), ($s.Out / 1e3), ($s.Cache / 1e6)) -ForegroundColor DarkCyan

  $report = Join-Path $ExecDir ".ticket-work\$($ctx.T.Label)\REPORT.md"
  $reportText = if (Test-Path $report) { Get-Content $report -Raw -Encoding UTF8 } else { '' }
  $marker = Get-LastLine $s.Final
  if ($marker -notmatch '^TICKET_(DONE|BLOCKED)') { $marker = Get-LastLine $reportText }
  $errText = (@($s.Errors) + @(Get-Tail $run.ErrFile 30)) -join "`n"
  $suspects = @(([regex]::Matches("$($s.Final)`n$reportText", 'LOCKED_TEST_SUSPECT:[^\r\n]*') | ForEach-Object { $_.Value }) | Select-Object -Unique)
  $status =
    if ($suspects.Count) { 'suspect' }
    elseif ($run.TimedOut) { 'timeout' }
    elseif ($marker -match '^TICKET_BLOCKED') { 'blocked' }
    elseif ($marker -eq 'TICKET_DONE') { 'done' }      # an exit code 1 after TICKET_DONE still delivered the work
    elseif ($errText -match $QuotaPattern) { 'exhausted' }
    elseif ($s.Errors.Count -or $run.ExitCode -ne 0) { 'provider' }
    else { 'nomarker' }
  Write-Host "  [$Exec] status: $status$(if ($marker) { " ($marker)" })" -ForegroundColor $(if ($status -in 'done', 'nomarker') { 'Green' } else { 'Yellow' })
  if ($s.SessionId) { $ctx.Sessions[$Exec] = $s.SessionId }
  [pscustomobject]@{ Status = $status; Marker = $marker; Suspects = $suspects; ErrorText = $errText; Final = $s.Final; Log = $log }
}

# An executor run with the one retry after a provider error.
function Invoke-ExecutorRun($ctx, [string]$Exec, [string]$Message, [switch]$Continue) {
  $sid = if ($Continue) { $ctx.Sessions[$Exec] } else { $null }
  $r = Start-ExecutorProcess $ctx $Exec $Message $sid
  if ($r.Status -eq 'provider') {
    Write-Host "  Provider error; one retry in $ProviderRetrySeconds s." -ForegroundColor Yellow
    Start-Sleep -Seconds $ProviderRetrySeconds
    $sid = $ctx.Sessions[$Exec]
    $retryMessage = if ($sid) { "A provider error interrupted you. Continue from where you stopped. Your instructions are in .ticket-work/$($ctx.T.Label)/EXEC-PROMPT.md." } else { $Message }
    $r = Start-ExecutorProcess $ctx $Exec $retryMessage $sid
    if ($r.Status -eq 'provider' -and $r.ErrorText -match $RatePattern) { $r.Status = 'exhausted' }
  }
  if ($r.Status -eq 'exhausted') {
    $exhausted[$Exec] = $true
    Write-Host "  $Exec hit a quota or usage limit; it is skipped for the rest of this run." -ForegroundColor Yellow
  }
  $r
}

# Writes a round file in the clone and returns the short message that points the executor at it.
function New-RoundMessage($ctx, [string]$Name, [string]$Body) {
  $l = $ctx.T.Label
  Write-Utf8 (Join-Path $ExecDir ".ticket-work\$l\$Name") $Body
  "Read .ticket-work/$l/$Name and follow it exactly."
}

# ---------------------------------------------------------------------------------------------------
# 3. Gate

function Get-LockedClasses($ctx) {
  $names = @{}
  foreach ($p in $ctx.Locked) {
    $f = Join-Path $ExecDir $p
    if (Test-Path $f) { foreach ($m in [regex]::Matches((Get-Content $f -Raw), '\bclass\s+(\w+)')) { $names[$m.Groups[1].Value] = $true } }
  }
  $names
}

function Invoke-Gate($ctx) {
  $ctx.GateRuns++
  $n = $ctx.GateRuns
  $log = Join-Path $ctx.Logs "gate-$n.log"
  $ctx.LastGateLog = ".ticket-logs/$($ctx.T.Label)/gate-$n.log"
  $started = Get-Date
  Write-Phase "  [gate $n] checking the executor's result" 'DarkCyan'
  $fail = {
    param([string]$Summary, [bool]$OnlyLocked)
    $lines = @($Summary -split "`r?`n")
    if ($lines.Count -gt 60) { $Summary = (($lines | Select-Object -First 60) -join "`n") + "`n... ($($lines.Count - 60) more lines in $($ctx.LastGateLog))" }
    Add-Content -Path $log -Value "`n=== GATE RED`n$Summary" -Encoding UTF8
    $ctx.GateMinutes += ((Get-Date) - $started).TotalMinutes
    $ctx.LastGateSummary = $Summary
    Write-Host "  [gate $n] RED: $((@($Summary -split "`n"))[0])" -ForegroundColor Yellow
    [pscustomobject]@{ Green = $false; Summary = $Summary; OnlyLocked = $OnlyLocked }
  }
  $lockedSet = @{}; foreach ($p in $ctx.Locked) { $lockedSet[$p.ToLowerInvariant()] = $true }

  # 1. Locked acceptance tests unchanged.
  $changed = @($ctx.Locked | Where-Object { $f = Join-Path $ExecDir $_; -not (Test-Path $f) -or (Get-Sha $f) -ne $ctx.LockedHash[$_] })
  if ($changed.Count) { return & $fail "Locked acceptance test files were modified or deleted (never change them): $($changed -join ', ')" $false }

  # 2. No commits in the clone.
  $head = (git -C $ExecDir rev-parse HEAD).Trim()
  if ($head -ne $ctx.ExecHead) { return & $fail "Commits were made in the workspace (HEAD $head, expected $($ctx.ExecHead)). Never run git commit." $false }

  # 3. Build.
  $dotnet = Resolve-Tool 'dotnet'
  $buildLog = Join-Path $ctx.Logs "gate-$n-build.log"
  $b = Invoke-Logged -File $dotnet -Arguments @('build', 'Scp3008.sln', '-nologo', '-v:minimal') -WorkDir $ExecDir -OutFile $buildLog -TimeoutMinutes 20
  Add-Content -Path $log -Value "=== dotnet build: exit $($b.ExitCode) (log $buildLog)" -Encoding UTF8
  if ($b.ExitCode -ne 0) {
    $errors = @(Get-Content $buildLog -Encoding UTF8 | Where-Object { $_ -match ': error ' } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
    if (-not $errors.Count) { $errors = @((Get-Tail $buildLog 30) -split "`n") }
    $prefix = ($ExecDir.TrimEnd('\') + '\').ToLowerInvariant()
    $files = @($errors | ForEach-Object { if ($_ -match '^(.+?)\(\d+,\d+\): error ') { $m = $Matches[1].ToLowerInvariant(); if ($m.StartsWith($prefix)) { $m = $m.Substring($prefix.Length) }; ConvertTo-RepoPath $m } else { '?' } })
    $onlyLocked = [bool]($files.Count -and -not @($files | Where-Object { -not $lockedSet.ContainsKey($_) }).Count)
    return & $fail ("dotnet build failed ($($errors.Count) errors):`n" + ($errors -join "`n")) $onlyLocked
  }

  # 4. The whole core test suite, in Release: the generation-heavy tests run about 5x faster than in Debug.
  #    That builds the core and the tests once more; the Debug build above already reported any compile errors.
  $testLog = Join-Path $ctx.Logs "gate-$n-test.log"
  $trxDir = Join-Path $ctx.Logs "gate-$n-results"
  $tr = Invoke-Logged -File $dotnet -Arguments @('test', 'tests/Scp3008.Core.Tests', '-c', 'Release', '-nologo', '--logger', "trx;LogFileName=gate-$n.trx", '--results-directory', $trxDir) `
    -WorkDir $ExecDir -OutFile $testLog -TimeoutMinutes $TestMinutes
  Add-Content -Path $log -Value "=== dotnet test: exit $($tr.ExitCode), $([int]$tr.Minutes) min (log $testLog)" -Encoding UTF8
  if ($tr.TimedOut) { return & $fail "The test run did not finish within $TestMinutes minutes (a hang counts as a failure). Look for an endless loop or a deadlock.`n$(Get-Tail $testLog 20)" $false }
  if ($tr.ExitCode -ne 0) {
    $trx = Get-ChildItem $trxDir -Filter '*.trx' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    $failures = @(); $classes = @()
    if ($trx) {
      [xml]$x = Get-Content $trx.FullName -Raw -Encoding UTF8
      $defs = @{}; foreach ($d in @($x.TestRun.TestDefinitions.UnitTest)) { if ($d) { $defs[$d.id] = $d.TestMethod.className } }
      foreach ($r in @($x.TestRun.Results.UnitTestResult | Where-Object { $_ -and $_.outcome -eq 'Failed' })) {
        $cls = "$($defs[$r.testId])"; $classes += (($cls -split '[.+]')[-1])
        $msg = "$($r.Output.ErrorInfo.Message)".Trim() -split "`r?`n" | Select-Object -First 3
        $failures += "FAILED $($r.testName)`n    " + ($msg -join "`n    ")
      }
    }
    if (-not $failures.Count) { return & $fail "dotnet test failed (exit $($tr.ExitCode)) without a readable result:`n$(Get-Tail $testLog 30)" $false }
    $lockedClasses = Get-LockedClasses $ctx
    $onlyLocked = -not @($classes | Where-Object { -not $lockedClasses.ContainsKey($_) }).Count
    return & $fail ("$($failures.Count) test(s) failed:`n" + ($failures -join "`n")) $onlyLocked
  }

  # 5. Headless Godot build.
  $godotLog = Join-Path $ctx.Logs "gate-$n-godot.log"
  $g = Invoke-Logged -File $GodotConsole -Arguments @('--headless', '--path', 'game', '--build-solutions', '--quit') -WorkDir $ExecDir -OutFile $godotLog -TimeoutMinutes $GodotMinutes
  Add-Content -Path $log -Value "=== godot build: exit $($g.ExitCode) (log $godotLog)" -Encoding UTF8
  if ($g.ExitCode -ne 0) {
    $errors = @(@(Get-Content $godotLog, "$godotLog.err" -Encoding UTF8 -ErrorAction SilentlyContinue) | Where-Object { $_ -match '(?i)error' } | Select-Object -Unique -First 40)
    return & $fail ("The headless Godot build failed (exit $($g.ExitCode)):`n" + ($errors -join "`n")) $false
  }

  # 6. Report and captures.
  $l = $ctx.T.Label
  if (-not (Test-Path (Join-Path $ExecDir ".ticket-work\$l\REPORT.md"))) { return & $fail ".ticket-work/$l/REPORT.md is missing. Write the report." $false }
  $missing = @(Read-Lines (Join-Path $ctx.Work 'captures.txt') | ForEach-Object { ConvertTo-RepoPath $_ } | Where-Object { -not (Test-Path (Join-Path $ExecDir $_)) })
  if ($missing.Count) { return & $fail "Planned captures are missing (take them as the plan says): $($missing -join ', ')" $false }

  Add-Content -Path $log -Value '=== GATE GREEN' -Encoding UTF8
  $ctx.GateMinutes += ((Get-Date) - $started).TotalMinutes
  Write-Host "  [gate $n] GREEN" -ForegroundColor Green
  [pscustomobject]@{ Green = $true; Summary = ''; OnlyLocked = $false }
}

# ---------------------------------------------------------------------------------------------------
# 1 / 3a. Plan and amend

function Save-Locked($ctx) {
  $ctx.Locked = @(Read-Lines (Join-Path $ctx.Work 'locked.txt') | ForEach-Object { ConvertTo-RepoPath $_ } | Select-Object -Unique)
  if (-not $ctx.Locked.Count) { return 'locked.txt lists no acceptance test files.' }
  $bad = @($ctx.Locked | Where-Object { $_ -notlike 'tests/Scp3008.Core.Tests/*' -or -not (Test-Path (Join-Path $root $_)) })
  if ($bad.Count) { return "Locked files missing or outside tests/Scp3008.Core.Tests/: $($bad -join ', ')" }
  $snapDir = Join-Path $ctx.Work 'locked-snapshot'
  if (Test-Path $snapDir) { Remove-Item $snapDir -Recurse -Force }
  $ctx.LockedHash = @{}
  foreach ($p in $ctx.Locked) {
    $snap = Join-Path $snapDir $p
    New-Item -ItemType Directory -Force (Split-Path -Parent $snap) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $p) -Destination $snap -Force
    $ctx.LockedHash[$p] = Get-Sha $snap
  }
  $foreign = @(Get-ForeignChanges $ctx)
  if ($foreign.Count) { return "The architect changed files besides the locked tests: $($foreign -join ', ')" }
  $null
}

function Invoke-Plan($ctx) {
  $started = Get-Date
  Write-Phase "  [plan] Opus architect" 'DarkCyan'
  $run = Invoke-Opus $ctx (Join-Path $ctx.Logs 'plan.jsonl') (Get-PlanPrompt $ctx) $null
  $ctx.PlanSession = $run.SessionId
  $ctx.PlanMinutes += ((Get-Date) - $started).TotalMinutes
  $last = Get-LastLine $run.Final
  if ($last -match '^PLAN_BLOCKED') { return "The architect could not plan the ticket: $last" }
  if ($last -ne 'PLAN_DONE') { return "The planning session ended without PLAN_DONE. Last line: $last" }
  if (-not (Test-Path (Join-Path $ctx.Work 'PLAN.md'))) { return "The architect did not write .ticket-work/$($ctx.T.Label)/PLAN.md." }
  if (-not (Test-Path (Join-Path $ctx.Work 'captures.txt'))) { Write-Utf8 (Join-Path $ctx.Work 'captures.txt') '' }
  Save-Locked $ctx
}

# Resumes the planning session with the evidence. Returns the architect's answer, or $null when the one amend
# of this ticket is already used. Stops the ticket when the amend itself goes wrong.
function Invoke-Amend($ctx, [string]$Evidence) {
  if ($ctx.Amends -ge 1) { return $null }
  $ctx.Amends++
  $started = Get-Date
  Write-Phase "  [amend] Opus architect re-checks the plan and the locked tests" 'DarkCyan'
  $prompt = "/plan-ticket $($ctx.T.Label) $($ctx.T.Issue) --amend`n`n$(Get-ContextBlock $ctx)`nEvidence (executor: $($ctx.Executor)):`n$Evidence"
  $run = Invoke-OpusInPlanSession $ctx (Join-Path $ctx.Logs 'amend.jsonl') $prompt
  if ($run.SessionId) { $ctx.PlanSession = $run.SessionId }
  $ctx.PlanMinutes += ((Get-Date) - $started).TotalMinutes
  $last = Get-LastLine $run.Final
  if ($last -notmatch '^AMEND_(CHANGED|CONFIRMED)') { Stop-Ticket $ctx "The amend session ended without AMEND_CHANGED/AMEND_CONFIRMED. Last line: $last" }
  $problem = Save-Locked $ctx
  if ($problem) { Stop-Ticket $ctx "After the amend: $problem" }
  Sync-Work $ctx
  $run.Final
}

# ---------------------------------------------------------------------------------------------------
# Execute + gate + fix rounds + amend, for one executor. Returns green | failed | stop.

function Invoke-ExecutorPhase($ctx, [string]$Exec, [string]$Message, [switch]$Continue) {
  $l = $ctx.T.Label
  $r = Invoke-ExecutorRun $ctx $Exec $Message -Continue:$Continue
  $rounds = 0
  while ($true) {
    if ($r.Status -in 'exhausted', 'timeout', 'provider', 'blocked') {
      $ctx.LastFailure = "$Exec ended with status '$($r.Status)'. $($r.Marker) $($r.ErrorText)".Trim()
      return 'failed'
    }
    if ($r.Status -eq 'suspect') {
      $reportTail = Get-Tail (Join-Path $ExecDir ".ticket-work\$l\REPORT.md") 40
      $answer = Invoke-Amend $ctx ("The executor reports a suspect locked test:`n" + ($r.Suspects -join "`n") + "`n`nEnd of its REPORT.md:`n$reportTail")
      if ($null -eq $answer) { Stop-Ticket $ctx "A second locked-test suspect after the one amend: $($r.Suspects -join '; ')" }
      $msg = New-RoundMessage $ctx "AMEND-$($ctx.Amends).md" (Get-AmendRound $ctx $answer)
      $r = Invoke-ExecutorRun $ctx $Exec $msg -Continue
      continue
    }
    $gate = Invoke-Gate $ctx
    if ($gate.Green) { return 'green' }
    if ($rounds -lt $FixRounds) {
      $rounds++; $ctx.FixRounds++
      $body = @"
# Fix round $rounds for ticket $l

The runner's gate (build, the whole core test suite, the headless Godot build, report and captures) failed after
your last run. Your standing instructions are in .ticket-work/$l/EXEC-PROMPT.md and the plan in
.ticket-work/$l/PLAN.md; they still apply (never modify the locked files, run only the affected test classes).
Fix the failures below, update REPORT.md, and end with TICKET_DONE or TICKET_BLOCKED: <reason>.

## Gate failures

$($gate.Summary)
"@
      $r = Invoke-ExecutorRun $ctx $Exec (New-RoundMessage $ctx "FIX-$($ctx.FixRounds).md" $body) -Continue
      continue
    }
    if ($gate.OnlyLocked) {
      $answer = Invoke-Amend $ctx "After $rounds fix round(s) the only remaining gate failures are in locked acceptance tests:`n$($gate.Summary)"
      if ($null -eq $answer) { Stop-Ticket $ctx "Locked-test-only failures again after the one amend:`n$($gate.Summary)" }
      $r = Invoke-ExecutorRun $ctx $Exec (New-RoundMessage $ctx "AMEND-$($ctx.Amends).md" (Get-AmendRound $ctx $answer)) -Continue
      $rounds = $FixRounds  # the amend round is one extra round; no further fix rounds after it
      continue
    }
    $ctx.LastFailure = "$Exec still red after $rounds fix round(s):`n$($gate.Summary)"
    return 'failed'
  }
}

function Get-AmendRound($ctx, [string]$Answer) {
  $l = $ctx.T.Label
  @"
# The architect's answer for ticket $l

The architect re-checked the plan and the locked acceptance tests against your report or the gate failures.
PLAN.md and the locked files in this workspace are now the architect's current version. Your standing
instructions are in .ticket-work/$l/EXEC-PROMPT.md. Continue the work accordingly, update REPORT.md, and end
with TICKET_DONE or TICKET_BLOCKED: <reason>.

## Answer

$Answer
"@
}

# ---------------------------------------------------------------------------------------------------
# Ticket bookkeeping

function Add-CsvRow($ctx, [string]$Verdict) {
  $lb = $ctx.LimitsBefore; $la = $ctx.LimitsAfter
  [pscustomobject][ordered]@{
    date = (Get-Date).ToString('yyyy-MM-dd HH:mm'); ticket = $ctx.T.Label; issue = $ctx.T.Issue
    primary = $ctx.Turn; finished_by = $ctx.Executor; attempts = $ctx.Attempts; fix_rounds = $ctx.FixRounds
    amends = $ctx.Amends; rejects = $ctx.Rejects
    plan_min = [Math]::Round($ctx.PlanMinutes, 1); exec_min = [Math]::Round($ctx.ExecMinutes, 1)
    gate_min = [Math]::Round($ctx.GateMinutes, 1); review_min = [Math]::Round($ctx.ReviewMinutes, 1)
    exec_tokens_in = [long]$ctx.ExecIn; exec_tokens_out = [long]$ctx.ExecOut; exec_tokens_cache = [long]$ctx.ExecCache
    opus_tokens = [long]$ctx.OpusTokens
    opus_5h_before = $(if ($lb) { $lb.five_hour.utilization }); opus_5h_after = $(if ($la) { $la.five_hour.utilization })
    opus_week_before = $(if ($lb) { $lb.seven_day.utilization }); opus_week_after = $(if ($la) { $la.seven_day.utilization })
    verdict = $Verdict
  } | Export-Csv -Path $csvFile -Append -NoTypeInformation -Encoding UTF8
}

# Stops the ticket as blocked: comment on the issue, keep the workspace state as a patch, clean the main tree,
# and stop the run with a non-zero exit code.
function Stop-Ticket($ctx, [string]$Reason, [switch]$NoComment) {
  Write-Host "`nTicket $($ctx.T.Label) is BLOCKED: $Reason" -ForegroundColor Red
  $patch = $null
  if (Test-Path (Join-Path $ExecDir '.git')) {
    try { $patch = New-WorkspacePatch $ctx (Join-Path $ctx.Logs ('blocked-{0:yyyyMMdd-HHmmss}.patch' -f (Get-Date))) -All } catch { }
  }
  try { Reset-MainTree $ctx -IncludeLocked; Remove-Item $currentFile -Force -ErrorAction SilentlyContinue }
  catch { Write-Host "  Could not clean the main tree: $($_.Exception.Message)" -ForegroundColor Red }
  if (-not $NoComment) {
    $tried = if ($ctx.Tried.Count) { $ctx.Tried -join ', ' } else { 'none (stopped before execution)' }
    $gate = if ($ctx.LastGateSummary) { "`n`nLast gate summary:`n``````n$($ctx.LastGateSummary)`n``````" } else { '' }
    $body = @"
Ticket runner v2 stopped this ticket as blocked; the issue stays open.

**Reason:** $Reason

- Executors tried: $tried (fix rounds: $($ctx.FixRounds), amends: $($ctx.Amends), rejects: $($ctx.Rejects))
- Logs: ``.ticket-logs/$($ctx.T.Label)/`` on the runner machine$(if ($patch) { "; the executor workspace state is saved as ``$((Split-Path -Leaf $patch))`` there" })$gate
"@
    $f = Join-Path $ctx.Logs 'blocked-comment.md'
    Write-Utf8 $f $body
    gh issue comment $ctx.T.Issue -R $Repo --body-file $f | Out-Null
  }
  Add-CsvRow $ctx 'blocked'
  Write-Host "Logs: $($ctx.Logs)" -ForegroundColor Red
  exit 1
}

# ---------------------------------------------------------------------------------------------------
# 4 / 5. Review and commit

function Install-Patch($ctx) {
  $patch = New-WorkspacePatch $ctx (Join-Path $ctx.Logs ('result-{0}.patch' -f $ctx.GateRuns))
  if ($patch) {
    git -C $root apply --check --binary $patch
    if ($LASTEXITCODE -ne 0) { Stop-Ticket $ctx "The executor's patch does not apply to the main tree (conflict with a baseline file?): $patch" }
    git -C $root apply --binary $patch
    if ($LASTEXITCODE -ne 0) { Stop-Ticket $ctx "git apply failed for $patch" }
  }
  Copy-WorkBack $ctx
}

function Invoke-Review($ctx) {
  $started = Get-Date
  Write-Phase "  [review] Opus reviewer" 'DarkCyan'
  $run = Invoke-OpusInPlanSession $ctx (Join-Path $ctx.Logs ('review-{0}.jsonl' -f ($ctx.Rejects + $ctx.Amends + 1))) (Get-ReviewPrompt $ctx)
  $ctx.ReviewMinutes += ((Get-Date) - $started).TotalMinutes
  $last = Get-LastLine $run.Final
  $verdict = switch -Regex ($last) {
    '^APPROVE$' { 'APPROVE'; break }
    '^FIX$' { 'FIX'; break }
    '^REJECT-PLAN:' { 'REJECT-PLAN'; break }
    '^REJECT:' { 'REJECT'; break }
    default { 'UNKNOWN' }
  }
  Write-Host "  [review] verdict: $last" -ForegroundColor $(if ($verdict -in 'APPROVE', 'FIX') { 'Green' } else { 'Yellow' })
  [pscustomobject]@{ Verdict = $verdict; Final = $run.Final; Last = $last }
}

function Test-Committed($ctx, $review) {
  $state = gh issue view $ctx.T.Issue -R $Repo --json state --jq '.state'
  $known = @{}; foreach ($p in $ctx.Baseline) { $known[$p.ToLowerInvariant()] = $true }
  $dirty = @(Get-ChangedPaths | Where-Object { -not $known.ContainsKey($_.ToLowerInvariant()) })
  git -C $root fetch -q origin
  $unpushed = [int](git -C $root rev-list --count origin/main..main)
  if ($state -ne 'CLOSED' -or $dirty.Count -or $unpushed -gt 0) {
    Write-Host "Ticket $($ctx.T.Label) did not finish cleanly (issue: $state, changes beyond the baseline: $($dirty -join ', '), unpushed commits: $unpushed)." -ForegroundColor Red
    Write-Host "Reviewer's last message:`n$($review.Final)" -ForegroundColor Red
    Write-Host "Logs: $($ctx.Logs)"
    Add-CsvRow $ctx "unclean-$($review.Verdict)"
    exit 1
  }
}

# ---------------------------------------------------------------------------------------------------
# One ticket

function Invoke-Ticket($ctx) {
  $t = $ctx.T
  $ctx.Baseline = @(Get-ChangedPaths)
  Write-Utf8 $currentFile (@{ pid = $PID; label = $t.Label; issue = $t.Issue; title = $t.Title; started = (Get-Date).ToString('o')
      head = (git -C $root rev-parse HEAD).Trim(); baseline = @($ctx.Baseline); rotationBefore = $ctx.RotationBefore } | ConvertTo-Json)
  if (Test-Path $ctx.Work) { Remove-Item $ctx.Work -Recurse -Force }
  New-Item -ItemType Directory -Force $ctx.Work, $ctx.Logs | Out-Null

  # 1. Plan.
  $problem = Invoke-Plan $ctx
  if ($problem) { Stop-Ticket $ctx $problem -NoComment:($problem -match 'could not plan') }

  # 2-3. Execute with the fallback chain.
  $result = 'failed'
  foreach ($exec in $ctx.Chain) {
    if ($exhausted[$exec]) { continue }
    $ctx.Executor = $exec; $ctx.Attempts++; $ctx.Tried += $exec
    Write-Phase "  [execute] $exec (attempt $($ctx.Attempts))" 'DarkCyan'
    Reset-Workspace $ctx
    $result = Invoke-ExecutorPhase $ctx $exec (New-RoundMessage $ctx 'START.md' "Read .ticket-work/$($t.Label)/EXEC-PROMPT.md and follow it exactly.")
    if ($result -eq 'green') { break }
    Write-Host "  $exec failed: $((@($ctx.LastFailure -split "`n"))[0])" -ForegroundColor Yellow
  }
  if ($result -ne 'green') {
    if (-not @('Nemotron', 'Gemini', 'Codex' | Where-Object { -not $exhausted[$_] }).Count) {
      Stop-Ticket $ctx 'Every executor is out (quota or usage limits). Run again later.' -NoComment
    }
    Stop-Ticket $ctx "The fallback chain is used up. Last failure: $($ctx.LastFailure)"
  }

  # 4-5. Review; one reject round, one amend per ticket.
  while ($true) {
    Install-Patch $ctx
    $review = Invoke-Review $ctx
    switch ($review.Verdict) {
      { $_ -in 'APPROVE', 'FIX' } { Test-Committed $ctx $review; Add-CsvRow $ctx $review.Verdict; return }
      'REJECT' {
        if ($ctx.Rejects -ge 1) { Stop-Ticket $ctx "Rejected twice by the reviewer: $($review.Last)" }
        $ctx.Rejects++
        Reset-MainTree $ctx
        $body = @"
# Review notes for ticket $($t.Label)

The reviewer rejected your result. Your standing instructions are in .ticket-work/$($t.Label)/EXEC-PROMPT.md and
the plan in .ticket-work/$($t.Label)/PLAN.md; they still apply. Fix the problems below, update REPORT.md, and end
with TICKET_DONE or TICKET_BLOCKED: <reason>.

## Reviewer's notes

$($review.Final)
"@
        $r = Invoke-ExecutorPhase $ctx $ctx.Executor (New-RoundMessage $ctx 'REVIEW-NOTES.md' $body) -Continue
        if ($r -ne 'green') { Stop-Ticket $ctx "After the reviewer's rejection the executor did not get green: $($ctx.LastFailure)" }
      }
      'REJECT-PLAN' {
        Reset-MainTree $ctx
        $answer = Invoke-Amend $ctx "The reviewer judged the plan or the locked tests wrong:`n$($review.Final)"
        if ($null -eq $answer) { Stop-Ticket $ctx "REJECT-PLAN after the one amend: $($review.Last)" }
        $r = Invoke-ExecutorPhase $ctx $ctx.Executor (New-RoundMessage $ctx "AMEND-$($ctx.Amends).md" (Get-AmendRound $ctx $answer)) -Continue
        if ($r -ne 'green') { Stop-Ticket $ctx "After the plan amendment the executor did not get green: $($ctx.LastFailure)" }
      }
      default { Stop-Ticket $ctx "The reviewer ended without a verdict line. Last line: $($review.Last)" }
    }
  }
}

function New-TicketContext($t, $choice) {
  @{ T = $t; Turn = $choice.Turn; Chain = $choice.Chain; StageStart = $(if ($t.IsStageEnd) { Get-StageStart $t } else { '' })
    Work = (Join-Path $workRoot $t.Label); Logs = (Join-Path $logRoot $t.Label)
    Baseline = @(); Locked = @(); LockedHash = @{}; PlanSession = $null; Sessions = @{}; ExecHead = ''; RotationBefore = $null
    Executor = ''; Tried = @(); Attempts = 0; FixRounds = 0; Amends = 0; Rejects = 0; ExecRuns = 0; GateRuns = 0
    LastGateLog = ''; LastGateSummary = ''; LastFailure = ''
    PlanMinutes = 0.0; ExecMinutes = 0.0; GateMinutes = 0.0; ReviewMinutes = 0.0
    ExecIn = 0.0; ExecOut = 0.0; ExecCache = 0.0; OpusTokens = 0.0; LimitsBefore = $null; LimitsAfter = $null }
}

# ---------------------------------------------------------------------------------------------------
# Main loop

$branch = (git -C $root branch --show-current).Trim()
if ($branch -ne 'main') { throw "The main working tree is on '$branch'; the runner commits to main." }

$done = 0
$firstOfRun = $true
while ($true) {
  if ($Max -gt 0 -and $done -ge $Max) { Write-Phase "Reached -Max $Max."; break }

  $t = Get-NextTicket
  if ($null -eq $t) { Write-Phase 'All tickets are closed. Done.' 'Green'; break }
  if ($t -eq 'ALL_BLOCKED') { Write-Phase 'Open tickets remain, but all are blocked. Stopping.' 'Yellow'; exit 1 }

  $choice = Get-ExecutorChain $firstOfRun
  if (-not $choice.Chain.Count) { Write-Phase 'Every executor is out (quota or usage limits). Stopping.' 'Red'; exit 1 }
  $ctx = New-TicketContext $t $choice
  $difficulty = if ($t.Difficulty) { $t.Difficulty } else { 'unlabelled' }
  $runEffort = if ($t.IsStageEnd) { "$StageEffort, stage end + stage review from $($ctx.StageStart)" } else { $Effort }
  Write-Phase "`n=== $($t.Title)  (#$($t.Issue), $difficulty, effort: $runEffort, executors: $($choice.Chain -join ' -> '), turn: $($choice.Turn)) ==="

  if ($DryRun) {
    $ctx.Executor = $choice.Chain[0]; $ctx.Baseline = @(Get-ChangedPaths); $ctx.LastGateLog = ".ticket-logs/$($t.Label)/gate-N.log"
    Write-Phase "`n--- plan prompt ---" 'DarkCyan'; Write-Host (Get-PlanPrompt $ctx)
    Write-Phase "--- executor prompt (.ticket-work/$($t.Label)/EXEC-PROMPT.md in $ExecDir) ---" 'DarkCyan'; Write-Host (Get-ExecutorPrompt $ctx)
    Write-Phase '--- review prompt ---' 'DarkCyan'; Write-Host (Get-ReviewPrompt $ctx)
    Write-Phase 'Dry run: nothing was run.' 'Green'
    break
  }

  $ctx.RotationBefore = if (Test-Path $rotationFile) { (Get-Content $rotationFile -Raw).Trim() } else { $null }
  Save-Rotation $choice.Turn
  $firstOfRun = $false
  # A crash (Claude exit code, usage limit too far away, git failure) still leaves the main tree clean.
  try { Invoke-Ticket $ctx }
  catch { Stop-Ticket $ctx "The runner stopped on an error: $($_.Exception.Message) ($($_.InvocationInfo.PositionMessage))" -NoComment }
  Remove-Item $currentFile -Force -ErrorAction SilentlyContinue
  Write-Phase "Ticket $($t.Label) done (executor: $($ctx.Executor), fix rounds: $($ctx.FixRounds), amends: $($ctx.Amends), rejects: $($ctx.Rejects))." 'Green'
  $done++

  # Closing the milestone marks the stage as reviewed, so tickets added to it later skip the stage review.
  if ($t.IsStageEnd) {
    gh api -X PATCH "repos/$Repo/milestones/$($t.Stage.number)" -f state=closed | Out-Null
    if (-not $NoStagePause) {
      Write-Phase "`nStage '$($t.Stage.title)' is complete. Check it visually (see the closing comments on the issues)." 'Yellow'
      $answer = Read-Host 'Continue with the next stage? (Y/n)'
      if ($answer -match '^[nN]') { break }
    }
  }
}
