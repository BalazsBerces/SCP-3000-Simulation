# Ticket 31 verification: Raids and Captives

## Automated (facade-level, `RaidTests`)

- A successful Raid moves stores (and is chronicled); Raids wound, and a second Raid kills the wounded.
- A Raid takes a Pillar site; a Raid seizes the fountain court and a Cafeteria (showcase worlds).
- A Raid takes Captives, who are absorbed into the captors' Village after `CaptiveAbsorbTicks`.
- One Raid takes from an outside Furniture yard and the open pile but is driven off at the defended Storehouse.
- Raiding likelihood follows Personality (Raiders raid repeatedly, Traders never; `RaidChance` ordering).
- Raids and Captives survive a save (save version 31).

## Decisions made in the unattended run

- **Holdings** are the territory: a Village takes the unheld Pillars, fountain courts and Cafeterias within 40 m and its 3 nearest food shelves within 20 m; afterwards they change hands only in Raids. Holding a Pillar does not (yet) stop another Village planning a scaffold on it.
- **Guard** per place, in defenders present (unwounded, within 40 m of home): outside yard x0.25, open pile x0.5, Storehouse / people / near Holdings x1 (x2 behind a complete home wall), far Holdings x0.5. Raiders (at most 4, one always stays home) win a place only when they outnumber its guard. Deterministic, so tests are exact.
- **Blows:** the losing side takes one blow per two winners (wounded first become Taken only after the unwounded are hit); a close win costs the Raiders one wounded.
- **Captives** join the captors' Village at once (so membership and feeding stay consistent) and are listed in `Village.Captives` until absorbed half a Store day later; they are never sent raiding.
- **Dispatch:** every 500 ticks while the Store is open, by `RaidChance` (Raiders 0.5, Bold 0.05, Cautious/Builders/Isolationists 0.01, Traders 0) on runtime stream 5. Starving Villages go for a food Holding, outnumbered ones for Captives, the rest for stores.
- An outside yard's pieces are carried off and broken up for their Materials (one per Raider).

## Needs human review

- Village panel: the new line `Holds: ... | Captives: N` under the currency (text only; no new models, props or textures, so no model search was needed).
- Raiders walk with the existing Villager figures (action `Raiding`); there is no dedicated Raid marker.
