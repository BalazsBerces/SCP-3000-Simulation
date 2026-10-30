# Ticket 22a verification: Fridges keep food while powered

## Automated (facade-level, `FridgeTests`)

- Food put back into a Fridge on display does not spoil (Store power).
- A Fridge moved to a Village protects Village food only while it draws power: it uses one Battery from the Village stores every 4 Store-hours of load, or a Villager holding a hand-cranked generator stays home cranking (and tiring) for it when no Battery is left; food spoils once neither is available.
- An unpowered Fridge slows spoilage no more than the Storehouse: Village food spoils exactly as without the Fridge ("only a cupboard").
- Builders with a cart (any available Builder, not only the first) bring a Fridge within 48 m home when the Village has Batteries to spare (more than one) or a generator; the Fridge becomes the Village's (`FridgePower.Home`) and the Chronicle records it once.
- Fridge charge, Home and Village food continue identically after a save/load round-trip. The save version is now 16; as in earlier tickets, older saves are refused.

## Decisions

- A home Fridge keeps cold up to one Storehouse shelf of food (`Storehouse.PerShelf`), fast-spoiling food first.
- A Fridge built into a Blueprint slot (a wall, castle and so on) has no home and is never powered; a Fridge being hauled keeps its previous home until it is set down.
- Like a Lamp, a Fridge leaves Store power once it is moved at all.

## Review fixes (stage review, tickets 17–22a)

- `GatherFridge` never ran for Village 0: its quarter-hour throttle (tick ≡ id mod 125) never coincided with construction ticks (tick ≡ 1 mod 50). The earlier test passed only because a wall builder hauled the Fridge into a barricade slot. The throttle was removed and the tests now assert `Home`.
- Slot-built Fridges no longer count as home Fridges (cold room, Battery drain, haul exemption).
- The version-15 save migration was dropped: it would have loaded moved Fridges as permanently Store-powered.
- The generator cranking check now runs after the cheap position and needs checks.

## Rendered check

Windowed `--villager-sandbox --sandbox-preset=empty --pause --fridge-check=<dir>` passes: the Builder hauls the Fridge to the Village at (18, 106) beside the Storehouse, the Chronicle records it, and `fridge-home.png` shows the Fridge next to the Villager and cart.

## Models

No new model: the Fridge uses the existing Furniture mesh (`FurnitureMeshes`). No procedural geometry was added.

## Needs human review

- The look of the Fridge standing in a Village (the capture is a top-down 16 m view).
