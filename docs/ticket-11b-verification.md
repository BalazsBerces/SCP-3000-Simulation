# Ticket 11b verification

Issue: #93. Spec: #1. Review fixed point: `63efa73af070dafbbc33455f01c790aa242e6162`.

## Implementation and unattended decisions

- One Dry spell runs at a time: one Department has slower Restock and another has faster Restock. Only Departments with actual Restock sources are selected. The sole Department supplying food, if there is one, is excluded from slowdown; the one-Department Villager sandbox therefore keeps its normal Restock.
- `RestockConfig.DrySpellIntervalHours` is the mean waiting time between the end of one spell and the next start (default 72 Store hours), sampled uniformly between half and one-and-a-half times that value. Zero disables new spells. `DrySpellDurationHours` defaults to 72 Store hours, and `DrySpellStrength` defaults to 0.875: 12.5% of normal Restock in the affected Department and 187.5% in the other. Strength accepts 0 through 1, including a complete stop. These are starting tuning values; balance tuning remains #85.
- Restock uses its own runtime RNG stream, independent of the Store seed and Weather. `DrySpellStarted` and `DrySpellEnded` carry the Department ids, start/end clocks and strength through their immutable Dry spell record. Running spells are exposed through the read-only world snapshot; old snapshots retain their state.
- Goods accumulate fractional refill credit per Department, with existing per-shelf targets and generation's Sector stock distribution preserved. Furniture return delays also advance at the modified rate; occupancy and Villager sight still gate return. Both rates resume normal speed after expiry, without a catch-up burst.
- The overview Sector panel lists affected Departments touching that Sector, their rate and expiry. It refreshes twice per second, including while the same Sector stays selected. The new percentage formatting uses invariant culture for English text.
- Save/load is conditional on #14, which is still open and has no existing world persistence API. No persistence implementation was added; integrating the running spell, timer, RNG and refill credits belongs with that API.
- No new domain terms, assets, dependencies or irreversible architecture decisions were introduced. GLOSSARY.md already defines Dry spell. No ADR was needed. Spec-review was skipped as instructed. Pre-existing work in scripts/ and .claude/ was left untouched.

## Automated verification

Facade-level `DrySpellTests` cover zero, partial and total slowdown, faster refill elsewhere, normal refill after expiry, start/end events and timestamps, immutable snapshots, repeatability with a runtime seed, variation with another runtime seed, timing independent of the Store seed and Weather, invalid configuration, disabling spells, the sole-food-Department guard, and slower/faster Furniture return.

A 60-Store-day headless facade run verifies many affected Departments across multiple Sectors, paired start/end events without overlapping spells, and food sources outside every affected Department. Its Goods refill rate is zero to isolate scheduling from stock changes; actual refill behavior is tested separately at nonzero rates.

Targeted Dry spell checks: **17 passed** in Release. Earlier combined Restock/Dry spell checks: **34 passed**, before the final extra cases were added. The initial red test failed on the missing API before implementation. Full command output is local under `.ticket-logs/11b-*`.

`dotnet build Scp3008.sln -c Release`: **passed**, 0 errors; one existing xUnit2031 warning in `NeedsTests.cs:144`. Godot 4.7.2 .NET console `--headless --path game --editor --build-solutions --quit`: **exit 0**, empty error log.

Final whole Release suite: **820 passed, 0 failed, 0 skipped**, 7 min 1 s, exit 0. Run once after the final code changes, immediately before committing; full output is `.ticket-logs/11b-full-suite.log`.

## Rendered verification and human review

Windowed Godot command: `--path game --resolution 1280x720 -- --pause --pillars=3x3 --dry-spell-check=<directory>`, with a 300-second timeout. The check advances the real simulation until its first scheduled spell, clicks the affected Sector through the overview's normal click handling, verifies the live label, captures it, advances through expiry and verifies that the same panel updates to `Dry spells: none`.

Final run: **exit 0**, empty error log. Screenshot `.ticket-logs/11b-render/dry-spell.png` is 1280 by 720 and was inspected once. The Dry spell row is readable, with Department identity, percentage and expiry; the selected Sector is highlighted. The check's report is `.ticket-logs/11b-render/report.txt`.

Human review remains for longer interactive play, selecting Departments spanning several Sectors, and judging the default frequency/strength. The pre-existing Known map overlay overlaps part of the Sector panel, and pre-existing counted labels contain incorrectly encoded multiplication characters; this ticket preserves those strings. Neither obscures the new Dry spell row in the captured view. No audio or rendering changes were made; no audio listening check or full 1080p fps benchmark was run.

Suggested rule: Godot FindChildren filters on engine class names and does not match a C# script class such as SectorInspector. Leave the type filter empty and use OfType<T>() for scripted nodes; format numeric capture assertions with an explicit culture.
