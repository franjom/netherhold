# Asset modernization pipeline

Status: **proposed, not built** (2026-09-26). Nothing here exists yet except
`DK2.Convert`, which already reads every KMF and texture. This is the initial
architecture, to be tested on three assets (§10) before anything is generalised.

The standard it serves is `docs/ART_DIRECTION.md`: *preserve design, increase
fidelity* and, for animation, *preserve motion, modernize representation*. The
decision and its reasons are ADR-0242. Each stage below is marked by how far it
can be trusted (§9); none is claimed to be more reliable than it is.

## 1. Where it sits

Nothing in the simulation or the presentation boundary changes. A modern asset
is one more answer to an `AssetRef` key (ADR-0034):

```
AssetRef ──► AssetProviderChain:  replacement ──► converted ──► placeholder
                                      │               │
                        modern pack (this doc)    DK2.Convert output
                                                  = the classic look (Goal 11)
```

- **The modern pack is served by the existing replacement tier.** No new key
  shape, category or provider interface is needed. Choosing between the classic
  and modern look is choosing whether that tier is installed.
- **`WorldView` still decides which animation an activity implies** (ADR-0035);
  the modern pack only supplies a better clip under the same key.
- **Collision meshes are presentation-only**: picking, the possession camera,
  effects. The simulation is tile-based and never reads them.
- **Licence:** every stage consumes EA's meshes and textures, so every output is
  a derivative work, treated as non-redistributable, and never committed
  (ADR-0242). What is committed is scripts, manifests, hashes and reports.

## 2. What the originals contain

Measured over `Meshes.WAD` on 2026-09-26 with `KmfReader` (format:
`src/DK2.Convert/README.md`): **2,015 KMF files, all decoded** — 862 static
meshes, 1,145 animations, 8 groups.

| input | where | notes |
|---|---|---|
| static mesh | KMF kind 1 | triangles per detail level, UVs, normals, materials |
| animation clip | KMF kind 2, one file per clip | per-frame vertex positions; **no skeleton, bones, bind pose or skin weights exist** |
| group | KMF kind 3 | placement semantics **UNKNOWN**; the converter reports these missing |
| textures | `DK2TextureCache/EngineTextures.dat` | decoded to PNG; named variants (`_DECAY`, per-keeper claimed floors); prefixes `#ADD#`, `&mask&` with unverified meaning |
| materials | KMF `MAT2` | brightness, shininess, environment texture; flags **UNKNOWN** |
| assignment | KWD records | which KMF a creature activity, object or room uses (`Dk2ArtCatalogue`) |

Three facts shape everything downstream:

1. **A creature's clips share one body.** Vertex counts differ from clip to clip
   (the Imp: 26 different counts over 32 clips), but that is vertex splitting.
   Distinct positions are constant per creature — Skeleton 112, Imp 96, Bile
   Demon 126 — and within a UV family every triangle matches the reference pose
   by index and UV. Props appear as appended geometry: the Bile Demon's Eat,
   Drink and bed clips keep all 212 body triangles and add the chicken, bottle
   or bed. **So one reference mesh per creature, and one mesh sequence across
   all its clips, is recoverable.**
2. **Some clips use a second UV family.** Five Imp files (`Imp.kmf`, the Claim1
   and Dig2 start/end clips) have the same 187 triangles over 96 positions but a
   different UV layout; the `Dejected_Pose` meshes differ the same way. How those
   correspond to the main family is Q443.
3. **The playback rate is not in the file.** `KmfReader` interpolates between
   key entries and `GlbWriter` writes `LINEAR` keys over frames/FPS seconds from
   the art descriptor, 30 fps where a KMF has none
   (`docs/research/kmf-animation-playback.md`, Q442, ADR-0389). Frame counts are exact.

Precision: animated positions are packed as signed 10-bit components scaled by
the clip's geometry scale, so the reference carries roughly 1/512 of that scale
per axis. No numeric comparison can be tighter than one step.

## 3. Artefacts, manifests and versions

