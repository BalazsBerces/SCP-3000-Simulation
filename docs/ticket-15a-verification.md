# Ticket 15a verification — issue #73

Review fixed point: `55d9badf509bc4d5fb0de53474ac9cce1906b9e7`.
Followed implement, TDD and Ponytail. Skipped spec-review as requested. Read ticket #73 (no comments), spec #1, GLOSSARY.md, all seven ADRs, the asset workflow and connected core/presentation code. Tests use only the simulation facade, scenarios, time advancement, snapshots and events.

## Behaviour and unattended decisions

- Every Band and Village starts knowing five basic Recipes, without a Workshop. Table-leg club: 2 wood/panel; canvas bag: 4 fabric and scissors; candle: 1 candle Goods; cardboard armour: 2 wood/panel; bandage: 1 fabric. Cardboard already disassembles into wood/panel, so no ninth Material was introduced. A candle Recipe prepares existing candle Goods as portable Gear rather than inventing wax Goods.
- Crafting takes 0.5 Store hours, or 0.1 for candles/bandages, divided by `1 + Builder skill`. Supplies are spent atomically at the start; tools remain equipped and wear by 1 on completion. Finished Gear enters the Village's open stores and can be collected through the existing Gear-seeking behaviour and matching slots.
- Idle Builders at home automatically make missing basic Gear when their supplies and tool permit it, targeting at most one of each kind per member, counting stored, equipped and pending Gear. Scenario commands can assign a particular Recipe. Workers cannot simultaneously craft, carry a load or work on Furniture. Urgent hunger/thirst, worker loss or a debug movement command cancels crafting; already spent supplies remain spent.
- Builder skill grows by 0.1 per crafting completion, shares the existing disassembly skill, and survives Role changes. Crafted durability starts at 60 and increases towards 100 with practice. Existing disassembly already uses Builder skill for faster work and richer yields; facade coverage now explicitly compares novice and expert recovery from identical Furniture.
- Bandages can be used once from the hand or from the Village's stores while at home; consumption emits `BandageUsed`. Wounded state and faster healing are intentionally left to #25, as the ticket specifies. No medicine, Workshop, Recipe discovery or combat behaviour was added.
- `GearMade` is emitted for every completion. Only the first completion per Village adds a Chronicle story, and its saved first flag prevents repeat stories after loading.
- Village panels list Recipes, each member's personal skills and unused Gear. The presentation reads snapshots; scenario mutations exist only in the dev check.
- Save format **4** includes community Recipes, stored Gear, first flags, personal skills, worker reservations and in-progress crafting. Damaged skill/task/Gear state is rejected. Earlier formats are explicitly rejected, following the existing version policy; no migration was added.

## Evidence

Final full Release suite, run once immediately before committing: **812 passed, 0 failed, 0 skipped**, 4 min 11 s (`.ticket-logs/15a-suite.log`). The final solution build passed with zero warnings/errors (`.ticket-logs/15a-build-final.log`), and the final Godot headless build exited 0 (`.ticket-logs/15a-godot-build-final.stdout.log`, stderr empty). No code changed after the full-suite run.

Twelve `RecipeTests` cover all five outputs, tool/Material rejection without spending, automatic Builder crafting, Gear pickup/carrying capacity, single-use bandages, speed/durability/skill growth, richer disassembly, worker reservations, damaged saves, frozen snapshots and save/load continuation without repeating the first story. The combined Recipes/Gear/FurnitureWork/Persistence regression run passed **53 tests** (`.ticket-logs/15a-regression-final.log`).

Godot 4.7.2 .NET console headless build passed (`.ticket-logs/15a-godot-build4.stdout.log`, stderr empty). The windowed check passed all assertions, exited normally, and captured a 1280 × 720 view:

```text
Godot_console --path game -- --villager-sandbox --sandbox-preset=empty --pause --recipe-check=<absolute-directory>
```

`.ticket-logs/15a-render3/report.txt` confirms crafting, all four newly rendered Gear kinds, all five panel Recipes, personal skill growth and first-Gear history. Inspected each capture once: the initial view shows all Recipes clearly; the corrected close view shows Villagers, reused Gear and the scrolled Skills/Gear section. The successful close capture is `.ticket-logs/15a-render3/recipes-skills.png`. An earlier rendered assertion was corrected to allow the existing Scavenger skill to precede Builder in the member's skill list. The first capture also exposed the already documented deferred sandbox camera move; the check now waits before positioning the camera.

## Assets and remaining human review

All visuals reuse existing downloaded models and their existing credits: canvas bag, candle Goods, cardboard box, rug (a small fabric bandage), and one leg's existing triangles/UVs/material from the dining table. No new downloaded models, textures or procedural geometry were added; no source search/download was needed after the reuse step of ADR 0006.

Human stage-end review remains: hand/armour alignment, whether the reused cloth and cardboard silhouettes read well at normal zoom, and motion quality. Audio and the full-Store FPS benchmark were not changed or rerun for this Recipe ticket; the stage-end review owns those checks.
