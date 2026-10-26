# Ticket 15 verification — issue #16

Review fixed point: `14c65d4d5de9f6cee57594d08c4d733d68d15d17`.
Followed implement, TDD and Ponytail; skipped spec-review as instructed. Read issue #16 with comments, spec #1, GLOSSARY.md, all seven ADRs and the model workflow. Tests use the simulation facade: scenario, advance time, snapshots/events.

## Implemented behaviour and unattended decisions

- Furniture tasks reserve one piece and one to four distinct Villagers. Workers approach before work starts; occupied hands, existing loads, duplicate reservations, fixed Furniture, invalid coordinates, unreachable approaches and blocked destinations reject the request before reserving anything.
- Single carry limit: 25 weight units; teams contribute 25 each, up to four workers. Above the single limit, an insufficient team drags at 30% walking speed and emits Noise with a 30 m radius. Sufficient teams carry at walking speed. Above 100 units, only a shopping cart or disassembly works. A cart hauls at 150% walking speed and wears on delivery. The existing shopping cart implements this ticket's cart option; platform cart Recipes are later work.
- Representative weights: cardboard/labelled boxes 2, loose flat-pack boxes 15, mattresses and shopping carts 20, armchairs 40, sofas 75, ordinary/large wardrobes 140 (narrow wardrobes 70). Other weights derive from their existing dimensions. Tuning is local to the Furniture resource catalogue.
- The five tool requirements match the task's list; they are matching tools rather than a higher-numbered tool replacing every lower one. The crowbar supplies the fifth level. Normal disassembly takes 0.5 Store hours, bare-hands pieces 0.1; forcing multiplies time by three, halves yield and emits 45 m Noise. Builder skill divides work time by `1 + skill`, increases yield by 25% per skill point, and grows by 0.1 per completion. Tools wear on completion.
- Materials are the eight specified kinds. A leather armchair yields leather 4, foam 4 and wood/panel 3; its fabric variant substitutes fabric. Mattresses yield fabric 4 and foam 4, carts metal 6, cardboard boxes wood/panel 2, lamps electrics 2/glass 2/metal 1. Other families share catalogue yields; appliances also yield plastic. Materials enter the worker's Village stores at completion, with no separate Material delivery feature added.
- Furniture moves through the existing incremental navigation update and Restock vacancy handling. A cancellation, loss of workers/cart, or urgent hunger/thirst releases the reservation and drops carried Furniture. Disassembly removes Furniture and its dependent stock/water/Gear through the existing removal path.
- Active tasks, routes, work progress, worker reservations, Materials, learned skill and worn tools survive save/load and continue. Save format is now **3**; earlier formats are explicitly rejected, with no migration added.
- Presentation reads snapshots only. Carried Furniture is raised, dragging remains on the floor, workers move together, disassembly leans/pulses the worker, and labels identify modes and progress. Village panels show Materials. Moving meshes are drawn separately so hauling does not rebuild all static instance groups each tick.
- Assignment is available through the existing scenario facade and the rendered dev check. Blueprint construction queues, Furniture yards, advanced transport and Staff response to Noise belong to their later tickets.

## Automated and rendered evidence

Final full Release suite, run once immediately before this commit: **800 passed, 0 failed, 0 skipped**, 4 min 21 s (`.ticket-logs/15-suite.log`). Final Godot console headless build and the final windowed Furniture work check both passed. No code changed after the full-suite run.

`FurnitureWorkTests` has 17 passing facade cases covering:

- position changes and both vacated and newly blocked navigation;
- slow/noisy dragging versus two-, three- and four-person carrying;
- heaviest-piece rejection, cart speed/wear and disassembly;
- all five tool requirements and the specified Material yields;
- forced armchair disassembly versus a knife;
- actual generated Landmark mattresses/cardboard and Point-of-interest fabric armchairs/carts;
- skill improving time/yield, cancellation, fixed pieces, invalid reservations, damaged saves;
- simultaneous carrying/disassembly save-load continuation and frozen snapshots.

Logs: `.ticket-logs/15-work-final.log` (focused tests), `15-build-final.log` (headless Godot build), `15-model-report.txt` (60 Furniture / 7 Gear models, zero failed checks).

The windowed check is reproducible with:

```text
Godot_console --path game -- --villager-sandbox --sandbox-preset=empty --pause --furniture-work-check=<directory>
```

It asserts all five modes are active, six work labels render, the downloaded crowbar is attached, all tasks finish, and leather/metal reach their Village stores. The successful report and two 1280 × 720 captures are in `.ticket-logs/15-visual-final/`. Both captures were inspected once: work modes and progress are legible, Furniture changes position, disassembled pieces disappear, and the stores panel shows the expected Materials. An earlier capture setup was corrected because the fast cart had already arrived; its initial camera framing was also corrected after SeedPanel's deferred startup movement.

Human stage-end review remains: animation quality, hand contact with Furniture, cart/load alignment, and label readability at normal Observer zooms. Static captures do not establish the quality of motion. Hauling currently shares pedestrian route clearance; wide loads and worker contact in narrow aisles still need stage-end review.

## Asset workflow

Existing Furniture and Villager meshes are reused. No procedural model, prop or texture was added.
The one new asset is **Crowbar 01**, Alexander Otterbeck, Poly Haven, CC0: <https://polyhaven.com/a/crowbar_01>.
No existing model fitted a crowbar. The first required source, Poly Haven, had a usable one: its preview was inspected once, its 1k glTF and textures total under 2 MB, and the existing fetch script downloaded it once. The fetch entry and credits are committed; normal/ORM/albedo textures use VRAM compression and mipmaps, with the normal map import flag set. The rendered model check verifies its 0.65 m size, 800-triangle budget, PBR maps and licence/credit. Gear has no coarse proxy, so there is no new coarse silhouette/colour pair to compare.

## Full Store performance

The fixed benchmark passed at 1920 ? 1080, MSAA 4x and TAA on the RTX 2070 SUPER, 20 seconds per view (`.ticket-logs/15-benchmark-final.log`):

| View | Average fps | 1% low fps |
| --- | ---: | ---: |
| Open | 133.7 | 65.8 |
| Closing time | 163.9 | 78.1 |
| Closing time far | 128.5 | 86.7 |
| Closing time at LOD switch | 112.6 | 77.9 |

All pass ADR 0004's 90/60 gate. The LOD-switch view misses the 120 fps target. This measures the existing fixed Store presentation; it does not establish large-scale hauling throughput.

## Tooling corrections

The fixed rendering benchmark now sets the clock with `Scenario.SetTimeOfDay` between lighting passes. Its previous `AdvanceTo` simulated hours of full-Store Villager behaviour during setup; the first run was stopped and this corrected before rerunning. This leaves the benchmark measuring its fixed lighting views rather than unmeasured history. The camera-startup and benchmark rules were appended to `docs/ticket-rules-proposed.md`; the established ticket rules were not changed.

Preexisting Furniture colour/model/shader edits and `.claude` files remain outside this commit.
