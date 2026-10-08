"""Build a modern Imp blockout: new geometry, simple materials, one adjustable skin colour.

    blender --background --factory-startup --python-exit-code 1
        --python tools/blender/imp_blockout.py --
        --reference build/reference/imp/imp_reference.blend
        --portrait build/modern/creature/imp/reference/portrait.png
        --out build/modern/creature/imp/blockout [--version v009] [--note "..."]

Writes `<out>/<version>/imp_blockout.blend`, `params.json` and a copy of this script; the version
defaults to the next free `vNNN`, so earlier blockouts are never overwritten. Renders are
`review_renders.py`.

The Imp is modelled at height 1 under `IMP_root`, which scales it to the original's height
(Imp_Idle1, frame 0, from the reference scene), front −Y, Z up, like the reference. Proportions,
pick and sack come from the original mesh; the face, colour and costume from the owner's portrait
(ADR-0398). The skin is one voxel-remeshed surface fused from anatomical masses (cranium, brow,
cheekbones, muzzle, nose, lips, jaw, chin), lofted ears with an inner fold, a capsule per limb
segment and torso masses. A mouth cavity is cut into it, then the skin around each eyeball is
wrapped onto the ball as continuous lids (`wrap_eyelids`). Eyes, teeth, mouth interior, clothing
and equipment are separate objects. Skin colour is the object property `skin_color`, read by the
shared skin material, so a variant is a property change (`SKIN_VARIANTS`).

This is a blockout for review: no UVs, no rig, no clean topology.
"""
import argparse
import json
import math
import os
import random
import re
import shutil
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

SKIN_VARIANTS = {                     # display-referred sRGB; converted to linear for the material
    "olive": (0.47, 0.50, 0.22),      # the portrait's mid skin, highlights and shadow excluded
    "gray": (0.50, 0.50, 0.48),
    "earthy_brown": (0.50, 0.36, 0.23),
    "muted_red": (0.55, 0.29, 0.24),
}

# Landmarks at height 1, front −Y. Head-relative offsets are from HEAD. The original (Imp_Idle1,
# frame 0) fixes height, head share, foot spread, pick and sack; the portrait fixes the face.
HEAD = (0.0, -0.10, 0.795)             # lowered and pushed forward from v007's (0, -0.06, 0.835)
P = {
    "eye_offset": (0.106, -0.132, 0.036),  # huge, nearly touching, 30% down the head, bulging (portrait)
    "eye_radius": 0.100,                    # the near eye spans ~45% of the face's width in the portrait
    "gaze_out": 0.22,                       # each eye looks a little outward
    "aperture": {"half_width": 0.72, "top": 0.42, "bottom": 0.62},   # radians on the ball: lid arcs, upper slightly low
    "lid_gap": 0.004,
    "lid_bump": {"upper": 0.007, "lower": 0.005, "width": 0.05},
    "lid_band": 0.25,                       # radians beyond the opening that the skin hugs the ball
    "ear_base": (0.172, 0.005, 0.015),
    "ear_dir": (0.80, 0.42, 0.45),
    "ear_length": 0.165,
    "ear_width": 0.095,
    "grip": (-0.205, -0.045, 0.305),       # right hand on the handle near the head (original)
    "pick_head": (-0.205, -0.13, 0.30),    # blade x≈−0.2, z 0.03..0.55, y −0.18..0 (original)
    "pick_handle_end": (-0.205, 0.34, 0.33),
    "sack_centre": (0.0, 0.30, 0.56),      # original sack: y 0.05..0.57, z 0.36..0.95
    "sack_radii": (0.18, 0.21, 0.28),
}

# The face as overlapping masses: name, offset from HEAD, radii, rotation about (x, y, z) in degrees.
FACE_MASSES = [
    ("cranium", (0, 0.010, 0.030), (0.195, 0.165, 0.130), (0, 0, 0)),          # low dome: the forehead is short
    ("forehead", (0, -0.070, 0.090), (0.170, 0.110, 0.075), (0, 0, 0)),       # broad, low, sloping back
    ("socket_L", (0.106, -0.118, 0.036), (0.107, 0.107, 0.107), (0, 0, 0)),    # flesh just covering each ball;
    ("socket_R", (-0.106, -0.118, 0.036), (0.107, 0.107, 0.107), (0, 0, 0)),   # the lids are cut from it
    ("brow_L", (0.106, -0.142, 0.110), (0.098, 0.050, 0.038), (0, 7, 0)),      # heavy, over the ball
    ("brow_R", (-0.106, -0.142, 0.110), (0.098, 0.050, 0.038), (0, -7, 0)),    # outer ends droop (weary)
    ("glabella", (0, -0.160, 0.075), (0.032, 0.032, 0.028), (0, 0, 0)),
    ("cheek_L", (0.115, -0.120, -0.040), (0.060, 0.058, 0.048), (0, 0, 0)),
    ("cheek_R", (-0.115, -0.120, -0.040), (0.060, 0.058, 0.048), (0, 0, 0)),
    ("muzzle", (0, -0.145, -0.075), (0.110, 0.088, 0.070), (0, 0, 0)),
    ("nose", (0, -0.205, -0.030), (0.044, 0.028, 0.026), (0, 0, 0)),           # small, flat, broad: mostly nostrils
    ("nose_wing_L", (0.036, -0.198, -0.040), (0.022, 0.022, 0.019), (0, 0, 0)),
    ("nose_wing_R", (-0.036, -0.198, -0.040), (0.022, 0.022, 0.019), (0, 0, 0)),
    ("upper_lip", (0, -0.212, -0.074), (0.094, 0.042, 0.022), (0, 0, 0)),      # drawn back off the teeth
    ("lower_lip", (0, -0.196, -0.146), (0.082, 0.042, 0.026), (0, 0, 0)),
    ("jaw", (0, -0.085, -0.110), (0.135, 0.110, 0.068), (0, 0, 0)),
    ("chin", (0, -0.168, -0.170), (0.048, 0.040, 0.034), (0, 0, 0)),
    ("nape", (0, 0.010, -0.100), (0.095, 0.085, 0.065), (0, 0, 0)),             # no stem under the skull
]
MOUTH_CAVITY = ((0, -0.185, -0.110), (0.074, 0.072, 0.034))  # between the lips: a wide grimace


