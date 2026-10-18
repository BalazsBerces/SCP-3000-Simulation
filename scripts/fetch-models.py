"""Fetch the external Furniture models the game draws (ticket 08c) into game/assets/models and write game/CREDITS.md.

Only CC0 and CC-BY models may be used; every model's source, author and licence goes into CREDITS.md,
which the game's Credits button shows. Requires Pillow. Run from the repository root:

    python scripts/fetch-models.py

Poly Haven models (CC0) are saved as their 1k glTF (with textures/). The bathroom models come from
loafbrr_1's "Toilets" pack (CC0, OpenGameArt): its glTF binary carries no textures, so each chosen
object is written as its own glTF with the pack's 1k Diffuse, Normal and AoRM maps (ORM order:
occlusion, roughness, metallic) attached, converted to JPEG.
Sketchfab models (CC-BY) need a Sketchfab API token (sketchfab.com/settings/password) in the
SKETCHFAB_API_TOKEN environment variable or the file ~/.sketchfab-token; their textures are
scaled down to 1k JPEGs.
The game maps each model to a Furniture type and variant and fits it to the core's size
(game/scripts/FurnitureModels.cs).
"""

import io
import copy
import json
import os
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
    ("fabric_sofa", "sketchfab", "be39bf64cf4c4a02b55a0ef1d40e1fbb", "Modern Minimalist Fabric Sofa", "Sofa and its large three-seat variant"),
    ("fabric_loveseat", "sketchfab", "5a1ccfe43ffd4f1883abe4dca7d2b3bb", "Modern Fabric Sofa - Low Poly / Game Ready", "Sofa, two-seat loveseat variant"),
    ("sofa_02", "polyhaven", "sofa_02", "Sofa 02", "Sofa, leather variant (rare)"),
    ("painted_wooden_chair_01", "polyhaven", "painted_wooden_chair_01", "Painted Wooden Chair 01", "Chair"),
    ("dining_chair_02", "polyhaven", "dining_chair_02", "Dining Chair 02", "Chair, high-back variant"),
    ("GreenChair_01", "polyhaven", "GreenChair_01", "Green Chair 01", "Chair, carver variant with arms"),
    ("toilet", "toilets", "Toilet_Elongated_C", "Toilets pack: Toilet_Elongated_C", "Toilet"),
    ("washbasin", "toilets", "Sink_C", "Toilets pack: Sink_C", "Vanity unit (pedestal washbasin)"),
    ("WetFloorSign_01", "polyhaven", "WetFloorSign_01", "Wet Floor Sign 01", "Out-of-order sign"),
    ("hand_dryer", "sketchfab", "67031384356e40dc96ec1d4fb61a18fd", "Hand Dryer", "Hand dryer"),
    ("paper_towel_dispenser", "sketchfab", "15c4a2aa81994874a8dfc8038339b08d", "Paper Towel Dispenser", "Paper towel dispenser"),
    ("baby_changing_station", "sketchfab", "8d9e952dc3c34536b30337450c29300f", "Baby Changing Station", "Baby-changing table"),
    ("restroom_sign", "sketchfab", "562555b57b5540ed81e25593a2d62791", "Restroom Sign", "Restroom sign"),
    ("cardboard_box_01", "polyhaven", "cardboard_box_01", "Cardboard Box 01", "Labelled box of lost items"),
    ("industrial_storage_cart", "polyhaven", "industrial_storage_cart", "Industrial Storage Cart", "Cart of unsorted things"),
    ("desk_bell", "sketchfab", "cc21b304c4bb4750bdde7a8fb538b116", "Desk Bell", "Service counter bell"),
    ("notice_board", "sketchfab", "9a2ae180dae541d2abfec62864dd9ef4", "Notice board", "Notice board of lost-item notes"),
    ("lost_and_found_sign", "sketchfab", "74157aae9eb746d9a0cfe7f9c5c7f83f", "Hospital hanging signage", "Lost and found sign"),
    ("service_counter", "sketchfab", "c8392731f0da42d68042013df924ea66", "Small modern reception counter", "Service counter"),
    ("dining_table", "polyhaven", "dining_table", "Dining Table", "Dining table"),
    ("round_wooden_table_01", "polyhaven", "round_wooden_table_01", "Round Wooden Table 01", "Dining table, round variant"),
    ("painted_wooden_table", "polyhaven", "painted_wooden_table", "Painted Wooden Table", "Dining table, long refectory variant"),
    ("modern_wooden_cabinet", "polyhaven", "modern_wooden_cabinet", "Modern Wooden Cabinet", "Sideboard"),
    ("steel_frame_shelves_01", "polyhaven", "steel_frame_shelves_01", "Steel Frame Shelves 01", "Shelf unit"),
    ("shopping_cart", "sketchfab", "8b2de85475184aa3a7fa9f244307dadf", "Shopping cart", "Shopping cart"),
    ("meal_plate", "sketchfab", "49a9493f884d405997d0f021639b22cf", "Plate of Food", "Plate of food"),
    ("rug", "sketchfab", "5f5de1ba643746fc9da76d2cac94fbb3", "Kuuma YE rug by Kristiina Lassus", "Rug"),
    ("door_frame", "sketchfab", "2f2f149f3ec44d658a02c1f924dfa449", "Door with frame", "Door frame"),
    ("exit_sign", "sketchfab_exit_sign", "46b52e08b8b64b81ad9ff5fb752bcc06", "Exit Sign", "EXIT sign"),
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
    info = json.loads(get(f"https://api.polyhaven.com/info/{asset}"))
    target = OUT / folder
    if not (target / f"{folder}.gltf").exists():
        files = json.loads(get(f"https://api.polyhaven.com/files/{asset}"))["gltf"]["1k"]["gltf"]
        target.mkdir(parents=True, exist_ok=True)
        (target / f"{folder}.gltf").write_bytes(get(files["url"]))
        for path, entry in files["include"].items():
            (target / path).parent.mkdir(parents=True, exist_ok=True)
            (target / path).write_bytes(get(entry["url"]))
    return ", ".join(info["authors"].keys()), f"https://polyhaven.com/a/{asset}", "CC0"


