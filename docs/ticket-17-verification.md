# Ticket 17: First Blueprint

Issue: #18. Spec: #1. Review fixed point: `5072969f73c75171b2f097357266ce1b654609f8`.

## Built

Villages autonomously plan a barricade and allocate a Builder when food allows it. The core chooses fitting, reachable Furniture within 48 m, reserves one piece at a time, and uses the existing approach/carry/drag/cart path. On delivery it places the piece at the slot's free position and rotation, advances Builder skill and emits `BlueprintPiecePlaced`. Built pieces cannot be borrowed for hauling or disassembly. Scenario removal or movement clears their slot; cancellation releases reservations immediately.

Each piece retains its type, variant, tint and source identifiers. Both moving and stationary rendering use the piece's own instance colour. The Village panel shows outline and completed/required pieces per ring. Presentation only reads snapshots; the rendered check sets up its situation through dev-only facade commands.

## Decisions for this unattended run

- Cautious Villages favour round outlines (a twelve-sided approximation); Builders favour square runs aligned with the Store axes. A usable shelving face or nearby Pillar face makes a cramped site freeform. Bands receive a runtime Personality and pass it to their founded Village.
- A freeform ring incorporates the existing shelving piece or Pillar face as an entire boundary edge. Its remaining runs return around the Village; it does not draw a decorative extra wall over the anchor. Positions, delivery and source searches wrap across Seams.
- Slots initially require at least 0.8 m height and 10 kg weight, enough width to span the slot, at most 0.8 m excess width and at most 1.2 m depth. Fixed pieces, Lamps, floor coverings and fragile glass pieces do not fit. Weight is the first barricade's simple sturdiness proxy; structural loads, Tiers and reinforcements belong to later tickets.
- Every ring has a two-metre open gate passage between its runs. The first barricade does not have a moving gate mechanism. After four more Villagers join, a completed Village plans a larger outer ring while retaining the old wall and outline where possible.
- Builders haul to reachable floor beside a slot and make the final placement there: ordinary pedestrian clearance around an adjacent built piece otherwise makes the next slot's centre unreachable. General hauling keeps its existing destination validation.
- Save format is now **6**, containing Personality, outline vertices, anchor references, slot requirements, placed pieces and reservations, plus the existing in-flight hauling state. Loading validates references and placements. Earlier save versions are explicitly rejected.
- No new ADR: these choices are local tuning or follow existing architecture and free-placement ADRs. No new models, props or textures: the wall reuses the existing Furniture models and materials. No procedural assets were added.

## Verification

- Facade tests cover two entirely different fitting Furniture sets (Bookcases and tall Kitchen cabinets), reject unsuitable/oversized/weak/fragile pieces, preserve appearance, and observe distinct placement ticks.
- Facade tests cover round/square/freeform choice, incorporated shelving and Pillar faces, closed outline geometry, gate traversal, Seam crossing, retained completed walls on population growth, immutable earlier snapshots, partial construction save/load continuation, malformed saves, cancellation and removal.
- Focused construction/hauling/Recipes/Bands run: **47 passed**. Complete Release suite and final build results are recorded below before commit.
- Godot 4.7.2 .NET headless `--build-solutions --quit`: **exit 0**.
- Windowed `--villager-sandbox --sandbox-preset=empty --pause --blueprint-check=...`: rendered assertions passed, including active model and per-instance tint, completed mixed wall, and captures at **1280 × 720**. Both images were inspected once: the carried piece is visible and the completed barricade reads as a patchwork wall around the Village, with a clear gate opening.
- Full logs and the two captures are under `.ticket-logs/17/`; logs are intentionally ignored by Git. The repeatable rendered check is `game/scripts/DevCapture.Blueprint.cs`.
- The inspected completed view is preserved at [screenshots/17/blueprint-completed.png](screenshots/17/blueprint-completed.png).

Final complete Release suite: **842 passed, 0 failed, 0 skipped**, 4.76 minutes (`.ticket-logs/17/suite-final.log`). The final focused Blueprint/persistence run passed **25 tests** (`focused-final.log`). The Release solution build passed with **0 warnings and 0 errors** (`solution-build.log`), and the final Godot headless build exited **0** (`godot-build-final.log`). No production or test code changed after the complete suite.

## Human review

Review live carrying/placement at normal speed, furniture intersections at corners, the round/freeform silhouettes in a populated Store, and the visual balance of different Furniture heights. The full-Store FPS benchmark and end-of-stage visual/audio review remain stage-end checks; this ticket's sandbox rendering check is not a full-Store performance claim.
