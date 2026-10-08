# Imp footwear: inspection and plan

Status: **footwear v001 accepted by the owner** (2026-10-08). The owner's decisions
of 2026-10-07 and the result are at the end. The approved light mesh (`retopo/v001`) is unchanged.
Evidence is in `build/modern/creature/imp/footwear/inspection/`, gitignored; its comparison sheet
is `feet_sheet.png`.

## What the original Imp wears

| source | what it shows at the feet |
|---|---|
| **In-game mesh and textures** (`Meshes.WAD`, `impfrnt`, `impback`, 128x128) | **Bare feet.** The feet's triangles map to texels painted as skin, with toes on `impfrnt` and the heel on `impback`. The ankle band is skin too, with no cloth, strap or sole colour. Both legs share these texels. |
| **DK2 portrait** (`reference/dk2_portrait.png`) | Footwear, at very low resolution. A wide band at each ankle with a light upper rim; on the left leg it reads as wound cloth in grey-blue and white. Below it, a brown band across the instep. Toes bare. A dark band under the feet may be a sole or a ground shadow: it cannot be resolved. |
| **Creature panel image** (`EngineTextures.WAD` `imp-bg.png`) | By eye the same render as the portrait (pose, background, sash, chest skull), cropped at the knees, so the portrait is STRONGLY INFERRED to be DK2 art; no pixel comparison was made. It shows no feet. |
| **Approved concepts** (ADR-0398) | Two layers. A grey cloth ankle wrap with a rolled rim and a metal stud on the outer side. Under it, a riveted brown leather strap: over the instep, under the arch, up both sides of the heel and into the wrap. Toes and heel bare, no sole. The four views show the same footwear, though they disagree on other details (manifest `inconsistencies`). |

The in-game model simplified the feet away. The only DK2 evidence for footwear is the portrait: a
cloth wrap and a brown band across the instep. The concepts were generated from that portrait, so
they are its interpretation, not a second source; they supply the details the portrait is too
small to show. Nothing shows a shoe, a sandal or a sole.

## What the current mesh has, and lacks

The current mesh is `t1-tripo-web` and its light version `retopo/v001`.
- **Has:** a grey cloth wrap at each ankle (about 10–21% of the height), fused into the body. The
  feet are bare and oversized (about 0.30 of the 0.98 height, four toes each).
- **Lacks:** the leather foot strap and the wrap's stud. The clean base image dropped them: its
  edit kept the "loincloth and ankle wraps" only.
- **Different:** the wrap sags loosely, where the concepts' wrap is a firm band with a rolled rim.
  This is a minor point, kept as is unless the owner asks.

## Reuse

- **Original DK2 assets:** no footwear geometry or texture to reuse; the in-game feet are bare. The
  original clips (`Imp_Run`, `Imp_Dig`, `Imp_Idle1`) give the ankle's range of motion for the
  clipping test below.
- **Modern mesh:** the body, feet, toes and skin as they are. The cloth wrap's shape and its baked
  cloth texture.
- **Materials:** one leather material, shared later with the concepts' belt and braces. One metal
  material for the stud, shared later with the buckle and pick.

## Proposed changes

1. **Leather foot strap, new and separate:** `IMP_foot_strap_L` and `IMP_foot_strap_R`, mirrored.
   - Built as a ribbon in Blender, fitted to the foot surface.
   - Path: from the wrap's lower edge on the inner ankle, over the instep, under the arch (clear of
     the ground: the arch is the foot's raised part), and up the outer side to the wrap.
   - About 4% of the height wide and about 0.3% thick, with a slightly rolled edge.
   - About 250–400 triangles each; one UV strip on a shared leather texture.
   - Toes, heel and sole stay bare.
2. **Stud:** one metal stud on the outer side of each wrap, about 40 triangles each, or a
   normal-map detail on the wrap if it costs too much at gameplay size.
