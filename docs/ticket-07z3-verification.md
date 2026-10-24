# Ticket 07z3 verification: Camping ground as a showroom

Follow-up to 07z2, asked for directly: the camp did not read as a store display and left too much lawn empty. The layout is redone as a showroom. It adds a sun lounger section and a sleeping bag section, doubles the tents, and replaces the flat artificial turf with a real lawn texture. The dinosaurs, the tree house, the caravan and every earlier prop stay.

## What changed

- **Layout.** Straight boardwalk aisles replace the winding paths. A spine runs from the south entrance up to the dinosaurs, and three aisles (front, middle, back) cross it. The seed picks which side the lake and the tent show are on, and varies the aisle positions, the lake and the tent order.
- **Tent show.** There are 16 tents (8 before), every kind at least twice. They stand in four rows of four, each pair of rows facing across the front or the middle aisle, each tent with its price tag; the first one is lantern-lit. The tree house closes the front aisle and the caravan the middle one. Trees are kept out of the tent show.
- **Sun lounger section.** It is south of the middle aisle and faces the lake. It has a row of wooden loungers and a row of white plastic ones, in pairs under beach umbrellas, then a row of striped deck chairs, with price tags.
- **Sleeping bag section.** It runs along the back aisle on the tent side:
  - groups of four sleeping bags laid out on the lawn, each with a display table of 13 rolled-up bags behind it;
  - across the aisle, sleeping bags on mattresses in threes, with lanterns between them.
- **Camp furniture, kayaks and hammocks.** These stand along the back aisle on the lake side:
  - round tables with camping chairs and a lantern;
  - three kayaks;
  - a row of five hammocks, in front of the hammock between two trees.
- **Section banners.** A banner over an aisle names the section whose goods are within 7 m of it ("TENTS", "SUN LOUNGERS", "SLEEPING BAGS", "CAMP FURNITURE & KAYAKS"). Otherwise it shows a slogan. Price tags name the new products.
- **Lawn.** The turf is now ambientCG `Grass004` (CC0), a real mown lawn. It is sampled as randomly offset copies so its repeat does not show, with faint mowing stripes and soft darker or drier patches. Its roughness is at least 0.85, and the Departments' metal transition strips no longer show through it.
- **Dinosaurs.** The triceratops' frill is now a fan of five yellow beds that sweeps down over the neck on both sides. Before, it was a single bed. The yellow kids' chairs on all three backs are now yellow table lamps, which light up at night like every Lamp. The Brachiosaurus has one more lamp at the base of its neck and five up the back of its neck ([night](screenshots/07z3/brachio-night.png)). Each lamp is cast down onto the dinosaur's body and stands on the surface it meets, so none float. That surface is a sofa's or armchair's seat or a bed's mattress, and the lamps stand only on pieces in the main colour. The legs of the Brachiosaurus and the Triceratops now reach up into the body, each with an armchair on end on top. The T-rex's arms sit against its chest ([T-rex](screenshots/07z3/trex.png), [Brachiosaurus at eye level](screenshots/07z3/brachio-eye.png)). Lamp shades now take a colour, but only the dinosaurs' tints give them one; every other lamp stays white ([day](screenshots/07z3/trike-day.png), [Brachiosaurus](screenshots/07z3/brachio-day.png), [side](screenshots/07z3/trike-side.png), [night](screenshots/07z3/dinos-night.png)).
- **Kept.** The three dinosaurs, the tree house (still the only Lookout), the caravan, the campfire with the marshmallow tasting stand and the supplies stand, the lake with its kayak, ducks and fishing rod, the axe in its tree, the plush bear, the trail sign, and the trees and bushes. The tents no longer have bedding beside them, because the sleeping bags now have their own section.

## Models and textures (ADR 0006)

All are downloaded; nothing new is procedural.

| Item | Furniture | Source |
|---|---|---|
| Wooden sun lounger | SunLounger 0 | [Deck chair](https://sketchfab.com/3d-models/deck-chair-f7b31f2190ae425682a873e28bd3244c), kirill, CC-BY |
| White plastic lounger | SunLounger 1 | [Sun lounger 3D](https://sketchfab.com/3d-models/sun-lounger-3d-974e74f55bbd4dc9a85c4a48fc987a18), rodestrela, CC-BY |
| Striped deck chair | SunLounger 2 (tinted) | [deck chair](https://sketchfab.com/3d-models/deck-chair-38de6171cbeb41c5bdc49c07df76ede9), MaX3Dd, CC-BY |
| Beach umbrella | Parasol 1 | [Beach Umbrella](https://sketchfab.com/3d-models/beach-umbrella-1014ad7c8a4c4aa8ba5e88fc6151aa4f), stealth86, CC-BY |
| Rolled sleeping bag | SleepingBag 1 (tinted) | [Camping sleeping bag](https://sketchfab.com/3d-models/camping-sleeping-bag-d2ab6e8a8ffc472e9fca8b920662dafe), Fochdog, CC-BY |
| Lawn | floor shader | [Grass004](https://ambientcg.com/view?id=Grass004), ambientCG, CC0 |

- **SunLounger 0 is used everywhere.** It was a procedural box and now has a model in the Outdoor Department as well. Its spec changed from 0.7 x 1.9 x 0.65 m to 0.62 x 1.95 x 0.8 m, the model's proportions.
- **New variants appear in Outdoor Displays.** These are SunLounger 1 and 2 and Parasol 1. Parasol 0 is still procedural, as before this ticket.
- **Rolled sleeping bag texture.** Its canvas was too dark to take a tint. `fetch-models.py` (`LIGHTEN_FOR_TINT`) turns its albedo into a light grey that keeps the detail.
- **`fetch-assets.py` downloads each material only once.** It no longer overwrites `game/assets/textures/CREDITS.md`, which has hand-made notes; the grass line was added there by hand.
- **Import settings.** All new textures are imported VRAM-compressed with mipmaps.

## Checks

- **Core tests:** 610/610 pass. `CampingGroundTests` were updated for 16 tents in four rows and straight aisles. There is a new test for the lounger and sleeping bag sections.
- **Godot build:** 0 errors.
- **Model check:** 59 models, 0 failures ([report](../.ticket-logs/07z3-model-check.txt)).
- **Screenshots:** [overview](screenshots/07z3/overview.png), [sun loungers](screenshots/07z3/beach.png), [sleeping bags](screenshots/07z3/sleeping.png), [tents](screenshots/07z3/tents.png).

## Known limitations

- The camp now has about 820 pieces. The benchmark world has no Camping ground, so the frame rate cost was not measured.
- The coarse colour check was not rerun for the new models.
