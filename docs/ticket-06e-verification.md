# Ticket 06e verification: Smaller, patchier Departments

- Sites on the standard Store: 48 → 144 (still scaled by area on larger Stores), minimum spacing 110 m → 55 m.
- Sites of the same type keep 190 m apart where room allows, so same-type Departments rarely merge.
- About one site in twenty (never Restaurant/food market, Plant section or Lighting & electrical) is a large Department (×2.4 size).
- Standard Store, six seeds (median / largest Department across, Restaurant and Plant floor share): ~122–130 Departments, median 97–106 m, largest 331–557 m, 1.4–2.8% and 1.9–2.5%.
- `DepartmentSizeTests` cover count, median, large Departments, rare merging, scarce food/Plant, determinism and seams. The Lighting & electrical test now allows more (still small) regions, since the site count tripled.
- Tests: 251 passed. Godot `--headless --build-solutions --quit`: exit 0.
- Benchmark ([log](screenshots/06e/benchmark.txt)): open 194.0 / 86.0, Closing time 188.3 / 83.8, far 128.8 / 94.1 fps (average / 1% low); PASSED, MSAA 4x + TAA on.
- F4 overview, seed 3008: [before](screenshots/06e/before-overview.jpg), [after](screenshots/06e/after-overview.jpg).

Needs human review: whether the new pattern looks right in the overview and at eye level.
