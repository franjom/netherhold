# Art direction: the asset modernization standard

> **Preserve design. Increase fidelity.**
>
> The original Dungeon Keeper 2 assets define what things are supposed to look
> like. Modern tools define only how much detail and rendering fidelity we can
> use to express them.

For animation, the same rule in its own terms:

> **Preserve motion. Modernize representation.**

This document is the standard: what a modernized asset must be, and what it
must not become. It binds humans and AI agents equally. The technical pipeline
that produces and checks such assets is `docs/ASSET_PIPELINE.md`; the decision
and its reasons are ADR-0242. Where the two documents disagree, this one wins
on *what* an asset should look like and the pipeline wins on *how* it is made.

## 1. The goal

The goal is **faithful modernization** of DK2's assets: not reinterpretation,
redesign or photorealistic recreation. For every creature, object, room, tile,
prop, door, trap, environmental element, effect, animation and material, the
target is what Dungeon Keeper 2 might have looked like had its original artists
had modern geometry budgets, texture resolutions, materials, lighting and GPUs.

The desired result is **Dungeon Keeper 2 with modern rendering fidelity** —
not a realistic reinterpretation of Dungeon Keeper 2, and not DK2 redesigned to
contemporary fantasy taste.

## 2. One principle, two halves of the project

The remake has one source-of-truth rule, applied twice:

| | source of truth | what the remake does |
|---|---|---|
| **gameplay** | original DK2 behaviour (`DKII.EXE`, the shipped data, the running game) | C# reimplements it (`AGENTS.md`, Goal 5) |
| **graphics** | original DK2 visual design (the shipped meshes, textures and animation frames) | modern assets and Unity rendering re-express it |

The consequences are the same on both sides. Unknowns are recovered from the
original before anything is invented. A deliberate departure is documented as a
deviation, never made silently. And **a result that looks better but is not the
original's is a defect**, exactly as a simulation that plays better but does not
match DK2 is one.

## 3. The four levels of change

Every change a modern asset makes to its original belongs to exactly one level.
The level decides whether the change is allowed and how tightly the original
constrains it.

| level | what | example | rule |
|---|---|---|---|
| **A — Preserved** | directly observable in DK2 | shape, proportion, colour, marking | **cannot change** |
| **B — Reconstructed** | strongly implied by the original geometry or texture | a polygonal horn becomes a smooth horn | allowed, **constrained heavily by the source** |
| **C — Inferred detail** | not represented, because of the original's technical limits | skin pores, tiny stone cracks, cloth weave, subtle scratches | allowed, **must not change how the asset reads** |
| **D — Redesign** | alters anything at level A, or adds what the original neither shows nor implies | new armour, different horns, new ornaments, changed anatomy | **forbidden** |

- **A** covers silhouette, proportions, shape language, character and facial
  identity, clothing, armour, equipment, horns, claws and accessories, colours,
  markings, stylization and exaggeration, visual humour, the dark-fantasy
  aesthetic, and the character of each animation.
- **B** is the intent the low-poly model approximated (§6). The original fixes
  its position, size and outline; only its smoothness and resolution are new.
- **C** is detail below the original's resolution entirely. It must fit the
  material the original shows — pores on what the texture shows as skin, cracks
  on what it shows as stone — and it must vanish when the modern render is
  reduced to the original's texel density and gameplay camera distance. A detail
  that survives that reduction is not C. Scars, runes, emblems, engravings and
  tattoos are markings, never C: they are A if DK2 has them and D if it does not.
- **D** includes photorealism, realistic reinterpretation, realistic anatomy in
  place of stylized anatomy, proportions changed because modern anatomy would
  look "better", and DK2 creatures turned into generic modern fantasy
  characters. There are no exceptions and no approval that admits one.
- **A doubtful change takes the stricter level**: between A and B it is A,
  between B and C it is B, and anything that might be D is treated as D until the
  owner rules otherwise.

**Rendering and representation are not classified**: shaders, lighting,
shadows, LODs, effects, resolution, and a rig in place of vertex animation
change how an asset is drawn, not what it is. The maps they consume are
classified: a roughness value that makes metal read as metal is B, and a
scratch in a normal map is C.

## 4. Photorealism is out of scope — a hard constraint

A photorealistic Horned Reaper may be technically impressive and is still a
failure if it no longer looks and feels like DK2's Horned Reaper. The same holds
for every asset. **When realism and faithfulness conflict, faithfulness wins.**

Physically based rendering is a *rendering technique*, not an instruction to
become realistic. Metal should respond like metal, leather read as leather,
stone as stone, wood as wood, and skin as **stylized** skin — while colour,
value, shape and exaggeration stay DK2's. A correct BRDF on the wrong design is
the wrong asset.

