"""Build a Blender reference scene from converted DK2 GLBs, inspect it, and render evidence.

    blender --background --factory-startup --python-exit-code 1
        --python tools/blender/reference_scene.py --
        --sources build/reference/imp/source/sources.json --out build/reference/imp
        [--resolution 1024] [--frames 8] [--no-render] [--no-video]

or, without a staged reference set, `--glb NAME=PATH` once per clip.

Starts from an empty factory scene and imports each GLB with Blender's own glTF importer
into collection `original/<clip>` (ASSET_PIPELINE.md §4, stage 3). The inspection reads
the imported data, never the importer's options: meshes, materials, images, UV layers,
shape keys and the slotted action that drives them, and evaluates the deformed mesh at
every key time and every midpoint. Each GLB is also read directly, so the imported
result is checked against the file: triangle and key counts, key times, and the
bounding box of every key frame. A FAIL exits 1 (with --python-exit-code 1).

Writes `<out>/<blend>`, `<out>/report.json`, `<out>/report.md` and, unless --no-render,
`<out>/renders/`: views (lit and unlit), per-clip frame strips, contact sheets and one
H.264 preview per clip. Nothing is smoothed, subdivided or retextured.
"""
import argparse
import json
import math
import os
import struct
import sys
import urllib.parse

import bpy
import numpy as np
from mathutils import Matrix, Vector

PINNED = (5, 2)                      # tools/blender/README.md: 5.2.2 LTS, d13f752e3b9c
GLTF_TO_BLENDER = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))   # the importer's Y-up -> Z-up
VIEWS = {                            # azimuth in degrees from the creature's front, elevation
    "front": (0, 8), "front_three_quarter": (45, 15), "right": (90, 8),
    "rear_three_quarter": (135, 15), "rear": (180, 8), "left": (270, 8), "top": (0, 89),
}
FRONT = Vector((0, -1, 0))           # the Imp's and nine other creatures' faces look down -Y (renders, 2026-10-06)
BACKGROUND = (0.32, 0.32, 0.32)      # display-referred grey: neither the texture's darks nor lights


# ---------------------------------------------------------------- arguments and results

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="reference_scene.py", description=__doc__.split("\n\n")[0])
    parser.add_argument("--sources", help="sources.json written by stage_reference.py")
    parser.add_argument("--glb", action="append", default=[], metavar="NAME=PATH")
    parser.add_argument("--out", required=True)
    parser.add_argument("--blend", default="reference.blend")
    parser.add_argument("--resolution", type=int, default=1024, help="still size in pixels")
    parser.add_argument("--frame-resolution", type=int, default=640, help="frame strip cell size")
    parser.add_argument("--frames", type=int, default=8, help="frames shown per clip")
    parser.add_argument("--samples", type=int, default=9, help="representative samples reported per clip")
    parser.add_argument("--rest-clip", help="clip whose first frame frames the stills (default: first)")
    parser.add_argument("--no-render", action="store_true")
    parser.add_argument("--no-video", action="store_true")
    parser.add_argument("--allow-version", action="store_true", help="run on a Blender other than the pin")
    args = parser.parse_args(argv)
    args.out = os.path.abspath(args.out)    # Blender resolves a relative render path against its own idea of cwd
    clips = []
    if args.sources:
        with open(args.sources, encoding="utf-8") as stream:
            staged = json.load(stream)
        root = os.path.dirname(os.path.abspath(args.sources))
        clips += [(clip["name"], os.path.join(root, clip["glb"]), clip) for clip in staged["clips"]]
    for item in args.glb:
        name, _, path = item.partition("=")
        if not path:
            parser.error(f"--glb wants NAME=PATH, got {item!r}")
        clips.append((name, os.path.abspath(path), None))
    if not clips:
        parser.error("give --sources or at least one --glb")
    return args, clips


