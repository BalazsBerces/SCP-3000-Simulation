# Proposed ticket rules

Agents append here, one line each (`- <ticket>: <cause and fix>`), when a problem cost them real time and another ticket would likely hit it. The runner does not put this file in the prompt. A human reads it and moves the rules that hold up to [ticket-rules.md](ticket-rules.md), then deletes the line.

- 07z: `gh issue view` printed nothing in Bash; use `gh issue view <n> -R <owner/repo> --json title,body,state,comments > file` and read the file. A showcase or Landmark position depends on the Pillar layout (the game defaults to DiamondLarge), so ask the core with that layout before aiming `--focus=`.
- 08p: The Godot exe is one folder deeper than the rule's glob (...Mono_*\Godot_v4.7.2-stable_mono_win64\*_console.exe); use Get-ChildItem -Recurse -Filter *_console.exe.