## 5. The originals are authoritative

The shipped assets are reference material and source-of-truth data. Where they
exist, keep and analyse: mesh, vertex positions, topology, dimensions and
proportions, UVs, textures and their named variants, material assignments,
object scale and orientation, and **every frame of every animation clip**.

What the originals contain is measured, not assumed (`ASSET_PIPELINE.md` §2).
In particular **DK2 has no skeleton**: KMF creature animation is per-frame
vertex animation, so there is no original bone hierarchy, bind pose or skin
weights to preserve, and none may be invented and then described as original.
The authoritative animation data is the frame set itself.

**Missing information is recovered, not filled in.** An AI tool or a human may
not replace information with an arbitrary artistic choice when it can be read
from the original game: the files, the binary (`reverse-engineering/README.md`),
or the running install. Only what the original genuinely lacks — levels B and C
(§3) — is authored, and it is authored to fit what the original shows.

## 6. Geometry: reconstruct intent, do not subdivide

This is level B. Subdivision raises the polygon count without restoring what the
artists were approximating. The original mesh is instead a **geometric
constraint**:

- a six-sided arm becomes a smoothly modelled arm of the same thickness and length;
- a faceted horn becomes a smooth horn along the same curve;
- blocky fingers become stylized fingers in the same count, size and pose;
- angular cloth becomes folded cloth over the same volume;
- a simplified face gains geometry that carries the same caricature;
- armour edges gain bevels and thickness without changing their outline.

The question is always *"what was the original low-poly model trying to
represent?"*, never *"how would we design this character today?"*.

## 7. Stylization is a feature

DK2's oversized hands and feet, unusual anatomy, exaggerated horns, chunky
equipment, caricatured faces, strong silhouettes, stylized architecture and
exaggerated animation are **design, not technical limitation**. Do not correct
them toward realism. Modern geometry should state those shapes more clearly,
never normalise them.

## 8. Animation: preserve motion, modernize representation

The original animation is part of the game's identity, and its frames are the
source of truth for poses, timing, locomotion, idles, attacks, eating,
sleeping, working, celebrating, reactions, slap reactions, anticipation,
follow-through, deformation and each creature's mannerisms.

The **runtime representation may change**. The remake does not have to play
vertex animation; the preferred direction is a reconstructed skeletal rig whose
animation reproduces the original frames, because a rig is what Unity blends,
interpolates, retargets to LODs and extends with secondary motion. That rig
corresponds to **no original skeleton**, because there is none. It is judged on
three things only: it deforms the modern mesh correctly, it reproduces DK2's
motion closely, and it is simple enough to maintain.

Reconstruction is a **constrained** problem: the original frames are the
target, and the solver's job is to match them. **Generative animation may not
recreate or reinterpret a clip whose original frames exist.** The four levels
apply to motion as they do to shape:

- **A:** the pose at every original frame, and the frame timing;
- **B:** interpolation between original frames, cleaner deformation, fixed
  clipping or popping;
- **C:** optional secondary motion — a jiggle, a cloth sway — that does not
  change how the motion reads;
- **D:** a new or altered pose, gesture or timing. Forbidden.

The original frames are never discarded; the modern clip is a derived asset
(`ASSET_PIPELINE.md` §5).

## 9. AI assists; it does not direct

AI is an **asset modernization assistant**. It may help with reconstruction,
geometry enhancement, detail suggestions, retopology, UVs, texture
reconstruction and upscaling, PBR map generation, rig reconstruction and skin
weights, animation cleanup, LODs, collision, Blender scripting, validation,
comparison renders and Unity import.

It may not redesign. Its image and texture generation is **reference-driven**:
conditioned on the original texture and geometry, and checked against them.
Whenever an AI output conflicts with the original, the original wins, and the
output is rejected rather than argued for. Its changes are classified by §3
like anyone's, and **one level-D change rejects the whole candidate**; it is
not patched out and kept. No AI provider is architectural:
each is a replaceable stage (`ASSET_PIPELINE.md` §6).

## 10. The human role

The owner is the **art director, reviewer, approver and source-of-truth
adjudicator** — not the modeller. The pipeline is designed so that sculpting,
retopology, UV editing, weight painting, texture painting and rigging are
automated or AI-assisted wherever that is reliable, and so that the owner needs
no 3D-artist skills to judge the result.

Automation never outranks visual correctness. Where automation cannot deliver a
faithful result, the pipeline says so, and the asset needs manual 3D work
rather than a lowered bar (`ASSET_PIPELINE.md` §9 marks which steps those are).

## 11. Acceptance

Every modernized asset passes two gates, in this order:

