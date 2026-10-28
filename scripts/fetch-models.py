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
    ("gear_crowbar", "polyhaven", "crowbar_01", "Crowbar 01", "Gear: crowbar"),
    ("gear_hex_key", "sketchfab", "02ebda14785f480b965da3fc2115cd7f", "Allen Wrench", "Gear: hex key"),
    ("gear_knife", "sketchfab", "4c75aae7dfa74b919b61a321242de7f4", "Kitchen Knife", "Gear: kitchen knife"),
    ("gear_scissors", "sketchfab", "6e0defc85edc4920a794fe9e82b5f16b", "Scissors", "Gear: scissors"),
    ("gear_saw", "polyhaven", "handsaw_wood", "Handsaw Wood", "Gear: saw"),
    ("gear_canvas_bag", "sketchfab", "3316c1f3fc4e4f95adf05b1ee5478813", "Canvas Bag", "Gear: canvas bag"),
    ("gear_screwdriver", "polyhaven", "flathead_screwdriver", "Flathead Screwdriver", "Gear: screwdriver"),
    ("gear_flashlight", "polyhaven", "vintage_flashlight", "Vintage Flashlight", "Gear: flashlight"),
    ("ArmChair_01", "polyhaven", "ArmChair_01", "Arm Chair 01", "Armchair"),
    ("cube_armchair", "sketchfab", "c27b118e5001429fb4411ba6f33fb3f3", "Muwanga Lego Sofa - Office Master.ae", "Armchair, modern cube fabric variant"),
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
    # Camping ground Landmark (ticket 07z): an artificial camping showroom.
    ("camp_tent_dome", "sketchfab", "3f624d58d582430595179bdf9a156be3", "Modern Camping Tent", "Camping ground: dome tent"),
    ("camp_tent_tipi", "sketchfab", "63e67c40b3994278a09705941b6981b7", "Teepee", "Camping ground: tipi tent"),
    ("camp_tent_round", "sketchfab", "4998d155bd654544a577dec078ef5aea", "Circus Tent", "Camping ground: round striped canvas bell tent"),
    ("camp_tent_family", "sketchfab", "5fa10f0a207d4c9580b6e77bd45f774e", "Camping tent", "Camping ground: large family tent"),
    ("camp_tent_ridge", "sketchfab", "21cc274c86104fc997e0f172938d4205", "Camping Tent", "Camping ground: A-frame ridge tent"),
    ("camp_tent_popup", "sketchfab", "ffdd0519fa604e138d88a0c7fa262bb8", "Carpa Playera - beach tent", "Camping ground: pop-up canopy tent"),
    ("camp_tree_oak", "sketchfab", "2f4ae6b907fd4741abda5d1b71c83b10", "Oak Tree | Game Ready", "Camping ground: broad deciduous tree"),
    ("camp_tree_spruce", "sketchfab", "b68f79aee62f4e849be265c903f724f5", "Spruce Tree - Low Poly", "Camping ground: conifer"),
    ("camp_bush", "sketchfab", "9d9ce79d3ae040619e96d5b22c7de1a6", "Stylized Bush", "Camping ground: round bush"),
    ("camp_bush_flowering", "sketchfab", "7ccbbb3307004ad18bed624e356c5975", "Stylized Bush (V1)", "Camping ground: flowering bush"),
    ("camp_tree_house", "sketchfab", "1cf11dd38e03427a843fb034d3f7eec9", "dlX - Tree House(with Tree)", "Camping ground: tree house with its tree"),
    ("camp_campfire", "sketchfab", "360f5729f7d447b598170fd66b26c3b5", "Campfire Logs", "Camping ground: campfire stone ring with logs"),
    ("camp_log_bench", "sketchfab", "55fea7c2f116426a9c7332fcf5ef1705", "Log Bench", "Camping ground: log seat"),
    ("camp_kayak", "sketchfab", "cda3354816af4a0aa1ca5cfee6c5dcd7", "Kayak with Paddle", "Camping ground: kayak"),
    ("camp_teddy_bear", "sketchfab", "9c7081efd187447da9a333f89ea43f80", "Teddy Bear", "Camping ground: plush teddy bear"),
    ("camp_axe", "polyhaven", "hatchet", "Hatchet", "Camping ground: axe"),
    ("camp_marshmallow", "sketchfab", "e7a92ddb4a11433690334eab67f29474", "Marshmallow_on_a_stick", "Camping ground: marshmallow on a stick"),
    ("camp_sleeping_bag", "sketchfab", "b1b452fe764a4cc9af3030f69939f506", "Sleeping bag", "Camping ground: sleeping bag"),
    ("camp_hammock", "sketchfab", "c5fd4cef873f44f5a31db1fc6a04c572", "Hammock (low poly)", "Camping ground: hammock on a wooden stand"),
    ("camp_fishing_rod", "sketchfab", "86c77e3d23d341cd90245c21402f68b9", "Fishing Rod", "Camping ground: fishing rod"),
    ("camp_rubber_duck", "polyhaven", "rubber_duck_toy", "Rubber Duck Toy", "Camping ground: rubber duck"),
    ("camp_signpost", "sketchfab", "493bcedc8b344a38a9a67587b14bfcff", "Signpost", "Camping ground: wooden trail signpost"),
    ("camp_chair", "sketchfab", "46b592f41c1f441ca19faadd2b4bb90a", "Camping Chair", "Camping ground: folding camping chair"),
    # Camping ground showroom sections (ticket 07z3): sun loungers, deck chairs, beach umbrellas and rolled sleeping bags.
    ("sun_lounger_wood", "sketchfab", "f7b31f2190ae425682a873e28bd3244c", "Deck chair", "Sun lounger: wooden slatted lounger"),
    ("sun_lounger_plastic", "sketchfab", "974e74f55bbd4dc9a85c4a48fc987a18", "Sun lounger 3D", "Sun lounger, white plastic variant"),
    ("deck_chair", "sketchfab", "38de6171cbeb41c5bdc49c07df76ede9", "deck chair", "Sun lounger, folding canvas deck chair variant"),
    ("beach_umbrella", "sketchfab", "1014ad7c8a4c4aa8ba5e88fc6151aa4f", "Beach Umbrella", "Parasol, beach umbrella variant"),
    ("camp_sleeping_bag_rolled", "sketchfab", "d2ab6e8a8ffc472e9fca8b920662dafe", "Camping sleeping bag", "Camping ground: rolled sleeping bag"),
    # Crops (ticket 17f): a tended potted herb of the Plant section.
    ("crop_basil", "sketchfab", "ecc5bdf2230946f2a92cf5be4cf3a34c", "Stylized basil pot plant", "Crop: potted basil"),
]

