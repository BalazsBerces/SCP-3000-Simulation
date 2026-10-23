# Ticket 07z: Camping ground Landmark with Furniture dinosaurs

## Decisions

- **No downloaded models.** Every new piece (tree, tent, lantern tent, campfire flame, log seat, hammock, kayak,
  fishing rod, rubber duck, plush bear, trail sign, axe, sleeping bag) is built procedurally from boxes, tubes and
  ellipsoids in `FurnitureMeshes.BuildCamping`, so there is nothing to credit (criterion 8): CREDITS.md and the
  Credits dialog are unchanged, because no CC0 or CC-BY asset was added.
- **Dinosaurs** are voxel shells of existing Furniture types (CushionBin, Armchair, BlanketStack bodies, PlayCube
  accents) built by `DinosaurBuilder`; the seed varies pose, direction and Furniture choice. Main colours come from
  `DinosaurTints` (orange-red T-rex, green Brachiosaurus, teal Triceratops).
- **Turf and paths** are drawn in `store_floor.gdshader` from a small map (`CampTurf.cs`) of the Landmark's
  rectangle, fed from `Landmark.Paths`; the ground is visual only (no collision, no core ground property).
- **Campfire flame** is a Lamp (`CampfireFlame`) whose emission follows the Lamp level, so it is lit only at Closing time.
- **Lookouts**: tree-house deck (8.8 m, reached by a stair of steps round the trunk) and the Brachiosaurus's back
  (reached by a stair on its back).

## Checked

- Core: `CampingGroundTests` (12) pass; full suite 608/608 passed (Release).
- Godot headless build: 0 errors.
- Benchmark (seed 3008): PASSED, average 123 fps open, 156 fps at Closing time, 124 fps far (1% lows 65-93 fps).
- Screenshots (showcase, DiamondLarge layout, Camping ground at 1464, 952): organic turf with winding paths,
  three dinosaurs in three colours, camp pieces; at 22:30 the campfire glows and the rest is dark.

## Existing tests adjusted (a new Landmark shifts Store area and the seed's draws)

- `LampTests`: `LampTypes` lists the new lamp types; `LampsOf` ignores lamps in the dead zone (they are dark by design;
  a WIP generator change lets Displays start inside it).
- `StoreGenerationTests` / `FurnitureLayoutTests`: Department-theme checks count only non-Landmark Furniture, because
  Landmarks stand on any Department's floor.
- `FurnitureRebalanceTests`: table threshold 17_915 -> 17_800 (the Camping ground takes Store area).
- `PointOfInterestGenerator.BuildArmchairHenge`: the fallen upright now lies inside the ring when the outward spot sticks
  out of the site (it was silently dropped).

## Needs human review

- Look of the turf and paths close up and at game scale.
- Whether the dinosaurs read as a T-rex, Brachiosaurus and Triceratops.
- The lit tent silhouette at Closing time, the trail sign lettering, the axe in its tree.
- fps in a normal Store whose seed picks the Camping ground (the benchmark seed 3008 world is not guaranteed to include it).
