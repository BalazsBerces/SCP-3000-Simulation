"""Fetch the external Furniture models the game draws (ticket 08c) into game/assets/models and write game/CREDITS.md.

Only CC0 and CC-BY models may be used; every model's source, author and licence goes into CREDITS.md,
which the game's Credits button shows. Requires Pillow. Run from the repository root:

    python scripts/fetch-models.py

Poly Haven models (CC0) are saved as their 1k glTF (with textures/). The bathroom models come from
loafbrr_1's "Toilets" pack (CC0, OpenGameArt): its glTF binary carries no textures, so each chosen
object is written as its own glTF with the pack's 1k Diffuse, Normal and AoRM maps (ORM order:
occlusion, roughness, metallic) attached, converted to JPEG.
The game maps each model to a Furniture type and variant and fits it to the core's size
(game/scripts/FurnitureModels.cs).
"""

import io
import json
import pathlib
import struct
import sys
import urllib.request
import zipfile

from PIL import Image

OUT = pathlib.Path("game/assets/models")
CREDITS = pathlib.Path("game/CREDITS.md")
USER_AGENT = {"User-Agent": "scp3008-simulation-asset-fetch"}

TOILETS_ZIP = "https://opengameart.org/sites/default/files/toilets_fbx_gltf_blend.zip"
TOILETS_PAGE = "https://opengameart.org/content/toilets"

# (folder, source, asset or root node, title, used for).
MODELS = [
    ("ArmChair_01", "polyhaven", "ArmChair_01", "Arm Chair 01", "Armchair"),
    ("sofa_02", "polyhaven", "sofa_02", "Sofa 02", "Sofa"),
    ("Sofa_01", "polyhaven", "Sofa_01", "Sofa 01", "Sofa, two-seat loveseat variant"),
    ("sofa_03", "polyhaven", "sofa_03", "Sofa 03", "Sofa, large three-seat variant"),
    ("painted_wooden_chair_01", "polyhaven", "painted_wooden_chair_01", "Painted Wooden Chair 01", "Chair"),
    ("dining_chair_02", "polyhaven", "dining_chair_02", "Dining Chair 02", "Chair, high-back variant"),
    ("GreenChair_01", "polyhaven", "GreenChair_01", "Green Chair 01", "Chair, carver variant with arms"),
    ("toilet", "toilets", "Toilet_Elongated_C", "Toilets pack: Toilet_Elongated_C", "Toilet"),
    ("washbasin", "toilets", "Sink_C", "Toilets pack: Sink_C", "Vanity unit (pedestal washbasin)"),
]

# Toilets pack material -> its texture folder in the pack.
TOILET_TEXTURES = {
    "Toilet_Elongated_A": "Toilet_Elongated",
    "Toilet_Elongated_Seat_Mat": "Toilet_Elongated_Seat",
    "Toilet_Flush_Box_Mat": "Toilet_Flush_Box",
    "Toilet_pipes_flush_Mat": "Toilet_pipes_flush",
    "Toilet_Round": "Toilet_Round",
    "Toilet_Round_Seat_Mat": "Toilet_Round_Seat",
    "Sink_A_Mat": "Sink_A",
    "Sink_B_Mat": "Sink_B",
    "Sink_C_Mat": "Sink_C",
}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=USER_AGENT)) as response:
        return response.read()


def polyhaven(folder, asset):
    files = json.loads(get(f"https://api.polyhaven.com/files/{asset}"))["gltf"]["1k"]["gltf"]
    info = json.loads(get(f"https://api.polyhaven.com/info/{asset}"))
    target = OUT / folder
    target.mkdir(parents=True, exist_ok=True)
    (target / f"{folder}.gltf").write_bytes(get(files["url"]))
    for path, entry in files["include"].items():
        (target / path).parent.mkdir(parents=True, exist_ok=True)
        (target / path).write_bytes(get(entry["url"]))
    return ", ".join(info["authors"].keys()), f"https://polyhaven.com/a/{asset}", "CC0"


