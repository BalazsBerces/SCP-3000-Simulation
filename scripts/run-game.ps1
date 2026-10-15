<#
.SYNOPSIS
  Builds the C# code and starts the simulation (or the Godot editor).

.EXAMPLE
  .\scripts\run-game.ps1                    # build, then start the game
  .\scripts\run-game.ps1 -Editor            # build, then open the project in the Godot editor
  .\scripts\run-game.ps1 -NoBuild           # start without rebuilding
  .\scripts\run-game.ps1 -- --pillars=2x2   # pass dev flags to the game
#>
param(
  [switch]$Editor,
  [switch]$NoBuild,
  [Parameter(ValueFromRemainingArguments = $true)] [string[]]$GameArgs
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$game = Join-Path $root 'game'

# Godot 4.7.2 .NET is installed by winget but not on PATH.
$dir = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*\Godot_v4.7.2-stable_mono_win64" -Directory |
  Select-Object -First 1
if (-not $dir) { throw 'Godot 4.7.2 .NET not found under %LOCALAPPDATA%\Microsoft\WinGet\Packages.' }
$gui = Join-Path $dir.FullName 'Godot_v4.7.2-stable_mono_win64.exe'
$console = Join-Path $dir.FullName 'Godot_v4.7.2-stable_mono_win64_console.exe'

if (-not $NoBuild) {
  # The first build after new assets also imports them, which can take a few minutes.
  $log = Join-Path $env:TEMP 'scp3008-build.log'
  # No lingering build servers: the C# compiler server or a reused MSBuild node would inherit the
  # redirected log handles, and the Godot console wrapper would then wait for them forever.
  $env:UseSharedCompilation = 'false'
  $env:MSBUILDDISABLENODEREUSE = '1'
  $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
  Write-Host "Building... (log: $log)" -ForegroundColor Cyan
  $build = Start-Process -FilePath $console -ArgumentList '--headless', '--path', "`"$game`"", '--build-solutions', '--quit' `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
  $null = $build.Handle  # keeps the exit code readable after the process ends
  $build.WaitForExit()
  if ($build.ExitCode -ne 0) { throw "Build failed (exit $($build.ExitCode)). See $log and $log.err." }
}

$cliArgs = @('--path', $game)
if ($Editor) { $cliArgs += '-e' }
elseif ($GameArgs) { $cliArgs += @('--') + ($GameArgs | Where-Object { $_ -ne '--' }) }
Start-Process -FilePath $gui -ArgumentList $cliArgs
Write-Host $(if ($Editor) { 'Godot editor started.' } else { 'Game started. F4 toggles the Store overview.' }) -ForegroundColor Green
