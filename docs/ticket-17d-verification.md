# Ticket 17d: Mess hall and shared meals

Issue: #82. Spec: #1. Review fixed point: `75e902d5a8609692f935884c38c9ffd680a7635d`.

## Built

Builders reserve, fetch and place a four-slot Mess hall Blueprint near the Village centre: a table, two chairs or benches, and kitchen Furniture. The existing construction queue, hauling, navigation, reservation cleanup and built-Furniture protection are reused. Its placement is inside the initial home wall footprint, beside the future Keep location. The presentation only reads snapshots.

Villagers gather for three shared meals daily. Those who actually reach the table eat from the Village stores, gain morale according to the number fed, and exchange their personal Known maps. A cook present at the table stretches one food Good into two servings. Shared meals emit a domain event and a Chronicle entry naming those who exchanged news. A Villager away on a trip, carrying a load, busy with Furniture or crafting, threatened, or urgently thirsty continues attending to that situation and can miss the meal. Normal solitary eating remains available.

The Mess hall is labelled in the Store, the existing downloaded meal plates appear on its table while people eat, and Villagers face the table with a slight eating movement. The Village panel shows construction progress, daily mealtimes and the current number eating together above the population list. [Rendered evidence, 1280 px](screenshots/17d/shared-meal.png).

## Decisions for this unattended run

- Meals gather at 09:00, 14:00 and 20:00, serve after 30 Store minutes, and end after another 15 minutes. The evening meal finishes before the 21:30 light fade and 22:00 Closing time.
- Available members within 24 m may come; paths use the existing footprint navigation and wrapped distances. Working members are not interrupted. Workshops keep their existing construction priority; available Mess hall pieces are fetched next, followed by walls.
- Each serving reduces hunger by 0.65, matching solitary food. Morale rises by 0.04 per other person actually fed, clamped to the existing maximum. A solitary diner gets no shared-meal morale reward. These are reversible tuning values, so no ADR was added.
- Village Known maps remain aggregate planning knowledge, but no longer broadcast automatically to every member. Only diners exchange personal observations at meals. Joining Wanderers receive the Village map; their arrival no longer broadcasts to existing members away from home. Bands retain their existing knowledge exchange.
- Save format **9** stores the Blueprint, reservations, last served meal, movement, meal actions, Needs and personal maps. Saves resume gathering and hauling and cannot grant a second meal reward after loading. Invalid references, positions, schedule ticks and duplicate construction claims are rejected. Earlier save versions are explicitly incompatible under the existing policy.
- Existing glossary terms suffice. Beliefs remain deferred to #36, as #82 requests. No new model, prop, texture, dependency, Keep implementation or seated character rig was added.

## Asset workflow

ADR 0006 and `docs/model-workflow.md` were followed: reuse search found existing DiningTable, Chair, HallBench, KitchenCabinet and MealPlate models already wired through FurnitureModels and credited in `game/CREDITS.md`. No downloads or generated geometry were needed; generated-item list: **none**. Existing Furniture rendering and material lighting are reused.

## Verification

Full test, build and Godot outputs are retained under ignored `.ticket-logs/17d-*.log` / `.err` and `.ticket-logs/17d-render-fixed/`.

- Eleven MessHallTests facade cases cover hauling and gathering, morale by group size, cook savings, personal map transfer, an absent traveller eating alone without receiving news, all three daily mealtimes, work and empty stores, corrupt saves, gathering/served/mid-haul save continuation, and joining-Wanderer news isolation.
- Connected tests: **55 passed, 0 failed, 0 skipped**, including Known maps, Wanderers, Blueprints and Outposts. Earlier Workshop/persistence/meal run: **38 passed**, including both full Store save continuations.
- Windowed Godot rendered check: **passed**, exit 0, empty stderr; asserts built Blueprint, four diners, meal plates, world label and Village panel. The final screenshot was inspected once: table, food, surrounding Villagers and panel are visible. The first capture exposed initialization resetting an early camera jump; the runner now waits for initialization before placing the camera. The corrected view uses a different focus and angle.
- Final `dotnet build Scp3008.sln -c Release`: **passed**, 0 warnings, 0 errors (`17d-solution-build-final.log`). Final Godot 4.7.2 .NET headless build: **passed**, exit 0 (`17d-godot-build-final.log`).
- Final complete Release suite: **875 passed, 0 failed, 0 skipped**, test duration **5 minutes 10 seconds**, exit 0 (`17d-suite-final.log`, TRX under `.ticket-logs/17d-test-results/`). No production or test code changed after this run began.

## Human review and limits

Review the shared meal arrangement and existing character pose in a populated Store, readability at different zooms and across Seams, and meal attendance and food balance over multiple days. Villagers currently use the project's standing character model; no seated rig was introduced. Full-Store FPS and audio were not measured for this ticket; stage-end performance review remains separate. Save format 8 and earlier cannot be loaded by this build.

Suggested rule: A dev capture started from _Ready can finish scenario setup before CameraRig._Ready resets its view. Wait for scene initialization before jumping the camera, then verify the rendered subject is actually on screen.