| artefact | where | committed | mutable |
|---|---|---|---|
| **source**: the user's install | `DK2_DIR` | never | never; identified by SHA-256 per file |
| **reference set**: decoded frames, textures, reference renders | `build/reference/<category>/<subject>/` | never | regenerated bit-identically from the source, never edited, never deleted by the pipeline |
| **working scene** | `build/modern/<…>/work.blend` | never | generated; discarded freely |
| **hand-touched source** (manual 3D work) | private store (§11) | never in this repo | versioned |
| **modern outputs**: meshes, LODs, textures, clips | `build/modern/<category>/<subject>/<version>/` | never | immutable per version, content-addressed |
| **manifest** | `art/manifests/<category>/<subject>.yaml` | **yes** | edited by people |
| **scripts** | `tools/blender/`, `dk2tools art …` (neither exists yet) | **yes** | code |
| **validation report** | beside the outputs; summary in the manifest | summary only | per version |

The canonical animation reference is the KMF file itself, pinned by hash, plus
the tested decoder. The decoded frame set is a cache of it that the pipeline
never discards, so ORIGINAL versus MODERN can be compared at any time without
returning to reverse engineering.

The manifest is the reproducibility contract: everything needed to rebuild a
version, and the owner's approval of it. A proposal, to evolve with the PoC:

```yaml
asset: creature.imp                 # AssetRef prefix; one manifest per subject
source:
  meshes:  [Imp-PoseFrame.kmf]      # reference pose
  clips:   [IMP-Dig2.kmf, Imp-Idle2.kmf, ...]   # every clip, not a selection
  textures: [impfrnt, impback]
  sha256:  { Imp-PoseFrame.kmf: "…", … }
standard:                           # ART_DIRECTION.md; not switches to relax
  preserveSilhouette: true
  preserveProportions: true
  preserveMotion: true
  allowRedesign: false
  photorealism: false
rig:
  method: decomposition             # or template; §5, decided by the PoC
  maxInfluences: 4
stages:                             # each: tool, version, parameters, seed
  - { stage: geometry, provider: constrained-smooth, version: "…", params: {…} }
  - { stage: textures, provider: "…", version: "…", seed: 1234 }
lod: { levels: 4, budgets: empirical }
outputs: { version: 3, sha256: "…" }
approval: { by: owner, date: 2026-10-02, outputs: "…" }   # void if outputs change
deviations: []                      # anything knowingly unlike the original, with a reason
```

Approval names the output hash, so any rebuild that changes a byte returns the
asset to *review required*.

## 4. The stages

Numbered as in the task that commissioned this document. Reliability classes
are defined in §9.

1. **Original inputs** — §2. *Reliable.*
2. **Extraction and conversion** — `DK2.Convert` already writes KMF to GLB
   (animation as morph targets, `LINEAR` keys) and textures to PNG. The PoC adds:
   exposing each vertex's geometry index so split vertices weld exactly rather
   than by position equality, and a lossless per-frame position export. C#,
   deterministic, unit-tested. *Reliable.*
3. **Blender source format** — a pinned Blender release, run headless
   (`blender --background --python`). Each asset gets a generated scene with
   fixed collections: `original` (reference pose and every clip), `modern`,
   `lod`, `collision`, `cameras`. The `.blend` is a working file; the manifest
   plus scripts rebuild it. *Reliable.*
4. **Reference renders** — both original and modern rendered **in the same
   Blender scene**, with the same cameras and lights, so a difference is the
   asset and not the renderer: front, rear, left, right, front and rear
   three-quarter, and top where useful; unlit base colour, lit, and a flat
   silhouette pass. Cameras are derived from the original's bounding box, never
   the modern one's. A screenshot of the asset in DK2 itself is a third reference
   for how the original *reads* under its own lighting. *Reliable.*
5. **AI-assisted stages** — any of 6–10 may use a provider (§6). *Experimental.*
6. **Geometry modernization** — no route is reliable for the core step, which
   is reconstructing intent (ART_DIRECTION §6). Candidates, in the order the PoC
   tries them:
   - **constrained smoothing**: subdivide, smooth, and project back within a
     distance bound of the original surface, keeping feature edges. Automated
     and faithful by construction, but adds roundness, not detail. The baseline;
     *reliable as a baseline*;
   - **provider reconstruction** from the reference renders, then non-rigidly
     registered onto the original and rejected on silhouette deviation.
     *Experimental;* image-to-3D tools tend to redesign, which is exactly what
     the gate exists to catch;
   - **manual or contracted sculpt** over the original as an underlay, for faces,
     hands and whatever the first two cannot do. *Manual.*

   Then retopology (Blender QuadriFlow or equivalent) and high-to-low normal
   baking. *Reliable, with visual review.*
