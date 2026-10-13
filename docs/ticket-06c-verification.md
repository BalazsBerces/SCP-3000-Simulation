# Ticket 06c verification

- Base fog and volumetric haze are 0 in every light state (`StoreAtmosphere.Describe()` prints `base fog 0,0000, haze 0,0000`); the Lamp halo shader no longer has haze or distance density.
- Edge fade: depth fog 480–550 m from the camera (draw distance ~600 m, last chunks drop out from ~555 m); Lamp halos fade over the same range. Lifted in the F4 overview.
- Fog banks unchanged (density and colour kept). Core tests: 246 passed.
- Captures (open 12:00 / night 23:00, zoom 45 / 220, with / without a Fog bank): `docs/screenshots/06c/before` and `after`.
- Benchmark (`docs/screenshots/06c/benchmark.txt`): open 198 fps avg / 83 low, Closing time 201 / 83, far 131 / 98; MSAA 4x + TAA on; PASSED.