1. **Automated validation** (`ASSET_PIPELINE.md` §7): standard comparison renders
   against the original from front, rear, both sides and both three-quarter
   views, with identical pose, scale, orientation and camera; silhouette
   overlays; for animation, synchronised side-by-side and overlay playback
   against the original frames; and a technical report. Automation can fail an
   asset. **It cannot pass one.**
2. **The art-direction test**, answered by the owner, together with the §3
   question for every visible change: which level is it, and does it keep that
   level's rule?

   > If a person familiar with Dungeon Keeper 2 sees this asset without
   > explanation, would they immediately identify it as the same DK2 asset, only
   > rendered with substantially greater fidelity?

   If the answer is no, find out why before changing anything else. A
   technically impressive asset can fail this test; a beautiful one can fail it;
   a realistic one very likely will. **Faithfulness outranks technical
   impressiveness.**

## 12. Scope limits

- **Nothing is modernized in bulk** until the three-asset proof of concept in
  `ASSET_PIPELINE.md` §10 has passed both gates and the pipeline has been
  revised on what it taught.
- **No new visual style is selected**, and no replacement DK2 artwork is
  generated outside that proof of concept.
- **The classic look survives.** Goal 11's classic presentation is the converted
  originals, and modernization adds a tier beside it rather than replacing it.
- **A modernized asset is derived from EA's art** and is treated as
  non-redistributable, exactly like converted assets (ADR-0242). It is never
  committed. Goal 4's independent replacement assets are a separate matter, and
  this standard does not describe them.

## 13. The interface

The HUD follows the same four levels (ADR-0497). There is one HUD and no
pixel-faithful classic mode: DK2's design, drawn at the display's resolution.

| level | in the interface |
|---|---|
| **A** | which element sits where and on which screen edge or corner; colours, frame and bevel shapes, icon content; DK2's letterforms; the minimap palette (`MapColours`) and colour classes ([0x00451C90](research/functions/00451C90.md)); what each element shows and when |
| **B** | the same shapes at display resolution: sharp edges, smooth curves, an anti-aliased minimap rim |
| **C** | surface detail below the original's pixel size (metal grain, stone wear), which must vanish at the original's size |
| **D** | new layout, new icons, a flat or "modern" restyle, colours DK2 does not use |

- **Nothing is sized in device pixels.** Layout is in reference units against
  a 768-unit-high reference (DK2's 1024×768 at 4:3), scales with screen height,
  and has a player UI-scale setting. Each element keeps DK2's anchor: the top
  bar on the top edge, the minimap block bottom-left, the portrait bottom-right.
  On wider screens the bars and panel fill extend with their tiled fill and the
  extra width goes to the 3D view. Where 4:3 gives no answer (32:9), the choice
  is a documented deviation per element.
- **The original bitmap is the reference.** It is read from the install
  (ADR-0408). A modern version is a replacement-tier asset (ADR-0242), approved
  against its hash. Reduced to the original's pixel size it must match the
  original; a difference that survives the reduction is level D. Until a modern
  version exists, the original is drawn with filtered upscaling, never
  nearest-neighbour at a non-integer scale.
- **Text keeps DK2's letterforms.** Glyphs come from the install's `.bf4` fonts
  at the largest size shipped, drawn as a distance field, or as the matching
  `.bf4` size where the scale makes a bitmap exact. DK2's per-width font choice
  (`BitmapText`) still picks the font for a style.
- **The minimap is drawn on the GPU** from a per-tile class grid that
  `DK2.Presentation` updates per changed tile, as `0x00508CA0` → `0x00451C60`
  does. A shader does the palette lookup, rotation, round mask and rock
  background. Markers are shapes in reference units, in DK2's colours and blink;
  what they are, and the map's zoom, click and camera-box behaviour, are read
  from `0x0041CC50` before they are built.
- **Each step is judged** beside DK2 at the same place, at 1024×768 and at
  2560×1440 or above: at 1024×768 the two read as the same interface; at the
  large size ours is sharp and nothing is smaller on screen than at 1024×768.
- **Cursor, front end, status flowers:** their 2D icons, sprites and text follow
  this section. The Hand of Evil's form is ADR-0242's and only its on-screen
  size follows this section (that it is a 3D model is not yet checked). A
  deliberate deviation (owner, 2026-10-24): the cursor's claw or finger tip, not
  DK2's hotspot, goes on the pointer
  ([hand-cursor-rendering.md](research/hand-cursor-rendering.md)). The
  front end is a 3D level in DK2 (`FrontEnd3DLevel`); its scene goes with the
  classic-look work. The flowers are world-space sprites whose size follows
  camera distance, a rule still to be read from the binary.