7. **Texture and PBR modernization** — the original texture is the reference
   for colour. Upscaling and map derivation (normal, roughness, metallic, AO,
   height, emissive) are provider stages. *Experimental.* One check is fully
   automated: render the modern base colour unlit from the standard views,
   downsample to the original's texel density, and compare colour against the
   original's render (CIELAB ΔE, mean and 95th percentile). Named variants —
   `_DECAY`, the per-keeper floors — are modernized as a set, never one alone.
8. **Rig and skin** — §5. *Experimental.*
9. **Animation preservation** — every original clip is reconstructed, none is
   generated. §5. *Experimental; the metrics are reliable.*
10. **LOD generation** — Blender decimation from LOD0, with skin weights carried
    through. *Reliable mechanically;* budgets are empirical (§8).
11. **Validation** — §7. *Reliable as a veto, never as an approval.*
12. **Export format** — glTF 2.0 (`.glb`) is the archival interchange, the same
    format `DK2.Convert` writes. Whether Unity imports that or an FBX exported
    from the same scene is open (§11): glTFast imports skins and blend shapes,
    but its documentation calls Mecanim animation import limited (checked
    2026-09-26).
13. **Unity import** — an editor script in the adapter imports a version,
    assigns materials and records the import result in the report. *Reliable
    once the format is chosen.*
14. **Prefab generation** — the same script builds one prefab per `AssetRef`
    key: `LODGroup`, materials, collider, and an animator whose states are the
    clip keys `WorldView` asks for. How a Unity-side provider resolves a key to a
    prefab (Addressables address = key is the obvious candidate) is part of the
    PoC. Today `DungeonScene` draws placeholders only.
15. **Artefact and version tracking** — §3.

## 5. Animation reconstruction

The problem is constrained: **find a rig and per-frame bone transforms that
reproduce the original frames**, then carry that deformation onto the modern
mesh. Nothing is invented; the original frames are the target and the judge.

```
KMF clips ──► weld by geometry index ──► one mesh sequence (all clips of a creature)
          ──► fit rig + weights + bone keys ──► RIG-FIT CHECK (original mesh vs original frames)
          ──► transfer weights to modern mesh ──► bake clips at original frame times
          ──► TRANSFER CHECK (modern mesh vs original frames) ──► export
```

- **Fit all clips at once.** Because the body is shared (§2), a creature's clips
  concatenate into one sequence of thousands of frames, which is what skinning
  decomposition needs to separate joints that one clip never moves.
- **Two candidate fitting methods, compared on the Imp:**
  - *decomposition*: smooth skinning decomposition (SSDR, Le & Deng 2012) and its
    hierarchy-extracting successor (2014) solve bones, weights and keys directly
    from the sequence. Dem Bones is an open implementation to evaluate; its
    licence is to be checked before use. Fits closely; bones are unnamed and may
    not read as anatomy;
  - *template*: fit an anatomical skeleton to the reference pose, then solve each
    frame's bone rotations by least squares against the original vertices. Bones
    are readable and blend well; the fit may be looser.

  The winner is the simpler rig that meets the rig-fit threshold, not the
  closest fit.
- **Validate the rig on the original mesh first.** Skinning DK2's own low-poly
  mesh with the reconstructed rig and comparing it to DK2's own frames has exact
  correspondence, so it measures motion preservation alone. Only then is the
  modern mesh introduced, and its error is measured separately. A failure is
  thereby always attributable to the rig or to the transfer, never "somewhere".
