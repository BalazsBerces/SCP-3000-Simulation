"""Fetch the CC0 PBR textures the game uses and write them, downscaled, into game/assets/textures.

Sources: Poly Haven (https://polyhaven.com) and ambientCG (https://ambientcg.com), both CC0.
Requires Pillow. Run from the repository root:  python scripts/fetch-assets.py

Each material gets <name>_albedo.jpg, <name>_normal.jpg (OpenGL convention) and <name>_roughness.jpg.
The Department floors are also stacked into vertical atlases (floor_albedo.jpg, floor_normal.jpg,
floor_roughness.jpg), one layer per Department type in the order of the core's DepartmentType enum;
Godot imports them as texture arrays.
"""

import io
import json
import pathlib
import urllib.request
import zipfile

from PIL import Image

SIZE = 512
OUT = pathlib.Path("game/assets/textures")
USER_AGENT = {"User-Agent": "scp3008-simulation-asset-fetch"}

# Department floors, in DepartmentType order.
FLOORS = [
    ("RestaurantAndFoodMarket", "polyhaven", "floor_tiles_06"),
    ("Showrooms", "polyhaven", "laminate_floor_02"),
    ("LightingAndElectrical", "polyhaven", "granite_tile"),
    ("SelfServeWarehouse", "polyhaven", "concrete_floor_worn_001"),
    ("BathroomsAndKitchens", "polyhaven", "marble_tiles"),
    ("PlantSection", "polyhaven", "patio_tiles"),
    ("KidsArea", "polyhaven", "old_linoleum_flooring_01"),
    ("Workspaces", "polyhaven", "dirty_carpet"),
    ("TextilesAndDecor", "polyhaven", "rectangular_parquet"),
    ("Outdoor", "polyhaven", "rectangular_paving"),
    ("DiningAndHallway", "polyhaven", "herringbone_parquet"),
]

# Furniture materials: (name used by the game, source, asset id).
MATERIALS = [
    ("tiles", "polyhaven", "floor_tiles_06"),
    ("restroom_tiles", "ambientcg", "Tiles002"),
    ("oak", "polyhaven", "oak_veneer_01"),
    ("walnut", "polyhaven", "walnut_veneer"),
    ("maple", "polyhaven", "white_maple_veneer"),
    ("fabric", "polyhaven", "leather_white"),
    ("cardboard", "ambientcg", "Cardboard001"),
    ("pine", "polyhaven", "coated_pine"),
    ("teak", "polyhaven", "teak_veneer"),
    ("linen", "polyhaven", "rough_linen"),
    ("painted_metal", "polyhaven", "painted_metal_shutter"),
    ("wool", "polyhaven", "poly_wool_herringbone"),
]


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=USER_AGENT)) as response:
        return response.read()


def polyhaven_maps(asset):
    files = json.loads(get(f"https://api.polyhaven.com/files/{asset}"))
    info = json.loads(get(f"https://api.polyhaven.com/info/{asset}"))

    def load(*keys):
        for key in keys:
            if key in files:
                return Image.open(io.BytesIO(get(files[key]["1k"]["jpg"]["url"])))
        raise KeyError(f"{asset}: none of {keys}")

    size_mm = info.get("dimensions", [1000, 1000])
    return load("Diffuse", "diff"), load("nor_gl"), load("Rough", "rough"), size_mm[0] / 1000.0


def ambientcg_maps(asset):
    archive = zipfile.ZipFile(io.BytesIO(get(f"https://ambientcg.com/get?file={asset}_1K-JPG.zip")))

    def load(suffix):
        name = next(n for n in archive.namelist() if n.endswith(f"_{suffix}.jpg"))
        return Image.open(io.BytesIO(archive.read(name)))

    return load("Color"), load("NormalGL"), load("Roughness"), None


def fetch(source, asset):
    albedo, normal, roughness, metres = polyhaven_maps(asset) if source == "polyhaven" else ambientcg_maps(asset)
    resize = lambda image, mode: image.convert(mode).resize((SIZE, SIZE), Image.LANCZOS)
    return resize(albedo, "RGB"), resize(normal, "RGB"), resize(roughness, "L"), metres


def save(image, path):
    image.save(path, quality=88, optimize=True)
    print(f"  {path} ({path.stat().st_size // 1024} KB)")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    credits = ["# Texture credits", "", "All textures are CC0 (public domain), downscaled to 512 px.", ""]

    for name, source, asset in MATERIALS:
        print(name, asset)
        albedo, normal, roughness, metres = fetch(source, asset)
        save(albedo, OUT / f"{name}_albedo.jpg")
        save(normal, OUT / f"{name}_normal.jpg")
        save(roughness, OUT / f"{name}_roughness.jpg")
        credits.append(f"- `{name}_*`: {asset} from [{'Poly Haven' if source == 'polyhaven' else 'ambientCG'}]({'https://polyhaven.com/a/' if source == 'polyhaven' else 'https://ambientcg.com/view?id='}{asset})")

    layers = {"albedo": [], "normal": [], "roughness": []}
    for department, source, asset in FLOORS:
        albedo, normal, roughness, metres = fetch(source, asset)
        print(department, asset, f"real width {metres} m" if metres else "")
        layers["albedo"].append(albedo)
        layers["normal"].append(normal)
        layers["roughness"].append(roughness)
        credits.append(f"- floor layer {department}: {asset} from [Poly Haven](https://polyhaven.com/a/{asset})")
    for kind, images in layers.items():
        atlas = Image.new(images[0].mode, (SIZE, SIZE * len(images)))
        for i, image in enumerate(images):
            atlas.paste(image, (0, i * SIZE))
        save(atlas, OUT / f"floor_{kind}.jpg")

    (OUT / "CREDITS.md").write_text("\n".join(credits) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
