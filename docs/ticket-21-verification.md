# Ticket 21: Climbing

Issue: #22. Spec: #1. Review fixed point: `fae2d4244117aed7f2a3126d1ba9d64821ad4045`.

## Built

- Pillar scaffold Blueprint: eight 4.6 m rack stages, 36.8 m Lookout, eight metal Materials per new stage. A nearby Village plans one after completing its Castle; the scenario builder can plan one directly. Builders approach the site and place one stage per construction pass during open hours, with support and load checks followed by the shared stability rules.
- Exactly one Abandoned scaffold in each normal Store and Landmark showcase, on a Pillar's walkway ring in addition to the other Landmarks. Five existing stages, side platforms and an abandoned chair can be reused; completing it needs 24 Materials instead of 64.
- Bidirectional Furniture links on open shelving and mattress surfaces, including sloping mattresses. Scouts choose nearby Lookouts autonomously. The rack canyon and mattress mountain are reachable through their actual Furniture.
- Height is core state: movement uses three-dimensional distance, sight gains 2 m per metre of height, and the eye height rises too. Observed Goods, water and dangers use that sight; Restock uses it too. Furniture changes replan climbs; loss of support returns people to free floor.
- Saves retain elevation, climb destination, remaining route, visited Lookouts and scaffold slots. The existing version remains compatible: new fields default to empty/zero, and the new Landmark kind is appended to the enum.
- Godot interpolates elevation, draws figures at that height, follows their height with the camera, names the new Landmark, and reports scaffold progress and Lookout height in the Village panel.

## Decisions for this unattended run

Reuse the existing PalletRack and OfficeChair representations, including their existing procedural meshes. A rack's open uprights and crossbars are the climbable scaffold; no new model, prop, texture or procedural geometry was added. There was therefore no new asset search/download/conversion to perform under ADR 0006. No new prestige currency: the Village's visible height and panel report show its achievement.

Vertical routes are local to a Lookout (100 m); the existing floor navigation supplies their approach. No whole-Store vertical pathfinder or additional Store floors were added. The graph uses 0.55 m step tolerance and up to 1.8 m between adjoining surfaces. Unsupported people land immediately; Furniture still uses its existing Collapse animation. These are reversible tuning choices, so no ADR is needed.

## Verification

All command output is in `.ticket-logs/21-*` (local run artifacts).

- Final full Release suite: `dotnet test tests/Scp3008.Core.Tests -c Release`, **933 passed, zero failed**, 5.28 minutes. The first full run exposed the Camping ground test's old one-Landmark-per-Sector assumption; its assertion now excludes the additional Pillar-bound Abandoned scaffold, and the complete suite passed after that correction.
- `dotnet build Scp3008.sln -c Release`: passed, zero warnings/errors. Godot 4.7.2 .NET `--headless --path game --build-solutions --quit`: passed. Windowed `--climbing-check`: all six checks passed.
- Facade tests cover construction and Material cost, reuse and completion of the Abandoned scaffold, autonomous rack canyon/mattress mountain ascents, a built tower ascent/descent, save/load mid-climb and of the finished scaffold, Goods observed beyond floor sight, replanning during a climb, support removal/Collapse, unreachable heights and a Seam crossing.
- Windowed Godot `--climbing-check`: figure at 8.98 m, renderer elevation equals the snapshot, and five further Store minutes move the figure over 2 m upwards. The close-follow check also verifies that camera focus matches the climber height. The wide screenshot was inspected once: the tower and the pale figure partway up it are visible. The initial capture exposed a deferred sandbox camera reset; the check now waits for that reset and hides the UI before framing the tower.
- Screenshots: [climbing tower](screenshots/21/climbing-wide.png) and [close follow](screenshots/21/climbing-follow.png), 1280 by 720.

Human review remains: whether the existing figure/model poses read well as climbing, the Abandoned scaffold's improvised silhouette and side rooms, close-zoom camera feel, and pacing/visibility of autonomous climbing in a populated Store. This is not the stage-end performance review; no new audio was added and the full Store fps benchmark was not repeated.
