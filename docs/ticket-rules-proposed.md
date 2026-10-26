# Proposed ticket rules

Agents append here, one line each (`- <ticket>: <cause and fix>`), when a problem cost them real time and another ticket would likely hit it. The runner does not put this file in the prompt. A human reads it and moves the rules that hold up to [ticket-rules.md](ticket-rules.md), then deletes the line.

- 07z: `gh issue view` printed nothing in Bash; use `gh issue view <n> -R <owner/repo> --json title,body,state,comments > file` and read the file. A showcase or Landmark position depends on the Pillar layout (the game defaults to DiamondLarge), so ask the core with that layout before aiming `--focus=`.
- 08p: The Godot exe is one folder deeper than the rule's glob (...Mono_*\Godot_v4.7.2-stable_mono_win64\*_console.exe); use Get-ChildItem -Recurse -Filter *_console.exe.
- 08r: The benchmark is its own scene: run `Godot --path game res://Benchmark.tscn -- --benchmark-seconds=8`; `-- --benchmark` alone starts the normal game and never exits. Likewise find Godot with a recursive Get-ChildItem under the WinGet package folder, as the exe sits one folder deeper than the glob.
- 08s: `--gpu-profile` itself drops the benchmark 1% lows to 33–40 fps, so take the per-pass GPU breakdown from a separate run and judge fps only on runs without it.
- 09: In a PowerShell array, `@('--x', '--capture=' + $path)` passes `--capture=` and the path as two arguments (the comma binds before `+`), so Godot saves to an empty path; write `("--capture=$path")`.
- 09a: PowerShell 5.1 native-process pipes can replace non-ASCII source text with question marks. Use apply_patch or explicitly set UTF-8 stdin when generating source containing non-ASCII characters.
- 10: Named sandbox presets use `--villager-sandbox --sandbox-preset=<name>`; `--villager-sandbox=<name>` is ignored and launches the full Store. Check the parsed options in SimulationHost before an unattended capture.
- 11: DevCapture can run before CameraRig._Ready resets the camera; wait for a drawn frame before positioning a scripted capture. Compare scene pixels outside the HUD so clock and counter changes cannot make a rendered check pass.
- 11a: Windows PowerShell can lose a short-lived Start-Process process's exit code before WaitForExit reads it. Retain its Handle immediately after starting it, then wait with a timeout and refresh before checking ExitCode.

- 11b: Godot FindChildren filters on engine class names and does not match a C# script class such as SectorInspector. Leave the type filter empty and use OfType<T>() for scripted nodes; format numeric capture assertions with an explicit culture.

- 12: When introducing autonomous populations, run a full-Store time-advance smoke check before rendering benchmarks. Sight and site scoring must query nearby Chunks rather than scan every Furniture piece for every person.
- 13: Godot changes the working directory to its project when using `--path`, so relative capture/check output paths land inside `game`. Pass absolute paths for evidence under the workspace's `.ticket-logs` directory.
- 14: Removing Stock Furniture does not create a lasting shortage because Restock can return it with Goods. In death/shortage scenarios, disable Restock in WorldConfig or occupy the vacated footprint before advancing time.

- 14a: Godot `FindChildren` type filters match native Godot classes, not C# script class names. Find scripted nodes by node name and native type, then cast.
- 14a: Restoring a source file with its old timestamp after a baseline comparison can leave MSBuild using the baseline binary. Force a rebuild or update the restored source timestamp before checking the implemented version.

- 15: SeedPanel defers its initial sandbox camera move, which can overwrite a rendered check's camera position. Await an initial frame before positioning the camera for a sandbox capture.
- 15: Rendering benchmarks must set the Store clock through Scenario.SetTimeOfDay rather than AdvanceTo. Advancing the full Store between lighting passes runs hours of Villager behaviour and can exhaust the Godot run timeout before rendering is measured.
- 15a: Windows PowerShell Start-Process can leave ExitCode unset even after waiting for a fast Godot process. Use a runner that reliably returns the exit status, such as Python subprocess.run with a 300-second timeout, and keep stdout/stderr in ticket logs.
