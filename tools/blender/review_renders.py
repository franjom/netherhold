"""Render a creature blockout for review: head and body views, clay and colour, skin variants,
a gameplay-distance view, silhouette overlays against the original, and labelled sheets.

    blender --background --factory-startup --python-exit-code 1
        --python tools/blender/review_renders.py --
        --blend build/modern/creature/imp/blockout/v008/imp_blockout.blend
        [--portrait build/modern/creature/imp/reference/portrait.png]
        [--baseline <dir holding a baseline's head/ renders>]
        [--head-only --out <dir>]

Reads a scene made by imp_blockout.py (collections `modern` and `original`, the blockout's
`params.json` beside it) and writes `renders/` and `sheets/` beside the .blend, or into `--out`;
the .blend is not modified. `--head-only` renders just the head views, which is how a baseline
version is shot again with today's cameras without touching its own folder. Every camera is
framed from the **original's** bounds (ASSET_PIPELINE.md §4), and lights, world and view
transform are reference_scene.py's, so every version is shot the same way.
"""
import argparse
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reference_scene as ref   # noqa: E402  (shared cameras, lights and render settings)
import sheets                   # noqa: E402

BODY_VIEWS = {"front": (0, 8), "front_3q_right": (45, 12), "right": (90, 8),
              "rear": (180, 8), "front_3q_left": (315, 12), "left": (270, 8)}
HEAD_VIEWS = {"front": (0, 2), "front_3q_right": (45, 6), "profile_right": (90, 2),
              "portrait_angle": (325, -2)}           # the portrait's close-up: the Imp's left, eye level
# Boxes as fractions of the original's height (x, y, z), so every version is framed alike.
HEAD_BOX = ((-0.42, -0.42, 0.56), (0.42, 0.22, 1.02))
FACE_BOX = ((-0.24, -0.34, 0.60), (0.24, 0.10, 0.98))   # tight, for the portrait comparison
PORTRAIT_CROP = (205, 40, 402, 268)                      # the close-up face in the 445x280 portrait: both eyes, teeth, ear tip
GAMEPLAY_PITCH, GAMEPLAY_FOV = 55.0, 60.0        # DungeonRunner.cameraPitch; Unity's default vertical FOV
GAMEPLAY_TILES_ACROSS = 10.0                     # tiles across a 4:3 frame at the target, as in DK2 screenshots


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="review_renders.py")
    parser.add_argument("--blend", required=True)
    parser.add_argument("--portrait")
    parser.add_argument("--baseline", help="folder holding a baseline's head/ renders (from --head-only)")
    parser.add_argument("--head-only", action="store_true")
    parser.add_argument("--out", help="output folder (default: beside the .blend)")
    parser.add_argument("--size", type=int, default=900)
    args = parser.parse_args(argv)
    args.blend = os.path.abspath(args.blend)
    args.dir = os.path.dirname(args.blend)
    args.out = os.path.abspath(args.out) if args.out else args.dir
    args.baseline = os.path.abspath(args.baseline) if args.baseline else None
    return args


def collection_visibility(scene, **shown):
    for child in scene.collection.children:
        if child.name in shown:
            child.hide_render = not shown[child.name]


def still(scene, camera, box, view, path, size, margin=1.12):
    ref.frame_camera(camera, box[0], box[1], *view, margin=margin)
    return ref.render_still(scene, path, size)


def clay(scene, on):
    if on and "M_clay" not in bpy.data.materials:
        material = bpy.data.materials.new("M_clay")
        node = material.node_tree.nodes["Principled BSDF"]
        node.inputs["Base Color"].default_value = (0.42, 0.42, 0.42, 1)
        node.inputs["Roughness"].default_value = 0.65
    scene.view_layers[0].material_override = bpy.data.materials["M_clay"] if on else None


