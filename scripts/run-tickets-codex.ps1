<#
.SYNOPSIS
  Works through the ready-for-agent tickets one by one, each in a fresh headless Codex session
  (the equivalent of /clear between tickets).

.DESCRIPTION
  Loop:
    1. Pick the lowest-numbered open ticket whose "blocked by" issues are all closed.
    2. Run `codex exec` on it with full permissions. The agent implements it, runs the tests,
       commits to main, pushes, and closes the issue.
    3. Check that the issue is closed and the working tree is clean and pushed. If not, stop.
    4. At the end of each build stage, pause for a manual visual review (unless -NoStagePause).

  Logs are written to .ticket-logs/ (git-excluded locally).

.EXAMPLE
  .\scripts\run-tickets-codex.ps1                 # run until done, pausing at stage boundaries
  .\scripts\run-tickets-codex.ps1 -Max 1          # run exactly one ticket
  .\scripts\run-tickets-codex.ps1 -NoStagePause   # never pause, stop only on failure
  .\scripts\run-tickets-codex.ps1 -Effort high -StageEffort high # more effort, including stage reviews
#>
param(
  [string]$Repo = 'BalazsBerces/SCP-3000-Simulation',
  [int]$Max = 0,
  [switch]$NoStagePause,
  [string]$Model = 'gpt-6.1-sol',
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$Effort = 'medium',
  # Stage-end tickets also review the whole stage; effort can be overridden separately.
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')] [string]$StageEffort = 'medium'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$OutputEncoding = [Text.Encoding]::UTF8

# Last ticket number of each build stage, including inserted suffix tickets (e.g. 05b).
$StageEnds = @{ 5 = 'Atmosphere'; 8 = 'Landmarks'; 16 = 'Villagers'; 22 = 'Fortifications'; 28 = 'Staff'; 33 = 'Relations'; 36 = 'Observer tools and Knowledge' }

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$logDir = Join-Path $root '.ticket-logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$exclude = Join-Path $root '.git\info\exclude'
if (-not (Select-String -Path $exclude -Pattern '^\.ticket-logs/' -Quiet)) { Add-Content $exclude '.ticket-logs/' }

function Get-NextTicket {
  $open = gh issue list -R $Repo --state open --label ready-for-agent --limit 200 --json number,title | ConvertFrom-Json
  # Titles start with 'NN: ', or 'NNa: ' for a ticket inserted after NN (it runs after NN, before NN+1).
  $tickets = $open | ForEach-Object { $_ } | Where-Object { $_.title -match '^(\d\d)([a-z]?): ' } |
    ForEach-Object {
      $null = $_.title -match '^(\d\d)([a-z]?): '
      $n = [int]$Matches[1]; $suffix = $Matches[2]
      [pscustomobject]@{ Issue = $_.number; Ticket = $n; Label = $Matches[1] + $suffix; Title = $_.title
        IsStageEnd = $false }
    } |
    Sort-Object Ticket, Label
  if (-not $tickets) { return $null }
  # A stage ends after its last remaining suffix ticket, not before inserted work.
  foreach ($stageEnd in $StageEnds.Keys) {
    $lastStageTicket = $tickets | Where-Object { $_.Ticket -eq $stageEnd } | Select-Object -Last 1
    if ($lastStageTicket) { $lastStageTicket.IsStageEnd = $true }
  }
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
  $first = 1 + [int](@($StageEnds.Keys | Where-Object { $_ -lt $t.Ticket }) + 0 | Measure-Object -Maximum).Maximum
  $firstTitle = $first.ToString('D2')
  "6. This ticket completes the build stage '$($StageEnds[$t.Ticket])' (tickets $first-$($t.Ticket)). Instead of reviewing only this ticket, run /spec-review on the whole stage: the diff from the parent of ticket $first's commit (find it with git log --grep '^${firstTitle}: ') up to and including your uncommitted changes. Fix the real findings, rerun the tests, and include the fixes in this ticket's commit."
}

function Get-Prompt($t) {
@"
Use the implement skill to implement ticket $($t.Label) of the SCP-3008 simulation: GitHub issue #$($t.Issue) in $Repo.
Read the implement skill's SKILL.md under your Codex skills directory (CODEX_HOME/skills, or ~/.codex/skills by default). References to /tdd and /spec-review mean read and follow those skills; references to the Claude Skill tool mean read the named skill's SKILL.md.
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
   - commit to main with the message "$($t.Title)", a blank line, "Closes #$($t.Issue)", a blank line, and "Co-Authored-By: Codex <noreply@openai.com>";
   - push to origin main;
   - make sure issue #$($t.Issue) is closed;
   - post a short closing comment: what was built, the test results, and what needs human visual review.
8. If you cannot finish (an unresolvable ambiguity, or a failure you cannot fix), do NOT commit broken code and do NOT close the issue. Comment on issue #$($t.Issue) explaining the blocker and what is done so far.
Never modify issue #1 or other tickets, never force-push, and never rewrite history.
End your final message with exactly one line: TICKET_DONE or TICKET_BLOCKED: <reason>.
"@
}

function Invoke-Agent($t, $log) {
  $cliArgs = @('exec', '--dangerously-bypass-approvals-and-sandbox', '--json')
  if ($Model) { $cliArgs += @('--model', $Model) }
  $runEffort = if ($t.IsStageEnd) { $StageEffort } else { $Effort }
  $cliArgs += @('--config', "model_reasoning_effort=$runEffort", '-')
  $final = ''
  $started = Get-Date
  # stdin preserves quotes and multiline prompts, including in PowerShell 5.1.
  Get-Prompt $t | & codex @cliArgs | ForEach-Object {
    $line = "$_"
    Add-Content -Path $log -Value $line -Encoding UTF8
    try { $ev = $line | ConvertFrom-Json } catch { return }
    if ($ev.type -eq 'item.completed' -and $ev.item.type -eq 'agent_message') {
      $final = $ev.item.text
      if ($final.Trim()) {
        $summary = ($final.Trim() -split '\r?\n')[0]
        Write-Host ("  > " + $summary) -ForegroundColor Gray
      }
    } elseif ($ev.type -eq 'item.started') {
      if ($ev.item.type -eq 'command_execution') {
        Write-Host '  - Shell' -ForegroundColor DarkGray
      } elseif ($ev.item.type -eq 'mcp_tool_call') {
        Write-Host "  - $($ev.item.server)/$($ev.item.tool)" -ForegroundColor DarkGray
      }
    } elseif ($ev.type -eq 'turn.completed') {
      Write-Host ("  {0:N0} min, tokens: {1:N1}M total, {2:N0}k cached input, {3:N0}k output" -f `
        ((Get-Date) - $started).TotalMinutes,
        (($ev.usage.input_tokens + $ev.usage.output_tokens) / 1e6),
        ($ev.usage.cached_input_tokens / 1e3), ($ev.usage.output_tokens / 1e3)) -ForegroundColor DarkCyan
    } elseif ($ev.type -eq 'turn.failed' -or $ev.type -eq 'error') {
      Write-Host "  Codex error: $line" -ForegroundColor Red
    }
  }
  if ($LASTEXITCODE -ne 0) { throw "Codex exited with code $LASTEXITCODE. Log: $log" }
  return $final
}

$done = 0
while ($true) {
  if ($Max -gt 0 -and $done -ge $Max) { Write-Host "Reached -Max $Max." -ForegroundColor Cyan; break }

  $t = Get-NextTicket
  if ($null -eq $t) { Write-Host 'All tickets are closed. Done.' -ForegroundColor Green; break }
  if ($t -eq 'ALL_BLOCKED') { Write-Host 'Open tickets remain, but all are blocked. Stopping.' -ForegroundColor Yellow; exit 1 }

  $log = Join-Path $logDir ('ticket-{0}-{1:yyyyMMdd-HHmmss}.jsonl' -f $t.Label, (Get-Date))
  $runEffort = if ($t.IsStageEnd) { "$StageEffort, stage end + review" } else { $Effort }
  Write-Host "`n=== $($t.Title)  (#$($t.Issue), effort: $runEffort) ===" -ForegroundColor Cyan
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

  if (-not $NoStagePause -and $t.IsStageEnd) {
    Write-Host "`nStage '$($StageEnds[$t.Ticket])' is complete. Check it visually (see the closing comments on the issues)." -ForegroundColor Yellow
    $answer = Read-Host 'Continue with the next stage? (Y/n)'
    if ($answer -match '^[nN]') { break }
  }
}
