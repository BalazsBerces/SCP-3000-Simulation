# Fake codex exec --json: reads the prompt from stdin and, as scenario.codex says (done | quota | blocked), plays a
# ticket that is implemented, committed, pushed and closed, a usage-limit failure, or a blocker.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stub-lib.ps1')
$a = @($env:CODEX_STUB_ARGS -split '\s+' | Where-Object { $_ })
$prompt = [Console]::In.ReadToEnd()
$effort = if ((Get-ArgAfter $a '--config') -match 'model_reasoning_effort=(\w+)') { $Matches[1] } else { '' }
Write-Call ("codex model=$(Get-ArgAfter $a '--model') effort=$effort prompt=" + (($prompt -split "`n")[0]))
Set-Content -Path (Join-Path $StubState ('codex-prompt-{0}.txt' -f (Get-NextIndex 'codex-prompt'))) -Value $prompt

function Emit($o) { $o | ConvertTo-Json -Compress -Depth 10 }
Emit @{ type = 'thread.started'; thread_id = 'codex-thread-1' }
Emit @{ type = 'item.started'; item = @{ id = 'i1'; type = 'command_execution'; command = 'gh issue view 67 --comments'; status = 'in_progress' } }

$step = Get-Step 'codex' 'done'
if ($step -eq 'quota') {
  Emit @{ type = 'turn.failed'; error = @{ message = "You've hit your usage limit. Try again later." } }
  exit 1
}
if ($step -eq 'done') {
  $label = if ($prompt -match 'implement ticket (\S+)') { $Matches[1] } else { 'x' }
  $issue = if ($prompt -match 'GitHub issue #(\d+)') { $Matches[1] } else { '0' }
  Set-Content -Path (Join-Path (Get-Location) 'src\impl.txt') -Value "good $label codex"
  Emit @{ type = 'item.completed'; item = @{ id = 'i2'; type = 'file_change'; changes = @(@{ path = (Join-Path (Get-Location) 'src\impl.txt'); kind = 'update' }); status = 'completed' } }
  $title = if ($prompt -match 'message "([^"]+)"') { $Matches[1] } else { 'untitled' }
  $co = if ($prompt -match '"(Co-Authored-By: [^"]+)"') { $Matches[1] } else { '' }
  $msgFile = Join-Path $StubState 'commit-msg.txt'
  Set-Content -Path $msgFile -Value "$title`n`nCloses #$issue`n`n$co"
  git add -A -- . ':!.claude'
  git commit -q -F $msgFile
  git push -q origin main
  & (Join-Path $PSScriptRoot 'gh.ps1') issue close $issue | Out-Null
  $final = "Implemented.`nTICKET_DONE"
} else { $final = "Cannot do it.`nTICKET_BLOCKED: stub blocker" }
Emit @{ type = 'item.completed'; item = @{ id = 'i3'; type = 'agent_message'; text = $final } }
Emit @{ type = 'turn.completed'; usage = @{ input_tokens = 9000; cached_input_tokens = 7000; output_tokens = 300 } }
exit 0