def silhouette(scene, camera, box, view, size, which):
    """A white-on-black mask of `which` collection from `view`."""
    previous = scene.render.engine
    ref.set_mode(scene, False)
    shading = scene.display.shading
    shading.color_type, shading.single_color = "SINGLE", (1, 1, 1)
    shading.background_color = (0, 0, 0)
    collection_visibility(scene, modern=which == "modern", original=which == "original")
    path = os.path.join(bpy.app.tempdir, f"mask_{which}.png")
    still(scene, camera, box, view, path, size)
    mask = sheets.load(path)[..., 0]
    shading.color_type = "TEXTURE"
    shading.background_color = [c ** 2.2 for c in ref.BACKGROUND]
    scene.render.engine = previous
    return mask


def head_views(scene, camera, head_box, out, size, version):
    paths = {}
    for mode in ("colour", "clay"):
        clay(scene, mode == "clay")
        paths[mode] = [still(scene, camera, head_box, v, os.path.join(out, "renders", "head", f"{n}_{mode}.png"), size)
                       for n, v in HEAD_VIEWS.items()]
    clay(scene, False)
    return paths


def main():
    args = parse_args()
    bpy.ops.wm.open_mainfile(filepath=args.blend)
    scene = bpy.context.scene
    with open(os.path.join(args.dir, "params.json"), encoding="utf-8") as stream:
        params = json.load(stream)
    version = params["version"]
    low, high = (np.array(b) for b in params["originalBounds"])
    height = high[2] - low[2]
    original = bpy.data.collections["original"].objects[0]
    original.location = (0, 0, 0)                      # renders put both at the origin
    body_box = (np.array((low[0] - 0.12 * height, low[1] - 0.12 * height, 0.0)),
                np.array((high[0] + 0.12 * height, high[1] + 0.05 * height, high[2] + 0.06 * height)))
    head_box = tuple(np.array(c) * height for c in HEAD_BOX)
    face_box = tuple(np.array(c) * height for c in FACE_BOX)
    camera, _ = ref.setup_render(scene, bpy.data.collections["cameras"])
    out, size = args.out, args.size
    renders, out_sheets = os.path.join(out, "renders"), os.path.join(out, "sheets")
    collection_visibility(scene, modern=True, original=False, reference=False)
    ref.set_mode(scene, True)

    head = head_views(scene, camera, head_box, out, size, version)
    labels = [n.replace("_", " ") for n in HEAD_VIEWS]
    for mode in ("colour", "clay"):
        sheets.save(sheets.grid([sheets.load(p) for p in head[mode]], 4, labels,
                                f"Imp blockout {version} - head, {mode} (unapproved)"),
                    os.path.join(out_sheets, f"head_{mode}.png"))
    if args.head_only:
        print(f"-- {version}: head views in {out}")
        return

    if args.baseline:
        for mode in ("colour", "clay"):
            before = [os.path.join(args.baseline, "renders", "head", f"{n}_{mode}.png") for n in HEAD_VIEWS]
            cells = [sheets.load(p) for p in before] + [sheets.load(p) for p in head[mode]]
            base = os.path.basename(os.path.normpath(args.baseline)).replace("baseline_", "")
            sheets.save(sheets.grid(cells, 4, [f"{base}: {l}" for l in labels] + [f"{version}: {l}" for l in labels],
                                    f"Head, {mode}: {base} above, {version} below - same cameras and lights"),
                        os.path.join(out_sheets, f"head_vs_{base}_{mode}.png"))

    # Body views in colour and clay.
    for mode in ("colour", "clay"):
        clay(scene, mode == "clay")
        body = [still(scene, camera, body_box, v, os.path.join(renders, "body", f"{n}_{mode}.png"), size)
                for n, v in BODY_VIEWS.items()]
        sheets.save(sheets.grid([sheets.load(p) for p in body], 3, list(BODY_VIEWS),
                                f"Imp blockout {version} - body, {mode} (unapproved)"),
                    os.path.join(out_sheets, f"body_{mode}.png"))
    clay(scene, False)

    # The portrait's close-up beside the head at the same angle and displayed height.
    if args.portrait:
        portrait = sheets.load(os.path.abspath(args.portrait))
        x0, y0, x1, y1 = PORTRAIT_CROP
        crop = portrait[y0:y1, x0:x1]
        cell_h = 760
        cell_w = round(cell_h * crop.shape[1] / crop.shape[0])          # the portrait's own aspect
        cells = [sheets.fit(crop, cell_w, cell_h)]
        for mode in ("colour", "clay"):
            clay(scene, mode == "clay")
            path = still(scene, camera, face_box, HEAD_VIEWS["portrait_angle"],
                         os.path.join(renders, "head", f"portrait_angle_tight_{mode}.png"), 1000, margin=1.04)
            render = sheets.load(path)
            w = round(render.shape[0] * crop.shape[1] / crop.shape[0])
            x0 = (render.shape[1] - w) // 2
            cells.append(sheets.fit(render[:, x0:x0 + w], cell_w, cell_h))  # the portrait's aspect, centred
        clay(scene, False)
        sheets.save(sheets.grid(cells, 3, ["portrait close-up (owner-supplied)", f"{version} colour", f"{version} clay"],
                                f"Imp head {version} against the portrait, same angle and displayed height"),
                    os.path.join(out_sheets, "portrait_vs_head.png"))

    # Skin variants under identical light.
    import imp_blockout   # noqa: E402  (the variant table lives with the model)
    skin = bpy.data.objects["IMP_skin"]
    cells, names = [], []
    for box, view, suffix in ((body_box, BODY_VIEWS["front_3q_right"], "body"), (head_box, HEAD_VIEWS["front_3q_right"], "head")):
        for variant in imp_blockout.SKIN_VARIANTS:
            imp_blockout.set_skin_colour(skin, variant)
            cells.append(sheets.load(still(scene, camera, box, view,
                                           os.path.join(renders, "variants", f"{variant}_{suffix}.png"), 700)))
            names.append(variant.replace("_", " ") + ("" if suffix == "body" else " (head)"))
    imp_blockout.set_skin_colour(skin, "olive")
    sheets.save(sheets.grid(cells, 4, names, f"Imp blockout {version} - skin colour previews, identical lighting"),
                os.path.join(out_sheets, "variants.png"))

    # The original under the same cameras.
    collection_visibility(scene, modern=False, original=True)
    original_3q = still(scene, camera, body_box, BODY_VIEWS["front_3q_right"],
                        os.path.join(renders, "original_front_3q_right.png"), size)
    original_head = still(scene, camera, head_box, HEAD_VIEWS["front_3q_right"],
                          os.path.join(renders, "original_head_3q_right.png"), size)
    collection_visibility(scene, modern=True, original=False)

    # Silhouette overlays (a diagnostic, not a score): red original only, cyan blockout only.
    overlays = []
    for name in ("front", "right"):
        a = silhouette(scene, camera, body_box, BODY_VIEWS[name], size, "original")
        b = silhouette(scene, camera, body_box, BODY_VIEWS[name], size, "modern")
        overlays.append(sheets.save(np.stack([a, b, b, np.ones_like(a)], axis=-1) * 0.85 + 0.08,
                                    os.path.join(renders, f"overlay_{name}.png")))
        iou = float(((a > 0.5) & (b > 0.5)).sum() / max(1, ((a > 0.5) | (b > 0.5)).sum()))
        print(f"OVERLAY {name}: silhouette IoU {iou:.3f}")
    collection_visibility(scene, modern=True, original=False)
    ref.set_mode(scene, True)

    gameplay = gameplay_view(scene, camera, renders, height)

    cell = 600
    row = [sheets.fit(sheets.load(original_3q), cell, cell),
           sheets.fit(sheets.load(os.path.join(renders, "body", "front_3q_right_colour.png")), cell, cell),
           sheets.fit(sheets.load(gameplay["crop"]), cell, cell)]
    row2 = [sheets.fit(sheets.load(original_head), cell, cell),
            sheets.fit(sheets.load(os.path.join(renders, "head", "front_3q_right_colour.png")), cell, cell),
            sheets.fit(sheets.load(gameplay["full"]), cell, cell)]
    sheets.save(sheets.grid(row + row2, 3, ["Original DK2 mesh: proportions", f"Blockout {version} (unapproved)",
                                            "Gameplay distance: original | blockout", "Original head",
                                            f"Blockout {version} head", "Gameplay view, 1024x768"],
                            f"Imp: original and blockout {version}"),
                os.path.join(out_sheets, "compare.png"))
    sheets.save(sheets.grid([sheets.load(p) for p in overlays], 2,
                            ["front: red original only, cyan blockout only", "right side: same"],
                            f"Imp blockout {version} - silhouette against the original (diagnostic)"),
                os.path.join(out_sheets, "overlay.png"))
    print(f"-- {version}: sheets in {out_sheets}")


