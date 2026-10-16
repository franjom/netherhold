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

Implementation gap: `TerrainChunks.Add` currently rotates the authored normal
without this per-draw inversion. Preserve decoded assets and animated normals;
carry the confirmed flag at the piece boundary, then verify the actual chunked
mesh and native-lighting capture. Heart-arch flags require a separate trace.

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
No shader brightness, gamma or normal change has been made from this trace.
