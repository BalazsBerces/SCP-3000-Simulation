# Ticket 11a verification

Issue: #71. Spec: #1. Review fixed point: `3e0a139ce671584ae3a412621c148cf9006abc5c`.

## Implementation and decisions

- Each Villager owns a Known map in the read-only snapshot. Existing snapshots retain their observations when the simulation advances.
- Explored ground records visited 8 m patches. Observation runs with the existing needs decision interval (0.1 Store hour), using the existing 40 m wrapped sight, Furniture/Pillar occlusion and Fog bank rules. Sources within interaction reach can also be inspected directly.
- Scavenging chooses remembered Goods and Water points, including stale observations, rather than searching the Store's hidden resource state. With no suitable known source, a hungry or thirsty Villager scouts the nearest unexplored walkable ground.
- Observations become stale after one Store hour unseen. This deliberately uses elapsed time, not remote changes in the true stock: an emptied or restocked shelf retains its last observed amount until seen again. Empty shelves remain remembered, so stale sources can be revisited after Restock.
- Showroom taps are remembered as untested, running or dry. Only trying a tap reveals whether it runs. Nearby visible Staff at Closing time are remembered as dangerous places.
- The dev debug panel shows the followed Villager's Known map; V selects the next Villager. Its switch hides the details, and the list scrolls.
- Shared Band/Village knowledge and other Village observations await the corresponding entities/behaviour tickets. Save/load is conditional on #14 in this ticket and is not implemented yet in the project. No persistence API was added here.
- No models, props, textures or dependencies were added. No decision needed an ADR. Spec-review was skipped as instructed.

## Automated checks

Facade-level `KnownMapTests` cover walking discovery of Goods and Water points, unexplored ground and separate Villager knowledge, remembered food preferred over nearer unknown food, scouting, unseen depletion/refill and refresh, immutable past observations, trying running/dry taps, wrapped sight, Fog bank occlusion and observed Staff dangers.

All 11 Known map tests and all 23 existing needs tests passed in Release. The existing death test now checks the Body's position against the Villager's last living position, since deprived Villagers scout rather than standing still.

Final whole suite: **803 passed, 0 failed, 0 skipped**, Release, 6 min 59 s (`.ticket-logs/11a-full-suite.log`). Run once after all code changes, immediately before committing.

`dotnet build Scp3008.sln -c Release`: **passed**, 0 warnings, 0 errors (`.ticket-logs/11a-build-final.log`). Godot 4.7.2 .NET console `--headless --path game --editor --build-solutions --quit`: **exit 0**, empty error log (`.ticket-logs/11a-godot-build-final*.log`). Full run output is kept locally under `.ticket-logs/11a-*`.

## Rendered check and human review

Windowed Godot sandbox capture: `--resolution 1280x720 -- --villager-sandbox --sandbox-preset=scavenge --follow-villager --capture=<path> --capture-after=8 --fps-report`, with a 300-second process limit. The first capture exposed overlap with the needs HUD; the panel was moved before taking the final capture.

Screenshots are local at `.ticket-logs/11a-known-map.png` and `.ticket-logs/11a-known-map-final.png`, both 1280 pixels wide and inspected once each. The final panel is readable and does not overlap the sandbox controls or needs HUD. The first run measured 377.9 fps average over eight seconds; the final run measured 380.2 fps. Both exited successfully with empty error logs. This is a sandbox smoke check, not a full-Store 1080p benchmark.

Human visual review remains for interactive V selection, scrolling/toggling large Known maps, and watching stale sources and tap labels change during longer play. No audio changes were made or listening review performed. Full-Store balance and stage-end performance review remain outside this ticket.
