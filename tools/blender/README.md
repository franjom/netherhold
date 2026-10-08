# Blender tooling

Headless Blender scripts for the asset pipeline (`docs/ASSET_PIPELINE.md`, ADR-0242). They build
and check the **original** reference; nothing here remodels, rigs or retextures. Everything they
write is derived from EA's art and lands under `build/` (gitignored); only these scripts are
committed.

## Pinned Blender

**Blender 5.2.2 LTS**, build hash `d13f752e3b9c` (2026-09-15), Python 3.13.13, glTF importer
5.2.40, verified 2026-10-06 with a portable install. `reference_scene.py` stops on any Blender
that is not 5.2.x unless given `--allow-version`, and records the exact version and hash in its
report. The scripts use
the 5.x data model: slotted layered actions (`action.layers → strips → channelbag(slot)`), no
`Action.fcurves`, `ImageFormatSettings.media_type` for video.

Machine paths are never written into a script: pass them, or set them per shell.

| what | how |
|---|---|
| Blender executable | `-Blender <path>` or `$env:BLENDER` |
| DK2 install | `-Dk2Dir <path>` or `$env:DK2_DIR` (as everywhere else, `Dk2Install`) |

## The Imp reference, in one command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/blender/imp-reference.ps1 -Blender <blender.exe> -Dk2Dir <DK2 install>
```

About six minutes, five of them the previews (`-NoVideo` skips them, `-NoRender` skips all
rendering). It runs, in order:

1. `dotnet build src/DK2.Tools -c Release`
2. from `build/`: `dotnet ../src/DK2.Tools/bin/Release/net10.0/DK2.Tools.dll convert --out converted-assets/ > convert.log`.
   The converter has **no subject filter** and only writes below `./converted-assets/`, so the
   whole catalogue (2,152 keys) goes to `build/converted-assets/`, apart from the reference.
3. `python tools/blender/stage_reference.py --converted build/converted-assets --log build/convert.log --subject creature.imp --out build/reference/imp/source --install <DK2> --key creature.imp.animation.idle --key creature.imp.animation.moving_to_job --key creature.imp.animation.working --key creature.imp.animation.dying --key creature.imp.mesh`
4. `<blender> --background --factory-startup --python-exit-code 1 --python tools/blender/reference_scene.py -- --sources build/reference/imp/source/sources.json --out build/reference/imp --blend imp_reference.blend`

Steps 3 and 4 run alone against an existing conversion.

## Scripts

**`stage_reference.py`** (plain CPython). Picks one subject's keys out of the conversion log,
keeps one GLB per distinct source KMF, and copies each with every file it references, keeping
the converter's relative layout so texture URIs resolve unchanged. `sources.json` records the
converter command, git commit, each clip's catalogue keys and source KMF, and SHA-256 of every
copied file and of `Meshes.WAD` and `Creatures.kwd`.

**`reference_scene.py`** (inside Blender). Also takes `--glb NAME=PATH` per clip instead of
`--sources`. From an empty factory scene, imports each GLB with Blender's own importer into
`original/<clip>`, then checks, failing the run (exit 1) on any FAIL:

- meshes imported, rest coordinates finite, a finite UV layer;
- every material's base colour is driven by an image that has pixels;
- the shape keys are driven by an action slot with more than one key;
- **file against import**, the GLB parsed directly: triangle count (a two-sided pair Blender
  merges is a WARN, not a FAIL), morph targets = shape keys = F-curves, interpolation, every key
  time, and each key frame's bounding box;
- **evaluated**, not just present: the deformed mesh at every key and every midpoint is finite,
  keeps its vertex count, matches the file's bounds, is the linear blend of its keys at each
  midpoint, and moves.

Then renders, unless told not to: seven views of the first clip's first frame (lit EEVEE with a
neutral world and three suns; unlit Workbench flat texture colour, for texel checks), each clip
at `--frames` evenly spaced times from two views, contact sheets, and per clip a numbered PNG
sequence encoded to H.264 at the scene's 24 fps, looped to at least 3 s. Cameras are
orthographic and framed from the original's bounds (a clip's frames from its bounds over every
key). View transform `Standard`; no smoothing, subdivision or texture changes.

Outputs in `--out`: `<blend>` (textures packed), `report.json`, `report.md`, `blender.log` (from
the wrapper), `renders/{views,frames}/`, `renders/sheet_*.png`, `renders/preview_<clip>.mp4` and
`preview_<clip>_frames/`.

The scene's front is −Y (the creature faces the camera at azimuth 0); "right" is the creature's own
right. The conversion is not mirrored: the converted Imp holds its pick in its right hand, as
DK2's does (Q568).

## Modern blockouts

**`imp_blockout.py`** builds new Imp geometry (not a subdivided original) into
`<out>/<vNNN>/imp_blockout.blend`, taking the next free version so none is overwritten, with
`params.json` (landmarks, masses, note, face counts) and a copy of the script that built it. The
skin is one voxel-remeshed surface fused from face masses (`FACE_MASSES`: cranium, forehead, a
socket over each eyeball, brows, cheeks, muzzle, nose, lips, jaw, chin, nape), lofted ears with an
inner fold, tapered capsules per limb segment and torso masses. A mouth cavity is then cut
(Boolean), and `wrap_eyelids` seats each eyeball: skin inside the lid arcs is tucked behind the
ball, skin around it is pulled onto it, none may stay inside it, and the stretched faces are
relaxed. Eyes, teeth, mouth interior, clothing and equipment are separate objects. The original
is appended into `original` for comparison; the reference .blend is only read. Skin colour is the
object property `skin_color` (linear RGB), read by the skin material's Attribute node;
`SKIN_VARIANTS` holds the previews.

**`review_renders.py`** renders a blockout into `renders/` and labelled `sheets/` beside it (or
`--out`): head views (front, three-quarter, profile, the portrait's angle) and body views in
colour and clay, a tight portrait-angle head beside a crop of the portrait at the same displayed
height and the portrait's aspect, the skin variants, the original under the same cameras, front
and side silhouette overlays (a diagnostic, not a score), and a gameplay-distance view (pitch 55°
as `DungeonRunner`, vertical FOV 60°, 10 tiles across 4:3; one tile is one unit, as a converted
wall face measures). `--head-only --out <dir>` re-shoots an earlier version's head without
touching its folder; `--baseline <dir>` then puts it above the new head in a same-camera sheet.
Cameras are framed from the original's bounds, so every version is shot alike.

**`sheets.py`** loads, resizes, labels and lays out PNGs inside Blender (labels are rendered text).

## Image-to-3D candidates

**`concept_inputs.py`** cuts the concept regions listed in `art/manifests/creature/imp.yaml` into
square PNG inputs, padded with the concept's own grey, and records hashes in `inputs.json`. It
alters no pixel and uploads nothing.

**`evaluate_candidate.py`** inspects a generated mesh (GLB, glTF, FBX, OBJ or PLY):
- it normalises the mesh to the original Imp's height, facing −Y (`--yaw`);
- it reports topology, loose parts (fused accessories), non-manifold and boundary edges, hidden
  faces, mirror symmetry, UV coverage and overlap, texture use and native scale;
- it renders colour, clay, unlit albedo and per-part views, head close-ups, and labelled
  comparisons with the concept regions and the original under the same camera.

**`tripo_multiview.py`** (plain CPython) prepares one Tripo multiview-to-model task: the input
hashes, the request and the credit estimate go to `trial.json`, and nothing is sent. Only
`--submit`, with `TRIPO_API_KEY` set and `--max-credits` at or above the estimate, uploads, runs
the task and downloads its outputs unchanged. It never calls rigging.

**`retopo_bake.py`** turns a dense candidate into a light game mesh carrying its detail:
- voxel remesh, cleaned for QuadriFlow (specks removed, edges under 1e-4 merged);
- QuadriFlow quads mirrored in X, shrink-wrapped onto the candidate;
- smart-projected UVs;
- Cycles bakes of base colour, normal, roughness and metallic at `--bake-size`, downscaled to
  `--texture-size`, roughness and metallic packed for glTF;
- comparison sheets and a gameplay-size view;
- `imp_low.glb`, `retopo.blend` and `report.json` in a new `--out` folder.

It neither rigs nor judges deformation.

**`imp_footwear.py`** adds the Imp's leather foot straps (separate objects) to a `retopo_bake.py`
.blend. It bakes the body and the straps into one re-packed texture set, with the wrap studs and
strap rivets painted in from temporary bake-only domes. It runs a temporary ankle bend test
(`--bend`, degrees) and writes `imp_footwear.glb`, `footwear.blend`, `report.json` and sheets to a
new `--out` folder. The decisions are in `docs/plans/2026-10-07-imp-footwear.md`.

`evaluate_candidate.py` approves nothing. The trial plan and commands are in
`docs/plans/2026-10-07-imp-image-to-3d-trial.md`.

The Imp's commands and review state: `docs/plans/2026-10-07-imp-blockout.md`.
