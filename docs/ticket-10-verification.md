# 10: Needs and Goods

Implements issue #11 against spec #1. Review fixed point: `e90aaf78d192eb9a78d0fbb5d2258af4da1443c8`.

## Decisions and scope

- The engine-independent core owns Needs, resource choices, navigation, consumption, deprivation and death. Godot reads snapshots and events; only existing dev commands set up scenarios.
- Hunger and thirst range from 0 (satisfied) to 1 (depleted). Rest, safety and morale range from 0 to 1, with higher better. Hunger fills over 24 Store hours, thirst over 12; one food unit relieves 0.65 hunger and one drink unit 0.8 thirst. A running Water point quenches thirst. Sweets and candles raise only morale.
- `WorldConfig.Needs` sets the fatal periods at full hunger/thirst (72/48 Store hours by default); eating/drinking resets the matching deprivation. Bodies retain Villager identity, position, heading, death time and cause. `VillagerDied`, `GoodsConsumed` and `WaterDrunk` expose the resulting events.
- Choices run every 0.1 Store hour, using existing wrapped footprint navigation. Nearby resources within 80 m provide a temporary local choice until Known maps arrive in their ticket. Dry taps are learned by trying them; unreachable destinations are skipped until Furniture changes. Staff encounters at Closing time lower safety and trigger moving away; combat remains in the Staff tickets.
- Exhaustion triggers rest on nearby Furniture, in an armchair henge or on the floor. Rest recovers twice as fast inside the henge. Comfort consumption follows low morale. These rates, initial values and local choices are reversible tuning decisions; no ADR added.
- Set dinner tables now hold four Fresh food portions each as Stock Furniture. Display fridges and cabinets still yield only generated left-behind finds, and ordinary tables keep their rare finds. Consumption removes one existing unit, updates Sector stock and the existing shelf visuals, and cannot spend the same portion twice.
- Both sandbox presets start hungry and thirsty. Scavenge supplies all three Villagers; starving has one food portion and a deliberately short two-hour starvation period, leaving two bodies after three Store hours. JSON supports initial `needs` and `needsConfig` through the same facade loader used by tests.
- The Observer follow panel shows all five Needs and the action. Sandbox counts update live. Bodies reuse the existing downloaded Villager mesh, laid on the floor: no new model, prop, texture or procedural geometry was needed.
- Carrying, spoilage, Restock, Band strategies and Staff combat remain with their own tickets. The requested spec-review step was skipped.

## Verification

Full outputs and captures are local under `.ticket-logs/10-*`.

- Needs facade tests: **23 passed**, covering time, autonomous consumption, all four Water point kinds, dry taps, Cafeterias, Set dinner tables, Aquarium fish, both comfort kinds, fatal periods and single death events, bodies and unique ids, faster henge rest, competing consumers, left-behind finds, safety, presets, validation and food across a Seam resetting starvation.
- Related stock, navigation and sandbox tests: **112 passed** before the final additional Seam case. Existing table-find assertions exclude the newly stocked Set dinner tables; sandbox day runs account for depletion and bodies.
- Solution Release build: **0 warnings, 0 errors**. Godot 4.7.2 .NET headless solution build: **exit 0**, no stderr.
- Windowed sandbox UI check: **exit 0**, no stderr; all presets, Step, 60x on the simulation thread and follow camera passed.
- Scavenge running capture: **404.0 fps** average over 10 seconds at 1280 × 720; starving capture: **400.7 fps** over four seconds. These are sandbox smoke measurements, including startup effects, not the 1080p full-Store benchmark or 1% lows.
- Inspected each 1280 px capture once. The first follow capture exposed an off-screen panel anchor, corrected before the final capture. `10-follow-needs-final.png` shows the readable five-Needs panel and seeking action; `10-starving-bodies.png` shows depleted shelves and a body, with two overlapping bodies recorded by both the panel and log. No Godot run timed out.
- Final whole Release suite, run once just before commit: **771 passed, 0 failed, 0 skipped**, **5 m 43 s** (`10-suite-final.log`). Final solution Release build again passed with **0 warnings, 0 errors** (`10-dotnet-build-final.log`).

## Human review

Review action readability while following at 1x and 60x, rest and body poses using the reused static Villager model, overlapping people/bodies, and pacing of scarcity. Full-Store visual review and the reference-machine 1080p benchmark remain for the stage-end ticket; no audio changed here.