3. **Ankle wrap, separated:** cut out from the high mesh by its cloth colour, rebuilt as a light
   tube of about 300 triangles with its detail baked, as `IMP_ankle_wrap_L` and `IMP_ankle_wrap_R`.
   The body under it is pulled in by the wrap's thickness so the two never show through each other.
   Optional: the wrap can stay fused, since it is tight and moves with the skin.
4. **No change** to anything above the wrap, to the toes, the skin texture or the proportions.

## Implementation

1. **Script:** `tools/blender/imp_footwear.py` reads `retopo/v001/retopo.blend` and writes a new
   folder `build/modern/creature/imp/footwear/v001/`. The approved light mesh is never written to.
2. **Build:** the strap path is defined by landmarks measured on each foot (inner and outer ankle,
   instep, arch, heel sides), mirrored from the right foot to the left. It is swept, fitted to the
   surface with a small offset, and given thickness. UVs are unwrapped along the strip.
3. **Texture:** leather albedo, normal and roughness at 512x512, shared by both straps. The colour
   is matched to the concepts' leather.
4. **Wrap separation:** by cloth colour, then a remesh to a tube, a bake, and pulling in the skin
   underneath.
5. **Review renders:** front, side, rear, three-quarter and gameplay size, side by side with the
   concept feet. Clay and edge views of the new parts.
6. **Motion check:** the feet posed at the ankle angles of the original clips' extremes, checking
   for the strap cutting into the skin or lifting off. This needs a temporary bend of the foot. It
   is not the production rig, which stays on hold until the owner approves it.
7. **Report:** triangle counts per part, the materials and the review sheets. The `parts` section
   of `art/manifests/creature/imp.yaml` is updated after approval.

**Cost:** under 1,500 extra triangles, one 512 texture set and one extra material per Imp, or none
if the leather is packed into the body's texture.

## Owner decisions (2026-10-07)

1. Wrap plus leather strap, as in the portrait and concepts.
2. The ankle wraps stay part of the body.
3. Studs (and the strap's rivets) are painted detail, not geometry.
4. The leather is packed into the body's texture: one material.

## Result: footwear v001 (2026-10-08)

`tools/blender/imp_footwear.py`, reading `retopo/v001/retopo.blend`; output in
`build/modern/creature/imp/footwear/v001/`:
- `imp_footwear.glb` (body plus both straps, one material);
- `footwear.blend`;
- `report.json`;
- `sheets/feet.png`, `sheets/bend.png`, `sheets/body.png`.

**What was built:**
- **Strap:** `IMP_foot_strap_L` and `_R` are separate objects, 448 triangles each. Each is a closed
  leather band whose upper rim starts at the wrap's front lower edge, found from the wrap's cloth
  colour on the dense mesh. It crosses the instep, passes under the arch and is lifted 2.5 mm off
  both the light and the dense surfaces.
- **Painted detail:** the wrap stud and two rivets per strap are temporary metal domes, baked into
  the colour, normal and metal maps, then discarded.
- **Leather:** the colour comes from the concepts' strap pixels, with the grain laid along the
  strap's own UVs.
- **Texture set:** the body and straps share one re-packed set (2048, baked at 4096). The body's
  geometry is the approved light mesh, unchanged.

**Bend test:** each foot is pitched about its ankle by a temporary falloff, not a rig, at −35, −20,
0, +20 and +35 degrees. No strap vertex enters the skin at any pitch. With the toes down, up to 8 of
448 vertices sit more than 8 mm off the skin.

The original clips could not set the range: their files differ in vertex layout, and one foot is
raised in some first frames, so the measurement was dropped. ±35 degrees is beyond what a walk or
run needs.

**Not done or still open:**
- The strap does not climb the sides of the heel into the wrap, as the concepts' rear view shows.
- At gameplay size the strap is a few brown pixels.
- The studs read dark in the review lighting.
- Real deformation is untested until the rig exists.
