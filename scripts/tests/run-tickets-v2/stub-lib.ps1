# Shared helpers of the fake CLIs. State lives in $env:STUB_STATE: scenario.json (what each fake does),
# gh.json (issues and milestones), calls.log (every call, for the assertions) and *.idx counters.
$StubState = $env:STUB_STATE
if (-not $StubState) { throw 'STUB_STATE is not set.' }

function Get-Scenario { Get-Content (Join-Path $StubState 'scenario.json') -Raw | ConvertFrom-Json }

function Write-Call([string]$Text) { Add-Content -Path (Join-Path $StubState 'calls.log') -Value $Text }

function Get-NextIndex([string]$Name) {
  $f = Join-Path $StubState "$Name.idx"
  $i = if (Test-Path $f) { [int](Get-Content $f) } else { 0 }
  Set-Content -Path $f -Value ($i + 1)
  $i
}

# The scenario's list entry for this call, or $Default when the list is used up.
function Get-Step([string]$List, [string]$Default) {
  $sc = Get-Scenario
  $items = @($sc.$List)
  $i = Get-NextIndex $List
  if ($i -lt $items.Count -and $items[$i]) { "$($items[$i])" } else { $Default }
}

function Get-GhState { Get-Content (Join-Path $StubState 'gh.json') -Raw | ConvertFrom-Json }
function Save-GhState($st) { Set-Content -Path (Join-Path $StubState 'gh.json') -Value ($st | ConvertTo-Json -Depth 10) }

function Get-ArgAfter([object[]]$A, [string]$Name) {
  for ($i = 0; $i -lt $A.Count - 1; $i++) { if ($A[$i] -eq $Name) { return "$($A[$i + 1])" } }
  $null
}

$LockedTest = 'tests/Scp3008.Core.Tests/AcceptanceTests.cs'
