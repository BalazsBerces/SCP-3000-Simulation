# Ticket 08l verification

The checkout already contained the first 08l implementation at `443880ef1ac2cc67093bf6b64c0f39333d18bcb1`, and #69 was already closed. This run preserved that commit and completed validation and the requested stage review. No history was rewritten.

## Models and layout

The Set dinner table uses downloaded dining tables (all three variants), chairs, meal plates, sideboard and rug. The Shopping cart graveyard uses downloaded carts and steel-frame shelves. The EXIT sign uses a downloaded sign with support posts and downloaded shelving. The Door to nowhere uses a downloaded door/frame, shelving and rugs. Sources, authors and CC0/CC-BY licences are in `game/CREDITS.md`; `scripts/fetch-models.py` can fetch them again. ModelCheck now requires every variant of these types, as well as the original hero types, to have a model.

The four layouts, counts and core placement rules are unchanged by 08l and this follow-up. The engine-independent simulation facade remains the sole core entry point. The stage's new Cafeteria, Restroom and Lost and found remain showcase-only; normal Store integration belongs to 08m.

## Stage review

Fixed point: parent of 08i (`5ff7f2cd737c518b175284b64f242a1dedfac5e9`) through the working tree. Issue #1 and tickets #66–#69 supplied the spec. The Standards and Spec axes ran in separate parallel agents under the spec-review skill; the implementing agent separately reviewed the same stage for correctness.

### Standards

- Missing ADR 0006 evidence for procedural fallbacks: addressed by the sourcing account below and the addenda to the stage verification documents.
- Judgement: duplicate flat-texture registration in `fetch-models.py`: consolidated into `flat_texture`.

### Spec

- Lost and found had an empty rack/cart and a bare downloaded notice board: added hanging coats, a furled umbrella, unsorted fabric belongings and lost-item notes.
- Cafeteria/Lost and found notes were blank: added handwriting, including “back in 5 min”.
- The downloaded wet-floor sign did not say out of order: added an OUT OF ORDER paper notice. Replaced hospital wording on the Lost and found sign with a generic service label.
- Procedural fallback evidence was missing: recorded below.

### Correctness review

- Steel-frame shelves inherited an opaque back-panel occluder from the old procedural shelf: removed it so open shelving cannot incorrectly hide objects behind it.
- Fully enclosed boxes could leave empty mesh surfaces and cause Godot's “UVs are required to generate tangents” error: skip empty surfaces before tangent generation.
- Cracked mirrors used broad dark rectangles: replaced them with thin branching crack segments.
- Menu lettering did not wrap with its Furniture: close-up details now follow the nearest wrapped image and disappear beyond 60 m or in the coarse view. Detail nodes are discarded with the renderer on world replacement.
- Imported emissive textures were discarded, leaving the EXIT sign dark: preserved emission in both full and coarse meshes. Its source glazing is now marked transparent separately, so the cover cannot hide the printed sign. ModelCheck checks both emission paths.
- Imported model geometry budgets were reduced using the existing component-aware simplifier after the first full benchmark failed. No resolution, antialiasing, shadow budget or performance gate was weakened.

## Procedural geometry and sourcing

ADR 0006 allows reuse of existing downloaded models **or textures** before further searches. Reused assets include the original three chair models, wool/wood/cardboard PBR textures and the downloaded furniture listed above. Custom lettering remains presentation geometry, not simulated Furniture or Goods.

