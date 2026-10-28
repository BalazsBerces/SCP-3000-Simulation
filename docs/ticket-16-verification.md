# Ticket 16 verification — issue #17

Implementation fixed point: `24753ef91e877b37ed2f99d8be0723311f63943e`. Stage review base: the parent of `350e8a9` (`09: ...`), through this ticket's working changes. Followed implement, TDD, spec-review and Ponytail. Read issue #17 and its comments, spec #1, GLOSSARY.md, all seven ADRs, the model workflow and the connected core/presentation code. Tests operate through the simulation facade, scenarios, time advancement, snapshots and events.

## Behaviour and unattended decisions

- Lost shoppers arrive as a lone Wanderer or a Band of up to four, on safe walkable floor outside the dead zone. They use the existing Needs, scouting, scavenging and founding behaviour. Arrivals use a separate deterministic runtime random stream, preserving Store generation and other runtime streams.
- The hidden `ArrivalConfig` defaults to one arrival opportunity per 12 Store hours. Above a population of 200 (Villagers plus Staff), the next interval is multiplied by population / threshold. It slows arrivals without imposing a population cap. Both values are configurable behind the facade; zero interval (`ArrivalConfig.Off`) disables arrivals. Sandboxes and showcases default to disabled, and can explicitly opt in. These are initial balance choices, with no new player-facing setting or births.
- The first interval is scheduled after starting Bands are created, so a low configured threshold also counts the initial population. A final source check caught the earlier ordering error; its focused regression failed before the change and passed afterward.
- A Band learns a Village only through the existing visibility rules or shared observations on its Known map. It chooses a destination only from its Known map, travels to a reachable known Village and joins when all members are nearby. A Villager who sees a remembered Village empty marks it abandoned, and the Band stops heading there. Needs retain priority during travel, including drinking, eating and rest; members at the destination wait together. A Band founds its own Village when no reachable known Village is available, or when it has at least three members, is not forced, and knows a good site (food and water nearby) closer than the nearest known Village.
- Joining unions the Band's Known map and Recipes into the Village and shares the result with members. Existing members keep their Roles and skills. Scenario placement can seed a Refugee Band with a fallen Village's observations and Recipes; actual Village destruction remains later-stage work. Founding also preserves inherited Recipes.
- Arrival, joining and founding are public domain events with Chronicle stories. Only arrivals of note get a story: a group of three or more, large enough to found a Village; lone shoppers and pairs arrive too often to record. Arrival and joining each have three name/place templates. The stage review also corrected first-Gear Chronicle variation and place references without changing its first-only policy.
- Save format **5** persists the arrival random stream, next scheduled arrival, observed Villages, Band destinations and inherited knowledge. Continuation checks compare future snapshots (including the Chronicle) after loading; the existing full-Store persistence tests also compare domain events. Earlier formats are explicitly rejected under the existing version policy; format 4 saves need the previous build. Stage review fixes also reject invalid saved Villager ids, community references and Staff patrol routes before they can cause runtime failures.
- No new domain term or hard-to-reverse architecture decision was needed, so no glossary entry or ADR was added. The engine-independent simulation remains behind one facade; production presentation reads snapshots/events.

## Stage reviews: tickets 09–16

Both reviewers received the complete stage diff and originating stage tickets, including follow-up tickets. Reports and the captured diff are retained under `.ticket-logs/16-*review*` and `.ticket-logs/16-stage-final.diff`.

## Standards

One documented-standard finding and one baseline heuristic, both P3. First-Gear history had only one template without a place, contrary to the glossary's Chronicle definition; it now has three place-aware variants, covered through the facade. `FinishDisassembly` also served crafting; the shared skill/tool-wear operation is now named `PracticeBuilding`, with both callers updated. Follow-up found both resolved and no further actionable standards finding.

## Spec

One P2. Repeated Band movement commands cancelled survival targets on every joining decision, preventing food/water/rest on the journey. Band decisions now record the destination, while the Needs decision chooses when to travel there. A red/green facade test verifies a thirsty Wanderer drinks from an en-route Water point before joining. The same fix covers founding travel; existing dead-zone escape coverage required and verified that exhausted Bands can still leave unsafe ground. Follow-up found the issue resolved and no additional concrete missing requirement or scope creep.

Review totals: Standards 2 findings (worst P3), both resolved; Spec 1 finding (P2), resolved.

## Post-commit spec-review fixes

A two-axis review of `917aba4` found: joining stories without a place; glossary-avoided terms (`AgentThreshold`, "newcomer"); every arrival chronicled rather than arrivals of note; Bands always joining any known Village; and joining decided from live Village state rather than the Band's Known map. All are fixed as described above. `ArrivalConfig.AgentThreshold` is now `PopulationThreshold` (scenario key `populationThreshold`). Smaller cleanups: a shared `ArrivalConfig.Off` replaces the per-fixture literal, a single safe-floor check, a shared arrival radius, named runtime random streams, a `VisibleAt` helper in Known map observation and a named membership validator for loading. `KnownVillage.Abandoned` defaults to false, so format 5 saves still load.

Test time: the ticket 16 suite took 23 min against 4 min at 15a. Almost all of the increase came from the full-Store ball-pit network test (1117 s of test time), which ran several Store days with arrivals but no other Villagers. It and the 12-hour Pillar comparison now set `ArrivalConfig.Off`, since neither needs Villagers. The full-Store persistence continuation was the next critical path (507 s, run serially within its class); it now schedules one arrival at 23 Store hours, late in the 24-hour continuation, and asserts that it happens, so the saved arrival stream stays covered on both layouts at a fraction of the cost.

