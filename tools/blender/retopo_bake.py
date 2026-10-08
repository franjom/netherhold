"""Turn a dense generated creature mesh into a light game mesh carrying the dense one's detail.

Run inside Blender 5.2 (headless):
    blender -b --factory-startup --python-exit-code 1 --python tools/blender/retopo_bake.py -- \
        --mesh <candidate.glb> --out <new folder> [--target-faces 6000] [--bake-size 4096] [--texture-size 2048]

Steps, all automatic and repeatable:
1. import the candidate (the high mesh) and centre it: X symmetric about 0, feet on Z 0, facing -Y;
2. a remesh source: voxel remesh (watertight, one surface), the voxel coarsened until it gives
   about --pre-faces faces;
3. QuadriFlow quad remesh to --target-faces quads, mirrored in X, then shrink-wrapped onto the
   high mesh: the low mesh;
4. UVs: smart projection, packed with a margin for the final texture size;
5. Cycles bakes from high to low at --bake-size: base colour, tangent-space normal, roughness and
   metallic; each map is also written downscaled to --texture-size, and roughness and metallic
   are packed into one texture (green, blue) as glTF expects;
6. renders and sheets (high against low in colour; low in clay without the normal map; low
   wireframe) and a gameplay-size view;
7. exports the low mesh with the downscaled maps as GLB, saves both meshes in a .blend, and
   writes report.json.

This is not a rig and does not judge deformation. QuadriFlow places quads evenly, not along the
loops a rig wants around eyes, mouth and joints; that is checked when rigging starts. Lighting
painted into the candidate's base colour is baked across unchanged.
"""
import argparse
import hashlib
import json
import math
import os
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reference_scene as ref          # noqa: E402
import review_renders as rr            # noqa: E402
import sheets                          # noqa: E402

VIEWS = {"front": (0, 6), "front_3q_right": (40, 10), "right": (90, 6), "rear": (180, 6)}
HEAD_VIEWS = {"front": (0, 2), "front_3q_right": (40, 6), "profile_right": (90, 2)}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--mesh", required=True)
    p.add_argument("--out", required=True, help="a new folder; an existing one is refused")
    p.add_argument("--target-faces", type=int, default=6000, help="QuadriFlow quads (about twice as many triangles)")
    p.add_argument("--pre-faces", type=int, default=300000, help="triangles handed to QuadriFlow")
    p.add_argument("--voxel", type=float, default=1 / 600, help="voxel size as a share of the creature's height")
    p.add_argument("--bake-size", type=int, default=4096)
    p.add_argument("--texture-size", type=int, default=2048)
    p.add_argument("--no-symmetry", action="store_true")
    a = p.parse_args(argv)
    a.mesh, a.out = os.path.abspath(a.mesh), os.path.abspath(a.out)
    return a


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def activate(obj, *others):
    bpy.ops.object.select_all(action="DESELECT")
    for o in (obj, *others):
        o.select_set(True)
    bpy.context.view_layer.objects.active = obj


def bounds(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)])


# ---------------------------------------------------------------- steps

def import_high(path, collection):
    bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[collection.name]
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    high = max((o for o in new if o.type == "MESH"), key=lambda o: len(o.data.polygons))
    activate(high)
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in new:
        if o is not high:
            bpy.data.objects.remove(o)
    low, high_corner = bounds(high)
    shift = Vector(((low.x + high_corner.x) / -2, (low.y + high_corner.y) / -2, -low.z))
    high.data.transform(Matrix.Translation(shift))
    high.data.update()
    high.name = "high"
    return high, high_corner.z - low.z