def sketchfab_token():
    token = os.environ.get("SKETCHFAB_API_TOKEN")
    if not token:
        path = pathlib.Path.home() / ".sketchfab-token"
        token = path.read_text(encoding="utf-8").strip() if path.exists() else ""
    if not token:
        sys.exit("Sketchfab models need an API token: set SKETCHFAB_API_TOKEN or write it to ~/.sketchfab-token.")
    return token


def flat_texture(target, gltf, name, colour):
    uri = f"textures/{name}.jpg"
    Image.new("RGB", (4, 4), colour).save(target / uri, quality=95)
    gltf.setdefault("images", []).append({"uri": uri})
    gltf.setdefault("textures", []).append({"source": len(gltf["images"]) - 1})
    return {"index": len(gltf["textures"]) - 1}


def sketchfab(folder, uid):
    info = json.loads(get(f"https://api.sketchfab.com/v3/models/{uid}"))
    licence = {"by": "CC-BY", "cc0": "CC0"}.get(info["license"]["slug"])
    if licence is None:
        sys.exit(f"{info['name']} has the licence {info['license']['label']}; only CC0 and CC-BY may be used.")
    target = OUT / folder
    if not (target / f"{folder}.gltf").exists():
        request = urllib.request.Request(f"https://api.sketchfab.com/v3/models/{uid}/download",
                                         headers={**USER_AGENT, "Authorization": f"Token {sketchfab_token()}"})
        with urllib.request.urlopen(request) as response:
            archive = zipfile.ZipFile(io.BytesIO(get(json.loads(response.read())["gltf"]["url"])))
        gltf = json.loads(archive.read("scene.gltf"))
        (target / "textures").mkdir(parents=True, exist_ok=True)
        for image in gltf.get("images", []):
            name = pathlib.PurePosixPath(image["uri"]).stem
            picture = Image.open(io.BytesIO(archive.read(image["uri"]))).convert("RGB")
            picture.thumbnail((1024, 1024))
            image["uri"] = f"textures/{name}.jpg"
            picture.save(target / image["uri"], quality=92, optimize=True)
            image.pop("mimeType", None)
        # The game wants albedo, normal and roughness maps on every material: missing ones become
        # small flat maps of the material's own factors (a flat normal map changes nothing).
        for index, material in enumerate(gltf["materials"]):
            material.pop("alphaMode", None)
            pbr = material.setdefault("pbrMetallicRoughness", {})
            tag = f"{folder}_material{index}"
            if "baseColorTexture" not in pbr:
                factor = pbr.pop("baseColorFactor", [1.0, 1.0, 1.0, 1.0])
                pbr["baseColorTexture"] = flat_texture(target, gltf, f"{tag}_albedo", tuple(round(255 * c ** (1 / 2.2)) for c in factor[:3]))
            if "metallicRoughnessTexture" not in pbr:
                roughness, metallic = pbr.pop("roughnessFactor", 1.0), pbr.pop("metallicFactor", 1.0)
                pbr["metallicRoughnessTexture"] = flat_texture(target, gltf, f"{tag}_orm", (255, round(255 * roughness), round(255 * metallic)))
            if "normalTexture" not in material:
                material["normalTexture"] = flat_texture(target, gltf, f"{tag}_normal", (128, 128, 255))
        gltf["buffers"] = [{"uri": f"{folder}.bin", "byteLength": len(archive.read("scene.bin"))}]
        (target / f"{folder}.bin").write_bytes(archive.read("scene.bin"))
        (target / f"{folder}.gltf").write_text(json.dumps(gltf, indent=1), encoding="utf-8")
    return info["user"]["displayName"], info["viewerUrl"], licence


