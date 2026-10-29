# Ticket 17c: Dwellings

Issue: #81. Spec: #1. Fixed point: `1a2081c55a2fa779727bcc2ed28c176d6a193aca`.

## Scope and decisions

The simulation owns Dwelling Blueprints, construction, per-piece reinforcement, sleeping-place allocation, rest and morale, and wall expansion. The Godot layer reads snapshots for Furniture, labels and the Village panel; the dev check uses the Scenario builder. Tests use that same facade.

The unfinished Dwelling work present at the fixed point was completed in the core, its persistence and construction callers, facade tests, renderer and dev capture. These required ticket paths are included; the pre-existing ticket runner scripts and `.claude` files remain untouched and excluded.

- A tent uses an existing curtain rack and rug; a house uses three tall wood-panel wall pieces, a wardrobe door and two wide tables for its roof. Builders haul each piece, then fit the cover: 0.1 Store hours for a tent, 2 hours for a house.
- Cautious Villages share communally; bold Villages reserve one place per personal Dwelling; builders reserve each Dwelling to a membership pair. Family relationships do not yet exist, so pairs follow membership order (the code marks this limit). Changing Personality re-evaluates admission.
- Houses, tents and open bedding provide progressively less rest and morale. A usual place wins ties in quality; occupied or reserved places are unavailable. Bare-floor rest loses morale.
- Automatic housing starts only when a complete local Furniture set is available, so unfinished houses do not consume a Village's wall supplies. Explicit scenario Blueprints can remain unfinished.
- Sites leave room for the Keep, Furniture yard and old walls. Clearance is checked against every edge of a round, square or freeform wall, including a shelving anchor; housing pressure plans a wider ring while retaining the old one during construction.
- Save format 14 includes Dwellings, fitting progress, reservations, upgrades and usual sleeping places, with reference validation. Format 13 saves are rejected with the existing incompatible-version message; no migration was added.
- No ADR: these are reversible placement, capacity and Personality tuning choices within the existing architecture.

## Assets

Reuse was checked in `game/assets/models`, `game/assets/textures`, `FurnitureModels` and `FurnitureMeshes`. Dwellings assemble existing Furniture: curtain racks, rugs, wardrobes, bookcases, dining/meeting tables. The rug and dining-table models already have their sources and licences in `game/CREDITS.md`; the other pieces retain their existing rendering. Strapping and Backfill reuse the existing wall presentation at the Blueprint elevation.

No new model, prop, texture or generated geometry was introduced, so no asset download or new fallback geometry search was needed. Newly generated items: none.

## Verification

Full command output is under `.ticket-logs/17c-implementation/` (ignored local evidence).

Facade coverage in `DwellingTests` checks quality and morale ordering; house/tent/bed/floor allocation and usual-place reuse; tent construction speed and two house Furniture sets; communal, personal and family admission and Personality changes; complete expanded walls; shelving-anchor clearance; save/load during construction and sleep; invalid references; loss of cover; and per-piece upgrades.

- Final `DwellingTests`: 15 passed, 0 failed (8 s), including the moved-bed and lost-cover regressions.
- Construction, Dwellings and persistence integration run: 40 passed, 0 failed (1 m 51 s). The earlier neighboring Needs and Castle checks passed; their initial two Blueprint failures were fixed by requiring a complete Furniture set before automatic housing starts.
- `dotnet build Scp3008.sln -c Release`: passed, 0 warnings, 0 errors.
- Godot 4.7.2 .NET `--headless --path game --build-solutions --quit`: exit 0, no stderr errors.
- Final windowed `--villager-sandbox --sandbox-preset=empty --pause --dwelling-check=<absolute directory>`: exit 0, all nine checks passed. It builds a surrounding barricade (105/105 pieces), checks 2 house sleepers, 2 tent sleepers, 1 open-bed sleeper, 1 floor sleeper and the panel's 4 covered places for 6 Villagers. The final 1280 × 720 capture in `render-final/dwellings.png` was inspected once: both Furniture assemblies and their sleeping labels are visible inside the walls, with the Village panel open. The initial capture was also inspected once, at a different camera angle.
- Complete Release suite immediately before commit: **925 passed, 0 failed, 0 skipped** (5 m 27 s); full output: `full-suite.log` in the evidence directory.

## Human review

At the stage-end visual review, judge whether the existing Furniture assemblies read clearly as an improvised tent and patchwork house, and check their labels and panel at normal play zooms. The sandbox capture verifies visibility and counts, not the final art direction. Full-Store performance, audio and the stage-end fps benchmark are outside this ticket; no audio changed.
