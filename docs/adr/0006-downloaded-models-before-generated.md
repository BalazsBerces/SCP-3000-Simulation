# Furniture and props come from downloaded models, not generated geometry

Every Furniture type, prop and surface the game draws (in Points of interest, Landmarks, Departments or anywhere else) uses a real, downloaded model or texture when a usable one exists. First reuse a model or texture already in `game/assets/models` or `game/assets/textures`; only for items that have no suitable one yet do we search the sources already in use, in this order:

- **Poly Haven** (polyhaven.com, CC0): models and textures, through its API
- **ambientCG** (ambientcg.com, CC0): textures only; skip it when looking for a model
- **Sketchfab** (downloadable models, CC-BY or CC0 only), through its API with the token described in `scripts/fetch-models.py`
- **OpenGameArt** (CC0 or CC-BY)

Procedural geometry built from boxes and cylinders is the fallback, used only when none of these has a usable model. We rejected generating everything in code: it is quicker to write, but the 08a/08b Points of interest built that way did not read as a real store, and ticket 08c had to replace them with external models anyway.

## Consequences

- Each new model goes into `scripts/fetch-models.py` and each new texture into `scripts/fetch-assets.py`, so the assets can be fetched again. Every source, author and licence goes into `game/CREDITS.md` or `game/assets/textures/CREDITS.md`.
- Only CC0 and CC-BY assets are allowed; no real store brand may show on any of them.
- When an item is still generated, the ticket's verification doc names it and says which sources were searched.
- The procedure for adding a model (search, download, conversion, wiring, tests) is in [docs/model-workflow.md](../model-workflow.md).
- Downloaded models must still fit the render budget ([ADR 0004](0004-120-fps-render-budget.md)): 1k textures, with reduced geometry where a model is too heavy.
