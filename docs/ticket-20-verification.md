# Ticket 20: Tiers, wall damage and daytime repair

Issue: #21. Spec: #1.

## Built

- **Tiers.** `BlueprintSlot.Tier` (1-3), `Strapped`, `Backfilled`; `Blueprint.Tier` is the weakest piece's Tier (0 until complete).
  A piece's own Tier comes from its weight (under 25 kg: 1, under 100 kg: 2, else 3); Strapping and Backfill add one each.
- **Upgrades** (`Simulation.Tiers.cs`): once every home wall and the Castle are complete, a free Builder upgrades the weakest piece:
  Strap (2 Metal), else Backfill (3 Foam), else swap in a sturdier loose piece that fits the slot (the old one is set down by the Village centre).
- **Blows.** `Scenario.StrikeWall` stands for a Staff blow (the Staff do not attack walls yet): 0.25 Damage, halved by Strapping, x0.6 by Backfill; noise 30 m, x0.4 with Backfill. A piece at Damage 1 breaks and its slot opens for rebuilding.
- **Repair.** While the Store is open a Builder mends 0.25 Damage per step.
- **Save/load.** Slot fields and piece Damage persist; Tier range is validated on load.
- **Godot.** `VillagerRenderer.Walls.cs`: two metal bands on a Strapped piece, a soft slab behind a Backfilled one. Damage is the existing darkening. Dev check: `--villager-sandbox --sandbox-preset=empty --pause --wall-check=<dir>`.

## Decisions

- A "Tier" label for a piece is derived from its weight rather than a Blueprint Tier catalogue.
- The blow is a facade method because no Staff attack exists yet.
- A swap has no hauling of the old piece to the yard (shortcut comment in code).

## Generated items

Strap bands and the Backfill slab are plain boxes (about 5 cm bands, a 20 cm slab), drawn procedurally. No source was searched for them; they are shapes, not models.

## Needs human review

Look at the strap bands, the Backfill slab and the damage darkening in a window (the capture shows the bands clearly, the slab and damage only faintly from that angle).

## Risks

- A piece broken by a blow lies at its slot; the rebuild did not finish in a test that broke a piece and then waited for upgrades.