def gameplay_view(scene, camera, renders, height):
    """Original and blockout half a tile either side of a tile centre, under the gameplay camera."""
    original = bpy.data.collections["original"].objects[0]
    root = bpy.data.objects["IMP_root"]
    original.location = (-0.5, 0, 0)
    root.location = (0.5, 0, 0)
    collection_visibility(scene, modern=True, original=True)
    floor = bpy.data.objects.new("floor", bpy.data.meshes.new("floor"))
    tiles = 9
    verts, faces, materials = [], [], []
    for i in range(tiles):
        for j in range(tiles):
            x0, y0 = i - tiles // 2 - 0.5, j - tiles // 2 - 0.5
            base = len(verts)
            verts += [(x0, y0, 0), (x0 + 1, y0, 0), (x0 + 1, y0 + 1, 0), (x0, y0 + 1, 0)]
            faces.append((base, base + 1, base + 2, base + 3))
            materials.append((i + j) % 2)
    floor.data.from_pydata(verts, [], faces)
    for shade in (0.20, 0.26):
        m = bpy.data.materials.new(f"M_floor_{shade}")
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (shade, shade * 0.9, shade * 0.8, 1)
        floor.data.materials.append(m)
    floor.data.polygons.foreach_set("material_index", materials)
    bpy.data.collections["cameras"].objects.link(floor)

    target = Vector((0, 0, height * 0.5))
    half_h = math.atan(math.tan(math.radians(GAMEPLAY_FOV) / 2) * 4 / 3)
    distance = (GAMEPLAY_TILES_ACROSS / 2) / math.tan(half_h)
    pitch = math.radians(GAMEPLAY_PITCH)
    camera.data.type = "PERSP"
    camera.data.sensor_fit = "VERTICAL"
    camera.data.angle_y = math.radians(GAMEPLAY_FOV)
    camera.data.clip_end = 100
    ref.look_at(camera, target, Vector((0, -math.cos(pitch), math.sin(pitch))), distance)
    scene.render.resolution_x, scene.render.resolution_y = 1024, 768
    path = os.path.join(renders, "gameplay", "gameplay_1024x768.png")
    scene.render.filepath = path
    scene.render.image_settings.media_type = "IMAGE"
    scene.render.image_settings.file_format = "PNG"
    bpy.ops.render.render(write_still=True)
    full = sheets.load(path)
    from bpy_extras.object_utils import world_to_camera_view
    points = [world_to_camera_view(scene, camera, Vector((x, 0, height * 0.5))) for x in (-0.5, 0.5)]
    cx = int(np.mean([p.x for p in points]) * 1024)
    cy = int((1 - np.mean([p.y for p in points])) * 768)
    crop = full[max(0, cy - 40):cy + 40, max(0, cx - 80):cx + 80]
    top = world_to_camera_view(scene, camera, Vector((0.5, 0, height)))
    bottom = world_to_camera_view(scene, camera, Vector((0.5, 0, 0)))
    print(f"GAMEPLAY imp height on screen: {abs(top.y - bottom.y) * 768:.1f} px of 768; camera distance "
          f"{distance:.2f} tiles at pitch {GAMEPLAY_PITCH} deg")
    crop_path = sheets.save(sheets.nearest(crop, 4), os.path.join(renders, "gameplay", "gameplay_crop_x4.png"))
    bpy.data.objects.remove(floor)
    original.location = (0, 0, 0)
    root.location = (0, 0, 0)
    camera.data.type = "ORTHO"
    return {"full": path, "crop": crop_path}


if __name__ == "__main__":
    main()
