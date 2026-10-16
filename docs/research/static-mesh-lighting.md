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

CONFIRMED quarter caps: `0x0043FF5D` initializes quarter draw flags to`0x90`;
only the open PlayerColouredPath branch clears them at`0x0043FF6B`.
`0x00440232..0x00440248` copies those flags into quarter-record`+0xC`.
Solid PlayerColouredWall quarters retain normal inversion. Floor quarters do not.

Built: `HasTop` and `QuarterKind.Top` submissions invert the normal in
`TerrainChunks.Add` before rotation. Decoded assets, positions, floors, room
quarter sets and animation normals retain their existing paths. Production
regressions and image checks are in classic-lighting-verification.md.

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
counts and capture limits live in classic-lighting-verification.md. Authored object
lights use the separate dynamic path below (Q603).

## 4. Authored object-light descriptors

CONFIRMED shipped data: the24-byte light block at object file`+0x314` uses the
existing `KwdLight` layout. Heart13 has offset(0,0,3686), radius8192, flags`0x805`,
RGB144/100/20; Arches86 has offset(0,0,15360), radius10240, flags`0x3800`,
RGB244/244/204. Big Steps88 has a zero descriptor. These are authored inputs,
not proof of runtime activation, fading, classification or final source positions.

CONFIRMED factory path: `0x0049DD60` copies the24-byte block to definition`+0xD8`
and ORs flag1 when any authored RGB byte is nonzero. `0x0049B1C0` requests the
light through bridge slot`+0xA0` with intensity180 and the object's owner. It
links the returned light to the object via light`+0x12E` and object`+0x131`.

`0x00439880` requires descriptor flag1, adds XYZ offsets directly to the supplied
position, and requests radius`((uint)radius<<1)/3` before the ordinary factory's
radius clamp. Flags`0x800/0x1000/0x2000` add256 to the respective RGB bytes;
flag`0x80` multiplies by the owner's palette RGB/255 and clamps each result to
at least32. This is not the torch room-colour +255 conversion. The helper sets
the created engine light's flag8. Assembly confirms the unsigned radius math.

CONFIRMED lifetime: `0x0049BD20` saves the linked light's old position at`+0x105`
and updates its position to the physical object position plus the same descriptor
XYZ after object animation. `0x0049B5E0` destroys it through bridge slot`+0xA8`
and clears the object's handle. `0x0043A2E0` leaves factory flags`0x28` steady:
there is no automatic colour fade or model-alpha multiplication on this path.

CONFIRMED classification: the light initializer calls`0x004468E0` with mode1,
stored at`+0xE4`. The prepared-tile pass`0x0043CC30` sends such kind2 lights to
renderer slot`+0xE8`,`0x00599C60`, including the cache-only branch without the
ordinary mesh's visibility test. This renderer prepends dynamic sources through
`0x00571AB0`; it does not use the torch's static source path. `0x00599BD0` maps
its normal flags`0x11` to`0x191` and cheap flags`0x15` to`0x95`. Linked positions
interpolate previous+(current-previous)*fraction through`0x0041C4C0`,
`0x0041C480` and`0x0044E7B0`. Tile membership follows the current position;
`0x00446C70` prepends on creation or tile change and leaves same-tile order alone.
The renderer's second prepend reverses encounter order back to insertion order.

CONFIRMED Heart placement: `0x004DB700` creates Heart13 and Arches86 at the
central tile with physical Z0. Our central Heart floor projection also returns0;
its authored lights therefore originate at heights3686/4096 and15360/4096.

Built: `ObjectDef.Light` retains the raw descriptor and its writer overlays only
those24 bytes. `WorldView.LightObjects` supplies light-bearing ordinary objects
and decorations independently of mesh visibility. `ClassicWorldLightCache`
retains dynamic sources across frames, preserves per-tile insertion/reentry order,
and removes absent objects; only prepared cells enter the native rolling window.
The normal/cheap shader classifications and existing24-slot cap remain unchanged.

Boundaries: held/carried object lights are omitted until their hand-space behavior
is traced. Generated decorations use our existing creation order and floor
projection; exact overflow ties and non-Heart physical-versus-rendered furniture
offsets still need comparison. These are not evidence for an ambient fill.
