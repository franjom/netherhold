"""Add the Imp's leather foot straps and wrap studs to a retopologised candidate (Blender 5.2).

    blender -b --factory-startup --python-exit-code 1 --python tools/blender/imp_footwear.py -- \
        --blend <retopo .blend> --out <new folder> [--bend <deg> ...]

Owner decisions, 2026-10-07 (docs/plans/2026-10-07-imp-footwear.md):
- add a leather strap per foot, as in the DK2 portrait and the approved concepts;
- the cloth ankle wraps stay part of the body;
- the wraps' studs and the straps' rivets are painted detail: small metal domes exist only as bake
  sources, so they reach the colour, normal and metal maps but add no game geometry;
- the leather is packed into the body's texture: one material, one texture set.

The strap is a band of the foot's own surface: two parallel tilted planes cut the light body mesh
around the midfoot (over the instep, under the arch, up both sides), the two contours are joined
into a strip, lifted off the skin and given thickness. The input .blend is only read; every
object it holds is preserved in the output beside the new ones.
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
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reference_scene as ref          # noqa: E402
import review_renders as rr            # noqa: E402
import sheets                          # noqa: E402

# Concept strap leather, sampled from front_side_back.png (sRGB medians, 2026-10-07), linearised.
LEATHER_DARK = (0.047, 0.018, 0.007)
LEATHER_MID = (0.16, 0.055, 0.021)
LEATHER_LIGHT = (0.41, 0.15, 0.05)
METAL = (0.75, 0.72, 0.66)
# Shares of the creature's height (0.98 for t1-tripo-web), measured on its feet.
STRAP_WIDTH = 0.055
STRAP_GAP = 0.0025
STRAP_THICKNESS = 0.0028
STUD_RADIUS = 0.012
RIVET_RADIUS = 0.0045
FEET_VIEWS = {"front": (0, 6), "front_3q_right": (40, 12), "right": (90, 4), "rear": (180, 6),
              "left": (270, 4), "front_3q_left": (320, 12)}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--blend", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--bend", type=float, nargs="*", default=[-35, -20, 0, 20, 35],
                   help="ankle pitches for the bend test, degrees, positive toes up")
    p.add_argument("--bake-size", type=int, default=4096)
    p.add_argument("--texture-size", type=int, default=2048)
    p.add_argument("--segments", type=int, default=56, help="strap segments around the foot")
    a = p.parse_args(argv)
    a.blend, a.out = os.path.abspath(a.blend), os.path.abspath(a.out)
    return a


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def coords(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def activate(obj, *others):
    bpy.ops.object.select_all(action="DESELECT")
    for o in (obj, *others):
        o.select_set(True)
    bpy.context.view_layer.objects.active = obj


def bvh(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    return tree


# ---------------------------------------------------------------- foot landmarks

def foot_frame(body, side, height, edge):
    """Landmarks of one foot (side -1: the creature's right, x < 0): the arch from the light mesh,
    the strap's top from the wrap's front edge (`wrap_edge`)."""
    co = coords(body)
    foot = co[(np.sign(co[:, 0]) == side) & (np.abs(co[:, 0]) > 0.04 * height) & (co[:, 2] < 0.16 * height)]
    centre_x = float(np.median(foot[:, 0]))
    ys = np.arange(foot[:, 1].min(), foot[:, 1].max(), 0.01 * height)
    sole = []
    for y in ys:
        s = foot[(foot[:, 1] >= y) & (foot[:, 1] < y + 0.01 * height) & (np.abs(foot[:, 0] - centre_x) < 0.04 * height)]
        if len(s):
            sole.append((y, s[:, 2].min(), s[:, 2].max()))
    sole = np.array(sole)
    # The arch: the highest underside between the ball (front, -Y) and the heel.
    middle = sole[(sole[:, 0] > np.percentile(ys, 35)) & (sole[:, 0] < np.percentile(ys, 75))]
    arch = middle[np.argmax(middle[:, 1])]
    # The concepts' strap crosses the instep just below the wrap's front edge and passes under the
    # arch: its upper rim starts at that edge, so its centre lies half a width down the instep.
    edge_y, edge_z, wrap_bottom = edge
    down = 0.35 * STRAP_WIDTH * height                         # half the width along a ~45 degree instep
    return {"centre_x": centre_x, "arch": (float(arch[0]), float(arch[1])),
            "instep": (edge_y - down, edge_z - down), "wrap_front": (edge_y, edge_z),
            "top_cut": wrap_bottom + 0.01 * height}


def wrap_edge(high, side, height):
    """The cloth wrap's front lower edge (y, z) and its lowest z, from the high mesh's base colour:
    the wrap is the low-saturation band above the foot."""
    me = high.data
    img = next(n.image for n in me.materials[0].node_tree.nodes
               if n.type == "TEX_IMAGE" and "basecolor" in n.image.name.lower())
    w, h = img.size
    px = np.array(img.pixels[:], np.float32).reshape(h, w, img.channels)[..., :3]
    uv = np.empty(len(me.loops) * 2); me.uv_layers.active.data.foreach_get("uv", uv); uv = uv.reshape(-1, 2)
    lv = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", lv)
    p = coords(high)[lv]
    near = (np.sign(p[:, 0]) == side) & (np.abs(p[:, 0]) > 0.04 * height) & (p[:, 2] > 0.08 * height) & (p[:, 2] < 0.25 * height)
    p, uv = p[near], uv[near]
    c = px[np.clip((uv[:, 1] % 1 * h).astype(int), 0, h - 1), np.clip((uv[:, 0] % 1 * w).astype(int), 0, w - 1)]
    sat = (c.max(1) - c.min(1)) / np.maximum(c.max(1), 1e-6)
    cloth = p[(sat < 0.22) & (c.max(1) > 0.25)]
    bottom = float(np.percentile(cloth[:, 2], 3))
    low = cloth[cloth[:, 2] < bottom + 0.012 * height]
    front = low[np.argmin(low[:, 1])]                          # the toes lie at -Y
    return float(front[1]), float(front[2]), bottom


# ---------------------------------------------------------------- strap geometry

def contour(body, side, height, point, normal):
    """The light mesh cut by a plane, limited to one foot: an ordered list of points."""
    bm = bmesh.new()
    bm.from_mesh(body.data)
    far = [v for v in bm.verts if np.sign(v.co.x) != side or v.co.z > 0.2 * height]
    bmesh.ops.delete(bm, geom=far, context="VERTS")
    cut = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-6,
                                 plane_co=point, plane_no=normal)
    on = {v for v in cut["geom_cut"] if isinstance(v, bmesh.types.BMVert)}
    links = {v: [] for v in on}
    for e in cut["geom_cut"]:
        if isinstance(e, bmesh.types.BMEdge) and e.verts[0] in on and e.verts[1] in on:
            links[e.verts[0]].append(e.verts[1])
            links[e.verts[1]].append(e.verts[0])
    best = []
    seen = set()
    for start in links:
        if start in seen:
            continue
        path, prev, cur = [start], None, start
        seen.add(start)
        while True:
            nxt = [n for n in links[cur] if n is not prev and n not in seen]
            if not nxt:
                break
            prev, cur = cur, nxt[0]
            seen.add(cur)
            path.append(cur)
        if len(path) > len(best):
            best = path
    pts = [v.co.copy() for v in best]
    bm.free()
    return pts


def sample_by_angle(pts, centre, e1, e2, angles):
    ang = np.array([math.atan2((p - centre).dot(e2), (p - centre).dot(e1)) for p in pts])
    order = np.argsort(ang)
    ang, pts = ang[order], [pts[i] for i in order]
    out = []
    for a in angles:
        i = int(np.searchsorted(ang, a)) % len(pts)
        j = (i - 1) % len(pts)
        a0, a1 = ang[j], ang[i]
        span = (a1 - a0) % (2 * math.pi) or 1e-9
        t = ((a - a0) % (2 * math.pi)) / span
        out.append(pts[j].lerp(pts[i], min(max(t, 0.0), 1.0)))
    return out


def build_strap(body, high_tree, side, height, segments, name, lm):
    (ay, az), (iy, iz) = lm["arch"], lm["instep"]
    along = Vector((0, iy - ay, iz - az)).normalized()        # up the band, from arch to instep
    normal = Vector((1, 0, 0)).cross(along).normalized()      # plane normal, tilted forward
    centre = Vector((lm["centre_x"], (ay + iy) / 2, (az + iz) / 2))
    half = STRAP_WIDTH * height / 2
    e1, e2 = Vector((1, 0, 0)), normal.cross(Vector((1, 0, 0))).normalized()
    angles = np.linspace(-math.pi, math.pi, segments, endpoint=False)
    rims = [sample_by_angle(contour(body, side, height, centre + normal * d, normal), centre + normal * d, e1, e2, angles)
            for d in (-half, half)]
    tree = bvh(body)
    keep = [k for k in range(segments) if max(rims[0][k].z, rims[1][k].z) < lm["top_cut"]]
    # The kept run is one arc: rotate it so it starts after the gap at the top.
    gap = [k for k in range(segments) if k not in keep]
    start = (max(gap) + 1) % segments if gap else 0
    run = [(start + i) % segments for i in range(segments) if (start + i) % segments in keep]
    verts, uvs = [], []
    length = 0.0
    prev = None
    for k in run:
        mid = (rims[0][k] + rims[1][k]) / 2
        if prev is not None:
            length += (mid - prev).length
        prev = mid
        for r, v in ((rims[0][k], 0.0), (rims[1][k], 1.0)):
            loc, nrm, _, _ = tree.find_nearest(r)
            p = r + nrm * STRAP_GAP * height
            # The dense mesh lies up to a few millimetres outside the light one: clear it as well,
            # or its skin is what the bake sees under the strap.
            hloc, hnrm, _, _ = high_tree.find_nearest(p)
            above = (p - hloc).dot(hnrm)
            if above < STRAP_GAP * height:
                p = p + nrm * (STRAP_GAP * height - above)
            verts.append(p)
            uvs.append((length, v * 2 * half))
    faces = [(2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1) for i in range(len(run) - 1)]
    if not gap:                                                # a closed ring over the instep
        faces.append((2 * len(run) - 2, 0, 1, 2 * len(run) - 1))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))   # flat faces bake as facets
    mesh.update()
    # Outward winding: the first face's normal must point away from the foot's centre.
    f0 = mesh.polygons[0]
    if f0.normal.dot(f0.center - centre) < 0:
        mesh.flip_normals()
    uv = mesh.uv_layers.new(name="UVMap")
    param = mesh.uv_layers.new(name="strap_param")
    for poly in mesh.polygons:
        for li, vi in zip(poly.loop_indices, poly.vertices):
            uv.data[li].uv = uvs[vi]
            param.data[li].uv = (uvs[vi][0] / max(length, 1e-9), uvs[vi][1] / (2 * half))
    obj = bpy.data.objects.new(name, mesh)
    obj["landmarks"] = json.dumps({k: (list(v) if isinstance(v, tuple) else v) for k, v in lm.items()})
    return obj, {"segments": len(run), "length": round(length, 4), "landmarks": lm}


# ---------------------------------------------------------------- bake sources

def leather_material(length, width):
    m = bpy.data.materials.new("leather_source")
    nt = m.node_tree
    p = nt.nodes["Principled BSDF"]
    uv = nt.nodes.new("ShaderNodeUVMap"); uv.uv_map = "strap_param"
    # The grain is laid out on the strap's own UVs, scaled to metres, so it is even along the band.
    metres = nt.nodes.new("ShaderNodeMapping"); metres.inputs["Scale"].default_value = (length, width, 1)
    nt.links.new(uv.outputs["UV"], metres.inputs["Vector"])
    noise = nt.nodes.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 180; noise.inputs["Detail"].default_value = 8
    nt.links.new(metres.outputs["Vector"], noise.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (*LEATHER_DARK, 1); ramp.color_ramp.elements[0].position = 0.3
    ramp.color_ramp.elements[1].color = (*LEATHER_LIGHT, 1); ramp.color_ramp.elements[1].position = 0.75
    mid = ramp.color_ramp.elements.new(0.52); mid.color = (*LEATHER_MID, 1)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    # Darker, worn edges across the strap's width.
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(uv.outputs["UV"], sep.inputs["Vector"])
    edge = nt.nodes.new("ShaderNodeMath"); edge.operation = "PINGPONG"; edge.inputs[1].default_value = 0.5
    nt.links.new(sep.outputs["Y"], edge.inputs[0])
    wear = nt.nodes.new("ShaderNodeMapRange")
    wear.inputs["From Min"].default_value = 0.0; wear.inputs["From Max"].default_value = 0.12
    wear.inputs["To Min"].default_value = 0.45; wear.inputs["To Max"].default_value = 1.0
    nt.links.new(edge.outputs[0], wear.inputs["Value"])
    mul = nt.nodes.new("ShaderNodeMix"); mul.data_type = "RGBA"; mul.blend_type = "MULTIPLY"
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(ramp.outputs["Color"], mul.inputs["A"]); nt.links.new(wear.outputs["Result"], mul.inputs["B"])
    nt.links.new(mul.outputs["Result"], p.inputs["Base Color"])
    rough = nt.nodes.new("ShaderNodeMapRange")
    rough.inputs["To Min"].default_value = 0.55; rough.inputs["To Max"].default_value = 0.8
    nt.links.new(noise.outputs["Fac"], rough.inputs["Value"]); nt.links.new(rough.outputs["Result"], p.inputs["Roughness"])
    p.inputs["Metallic"].default_value = 0.0
    bump = nt.nodes.new("ShaderNodeBump"); bump.inputs["Strength"].default_value = 0.35; bump.inputs["Distance"].default_value = 0.0006
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"]); nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    return m


def metal_material():
    m = bpy.data.materials.new("metal_source")
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*METAL, 1)
    p.inputs["Metallic"].default_value = 0.85
    p.inputs["Roughness"].default_value = 0.32
    return m


def dome(name, location, normal, radius, material, collection):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=radius, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_euler = normal.to_track_quat("Z", "Y").to_euler()
    obj.scale = (1, 1, 0.45)
    obj.data.materials.append(material)
    for c in obj.users_collection:
        c.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def wrap_stud_site(high, side, height):
    """The outer side of an ankle wrap, halfway up it: the concepts' stud."""
    co = coords(high)
    ring = co[(np.sign(co[:, 0]) == side) & (np.abs(co[:, 2] - 0.155 * height) < 0.004 * height)
              & (np.abs(co[:, 0]) > 0.04 * height)]
    outer = ring[np.argmax(ring[:, 0] * side)]
    centre = ring.mean(0)
    n = Vector((outer[0] - centre[0], outer[1] - centre[1], 0)).normalized()
    return Vector(outer), n


# ---------------------------------------------------------------- bake

def route_metallic(material):
    """Emission carries metallic during the EMIT bake; returns an undo closure."""
    nt = material.node_tree
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    em = nt.nodes.new("ShaderNodeEmission")
    src = p.inputs["Metallic"]
    if src.links:
        nt.links.new(src.links[0].from_socket, em.inputs["Color"])
    else:
        em.inputs["Color"].default_value = (src.default_value,) * 3 + (1,)
    old = out.inputs["Surface"].links[0].from_socket
    nt.links.new(em.outputs[0], out.inputs["Surface"])

    def undo():
        nt.links.new(old, out.inputs["Surface"])
        nt.nodes.remove(em)
    return undo


def bake_all(target, sources, args, maps_dir, height):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    bk = scene.render.bake
    bk.use_selected_to_active, bk.use_cage = True, False
    bk.cage_extrusion, bk.max_ray_distance = height * 0.01, height * 0.03
    bk.margin, bk.margin_type = 16, "EXTEND"
    material = bpy.data.materials.new("imp")
    material.use_backface_culling = True
    target.data.materials.clear()
    target.data.materials.append(material)
    nt = material.node_tree
    p = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    nodes, timings = {}, {}
    for name, kind, space in (("base_color", "DIFFUSE", "sRGB"), ("normal", "NORMAL", "Non-Color"),
                              ("roughness", "ROUGHNESS", "Non-Color"), ("metallic", "EMIT", "Non-Color")):
        image = bpy.data.images.new(f"imp_{name}", args.bake_size, args.bake_size, alpha=False)
        image.colorspace_settings.name = space
        node = nt.nodes.new("ShaderNodeTexImage"); node.image = image; nt.nodes.active = node
        undo = [route_metallic(m) for m in {s.data.materials[0] for s in sources}] if kind == "EMIT" else []
        activate(target, *sources)
        kwargs = {"type": kind}
        if kind == "DIFFUSE":
            kwargs["pass_filter"] = {"COLOR"}
        if kind == "NORMAL":
            kwargs["normal_space"] = "TANGENT"
        t = time.time()
        bpy.ops.object.bake(**kwargs)
        timings[name] = round(time.time() - t, 1)
        for u in undo:
            u()
        image.filepath_raw = os.path.join(maps_dir, f"{name}_{args.bake_size}.png"); image.file_format = "PNG"; image.save()
        small = image.copy(); small.name = f"imp_{name}_{args.texture_size}"
        small.scale(args.texture_size, args.texture_size)
        small.filepath_raw = os.path.join(maps_dir, f"{name}_{args.texture_size}.png"); small.file_format = "PNG"; small.save()
        node.image = small
        nodes[name] = node
    size = args.texture_size
    channel = lambda n: np.array(n.image.pixels[:], np.float32).reshape(-1, n.image.channels)[:, 0]
    packed = np.ones((size * size, 4), np.float32)
    packed[:, 1], packed[:, 2] = channel(nodes["roughness"]), channel(nodes["metallic"])
    orm = bpy.data.images.new(f"imp_metal_rough_{size}", size, size, alpha=False)
    orm.colorspace_settings.name = "Non-Color"; orm.pixels.foreach_set(packed.ravel())
    orm.filepath_raw = os.path.join(maps_dir, f"metal_rough_{size}.png"); orm.file_format = "PNG"; orm.save()
    for n in ("roughness", "metallic"):
        nt.nodes.remove(nodes.pop(n))
    orm_node = nt.nodes.new("ShaderNodeTexImage"); orm_node.image = orm
    split = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(orm_node.outputs["Color"], split.inputs["Color"])
    nt.links.new(nodes["base_color"].outputs["Color"], p.inputs["Base Color"])
    nt.links.new(split.outputs["Green"], p.inputs["Roughness"])
    nt.links.new(split.outputs["Blue"], p.inputs["Metallic"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nodes["normal"].outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], p.inputs["Normal"])
    return material, timings


# ---------------------------------------------------------------- motion check

def bend_test(body, straps, landmarks, height, degrees):
    """Bend each foot about its ankle by the given pitches (a temporary deformation, not a rig)
    and measure where a strap enters the skin or lifts off."""
    results = []
    base_body = coords(body)
    for deg in degrees:
        posed = {}
        for obj in [body, *straps]:
            co = coords(obj) if obj is not body else base_body.copy()
            out = co.copy()
            for side in (-1, 1):
                lm = landmarks[side]
                pivot = np.array([lm["centre_x"], lm["instep"][0] + 0.03 * height, lm["instep"][1]])
                sel = np.sign(co[:, 0]) == side
                w = np.clip((pivot[2] + 0.04 * height - co[:, 2]) / (0.06 * height), 0, 1) * sel
                a = math.radians(-deg)                          # positive: toes up (the toes lie at -Y)
                rel = co - pivot
                rot = np.stack([rel[:, 0], rel[:, 1] * math.cos(a) - rel[:, 2] * math.sin(a),
                                rel[:, 1] * math.sin(a) + rel[:, 2] * math.cos(a)], 1) + pivot
                out = np.where(w[:, None] > 0, co + (rot - co) * w[:, None], out)
            posed[obj.name] = out
        bm = bmesh.new(); bm.from_mesh(body.data)
        for v, c in zip(bm.verts, posed[body.name]):
            v.co = c
        bm.normal_update()
        tree = BVHTree.FromBMesh(bm)
        inside, lifted, worst = 0, 0, 0.0
        total = 0
        for s in straps:
            for c in posed[s.name]:
                hit = tree.find_nearest(Vector(c))
                if hit[0] is None:
                    continue
                loc, nrm, _, dist = hit
                signed = (Vector(c) - loc).dot(nrm)
                total += 1
                if signed < 0:
                    inside += 1; worst = max(worst, -signed)
                elif signed > 0.008 * height:
                    lifted += 1
        bm.free()
        results.append({"degrees": deg, "strap_vertices": total, "inside_skin": inside, "lifted_over_8mm": lifted,
                        "deepest_mm": round(worst * 1000 / height, 2)})
        results[-1]["posed"] = posed
    return results


# ---------------------------------------------------------------- main

def main():
    args = parse_args()
    if os.path.exists(args.out):
        sys.exit(f"{args.out} exists; every run gets a new folder")
    maps_dir, renders, out_sheets = (os.path.join(args.out, d) for d in ("maps", "renders", "sheets"))
    for d in (maps_dir, renders, out_sheets):
        os.makedirs(d)
    report = {"input": {"file": args.blend, "sha256": sha256(args.blend)}, "params": {
        "strap_width": STRAP_WIDTH, "strap_gap": STRAP_GAP, "strap_thickness": STRAP_THICKNESS, "segments": args.segments}}

    bpy.ops.wm.open_mainfile(filepath=args.blend)
    scene = bpy.context.scene
    high, before = bpy.data.objects["high"], bpy.data.objects["low"]
    height = float(coords(high)[:, 2].max())
    cols = {}
    for name in ("footwear", "bake_sources", "cameras_footwear"):
        cols[name] = bpy.data.collections.new(name); scene.collection.children.link(cols[name])
    before.name = "low_before_footwear"

    # The game body: a copy of the approved light mesh, its geometry untouched.
    body = before.copy(); body.data = before.data.copy(); body.name = "IMP_body"; body.data.name = "IMP_body"
    cols["footwear"].objects.link(body)

    high_tree = bvh(high)
    right = foot_frame(body, -1, height, wrap_edge(high, -1, height))
    straps, report["straps"] = [], {}
    for side, name in ((-1, "IMP_foot_strap_R"), (1, "IMP_foot_strap_L")):
        # The light body is mirrored in X, so one foot's landmarks serve both.
        lm = dict(right, centre_x=right["centre_x"] * -side)
        strap, facts = build_strap(body, high_tree, side, height, args.segments, name, lm)
        cols["footwear"].objects.link(strap)
        straps.append(strap)
        report["straps"][name] = facts

    # Bake sources: the high mesh, a leather copy of each strap with its rivets, a stud per wrap.
    first = next(iter(report["straps"].values()))
    leather, metal = leather_material(first["length"], STRAP_WIDTH * height), metal_material()
    sources = [high]
    for strap in straps:
        src = strap.copy(); src.data = strap.data.copy(); src.name = strap.name + "_source"
        src.data.materials.clear(); src.data.materials.append(leather)
        cols["bake_sources"].objects.link(src)
        sources.append(src)
        side = 1 if coords(strap)[:, 0].mean() > 0 else -1
        # Rivets near both ends of the strap, where it meets the wrap.
        me = strap.data
        n = len(me.vertices) // 2
        for k in (1, n - 2):
            a, b = Vector(me.vertices[2 * k].co), Vector(me.vertices[2 * k + 1].co)
            centre = (a + b) / 2
            normal = me.polygons[min(k, len(me.polygons) - 1)].normal
            sources.append(dome(f"{strap.name}_rivet_{k}", centre + normal * 0.0005 * height, normal,
                                RIVET_RADIUS * height, metal, cols["bake_sources"]))
        site, n_out = wrap_stud_site(high, side, height)
        sources.append(dome(f"stud_{'L' if side > 0 else 'R'}", site + n_out * 0.001 * height, n_out,
                            STUD_RADIUS * height, metal, cols["bake_sources"]))

    # One texture set: the straps join the body for UV packing and baking, then split off again.
    for strap in straps:
        vg = strap.vertex_groups.new(name=strap.name)
        vg.add(range(len(strap.data.vertices)), 1.0, "REPLACE")
    activate(body, *straps)
    bpy.ops.object.join()
    body.data.uv_layers.active = body.data.uv_layers["UVMap"]
    body.data.uv_layers["UVMap"].active_render = True
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(margin=4 / args.texture_size, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    material, report["bake_seconds"] = bake_all(body, sources, args, maps_dir, height)
    report["maps"] = {f: sha256(os.path.join(maps_dir, f)) for f in sorted(os.listdir(maps_dir))}

    straps = []
    for name in ("IMP_foot_strap_R", "IMP_foot_strap_L"):
        activate(body)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="DESELECT")
        body.vertex_groups.active_index = body.vertex_groups[name].index
        bpy.ops.object.vertex_group_select()
        bpy.ops.mesh.separate(type="SELECTED")
        bpy.ops.object.mode_set(mode="OBJECT")
        part = next(o for o in bpy.context.selected_objects if o is not body)
        part.name = part.data.name = name
        for g in list(part.vertex_groups):
            part.vertex_groups.remove(g)
        sol = part.modifiers.new("thickness", "SOLIDIFY")
        sol.thickness, sol.offset, sol.use_rim = STRAP_THICKNESS * height, 1.0, True
        activate(part); bpy.ops.object.modifier_apply(modifier="thickness")
        bpy.ops.object.shade_smooth()
        straps.append(part)
    for g in list(body.vertex_groups):
        body.vertex_groups.remove(g)
    report["triangles"] = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in [body, *straps]}
    report["materials"] = sorted({m.name for o in [body, *straps] for m in o.data.materials})

    # Motion check: a walk or run bends the ankle well inside +-35 degrees.
    degrees = args.bend
    landmarks = {(-1 if n.endswith("_R") else 1): f["landmarks"] for n, f in report["straps"].items()}
    bends = bend_test(body, straps, landmarks, height, degrees)
    report["bend_test"] = [{k: v for k, v in b.items() if k != "posed"} for b in bends]

    # Renders.
    camera, _ = ref.setup_render(scene, cols["cameras_footwear"])
    shown = [high, before, body, *straps]
    hide = lambda *vis: [setattr(o, "hide_render", o not in vis) for o in bpy.data.objects if o.type == "MESH"]
    feet = ((-0.28 * height, -0.18 * height, 0), (0.28 * height, 0.19 * height, 0.24 * height))
    whole = ((-0.5 * height, -0.4 * height, 0), (0.5 * height, 0.4 * height, height))
    ref.set_mode(scene, True)
    rows = {}
    hide(before); rows["before (retopo v001)"] = [rr.still(scene, camera, feet, v, os.path.join(renders, f"before_{n}.png"), 640) for n, v in FEET_VIEWS.items()]
    hide(body, *straps); rows["after: strap and painted studs"] = [rr.still(scene, camera, feet, v, os.path.join(renders, f"after_{n}.png"), 640) for n, v in FEET_VIEWS.items()]
    clay = bpy.data.materials.new("fw_clay"); clay.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.42, 0.42, 1)
    tint = bpy.data.materials.new("fw_tint"); tint.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.22, 0.08, 1)
    for o, m in [(body, clay)] + [(s, tint) for s in straps]:
        o.data.materials[0] = m
    rows["after: geometry (strap tinted)"] = [rr.still(scene, camera, feet, v, os.path.join(renders, f"geometry_{n}.png"), 640) for n, v in FEET_VIEWS.items()]
    for o in [body, *straps]:
        o.data.materials[0] = material
    sheets.save(sheets.grid([sheets.load(p) for r in rows.values() for p in r], len(FEET_VIEWS),
                            [f"{r}: {v}" for r in rows for v in FEET_VIEWS], "Imp feet before and after the footwear"),
                os.path.join(out_sheets, "feet.png"))
    # Posed feet, side view, at the original's extremes.
    posed_cells = []
    for b in bends:
        for obj in [body, *straps]:
            obj.data.vertices.foreach_set("co", b["posed"][obj.name].ravel())
            obj.data.update()
        posed_cells.append(sheets.load(rr.still(scene, camera, feet, (90, 4), os.path.join(renders, f"bend_{b['degrees']:+.0f}.png"), 640)))
    sheets.save(sheets.grid(posed_cells, len(posed_cells), [f"toes {'up' if b['degrees'] > 0 else 'down' if b['degrees'] < 0 else 'at rest'} {abs(b['degrees']):.0f} deg: {b['inside_skin']} strap vertices inside skin"
                                                             for b in bends], "Temporary ankle bend (not a rig)"),
                os.path.join(out_sheets, "bend.png"))
    rest = next(b for b in bends if b["degrees"] == 0)
    for obj in [body, *straps]:
        obj.data.vertices.foreach_set("co", rest["posed"][obj.name].ravel()); obj.data.update()
    hide(body, *straps)
    full = [rr.still(scene, camera, whole, v, os.path.join(renders, f"body_{n}.png"), 700) for n, v in
            {"front": (0, 6), "front_3q_right": (40, 10), "rear": (180, 6)}.items()]
    p = rr.still(scene, camera, whole, (25, 55), os.path.join(renders, "game.png"), 24)
    full.append(sheets.fit(sheets.nearest(sheets.load(p), 29), 700, 700))
    sheets.save(sheets.grid([sheets.load(f) if isinstance(f, str) else f for f in full], 4,
                            ["front", "front_3q_right", "rear", "gameplay size (24 px), enlarged"], "Whole Imp with footwear"),
                os.path.join(out_sheets, "body.png"))

    # Outputs.
    activate(body, *straps)
    glb = os.path.join(args.out, "imp_footwear.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True, export_apply=True, export_yup=True)
    report["glb"] = {"file": glb, "sha256": sha256(glb), "bytes": os.path.getsize(glb)}
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(args.out, "footwear.blend"), compress=True)
    with open(os.path.join(args.out, "report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, default=str)
    print("-- done: " + json.dumps({k: report.get(k) for k in ("triangles", "bend_test")}, default=str))


if __name__ == "__main__":
    main()
