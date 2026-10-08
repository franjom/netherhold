# Imp image-to-3D trial — plan

Status: **first candidate accepted by the owner as the shape** (2026-10-07): `t1-tripo-web`, from the owner's
free Tripo web account (results below). The API run `t1-tripo` was refused for lack of API
credit after its two images had been uploaded. Primitive-based construction is paused: the
procedural blockouts v001–v015 stay as comparisons (`2026-10-07-imp-blockout.md`). A generated
mesh is a candidate starting point, never an asset. Uploads so far, both approved by the owner for
a free-plan test: `reference/front.png` and `reference/rear.png`, to the Tripo API and the Tripo web
studio. Nothing is uploaded without the owner's explicit approval of the named service and plan.

## What this machine can do

Checked 2026-10-07:
- **No local image-to-3D capability.** AMD Ryzen 7 7735HS with an integrated Radeon 680M, 32 GB
  RAM, no NVIDIA GPU or CUDA. There are no installed models, Python ML packages (torch, diffusers),
  Blender add-ons, image-to-3D MCP tools, or service keys in the environment.
- **Blender 5.2.2** imports GLB, glTF, FBX, OBJ and PLY, and every Blender stage here runs.
- Docker Desktop and WSL Ubuntu exist, but do not help without a CUDA GPU.

## The options

From vendor documentation, licences and terms, read 2026-10-07. "Unconfirmed" means the page did
not render and the claim comes from search results. Prices and terms change, so re-read them
before paying.

