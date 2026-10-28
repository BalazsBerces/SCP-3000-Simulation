# Ticket 18: Structural stability and collapse

Issue: #19. Spec: #1.

## Built

- **Weight, load capacity and damage.** Each piece already had a weight (`FurnitureResources.Weight`).
  - `FurniturePiece.Damage` (0 whole, 1 wrecked) is new.
  - `Stability.LoadCapacity` is new:
    - most Furniture carries ten times its weight;
    - cardboard boxes carry 60 kg, flat-pack boxes 300 kg, pallet racks 3000 kg, mattresses 400 kg and Lamps 5 kg;
    - fixed and flat pieces never give way.
  - Damage lowers the capacity to `capacity × (1 − Damage)`.
- **Stability rules (ADR 0002, rule-based).** A piece:
  - stands on the floor;
  - rests on the pieces whose tops meet its bottom (within 5 cm) under its footprint, passing its weight and the load on it to them in equal shares;
  - or hangs where it was put (nothing under it, and it did not rest on a piece that just moved).
- **Collapse.** When pieces move, are placed, are removed, are restocked or are damaged, the structures touching the change are checked at the next tick, within a 20 m region.
  - The lowest overloaded piece gives way (its Damage becomes 1).
  - Everything left with nothing under it falls to the floor:
    - it is thrown out from where the collapse began;
    - a tall piece topples onto its front or back;
    - it is turned a little;
    - the fall damages it by 0.1–0.6, depending on the height.
  - A carried piece is ignored.
  - A fallen built piece is unbuilt, and its Furniture tasks are cancelled.
  - The tick emits `StructureCollapsed` with each piece's `From` and `To` pose and the broken ids.
- **Chronicle.** A collapse of 3 or more pieces gets a Chronicle entry. It names the Sector, the Landmark ("part of the rack canyon") or "a stack of Furniture", and the reason the collapse began.
- **Facade.**
  - `ScenarioBuilder.PlaceFurniture(..., elevation)`.
  - `ScenarioBuilder.DamageFurniture(id, damage)`.
  - Save format **13** adds `Damage`. The load check rejects values outside 0..1.
- **Presentation.** `FurnitureRenderer.Collapse.cs` animates each fallen piece from its old pose to its resting pose:
  - it falls under gravity (duration √(2h/g)) while it tips (a slerp that eases in), and the higher pieces start later, so the stack comes down from the bottom up;
  - it bounces once as it lands;
  - while a piece falls, the static instance groups leave it out.
  - Damage darkens a piece's tint by up to 45 %.
  - No new assets were needed.

## Decisions for this unattended run

- **A Store stands as generated.** Checks run only around changes, so an untouched world never collapses on its own.
  - I checked every piece of the seed 3008 and 7 Stores, the Landmark showcase and the Point of interest showcase as if all of them had changed. With the final rules, 0 pieces broke and 0 fell.
- **Landmark pieces never give way under load.** Before this rule, the check broke hundreds of pieces in authored heaps whose loads far exceed any believable capacity: the clock wall, the ruined Village, the box fort and the mattress mountain.
  - They still fall when their support is taken away, which is how the rack canyon collapses.
  - The rule is recorded under Collapse in GLOSSARY.md.
- **Pieces with nothing under them hang.** Wall clocks, mirrors, notice boards and the Ball pit nets are mounted. A piece that loses the piece it rested on still falls.
- Capacities and fall offsets are tuning values, so there is no ADR. ADR 0002 already covers the rule-based approach.
- Fallen pieces land on the floor and do not stack on or break what they land on.
- Restock already refuses to return a piece while moved pieces cover its place. A removed piece whose load fell therefore stays missing until the fallen pieces are cleared.
  - `DrySpellTests` now removes the pieces resting on the ones it takes away.
- Checks scan every piece's cell; the code marks this with `shortcut:` until it shows in the tick time.

## Tests

- `StabilityTests` (facade): 7 tests.
  - Overload: a wardrobe on a coffee table breaks the table, and the wardrobe falls and topples.
  - Within limits: a lamp and an armchair on a dining table stay.
  - Damage lowers the load capacity.
  - Unsupported pieces fall and hung pieces stay.
  - Removing the bottom rack of a rack canyon stack brings the racks above to the floor, with a Chronicle entry.
  - A generated Landmark showcase stands when pieces are placed in it.
  - Damage survives save and load.
- Full core suite in Release, and the Godot headless build.
- Rendered check: `-- --villager-sandbox --sandbox-preset=empty --pause --collapse-check=<dir>`.
  - It brings down a three-wardrobe tower topped by a box and overloads a coffee table.
  - It asserts that 4 pieces fall, the table is wrecked, the falls finish and the Chronicle has an entry.
  - It captures `collapse-before`, `collapse-falling` and `collapse-after`.

## Human review

- Whether the fall looks believable at play speed: timing, the bottom-up stagger, the bounce, the toppling direction.
- How a large rack canyon collapse looks, with dozens of racks and boxes landing on the floor at once.
- Whether the damaged darkening reads well.