def toilets(pack, folder, root):
    data = pack.read("glb/Toilets.glb")
    json_length = struct.unpack("<I", data[12:16])[0]
    gltf = json.loads(data[20:20 + json_length])
    binary = data[20 + json_length + 8:]
    nodes = gltf["nodes"]

    # Keep only the root's subtree, with the root at the origin.
    keep, order = {}, []

    def visit(index):
        keep[index] = len(order)
        order.append(index)
        for child in nodes[index].get("children", []):
            visit(child)

    visit(next(i for i, n in enumerate(nodes) if n["name"] == root))
    meshes = sorted({nodes[i]["mesh"] for i in order if "mesh" in nodes[i]})
    new_nodes = []
    for i in order:
        node = dict(nodes[i])
        if "children" in node:
            node["children"] = [keep[c] for c in node["children"]]
        if "mesh" in node:
            node["mesh"] = meshes.index(node["mesh"])
        new_nodes.append(node)
    new_nodes[0].pop("translation", None)
    gltf["nodes"] = new_nodes
    gltf["meshes"] = [gltf["meshes"][m] for m in meshes]
    gltf["scenes"] = [{"name": root, "nodes": [0]}]
    gltf["scene"] = 0

    # Attach the pack's textures to the materials this object uses.
    target = OUT / folder
    (target / "textures").mkdir(parents=True, exist_ok=True)
    used = sorted({p["material"] for m in gltf["meshes"] for p in m["primitives"] if "material" in p})
    images, textures = [], []

    def texture(kind, image_name, mode):
        source = f"Textures/{image_name}/{image_name}_{kind}_1K.png"
        uri = f"textures/{image_name}_{kind.lower()}.jpg"
        Image.open(io.BytesIO(pack.read(source))).convert(mode).save(target / uri, quality=92, optimize=True)
        images.append({"uri": uri})
        textures.append({"source": len(images) - 1})
        return {"index": len(textures) - 1}

    for index in used:
        material = gltf["materials"][index]
        image_name = TOILET_TEXTURES[material["name"]]
        material["pbrMetallicRoughness"] = {
            "baseColorTexture": texture("Diffuse", image_name, "RGB"),
            "metallicRoughnessTexture": texture("AoRM", image_name, "RGB"),
        }
        material["normalTexture"] = texture("Normal", image_name, "RGB")
    # The pack marks some materials unlit; they are lit like every other material.
    for material in gltf["materials"]:
        material.pop("extensions", None)
    gltf.pop("extensionsUsed", None)
    gltf["images"], gltf["textures"] = images, textures
    gltf["buffers"] = [{"uri": f"{folder}.bin", "byteLength": len(binary)}]
    (target / f"{folder}.bin").write_bytes(binary)
    (target / f"{folder}.gltf").write_text(json.dumps(gltf, indent=1), encoding="utf-8")
    return "loafbrr_1", TOILETS_PAGE, "CC0"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    pack = None
    lines = [
        "# Credits",
        "",
        "External assets in the game. Only CC0 (public domain) and CC-BY licences are used.",
        "",
        "## Furniture models",
        "",
        "Fetched by `scripts/fetch-models.py` into `game/assets/models`; each is fitted to the core's size of its Furniture type.",
        "",
    ]
    for folder, source, asset, title, used_for in MODELS:
        print(f"{folder} ...", file=sys.stderr)
        if source == "polyhaven":
            author, url, licence = polyhaven(folder, asset)
            where = "Poly Haven"
        else:
            if pack is None:
                pack = zipfile.ZipFile(io.BytesIO(get(TOILETS_ZIP)))
            author, url, licence = toilets(pack, folder, asset)
            where = "OpenGameArt"
        lines.append(f"- {used_for}: [{title}]({url}) by {author}, {where}, {licence} (`models/{folder}`)")
    lines += [
        "",
        "## Textures",
        "",
        "PBR textures for the procedural Furniture and the floors are listed in `game/assets/textures/CREDITS.md`; all CC0, from Poly Haven and ambientCG.",
        "",
    ]
    CREDITS.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
