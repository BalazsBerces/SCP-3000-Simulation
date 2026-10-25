# 09a: Villager sandbox

The sandbox uses the engine-independent facade and the existing Godot presentation. It has four
64 m Chunks, one Sector and one Pillar. Its dedicated layout is excluded from the normal Store
layout menu and normal generation tests; those still test all five generated layouts.

## Decisions and scope

- Default preset: scavenge. JSON files in `game/scenarios/villager-sandbox` are embedded in the
  core, so both the Godot launcher and facade tests load exactly the same files through
  `ScenarioBuilder`. No runtime working-directory dependency; editing files requires a rebuild.
- Clear Weather and one food-market Department keep the sandbox simple and fast. All furniture
  is hand-placed; no full Store generation, Points of interest or Landmarks run.
- Bands record membership and tools are available Gear, listed in the debug panel. Hunger,
  autonomous scavenging, Gear use and Band decisions belong to later behaviour tickets.
  The starving preset starts with one food unit versus three in scavenge.
- Staff use the existing footprint navigation, following their looping patrol only at Closing
  time. Combat, noise attraction and the final Staff appearance belong to their later tickets.
- Assets are reused: existing Furniture, Goods, Villager and faceless mannequin meshes and
  textures. No assets downloaded and no new procedural models, props or textures generated.
- No ADR: these fixture and UI choices are local and reversible.

## Automated evidence

Full command output and screenshots are kept under `.ticket-logs/09a-*` (local, ignored).

- Focused facade suite: 12 passing cases, including all presets running a Store day at 60x,
  distinct stock and membership, Closing-time event, patrol movement/daytime dormancy,
  wrapped navigation, pause/step, and rejection of invalid scenario data.
- Windowed `--sandbox-check`: all five presets selected through the real debug OptionButton;
  old people replaced, Step advances exactly one tick, 60x advances on the simulation thread,
  follow camera reaches a walking Villager. Exit 0, no Godot errors.
- Default CLI launch: generation 141 ms, first drawn frame 4315 ms; 369.4 fps average over
  the five-second capture at 1280 × 720. Inspected the screenshot once: readable controls,
  three Villagers, stocked shelves, Furniture and Pillar.
- Named CLI launch (`staff nearby`): generation 107 ms, first frame 4105 ms; 458.9 fps average
  over four seconds at 1280 × 720. Exit 0, no errors. Inspected the screenshot once: Closing
  time, one dim Staff silhouette, lit Lamp and correct panel state. Both captures are 1280 px wide.
- Final whole Release suite: **748 passed, 0 failed, 0 skipped**, 5 m 56 s.
  `dotnet test tests/Scp3008.Core.Tests -c Release`, run once at the end.
- Final solution Release build: **0 warnings, 0 errors**.
- Final Godot 4.7.2 .NET `--headless --path game --build-solutions --quit`: **exit 0**, no stderr.
- Final windowed sandbox UI check after the final build: **exit 0**, no stderr.

These short capture averages include startup effects and are sandbox smoke measurements,
not an ADR 0004 full-Store benchmark or a measurement of 1% lows.

## Human review

Review the sandbox's framing and Closing-time readability, camera follow and walking at 60x,
and the reused mannequin as a temporary Staff figure. Tools currently appear as named Gear in
the panel, not new 3D tool models. Final character animation/appearance and autonomous behaviour
are outside 09a; no full-Store stage-end visual/performance review was run for this ticket.
