# Ticket 17b: Outposts

Issue: #75. Spec: #1. Review fixed point: `165e610ff4248a00f060aae2a17b37f4196d311a`.

## Built

A Village whose nearby known food sources run low assigns two scavengers to an Outpost near a distant known source. They retain their Village membership, scavenge around the forward site and physically carry Goods back to the Village stores through the existing carrying and delivery path. The Outpost uses the existing barricade Blueprint: Builders reserve, fetch and place individual Furniture pieces near it, prioritizing the forward barricade over further home-wall construction. Home-wall expansion excludes Outpost barricades.

An Outpost is abandoned when its Known map reports no remaining nearby food or none of its assigned Villagers remain. Surviving residents are sent home; completed Furniture remains in place. The snapshot retains abandoned sites and their construction history. Restock can make a source worth using again after a fresh observation.

Outposts have Store labels, a prominent section in the owning Village's panel, construction progress and founding/abandonment chronicle entries. The Godot presentation reads snapshots. Save format **8** includes Outpost identities, positions, residents, abandonment and Blueprint references; existing Blueprint and Villager persistence handles construction reservations, movement and cargo. Loading validates Outpost references and membership. Earlier save formats are rejected under the existing version policy.

## Decisions for this unattended run

- Food drives initial expansion: a Village needs at least six members, less than two known food units per member within 48 m, and a known distant source with at least two units per member. The existing default Restock rate, 0.125 units per source per Store hour, is already slower than a large Village's consumption; no global rate change was needed.
- One active Outpost per Village, held by two available scavengers. Its site is walkable floor 6 m from its source; its scavenging catchment is 24 m. All distances and routes use the wrapping Store. These are local tuning choices, with broader balance runs remaining #85's work.
- Outpost barricades are stored with the Village's wall Blueprints and referenced by a stable wall index. This reuses construction, reservation cleanup, Furniture protection, navigation and save validation. Outpost rings are excluded from home-wall size, outline and population-growth decisions.
- Outpost residents retain the normal Needs priorities and carry each load home themselves. No separate logistics inventory, delivery timer or new Caravan system was added. An abandoned barricade remains as Furniture; no removal animation or independent Village split was added.
- No new domain term or irreversible architectural decision was introduced, so neither a glossary change nor a new ADR was needed. The requested spec review was skipped.

## Assets and rendered verification

Only existing Furniture models and materials are used. No new model, texture or generated fallback item was added, so no download or credit change was required.

The windowed `--outpost-check` passed with a 300-second process limit. It built all 57 barricade slots using the shared Blueprint path, checked the Store label, two assigned Villagers in the Village panel, construction progress and the founding chronicle entry. The [final 1280 × 720 capture](screenshots/17b/outpost-panel.png) was inspected once: the bookcase ring and gate opening are visible beside the Outpost panel entry. An earlier capture exposed that the new section needed to move above the member list; the final capture verifies that layout change.

## Automated verification

Full outputs are saved under `.ticket-logs/17b/`, which is ignored by Git. The initial red test, focused failures and fixes are retained there. An initial full-suite run was stopped after two damaged-save fixtures were found still targeting version 7; both fixtures now target version 8, and all five damaged/incompatible-save cases pass. The subsequent complete run is the final verification.

- Six Outpost facade tests pass: local depletion and longer scavenging trips, including across a Seam; founding near a known distant source; deliveries by assigned residents; construction; active and abandoned save/load round-trips; identical continued deliveries/events after loading; abandonment after observed depletion; frozen snapshots; corrupt Blueprint references; and guards for small Villages, rich surroundings and unknown sources.
- The related Blueprint, Workshop and Outpost run passed **29 tests** before the additional Seam case. The final Outpost run passed **6/6**, with no failures or skips.
- Final `dotnet build Scp3008.sln -c Release`: **passed**, 0 warnings, 0 errors (`solution-build-final.log`).
- Final Godot 4.7.2 .NET headless build: **passed**, exit 0, empty stderr (`godot-build-final.log`). Final windowed Outpost check: **passed**, exit 0, empty stderr (`godot-outpost-final.log`).
- Final complete Release suite: **864 passed, 0 failed, 0 skipped**, **4 minutes 42 seconds**, exit 0 (`suite-final.log`). No production or test code changed after this run began.

## Human review and limits

Review Outpost placement among dense Store Displays, their labels at different zooms and across Seams, and the long journeys between the forward barricade and home. Review depletion/Restock pacing and the one-active-Outpost tuning during the stage-end full-Store run. Full-Store FPS and audio were not measured for this ticket; no audio, asset or render-budget changes were introduced. Splitting into independent Villages, Staff combat and Raids remain their own tickets.

The PowerShell Godot runner returned an unset exit status once; the build was rerun successfully through Python with a reliable exit status and timeout. The proposed runner and save-fixture rules were appended to `docs/ticket-rules-proposed.md`.