- **Weights to the modern mesh** by surface correspondence from the original
  (Blender's data-transfer tools), then smoothed; automatic weights on the modern
  mesh as the fallback. At most four influences per vertex, for Unity.
- **Props and parts** — the chicken, bottle, bed, the Imp's horn, the Reaper's
  gem — are separate meshes on their own rigid bone, fitted to their original
  trajectory.
- **Timing:** each clip keeps its original frame count and is keyed at original
  frame times; the rate waits on Q442. Interpolating between original frames is
  a modernization of the representation and is allowed; moving a key is not.
- **Fallback for a clip no rig reproduces:** deformation transfer of the
  original frames onto the modern mesh, played as blend shapes or a vertex
  animation texture. The motion is kept; the rig is not.

## 6. Providers are replaceable

Every stage that may call an AI service or model is a provider behind one
contract, so experimenting with a different system is a manifest change:

```
reference package ──► [provider] ──► candidate ──► Blender ──► validation ──► approve or reject
```

- **Input:** the reference package — standard renders, silhouettes, original
  textures, dimensions and the original mesh — never a text prompt alone.
- **Output:** a candidate file plus the provider's name, version, parameters
  and seed, recorded in the manifest.
- **No stage depends on a provider's identity**, and no candidate reaches the
  modern pack without passing §7. A provider that is not reproducible (a hosted
  service with no seed) is allowed, but its output then *is* the source and is
  kept like hand-touched work (§3).

## 7. Validation

A report per asset version, as data (for tools) and as a contact sheet (for the
owner). Automated checks can mark an asset FAIL; only the owner can mark it
approved.

```
Asset: creature.imp  version 3
Original:  reference pose, 2 textures, 32 clips (2 UV families), 187 triangles
Modern:    LOD0–LOD3, 4 PBR materials, reconstructed rig (N bones), 32 clips
Validation:
  geometry        PASS    silhouette IoU ≥ … on all 7 views; bbox within …%
  proportions     PASS    per-axis extent ratios within …%
  materials       PASS    base-colour ΔE mean …, p95 …; all required maps present
  rig fit         PASS    worst frame RMS … (… quantization steps), IDLE_2 f117
  transfer        PASS    landmark RMS …; silhouette IoU per frame ≥ …
  clips           PASS    32/32 present; frame counts identical
  unity import    PASS
  visual review   REQUIRED
```

- **Static:** triangle counts (original, LOD0–3), dimensions and bounding-box
  deltas, scale and orientation, per-view silhouette IoU and overlays, one-sided
  surface distance from the original to the modern surface (normalised by the
  bounding-box diagonal), UV validity, texture resolutions, required PBR maps,
  Unity import status.
- **Animation:** synchronised side-by-side and overlay playback — original KMF
  frames beside the modern clip at the same time, scale, orientation, camera and
  speed; rig-fit error per frame on the original mesh; transfer error on
  landmarks (each original welded vertex bound to the modern rest surface) and
  per-frame silhouette IoU; missing clips and frame-count differences.
- **By level of change** (`ART_DIRECTION.md` §3), which is what the checks
  above are for:

  | level | automated veto | left to the owner |
  |---|---|---|
  | A — preserved | silhouette IoU, extents, base-colour ΔE, rig fit at original frames | whether identity and markings survive |
  | B — reconstructed | surface distance from the original stays within its bound | whether the shape is the one the original implied |
  | C — inferred detail | the render reduced to the original's texel density matches the original's; a detail that survives is not C | whether it suits the material |
  | D — redesign | none can detect one reliably | **every** asset is reviewed for it, and one rejects the candidate |
- **Thresholds are not set here.** They come out of the PoC, per asset class,
  from what the owner accepts and rejects. A threshold nobody has calibrated
  against a judgement would be a guess presented as a gate.

## 8. LOD and budgets

Four levels, `LOD0` close-up to `LOD3` crowds, through Unity's `LODGroup`. **No
polygon budget is fixed** until measured: the PoC renders the Lair kit (§10)
populated with increasing numbers of modern Imps on the target hardware, and
budgets follow from visual quality at gameplay camera distance, creature count,
GPU and skinning cost, and the render pipeline chosen (§11).

## 9. What can be trusted

| class | meaning | stages |
|---|---|---|
| **Reliable** | deterministic, scriptable, checkable today | extraction; Blender scene build; reference renders; retopology; normal baking; decimation; numeric validation; version tracking |
| **Experimental** | may work; the PoC finds out | intent reconstruction of geometry; provider geometry; texture upscaling and PBR map derivation; rig fitting; weight transfer to the modern mesh; Unity animation import |
| **Needs visual approval** | always, whatever the metrics say | every modern asset, every clip, every material set |
| **May need manual 3D work** | occasionally, by a human | faces and hands; any geometry the first two routes cannot make faithful; weight fixes at hard joints; seams in modular pieces |

## 10. Proof of concept

Three assets, chosen from the measurements in §2, then a stop to revise this
document before anything else is modernized.

**A. Creature — the Imp.** 187 triangles over 96 positions, two textures, 32
clips covering locomotion, digging, claiming, dragging and idles, plus three
static poses including a reference pose (`Imp-PoseFrame`). It exercises both UV
families (27 files and 5, Q443) and an appended part (the three horn clips). It
is the creature on screen most often, so its LODs are tested against the largest
crowd, and the simulation already drives its walk and dig activities through
`WorldView`, so a modern Imp can replace a placeholder in a running dungeon. It
is strongly stylized: head, ears, horns and claws. The Bile Demon follows as the soft-body test, and the Horned
Reaper (`REA3_*`: 15 mostly cinematic clips, 406–502 triangles, a gem prop) is
held back as a showcase rather than a first case.

**B. Static prop — the Training Dummy.** One primitive, 78 triangles, one
texture; placed by the Training Room's record (`objects.md`), so it appears in
a simulated dungeon, and creatures strike it, which gives collision a purpose.
The Gold Chest (89 triangles, 8 primitives, 3 textures) is the alternative if a
metal-and-wood material split is wanted from the first prop.

**C. Environment — the Lair kit.** Floor pieces `LAIR0`–`LAIR3`, plaster wall
pieces (end, small arch, plain, alcove, each with a `_DECAY` variant) and
`Lair_Pillar`. The Treasury uses the same plaster textures and a 66-triangle
pillar, so one modernization is tested for reuse across two rooms. Which piece
DK2 places where is not known (Q71), so the PoC arranges a fixture room by hand
from an in-game screenshot; the placement rule gates in-game use, not the asset.

**Workflow**, with the output of each step reviewed before the next begins:

1. Extract and weld: expose geometry indices, export exact frames, measure Q443
   on the Imp, and confirm the reference GLBs open in Blender. Close Q442 from
   the binary if it is cheap; otherwise key by frame and defer durations.
2. Build the Blender scenes and reference renders for all three. The owner
   confirms the renders look like DK2.
3. Geometry: constrained smoothing on all three first, then one provider route
   on the prop. Owner review.
4. Textures and PBR on all three; ΔE check; owner review.
5. Imp rig: both fitting methods, rig-fit check on the original mesh, choose.
6. Transfer to the modern Imp; bake all 32 clips; transfer check; side-by-side
   playback; owner review of every clip.
7. LODs and collision; export; Unity import and prefabs; the Imp, the dummy and
   the Lair kit in one Unity scene beside the classic assets.
8. The crowd measurement (§8), then budgets.
9. Revise this document: thresholds, the rig method, the export format, the
   manifest schema, and every stage whose reliability class turned out wrong.

**Exit:** all three pass automated validation and the art-direction test, the
rig method and export format are chosen, and the manifest rebuilds each asset
from the source with no undocumented step.

## 11. Open decisions

For the owner:

1. **Render pipeline.** The Unity project uses the built-in pipeline today.
   URP is the recommended candidate — creature crowds and hardware breadth favour
   it — with HDRP only if the PoC shows URP cannot reach the look.
2. **Unity interchange**: glTF through glTFast, or FBX. Decided in PoC step 7.
3. **Where hand-touched derived work lives.** It cannot enter this repository;
   a private store is needed before the first manual edit.
4. **When.** The PoC is pre-M11 work that no milestone schedules; M11 runs
   eighth (plan §8).
5. **External help and cost**: paid AI services, or a contracted artist for the
   manual class in §9.

For research: Q442 (playback rate), Q443 (UV-family correspondence), Q71 and
Q72 (wall and room pieces), and the KMF unknowns listed in
`src/DK2.Convert/README.md` (group placement, material flags, key-frame byte).
