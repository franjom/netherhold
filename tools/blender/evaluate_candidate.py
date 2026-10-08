"""Inspect a generated creature mesh (an image-to-3D candidate) against the concepts and the original.

    blender --background --factory-startup --python-exit-code 1
        --python tools/blender/evaluate_candidate.py --
        --mesh <candidate.glb|.gltf|.fbx|.obj|.ply> --out <dir>
        [--manifest art/manifests/creature/imp.yaml] [--reference build/reference/imp/imp_reference.blend]
        [--yaw 0] [--size 900]

A candidate is a starting point, never an asset: this reports what it is, it approves nothing.
The mesh is imported as-is, then normalised under `CANDIDATE_root` (turned by --yaw so it faces
−Y, centred, standing on z = 0, scaled to the original Imp's height); the native size is
reported. Measured: topology (triangles/quads/n-gons, valence, edge lengths), loose parts (fused
accessories show up as one part), non-manifold and boundary edges, degenerate faces, faces no
ray from outside can reach (hidden surfaces), mirror symmetry across X, UV layers with coverage
and overlap, and which textures drive which material inputs. Rendered: six views in colour, clay
and unlit albedo (lighting painted into a texture shows there), head close-ups, loose parts in
distinct colours, and labelled comparisons with the manifest's concept regions and the original.

Writes `<out>/report.json`, `<out>/report.md`, `<out>/renders/`, `<out>/sheets/` and
`<out>/candidate.blend`; the candidate file and the reference scene are only read.
"""
import argparse
import json
import math
import os
import random
import re
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reference_scene as ref   # noqa: E402  (cameras, lights, render settings)
import review_renders as rr     # noqa: E402  (clay override, view tables)
import sheets                   # noqa: E402

IMPORTERS = {".glb": "gltf", ".gltf": "gltf", ".fbx": "fbx", ".obj": "obj", ".ply": "ply"}
HEAD_SHARE = 0.32                   # the top of the creature counted as head (original and concepts ≈ 30%)
# The concepts' camera angles, by eye (azimuth from the creature's front, elevation), for comparisons.
CONCEPT_ANGLES = {"face_closeup": (330, 0), "full_body": (35, 8), "front": (0, 4), "side": (290, 4), "rear": (180, 4),
                  "base_front": (0, 4), "base_rear": (180, 4)}


# ---------------------------------------------------------------- inputs

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="evaluate_candidate.py")
    parser.add_argument("--mesh", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--manifest", default="art/manifests/creature/imp.yaml")
    parser.add_argument("--reference", default="build/reference/imp/imp_reference.blend")
    parser.add_argument("--reference-object", default="Imp_Idle1")
    parser.add_argument("--yaw", type=float, default=0.0, help="degrees about Z that turn the candidate to face -Y")
    parser.add_argument("--size", type=int, default=900)
    parser.add_argument("--max-ray-faces", type=int, default=12000, help="faces sampled for the hidden-surface test")
    args = parser.parse_args(argv)
    for name in ("mesh", "out", "manifest", "reference"):     # Blender resolves relative paths its own way
        setattr(args, name, os.path.abspath(getattr(args, name)))
    return args


def manifest_regions(path):
    """The concept files and their named pixel regions, read from the manifest's own simple layout
    (`file:` lines and `name: [x0, y0, x1, y1]` lines under `regions:`); Blender has no YAML parser."""
    concepts, current, root = [], None, os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(path))))
    if not os.path.isfile(path):
        return concepts
    in_concepts = False
    for line in open(path, encoding="utf-8"):
        if re.match(r"^\s{2}concepts:", line):
            in_concepts = True
        elif re.match(r"^\s{2}original:", line):
            in_concepts = False
        if not in_concepts:
            continue
        if m := re.match(r"^\s+file:\s*(\S+)", line):
            current = {"file": os.path.join(root, m.group(1)), "regions": {}}
            concepts.append(current)
        elif current and (m := re.match(r"^\s+(\w+):\s*\[(\d+),\s*(\d+),\s*(\d+),\s*(\d+)\]", line)):
            current["regions"][m.group(1)] = tuple(int(v) for v in m.groups()[1:])
    return concepts


