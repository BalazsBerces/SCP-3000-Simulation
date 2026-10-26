# Proposed ticket rules

Agents append here, one line each (`- <ticket>: <cause and fix>`), when a problem cost them real time and another ticket would likely hit it. The runner does not put this file in the prompt. A human reads it and moves the rules that hold up to [ticket-rules.md](ticket-rules.md), then deletes the line.

- 07z: `gh issue view` printed nothing in Bash; use `gh issue view <n> -R <owner/repo> --json title,body,state,comments > file` and read the file. A showcase or Landmark position depends on the Pillar layout (the game defaults to DiamondLarge), so ask the core with that layout before aiming `--focus=`.
- 08p: The Godot exe is one folder deeper than the rule's glob (...Mono_*\Godot_v4.7.2-stable_mono_win64\*_console.exe); use Get-ChildItem -Recurse -Filter *_console.exe.
- 08r: The benchmark is its own scene: run `Godot --path game res://Benchmark.tscn -- --benchmark-seconds=8`; `-- --benchmark` alone starts the normal game and never exits. Likewise find Godot with a recursive Get-ChildItem under the WinGet package folder, as the exe sits one folder deeper than the glob.
- 08s: `--gpu-profile` itself drops the benchmark 1% lows to 33–40 fps, so take the per-pass GPU breakdown from a separate run and judge fps only on runs without it.
- 09: In a PowerShell array, `@('--x', '--capture=' + $path)` passes `--capture=` and the path as two arguments (the comma binds before `+`), so Godot saves to an empty path; write `("--capture=$path")`.
- 09a: PowerShell 5.1 native-process pipes can replace non-ASCII source text with question marks. Use apply_patch or explicitly set UTF-8 stdin when generating source containing non-ASCII characters.
- 10: Named sandbox presets use `--villager-sandbox --sandbox-preset=<name>`; `--villager-sandbox=<name>` is ignored and launches the full Store. Check the parsed options in SimulationHost before an unattended capture.