Not changed: Refugees still come only from scenarios (Village destruction is later-stage work); Recipe union on joining is a no-op for ordinary Wanderers because every Band and Village knows the basic Recipes (ticket 15a). The stage-review changes the review called scope creep (save hardening, first-Gear stories, `PracticeBuilding`, no rest on Darkness ground) stay.

## Own correctness review

Reviewed the same stage diff and connected movement/navigation, Needs, knowledge sharing, community work, Furniture work, Gear/Recipes, restock, persistence, host, snapshot interpolation and community presentation code. Reproduced two malformed-save failures (negative Villager id and nonexistent Village membership); load now rejects both, with facade regression tests. Checked the fixes for duplicate memberships, existing Role/skill retention, safe arrival positions, deterministic continuation and target interruption. The final first-interval ordering defect is also covered by a failing-then-passing regression. No further concrete correctness bug remained. Future Staff/combat/healing, construction/transport and advanced Recipe work retain their documented stage boundaries.

## Evidence

Targeted Release regressions passed: **37 tests** covering Wanderers, Bands, Recipes and damaged membership saves (`.ticket-logs/16-regression5.log`), followed by **21 tests** covering the final Wanderer, Chronicle and damaged-save cases (`.ticket-logs/16-regression6.log`). Tests include arrival groups, safe full-Store spawning, joining only remembered Villages, seam-crossing travel, map/Recipe transfer on joining and founding, survival actions during travel, slowdown counting Staff, configuration validation, event stories and exact save/load continuation.

The final **15 Wanderer tests** passed after the first-interval fix (`.ticket-logs/16-wanderer-final.log`); its failing regression is retained in `16-initial-population-red.log`.

The first complete-suite attempt exposed an older fridge-consumption fixture that advanced 20 hours while assuming only its original Villager existed. It also made focused weather/lighting/restock tests simulate unrelated populations, including a 60-day Dry spell run. That already-failed attempt was stopped (683 passed, one failed); the affected focused fixtures now explicitly disable arrivals, while the full-Store persistence tests retain them. Production defaults remain enabled. The interrupted attempt is retained in `.ticket-logs/16-suite-before-fixture-fix.log`.

A second attempt was stopped to apply the newly reproduced first-interval bug (716 tests had passed with no failures, `16-suite-before-initial-population-fix.log`). Neither interrupted attempt is counted as a successful full-suite check.

Detailed output from the next attempt identified the remaining six-day fog comparison and repeated lamp-cycle fixtures still admitting unrelated shoppers. That attempt was stopped after 724 passes with no failures (`16-suite-before-world-fixture-fix.log`), and the remaining clock/lighting/landmark fixture helpers were explicitly isolated as well. No assertions were removed. The final complete result below supersedes all interrupted attempts.

Final complete Release suite: **830 passed, 0 failed, 0 skipped**, 23 min 9 s (`.ticket-logs/16-suite-final.log`). Both full-Store persistence continuations retain active arrivals (see the post-commit fixes). No production or test code changed after this run.

The final Release solution build passed with zero errors and one pre-existing xUnit2031 warning in `NeedsTests.cs:144` (`16-build-final.log`). The final Godot 4.7.2 .NET console headless build exited 0 with empty stderr (`16-godot-build-final.stdout.log`, `16-godot-build-final.stderr.log`).

The windowed Godot check exited 0 and passed every assertion (`.ticket-logs/16-band-render/report.txt`): Band membership, founding, scavenged stores, a lone Wanderer walking to and joining the Village, Known map transfer, population update and joining Chronicle display. Each of its three 1280 × 720 captures was inspected once. The joined view is preserved at [screenshots/16/joined.png](screenshots/16/joined.png).

```text
Godot_console --path game -- --villager-sandbox --sandbox-preset=empty --pause --band-check=<absolute-directory>
```

A normal windowed full-Store run also exited 0 (`16-full-store.stdout.log`, stderr empty). Its [1280 × 720 capture](screenshots/16/full-store.png), inspected once, shows four starting Bands (13 Villagers), their Store surroundings and the Villager/community UI. This complements the sandbox behaviour check; it is not an audio or motion-quality assessment.

The full-Store windowed benchmark exited 0: Store seed 3008, 1920 × 1080, RTX 2070 SUPER, fixed 160 m Fog bank, 4× MSAA, TAA, 20 seconds per view (`.ticket-logs/16-benchmark.stdout.log`, stderr empty). All views passed the enforced 90 FPS average / 60 FPS 1% low gate. The two distant views missed the aspirational 120 FPS average target; the far view rounds to 120.0 but is just below it.

| View | Average FPS | 1% low FPS |
| --- | ---: | ---: |
| Open | 129.2 | 62.1 |
| Closing time | 156.1 | 74.2 |
| Closing time far | 120.0 | 82.2 |
| Closing time at LOD switch | 106.4 | 78.5 |

## Assets and remaining human review

All visuals reuse the stage's existing credited assets; the ADR 0006 reuse step was sufficient. No new models, props, textures or procedural items were introduced.

Human review remains for motion quality (including the existing static character pose), body/hand/armour/cart alignment, silhouette and panel readability during ordinary play, and listening to audio in a normal session. Automated visual assertions establish state display and successful rendering, not subjective quality. The benchmark measures this machine and its fixed views, not every population size or play session; long-term arrival balance remains a playtest concern.

Windows testhost locking and long world-only fixtures admitting shoppers produced the two proposed rules in `docs/ticket-rules-proposed.md`. All final checks run sequentially; population behaviour remains covered by Wanderer tests and full-Store persistence checks.