def concept_crop(concepts, region):
    for concept in concepts:
        if region in concept["regions"] and os.path.isfile(concept["file"]):
            x0, y0, x1, y1 = concept["regions"][region]
            return sheets.load(concept["file"])[y0:y1, x0:x1]
    return None


def import_candidate(path, collection):
    kind = IMPORTERS.get(os.path.splitext(path)[1].lower())
    if kind is None:
        sys.exit(f"unsupported candidate format: {path}")
    layer = bpy.context.view_layer.layer_collection.children[collection.name]
    bpy.context.view_layer.active_layer_collection = layer
    before = set(bpy.data.objects)
    {"gltf": lambda: bpy.ops.import_scene.gltf(filepath=path),
     "fbx": lambda: bpy.ops.import_scene.fbx(filepath=path),
     "obj": lambda: bpy.ops.wm.obj_import(filepath=path),
     "ply": lambda: bpy.ops.wm.ply_import(filepath=path)}[kind]()
    return [o for o in bpy.data.objects if o not in before]


# ---------------------------------------------------------------- geometry gathered once

def gather(meshes):
    """World-space vertices and polygons of every candidate mesh, as one indexed set."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    verts, polys, owners, offset = [], [], [], 0
    for index, obj in enumerate(meshes):
        mesh = obj.evaluated_get(depsgraph).to_mesh()
        matrix = np.array(obj.matrix_world)
        co = np.empty(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3) @ matrix[:3, :3].T + matrix[:3, 3]
        verts.append(co)
        polys += [[offset + v for v in p.vertices] for p in mesh.polygons]
        owners += [index] * len(mesh.polygons)
        offset += len(co)
        obj.evaluated_get(depsgraph).to_mesh_clear()
    return np.concatenate(verts) if verts else np.empty((0, 3)), polys, np.array(owners)


def weld(co, scale=1e-6):
    """One id per distinct position. glTF splits vertices at UV seams and normal breaks, so
    counting by vertex index makes every seam a boundary and every UV island a loose part."""
    span = max(float(np.ptp(co, axis=0).max()) if len(co) else 0.0, 1e-9)
    return np.unique(np.round(co / (span * scale)).astype(np.int64), axis=0, return_inverse=True)[1].ravel()


def welded_edges(mesh, ids):
    """Faces per edge after welding: (boundary, non-manifold) counts."""
    loop_vert = np.empty(len(mesh.loops), dtype=np.int64)
    mesh.loops.foreach_get("vertex_index", loop_vert)
    start = np.empty(len(mesh.polygons), dtype=np.int64)
    total = np.empty(len(mesh.polygons), dtype=np.int64)
    mesh.polygons.foreach_get("loop_start", start)
    mesh.polygons.foreach_get("loop_total", total)
    nxt = np.arange(len(loop_vert)) + 1
    ends = start + total
    nxt[ends - 1] = start
    a, b = ids[loop_vert], ids[loop_vert[nxt]]
    keep = a != b
    pairs = np.sort(np.stack([a[keep], b[keep]], axis=1), axis=1)
    counts = np.unique(pairs, axis=0, return_counts=True)[1]
    return int((counts == 1).sum()), int((counts > 2).sum())


def topology(meshes):
    stats = {"objects": [], "vertices": 0, "faces": 0, "triangles": 0, "quads": 0, "ngons": 0,
             "boundary_edges": 0, "nonmanifold_edges": 0, "seam_split_edges": 0, "degenerate_faces": 0}
    valences, lengths = [], []
    for obj in meshes:
        mesh = obj.data
        sizes = np.empty(len(mesh.polygons), dtype=np.int64)
        mesh.polygons.foreach_get("loop_total", sizes)
        edge_of_loop = np.empty(len(mesh.loops), dtype=np.int64)
        mesh.loops.foreach_get("edge_index", edge_of_loop)
        faces_per_edge = np.bincount(edge_of_loop, minlength=len(mesh.edges))
        edges = np.empty(len(mesh.edges) * 2, dtype=np.int64)
        mesh.edges.foreach_get("vertices", edges)
        co = np.empty(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        area = np.empty(len(mesh.polygons))
        mesh.polygons.foreach_get("area", area)
        valences.append(np.bincount(edges, minlength=len(mesh.vertices)))
        lengths.append(np.linalg.norm(co[edges[0::2]] - co[edges[1::2]], axis=1))
        boundary, nonmanifold = welded_edges(mesh, weld(co))
        entry = {"name": obj.name, "vertices": len(mesh.vertices), "faces": len(mesh.polygons),
                 "triangles": int((sizes == 3).sum()), "quads": int((sizes == 4).sum()), "ngons": int((sizes > 4).sum()),
                 "boundary_edges": boundary, "nonmanifold_edges": nonmanifold,
                 "seam_split_edges": int((faces_per_edge == 1).sum()) - boundary,
                 "degenerate_faces": int((area < 1e-12 * max(area.max(initial=0), 1e-12) + 1e-14).sum()),
                 "materials": [m.name if m else None for m in mesh.materials], "uv_layers": [u.name for u in mesh.uv_layers],
                 "color_attributes": [a.name for a in mesh.color_attributes]}
        stats["objects"].append(entry)
        for key in ("vertices", "faces", "triangles", "quads", "ngons", "boundary_edges", "nonmanifold_edges",
                    "seam_split_edges", "degenerate_faces"):
            stats[key] += entry[key]
    if valences:
        v = np.concatenate(valences)
        lengths = np.concatenate(lengths)
        stats["valence_over_6"] = round(float((v > 6).mean()), 4)
        stats["valence_under_3"] = round(float(((v > 0) & (v < 3)).mean()), 4)
        stats["edge_length_cv"] = round(float(lengths.std() / max(lengths.mean(), 1e-12)), 3)
    return stats


def loose_parts(verts, polys):
    """Connected components by shared positions (union-find over welded vertices), largest first."""
    welded = weld(verts)
    parent = list(range(int(welded.max()) + 1 if len(welded) else 0))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for poly in polys:
        root = find(int(welded[poly[0]]))
        for v in poly[1:]:
            other = find(int(welded[v]))
            if other != root:
                parent[other] = root
    labels = np.array([find(int(w)) for w in welded])
    ids, inverse = np.unique(labels, return_inverse=True)
    face_part = np.array([inverse[p[0]] for p in polys]) if polys else np.empty(0, dtype=int)
    parts = []
    for k in range(len(ids)):
        members = verts[inverse == k]
        parts.append({"faces": int((face_part == k).sum()), "min": members.min(0).round(4).tolist(),
                      "max": members.max(0).round(4).tolist()})
    order = np.argsort([-p["faces"] for p in parts])
    return [parts[i] for i in order], inverse, order


def hidden_faces(verts, polys, limit, seed=1):
    """Faces from which no ray reaches open space: interior shells, buried accessories, double walls."""
    bvh = BVHTree.FromPolygons([tuple(v) for v in verts], polys)
    rng = random.Random(seed)
    sample = list(range(len(polys))) if len(polys) <= limit else rng.sample(range(len(polys)), limit)
    golden = math.pi * (3 - math.sqrt(5))
    directions = [Vector((math.cos(golden * i) * math.sqrt(1 - (1 - 2 * (i + 0.5) / 48) ** 2),
                          math.sin(golden * i) * math.sqrt(1 - (1 - 2 * (i + 0.5) / 48) ** 2),
                          1 - 2 * (i + 0.5) / 48)) for i in range(48)]
    scale = float(np.linalg.norm(verts.max(0) - verts.min(0)))
    hidden = 0
    for index in sample:
        corners = [Vector(verts[v]) for v in polys[index]]
        centre = sum(corners, Vector()) / len(corners)
        normal = (corners[1] - corners[0]).cross(corners[2] - corners[0])
        if normal.length < 1e-12:
            continue
        normal.normalize()
        seen = False
        for side in (normal, -normal):                 # either side counts: a candidate's winding is not trusted
            for d in [side] + [d for d in directions if d.dot(side) > 0.15]:
                if bvh.ray_cast(centre + d * scale * 1e-4, d)[0] is None:
                    seen = True
                    break
            if seen:
                break
        hidden += not seen
    return {"sampled": len(sample), "hidden": hidden, "hidden_share": round(hidden / max(len(sample), 1), 4)}


def symmetry(verts, height, head_z):
    """Distance from each mirrored (−x) vertex to the nearest vertex, as a share of height."""
    tree = KDTree(len(verts))
    for i, v in enumerate(verts):
        tree.insert(v, i)
    tree.balance()
    rng = np.random.default_rng(2)
    pick = rng.choice(len(verts), size=min(len(verts), 20000), replace=False)
    d = np.array([tree.find((-verts[i][0], verts[i][1], verts[i][2]))[2] for i in pick]) / height
    head = verts[pick][:, 2] > head_z
    summary = lambda x: {"median": round(float(np.median(x)), 4), "p90": round(float(np.percentile(x, 90)), 4)} if len(x) else None
    return {"body": summary(d), "head": summary(d[head])}


def uv_report(meshes, grid=512):
    """Per mesh and UV layer: UVs outside 0..1, zero-area UV faces, coverage of the unit square, and
    overlap (summed UV area over covered area; 1.0 means no overlap, mirrored or stacked UVs raise it)."""
    result = []
    for obj in meshes:
        mesh = obj.data
        if not mesh.uv_layers:
            result.append({"object": obj.name, "uv": None})
            continue
        mesh.calc_loop_triangles()
        tris = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int64)
        mesh.loop_triangles.foreach_get("loops", tris)
        tris = tris.reshape(-1, 3)
        for layer in mesh.uv_layers:
            uv = np.empty(len(layer.uv) * 2)
            layer.uv.foreach_get("vector", uv)
            uv = uv.reshape(-1, 2)
            t = uv[tris]
            area = 0.5 * np.abs((t[:, 1, 0] - t[:, 0, 0]) * (t[:, 2, 1] - t[:, 0, 1]) - (t[:, 2, 0] - t[:, 0, 0]) * (t[:, 1, 1] - t[:, 0, 1]))
            covered = np.zeros((grid, grid), dtype=bool)
            for tri in (t % 1.0) * grid:                       # wrap tiles into 0..1 for coverage
                x0, y0 = np.floor(tri.min(0)).astype(int)
                x1, y1 = np.ceil(tri.max(0)).astype(int)
                if x1 - x0 > grid // 2 or y1 - y0 > grid // 2:
                    continue
                xs, ys = np.meshgrid(np.arange(max(x0, 0), min(x1, grid)) + 0.5, np.arange(max(y0, 0), min(y1, grid)) + 0.5)
                if xs.size == 0:
                    continue
                (ax, ay), (bx, by), (cx, cy) = tri
                d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
                if abs(d) < 1e-12:
                    continue
                l1 = ((by - cy) * (xs - cx) + (cx - bx) * (ys - cy)) / d
                l2 = ((cy - ay) * (xs - cx) + (ax - cx) * (ys - cy)) / d
                inside = (l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)
                covered[ys[inside].astype(int), xs[inside].astype(int)] = True
            coverage = covered.mean()
            result.append({"object": obj.name, "uv": layer.name,
                           "outside_unit_square": round(float(((uv < 0) | (uv > 1)).any(axis=1).mean()), 4),
                           "zero_area_faces": int((area < 1e-10).sum()), "coverage": round(float(coverage), 4),
                           "overlap": round(float(area.sum() / max(coverage, 1e-9)), 3)})
    return result


def textures(meshes):
    found = {}
    for obj in meshes:
        for material in obj.data.materials:
            if material is None or material.node_tree is None or material.name in found:
                continue
            uses = []
            for node in material.node_tree.nodes:
                if node.type != "TEX_IMAGE" or node.image is None:
                    continue
                targets = []
                frontier = [link.to_node for out in node.outputs for link in out.links]
                sockets = [link.to_socket.name for out in node.outputs for link in out.links]
                for nxt in frontier:                     # follow one hop through normal-map or separate nodes
                    sockets += [f"{nxt.bl_label}->{link.to_socket.name}" for out in nxt.outputs for link in out.links]
                targets = sorted(set(sockets))
                uses.append({"image": node.image.name, "size": list(node.image.size),
                             "colorspace": node.image.colorspace_settings.name, "feeds": targets})
            found[material.name] = uses
    return found


# ---------------------------------------------------------------- renders

def parts_colours(meshes, inverse, order):
    """Colour every loose part distinctly (largest part grey), as a face-corner attribute."""
    rank = np.empty(len(order), dtype=int)
    rank[order] = np.arange(len(order))
    rng = np.random.default_rng(5)
    palette = np.vstack([[0.55, 0.55, 0.55], rng.uniform(0.15, 1.0, size=(max(len(order), 1), 3))])
    offset = 0
    for obj in meshes:
        mesh = obj.data
        corners = np.empty(len(mesh.loops), dtype=np.int64)
        mesh.loops.foreach_get("vertex_index", corners)
        colour = palette[np.minimum(rank[inverse[offset + corners]], len(palette) - 1)]
        attribute = mesh.color_attributes.new("eval_parts", "FLOAT_COLOR", "CORNER")
        attribute.data.foreach_set("color", np.hstack([colour, np.ones((len(colour), 1))]).ravel())
        mesh.color_attributes.active_color = attribute
        offset += len(mesh.vertices)


def render_set(scene, camera, box, views, folder, size, modes):
    paths = {}
    for mode in modes:
        if mode == "colour":
            ref.set_mode(scene, True); rr.clay(scene, False)
        elif mode == "clay":
            ref.set_mode(scene, True); rr.clay(scene, True)
        else:
            rr.clay(scene, False); ref.set_mode(scene, False)
            scene.display.shading.color_type = "TEXTURE" if mode == "albedo" else "VERTEX"
        paths[mode] = [rr.still(scene, camera, box, view, os.path.join(folder, f"{name}_{mode}.png"), size)
                       for name, view in views.items()]
    rr.clay(scene, False)
    ref.set_mode(scene, True)
    return paths


def side_by_side(cells, labels, title, path, height=760):
    widths = [round(height * c.shape[1] / c.shape[0]) for c in cells]
    width = max(widths)
    sheets.save(sheets.grid([sheets.fit(c, width, height) for c in cells], len(cells), labels, title), path)
    return path


# ---------------------------------------------------------------- main

def main():
    args = parse_args()
    import imp_blockout                                             # noqa: E402  (append_reference)
    os.makedirs(args.out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    for name in ("modern", "original", "cameras"):             # review_renders' collection names
        scene.collection.children.link(bpy.data.collections.new(name))
    imported = import_candidate(args.mesh, bpy.data.collections["modern"])
    meshes = [o for o in imported if o.type == "MESH"]
    if not meshes:
        sys.exit(f"{args.mesh}: no mesh objects imported")
    original, o_low, o_high = imp_blockout.append_reference(args.reference, args.reference_object,
                                                           bpy.data.collections["original"])
    original.location = (0, 0, 0)
    height = float(o_high[2] - o_low[2])

    # Normalise: turn, centre, stand on the ground, scale to the original's height.
    root = bpy.data.objects.new("CANDIDATE_root", None)
    bpy.data.collections["modern"].objects.link(root)
    for obj in imported:
        if obj.parent is None:
            obj.parent = root
    root.rotation_euler = (0, 0, math.radians(args.yaw))
    bpy.context.view_layer.update()
    native, _, _ = gather(meshes)
    low, high = native.min(0), native.max(0)
    native_height = float(high[2] - low[2])
    scale = height / max(native_height, 1e-12)
    centre = (low + high) / 2
    root.scale = (scale,) * 3
    root.location = (-centre[0] * scale, -centre[1] * scale, -low[2] * scale)
    bpy.context.view_layer.update()
    verts, polys, _ = gather(meshes)
    c_low, c_high = verts.min(0), verts.max(0)
    head_z = height * (1 - HEAD_SHARE)
    print(f"-- candidate {os.path.basename(args.mesh)}: {len(verts)} vertices, {len(polys)} faces; native height {native_height:.4f}, scaled x{scale:.4f}")

    report = {"candidate": args.mesh, "blender": bpy.app.version_string,
              "scale": {"native_size": (high - low).round(5).tolist(), "native_height": native_height,
                        "scale_to_original": scale, "original_height": height, "yaw": args.yaw,
                        "normalised_size": (c_high - c_low).round(4).tolist(),
                        "original_size": (o_high - o_low).round(4).tolist()},
              "topology": topology(meshes)}
    parts, inverse, order = loose_parts(verts, polys)
    report["loose_parts"] = {"count": len(parts), "largest": parts[:12],
                             "largest_share": round(parts[0]["faces"] / max(len(polys), 1), 4),
                             "fragments_under_0.1pct": sum(p["faces"] < 0.001 * len(polys) for p in parts)}
    report["hidden_surfaces"] = hidden_faces(verts, polys, args.max_ray_faces)
    report["symmetry"] = symmetry(verts, height, head_z)
    report["uv"] = uv_report(meshes)
    report["textures"] = textures(meshes)

    # Findings: statements for the review, never a pass.
    notes = []
    t = report["topology"]
    if report["loose_parts"]["count"] == 1:
        notes.append("one connected surface: any sack, pick, clothing or eyes are fused into the body and must be cut out")
    if report["loose_parts"]["fragments_under_0.1pct"]:
        notes.append(f"{report['loose_parts']['fragments_under_0.1pct']} tiny loose fragments")
    if t["triangles"] > 0.9 * t["faces"]:
        notes.append("triangulated: retopology is needed before rigging")
    if t["nonmanifold_edges"] or t["boundary_edges"]:
        notes.append(f"{t['nonmanifold_edges']} non-manifold and {t['boundary_edges']} boundary edges")
    if report["hidden_surfaces"]["hidden_share"] > 0.02:
        notes.append(f"{100 * report['hidden_surfaces']['hidden_share']:.1f}% of sampled faces are hidden (interior or buried geometry)")
    if report["symmetry"]["head"] and report["symmetry"]["head"]["median"] > 0.01:
        notes.append(f"head asymmetric: median mirror deviation {100 * report['symmetry']['head']['median']:.1f}% of height")
    if not any(u.get("uv") for u in report["uv"]):
        notes.append("no UVs")
    elif any((u.get("overlap") or 0) > 1.15 for u in report["uv"]):
        notes.append("overlapping or stacked UVs")
    if not report["textures"] or not any(report["textures"].values()):
        notes.append("no textures")
    elif not any("Normal" in " ".join(u["feeds"]) for uses in report["textures"].values() for u in uses):
        notes.append("base colour only: expect lighting painted into it (check the albedo renders)")
    report["notes"] = notes

    # Renders.
    camera, _ = ref.setup_render(scene, bpy.data.collections["cameras"])
    rr.collection_visibility(scene, modern=True, original=False)
    renders, out_sheets = os.path.join(args.out, "renders"), os.path.join(args.out, "sheets")
    body_box = (np.minimum(c_low, o_low) - 0.05 * height, np.maximum(c_high, o_high) + 0.05 * height)
    head_pts = verts[verts[:, 2] > head_z]
    head_box = (head_pts.min(0) - 0.04 * height, head_pts.max(0) + 0.04 * height)
    parts_colours(meshes, inverse, order)
    body = render_set(scene, camera, body_box, rr.BODY_VIEWS, os.path.join(renders, "body"), args.size,
                      ("colour", "clay", "albedo", "parts"))
    head_views = {"front": (0, 2), "front_3q_right": (45, 6), "profile_right": (90, 2), "concept_face_angle": CONCEPT_ANGLES["face_closeup"]}
    head = render_set(scene, camera, head_box, head_views, os.path.join(renders, "head"), args.size, ("colour", "clay"))
    for mode, paths in body.items():
        sheets.save(sheets.grid([sheets.load(p) for p in paths], 3, list(rr.BODY_VIEWS), f"Candidate - body, {mode}"),
                    os.path.join(out_sheets, f"body_{mode}.png"))
    for mode, paths in head.items():
        sheets.save(sheets.grid([sheets.load(p) for p in paths], 4, list(head_views), f"Candidate - head, {mode}"),
                    os.path.join(out_sheets, f"head_{mode}.png"))

    concepts = manifest_regions(args.manifest)
    comparisons = {}
    angle_render = lambda box, angle, name, mode: render_set(scene, camera, box, {name: angle},
                                                             os.path.join(renders, "compare"), 1000, (mode,))[mode][0]
    for region, box, label in (("face_closeup", head_box, "face"), ("full_body", body_box, "body"),
                               ("front", body_box, "front"), ("side", body_box, "side"), ("rear", body_box, "rear"),
                               ("base_front", body_box, "base_front"), ("base_rear", body_box, "base_rear")):
        crop = concept_crop(concepts, region)
        if crop is None:
            continue
        angle = CONCEPT_ANGLES[region]
        cells = [crop, sheets.load(angle_render(box, angle, f"{region}", "colour")),
                 sheets.load(angle_render(box, angle, f"{region}", "clay"))]
        labels = [f"concept: {region}", "candidate colour", "candidate clay"]
        if label != "face":
            rr.collection_visibility(scene, modern=False, original=True)
            cells.append(sheets.load(angle_render(body_box, angle, f"{region}_original", "colour")))
            labels.append("original DK2 mesh, same camera")
            rr.collection_visibility(scene, modern=True, original=False)
        comparisons[region] = side_by_side(cells, labels, f"Candidate against the concept: {region} "
                                           f"(view {angle[0]} deg; the concept's angle is approximate)",
                                           os.path.join(out_sheets, f"compare_{region}.png"))
    report["sheets"] = sorted(os.path.join(out_sheets, f) for f in os.listdir(out_sheets))

    with open(os.path.join(args.out, "report.json"), "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
    write_markdown(report, os.path.join(args.out, "report.md"))
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(args.out, "candidate.blend"), compress=True)
    print("-- notes: " + ("; ".join(notes) if notes else "none"))


def write_markdown(report, path):
    s, t, lp = report["scale"], report["topology"], report["loose_parts"]
    lines = ["# Candidate inspection", "", f"`{report['candidate']}` (Blender {report['blender']}). "
             "A candidate is a starting point; nothing here approves it.", "",
             "## Notes", ""] + [f"- {n}" for n in report["notes"] or ["none"]] + [
             "", "## Scale", "",
             f"- native size {s['native_size']} (height {s['native_height']:.4f}), scaled x{s['scale_to_original']:.4f} "
             f"to the original's height {s['original_height']:.4f}; yaw {s['yaw']} deg",
             f"- normalised size {s['normalised_size']} against the original's {s['original_size']}",
             "", "## Topology", "",
             f"- {t['vertices']} vertices, {t['faces']} faces: {t['triangles']} triangles, {t['quads']} quads, {t['ngons']} n-gons",
             f"- {t['boundary_edges']} boundary and {t['nonmanifold_edges']} non-manifold edges after welding coincident vertices "
             f"({t['seam_split_edges']} more edges only split at UV seams), {t['degenerate_faces']} degenerate faces",
             f"- valence over 6: {t.get('valence_over_6')}, under 3: {t.get('valence_under_3')}; edge length CV {t.get('edge_length_cv')}",
             f"- loose parts: {lp['count']} (largest holds {100 * lp['largest_share']:.1f}% of faces; "
             f"{lp['fragments_under_0.1pct']} fragments under 0.1%)",
             f"- hidden faces: {report['hidden_surfaces']['hidden']} of {report['hidden_surfaces']['sampled']} sampled",
             f"- mirror deviation (share of height): body {report['symmetry']['body']}, head {report['symmetry']['head']}",
             "", "## UVs and textures", ""] + [f"- {u}" for u in report["uv"]] + [
             f"- {m}: {uses}" for m, uses in report["textures"].items()] + [
             "", "## Sheets", ""] + [f"- {p}" for p in report["sheets"]]
    with open(path, "w", encoding="utf-8") as stream:
        stream.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
