# Ticket 30c verification: Emergent currency

## Automated

- `CurrencyTests` (facade level): a durable kind wanted by all known Villages becomes currency and is taken in trade beyond need; with no fitting kind (only fresh food wanted, which spoils) no currency appears and excess Goods are refused (pure barter); a flood of the currency kind lowers its per-unit trade value; currency survives save/load. Chronicle line names the Village and Sector.
- `CaravanTests`: barter now takes Goods only up to the Village's need; the rest is carried home.
- Rendered: `--villager-sandbox --sandbox-preset=empty --pause --currency-check=<dir>` places four Villages holding a few Batteries, waits until they take Batteries as currency, checks the map marker text (`Village 0 | takes Batteries`) and the Village panel (`Currency: Batteries (taken beyond need)`), and captures `map.png`, `overview.png`, `panel.png`.

## Decisions

- A Village's region is the Villages on its Known map; a kind becomes its currency when it does not spoil fast, someone in the region holds it, and at least 75% of known Villages (two or more) hold less than they need; it stays while 50% still want it. Re-checked every 500 ticks.
- Currency is worth the higher of the Village's own barter price and the mean price of the known Villages.
- Barter now refuses Goods beyond the receiving Village's need (they go home with the Caravan), so accepting currency beyond need is a real difference.
- No new models or textures: the currency is shown as text on the existing Village markers and panel (no procedural geometry).
- Fixed a pre-existing crash from 30b: `VillagerRenderer._Ready` preloaded hand meshes for every `GearKind`, including Pulley and Winch, which have none; they are now skipped.

## Human review

- Readability of the longer Village marker labels (`Village N | takes Batteries`) when Villages crowd, in the normal and the F4 overview.