def remesh_source(high, height, args, collection):
    src = high.copy()
    src.data = high.data.copy()
    src.name = "remesh_source"
    collection.objects.link(src)
    src.data.materials.clear()
    # QuadriFlow needs a manifold, consistently oriented surface. A voxel remesh gives one;
    # decimating it afterwards does not, so the voxel size is coarsened instead until the
    # remesh alone lands near --pre-faces (faces scale with 1 / voxel size squared).
    m = src.modifiers.new("voxel", "REMESH")
    m.mode = "VOXEL"
    m.adaptivity = 0.0
    voxel = height * args.voxel
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for _ in range(4):
        m.voxel_size = voxel
        depsgraph.update()
        faces = len(src.evaluated_get(depsgraph).data.polygons)
        if faces <= args.pre_faces * 1.2:
            break
        voxel *= math.sqrt(faces / args.pre_faces)
    activate(src)
    bpy.ops.object.modifier_apply(modifier="voxel")
    # The remesh can leave specks (a 6-face fragment on t1-tripo-web) that QuadriFlow rejects:
    # keep pieces holding at least 1% of the faces, and point every normal outward.
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.faces.ensure_lookup_table()
    seen, pieces = set(), []
    for face in bm.faces:
        if face.index in seen:
            continue
        stack, piece = [face], []
        seen.add(face.index)
        while stack:
            f = stack.pop()
            piece.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        seen.add(g.index)
                        stack.append(g)
        pieces.append(piece)
    specks = [f for p in pieces if len(p) < 0.01 * len(bm.faces) for f in p]
    bmesh.ops.delete(bm, geom=specks, context="FACES")
    # QuadriFlow also refuses any edge shorter than 1e-4 in absolute units, which the remesh
    # leaves on a creature about 1 unit tall (262 on t1-tripo-web, the shortest 3e-7).
    merge = max(2e-4, height * 2e-4)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=merge)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(src.data)
    bm.free()
    return src, {"faces": len(src.data.polygons), "voxel_size": round(voxel, 5),
                 "pieces": len(pieces), "speck_faces_removed": len(specks)}


def quadriflow(src, args):
    activate(src)
    src.data.use_mirror_x = not args.no_symmetry
    src.data.use_mirror_y = src.data.use_mirror_z = False
    t = time.time()
    result = bpy.ops.object.quadriflow_remesh(use_mesh_symmetry=not args.no_symmetry, use_preserve_sharp=False,
                                              use_preserve_boundary=False, preserve_attributes=False,
                                              smooth_normals=False, mode="FACES", target_faces=args.target_faces, seed=0)
    if "FINISHED" not in result or len(src.data.polygons) > args.pre_faces // 2:
        sys.exit(f"QuadriFlow did not finish: {result}")
    src.name = "low"
    src.data.name = "low"
    return src, time.time() - t


def fit_and_unwrap(low, high, texture_size):
    w = low.modifiers.new("fit", "SHRINKWRAP")
    w.target = high
    w.wrap_method = "NEAREST_SURFACEPOINT"
    activate(low)
    bpy.ops.object.modifier_apply(modifier="fit")
    bpy.ops.object.shade_smooth()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    margin = 4 / texture_size
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=margin, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def high_inputs(high):
    """The high material's Principled node and what feeds its metallic input."""
    material = high.data.materials[0]
    principled = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    return material, principled