# Goods on Stock Furniture shelves (ticket 08r): one model per Goods kind, copied along the shelves.
GOODS_MODELS = [
    ("goods_tinned_food", "polyhaven", "long_life_food", "Long Life Food: tin of tomatoes", "Tinned food"),
    ("goods_fresh_food", "polyhaven", "long_life_food", "Long Life Food: milk carton", "Fresh food"),
    ("goods_dry_food", "polyhaven", "russian_food_cans_01", "Russian Food Cans 01: salt box", "Dry food"),
    ("goods_bottled_water", "polyhaven", "plastic_bottle_gallon", "Plastic Bottle Gallon", "Bottled water"),
    ("goods_soft_drinks", "sketchfab", "7a54b5302f40454590e876a76146ad80", "Soda Can", "Soft drinks"),
    ("goods_coffee", "sketchfab", "9f9457e0e06444bb84813c896722c93c", "coffee bag", "Coffee"),
    ("goods_sweets", "sketchfab", "890c6ccc39ef414bb1c8f79fa821debf", "Candies", "Sweets"),
    ("goods_candles", "sketchfab", "f9ccd7e51c004c9e8354a4c723d20010", "Candle Low", "Candles"),
    ("goods_batteries", "sketchfab", "d3e533bb1bd948f68bf0a7ead9a5e211", "AA Battery", "Batteries"),
    ("goods_bulbs", "polyhaven", "lightbulb_led", "Lightbulb LED", "Bulbs"),
    ("goods_cables", "sketchfab", "2180b1f7b55b433285cbe08181a9e88a", "Power strip extension lead", "Cables"),
    ("goods_tape", "polyhaven", "medical_tape", "Medical Tape", "Tape"),
]

