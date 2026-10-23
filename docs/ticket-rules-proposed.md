# Proposed ticket rules

Agents append here, one line each (`- <ticket>: <cause and fix>`), when a problem cost them real time and another ticket would likely hit it. The runner does not put this file in the prompt. A human reads it and moves the rules that hold up to [ticket-rules.md](ticket-rules.md), then deletes the line.

- 07z: `gh issue view` printed nothing in Bash; use `gh issue view <n> -R <owner/repo> --json title,body,state,comments > file` and read the file. A showcase or Landmark position depends on the Pillar layout (the game defaults to DiamondLarge), so ask the core with that layout before aiming `--focus=`.
