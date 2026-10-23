# KMF materials: flags, blend, light

How DK2 turns a KMF `MAT2` record into a drawn material, and what the classic look
draws for each. Read from DKII.EXE on 2026-10-10 (claude). Confidence labels are per
claim. Related: [tag-highlight.md](tag-highlight.md) §6 (the `S_`/`A_`/`AA_` materials
built from a texture name, which share the constructor and applier below).

## 1. The loader keeps six bits

The chunk tags `KMSH`, `MATL`, `MAT2`, `SPRS` sit as dwords in a table at
0x006BE130..; `MATL` (0x006BE154) and `MAT2` (0x006BE14C) are each referenced once, by
[0x0057D390](functions/0057D390.md), the one MATL reader. CONFIRMED (byte scan of every
block).

Per material it reads, in file order: the name, the texture count and names, the
flags dword, brightness, shininess, the environment texture name. The same order
`KmfReader` reads. CONFIRMED.

It translates the file flags into engine material flags:

| File bit | Engine flag | Names that carry it in the shipped data |
|---|---|---|
| 0x01 | 0x001 | `#mask#`, the gold spill cards |
| 0x04 | 0x030 | `#ADD#` |
| 0x08 | 0x002 | `#TRANS25#` (with 0x02) |
| 0x10 | 0x004 | — (only with 0x01 and 0x02) |
| 0x20 | 0x008 | — (none ships) |
| 0x40 | 0x200 | `frontenv…`, environment-mapped |
| 0x02, 0x80, 0x100 | dropped | "double sided", `…Illum…`, `Invisible…` |

CONFIRMED. The engine flags go to the material descriptor builder
[0x0057C340](functions/0057C340.md) ORed with 0x4000 (the texture-list path), with
brightness stored three times as the colour (+0x50, +0x54, +0x58), shininess at +0x4C
and the environment texture name at +0x10. CONFIRMED.

So bits 0x02, 0x80 and 0x100 have no runtime effect through this loader. The
"Illum" materials are lit by their **brightness**, which every material carries
(§3). CONFIRMED for this loader; no other MAT2 reader exists (one reference).

## 2. Engine flag to render state

The material constructor [0x0057C420](functions/0057C420.md) starts the state word at
0x1C0 (0x5C0 without engine flag 0x100) and applies, in this order:

| Engine flag | State change | Meaning (applier [0x00589250](functions/00589250.md)) |
|---|---|---|
| 0x010 | `& ~0xC0 \| 0x201`, colour forced to 255,255,255 | ONE/ONE additive, no z-write, deferred |
| 0x001 | `& ~0x80 \| 0x222` | SRCALPHA/INVSRCALPHA, no z-write, deferred |
| 0x400 | `& ~0x80 \| 0x1200` | ZERO/INVSRCCOLOR (not reachable from a KMF) |
| 0x002, 0x004, 0x008 | `& ~0x80 \| 0x220` | SRCALPHA/INVSRCALPHA, no z-write, deferred |
| 0x040 | `& ~0x40` | state bit 0x40, meaning UNREAD |
| 0x200 | `\| 0x800`, loads the environment texture into material +0x8 | a separate additive pass, §8 |

CONFIRMED for the state words. In the applier, bit 0x1 sets SRCBLEND ONE, DESTBLEND
ONE; bit 0x20 sets SRCALPHA/INVSRCALPHA and is issued after bit 0x1, so a state with
both blends by alpha; 0x80 is ZWRITEENABLE, 0x100 ZENABLE. CONFIRMED. State bit 0x2
has no reader in the applier; its reader is UNREAD.

State bit 0x200 is set by every blended state. The batch drawer
[0x0058A150](functions/0058A150.md) and the triangle sink
[0x0058B9D0](functions/0058B9D0.md) test 0x200 on the current batch's word at +0x10 and,
when set, put the triangle into a 1024-bucket depth list drawn back to front by
[0x00588D00](functions/00588D00.md) instead of drawing it at once. LIKELY: that the
+0x10 word they test is the blended state (in the constructor's own object +0x10 is
the engine flags, so the batch object is a different one; its builder is unread).