def sketchfab_exit_sign(folder, uid):
    author, url, licence = sketchfab(folder, uid)
    target = OUT / folder
    gltf = json.loads((target / f"{folder}.gltf").read_text(encoding="utf-8"))
    # The source uses the same blended material for the glazing and the printed sign.
    # Keep the cover transparent so the opaque import does not hide the sign's face.
    if not any(m.get("name") == "ExitSignGlazing" for m in gltf["materials"]):
        glazing = copy.deepcopy(gltf["materials"][0])
        glazing["name"] = "ExitSignGlazing"
        glazing["alphaMode"] = "BLEND"
        glazing.setdefault("pbrMetallicRoughness", {})["baseColorFactor"] = [1, 1, 1, 0.1]
        gltf["materials"].append(glazing)
        for primitive in gltf["meshes"][0]["primitives"]:
            primitive["material"] = len(gltf["materials"]) - 1
        (target / f"{folder}.gltf").write_text(json.dumps(gltf, indent=1), encoding="utf-8")
    # Check if support posts already added
    if any(m.get("name") == "Posts" for m in gltf.get("meshes", [])):
        return author, url, licence
    bin_data = bytearray((target / f"{folder}.bin").read_bytes())

    def add_box(min_pt, max_pt):
        x0, y0, z0 = min_pt
        x1, y1, z1 = max_pt
        faces = [
            ([(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)], (1.0, 0.0, 0.0)),
            ([(x0, y0, z1), (x0, y1, z1), (x0, y1, z0), (x0, y0, z0)], (-1.0, 0.0, 0.0)),
            ([(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)], (0.0, 1.0, 0.0)),
            ([(x0, y0, z1), (x0, y0, z0), (x1, y0, z0), (x1, y0, z1)], (0.0, -1.0, 0.0)),
            ([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], (0.0, 0.0, 1.0)),
            ([(x1, y0, z0), (x0, y0, z0), (x0, y1, z0), (x1, y1, z0)], (0.0, 0.0, -1.0)),
        ]
        verts, normals, uvs, indices = [], [], [], []
        for quad, norm in faces:
            base = len(verts)
            verts.extend(quad)
            normals.extend([norm] * 4)
            uvs.extend([(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)])
            indices.extend([base, base + 1, base + 2, base, base + 2, base + 3])
        return verts, normals, uvs, indices

    all_verts, all_normals, all_uvs, all_indices = [], [], [], []
    def append_mesh(min_pt, max_pt):
        v, n, u, ind = add_box(min_pt, max_pt)
        base = len(all_verts)
        all_verts.extend(v)
        all_normals.extend(n)
        all_uvs.extend(u)
        all_indices.extend([base + i for i in ind])

    append_mesh((-14.6, 0.0, -91.5), (-13.4, 152.0, -90.1))
    append_mesh((13.4, 0.0, -91.5), (14.6, 152.0, -90.1))
    append_mesh((-16.0, 0.0, -95.5), (-12.0, 2.5, -86.1))
    append_mesh((12.0, 0.0, -95.5), (16.0, 2.5, -86.1))

    post_mat_idx = len(gltf["materials"])
    gltf["materials"].append({
        "name": "SteelPost",
        "pbrMetallicRoughness": {
            "baseColorTexture": flat_texture(target, gltf, "exit_sign_post_albedo", (45, 45, 50)),
            "metallicRoughnessTexture": flat_texture(target, gltf, "exit_sign_post_orm", (255, round(255 * 0.4), round(255 * 0.8))),
        },
        "normalTexture": flat_texture(target, gltf, "exit_sign_post_normal", (128, 128, 255)),
    })

    while len(bin_data) % 4 != 0:
        bin_data.append(0)

    bv_pos_offset = len(bin_data)
    for v in all_verts:
        bin_data.extend(struct.pack("<fff", *v))
    bv_norm_offset = len(bin_data)
    for norm in all_normals:
        bin_data.extend(struct.pack("<fff", *norm))
    bv_uv_offset = len(bin_data)
    for uv in all_uvs:
        bin_data.extend(struct.pack("<ff", *uv))
    bv_idx_offset = len(bin_data)
    for idx in all_indices:
        bin_data.extend(struct.pack("<H", idx))
    while len(bin_data) % 4 != 0:
        bin_data.append(0)

    bv_start = len(gltf["bufferViews"])
    gltf["bufferViews"].extend([
        {"buffer": 0, "byteOffset": bv_pos_offset, "byteLength": len(all_verts) * 12, "target": 34962},
        {"buffer": 0, "byteOffset": bv_norm_offset, "byteLength": len(all_normals) * 12, "target": 34962},
        {"buffer": 0, "byteOffset": bv_uv_offset, "byteLength": len(all_uvs) * 8, "target": 34962},
        {"buffer": 0, "byteOffset": bv_idx_offset, "byteLength": len(all_indices) * 2, "target": 34963},
    ])

    min_v = [min(v[i] for v in all_verts) for i in range(3)]
    max_v = [max(v[i] for v in all_verts) for i in range(3)]

    acc_start = len(gltf["accessors"])
    gltf["accessors"].extend([
        {"bufferView": bv_start, "byteOffset": 0, "componentType": 5126, "count": len(all_verts), "type": "VEC3", "min": min_v, "max": max_v},
        {"bufferView": bv_start + 1, "byteOffset": 0, "componentType": 5126, "count": len(all_normals), "type": "VEC3"},
        {"bufferView": bv_start + 2, "byteOffset": 0, "componentType": 5126, "count": len(all_uvs), "type": "VEC2"},
        {"bufferView": bv_start + 3, "byteOffset": 0, "componentType": 5123, "count": len(all_indices), "type": "SCALAR"},
    ])

    mesh_idx = len(gltf["meshes"])
    gltf["meshes"].append({
        "name": "Posts",
        "primitives": [{
            "attributes": {
                "POSITION": acc_start,
                "NORMAL": acc_start + 1,
                "TEXCOORD_0": acc_start + 2,
            },
            "indices": acc_start + 3,
            "material": post_mat_idx,
        }],
    })

    post_node_idx = len(gltf["nodes"])
    gltf["nodes"].append({
        "name": "Posts_Node",
        "mesh": mesh_idx,
    })
    gltf["nodes"][1].setdefault("children", []).append(post_node_idx)
    gltf["buffers"][0]["byteLength"] = len(bin_data)
    (target / f"{folder}.bin").write_bytes(bin_data)
    (target / f"{folder}.gltf").write_text(json.dumps(gltf, indent=1), encoding="utf-8")
    return author, url, licence


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
        "Models are simplified for rendering and textures resized. The EXIT sign has added support posts; its transparent cover is omitted when drawing the opaque Furniture. Generic service lettering and lost-item notes overlay the downloaded sign and notice-board frames.",
        "",
    ]
    for folder, source, asset, title, used_for in MODELS:
        print(f"{folder} ...", file=sys.stderr)
        if source == "polyhaven":
            author, url, licence = polyhaven(folder, asset)
            where = "Poly Haven"
        elif source == "sketchfab":
            author, url, licence = sketchfab(folder, asset)
            where = "Sketchfab"
            licence = "CC-BY ([CC BY 4.0](https://creativecommons.org/licenses/by/4.0/))" if licence == "CC-BY" else licence
        elif source == "sketchfab_exit_sign":
            author, url, licence = sketchfab_exit_sign(folder, asset)
            where = "Sketchfab"
            licence = "CC-BY ([CC BY 4.0](https://creativecommons.org/licenses/by/4.0/))" if licence == "CC-BY" else licence
        else:
            if pack is None:
                pack = zipfile.ZipFile(io.BytesIO(get(TOILETS_ZIP)))
            author, url, licence = toilets(pack, folder, asset)
            where = "OpenGameArt"
        lines.append(f"- {used_for}: [{title}]({url}) by {author}, {where}, {licence} (`models/{folder}`)")
    lines += [
        "",
        "## Cafeteria food models",
        "",
        "Fitted into the hot food counter wells and dessert counter shelves (ticket 08n).",
        "",
        "- Meatballs and sauce: [Swedish Meatballs](https://polyhaven.com/a/swedish_meatballs) by Poly Haven, CC0 (`models/meatballs`)",
        "- Mashed potatoes: [Mashed Potatoes](https://polyhaven.com/a/mashed_potatoes) by Poly Haven, CC0 (`models/mash`)",
        "- Lingonberry jam: [Cranberry Sauce](https://sketchfab.com/3d-models/cranberry-sauce-e55500d0263346d0a7751fb4be3ea7c2) by shedmon, Sketchfab, CC-BY ([CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)) (`models/lingonberries`)",
        "- Whole almond cake: [Carrot Cake](https://polyhaven.com/a/carrot_cake) by Poly Haven, CC0 (`models/whole_cake`)",
        "- Sliced cake and plate: [Cake Slice](https://sketchfab.com/3d-models/cake-slice-d01a91e1d2484433917dc9435b5a2e57) by omarelone, Sketchfab, CC-BY ([CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)) (`models/cake_slice`)",
        "",
        "## Textures",
        "",
        "PBR textures for the procedural Furniture and the floors are listed in `game/assets/textures/CREDITS.md`; all CC0, from Poly Haven and ambientCG.",
        "",
    ]
    CREDITS.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