def bake(low, high, args, maps_dir, height):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    bk = scene.render.bake
    bk.use_selected_to_active = True
    bk.use_cage = False
    bk.cage_extrusion = height * 0.01
    bk.max_ray_distance = height * 0.03
    bk.margin = 16
    bk.margin_type = "EXTEND"

    material = bpy.data.materials.new("low")
    material.use_backface_culling = True          # a closed body: glTF doubleSided false, no back faces drawn
    low.data.materials.clear()
    low.data.materials.append(material)
    nt = material.node_tree
    principled = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    images, timings = {}, {}
    plan = (("base_color", "DIFFUSE", "sRGB"), ("normal", "NORMAL", "Non-Color"),
            ("roughness", "ROUGHNESS", "Non-Color"), ("metallic", "EMIT", "Non-Color"))
    high_material, high_principled = high_inputs(high)
    for name, kind, space in plan:
        image = bpy.data.images.new(f"low_{name}", args.bake_size, args.bake_size, alpha=False)
        image.colorspace_settings.name = space
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = image
        node.name = name
        nt.nodes.active = node
        restore = None
        if kind == "EMIT":
            # Metallic cannot be baked directly: route the high material's metallic into emission.
            links = high_material.node_tree.links
            output = next(n for n in high_material.node_tree.nodes if n.type == "OUTPUT_MATERIAL")
            emission = high_material.node_tree.nodes.new("ShaderNodeEmission")
            metal_in = high_principled.inputs["Metallic"]
            if metal_in.links:
                links.new(metal_in.links[0].from_socket, emission.inputs["Color"])
            else:
                emission.inputs["Color"].default_value = (*(metal_in.default_value,) * 3, 1)
            old = output.inputs["Surface"].links[0].from_socket
            links.new(emission.outputs[0], output.inputs["Surface"])
            restore = (links, old, output, emission)
        activate(low, high)
        t = time.time()
        kwargs = {"type": kind}
        if kind == "DIFFUSE":
            kwargs["pass_filter"] = {"COLOR"}
        if kind == "NORMAL":
            kwargs["normal_space"] = "TANGENT"
        bpy.ops.object.bake(**kwargs)
        timings[name] = round(time.time() - t, 1)
        if restore:
            links, old, output, emission = restore
            links.new(old, output.inputs["Surface"])
            high_material.node_tree.nodes.remove(emission)
        full = os.path.join(maps_dir, f"{name}_{args.bake_size}.png")
        image.filepath_raw = full
        image.file_format = "PNG"
        image.save()
        small = image.copy()
        small.name = f"low_{name}_{args.texture_size}"
        small.scale(args.texture_size, args.texture_size)
        small.filepath_raw = os.path.join(maps_dir, f"{name}_{args.texture_size}.png")
        small.file_format = "PNG"
        small.save()
        node.image = small
        images[name] = node
    # glTF (and so the GLB) carries roughness and metallic in one texture, green and blue.
    size = args.texture_size
    channel = lambda node: np.array(node.image.pixels[:], dtype=np.float32).reshape(-1, node.image.channels)[:, 0]
    packed = np.ones((size * size, 4), dtype=np.float32)
    packed[:, 1], packed[:, 2] = channel(images["roughness"]), channel(images["metallic"])
    orm = bpy.data.images.new(f"low_metal_rough_{size}", size, size, alpha=False)
    orm.colorspace_settings.name = "Non-Color"
    orm.pixels.foreach_set(packed.ravel())
    orm.filepath_raw = os.path.join(maps_dir, f"metal_rough_{size}.png")
    orm.file_format = "PNG"
    orm.save()
    for name in ("roughness", "metallic"):
        nt.nodes.remove(images.pop(name))
    orm_node = nt.nodes.new("ShaderNodeTexImage")
    orm_node.image = orm
    split = nt.nodes.new("ShaderNodeSeparateColor")
    links = nt.links
    links.new(orm_node.outputs["Color"], split.inputs["Color"])
    links.new(images["base_color"].outputs["Color"], principled.inputs["Base Color"])
    links.new(split.outputs["Green"], principled.inputs["Roughness"])
    links.new(split.outputs["Blue"], principled.inputs["Metallic"])
    normal_map = nt.nodes.new("ShaderNodeNormalMap")
    links.new(images["normal"].outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])
    return timings


# ---------------------------------------------------------------- evidence

def wire_copy(low, collection, height):
    wire = low.copy()
    wire.data = low.data.copy()
    wire.name = "low_wire"
    collection.objects.link(wire)
    m = wire.modifiers.new("wire", "WIREFRAME")
    m.thickness = height * 0.0012
    m.use_replace = True
    mat = bpy.data.materials.new("wire")
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.02, 0.02, 0.02, 1)
    wire.data.materials.clear()
    wire.data.materials.append(mat)
    return wire


def shoot(scene, camera, box, views, folder, tag, size):
    return [rr.still(scene, camera, box, view, os.path.join(folder, f"{tag}_{name}.png"), size) for name, view in views.items()]


def show(objs, *visible):
    for o in objs:
        o.hide_render = o not in visible


