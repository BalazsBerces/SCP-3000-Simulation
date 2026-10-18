# Adding an external model: the workflow

How every downloaded model gets into the game. The decision to use downloaded models is [ADR 0006](adr/0006-downloaded-models-before-generated.md); the render budget is [ADR 0004](adr/0004-120-fps-render-budget.md). Follow the steps in order; do not invent your own tooling.

## 1. Reuse first

Look in `game/assets/models` and `game/assets/textures` for a model that already fits (a variant, a recolour with `Tinted`). Only if none does, search.

## 2. Search: a short list, then decide

Search the sources of ADR 0006 in its order (Poly Haven, Sketchfab, OpenGameArt; ambientCG for textures only). Stop at **at most 3 candidates per item** and pick one. Do not keep searching for a better one.

A candidate must pass all of these before it is downloaded:

- **Licence:** CC0 or CC-BY only. Sketchfab: check the model's licence field, not the page text. No real store brand visible on it.
- **One object:** the model is the item, not a scene. A pack with plates, cutlery and napkins is only usable if the item can be written out on its own (step 4).
- **Size on disk:** the archive is under about 100 MB; if it is far bigger, the textures are too heavy to be worth it.
- **Looks right:** open its preview image; the item is recognisable at the distance the player sees it.

## 3. Download once

Add the model to `MODELS` in `scripts/fetch-models.py` (folder, source, id, title, what it is used for). Textures go to `scripts/fetch-assets.py`. Then run, from the repository root:

    python scripts/fetch-models.py

The script is the only thing that downloads. It skips a folder whose `<folder>.gltf` already exists, writes the 1k JPEG textures, and updates `game/CREDITS.md`.

For inspecting a source (mesh names, bounds, triangle counts, texture colours), read the files the script wrote under `game/assets/models/<folder>/`, or keep the raw archive in `.ticket-logs/<name>/` after one download. **An inspection script never downloads.** If you need data the script does not keep, extend `fetch-models.py` or save the raw archive once, then read that.

## 4. Convert

`fetch-models.py` already does these; do them there, not in throwaway scripts:

- keep only the meshes of the item (drop bowls, cutlery, tissues, floors);
- bake the item's own transform, so the origin is at the bottom centre and Y is up;
- textures: albedo, normal and ORM (occlusion, roughness, metallic), 1k, JPEG;
- one material per distinct surface, named.

If a model needs a special treatment (a pack, a part to remove), add it to `fetch-models.py` as a new source kind with a comment saying why, so the next model of that kind reuses it.

## 5. Wire it into the game

Add a `Model` entry to `FurnitureModels.cs`: type, variant, folder, `Yaw` (the turn that brings its front to +Z), triangle budget, `Tinted`, author, licence, source URL. The game fits the model to the core's size of that Furniture type and reduces it to the budget at load time, so you do not scale or decimate the files yourself.

Budgets (triangles per model): chair 1500, seat 3500, sofa 4500, bathroom 8000. For a new class, choose a budget from how many of that item can be on screen at once, and write the reason next to the constant.

New Furniture type: add it to `ModelCheck.HeroTypes`.

## 6. Test

Run these in order; all must pass.

1. `dotnet test tests/Scp3008.Core.Tests -c Release` (the core suite).
2. `Godot --headless --path game --build-solutions --quit` (build).
3. In a window (not headless), after `--`:
   - `--model-check=<file>`: every variant has a model; each fits its size within 1 mm and its budget; the coarse mesh stays inside and passes the silhouette check; every material has albedo, normal and roughness; the licence is CC0 or CC-BY; `CREDITS.md` lists it.
   - `--coarse-colour-check=<dir>`: the coarse mesh has the same colours as the full one, part by part; look at the contact sheets it writes.
   - `--silhouette-check=<file>` for shapes that are not boxes.
4. Look at it once: `--capture=<png>` with `--poi=<name>` or `--furniture-tour=<dir>`, at most 1280 px wide. Judge the picture once; do not recapture the same view.
5. The 120 fps check of ADR 0004 for the stage end ticket (`--fps-report`).

Every Godot run has a time limit; a run that hits it is a failure to fix.

## 7. Credits and the ticket's verification doc

- `game/CREDITS.md` (textures: `game/assets/textures/CREDITS.md`): source, author, licence, link. `fetch-models.py` writes it; check that your model is in it.
- The ticket's `docs/ticket-NN-verification.md` names every item that is still generated (and which sources were searched), and lists what needs a human look.

## What the ticket does not have to decide

Search order, licences, texture size, folder layout, budgets, yaw convention, tests. These are fixed here. A ticket only names **which items** need models and any exception to the above.
