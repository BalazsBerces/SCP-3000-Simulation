# Fake process-launched tools: opencode / agy / codex (executors), dotnet (build, test) and godot.
# Usage (from the .cmd shims): stub-exec.ps1 <tool> <arguments...>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stub-lib.ps1')
$tool = "$($args[0])"
$rest = @($args | Select-Object -Skip 1 | ForEach-Object { "$_" })
$sc = Get-Scenario
$lockedPath = Join-Path (Get-Location) ($LockedTest -replace '/', '\')
$implPath = Join-Path (Get-Location) 'src\impl.txt'
$impl = if (Test-Path $implPath) { (Get-Content $implPath -Raw).Trim() } else { '' }

if ($tool -eq 'godot') { Write-Call "godot $($rest -join ' ')"; exit 0 }

if ($tool -eq 'dotnet') {
  Write-Call "dotnet $($rest[0])"
  $lockedText = if (Test-Path $lockedPath) { Get-Content $lockedPath -Raw } else { '' }
  if ($rest[0] -eq 'build') {
    if ($impl -like 'compile-error*') { "$(Get-Location)\src\Impl.cs(3,5): error CS0103: The name 'x' does not exist [$(Get-Location)\src\Core.csproj]"; exit 1 }
    'Build succeeded.'; exit 0
  }
  if ($sc.testHang) { Start-Sleep -Seconds 300 }
  $failures = @()
  if (-not $impl.StartsWith('good')) { $failures += @{ cls = 'Scp3008.Core.Tests.ExecutorTests'; name = 'Works' } }
  if ($lockedText -match 'WRONG') { $failures += @{ cls = 'Scp3008.Core.Tests.AcceptanceTests'; name = 'Criterion' } }
  $dir = Get-ArgAfter $rest '--results-directory'
  $file = (Get-ArgAfter $rest '--logger') -replace '^trx;LogFileName=', ''
  New-Item -ItemType Directory -Force $dir | Out-Null
  $i = 0
  $results = ($failures | ForEach-Object { $i++; "<UnitTestResult testId=`"id$i`" testName=`"$($_.cls).$($_.name)`" outcome=`"Failed`"><Output><ErrorInfo><Message>Assert.Equal() Failure: expected good</Message></ErrorInfo></Output></UnitTestResult>" }) -join ''
  $i = 0
  $defs = ($failures | ForEach-Object { $i++; "<UnitTest name=`"$($_.name)`" id=`"id$i`"><TestMethod className=`"$($_.cls)`" name=`"$($_.name)`" /></UnitTest>" }) -join ''
  Set-Content -Path (Join-Path $dir $file) -Value "<?xml version=`"1.0`" encoding=`"utf-8`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Results>$results</Results><TestDefinitions>$defs</TestDefinitions></TestRun>"
  "Failed: $($failures.Count)"
  exit $(if ($failures.Count) { 1 } else { 0 })
}

# Executors.
$name = @{ opencode = 'Nemotron'; agy = 'Gemini'; codex = 'Codex' }[$tool]
switch ($tool) {
  'opencode' { $msg = $rest[-1]; $session = Get-ArgAfter $rest '--session' }
  'agy' { $msg = Get-ArgAfter $rest '-p'; $session = Get-ArgAfter $rest '--conversation' }
  'codex' { $msg = $rest[-1]; $session = if ($rest[1] -eq 'resume') { $rest[-2] } else { $null } }
}
$outcome = Get-Step 'exec' 'done'
Write-Call "exec $name session=$session outcome=$outcome msg=$msg"
$sid = if ($session) { $session } else { "$tool-ses-$(Get-NextIndex 'sid')" }
# v2 works from .ticket-work/<label>; v3 has none and names the ticket in its first prompt only.
$workRoot = Join-Path (Get-Location) '.ticket-work'
$labelFile = Join-Path $StubState 'exec-label.txt'
if (Test-Path $workRoot) { $label = (Get-ChildItem $workRoot -Directory | Select-Object -First 1).Name }
elseif ($msg -match '^/implement ticket (\S+)') { $label = $Matches[1]; Set-Content -Path $labelFile -Value $label }
else { $label = (Get-Content $labelFile -ErrorAction SilentlyContinue | Select-Object -First 1) }
$work = Join-Path $workRoot $label
$v3 = -not (Test-Path $work)

function Emit($o) { [Console]::Out.WriteLine(($o | ConvertTo-Json -Compress -Depth 10)) }
function Emit-Start {
  switch ($tool) {
    'opencode' { Emit @{ type = 'step_start'; sessionID = $sid; part = @{ type = 'step-start' } } }
    'agy' { Emit @{ event = 'init'; conversation_id = $sid; init = @{ model = 'fake' } } }
    'codex' { Emit @{ type = 'thread.started'; thread_id = $sid } }
  }
}
function Emit-Final([string]$Text) {
  switch ($tool) {
    'opencode' {
      Emit @{ type = 'text'; sessionID = $sid; part = @{ type = 'text'; text = $Text } }
      Emit @{ type = 'step_finish'; sessionID = $sid; part = @{ type = 'step-finish'; tokens = @{ input = 100; output = 10; reasoning = 5; cache = @{ read = 1000; write = 0 } } } }
    }
    'agy' { Emit @{ event = 'result'; result = @{ conversation_id = $sid; status = 'SUCCESS'; response = $Text; usage = @{ input_tokens = 100; output_tokens = 10; thinking_tokens = 5; cache_read_tokens = 1000 } } } }
    'codex' {
      Emit @{ type = 'item.completed'; item = @{ type = 'agent_message'; text = $Text } }
      Emit @{ type = 'turn.completed'; usage = @{ input_tokens = 1100; cached_input_tokens = 1000; output_tokens = 10 } }
    }
  }
}
function Emit-Error([string]$Message) {
  switch ($tool) {
    'opencode' { Emit @{ type = 'error'; sessionID = $sid; error = @{ type = 'provider.invalid-request'; message = $Message; status = 400 } } }
    'agy' { Emit @{ event = 'result'; result = @{ conversation_id = $sid; status = 'ERROR'; response = ''; error = $Message } } }
    'codex' { Emit @{ type = 'turn.failed'; error = @{ message = $Message } } }
  }
}
function Write-Work([string]$Impl, [string]$Last, [switch]$NoCapture) {
  New-Item -ItemType Directory -Force (Split-Path -Parent $implPath) | Out-Null
  # The label makes every ticket's change differ, so consecutive tickets each have something to commit.
  Set-Content -Path $implPath -Value "$Impl $label"
  if ($v3) {
    # v3: the closing comment the runner posts after its checks.
    $logs = Join-Path (Get-Location) '.ticket-logs'
    New-Item -ItemType Directory -Force $logs | Out-Null
    Set-Content -Path (Join-Path $logs "$label-closing.md") -Value "Stub closing comment for $label"
    return
  }
  Set-Content -Path (Join-Path $work 'REPORT.md') -Value "# Report`nChanges: src/impl.txt`n$Last"
  if (-not $NoCapture) {
    Get-Content (Join-Path $work 'captures.txt') | Where-Object { $_.Trim() } | ForEach-Object {
      Set-Content -Path (Join-Path (Get-Location) ($_.Trim() -replace '/', '\')) -Value 'png'
    }
  }
}

Emit-Start
switch ($outcome) {
  'done' { Write-Work 'good' 'TICKET_DONE'; Emit-Final "All done.`nTICKET_DONE" }
  'done-exit1' { Write-Work 'good' 'TICKET_DONE'; Emit-Error 'OpenAI Chat stream ended without finish_reason'; Emit-Final "All done.`nTICKET_DONE"; exit 1 }
  'done-nocapture' { Write-Work 'good' 'TICKET_DONE' -NoCapture; Emit-Final "All done.`nTICKET_DONE" }
  'red' { Write-Work 'bad' 'TICKET_DONE'; Emit-Final "Done, I think.`nTICKET_DONE" }
  'compile' { Write-Work 'compile-error' 'TICKET_DONE'; Emit-Final "Done.`nTICKET_DONE" }
  'nomarker' { Write-Work 'good' 'done'; Emit-Final 'Finished the work.' }
  'blocked' { Emit-Final "I cannot do this.`nTICKET_BLOCKED: too hard" }
  'provider' { Emit-Error 'Upstream request failed: [400] Provider returned error'; exit 1 }
  'quota' { Emit-Error 'You exceeded your current quota, please check your plan and billing details'; exit 1 }
  'ratelimit' { Emit-Error '429 Too Many Requests: rate limit exceeded'; exit 1 }
  'suspect' {
    $line = "LOCKED_TEST_SUSPECT: $($LockedTest):Criterion - it expects a value the plan does not produce"
    Set-Content -Path (Join-Path $work 'REPORT.md') -Value "# Report`n$line`nTICKET_BLOCKED: suspect locked test"
    Emit-Final "$line`nTICKET_BLOCKED: suspect locked test"
  }
  'timeout' { Start-Sleep -Seconds 300 }
  'commit' { Write-Work 'good' 'TICKET_DONE'; git add -A; git commit -q -m 'executor commit'; Emit-Final "Committed.`nTICKET_DONE" }
  'touchlocked' { Write-Work 'good' 'TICKET_DONE'; Add-Content -Path $lockedPath -Value '// executor edit'; Emit-Final "Done.`nTICKET_DONE" }
  default { throw "unknown outcome $outcome" }
}
exit 0
