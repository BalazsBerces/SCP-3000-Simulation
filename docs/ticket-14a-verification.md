# Ticket 14a verification - issue #72

Status: Gear implementation complete; current workspace verification passes.

## Current unattended verification (2026-10-26)

Review fixed point: `f516e7ac04d6cad8b6d312e8a02b64d302471189`. Read the implement/TDD skills, issue #72 with comments, spec #1, glossary, all seven ADRs, model workflow and unfinished implementation. The existing Gear implementation meets the acceptance criteria; no further feature changes were needed. Tests use the agreed facade seam.

- Release `GearTests`: 9 passed, 0 failed/skipped (`.ticket-logs/14a/current-gear-tests.txt`).
- Final full Release suite, run once immediately before committing: 783 passed, 0 failed/skipped, 4 min 8 s (`.ticket-logs/14a/current-full-suite.txt`).
- Final Godot headless solution build immediately before committing: exit 0 (`.ticket-logs/14a/current-final-build.txt`).
- Godot headless solution build: exit 0 (`.ticket-logs/14a/current-build.txt`).
- Windowed model check: 60 Furniture and 6 Gear models, 0 failed checks, exit 0 (`.ticket-logs/14a/current-model-report.txt`). This includes Furniture silhouette checks and Gear dimensions, budgets, PBR textures, licences and credits.
- Windowed coarse-colour check: 287 comparisons, 0 failed, exit 0 (`game/.ticket-logs/14a/current-colour/report.txt`). All eight contact sheets, each 1280 px wide, were inspected once. This run includes the owner's preexisting Furniture colour work. Those unrelated files and colour-related hunks in `FurnitureModels.cs` are excluded from this ticket's commit; this result does not establish that the global colour check passes without that work.
- Gear texture import checks found no missing compression, mipmap or normal-map settings.
- Retained Gear screenshots and the windowed Gear-check evidence from the implementation attempt below were preserved, without recapturing or rereading already judged images. No assets were downloaded again and no new generated models/props/textures were added.

Unattended decision: keep the completed Gear change scoped, preserve preexisting Furniture colour changes in the working tree and stage only the Gear hunks in their shared model loader. The historical colour failures below are resolved in the current workspace, not waived. No additional cross-ticket tooling problem required a proposed rule.

## Earlier resumed verification (historical)

Reviewed the unfinished implementation against issue #72, spec #1, GLOSSARY.md, all seven ADRs and the model workflow. The implementation and nine facade tests cover the ticket's Gear acceptance criteria. No implementation changes were needed during this resumed attempt.

- Release `GearTests`: 9 passed, 0 failed or skipped (`.ticket-logs/14a/resume-gear-tests.txt`).
- Godot headless solution build: exit 0 (`.ticket-logs/14a/resume-build.txt`).
- Windowed model check: exit 0, 60 Furniture models and 6 Gear models, 0 failed checks (`.ticket-logs/14a/resume-model-report.txt`).
- Windowed coarse-colour check: exit 3, 20 failures in 287 comparisons (`.ticket-logs/14a/resume-colour/report.txt`). The failing comparisons match the earlier original-code baseline: SunLounger, HotFoodCounter, Tree variant 3, Tent variants 0/3/4, CampfireFlame, Kayak and SleepingBag variants 0/1. No Gear model fails this check. Neither Godot run timed out.

Unattended decision: preserve the ticket-only scope rather than change unrelated Furniture shading, proxy geometry or the check's thresholds. The required colour gate remains a failure, so the ticket cannot be committed, pushed or closed under the run's rules. The earlier full-suite and screenshot evidence below remains historical evidence; this attempt did not repeat the full suite (reserved for immediately before a commit) or recapture/reinspect already judged views. No new cross-ticket tooling problem was encountered and no additional proposed rule was added.

Review fixed point: `f516e7ac04d6cad8b6d312e8a02b64d302471189`.

## Implemented