| route | input | upload | fit here |
|---|---|---|---|
| [Tripo](https://developers.tripo3d.com/en/docs/generation-multiview-to-model/standard.md), paid plan | a front view plus at least one of left, back, right; ≥256 px; consistent lighting | yes | **best fit.** Paid plans are private and commercial, and the terms say inputs and outputs are not used for training. Quad output, `face_limit`, `generate_parts` (texture off), auto-rig. From $20 a month. |
| [Meshy](https://docs.meshy.ai/en/api/multi-image-to-3d), paid plan | 1–4 views of the same object | yes | good controls: quad, polycount, A/T-pose, separated parts, and a plugin tested on Blender 5.2. But its terms allow training on uploads from every non-Enterprise customer, paid ones included. |
| [Hyper3D Rodin](https://developer.hyper3d.ai/) | 1–5 images (unconfirmed) | yes | API access only on the Business plan ($120 a month); retention and training terms unconfirmed. |
| [Tencent Hunyuan 3D cloud](https://intl.cloud.tencent.com/document/product/1284/75540) | front plus left, right and back (3.1 adds more); plain background | yes | pricing and EU terms unconfirmed. |
| [Stable Fast 3D](https://github.com/Stability-AI/stable-fast-3d), local CPU | one image | none (the gated weights download needs the owner's Hugging Face account) | **the only no-upload route**: CPU fallback documented, run time not; one view; expected to be the weakest. Community licence, free under US$1M revenue a year. |
| [TRELLIS.2](https://github.com/microsoft/TRELLIS.2) on a rented NVIDIA GPU | one image | to the owner's own cloud instance | MIT; needs ≥24 GB VRAM. |
| [Hunyuan3D 2.x open weights](https://github.com/Tencent-Hunyuan/Hunyuan3D-2) | — | — | **excluded**: the licence does not apply in the EU, UK and South Korea. |

Free tiers make outputs public or vendor-owned (Tripo, Meshy) or list public gallery publishing
(Rodin). Tripo's terms: paid users own their inputs and outputs and they are not used for
training; for free users Tripo may train on them and holds the rights. The owner accepted that
for t1 (2026-10-07); a mesh kept for production must come from a paid run.

## Inputs

**Current (t1):** the clean base pair, `reference/front.png` and `reference/rear.png` (manifest
`concept.base_front`, `concept.base_rear`): no sack, pick, braces, belly plate or ornament. Scale,
pose and outline agree; the hands, ears and painted lighting do not (manifest
`base_pair_inconsistencies`).

**Earlier crops, superseded as inputs:**

`tools/blender/concept_inputs.py` cuts the manifest's regions (`art/manifests/creature/imp.yaml`)
into square PNGs on the concepts' grey and records their hashes in `inputs.json`. They are in
`build/modern/creature/imp/candidates/inputs/`: `front`, `rear`, `side`, `full_body`,
`face_closeup` and `pickaxe`. None is altered beyond the crop. All of them still show the rejected
belly plate and the sack. The `side` view is turned toward the camera, so it is not a true left
view. Lighting is painted in.

## Trial t1

**Tripo multiview, base creature, one task**, folder `candidates/t1-tripo/` (`params.json`,
`trial.json` from a dry run). Settings, checked against the endpoint's documentation on 2026-10-07:

| parameter | value | why |
|---|---|---|
| inputs | `front` = `reference/front.png`, `back` = `reference/rear.png` | front is required, ≥2 views |
| `model` | `v3.1-20260211` | latest H series |
| `texture`, `pbr` | true, true | textures on; PBR maps |
| `texture_quality` | `standard` | the cheapest textured tier |
| `texture_version`, `delight` | `v3.5-20260815`, true | removes painted lighting before texturing (v3.5 only) |
| `texture_alignment` | `original_image` | default |
| `geometry_quality` | `standard` | `detailed` adds 20 credits |
| `model_seed`, `texture_seed` | 1, 1 | reproducible |
| not sent | `quad`, `face_limit`, `smart_low_poly`, `generate_parts`, `auto_size`, `export_orientation` | defaults: triangles, adaptive face count |

No rigging: rigging is a separate endpoint (`animations-rig`) that nothing here calls.

**Cost:** 30 credits (multiview with standard texture; 100 credits = 1 USD, Tripo's pricing page,
2026-10-07). Failed or cancelled tasks are refunded. The task's `credits_consumed` is recorded.
The documentation and prices are from the China-mainland platform (`openapi.tripo3d.com`); the
owner's key is an overseas one and answers only at `openapi.tripo3d.ai/v3` (the script's default),
where the prices were not seen. The account's balance on 2026-10-07 was 0 credits.

**Run, 2026-10-07:** refused with code 2010, "not enough credit", after both images had been
uploaded (`t1-tripo/trial.json`, status `refused`). The command, for a funded account, with the key
only in the environment:

```
$env:TRIPO_API_KEY = "<key>"
python tools/blender/tripo_multiview.py --front build/modern/creature/imp/reference/front.png --back build/modern/creature/imp/reference/rear.png --params build/modern/creature/imp/candidates/t1-tripo/params.json --out build/modern/creature/imp/candidates/t1-tripo --submit --max-credits 30
```

Without `--submit` it only writes `trial.json`. With it, it checks the balance, uploads the two
images, creates the task, polls, downloads every output unchanged and records hashes and credits.

**Expected faults:** hands and ears averaged between contradictory views, invented toes behind,
residual painted light, asymmetry. Loincloth and ankle wraps fused into the body.

**No-upload alternative, Trial 0:** Stable Fast 3D on the CPU from `front.png`. It needs a
Python environment with PyTorch for the CPU (several GB, installed outside the repo), and the
owner must accept the model's gated access on Hugging Face. It is worth running only if no
upload is approved.

## Result: t1-tripo-web

The owner ran the same two images through the Tripo web studio (free plan; textured, no rig) and
exported `imp.glb` (68 MB, `b823d919…`). The folder `candidates/t1-tripo-web/` holds `trial.json`,
which lacks the model version and seeds, and `eval/`. The owner's copy stays at
`build/modern/creature/imp/imp.glb`.

From `eval/report.md` and its sheets:
- **Geometry is real, not texture.** In clay it holds the concept's anatomy: skull, ears, sunken
  cheeks, ribs, belly, wiry arms, the loincloth's tattered hem and the ankle wraps. Four toes on each
  foot, as in the approved front. The profile shows a long, backward-swept skull that neither input
  shows from the side.
- **One closed surface:** 1.91 M triangles, watertight after welding the UV-seam splits, no
  non-manifold edges. Eyes, teeth, loincloth and wraps are fused into the body. The teeth are a
  ridged block, and the eyes are smooth domes fused into the sockets.
- **Symmetric** within 0.2% of height. Native height 0.98 m, scaled by 0.369 to the original's 0.3615.
- **Textures:** 4096² base colour, roughness/metal and normal maps, UVs covering 63% without
  overlap. The albedo still carries the eyes' specular highlights and some crevice darkening.

**Accepted** by the owner on 2026-10-07 as the Imp's shape, the long skull included. It is a
free-plan output (Tripo holds its rights), so production art needs a paid regeneration first.
Cleanup then follows the order below, plus retopology from 1.9 M triangles.

**Not game-ready as generated (checked 2026-10-07).** 1.91 M triangles and 982 k vertices
give an estimated ~100 MB of mesh data, and ~1 M vertices to skin per visible Imp per frame.
Eyes and teeth are not the cause. Classified by albedo colour, they hold about 2.4% of the
triangles, at the same density per area as the rest of the body. A decimation test
(`eval/sheets/triangle_budget_test.png`) loses no visible detail at 20 k or 8 k triangles. It
cracks open along UV seams at 3 k, which a real retopology avoids. At the gameplay camera the Imp
is about 24 px tall, and every level looks the same. Decimation is only a cost probe: the game
mesh needs retopology, normal maps baked from this mesh, and LODs (`ASSET_PIPELINE.md` §8, §10).

**Retopology prototype** (`tools/blender/retopo_bake.py`, `t1-tripo-web/retopo/v001/`,
2026-10-07, two minutes on the CPU):
- **Mesh:** 5,252 quads (10,504 triangles, 5,402 vertices), mirrored in X.
- **Maps:** baked at 4096, shipped at 2048: base colour, normal, and roughness with metallic
  packed in one texture.
- **Material:** one, single-sided.

In colour it is hard to tell from the 1.9 M original at any distance.

Faults:
- **Eyes** come out near-black: the light surface does not follow the bulging balls. They need to
  be separate objects, as planned.
- **Loincloth flap** is lost: it is too thin for the remesh, and only the waistband survives in the
  colour. It needs to be a separate cloth object.
- **Fingers and ear rims** are softened.
- **Even grid of quads**, with no loops around eyes, mouth or joints. Deformation is untested
  until rigging.
- **Painted highlights** in the colour map are still there.

## Recording a run

Each run goes in `build/modern/creature/imp/candidates/<trial-id>/` with the downloaded files left
unchanged, plus a `trial.json` (`tripo_multiview.py` writes it) recording:
- the service, model version, plan and every parameter;
- the date and the seed, if any;
- the SHA-256 of each input from `inputs.json`;
- the SHA-256 of each output.

A run without a seed cannot be reproduced, so its output is kept like hand-made work
(`ASSET_PIPELINE.md` §6).

## Evaluation in Blender

```
<blender> -b --factory-startup --python-exit-code 1 --python tools/blender/evaluate_candidate.py -- --mesh build/modern/creature/imp/candidates/<trial-id>/<file> --out build/modern/creature/imp/candidates/<trial-id>/eval [--yaw <deg>]
```

The tool normalises the candidate to the original's height, facing −Y, and writes `report.md`,
`report.json`, `candidate.blend` and the following sheets:
- `body_{colour,clay,albedo,parts}` (six views);
- `head_{colour,clay}` (front, three-quarter, profile, the face concept's angle);
- `compare_{face_closeup,full_body,front,side,rear}`: the concept crop beside the candidate in
  colour and clay, and the original under the same camera.

It reports:
- topology: triangles, quads and n-gons, valence, edge-length spread;
- loose parts, where one part means the accessories are fused;
- non-manifold and boundary edges, and degenerate faces;
- hidden faces, those no outside ray reaches;
- mirror deviation for the body and the head;
- UV coverage and overlap;
- which textures drive which material inputs;
- the native scale.

The `albedo` pass shows lighting painted into the texture, and `clay` shows the geometry without
it. The tool was tried on the converted original and on the v015 blockout exported to OBJ
(`candidates/_tooltest/`); both runs were correct and approve nothing.

The owner's review against the concepts decides; the numbers can only show problems. After an
accepted candidate, cleanup follows in this order:
1. cut out any fused clothing;
2. separate the eyes and teeth;
3. add the skull ornament;
4. retopologise;
5. UVs and textures.
Rigging and Unity wait (ASSET_PIPELINE.md §10).

## Who does what

- **The agent:** prepare inputs; set up and run a service's API or a local model once approved;
  record the trial; run the evaluation; report.
- **The owner:**
  - done 2026-10-07: chose Tripo, approved the free-plan uploads, made the clean front and rear,
    ran the web studio and accepted the shape;
  - next: buy API credits for the paid regeneration (the key is already in the user environment,
    never in the repo);
  - review each candidate.