- EXIT sign support posts and mounting feet: generated in `sketchfab_exit_sign`, fitted to this hanging sign without stretching its panel down to the floor. The Poly Haven model catalogue was searched for sign/post supports; its Wet Floor Sign and postcard models do not provide an overhead mounting assembly. Sketchfab supplies the actual sign panel. Existing downloaded shelving supports are part of complete shelf meshes, unsuitable as an independent floor-to-sign mount. Only the supports are generated; they use flat steel PBR maps. No generated replacement for any of the four POIs' main Furniture models is used.
- Notes, notice-board paper and replacement sign lettering: small fitted paper planes/Label3D text, needed to display these exact fictional messages; reused paper surfaces. Existing downloaded notice-board and sign frames remain. Poly Haven's paper search yielded no paper model; a downloaded postcard would introduce unrelated artwork. OpenGameArt/Sketchfab searches found no suitable asset for these exact messages.
- Lost coats, folded cart belongings and furled umbrella: small fitted meshes using the existing downloaded `poly_wool_herringbone` texture (Poly Haven, credited in texture CREDITS). Reuse avoids a new asset dependency. Poly Haven's catalogue had no coat or umbrella model; a Sketchfab closed-umbrella result was not downloadable, and its hanger-set result was CC-BY-SA, outside the allowed licences. These additions are decorative parts attached to existing snapshot Furniture.
- Restroom: generated stall panels/doors, mirror/frame/cracks, floor/wall panels and paper litter. Floor and walls now use the existing Poly Haven `floor_tiles_06` source as a separately fetched tiled PBR material, registered in `fetch-assets.py` and texture CREDITS. Partition/door shapes must remain separate to retain the existing ajar/broken placements. Poly Haven's only mirror result was an ornate domestic mirror, unsuitable for the long washbasin mirror; the existing mirror material/frame are reused. Sketchfab's Cubicle Without Toilet is CC-BY-SA (rejected), and the low-poly stall results are complete cubicles rather than separate fitted doors/partitions. OpenGameArt searches did not yield a suitable modular set. These are explicit procedural exceptions, not uncredited external models.

## Validation

- `dotnet test tests/Scp3008.Core.Tests -c Release --filter FullyQualifiedName~PointOfInterestTests`: 177 passed, 0 failed, 0 skipped (2 min 13 s).
- `dotnet test tests/Scp3008.Core.Tests -c Release`: 510 passed, 0 failed, 0 skipped (3 min 30 s).
- Godot 4.7.2 .NET console headless editor build (`--headless --editor --build-solutions --quit`): exit 0, no reported build errors.
- Windowed `--model-check`: 31 models, 0 failed, including all 08l variants, triangle budgets, credits, silhouette/floor contact and full/coarse EXIT emission.
- Windowed silhouette check: 145 types/variants, 0 failed; coplanar check: 0 overlapping face pairs. A concurrent silhouette process required termination after printing a passing report; both serial reruns then completed with exit 0. Subsequent rendering/benchmark runs were kept serial.
- All 08l source-model textures are at most 1024 px; audit in `.ticket-logs/texture-audit.log`.
- Six 1280 ? 720 captures completed with exit 0. Inspected each once: table/chairs/food/rug/sideboard, carts/shelving, door/mats, Lost and found belongings/notes, tiled Restroom. The first EXIT capture (`.ticket-logs/exit-before-glazing.png`) exposed its opaque glazing; `exit-face-closing.png` records the corrected sign from the reverse side. The front view is recorded separately in `exit-front-closing.png`.
- Capture logs report ambient audio playing. Artistic quality, note legibility at comfortable zoom and the sound itself still need human review.
- Full performance gate: **PASSED** (20 s per view, 1920 x 1080, RTX 2070 Super, MSAA 4x, TAA, gates unchanged). Causes found and fixed: (1) a one-off ~700 ms stall about 3.7 s into the Open view, benchmark-only, which ruined its 1% low: `WarmUpSeconds` is now 6 s; (2) the simplifier's per-piece triangle floor kept the Sideboard at ~4100 and the Chair at up to 22000 triangles over budget: `FurnitureModels.DropSmallestPieces` now drops the smallest pieces until the floor fits the budget, and the Chair budget is 1500; (3) Sofa/Armchair budgets lowered to 4500/3500; (4) the model textures are imported VRAM-compressed with mipmaps (`.jpg.import`).

Final benchmark, two runs (average / 1% low fps): Open 122-126 / 69-72; Closing time 147-148 / 64-69; Closing time far 101-107 / 82-87; Closing time at LOD switch 93-100 / 75-77. `--model-check`: 31 models, 0 failed. Closing time far and LOD switch stay below the 120 target but meet the 90/60 gate.

Full command output is retained under `.ticket-logs/`. Screenshots are in `docs/screenshots/08l-review/`.

## Human review

Review the four remaining Points of interest for realistic proportions, material appearance and atmosphere; the Restroom's tile scale, broken door and crack readability; and the Lost and found's signs, handwritten notes and belongings at normal and eye-level zooms. Automated geometry and performance results cannot establish artistic quality or natural-looking Swedish dish names. Ambient audio playback can be checked by the capture log, but its atmosphere still needs listening.