def main():
    args = parse_args()
    if os.path.exists(args.out):
        sys.exit(f"{args.out} exists; every run gets a new folder")
    maps_dir, renders, out_sheets = (os.path.join(args.out, d) for d in ("maps", "renders", "sheets"))
    for d in (maps_dir, renders, out_sheets):
        os.makedirs(d)
    report = {"blender": bpy.app.version_string, "source": {"file": args.mesh, "sha256": sha256(args.mesh)},
              "params": {k: v for k, v in vars(args).items() if k not in ("mesh", "out")}}
    t0 = time.time()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    cols = {}
    for name in ("high", "low", "work", "cameras"):
        cols[name] = bpy.data.collections.new(name)
        scene.collection.children.link(cols[name])

    high, height = import_high(args.mesh, cols["high"])
    report["high"] = {"triangles": tris(high), "vertices": len(high.data.vertices), "height": round(height, 4)}
    src, voxel = remesh_source(high, height, args, cols["work"])
    report["remesh_source"] = voxel
    print(f"-- remesh source: {voxel}")
    low, qf_time = quadriflow(src, args)
    cols["work"].objects.unlink(low)
    cols["low"].objects.link(low)
    report["quadriflow_seconds"] = round(qf_time, 1)
    fit_and_unwrap(low, high, args.texture_size)
    sizes = [len(p.vertices) for p in low.data.polygons]
    report["low"] = {"faces": len(sizes), "quads": sum(s == 4 for s in sizes), "triangles_other": sum(s == 3 for s in sizes),
                     "ngons": sum(s > 4 for s in sizes), "triangles": tris(low), "vertices": len(low.data.vertices),
                     "uv_layers": [u.name for u in low.data.uv_layers]}
    print(f"-- low mesh: {report['low']}")
    report["bake_seconds"] = bake(low, high, args, maps_dir, height)
    report["maps"] = {f: sha256(os.path.join(maps_dir, f)) for f in sorted(os.listdir(maps_dir))}

    # renders
    camera, lights = ref.setup_render(scene, cols["cameras"])
    wire = wire_copy(low, cols["low"], height)
    objs = [high, low, wire]
    body = ((-0.5 * height, -0.4 * height, 0), (0.5 * height, 0.4 * height, height))
    head = ((-0.3 * height, -0.3 * height, 0.62 * height), (0.3 * height, 0.25 * height, height))
    ref.set_mode(scene, True)
    scene.render.engine = "BLENDER_EEVEE"
    rows = {}
    show(objs, high); rows["high (generated), colour"] = shoot(scene, camera, body, VIEWS, renders, "high_colour", 700)
    show(objs, low); rows["low, baked maps"] = shoot(scene, camera, body, VIEWS, renders, "low_colour", 700)
    baked = low.data.materials[0]
    clay = bpy.data.materials.new("low_clay")
    clay.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.42, 0.42, 1)
    clay.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.65
    low.data.materials[0] = clay
    rows["low, clay, no normal map (the real geometry)"] = shoot(scene, camera, body, VIEWS, renders, "low_clay", 700)
    show(objs, low, wire); rows["low, edges"] = shoot(scene, camera, body, VIEWS, renders, "low_wire", 700)
    low.data.materials[0] = baked
    labels = [f"{r}: {v}" for r in rows for v in VIEWS]
    sheets.save(sheets.grid([sheets.load(p) for r in rows.values() for p in r], len(VIEWS), labels,
                            f"High {report['high']['triangles']:,} triangles against low {report['low']['triangles']:,}"),
                os.path.join(out_sheets, "body.png"))
    hrows = {}
    show(objs, high); hrows["high"] = shoot(scene, camera, head, HEAD_VIEWS, renders, "head_high", 700)
    show(objs, low); hrows["low, baked"] = shoot(scene, camera, head, HEAD_VIEWS, renders, "head_low", 700)
    low.data.materials[0] = clay; show(objs, low, wire)
    hrows["low, edges"] = shoot(scene, camera, head, HEAD_VIEWS, renders, "head_wire", 700)
    low.data.materials[0] = baked
    sheets.save(sheets.grid([sheets.load(p) for r in hrows.values() for p in r], len(HEAD_VIEWS),
                            [f"{r}: {v}" for r in hrows for v in HEAD_VIEWS], "Head: high against low"),
                os.path.join(out_sheets, "head.png"))
    game = []
    for o, tag in ((high, "high"), (low, "low")):
        show(objs, o)
        p = rr.still(scene, camera, body, (25, 55), os.path.join(renders, f"game_{tag}.png"), 24)
        game.append(sheets.fit(sheets.nearest(sheets.load(p), 24), 576, 576))
    sheets.save(sheets.grid(game, 2, ["high at gameplay size (24 px), enlarged", "low at gameplay size, enlarged"],
                            "Gameplay size"), os.path.join(out_sheets, "gameplay.png"))

    # outputs
    show(objs, low)
    activate(low)
    glb = os.path.join(args.out, "imp_low.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_image_format="AUTO")
    report["glb"] = {"file": glb, "sha256": sha256(glb), "bytes": os.path.getsize(glb)}
    bpy.data.objects.remove(wire)
    for o in list(cols["work"].objects):
        bpy.data.objects.remove(o)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(args.out, "retopo.blend"), compress=True)
    report["seconds"] = round(time.time() - t0, 1)
    report["sheets"] = sorted(os.listdir(out_sheets))
    with open(os.path.join(args.out, "report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)
    print("-- done: " + json.dumps({k: report[k] for k in ("low", "quadriflow_seconds", "bake_seconds", "seconds")}))


if __name__ == "__main__":
    main()