# ---------------------------------------------------------------- small helpers

def V(x, y=None, z=None):
    return Vector(x) if y is None else Vector((x, y, z))


def H(offset):
    return V(HEAD) + V(offset)


def srgb_to_linear(c):
    return tuple(v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in c)


def mesh_object(name, vertices, faces, collection):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in vertices], [], [tuple(f) for f in faces])
    mesh.validate()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    return obj


def apply_modifiers(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    old = obj.data
    obj.modifiers.clear()
    obj.data = mesh
    if old.users == 0:
        bpy.data.meshes.remove(old)


def shade(obj, smooth=True):
    obj.data.polygons.foreach_set("use_smooth", [smooth] * len(obj.data.polygons))


def coordinates(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def set_coordinates(obj, co):
    obj.data.vertices.foreach_set("co", np.ascontiguousarray(co, dtype=np.float64).ravel())
    obj.data.update()


def normals(obj):
    n = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("normal", n)
    return n.reshape(-1, 3)


def icosphere(name, subdivisions, collection):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1.0)
    obj = bpy.context.active_object
    obj.name = name
    for c in obj.users_collection:
        c.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def ellipsoid(name, centre, radii, collection, subdivisions=4, rotation=None):
    obj = icosphere(name, subdivisions, collection)
    co = coordinates(obj) * np.array(radii)
    if rotation is not None:
        co = co @ np.array(rotation).T
    set_coordinates(obj, co + np.array(centre))
    return obj


def euler(degrees):
    return np.array(Matrix.Rotation(math.radians(degrees[2]), 3, "Z") @ Matrix.Rotation(math.radians(degrees[1]), 3, "Y")
                    @ Matrix.Rotation(math.radians(degrees[0]), 3, "X"))


def loft(name, sections, collection, cap=True):
    """Join closed rings of equal vertex count into a tube; optional fan caps."""
    count = len(sections[0])
    vertices = [v for section in sections for v in section]
    faces = []
    for s in range(len(sections) - 1):
        for i in range(count):
            a, b = s * count + i, s * count + (i + 1) % count
            faces.append((a, b, b + count, a + count))
    if cap:
        for index, section in ((0, sections[0]), (len(sections) - 1, sections[-1])):
            vertices.append(sum((Vector(v) for v in section), Vector()) / count)
            c = len(vertices) - 1
            base = index * count
            for i in range(count):
                a, b = base + i, base + (i + 1) % count
                faces.append((a, b, c) if index else (b, a, c))
    return mesh_object(name, vertices, faces, collection)


def frame_axes(direction, up_hint=(0, 0, 1)):
    d = Vector(direction).normalized()
    side = d.cross(Vector(up_hint))
    if side.length < 1e-6:
        side = d.cross(Vector((0, 1, 0)))
    side.normalize()
    return d, side, side.cross(d).normalized()


def ring(centre, axis_a, axis_b, radius_a, radius_b, count=16):
    return [centre + axis_a * (radius_a * math.cos(2 * math.pi * i / count))
            + axis_b * (radius_b * math.sin(2 * math.pi * i / count)) for i in range(count)]


def join(parts, name):
    with bpy.context.temp_override(active_object=parts[0], selected_editable_objects=parts, object=parts[0]):
        bpy.ops.object.join()
    parts[0].name = name
    return parts[0]


def capsule(name, a, radius_a, b, radius_b, collection, ring_count=16, cap_steps=5):
    """A closed tube from sphere (a, radius_a) to sphere (b, radius_b)."""
    axis, side, up = frame_axes(b - a, (0, 0, 1) if abs((b - a).normalized().z) < 0.95 else (0, 1, 0))
    sections = []
    for k in range(cap_steps, -1, -1):
        phi = math.pi / 2 * k / cap_steps
        r = max(radius_a * math.cos(phi), 1e-4)
        sections.append(ring(a - axis * (radius_a * math.sin(phi)), side, up, r, r, ring_count))
    for k in range(cap_steps + 1):
        phi = math.pi / 2 * k / cap_steps
        r = max(radius_b * math.cos(phi), 1e-4)
        sections.append(ring(b + axis * (radius_b * math.sin(phi)), side, up, r, r, ring_count))
    return loft(name, sections, collection)


def smoothstep(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def segment_distance(points, a, b):
    a, b = np.array(a), np.array(b)
    ab = b - a
    t = np.clip(((points - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(points - (a + t[:, None] * ab), axis=1)


# ---------------------------------------------------------------- the face

def mouth_droop(points):
    """Pull the mouth's corners down: the tense, weary line, not a smile."""
    rel = points - np.array(HEAD)
    near = (rel[:, 1] < -0.13) & (rel[:, 2] > -0.19) & (rel[:, 2] < -0.06)
    corner = smoothstep((np.abs(rel[:, 0]) - 0.035) / 0.07)
    out = points.copy()
    out[:, 2] -= 0.016 * corner * near
    return out


def face_masses(collection):
    parts = []
    for name, offset, radii, rotation in FACE_MASSES:
        parts.append(ellipsoid(name, H(offset), radii, collection, 4, rotation=euler(rotation)))
    for part in parts:
        set_coordinates(part, mouth_droop(coordinates(part)))
    return parts


def ear(side, collection):
    """Pointed, thick at the base, with a raised rim around a concave inner fold."""
    base = H((side * P["ear_base"][0], P["ear_base"][1], P["ear_base"][2]))
    direction = V(P["ear_dir"]); direction.x *= side
    d, across, up = frame_axes(direction)
    forward = across if side > 0 else -across
    sections = []
    for k in range(17):
        t = k / 16
        width = 0.5 * P["ear_width"] * (1 - t) ** 0.85 * (1 + 0.35 * math.sin(math.pi * t))
        thick = 0.008 + 0.020 * (1 - t) ** 1.5
        cup = 0.75 * thick * math.sin(math.pi * min(t * 1.25, 1))
        centre = base + d * (P["ear_length"] * t) + up * (0.035 * math.sin(math.pi * t * 0.8))
        section = []
        for i in range(20):
            a = 2 * math.pi * i / 20
            w, f = math.cos(a), math.sin(a)
            fold = -cup * (1 - w * w) if f > 0 else 0.0          # front face dished, rim left standing
            section.append(centre + up * (max(width, 0.002) * w) + forward * (thick * f + fold))
        sections.append(section)
    return loft(f"ear_{'L' if side > 0 else 'R'}", sections, collection)


def eye_frames():
    result = []
    for side in (-1, 1):
        ox, oy, oz = P["eye_offset"]
        centre = H((side * ox, oy, oz))
        forward = V((side * P["gaze_out"], -1, 0.04)).normalized()
        right = forward.cross(V((0, 0, 1))).normalized()        # the eye's own horizontal axis
        up = right.cross(forward).normalized()
        result.append((side, centre, forward, right, up))
    return result


def wrap_eyelids(skin):
    """Seat each eyeball in the skin. On the ball, the aperture (an ellipse cut flat by a low,
    weary upper lid) is opened by tucking skin behind the ball; around it the skin is pulled onto
    the ball, thickest at the lid margins, and nowhere may skin stay inside the ball, which is
    what exposed v007's red caps above the brows."""
    co = coordinates(skin)
    R, gap = P["eye_radius"], P["lid_gap"]
    ap, bump = P["aperture"], P["lid_bump"]
    report, masks = {}, []
    for side, centre, forward, right, up in eye_frames():
        q = co - np.array(centre)
        dist = np.linalg.norm(q, axis=1)
        d = q / np.maximum(dist, 1e-9)[:, None]
        az = np.arctan2(d @ np.array(right) * side, d @ np.array(forward))
        el = np.arcsin(np.clip(d @ np.array(up), -1, 1))
        across = np.sqrt(np.clip(1 - (az / ap["half_width"]) ** 2, 0, 1))
        upper_lid, lower_lid = ap["top"] * across, -ap["bottom"] * across   # arcs meeting at the corners
        s = np.maximum.reduce([el - upper_lid, lower_lid - el, np.abs(az) - ap["half_width"]])   # > 0 outside
        front = (d @ np.array(forward)) > -0.2
        local = dist < 2.0 * R
        inside = front & local & (s <= 0)
        margin = np.where(el > 0, bump["upper"], bump["lower"]) * np.exp(-(np.maximum(s, 0) / bump["width"]) ** 2)
        target = R + gap + margin
        weight = np.exp(-(np.maximum(s, 0) / P["lid_band"]) ** 2) * (1 - smoothstep((dist - 1.15 * R) / (0.85 * R)))
        weight = np.where(front & local, weight, 0)
        radius = dist + (target - dist) * weight
        radius = np.where(inside, 0.80 * R, radius)                         # opened: behind the ball
        radius = np.where(~inside & (radius < R + gap), R + gap, radius)    # never inside the ball
        moved = np.abs(radius - dist) > 1e-7
        co = np.where(moved[:, None], np.array(centre) + d * radius[:, None], co)
        masks.append((moved, inside))
        report[side] = {"opened": int(inside.sum()), "pushed_out": int((~inside & (dist < R + gap)).sum())}
    report["masks"] = masks
    set_coordinates(skin, co)
    relax_lids(skin, report.pop("masks"))
    return report


def relax_lids(skin, masks, iterations=6, factor=0.5):
    """Even out the faces the wrap stretched (v008 smeared the bridge between the eyes), then put
    back the rule that no skin lies inside a ball."""
    edges = np.empty(len(skin.data.edges) * 2, dtype=np.int64)
    skin.data.edges.foreach_get("vertices", edges)
    a, b = edges[0::2], edges[1::2]
    count = np.bincount(np.concatenate([a, b]), minlength=len(skin.data.vertices)).astype(float)
    movable = np.zeros(len(skin.data.vertices), dtype=bool)
    opened = np.zeros_like(movable)
    for moved, inside in masks:
        movable |= moved & ~inside
        opened |= inside
    movable &= ~opened
    co = coordinates(skin)
    R, gap = P["eye_radius"], P["lid_gap"]
    for _ in range(iterations):
        total = np.zeros_like(co)
        np.add.at(total, a, co[b])
        np.add.at(total, b, co[a])
        mean = total / np.maximum(count, 1)[:, None]
        co = np.where(movable[:, None], co + (mean - co) * factor, co)
        for _, centre, _, _, _ in eye_frames():
            q = co - np.array(centre)
            dist = np.linalg.norm(q, axis=1)
            push = movable & (dist < R + gap)
            co[push] = np.array(centre) + q[push] / dist[push, None] * (R + gap)
    set_coordinates(skin, co)


def face_folds(skin):
    """Nostrils and broad folds only: nasolabial grooves and a short frown between the brows."""
    co, n = coordinates(skin), normals(skin)
    depth = np.zeros(len(co))
    for side in (-1, 1):
        nostril = np.array(H((side * 0.020, -0.226, -0.048)))
        depth += 0.011 * np.exp(-(np.linalg.norm(co - nostril, axis=1) / 0.009) ** 2)
        fold = segment_distance(co, H((side * 0.052, -0.215, -0.040)), H((side * 0.095, -0.172, -0.120)))
        depth += 0.006 * np.exp(-(fold / 0.010) ** 2)
        frown = segment_distance(co, H((side * 0.012, -0.190, 0.070)), H((side * 0.018, -0.180, 0.110)))
        depth += 0.004 * np.exp(-(frown / 0.007) ** 2)
    set_coordinates(skin, co - n * depth[:, None])


def mouth_parts(collection, seed=11):
    """The dark interior filling the cavity, and big irregular teeth rooted in the gums."""
    centre, radii = MOUTH_CAVITY
    interior = ellipsoid("IMP_mouth_interior", H((centre[0], centre[1] + 0.035, centre[2])),
                         (radii[0] * 0.92, radii[1] * 0.55, radii[2] * 0.97), collection, 3)   # behind the teeth
    set_coordinates(interior, mouth_droop(coordinates(interior)))
    rng = random.Random(seed)
    teeth = []
    rows = (("upper", -0.082, -1, 7, 0.064, (0.028, 0.040), -0.222),
            ("lower", -0.138, 1, 6, 0.056, (0.022, 0.032), -0.212))
    for row, gum_z, pointing, count, half, lengths, front_y in rows:
        for k in range(count):
            if rng.random() < 0.12:
                continue                                         # a gap
            x = -half + 2 * half * k / (count - 1) + rng.uniform(-0.006, 0.006)
            y = front_y + 0.030 * (x / (half + 0.01)) ** 2
            root = np.array(mouth_droop(np.array([H((x, y, gum_z))]))[0])
            length = rng.uniform(*lengths) * (1.25 if abs(abs(x) - 0.042) < 0.012 else 1.0)
            width, thick = rng.uniform(0.021, 0.029), rng.uniform(0.011, 0.014)   # broad and blunt (portrait)
            tilt = Matrix.Rotation(math.radians(rng.uniform(-12, 12)), 3, "Y") @ Matrix.Rotation(
                math.radians(rng.uniform(-8, 14)), 3, "X")
            axis = tilt @ V((0, 0, pointing))
            across, depth_axis = tilt @ V((1, 0, 0)), tilt @ V((0, 1, 0))
            start = V(root) - axis * 0.012                       # rooted inside the gum
            sections = [ring(start + axis * ((length + 0.012) * f), across, depth_axis,
                             width / 2 * r, thick / 2 * r, 10)
                        for f, r in ((0, 1.0), (0.5, 0.97), (0.85, 0.80), (1.0, 0.50))]
            teeth.append(loft(f"tooth_{row}_{k}", sections, collection))
    return interior, join(teeth, "IMP_teeth")


def eyeballs(collection):
    result = []
    for side, centre, forward, right, up in eye_frames():
        eye = ellipsoid(f"IMP_eye_{'L' if side > 0 else 'R'}", centre, (P["eye_radius"],) * 3, collection, 5)
        shade(eye)
        result.append(eye)
    return result


# ---------------------------------------------------------------- the body

def skeleton(collection):
    """Spine, arms, legs, hands and feet as (position, radius) nodes, one capsule per edge."""
    nodes, edges = [], []

    def add(position, radius, parent=None):
        nodes.append((V(position), radius))
        if parent is not None:
            edges.append((parent, len(nodes) - 1))
        return len(nodes) - 1

    def chain(points, parent):
        for position, radius in points:
            parent = add(position, radius, parent)
        return parent

    # A curved back: pelvis behind, chest forward, neck reaching forward and down into the head.
    pelvis = add((0, 0.065, 0.31), 0.075)
    belly = add((0, 0.025, 0.42), 0.09, pelvis)
    chest = add((0, 0.020, 0.53), 0.085, belly)
    chain([((0, -0.010, 0.600), 0.075), ((0, -0.055, 0.655), 0.074), ((0, -0.080, 0.700), 0.072)], chest)
    for s in (-1, 1):
        shoulder = add((s * 0.130, 0.000, 0.600), 0.046, chest)       # rolled forward and down
        elbow = add((s * 0.205, -0.005, 0.450), 0.029, shoulder)
        if s < 0:        # right hand grips the pick handle (runs along +Y), fingers from above
            gx, gy, gz = P["grip"]
            wrist = add((gx + 0.005, gy + 0.040, gz + 0.032), 0.022, elbow)
            palm = add((gx - 0.012, gy, gz + 0.012), 0.028, wrist)
            for offset in (-0.022, 0.0, 0.022):
                chain([((gx - 0.030 * math.cos(math.radians(a)), gy + offset, gz + 0.030 * math.sin(math.radians(a))), 0.011)
                       for a in (100, 40, -20, -80)], palm)
            chain([((gx + 0.018, gy - 0.02, gz + 0.02), 0.011), ((gx + 0.026, gy - 0.035, gz - 0.005), 0.009)], palm)
        else:            # left arm hangs from the forward shoulder, hand loosely curled
            wrist = add((0.232, -0.045, 0.325), 0.022, elbow)
            palm = add((0.238, -0.062, 0.288), 0.028, wrist)
            for offset in (-0.018, 0.0, 0.018):
                chain([((0.238 + offset * 0.4, -0.078 + offset, 0.255), 0.011),
                       ((0.235 + offset * 0.5, -0.092 + offset, 0.228), 0.010),
                       ((0.228 + offset * 0.5, -0.086 + offset, 0.208), 0.008)], palm)
            chain([((0.222, -0.085, 0.283), 0.010), ((0.217, -0.100, 0.263), 0.008)], palm)
        hip = add((s * 0.075, 0.065, 0.30), 0.055, pelvis)
        knee = add((s * 0.130, -0.050, 0.170), 0.043, hip)                 # knees bent forward
        ankle = add((s * 0.150, 0.030, 0.065), 0.034, knee)
        add((s * 0.150, 0.088, 0.030), 0.030, ankle)                       # heel
        mid = add((s * 0.160, -0.040, 0.032), 0.042, ankle)
        ball = add((s * 0.172, -0.115, 0.028), 0.040, mid)
        splay = math.radians(14) * s
        for offset in (-0.038, 0.0, 0.038):
            tip_x = s * 0.172 + offset + math.sin(splay) * 0.09
            chain([((s * 0.172 + offset * 0.7, -0.15, 0.026), 0.020),
                   ((tip_x, -0.185, 0.020), 0.016), ((tip_x + math.sin(splay) * 0.02, -0.203, 0.016), 0.010)], ball)
    pieces = [capsule(f"limb_{a}_{b}", nodes[a][0], nodes[a][1], nodes[b][0], nodes[b][1], collection)
              for a, b in edges]
    return join(pieces, "skeleton")


def build_skin(collection, voxel):
    parts = face_masses(collection) + [ear(1, collection), ear(-1, collection), skeleton(collection),
             ellipsoid("chest_mass", (0, 0.040, 0.545), (0.120, 0.080, 0.100), collection),
             ellipsoid("belly_mass", (0, -0.025, 0.420), (0.105, 0.090, 0.105), collection),
             ellipsoid("seat_mass", (0, 0.085, 0.315), (0.095, 0.075, 0.070), collection),
             ellipsoid("upper_back", (0, 0.100, 0.585), (0.125, 0.082, 0.105), collection),
             ellipsoid("trapezius", (0, 0.020, 0.630), (0.120, 0.075, 0.050), collection)]
    skin = join(parts, "IMP_skin")
    remesh = skin.modifiers.new("fuse", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = voxel
    smooth = skin.modifiers.new("soften", "CORRECTIVE_SMOOTH")
    smooth.factor, smooth.iterations, smooth.use_only_smooth = 0.5, 9, True
    apply_modifiers(skin)
    lap = skin.modifiers.new("soften2", "LAPLACIANSMOOTH")
    lap.lambda_factor, lap.iterations = 0.5, 3
    apply_modifiers(skin)
    # The mouth opening: a cavity between the lips, filled later by the dark interior.
    centre, radii = MOUTH_CAVITY
    cavity = ellipsoid("mouth_cavity", H(centre), radii, collection, 4)
    set_coordinates(cavity, mouth_droop(coordinates(cavity)))
    cut = skin.modifiers.new("mouth", "BOOLEAN")
    cut.operation, cut.object = "DIFFERENCE", cavity
    solvers = [e.identifier for e in cut.bl_rna.properties["solver"].enum_items]
    cut.solver = "MANIFOLD" if "MANIFOLD" in solvers else "EXACT"
    apply_modifiers(skin)
    bpy.data.objects.remove(cavity)
    face_folds(skin)
    lids = wrap_eyelids(skin)
    shade(skin)
    return skin, lids


# ---------------------------------------------------------------- clothing and equipment

def surface(obj):
    return BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())


def snap(bvh, point, offset):
    location, normal, _, _ = bvh.find_nearest(point)
    return location + normal * offset, normal


def ribbon(name, bvh, guide, width, offset, thickness, collection, samples=48):
    """A flat leather band following `guide` over the surface: flat-shaded, square-edged."""
    guide = [V(g) for g in guide]
    points = []
    for k in range(samples + 1):
        t = k / samples * (len(guide) - 1)
        i = min(int(t), len(guide) - 2)
        f = t - i
        p0, p1, p2, p3 = guide[max(i - 1, 0)], guide[i], guide[i + 1], guide[min(i + 2, len(guide) - 1)]
        points.append(0.5 * ((2 * p1) + (-p0 + p2) * f + (2 * p0 - 5 * p1 + 4 * p2 - p3) * f * f
                             + (-p0 + 3 * p1 - 3 * p2 + p3) * f ** 3))
    snapped = [snap(bvh, p, offset) for p in points]
    vertices, faces = [], []
    for k, (p, n) in enumerate(snapped):
        tangent = (snapped[min(k + 1, samples)][0] - snapped[max(k - 1, 0)][0]).normalized()
        side = n.cross(tangent).normalized() * (width / 2)
        vertices += [p - side, p + side]
        if k:
            faces.append((2 * k - 2, 2 * k - 1, 2 * k + 1, 2 * k))
    obj = mesh_object(name, vertices, faces, collection)
    solid = obj.modifiers.new("thickness", "SOLIDIFY")
    solid.thickness, solid.offset = thickness, 1.0
    apply_modifiers(obj)
    shade(obj, smooth=False)
    return obj


def band_around(name, bvh, centre, z_values, collection, offset, count=48, thickness=0.006):
    sections = []
    for z in z_values:
        origin = V((centre[0], centre[1], z))
        section = []
        for i in range(count):
            a = 2 * math.pi * i / count
            direction = V((math.cos(a), math.sin(a), 0))
            hit, _, _, _ = bvh.ray_cast(origin, direction)
            section.append((hit if hit is not None else origin + direction * 0.05) + direction * offset)
        sections.append(section)
    obj = loft(name, sections, collection, cap=False)
    solid = obj.modifiers.new("thickness", "SOLIDIFY")
    solid.thickness = thickness
    apply_modifiers(obj)
    shade(obj)
    return obj


def waist_cloth(bvh, collection, seed=5):
    """Belt, and front and back cloth with thickness, curvature, broad folds and an uneven hem."""
    rng = random.Random(seed)
    belt = band_around("IMP_belt", bvh, (0, 0.05), (0.315, 0.335, 0.355), collection, 0.006)
    flaps = []
    for name, centre_angle, length, flare, folds in (("IMP_loin_front", -90, 0.135, -0.035, 3),
                                                     ("IMP_loin_back", 90, 0.085, 0.025, 2)):
        columns, cols, rows = [], 17, 10
        hem = [rng.uniform(-0.012, 0.012) for _ in range(cols)]
        for k in range(cols):
            a = math.radians(centre_angle - 40 + 80 * k / (cols - 1))
            direction = V((math.cos(a), math.sin(a), 0))
            top, _, _, _ = bvh.ray_cast(V((0, 0.05, 0.315)), direction)
            top = (top or V((0, 0.05, 0.315)) + direction * 0.1) + direction * 0.009
            u = k / (cols - 1)
            taper = 1 - 0.28 * abs(u - 0.5) * 2
            column = []
            for r in range(rows):
                t = r / (rows - 1)
                fold = 0.010 * t * math.sin(math.pi * folds * u * 2)       # folds deepen toward the hem
                drop = (length + hem[k]) * t
                outward = direction * (fold + 0.004 * math.sin(math.pi * t))
                column.append(V((top.x * (1 - 0.18 * t) * taper ** t, top.y + flare * t, top.z - drop)) + outward)
            columns.append(column)
        vertices = [v for column in columns for v in column]
        faces = [(c * rows + r, c * rows + r + 1, (c + 1) * rows + r + 1, (c + 1) * rows + r)
                 for c in range(cols - 1) for r in range(rows - 1)]
        flap = mesh_object(name, vertices, faces, collection)
        solid = flap.modifiers.new("thickness", "SOLIDIFY")
        solid.thickness = 0.011
        flap.modifiers.new("soft", "SUBSURF").levels = 1
        apply_modifiers(flap)
        shade(flap)
        flaps.append(flap)
    return [belt] + flaps


def sack(collection, voxel, body, seed=3):
    """Heavy, sagging, unevenly packed, gathered at a tied neck, resting against the back."""
    rng = random.Random(seed)
    c, r = V(P["sack_centre"]), V(P["sack_radii"])
    obj = icosphere("sack_body", 5, collection)
    u = coordinates(obj)
    u /= np.linalg.norm(u, axis=1)[:, None]
    z = u[:, 2]
    profile = 1.0 - 0.10 * z - 0.30 * np.clip(z, 0, None) ** 2 + 0.08 * np.clip(-z, 0, None) ** 2   # full, sagging base
    for _ in range(6):                                                    # packed bulges, facing away from the back
        direction = np.array([rng.uniform(-1, 1), rng.uniform(0.2, 1), rng.uniform(-0.8, 0.5)])
        direction /= np.linalg.norm(direction)
        profile += rng.uniform(0.12, 0.18) * np.exp(-(np.arccos(np.clip(u @ direction, -1, 1)) / 0.38) ** 2)
    phi = np.arctan2(u[:, 1], u[:, 0])
    gather = smoothstep((z - 0.55) / 0.4)
    profile *= 1 + 0.10 * np.sin(9 * phi) * gather                         # fabric gathered to the neck
    p = u * np.array(r) * profile[:, None]
    p[:, 2] -= 0.035 * np.clip(-z, 0, None)                               # gravity
    p = p @ np.array(Matrix.Rotation(math.radians(-8), 3, "X")).T
    set_coordinates(obj, p + np.array(c))
    top = V(p[np.argmax(p[:, 2])] + np.array(c)) - V((0, 0, 0.02))
    neck = loft("sack_neck", [ring(top + V((0, 0.006 * k, 0.012 * k)), V((1, 0, 0)), V((0, 1, 0)), rx, ry, 18)
                              for k, (rx, ry) in enumerate(((0.05, 0.045), (0.032, 0.028), (0.024, 0.021), (0.022, 0.019)))],
                collection)
    tuft = []
    for k, (rx, ry) in enumerate(((0.022, 0.019), (0.036, 0.031), (0.046, 0.040), (0.040, 0.034))):
        section = ring(top + V((0, 0.02 + 0.008 * k, 0.03 + 0.016 * k)), V((1, 0, 0)), V((0, 1, 0)), rx, ry, 18)
        tuft.append([v + (v - (top + V((0, 0.02 + 0.008 * k, 0.03 + 0.016 * k)))) * (0.18 * math.sin(6 * i) * (k / 3))
                     for i, v in enumerate(section)])                       # ruffled mouth of the sack
    tie = loft("sack_tuft", tuft, collection)
    obj = join([obj, neck, tie], "IMP_sack")
    remesh = obj.modifiers.new("fuse", "REMESH")
    remesh.mode, remesh.voxel_size = "VOXEL", voxel * 1.3
    obj.modifiers.new("soften", "CORRECTIVE_SMOOTH").iterations = 2    # keep the bulges (v014 lost them)
    apply_modifiers(obj)
    # Rest it against the back: no sack vertex inside the body.
    bvh = surface(body)
    co = coordinates(obj)
    for i, point in enumerate(co):
        location, normal, _, _ = bvh.find_nearest(point)
        if location is not None and (V(point) - location).dot(normal) < 0.004:
            co[i] = location + normal * 0.004
    set_coordinates(obj, co)
    obj.modifiers.new("settle", "CORRECTIVE_SMOOTH").iterations = 2
    apply_modifiers(obj)
    shade(obj)
    return obj


def pickaxe(collection):
    head, end = V(P["pick_head"]), V(P["pick_handle_end"])
    axis = (end - head).normalized()
    _, a1, a2 = frame_axes(axis, (0, 0, 1))
    handle = loft("IMP_pick_handle", [ring(head + axis * ((end - head).length * t) - axis * 0.02, a1, a2,
                                           0.016 - 0.003 * t, 0.016 - 0.003 * t, 12) for t in (0, 0.5, 1.0)],
                  collection)
    sections = []
    for k in range(25):
        s = -1 + 2 * k / 24
        point = head + V((0, 0.10 * s * s, 0.26 * s))
        taper = (1 - abs(s)) ** 0.7
        tangent = V((0, 0.20 * s, 0.26)).normalized()
        sections.append(ring(point, V((1, 0, 0)), V((0, -tangent.z, tangent.y)),
                             0.004 + 0.012 * taper, 0.004 + 0.022 * taper, 10))
    blade = loft("IMP_pick_head", sections, collection)
    collar = loft("IMP_pick_collar", [ring(head + axis * t, a1, a2, 0.022, 0.022, 12) for t in (-0.025, 0.025)],
                  collection)
    for obj in (handle, blade, collar):
        shade(obj)
    return [handle, blade, collar]


def ornament(bvh, collection):
    position, normal = snap(bvh, V((0, -0.14, 0.50)), 0.010)
    rotation = normal.to_track_quat("Z", "Y").to_matrix()
    orn = ellipsoid("IMP_ornament", position, (0.034, 0.030, 0.011), collection, 3, rotation=rotation)
    shade(orn)
    return orn


# ---------------------------------------------------------------- materials

def principled(name, colour, roughness=0.6, metallic=0.0, coat=0.0, linear=False):
    material = bpy.data.materials.new(name)
    node = material.node_tree.nodes.get("Principled BSDF")
    node.inputs["Base Color"].default_value = (*(colour if linear else srgb_to_linear(colour)), 1)
    node.inputs["Roughness"].default_value = roughness
    node.inputs["Metallic"].default_value = metallic
    if coat:
        node.inputs["Coat Weight"].default_value = coat
        node.inputs["Coat Roughness"].default_value = 0.03
    return material


def skin_material():
    """Base colour = the object's `skin_color` property (linear RGB); nothing else is tinted."""
    material = principled("M_imp_skin", (0.5, 0.5, 0.5), roughness=0.55)
    tree = material.node_tree
    attribute = tree.nodes.new("ShaderNodeAttribute")
    attribute.attribute_type = "OBJECT"
    attribute.attribute_name = "skin_color"
    tree.links.new(attribute.outputs["Color"], tree.nodes["Principled BSDF"].inputs["Base Color"])
    return material


def set_skin_colour(obj, variant):
    obj["skin_color"] = list(srgb_to_linear(SKIN_VARIANTS[variant]))
    obj["skin_variant"] = variant
    obj.update_tag()


# ---------------------------------------------------------------- scene

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="imp_blockout.py")
    parser.add_argument("--reference", required=True, help="the original reference .blend (read only)")
    parser.add_argument("--reference-object", default="Imp_Idle1")
    parser.add_argument("--portrait", help="appearance reference image, shown in the scene as an image empty")
    parser.add_argument("--out", required=True)
    parser.add_argument("--version", help="vNNN; default: the next free one")
    parser.add_argument("--voxel", type=float, default=0.0030, help="remesh voxel at height 1")
    parser.add_argument("--note", default="", help="what changed in this version, kept in params.json")
    args = parser.parse_args(argv)
    args.out = os.path.abspath(args.out)
    if not args.version:
        taken = [int(m.group(1)) for d in (os.listdir(args.out) if os.path.isdir(args.out) else [])
                 if (m := re.fullmatch(r"v(\d{3})", d))]
        args.version = f"v{max(taken, default=0) + 1:03d}"
    return args


def append_reference(path, name, collection):
    with bpy.data.libraries.load(path, link=False) as (_, target):
        target.objects = [name]
    obj = target.objects[0]
    collection.objects.link(obj)
    bpy.context.scene.frame_set(0)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = obj.evaluated_get(depsgraph).to_mesh()
    co = np.array([obj.matrix_world @ v.co for v in mesh.vertices])
    obj.evaluated_get(depsgraph).to_mesh_clear()
    return obj, co.min(axis=0), co.max(axis=0)


def main():
    args = parse_args()
    version_dir = os.path.join(args.out, args.version)
    if os.path.exists(os.path.join(version_dir, "imp_blockout.blend")):
        sys.exit(f"{version_dir} already holds a blockout; versions are never overwritten")
    os.makedirs(version_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    collections = {}
    for name in ("modern", "original", "reference", "cameras", "build"):
        collections[name] = bpy.data.collections.new(name)
        scene.collection.children.link(collections[name])

    original, low, high = append_reference(os.path.abspath(args.reference), args.reference_object,
                                           collections["original"])
    height = float(high[2] - low[2])
    root = bpy.data.objects.new("IMP_root", None)
    root.scale = (height, height, height)
    collections["modern"].objects.link(root)

    build = collections["build"]
    skin, lids = build_skin(build, args.voxel)
    bvh = surface(skin)
    interior, teeth = mouth_parts(build)
    strap_width, strap_offset, strap_thick = 0.034, 0.004, 0.005
    straps = []
    for s, n in ((1, "L"), (-1, "R")):
        straps.append(ribbon(f"IMP_strap_{n}", bvh, [(s * 0.012, -0.14, 0.50), (s * 0.055, -0.11, 0.565),
                                                     (s * 0.095, -0.04, 0.625), (s * 0.090, 0.05, 0.660),
                                                     (s * 0.075, 0.13, 0.665)], strap_width, strap_offset, strap_thick, build))
        straps.append(ribbon(f"IMP_strap_lower_{n}", bvh, [(s * 0.060, -0.10, 0.36), (s * 0.115, -0.02, 0.40),
                                                           (s * 0.120, 0.08, 0.44), (s * 0.090, 0.15, 0.46)],
                             strap_width * 0.85, strap_offset, strap_thick, build))
    straps.append(ribbon("IMP_strap_front", bvh, [(0, -0.14, 0.49), (0, -0.14, 0.44), (0, -0.125, 0.36)],
                         strap_width * 0.9, strap_offset, strap_thick, build))
    parts = {"skin": [skin], "eyes": eyeballs(build), "teeth": [teeth], "mouth": [interior],
             "cloth": waist_cloth(bvh, build),
             "bands": [band_around(f"IMP_ankle_band_{n}", bvh, (s * 0.15, 0.03), (0.075, 0.095, 0.115), build, 0.005)
                       for s, n in ((1, "L"), (-1, "R"))],
             "straps": straps, "ornament": [ornament(bvh, build)], "sack": [sack(build, args.voxel, skin)],
             "pick": pickaxe(build)}

    materials = {
        "skin": skin_material(),
        "eyes": principled("M_imp_eye", (0.12, 0.003, 0.005), roughness=0.06, coat=1.0, linear=True),
        "teeth": principled("M_imp_teeth", (0.74, 0.66, 0.46), roughness=0.45),
        "mouth": principled("M_imp_mouth", (0.16, 0.05, 0.05), roughness=0.5),
        "leather": principled("M_imp_leather", (0.27, 0.16, 0.10), roughness=0.6),
        "cloth": principled("M_imp_cloth", (0.42, 0.20, 0.14), roughness=0.85),
        "band": principled("M_imp_band_cloth", (0.36, 0.38, 0.42), roughness=0.85),
        "sack": principled("M_imp_sack", (0.46, 0.35, 0.23), roughness=0.95),
        "wood": principled("M_imp_wood", (0.40, 0.26, 0.14), roughness=0.7),
        "metal": principled("M_imp_metal", (0.56, 0.56, 0.57), roughness=0.42, metallic=1.0),
        "bone": principled("M_imp_bone", (0.76, 0.70, 0.56), roughness=0.5),
    }
    assignment = {"skin": "skin", "eyes": "eyes", "teeth": "teeth", "mouth": "mouth", "cloth": "cloth",
                  "bands": "band", "straps": "leather", "ornament": "bone", "sack": "sack"}
    for group, objects in parts.items():
        for obj in objects:
            key = assignment.get(group) or ("wood" if obj.name == "IMP_pick_handle" else "metal")
            obj.data.materials.clear()
            obj.data.materials.append(materials[key])
            for c in obj.users_collection:
                c.objects.unlink(obj)
            collections["modern"].objects.link(obj)
            obj.parent = root
    set_skin_colour(skin, "olive")
    for obj in list(build.objects):
        bpy.data.objects.remove(obj)
    bpy.data.collections.remove(build)

    if args.portrait:
        image = bpy.data.images.load(os.path.abspath(args.portrait))
        image.pack()
        empty = bpy.data.objects.new("portrait", None)
        empty.empty_display_type = "IMAGE"
        empty.data = image
        empty.empty_display_size = 0.3
        empty.location = (0.35, 0.0, 0.18)
        empty.rotation_euler = (math.radians(90), 0, 0)
        collections["reference"].objects.link(empty)
    original.location.x = -0.3
    collections["original"].hide_render = True
    collections["reference"].hide_render = True

    counts = {obj.name: len(obj.data.polygons) for obj in collections["modern"].objects if obj.type == "MESH"}
    params = {"version": args.version, "note": args.note, "height": height, "voxel": args.voxel,
              "head": HEAD, "landmarks": P, "faceMasses": FACE_MASSES, "mouthCavity": MOUTH_CAVITY,
              "eyelids": {str(k): v for k, v in lids.items()}, "skinVariants": SKIN_VARIANTS,
              "reference": os.path.abspath(args.reference),
              "portrait": os.path.abspath(args.portrait) if args.portrait else None,
              "originalBounds": [low.tolist(), high.tolist()], "faces": counts,
              "blender": bpy.app.version_string}
    with open(os.path.join(version_dir, "params.json"), "w", encoding="utf-8") as stream:
        json.dump(params, stream, indent=2)
    shutil.copyfile(os.path.abspath(__file__), os.path.join(version_dir, "imp_blockout.py"))
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(version_dir, "imp_blockout.blend"), compress=True)
    print(f"-- {args.version}: {sum(counts.values())} faces in {len(counts)} objects -> {version_dir}; lids {lids}")


if __name__ == "__main__":
    main()
