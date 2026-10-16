# Static mesh lighting inputs

Retail DKII.EXE v1.70, primary Ghidra reads and shipped KMF data, 2026-10-16.
Point-light arithmetic remains in [classic-lighting.md](classic-lighting.md).

## 1. Draw flags can invert normals

CONFIRMED: `0x0057AD20` tests its final flags byte for`0x80`. When clear,
it transforms the source normal through the piece matrix. When set, it negates
all three source-normal components before the same transform. Positions still
follow their ordinary transform and lattice path. The decisive assembly is
`0x0057AEA0..0x0057AF62`; an independent reader checked the three negations.
`0x00585F90` forwards its draw flags into that parameter.

CONFIRMED: `0x0057D5B0` copies the three static KMF normal floats into the
runtime vertex without arithmetic; this is not cancelling a loader inversion.
The ordinary top arm of`0x0043E860` puts terrain art2 into a whole-piece record
with flags`0x90`. `0x0043C310` forwards that record's flags through whole-piece
sink`0x0059A050`, which preserves them into`0x00585F90`. Ordinary rock tops
therefore use inverted lighting normals. This does not establish every room,
static object or animated draw's flags.

CONFIRMED shipped data: `ROCK TOP.kmf` has positions Z=-2 and normals(0,0,-1).
Both map to our render frame with the same orthogonal axis conversion, producing
height2 and an upward authored normal. The native flag makes that lighting
normal point downward instead. ClaimScan's light is below that top, so this
changes the sign of incidence; gamma or a normal-length adjustment cannot do so.

Built: ordinary `HasTop` submissions invert the normal in `TerrainChunks.Add`
before rotation. Decoded assets, positions, other pieces and animation normals
retain their existing paths. The production-mesh regression fails with the
correction disabled and passes for1484 top-only Rock vertices on Level1.

## 2. Texture stages and remaining limits

CONFIRMED: the ordinary stage0 colour operation set by`0x00589250` is4,
with texture and diffuse arguments. Direct3D defines that as ordinary modulation,
not an added ambient term. Special state bits`0x8000`/`0x10000` configure
bump-environment stages; they are not evidence of a default scene fill.
[Microsoft's enum definitions](https://learn.microsoft.com/en-us/windows/win32/direct3d9/d3dtextureop)
identify the numeric API operations; DK2's state choices come from the binary.

The shipped Rock Top and all five Heart Arches materials have no environment-map
flag. Their zero brightness is recorded in kmf-materials.md section3. The captured
settings`+0x5C` option gates a material-state`0x800` extra-pass arm in static and
animated preparation; its full public setting meaning is not established here.
No arbitrary shader brightness or gamma adjustment follows from this trace.

## 3. Ordinary objects use instance lighting

CONFIRMED: type5 objects pass through`0x0059CCF0`/`0x00580480` into draw vtable
`0x0066FD1C`. Preparation slot+8,`0x00580EC0`, selects flag`0x100` sources into
draw`+0x60`, and separately evaluates flag4 sources with`0x0057C080` at the draw
translation. This is not terrain's`0x60`/`0xA0`/1 per-vertex classification.

The ordinary primitive dispatcher`0x00582CE0` reaches`0x00581BE0`. Under its
lighting-context bit`0x40`, that consumer transforms the model's centre with the
draw matrix, adds draw translation, collects the selected sources through
`0x0057BD70`, and evaluates them through`0x0057C190`. It uses the same sampled
incidence helper as animated objects. Static model centre is runtime`+0x24`;
the file Origin is already retained by the KMF reader.

Built: ordinary objects use the instance shader marker regardless of static or
animated asset format. Torch mounts explicitly keep terrain-piece lighting:
their native path is`0x00599960` through`0x00585F90`. `ClassicMeshes` caches by
asset, frame choice and lighting mode, preserving distinct shared meshes for
the two draw roles. No per-renderer material block or frame allocation is added.

Production role regressions fail before this correction and pass on Levels1/8;
counts and capture limits live in classic-lighting-verification.md. Arches remain
dark. Authored object lights are not published; their native path needs tracing (Q603).

## 4. Authored object-light descriptors

CONFIRMED shipped data: the24-byte light block at object file`+0x314` uses the
existing `KwdLight` layout. Heart13 has offset(0,0,3686), radius8192, flags`0x805`,
RGB144/100/20; Arches86 has offset(0,0,15360), radius10240, flags`0x3800`,
RGB244/244/204. Big Steps88 has a zero descriptor. These are authored inputs,
not proof of runtime activation, fading, classification or final source positions.

`KwdObjects` skips this descriptor and `ClassicWorldLightCache` publishes terrain
and torches only. Trace native loading, source creation and its tile-list lifetime
before adding these objects' lights; do not replace the missing path with a fill.
