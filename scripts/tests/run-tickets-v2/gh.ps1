# Fake gh: serves issues, milestones, blockers, comments and closing from $env:STUB_STATE\gh.json.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stub-lib.ps1')
$a = @($args | ForEach-Object { "$_" })
Write-Call ("gh " + ($a -join ' '))
$st = Get-GhState

function Out-Json($o) { ConvertTo-Json -InputObject @($o) -Depth 10 -Compress }

if ($a[0] -eq 'issue' -and $a[1] -eq 'list') {
  $ms = Get-ArgAfter $a '--milestone'
  if ($ms) {
    $num = @($st.milestones | Where-Object { $_.title -eq $ms })[0].number
    Out-Json @($st.issues | Where-Object { $_.milestone -eq $num } | ForEach-Object { @{ title = $_.title } })
  } else {
    Out-Json @($st.issues | Where-Object { $_.state -eq 'OPEN' -and @($_.labels) -contains 'ready-for-agent' } | ForEach-Object {
      $m = if ($_.milestone) { $n = $_.milestone; @{ number = $n; title = @($st.milestones | Where-Object { $_.number -eq $n })[0].title } } else { $null }
      @{ number = $_.number; title = $_.title; labels = @($_.labels | ForEach-Object { @{ name = $_ } }); milestone = $m }
    })
  }
  exit 0
}
if ($a[0] -eq 'api') {
  $path = if ($a[1] -eq '-X') { $a[3] } else { $a[1] }
  if ($path -match 'milestones\?') {
    Out-Json @($st.milestones | ForEach-Object {
      $n = $_.number
      @{ number = $n; title = $_.title; state = $_.state; open_issues = @($st.issues | Where-Object { $_.milestone -eq $n -and $_.state -eq 'OPEN' }).Count }
    })
  } elseif ($path -match 'issues/(\d+)/dependencies/blocked_by') {
    $n = [int]$Matches[1]
    $issue = @($st.issues | Where-Object { $_.number -eq $n })[0]
    Out-Json @(@($issue.blockedBy) | Where-Object { $_ } | ForEach-Object { $b = $_; @{ number = $b; state = $(if (@($st.issues | Where-Object { $_.number -eq $b })[0].state -eq 'OPEN') { 'open' } else { 'closed' }) } })
  } elseif ($path -match 'milestones/(\d+)$') {
    $n = [int]$Matches[1]
    foreach ($m in $st.milestones) { if ($m.number -eq $n) { $m.state = 'closed' } }
    Save-GhState $st
    '{}'
  }
  exit 0
}
if ($a[0] -eq 'issue' -and $a[1] -eq 'view') {
  @($st.issues | Where-Object { $_.number -eq [int]$a[2] })[0].state
  exit 0
}
if ($a[0] -eq 'issue' -and $a[1] -eq 'comment') {
  # Not Get-Content: its provider NoteProperties make PS 5.1's ConvertTo-Json walk the whole provider graph.
  $body = [IO.File]::ReadAllText((Get-ArgAfter $a '--body-file'))
  $st.comments = @($st.comments) + @(@{ number = [int]$a[2]; body = $body })
  Save-GhState $st
  exit 0
}
if ($a[0] -eq 'issue' -and $a[1] -eq 'close') {
  foreach ($i in $st.issues) { if ($i.number -eq [int]$a[2]) { $i.state = 'CLOSED' } }
  Save-GhState $st
  exit 0
}
Write-Error "fake gh: unsupported call: $($a -join ' ')"
exit 1