# People (ticket 09): one figure for every Villager, fitted to a Villager's height.
PEOPLE_MODELS = [
    ("villager", "sketchfab", "24ad029f3a094a679bc0b0b109ee40e3", "Male Character In Caual Clothing", "Villager"),
]

# Packs of several items where only one is wanted: folder -> the names of the nodes to keep as the scene (the rest are cut out).
KEEP_NODES = {
    "goods_tinned_food": {"long_life_food_tomatoes"},
    "goods_fresh_food": {"long_life_food_milk"},
    "goods_dry_food": {"russian_food_cans_01_salt_box"},
}

# Sketchfab models whose scene carries helper nodes that are not part of the item: folder -> node names to cut out
# (the Kayak's source has a camera with a large backdrop plane under it). An entry is a node name, or a node index
# where the names are not unique.
DROP_NODES = {
    "camp_kayak": ["Camera_7"],
    # The basil source stacks a large and a small plant, each in its own pot, at the same spot: the Crop keeps the large one.
    "crop_basil": ["Basil_B.002", "Pot.002"],
}

# Tinted models whose fabric is too dark to take the piece's tint (the game tints only texels lighter than its threshold):
# their albedo becomes a light grey that keeps the texture's detail. The rolled sleeping bag's canvas is dark brown,
# the cube armchair's fabric dark grey.
LIGHTEN_FOR_TINT = ["camp_sleeping_bag_rolled", "cube_armchair"]


def lighten_for_tint(folder):
    for path in (OUT / folder / "textures").glob("*baseColor.jpg"):
        image = Image.open(path).convert("L")
        # Already lightened: the step runs on every fetch, also when the download is skipped.
        if sum(image.getdata()) / (image.width * image.height) > 150:
            continue
        low, high = image.getextrema()
        # Pillow evaluates the curve for every level, also those below the image's darkest.
        image = image.point(lambda v: int(150 + 95 * (max(0, v - low) / max(1, high - low)) ** 0.7))
        image.convert("RGB").save(path, quality=90)

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
    keep_nodes(folder)
    return ", ".join(info["authors"].keys()), f"https://polyhaven.com/a/{asset}", "CC0"


