# Ticket 17a: Workshops and know-how

Issue: #74. Spec: #1. Review fixed point: `1c194c06f79b84a7548915f10c2c451c59ebeb1c`.

## Built

Villages plan three one-slot Workshop Blueprints, fetch suitable tables or desks with the shared Furniture hauling path, and retain their model, variant and tint. Completed Workshops protect their Furniture from reuse and enable only their matching Recipes. Removal or movement clears completion; interrupted hauling releases the reservation.

Nine middle Recipes make leather backpacks, saws, crowbars, spears, tabletop shields, platform carts, quilted armour, lanterns and hand-cranked generators. Recipes spend their Materials, including a second Material where needed. A spear consumes its knife. Beds now yield frame metal. Saws join crowbars at the top disassembly tool level; platform carts use the existing heavy Furniture hauling path; leather backpacks carry 20 units against a canvas bag's 8. Lanterns reuse battery-backed portable lighting. Generator power remains #23's work.

Builders experiment autonomously with the matching completed Workshop, tool and Materials. Instructions in generated flat-pack/Lost and found boxes teach the discovering Band or Village a Recipe. Joining Bands carry their know-how into the host Village, including Refugees with their former Village's Recipes. The chronicle records discoveries; the Village panel lists Workshops and all known Recipes. Presentation reads snapshots only.

## Decisions for this unattended run

- A working surface is one Blueprint slot requiring a table or desk 0.8–1.6 m wide, at most 1.2 m deep, at least 0.6 m high and 10 kg. All three stations reuse these Furniture types, with the appropriate tool and a readable station label. Their initial positions are inside a normal first wall ring. A Builder fetches them before further wall construction when suitable Furniture is available.
- Experimenting reserves a Builder like crafting, spends a Recipe's Materials at the start, and takes 8 Store hours divided by `1 + Builder skill`. Interrupted experiments lose spent Materials. Discovery grants the Recipe, not free Gear. These are reversible tuning choices, not a research tree.
- Generated instructions are scarce: one suitable box per Department, with its Recipe chosen from the box's stable identity. A community reads nearby instructions within 3 m and consumes the sheet once; knowledge remains with it. The existing spatial index bounds this search, avoiding whole-Store scans on every discovery check.
- Refugees use the existing Band entry seam with their former Recipes. Village collapse, Captive capture/absorption, trade and alliance systems remain their own tickets; this ticket makes the common joining path carry middle Recipes.
- Save format **7** persists Workshops and reservations, unread instructions, active experiments and their progress, extended Recipe sets and new Gear. Loading validates references, slots, workers and progress. Earlier save versions are explicitly rejected, following the existing persistence policy.
- No new ADR: the architecture and asset choices follow existing ADRs, and the new decisions are local tuning.

## Assets and presentation

Reused the existing table/desk, bag, rug, industrial cart, lantern, crowbar, knife, hex key and flashlight assets. Spear, shield and improvised hand-cranked generator reuse downloaded mesh parts and their PBR materials; no new procedural asset was added. The larger bag uses the existing bag silhouette; quilted armour reuses folded cloth. Gear fit and material readability still need human review.

The new saw uses Poly Haven's **Handsaw Wood**, by Konrad Szałankiewicz, CC0. Its API metadata, 1k file listing and preview were inspected before downloading through `scripts/fetch-models.py`. It has a 1,200-triangle budget because few Villagers' tools are visible close up, 1k textures and VRAM-compressed mipmapped imports. Credits are in `game/CREDITS.md`. No generated fallback items require a source-search exception.

The asset checks exposed two existing validation problems. Silhouette comparison counted transparent counter glass although coarse meshes intentionally omit it; the checker now compares opaque silhouettes. The Lantern tent's coarse mesh dropped the person's silhouette; it now retains that silhouette through the existing coarse ellipsoid boxes. The corrected silhouette check passes all 181 Furniture variants.

## Verification

All raw outputs are under `.ticket-logs/17a/` (ignored by Git). The first failing facade test is `red.log`; the focused checks and subsequent fixes are retained there. A superseded persistence run was stopped after identifying an unnecessarily global instruction search; the spatial-index fix is covered by the final complete suite. An overlapping compilation hit Windows testhost's assembly lock; subsequent checks run sequentially.

- **16 Workshop facade tests passed**, covering Workshop gating, the armchair-to-backpack path, novice/skilled discovery, assembly instructions, joining Refugees, all nine middle Recipes and their ingredients, knife consumption, saw disassembly, heavy platform-cart hauling, frozen snapshots, corrupt saves, and continuation of crafting, experimenting and reserved Workshop construction after loading.
- Windowed `--workshop-check`: **passed**, including meshes for all nine middle Gear, three visible stations, assembly-instruction discovery, Village panel and chronicle assertions. The [1280 × 720 capture](screenshots/17a/workshops.png) was inspected once: all three tables and labels are visible with the middle Gear lineup and Workshop panel.
- Model validation: **60 Furniture models and 8 Gear models, zero failures**; includes saw size, base, triangle budget, PBR maps and licence/credits.
- Final coarse-colour validation: **287 combinations, zero failures** after the tent silhouette repair. A 1280 px overview of all eight contact sheets was inspected once; paired colours remain consistent.
- Silhouette validation after repair: **181 variants, zero failures**.
- Final complete Release suite: **858 passed, 0 failed, 0 skipped**, **4 minutes 40 seconds**, exit 0 (`suite-final.log`). No production or test code changed after this run began.
- Final solution build (`dotnet build Scp3008.sln -c Release`): **0 warnings, 0 errors**, exit 0 (`solution-build-final.log`).
- Final Godot 4.7.2 .NET headless build: **exit 0** (`godot-build-final.log`). All windowed checks ran with 300-second process limits and completed within them.

## Human review

Review Workshop tool placement on alternative table/desk variants, station spacing in round/freeform walls and across Seams, and Gear fit and material readability at close zoom (especially the reused backpack, quilted cloth, cart and improvised generator). Review the restored tent silhouette at the LOD transition. The full-Store FPS/audio review remains the stage-end check; this sandbox capture is not a performance measurement. No trade, alliances, power production or combat behaviour was added.
