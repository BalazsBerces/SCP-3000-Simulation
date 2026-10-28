# Ticket 17f: Crops: tended potted plants in the Plant section

Issue: #94. Spec: #1.

## Built

- **Crop Furniture.** `FurnitureType.Crop` is a potted herb that is 0.35 × 0.35 × 0.24 m and weighs 1. Only the Plant section places it:
  - on Greenhouse plant tables, 1–3 per table at a 0.6 chance;
  - loose as a PlantHall accent.
  
  Decorative PottedPlant, LargePlant and PlanterBox stay ordinary Furniture.
- **Crop growth.** `WorldSnapshot.Crops` records Growth, Water, DryDays, Tended and Withered for each Crop piece.
  - A Crop the Store keeps does not change until it is first watered.
  - A tended Crop grows 0.5 per Store day while it has water and loses 1 water per day.
  - A tended Crop that stays dry for 2 Store days withers for good and emits `CropWithered`.
- **Farmers.** Farmers tend the Crops within 24 m of their Village:
  - A farmer with no water fetches 4 waterings from the nearest running Water point it knows (`FetchingWater`), or scouts for one.
  - A farmer waters thirsty Crops (water below 0.5) and harvests ripe ones (`Tending`). A harvest gives 2 FreshFood, which the farmer delivers to the stores, and emits `CropHarvested`.
- **Bringing Crops home.** A Village tending fewer than 4 Crops sends an idle farmer to carry the nearest living Crop from 24–48 m away to a row beside the Village centre, inside the barricade, using the existing Furniture task. A Crop keeps its growth wherever it is carried.
- **Roles.** A Village with living Crops within 48 m names one farmer, taken from its home scavengers. A food shortage no longer turns its farmers into scavengers.
- **Rendering.** The Crop uses the `crop_basil` model. Within 30 m of the focus, each tended Crop shows a head above its pot that grows with Growth and goes yellow when ripe and brown when withered, plus a label ("Crop 41%", "Crop ripe", "Crop withered", "· dry").

## Decisions for this unattended run

- Growth, water and wither rates, yield, ranges and the Kept count are tuning values, so there is no ADR.
- A Crop the Store keeps does not wither: the ticket's "untended Crops wither" applies once a Village has started tending one. Otherwise every Plant section in every Store would be dead within two days. This is recorded in GLOSSARY.md.
- One model, basil, stands for every herb and vegetable Crop.
- Save format **12** adds `Crops`. On load, a Crop entry must match a Crop piece one to one, with finite values in range.

## Asset workflow

ADR 0006 and docs/model-workflow.md were followed. Reuse was ruled out because the existing potted plants are the decorative ones and Crops must look different.

- **Poly Haven:** nothing usable. It has only decorative potted plants, planter boxes and a seeding tray.
- **Sketchfab:**
  - "Stylized basil pot plant" by sauti (CC-BY, 3968 faces): **chosen**.
  - "Basil plant" by ellievaskali (39k faces): too heavy.
  - "Pot de basilic" (319k faces): too heavy.
- **OpenGameArt:** not searched, because a usable model was found first.

The model was downloaded once with `scripts/fetch-models.py`. The source stacks two plants in two pots, so `DROP_NODES` keeps only the large one: 2580 triangles against a budget of 3000. It is credited in game/CREDITS.md.

Generated items: **none** (the growth head is a plain sphere marker, not a model).

## Verification

Logs are under `.ticket-logs/17f-*`.

- `CropTests` covers 7 facade cases:
  - generation places Crops only in the Plant section;
  - a tended Crop yields over Store days;
  - an untended (unwatered) Crop withers and stops growing;
  - without a Water point there is no growth;
  - a Crop 35 m away is carried inside the barricade polygon and harvested;
  - a farmers-only Village with no food delivers FreshFood and nobody scavenges;
  - Crops survive a save/load round-trip.
- `PersistenceTests` now expects version 12.
- Full Release suite: **896 passed, 0 failed, 0 skipped** (4 m 46 s).
- Godot 4.7.2 headless `--build-solutions`: **passed**.
- `--model-check`: **0 failed**. Crop 0 fills its 0.35 × 0.35 × 0.24 m size, has textures, and its licence and credit check out.
- Windowed `--crop-check`: **passed**. It asserts that farmers water and grow a Crop and that the growth labels show. The [1280 px screenshot](screenshots/17f/crops.png) was inspected once.

## Human review and limits

- In the capture, the Known map panel half-covers the Crops. Check the basil next to a decorative potted plant up close, the growth head and label at different stages, and the withered colour.
- Crop balance over many days in a populated Store has not been tuned.
- The crops were not checked inside a generated Plant section on screen.
- Save format 11 and earlier cannot be loaded.

Suggested rule: fetch-models.py assumed every Sketchfab glTF names its buffer `scene.bin`. It now reads the name from `buffers[0].uri`, so a new source does not crash the fetch.
