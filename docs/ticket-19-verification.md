# Ticket 19: Castle Blueprints: towers, gatehouse, keep, moat, battlements, heraldry

Issue: #20. Spec: #1.

## Built

- **Curtain wall with Battlements.** A Village's second home ring is its Curtain wall.
  - Its slots alternate tall units (1.8 m and up) and short units (1.2 m at most).
  - It leaves a gap of `2 × Castle.GateHalfWidth` (3.4 m) for the Gatehouse.
  - `Wall.Battlements` and `BlueprintSlot.MaximumHeight`/`Elevation` are new.
- **Castle Blueprints (`Village.Castle`, `CastleStructure`).** They are planned together with the Curtain wall and built once every wall ring is complete, one slot at a time.
  - A stacked slot is built after the slots that carry it (`Castle.Support`), and its piece is set at the height those pieces reach.
  - A stacked piece must not crush what carries it (ticket 18 load capacity).
  - Structures:
    - **Tower** at each ring corner: two pieces of shelving and a watch-lamp (any Lamp) on top.
    - **Gatehouse**: two tall flanking pieces (0.4 m to 1.0 m wide) with a double-door wardrobe (Wardrobe variant 1) across their tops. The passage stays 1.4 m wide and walkable.
    - **Keep** (one per Village) near the centre: two stacks of two showroom storage pieces (wardrobe, bookcase, dresser, sideboard, shoe cabinet, TV bench).
- **Moat (`Village.Moats`).** A ditch 2.5 m outside the Curtain wall, broken by a causeway before the gate.
  - It holds water when it lies on Water ground or within 30 m of a fountain court or Camping lake Water point.
  - It is not dug by labour and does not block movement (decision below).
- **Heraldry (`Village.Heraldry`).** Chosen when a Village plans its first barricade:
  - the field and charge are the two commonest tints among fabric pieces within 30 m;
  - the division is the first one no other Village with those tints bears, so neighbours with the same fabrics still differ.
- **Terrain fit.** A Tower is not planned inside a Pillar, and a Moat run is not planned where its midpoint falls inside a Pillar. Wall rings keep their existing terrain fitting (outlines around Pillars, freeform rings).
- **Persistence.** Castle structures, Moats and Heraldry are saved and loaded, and validated on load.
- **Godot** (`VillagerRenderer.Castle.cs`): the pieces are real Furniture, so they are drawn by `FurnitureRenderer`. This file adds:
  - banners over the Village centre and over each built Tower, Gatehouse and Keep;
  - Moat strips (water blue, or a dark dry ditch);
  - a "Castle" section in the Village panel listing the arms, structure progress and the Moat.
- **Dev check:** `--villager-sandbox --sandbox-preset=empty --pause --castle-check=<dir>` (`DevCapture.Castle.cs`).

## Decisions

- **"The flooded Department".** No such Department exists in the code. The Store's standing water is the fountain court and Camping lake Water points, plus Water ground, so those fill a Moat.
- **Moats are planned, not dug or navigated around.** Digging needs a terrain-editing system that the core does not have. A Moat is a Blueprint of the ditch line, rendered on the floor.
- **Battlement slot heights** come from the slot index (even slots tall, odd slots short), so the facade test can assert the alternation.
- **Banners reuse the camp banner model at 35 % scale.** Its cloth is tinted with the field colour, and the charge is drawn as a division overlay.
- **CastleTests Village** uses the Cautious Personality. Its Furniture yard lies inside, so no yard barricade has to be built before the Curtain wall (see risks).

## Tests

- `CastleTests` (facade): 3 passed.
  - A Village progresses from a barricade to a full Curtain wall with 4 Towers, a Gatehouse with an open passage, a Keep and a Moat. It checks the slot fits, the stacking heights and the save/load.
  - A Moat near the fountain court fills with water.
  - Three Villages bear Heraldry from their local fabrics, and two with the same fabrics differ by division.
- Full core suite: see the closing comment.
- Godot headless build: passed.
- `--castle-check`, windowed: all 8 checks passed (`.ticket-logs/19-castle/report.txt`, `castle.png`).

## Models and assets (ADR 0006)

- **Reused:**
  - the camp banner mesh (`FurnitureType.CampBanner`) for every banner;
  - the existing Furniture models for every castle piece;
  - the tank-water blue for water-filled Moats.
- **Generated:**
  - **Heraldic division overlay** (`VillagerRenderer.DivisionMesh`): flat polygons on the banner cloth, in the charge colour. They are per pale, per fess, per bend, quarterly, cross and chevron.
    - Searched: Poly Haven (no banner or flag models), Sketchfab ("templar warbanner": licence not confirmed and its arms are baked into the texture; an Objaverse medieval banner: no licence shown), OpenGameArt ("war banner": CC-BY-SA 3.0, not allowed; "medieval themed banner": 2D UI art) and ambientCG (textures only).
    - Generated because arms must be recoloured per Village, which no candidate allows.
  - **Moat strip:** a flat box in a plain colour. It is a floor marking, not a model; no search was needed.

## Risks

- **Outside yard barricade.** An outside Furniture yard barricade that cannot be finished blocks the Curtain wall and the Castle. This is the existing 17e behaviour: walls are built in order.
- **Short Battlement slots** on narrow segments (about 0.37 m wide) need pieces of 10 kg or more that are at most 1.2 m tall. A Village without such pieces stalls.
- **Carts wear out mid-build.** A double-door wardrobe needs a cart, so a Gatehouse waits until a Builder has one.
- **Stalled builds slow ticks** (repeated failing pathfinds). This was seen while debugging, and the test avoids it.
- No dedicated test covers the Pillar terrain fitting of Towers and Moats.

## Human visual review

- The castle from above: the Battlements' alternating heights read as crenellations.
- Tower stacks with lamps, and the Gatehouse wardrobe lying across its flankers.
- The banner scale and visibility. A light field tint (tint 0) makes the banner hard to see against the grey floor.
- The division overlay reads as heraldry on both banner faces.
- The water Moat colour and the dry ditch colour against the floor.
- The Village panel "Castle" section.
