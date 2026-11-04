# Ticket 30b verification: Heavy hauling

Automatic: `HaulingTests` (winch/pulley need fewer lifters, winch lets a pair carry a wardrobe, cart train carries more / slower / louder, save/load) and the Godot build.

Generated (ADR 0006): pulley/winch rig (posts, beam, wheel, drum, crank). Searched: Poly Haven, Sketchfab, general web (winch, pulley); only commercial/royalty-free listings or no licence shown, none CC0/CC-BY. Platform carts reuse the existing `industrial_storage_cart` model.

Needs human visual review (not captured): hoist rig beside a Village that owns a pulley/winch; a train of 2-4 platform carts trailing a Caravan leader.

Shortcuts: no worker-built tower lift yet, the Hoist factor applies to Furniture carrying/team-carry limits and `LiftersNeeded`; the cart train needs no Gear (carts 1-4 on `SendCaravan`); Caravan members' slow pace is not saved, it is reapplied on the next Caravan step.