def keep_nodes(folder):
    if folder not in KEEP_NODES:
        return
    path = OUT / folder / f"{folder}.gltf"
    gltf = json.loads(path.read_text(encoding="utf-8"))
    keep = [i for i, node in enumerate(gltf["nodes"]) if node.get("name") in KEEP_NODES[folder]]
    if not keep:
        sys.exit(f"{folder}: none of the nodes {KEEP_NODES[folder]} is in the model")
    if gltf["scenes"][gltf.get("scene", 0)]["nodes"] != keep:
        gltf["scenes"][gltf.get("scene", 0)]["nodes"] = keep
        path.write_text(json.dumps(gltf, indent=1), encoding="utf-8")


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
            # An unlit material would drop the normal and roughness maps the game wants.
            material.get("extensions", {}).pop("KHR_materials_unlit", None)
            pbr = material.setdefault("pbrMetallicRoughness", {})
            # A specular-glossiness material (an older glTF extension) becomes a matte non-metal with its diffuse map as
            # albedo; left as it is, its empty metallic-roughness part would default to white metal, which renders black.
            spec_gloss = material.get("extensions", {}).pop("KHR_materials_pbrSpecularGlossiness", None)
            if spec_gloss is not None:
                if "diffuseTexture" in spec_gloss:
                    pbr["baseColorTexture"] = spec_gloss["diffuseTexture"]
                pbr["metallicFactor"], pbr["roughnessFactor"] = 0.0, 0.8
            tag = f"{folder}_material{index}"
            if "baseColorTexture" not in pbr:
                factor = pbr.pop("baseColorFactor", [1.0, 1.0, 1.0, 1.0])
                pbr["baseColorTexture"] = flat_texture(target, gltf, f"{tag}_albedo", tuple(round(255 * c ** (1 / 2.2)) for c in factor[:3]))
            if "metallicRoughnessTexture" not in pbr:
                roughness, metallic = pbr.pop("roughnessFactor", 1.0), pbr.pop("metallicFactor", 1.0)
                pbr["metallicRoughnessTexture"] = flat_texture(target, gltf, f"{tag}_orm", (255, round(255 * roughness), round(255 * metallic)))
            if "normalTexture" not in material:
                material["normalTexture"] = flat_texture(target, gltf, f"{tag}_normal", (128, 128, 255))
        for key in ("extensionsUsed", "extensionsRequired"):
            kept = [e for e in gltf.pop(key, []) if e not in ("KHR_materials_unlit", "KHR_materials_pbrSpecularGlossiness")]
            if kept:
                gltf[key] = kept
        # Most archives name the buffer scene.bin; a few keep the exporter's own name.
        buffer = archive.read(gltf["buffers"][0]["uri"])
        gltf["buffers"] = [{"uri": f"{folder}.bin", "byteLength": len(buffer)}]
        (target / f"{folder}.bin").write_bytes(buffer)
        (target / f"{folder}.gltf").write_text(json.dumps(gltf, indent=1), encoding="utf-8")
    if folder in DROP_NODES:
        gltf = json.loads((target / f"{folder}.gltf").read_text(encoding="utf-8"))
        drop = {i for i, node in enumerate(gltf["nodes"]) if node.get("name") in DROP_NODES[folder] or i in DROP_NODES[folder]}
        if drop:
            for node in gltf["nodes"]:
                if "children" in node:
                    node["children"] = [c for c in node["children"] if c not in drop]
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
    if pack is None:
        return "loafbrr_1", TOILETS_PAGE, "CC0"
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


pack = None


def credit_lines(models):
    global pack
    lines = []
    for folder, source, asset, title, used_for in models:
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
            # The pack is about 100 MB: download it only while one of its models is missing.
            if pack is None and not (OUT / folder / f"{folder}.gltf").exists():
                pack = zipfile.ZipFile(io.BytesIO(get(TOILETS_ZIP)))
            author, url, licence = toilets(pack, folder, asset)
            where = "OpenGameArt"
        lines.append(f"- {used_for}: [{title}]({url}) by {author}, {where}, {licence} (`models/{folder}`)")
    return lines


def main():
    OUT.mkdir(parents=True, exist_ok=True)
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
    lines += credit_lines(MODELS)
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
        "## Goods models",
        "",
        "Copied along the shelves of Stock Furniture, as many as the shelf's stock (ticket 08r).",
        "",
        *credit_lines(GOODS_MODELS),
        "",
        "## People models",
        "",
        "Drawn for every Villager, scaled to a Villager's height (ticket 09).",
        "",
        *credit_lines(PEOPLE_MODELS),
        "",
        "## Textures",
        "",
        "PBR textures for the procedural Furniture and the floors are listed in `game/assets/textures/CREDITS.md`; all CC0, from Poly Haven and ambientCG.",
        "",
    ]
    for folder in LIGHTEN_FOR_TINT:
        lighten_for_tint(folder)
    CREDITS.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