- Found hex keys in Self-serve warehouse flat-pack boxes, kitchen knives in Bathrooms & kitchens, scissors in Workspaces and Textiles & Decor, canvas bags in the food market and Textiles & Decor, flashlights in Lighting & electrical, carts at the Shopping cart graveyard, and odd finds in Lost and found boxes.
- Nearby Villagers take suitable Gear automatically, with one hand, bag and armour slot. A cart reserves both hands through the hand slot. Finds are scarce: one suitable find per kind per Department. Found carts are selected from reachable positions.
- Goods and Materials share an abstract carrying capacity: 2 units bare-handed, 8 with a canvas bag, 32 with a cart. Capacity increases do not stack. Goods scavengers fill available capacity on arrival and deliver that amount to Village stores. Full loads halve speed; carts multiply the otherwise loaded speed by 1.5.
- Bags and carts lose one durability point per completed hauling trip. Flashlights lose one point per Store hour while powered at Closing time; one battery Goods unit supplies four hours. Batteries come from carried Goods or nearby home stores. Gear disappears at zero. Found cutting/assembly tools await their actual disassembly use in ticket 14b; they do not wear while idle.
- Snapshots and version 2 saves include slots, durability, batteries, cargo quantities, source locations, refill state and the next Gear id. Version 1 saves are rejected clearly; this is an explicit schema change, not an attempted migration.
- Villager attachments reuse the model loader and shaders; the follow panel lists Gear, wear and load. The sandbox has a repeatable `--gear-check` dev check. Armour is a scenario-only slot fixture, reusing an existing cloth model; armour crafting is not added.

Unattended choices: the unit capacities, wear rates and battery duration above are initial tuning values. Materials are an abstract carried unit count; producing and using typed Materials remains with disassembly. No Recipes, Workshops, tool-driven disassembly or animation system was added. No new ADR was necessary.

## Assets

Reuse inspection found the existing shopping cart and rug models. Poly Haven's model catalogue had suitable screwdrivers and flashlights, but no usable hex key, scissors or canvas bag; its fish-shaped folding knife was unsuitable for a kitchen knife. Sketchfab supplied those four objects. All chosen previews were inspected, licence API fields checked (CC0 or CC-BY), and download sizes checked below 100 MB. Search stopped after selecting suitable candidates; OpenGameArt was not needed.

The six new source entries are in `scripts/fetch-models.py`; that script downloaded and converted them once. Existing shopping carts are reused. Textures use VRAM compression and mipmaps, normal imports use normal-map compression, and credits name every author and licence. New Gear uses the same mesh at every distance, with small per-model triangle budgets. The shared product loader refits its rendered vertices after simplification to retain the intended size and floor origin.

New generated models/props/textures: none.

## Checks

- Nine Gear facade tests pass, including one-trip capacity, speed under Goods/Material loads, slot limits, wear to zero, batteries, found source restrictions, cart transfer/restock, save/load continuation and corrupt data rejection.
- Connected Gear, Band and persistence tests passed: 32 tests before the additional cart and invalid-input cases.
- Final full Release test suite: passed, 783 tests, zero failures or skipped, 4 min 13 s (exit 0).
- Final Godot headless solution build: passed (exit 0).
- Model check: 60 Furniture models and six Gear models, zero failed checks (sizes within 1 mm, triangle budgets, PBR textures, licences and credits). Final rerun after the baseline comparison passed (exit 0).
- Windowed Gear check passes: all six hand models and bags attach, and the battery-powered Closing-time flashlight beam is visible. Captures are 1280 x 720: `screenshots/14a/gear-open-centred.png` and `screenshots/14a/gear-closing-centred.png`. Each was inspected once. Initial captures exposed a dev-check startup camera timing problem; that was fixed before the retained captures.
- Mandatory `--coarse-colour-check`: FAIL, 20 of 287 Furniture type/variant/tint comparisons. A calibration attempt reduced this to 13 but still failed; all unrelated calibration edits were reverted. A controlled rerun with the original product fitting and proxy logic also produced the same 20 failures. Reports: `.ticket-logs/14a/colour-baseline/report.txt` and `.ticket-logs/14a/coarse-colour-3/report.txt`. These failures affect existing Furniture, including tents, camping models, the food counter and a flame with no compared coarse pixels. Resolving them needs work outside this ticket's Gear scope; this gate has not been waived or reported as passing.

All full command output is under `.ticket-logs/14a/` (ignored). No Godot run timed out. Godot scene lookup and an incremental-build cache during the controlled baseline comparison cost time; proposed rules are recorded in `ticket-rules-proposed.md`.

## Human review and limits

Review the retained daytime Gear silhouettes and attachments, cart handle/hand alignment with the existing static character pose, Closing-time beam and legibility at play zoom. No new audio was added, and no stage-end 1080p fps benchmark was run. Version 1 saves cannot load in this schema. Reproducing the passing global colour check requires the preexisting Furniture colour work, which is deliberately left uncommitted.