Engine flag 0x20 (half of the file's ADD bit) has no test in the constructor. UNREAD
elsewhere.

So for a KMF material:

- file 0x01, 0x08, 0x10 or 0x20 → **alpha-blended** by the texture's alpha, no depth
  write, depth-sorted;
- file 0x04 alone → **additive** ONE/ONE, colour white (full light), no depth write;
- otherwise **opaque**. CONFIRMED.

## 3. Light: brightness is added, specular is off

The constructor stores brightness × 255 as the material colour (+0x21..+0x29), and the
vertex-colour loops add that colour to each vertex's light before the texture is
modulated by it ([tag-highlight.md](tag-highlight.md) §6). So brightness is
self-illumination: the gold spill cards (0.6) and chest gold (0.6) glow. An additive
material's colour is forced to white, so it draws at full light. CONFIRMED.

CONFIRMED shipped data (2026-10-16): `ROCK TOP.kmf` and all five materials in
`DHeart Arches.kmf` have brightness zero. Their black appearance with no supplied
light cannot be corrected by restoring omitted authored self-illumination.
The Rock Top's nine geometry vertices all have native Z=-2 (Unity height2), before terrain deformation.

The device set-up [0x005899F0](functions/005899F0.md) sets SPECULARENABLE to 0, CULLMODE
to NONE, ZFUNC to LESSEQUAL, Gouraud shading and dithering. No call sets
ALPHATESTENABLE, ALPHAREF, ALPHAFUNC or COLORKEYENABLE: a scan of every indirect call
through the device's SetRenderState slot (+0x58) found the state ids 0x4, 0x7, 0x8,
0xE, 0x13, 0x14, 0x16, 0x17, 0x1A, 0x1B, 0x1D, 0x1F and two passed in registers.
LIKELY: DK2 has **no alpha test**; "mask" materials are alpha-blended.

## 4. Culling is in software, for every material

CULLMODE NONE leaves culling to the engine: the triangle sink 0x0058B9D0 (installed in
0x0077F4E4 at 0x0058ABA4) drops a triangle whose screen-space cross product is
negative, with no material test. The loader drops the file's "double sided" bit
(§1). So every KMF face is one-sided in DK2. CONFIRMED for this sink; the clipping sink
[0x0058B940](functions/0058B940.md) defers clipped triangles and hands the rest to it.

## 5. Texture alpha

`EngineTextureArchive` already decodes each entry's alpha channel when the entry has
one (`HasAlpha`): `Gold_Spill_Small` and `Gold_Spill` have alpha. The texture loader
in the constructor (0x00590B40) takes a mode of 1 (3 with engine flag 0x1000, from
the tag materials only) and does not depend on the blend. CONFIRMED.

## 6. What the classic look draws

`KmfMaterial.Blend` and `SelfIllumination` (src/DK2.Art/KmfModel.cs) carry §2 and §3;
`RenderMesh.Blends` gives one blend per triangle list. `ClassicMeshes` makes one Unity
material per (texture, blend, self-illumination):

- opaque: URP Lit, smoothness 0, specular highlights and environment reflections off;
- alpha-blended: the same, transparent, SRCALPHA/INVSRCALPHA, no depth write, queue 3000;
- additive: URP Unlit, white, ONE/ONE, no depth write, queue 3000;
- brightness above 0: emission = texture × brightness.

A material with several textures draws the first, or the one its art's texture phase
selects (the Heart's decay; a hovered gold object's `_HIGHLIGHT`,
[gold-hover-and-sparkle.md](gold-hover-and-sparkle.md) §1).

Back faces stay culled (§4). The environment pass (file 0x40) is drawn as §8 says. Not
drawn: the UNREAD state bits 0x2 and 0x40. Blended terrain faces now sort across chunks and
materials before each camera, batching only adjacent equal materials into explicitly ordered
renderers. Standalone object meshes and liquids still use Unity's object sort.
The terrain runs use sortingOrder 1..N, so they follow those other blended renderers
(at order 0); transparent faces across the two streams do not interleave by depth.

Shipped flags across Meshes.WAD (`KmfMaterialTests.ShippedFlagsUseOnlyTheSurveyedBits`):
0x0 3957, 0x40 1031, 0x42 600, 0x2 381, 0x100 198, 0x3 117, 0x80 78, 0x1 63, 0x4 20,
0x81 16, 0x6 11, 0x83 6, 0xC0 5, 0x82 4, 0x13 3, 0xA 1, 0x43 1.

The Casino's Alcove Barrels (object 68, `Bar_Barrel2`) have flags 0 and brightness 0:
opaque and unlit by themselves. Their bright patches were most likely URP Lit's default
specular (smoothness 0.5), which DK2 never draws (§3). STRONGLY INFERRED; check on screen.

## 7. Portal crystal occlusion — CONFIRMED

The Level8 Portal uses static `PORTAL0`–`PORTAL8` meshes. Its four crystal primitives
use `Portal_Gem5`, file flags 0x81 or 0x83, brightness 0.4; the chasm uses
`Portal_Chasm`, flags 0x81, brightness 0.4. Neither has an environment-map stage.
The gem texture is translucent (most texels near alpha 221/255). The brown chasm
and black centre descend below the floor in the authored positions.

A software render of those shipped positions, UVs and textures, with one camera
and unchanged lighting, reproduced the split blue blade appearance when the
crystal material's triangles preceded the chasm's. Sorting the same transparent
faces by depth restored four continuous crystal pillars. The Level8 lattice's
largest displacement was 0.219625 tile; the distortion also occurs without it.
This confirms a submission-order defect, not corrupt mesh positions.

The sink 0x0058B9D0 forms its bucket key from the sum of the three transformed
vertex depths; 0x00588D00 traverses those buckets in decreasing order and flushes
when state or textures change. Thus sorting precedes material batching. The
adapter uses continuous camera-facing centroid depth with stable equal-depth ties;
DK2's bucket quantisation and within-bucket tie order are not reproduced.
`TriangleDepthOrderTests` covers material interleaving, camera reversal, centroid
versus maximum depth, stable ties and the shipped Portal's complete blended stream.

## 8. The environment pass (2026-10-23 claude, Q622/Q623)

CONFIRMED (binary): a material with state bit 0x800 is drawn **twice**. The static mesh
queue (0x0058178E-0x00581840, inside a function Ghidra has not defined) and the animated
one ([0x00584900](functions/00584900.md), test at 0x005851E0) add a second batch entry
for the same triangles when all hold: the material state has 0x800; the item-adjusted
state has none of bits 0x21 (opaque), unless the item's flag bit 0x400 is set; the item's
flag bit 0x200 is clear; global 0x00764BE4 is non-zero. The item flags are read from
`+0x50` in the static queue (0x0058158B for 0x400, 0x00581597 for 0x200) and from
`+0x4C` in the animated one (0x00584FD1, 0x00584FDD). The entry carries the material's
environment texture (+0x8, §2), render state **0x741** (ONE/ONE additive, z-test on,
no z-write, depth-sorted) and sub-mesh id + 1000, which the dispatch (0x00582CE0,
0x005848B0) sends to the environment emitters 0x00582290 (static) and 0x00583DC0
(animated). It is one texture in one pass, not a second texture stage.

- **Coordinates** (0x00582503-0x00582769, CONFIRMED arithmetic): E = vertex minus
  camera (0x00760AB8), rotated by the view matrix at 0x00760AC4 and normalised; N =
  the vertex normal turned by the object's own matrix, times 0.5 (0x0066FC20);
  `u = Nz·Ex − Nx·Ez + 0.5`, `v = Nz·Ey − Ny·Ez + 0.5`. No clock: the shine moves
  only with the camera and the object. LIKELY: E is camera space and N world space.
  Unconfirmed (LIKELY at most): the camera-space axis convention and with it the sign of
  v; the matrix at 0x00760AC4 was not traced to its builder. The coordinates can leave
  0..1; DK2's texture address mode for this pass is unread (ours clamps).
- **Colour**: the item's base colour (+0x3C..+0x44) plus its point lights
  (0x0057C190; the light collection 0x00582140/0x0057BD70 is skipped when item `+0x50`
  bit 0x4 is set, 0x0058237B), times material +0x1D, the descriptor's **shininess**. Brightness
  plays no part. That the texture is modulated by it is LIKELY (stage set-up UNREAD).
- **Option**: global 0x00764BE4 is the EnvironmentMapping setting (settings +0x5C,
  setter [0x00566420](functions/00566420.md), through engine slot +0x70 = 0x0059C110
  -> 0x005747B0, from renderer init 0x005983C0 and the options toggle 0x0042A0F0).
  Default on, LIKELY: 0x00564B00 writes 1 when the stored value is missing, and
  0x00574310 writes 1 to 0x00764BE4 directly (0x005744DF).

Shipped examples: the Portal Gem (`Gem.kmf`, object 28) is `128gem1`, a dull
grey-violet noise, flags 0x42, shininess 1, environment `EnvmapK`; its look is the
pass. Its `Rays_Of_Light` (`rayolight`, flags 0x6) are additive and get none. The Lord
of the Land's armour, living and dead (`LOL-DiePose`), is `frontenv`/`KnightBack`,
flags 0x40/0x42, shininess 0.2, `DefaultEnvMap`; his sword 0.5.

**Built** (adapter): `KmfMaterial.HasEnvironmentPass` and `EnvironmentPassLight`
(DK2.Art) carry the gate and the scale; the classic vertex-lit shader
(`ClassicVertexLit.shader`, keyword `DK2_ENVIRONMENT_MAP`) computes the coordinates
inline, and `EnvironmentMapCoordinates` (DK2.Presentation) is only the tested C#
reference of that formula; nothing calls it at run time. `ClassicMeshes` appends each such material's triangles as an
extra submesh drawn ONE/ONE with the environment texture, lit by the shader's own light
sum times shininess. Approximated: our light sum includes the owner-tuned Unity fill
(ADR-0522); DK2's camera space is assumed to be Direct3D's (GUESS); animated meshes keep
their first frame's normals; terrain pieces and effect frames get no pass; a fading
object drops its pass, as DK2's blended gate does; the option is always on. Without the
classic vertex-lit shader no pass is drawn.