class Checks:
    """PASS/WARN/FAIL lines; any FAIL makes the run fail."""

    def __init__(self):
        self.items = []

    def add(self, level, subject, message):
        self.items.append({"level": level, "subject": subject, "message": message})
        print(f"[{level}] {subject}: {message}")

    def check(self, condition, subject, message, failure="FAIL"):
        self.add("PASS" if condition else failure, subject, message)
        return condition

    @property
    def failed(self):
        return any(item["level"] == "FAIL" for item in self.items)


# ---------------------------------------------------------------- reading the GLB itself

def read_glb(path):
    with open(path, "rb") as stream:
        data = stream.read()
    if data[:4] != b"glTF":
        raise ValueError(f"{path} is not a GLB")
    json_length = struct.unpack_from("<I", data, 12)[0]
    document = json.loads(data[20:20 + json_length])
    binary = data[20 + json_length + 8:]
    return document, binary


def accessor(document, binary, index):
    view_of = document["accessors"][index]
    view = document["bufferViews"][view_of["bufferView"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[view_of["type"]]
    dtype = {5121: np.uint8, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}[view_of["componentType"]]
    offset = view.get("byteOffset", 0) + view_of.get("byteOffset", 0)
    values = np.frombuffer(binary, dtype=dtype, count=view_of["count"] * width, offset=offset)
    return values.reshape(view_of["count"], width) if width > 1 else values


def glb_facts(path):
    """What the file says, in Blender's axes: counts, key times, and each key frame's bounds."""
    document, binary = read_glb(path)
    base_dir = os.path.dirname(path)
    triangles, vertices, duplicate_pairs, frames = 0, 0, 0, None
    for mesh in document["meshes"]:
        for primitive in mesh["primitives"]:
            positions = accessor(document, binary, primitive["attributes"]["POSITION"]).astype(np.float64)
            vertices += len(positions)
            # A KMF sprite with no triangles at its first detail level is written as points (mode 0).
            indices = (accessor(document, binary, primitive["indices"]).astype(np.int64).reshape(-1, 3)
                       if primitive.get("mode", 4) == 4 and "indices" in primitive else np.empty((0, 3), np.int64))
            triangles += len(indices)
            seen = set()
            for triangle in indices:
                key = frozenset(tuple(positions[i]) for i in triangle)
                duplicate_pairs += key in seen
                seen.add(key)
            targets = [positions] + [positions + accessor(document, binary, target["POSITION"])
                                     for target in primitive.get("targets", [])]
            stacked = np.stack(targets)                         # frames x vertices x 3
            frames = stacked if frames is None else np.concatenate([frames, stacked], axis=1)
    blender = frames @ np.array(GLTF_TO_BLENDER, dtype=np.float64).T
    bounds = [(f.min(axis=0).tolist(), f.max(axis=0).tolist()) for f in blender]
    times, interpolation = [], None
    if document.get("animations"):
        sampler = document["animations"][0]["samplers"][0]
        times = accessor(document, binary, sampler["input"]).astype(float).tolist()
        interpolation = sampler.get("interpolation", "LINEAR")
    images = []
    for image in document.get("images", []):
        uri = image.get("uri")
        resolved = os.path.normpath(os.path.join(base_dir, urllib.parse.unquote(uri))) if uri else None
        images.append({"uri": uri, "exists": bool(resolved and os.path.isfile(resolved))})
    return {
        "triangles": triangles, "vertices": vertices, "duplicatePositionTriangles": duplicate_pairs,
        "morphTargets": len(frames) - 1, "keyTimes": times, "interpolation": interpolation,
        "keyBounds": bounds, "images": images,
        "materials": [m.get("name") for m in document.get("materials", [])],
        "generator": document.get("asset", {}).get("generator"),
    }


# ---------------------------------------------------------------- import and inspection

def fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    original = bpy.data.collections.new("original")
    scene.collection.children.link(original)
    cameras = bpy.data.collections.new("cameras")
    scene.collection.children.link(cameras)
    return scene, original, cameras


def import_clip(scene, parent, name, path):
    collection = bpy.data.collections.new(name)
    parent.children.link(collection)
    layer = bpy.context.view_layer.layer_collection.children[parent.name].children[collection.name]
    bpy.context.view_layer.active_layer_collection = layer
    before = set(bpy.data.objects)
    result = bpy.ops.import_scene.gltf(filepath=path)
    if "FINISHED" not in result:
        raise RuntimeError(f"glTF import of {path} returned {result}")
    objects = [o for o in bpy.data.objects if o not in before]
    return collection, objects


def action_channels(id_data):
    """The F-curves that animate `id_data` through its action slot (Blender 4.4+ layered actions)."""
    animation = id_data.animation_data
    if animation is None or animation.action is None:
        return None, None, []
    action, slot = animation.action, animation.action_slot
    curves = []
    for layer in action.layers:
        for strip in layer.strips:
            bag = strip.channelbag(slot) if slot is not None else None
            if bag is not None:
                curves += list(bag.fcurves)
    return action, slot, curves


def material_facts(material):
    facts = {"name": material.name if material else None, "images": [], "baseColorLinked": False}
    if material is None or material.node_tree is None:     # use_nodes is deprecated in 5.x
        return facts
    for node in material.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            base = node.inputs["Base Color"]
            facts["baseColorLinked"] = base.is_linked
            facts["metallic"] = round(node.inputs["Metallic"].default_value, 4)
            facts["roughness"] = round(node.inputs["Roughness"].default_value, 4)
        if node.type == "TEX_IMAGE" and node.image is not None:
            image = node.image
            path = bpy.path.abspath(image.filepath) if image.filepath else ""
            facts["images"].append({
                "name": image.name, "file": path, "fileExists": bool(path) and os.path.isfile(path),
                "packed": image.packed_file is not None, "size": list(image.size),
                "hasPixels": image.size[0] > 0 and image.size[1] > 0 and len(image.pixels) > 0,
                "interpolation": node.interpolation,
            })
    return facts


def evaluated_coordinates(objects, scene, frame):
    whole = int(math.floor(frame))
    scene.frame_set(whole, subframe=frame - whole)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    result = []
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        coordinates = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
        mesh.vertices.foreach_get("co", coordinates)
        coordinates = coordinates.reshape(-1, 3)
        matrix = np.array(obj.matrix_world, dtype=np.float64)
        result.append(coordinates @ matrix[:3, :3].T + matrix[:3, 3])
        evaluated.to_mesh_clear()
    return np.concatenate(result) if result else np.empty((0, 3))


def bounds_of(points):
    return points.min(axis=0), points.max(axis=0)


def inspect_clip(scene, name, path, objects, glb, checks, samples):
    fps = scene.render.fps / scene.render.fps_base
    meshes = [o for o in objects if o.type == "MESH"]
    report = {"name": name, "glb": path, "fileFacts": {k: v for k, v in glb.items() if k != "keyBounds"},
              "objects": [], "samples": []}
    if not checks.check(bool(meshes), name, f"{len(meshes)} mesh object(s) imported"):
        return report

    polygons = sum(len(o.data.polygons) for o in meshes)
    for obj in meshes:
        mesh = obj.data
        coordinates = np.empty(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", coordinates)
        uv_layers = []
        for layer in mesh.uv_layers:
            uv = np.empty(len(layer.uv) * 2)
            layer.uv.foreach_get("vector", uv)
            uv_layers.append({"name": layer.name, "finite": bool(np.isfinite(uv).all()),
                              "min": uv.reshape(-1, 2).min(axis=0).round(4).tolist() if len(uv) else None,
                              "max": uv.reshape(-1, 2).max(axis=0).round(4).tolist() if len(uv) else None})
        keys = mesh.shape_keys
        action, slot, curves = action_channels(keys) if keys else (None, None, [])
        key_times = sorted({round(k.co[0], 6) for c in curves for k in c.keyframe_points})
        interpolations = sorted({k.interpolation for c in curves for k in c.keyframe_points})
        materials = [material_facts(m) for m in mesh.materials]
        entry = {
            "object": obj.name, "vertices": len(mesh.vertices), "polygons": len(mesh.polygons),
            "loops": len(mesh.loops), "finite": bool(np.isfinite(coordinates).all()),
            "uvLayers": uv_layers, "materials": materials,
            "shapeKeys": len(keys.key_blocks) if keys else 0,
            "shapeKeysRelative": keys.use_relative if keys else None,
            "action": action.name if action else None, "actionSlot": slot.identifier if slot else None,
            "actionLayered": action.is_action_layered if action else None,
            "fcurves": len(curves), "keyFrames": len(key_times), "interpolation": interpolations,
            "frameRange": [key_times[0], key_times[-1]] if key_times else None,
            "objectAnimation": obj.animation_data is not None and obj.animation_data.action is not None,
            "customNormals": mesh.has_custom_normals,
        }
        report["objects"].append(entry)
        checks.check(entry["finite"], name, f"{obj.name}: {len(mesh.vertices)} rest vertices all finite")
        checks.check(bool(uv_layers) and all(u["finite"] for u in uv_layers), name,
                     f"{obj.name}: UV layers {[u['name'] for u in uv_layers]} present and finite")
        for material in materials:
            images = material["images"]
            checks.check(material["baseColorLinked"] and bool(images) and all(
                i["hasPixels"] and (i["fileExists"] or i["packed"]) for i in images), name,
                f"material {material['name']}: base colour textured by "
                f"{[(i['name'], i['size']) for i in images]}")
        if not checks.check(action is not None and len(curves) > 0 and len(key_times) > 1, name,
                            f"{obj.name}: action {entry['action']!r} slot {entry['actionSlot']!r}, "
                            f"{len(curves)} F-curves, {len(key_times)} key times"):
            return report

    # The file against the import.
    checks.check(glb["triangles"] - polygons <= glb["duplicatePositionTriangles"], name,
                 f"triangles: file {glb['triangles']}, imported {polygons}; the file holds "
                 f"{glb['duplicatePositionTriangles']} triangle(s) whose positions repeat another's "
                 "(two-sided), which Blender may merge", failure="FAIL")
    if polygons != glb["triangles"]:
        checks.add("WARN", name, f"{glb['triangles'] - polygons} two-sided triangle(s) merged by the importer; "
                                 "the surface drawn is unchanged with back-face culling off")
    first = report["objects"][0]
    checks.check(first["shapeKeys"] - 1 == glb["morphTargets"] == first["fcurves"], name,
                 f"morph targets: file {glb['morphTargets']}, shape keys {first['shapeKeys'] - 1}, "
                 f"F-curves {first['fcurves']}")
    checks.check(first["interpolation"] == [glb["interpolation"]], name,
                 f"interpolation: file {glb['interpolation']}, imported {first['interpolation']}")
    blender_times = [t / fps for t in sorted({round(k.co[0], 6) for c in action_channels(meshes[0].data.shape_keys)[2]
                                               for k in c.keyframe_points})]
    worst_time = max(abs(a - b) for a, b in zip(blender_times, glb["keyTimes"])) if blender_times else math.inf
    checks.check(len(blender_times) == len(glb["keyTimes"]) and worst_time < 1e-4, name,
                 f"key times: {len(glb['keyTimes'])} in file, {len(blender_times)} imported, "
                 f"worst difference {worst_time:.2e} s")

    # Evaluate at every key and every midpoint: bounds against the file, interpolation, motion.
    frames = [t * fps for t in glb["keyTimes"]]
    duration = glb["keyTimes"][-1] if glb["keyTimes"] else 0.0
    report["durationSeconds"] = duration
    report["sceneFps"] = fps
    rest = evaluated_coordinates(meshes, scene, frames[0])
    diagonal = float(np.linalg.norm(np.subtract(*bounds_of(rest)[::-1])))
    tolerance = 1e-4 * max(diagonal, 1e-9)
    worst_bounds, worst_mid, finite, counts = 0.0, 0.0, True, set()
    keyed = []
    for index, frame in enumerate(frames):
        points = evaluated_coordinates(meshes, scene, frame)
        keyed.append(points)
        counts.add(len(points))
        finite &= bool(np.isfinite(points).all())
        low, high = bounds_of(points)
        file_low, file_high = (np.array(b) for b in glb["keyBounds"][index])
        worst_bounds = max(worst_bounds, float(np.abs(low - file_low).max()), float(np.abs(high - file_high).max()))
    for index in range(len(frames) - 1):
        middle = evaluated_coordinates(meshes, scene, (frames[index] + frames[index + 1]) / 2)
        finite &= bool(np.isfinite(middle).all())
        worst_mid = max(worst_mid, float(np.abs(middle - (keyed[index] + keyed[index + 1]) / 2).max()))
    motion = max(float(np.abs(points - keyed[0]).max()) for points in keyed)
    checks.check(finite, name, f"evaluated mesh finite at {len(frames)} keys and {len(frames) - 1} midpoints")
    checks.check(len(counts) == 1, name, f"vertex count constant across the clip: {sorted(counts)}")
    checks.check(worst_bounds <= tolerance, name,
                 f"key-frame bounds match the file within {worst_bounds:.2e} (tolerance {tolerance:.2e})")
    checks.check(worst_mid <= tolerance, name,
                 f"midpoints are the linear blend of their keys within {worst_mid:.2e}")
    checks.check(motion > 100 * tolerance, name,
                 f"the evaluated mesh moves: largest vertex displacement from the first key {motion:.4f} "
                 f"({100 * motion / diagonal:.1f}% of the rest diagonal)")

    count = max(2, min(len(frames), samples))
    for k in range(count):
        time = duration * k / (count - 1)
        points = evaluated_coordinates(meshes, scene, time * fps)
        low, high = bounds_of(points)
        report["samples"].append({
            "time": round(time, 4), "frame": round(time * fps, 3), "vertices": len(points),
            "min": low.round(4).tolist(), "max": high.round(4).tolist(),
            "maxDisplacement": round(float(np.abs(points - keyed[0]).max()), 4),
        })
    report["restDiagonal"] = diagonal
    report["motion"] = motion
    return report


# ---------------------------------------------------------------- rendering

def look_at(camera, target, direction, distance):
    camera.location = target + direction.normalized() * distance
    camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()


def view_direction(azimuth, elevation):
    """Camera direction from the creature: azimuth 0 faces it, 90 is on its own right side."""
    a, e = math.radians(azimuth), math.radians(elevation)
    right = FRONT.cross(Vector((0, 0, 1)))                    # facing -Y with Z up, its right is -X
    horizontal = FRONT * math.cos(a) + right * math.sin(a)
    return (horizontal * math.cos(e) + Vector((0, 0, 1)) * math.sin(e)).normalized()


def frame_camera(camera, low, high, azimuth, elevation, margin=1.12):
    centre = Vector(((low[0] + high[0]) / 2, (low[1] + high[1]) / 2, (low[2] + high[2]) / 2))
    size = Vector(high) - Vector(low)
    direction = view_direction(azimuth, elevation)
    look_at(camera, centre, direction, size.length * 3)
    bpy.context.view_layer.update()
    inverse = camera.matrix_world.inverted()
    corners = [inverse @ Vector((x, y, z)) for x in (low[0], high[0]) for y in (low[1], high[1])
               for z in (low[2], high[2])]
    width = max(c.x for c in corners) - min(c.x for c in corners)
    height = max(c.y for c in corners) - min(c.y for c in corners)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = max(width, height) * margin
    camera.data.clip_end = size.length * 10


def setup_render(scene, cameras):
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"           # no tone curve on the original's colours
    scene.view_settings.look = "None"
    world = bpy.data.worlds.new("neutral")
    if world.node_tree is None:                            # 5.x creates it; older releases need use_nodes
        world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (*[c ** 2.2 for c in BACKGROUND], 1)
    background.inputs["Strength"].default_value = 1.0
    scene.world = world
    lights = []
    for name, rotation, energy in (("key", (50, 0, -35), 3.0), ("fill", (60, 0, 140), 1.2), ("rim", (20, 0, 180), 1.5)):
        light = bpy.data.lights.new(name, "SUN")
        light.energy = energy
        light.angle = math.radians(10)
        obj = bpy.data.objects.new(name, light)
        obj.rotation_euler = [math.radians(v) for v in rotation]
        cameras.objects.link(obj)
        lights.append(obj)
    camera = bpy.data.objects.new("camera", bpy.data.cameras.new("camera"))
    cameras.objects.link(camera)
    scene.camera = camera
    return camera, lights


def set_mode(scene, lit):
    """Lit: EEVEE, neutral world and three suns. Unlit: Workbench flat texture colour, for texel checks."""
    if lit:
        scene.render.engine = "BLENDER_EEVEE"
    else:
        scene.render.engine = "BLENDER_WORKBENCH"
        shading = scene.display.shading
        shading.light = "FLAT"
        shading.color_type = "TEXTURE"
        shading.show_backface_culling = False
        shading.background_type = "VIEWPORT"
        shading.background_color = [c ** 2.2 for c in BACKGROUND]   # linear, as the world colour is


def show_only(original, name):
    for child in original.children:
        child.hide_render = child.name != name


def render_still(scene, path, size):
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.media_type = "IMAGE"
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def contact_sheet(paths, columns, out_path, gap=8):
    images = [bpy.data.images.load(p, check_existing=False) for p in paths]
    width, height = images[0].size
    rows = math.ceil(len(images) / columns)
    sheet = np.full(((height + gap) * rows + gap, (width + gap) * columns + gap, 4), 0.12, dtype=np.float32)
    sheet[..., 3] = 1
    for index, image in enumerate(images):
        pixels = np.empty(width * height * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        row, column = divmod(index, columns)
        y = (rows - 1 - row) * (height + gap) + gap               # Blender images start bottom-left
        x = column * (width + gap) + gap
        sheet[y:y + height, x:x + width] = pixels.reshape(height, width, 4)
        bpy.data.images.remove(image)
    result = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=False)
    result.pixels.foreach_set(sheet.ravel())
    result.filepath_raw = out_path
    result.file_format = "PNG"
    result.save()
    bpy.data.images.remove(result)
    return out_path


def render_evidence(scene, original, cameras, clips, args, report):
    renders = os.path.join(args.out, "renders")
    camera, _ = setup_render(scene, cameras)
    fps = scene.render.fps / scene.render.fps_base
    rest_name = args.rest_clip or clips[0]["name"]
    rest = next(c for c in clips if c["name"] == rest_name)
    evidence = {"views": {}, "frames": {}, "sheets": {}, "previews": {}, "restClip": rest_name}

    # Stills: the rest clip's first key, framed by its own bounds.
    show_only(original, rest_name)
    points = evaluated_coordinates(rest["meshes"], scene, 0.0)
    low, high = bounds_of(points)
    for lit in (True, False):
        set_mode(scene, lit)
        mode = "lit" if lit else "unlit"
        paths = []
        for view, (azimuth, elevation) in VIEWS.items():
            frame_camera(camera, low, high, azimuth, elevation)
            paths.append(render_still(scene, os.path.join(renders, "views", f"{rest_name}_{view}_{mode}.png"),
                                      args.resolution))
        evidence["views"][mode] = paths
        evidence["sheets"][f"views_{mode}"] = contact_sheet(
            paths[:6], 3, os.path.join(renders, f"sheet_views_{mode}.png"))

    # Frame strips and previews: each clip framed by its bounds over every key, so nothing leaves frame.
    set_mode(scene, True)
    for clip in clips:
        show_only(original, clip["name"])
        keyed = [evaluated_coordinates(clip["meshes"], scene, t * fps) for t in clip["glb"]["keyTimes"]]
        low = np.min([k.min(axis=0) for k in keyed], axis=0)
        high = np.max([k.max(axis=0) for k in keyed], axis=0)
        duration = clip["glb"]["keyTimes"][-1]
        count = max(2, args.frames)
        paths = []
        for view in ("front_three_quarter", "right"):
            frame_camera(camera, low, high, *VIEWS[view])
            for k in range(count):
                time = duration * k / (count - 1)
                whole = int(math.floor(time * fps))
                scene.frame_set(whole, subframe=time * fps - whole)
                paths.append(render_still(scene, os.path.join(
                    renders, "frames", clip["name"], f"{view}_{k:02d}_t{time:.3f}s.png"), args.frame_resolution))
        evidence["frames"][clip["name"]] = paths
        evidence["sheets"][f"frames_{clip['name']}"] = contact_sheet(
            paths, count, os.path.join(renders, f"sheet_frames_{clip['name']}.png"))
        if not args.no_video:
            frame_camera(camera, low, high, *VIEWS["front_three_quarter"])
            evidence["previews"][clip["name"]] = render_preview(
                scene, os.path.join(renders, f"preview_{clip['name']}.mp4"), duration, fps)
    scene.frame_set(0)
    report["evidence"] = evidence


def render_preview(scene, path, duration, fps, size=720, at_least_seconds=3.0):
    """Back-to-back passes at the scene rate, at least `at_least_seconds` long, rendered as a
    numbered PNG sequence and then encoded. The action holds its last key after one pass; frame f
    is drawn at f modulo one pass rather than adding a Cycles modifier, so the reference's
    animation data is never edited."""
    passes = max(1, math.ceil(at_least_seconds / duration)) if duration > 0 else 1
    pass_frames = duration * fps
    total = max(2, int(round(pass_frames * passes)))
    sequence_dir = os.path.splitext(path)[0] + "_frames"
    files = []
    for f in range(total):
        local = f % pass_frames if pass_frames > 0 else 0
        whole = int(math.floor(local))
        scene.frame_set(whole, subframe=local - whole)
        files.append(render_still(scene, os.path.join(sequence_dir, f"{f:04d}.png"), size))
    return {"video": encode_sequence(scene, files, path), "sequence": sequence_dir, "frames": total,
            "passSeconds": duration, "fps": fps}


def encode_sequence(scene, files, path):
    """Encode PNGs to H.264 through Blender's own FFmpeg, via a temporary sequencer scene."""
    video = bpy.data.scenes.new("encode")
    video.render.resolution_x, video.render.resolution_y = scene.render.resolution_x, scene.render.resolution_y
    video.render.resolution_percentage = 100
    video.render.fps, video.render.fps_base = scene.render.fps, scene.render.fps_base
    video.view_settings.view_transform = "Standard"
    editor = video.sequence_editor_create()
    strips = editor.strips if hasattr(editor, "strips") else editor.sequences
    strip = strips.new_image("frames", files[0], channel=1, frame_start=1)
    for file in files[1:]:
        strip.elements.append(os.path.basename(file))
    video.frame_start, video.frame_end = 1, len(files)
    settings = video.render.image_settings
    settings.media_type = "VIDEO"
    settings.file_format = "FFMPEG"
    video.render.ffmpeg.format = "MPEG4"
    video.render.ffmpeg.codec = "H264"
    video.render.ffmpeg.constant_rate_factor = "HIGH"
    video.render.filepath = path
    video.render.use_sequencer = True
    with bpy.context.temp_override(scene=video):
        bpy.ops.render.render(animation=True, scene=video.name)
    bpy.data.scenes.remove(video)
    produced = [p for p in (path, path + ".mp4") if os.path.isfile(p)]
    return produced[0] if produced else None


# ---------------------------------------------------------------- report

def write_markdown(report, path):
    lines = [f"# Reference scene report", "",
             f"Blender {report['blender']['version']} ({report['blender']['hash']}), glTF importer "
             f"{report['blender']['gltfImporter']}. Result: **{report['result']}**.", ""]
    for clip in report["clips"]:
        facts = clip["fileFacts"]
        lines += [f"## {clip['name']}", "",
                  f"- file: {facts['triangles']} triangles, {facts['vertices']} vertices, "
                  f"{facts['morphTargets'] + 1} frames over {clip.get('durationSeconds', 0):.3f} s, "
                  f"{facts['interpolation']}",
                  *[f"- object {o['object']}: {o['vertices']} vertices, {o['polygons']} faces, "
                    f"{o['shapeKeys']} shape keys, action {o['action']!r} slot {o['actionSlot']!r}, "
                    f"{o['fcurves']} F-curves, {o['interpolation']}; materials "
                    + ", ".join(f"{m['name']} <- {[i['name'] for i in m['images']]}" for m in o["materials"])
                    for o in clip["objects"]],
                  "", "| t (s) | frame | vertices | min | max | max displacement |", "|---|---|---|---|---|---|",
                  *[f"| {s['time']} | {s['frame']} | {s['vertices']} | {s['min']} | {s['max']} | {s['maxDisplacement']} |"
                    for s in clip["samples"]], ""]
    lines += ["## Checks", ""] + [f"- **{c['level']}** {c['subject']}: {c['message']}" for c in report["checks"]]
    if "evidence" in report:
        lines += ["", "## Evidence", ""]
        for kind, value in report["evidence"].items():
            if isinstance(value, dict):
                for key, item in value.items():
                    shown = item if isinstance(item, str) else f"{len(item)} files"
                    lines.append(f"- {kind}/{key}: {shown}")
    with open(path, "w", encoding="utf-8") as stream:
        stream.write("\n".join(lines) + "\n")


def main():
    args, clip_inputs = parse_args()
    version = bpy.app.version
    if tuple(version[:2]) != PINNED and not args.allow_version:
        sys.exit(f"Blender {bpy.app.version_string} is not the pinned {PINNED[0]}.{PINNED[1]}.x "
                 "(tools/blender/README.md); pass --allow-version to run anyway")
    os.makedirs(args.out, exist_ok=True)
    checks = Checks()
    scene, original, cameras = fresh_scene()
    import addon_utils
    gltf = next((m.bl_info.get("version") for m in addon_utils.modules() if m.__name__ == "io_scene_gltf2"), None)

    clips, reports = [], []
    for name, path, staged in clip_inputs:
        if not checks.check(os.path.isfile(path), name, f"GLB exists: {path}"):
            continue
        glb = glb_facts(path)
        checks.check(all(i["exists"] for i in glb["images"]), name,
                     f"every image the GLB names exists: {[i['uri'] for i in glb['images']]}")
        collection, objects = import_clip(scene, original, name, path)
        clip_report = inspect_clip(scene, name, path, objects, glb, checks, args.samples)
        if staged:
            clip_report["catalogueKeys"] = staged.get("keys")
            clip_report["sourceKmf"] = staged.get("source")
        reports.append(clip_report)
        clips.append({"name": name, "glb": glb, "meshes": [o for o in objects if o.type == "MESH"]})

    report = {
        "blender": {"version": bpy.app.version_string, "hash": bpy.app.build_hash.decode()
                    if isinstance(bpy.app.build_hash, bytes) else bpy.app.build_hash,
                    "gltfImporter": ".".join(map(str, gltf)) if gltf else None},
        "sources": os.path.abspath(args.sources) if args.sources else None,
        "clips": reports,
    }
    blend = os.path.join(os.path.abspath(args.out), args.blend)
    if not checks.failed and not args.no_render and clips:
        render_evidence(scene, original, cameras, clips, args, report)
    for child in original.children:
        child.hide_render = False
    scene.frame_set(0)
    bpy.context.preferences.filepaths.save_version = 0    # no .blend1 beside a generated file
    bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
    report["blend"] = blend
    report["checks"] = checks.items
    report["result"] = "FAIL" if checks.failed else "PASS"
    with open(os.path.join(args.out, "report.json"), "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
    write_markdown(report, os.path.join(args.out, "report.md"))
    print(f"-- {report['result']}: {len(reports)} clip(s); {blend}")
    if checks.failed:
        sys.exit(1)


if __name__ == "__main__":
    main()
