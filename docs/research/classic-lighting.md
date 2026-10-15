# Classic vertex lighting

Source: retail v1.70 DKII.EXE, Ghidra whole reads, 2026-10-14/15.
Existing inputs: kmf-materials.md, tag-highlight.md, wall-pieces.md and wall-torches.md.

## 1. Point-light contribution

STRONGLY INFERRED: 0x0057BF00 accumulates point lights. It computes squared
distance from each source to the vertex. Outside the light's squared radius
it adds nothing. Inside, it multiplies the light-direction/vertex-normal dot
product by a light-specific coefficient; negative-facing contributions are skipped.
It combines that factor with a lookup-table value, remaining
squared radius and another light coefficient. The resulting contribution
factor is capped at one, then the light's RGB is added to the running colour.
This caps each contribution without clamping accumulated RGB.

The vertex loop 0x0057B6D0 initializes colour from vertex light, material
brightness, instance tint and global ambient, then calls this helper with
the vertex position and normal before handing colour/UV to a render sink.
The original sum remains documented in tag-highlight.md section6.

Coefficient/lookup reads are resolved in section5, vertex RGB packing in section6.
Downstream framebuffer transfer, native-versus-URP response and
matched-frame brightness remain open.

## 2. Terrain instance colour

CONFIRMED: the whole-body read of 0x004F66E0 copies terrain RGB bytes at file
0x1DB..0x1DD through intermediate +0x293..+0x295 into runtime +0x1AF/+0x1B1/+0x1B3.
Ambient RGB bytes at file 0x201..0x203 pass through +0x2B9..+0x2BB into
runtime +0x1B9/+0x1BB/+0x1BD. Flags 19..21 add 255 to the corresponding terrain
channel; flags 25..27 do the same for ambient channels. These are added-light
values, not capped byte colours. The intermediate record is file-shifted +0xB8.

The loader checks individual field-present words before copying each byte;
the channel boost follows that check. Our complete-record reader exposes the
authored bytes and computes the boosted value separately; overlay writing
preserves those bytes without baking flags into them.

CONFIRMED (cached binary instructions, 2026-10-15): piece builders copy the
low byte of each runtime ambient word into the piece RGB bytes. A boosted265
therefore becomes9, not a saturated255 or an HDR265. This narrowing belongs to
piece tint only; terrain point-light RGB retains its word channels.
`ClassicTerrainTint.PieceColour` reproduces it at material publication.
Normal and DoubleQuad room quarters receive ambient tint; Quad quarters do
so only for Wooden Bridge over lava (0x00443979..0x00443996,
0x00443A66..0x00443AC8, repeated for every quadrant). Whole room pieces and
general room walls retain their documented tint sources. Plain terrain pieces
also receive their ambient tint. `TerrainChunks` publishes these through shared
texture-weighted emission variants, with tags replacing instance tint.
Additive materials already supply white diffuse, so nonnegative additional tint
cannot change their saturated native RGB; their unlit variants remain shared.
Special gate wall tint and final colour transfer remain outside this application.

## 3. Local light lists

STRONGLY INFERRED (whole reads, 2026-10-14): 0x0057BCB0 copies selected source
lights into ten-dword local entries: position, squared radius, attenuation
coefficient, normal coefficient and RGB. Selection uses successive bits of
the supplied mask. 0x0057BD70 instead samples squared distance at a supplied
instance position, keeps strictly positive attenuation inside the source
radius, and transforms the unnormalized source-minus-instance direction
through 0x00594E70. It stores that direction, attenuation, source intensity and
RGB separately; it receives no normal and applies no normal dot product.
The transform performs row dot products without translation or normalization.
Squared distance adds `(dx²+dy²)+dz²`; sampled attenuation multiplies
`(radius²-d²) * L[n]`, then stored reciprocal radius², using section5's bins.
Its callers include animated mesh-part draw
`0x005836A0`, whose context flag0x40 enables this collection with draw mask
`+0x6C` (the animated preparation's0x100 sources). It copies the draw's3x3
matrix verbatim through `0x00582140`, without inverse-scale adjustment.
Sampling uses draw translation plus the model bounds centre transformed as a
row vector. Projection callbacks0x0058ACB0/0x0058AD10 transform into temporary
stack storage and leave the evaluated local vertex untouched. The incidence
displacement subtracts the transformed centre from that local vertex; it is
not a world-space vertex minus the sample position.
Animation loader0x0057DAD0 and constructor0x0057E2A0 copy the file Origin to
model+0x10/+0x14/+0x18 and BoundsScale to+0x0C. These match the decoded frame
metadata; geometryScale is a separate subsequent float.

CONFIRMED (consumer `0x0057C190`, assembly and whole reads, 2026-10-15):
let `d = transformedToLight - vertexDisplacement`. It computes
`incidence = ((d.x*n.x + d.z*n.z) + d.y*n.y) * intensity`, skips a negative
sign bit (including negative zero), then multiplies by the sampled attenuation.
Each factor caps at1 before RGB addition; the sum remains uncapped. No
direction or normal normalization occurs here. Positive incidence with bin0
infinity reaches full RGB; zero incidence times infinity raises x87 invalid,
with the same unresolved exception-mask boundary as section5.
`ClassicInstancePointLight` implements the sampled reference and its singular boundary.
Animated preparation sends flag4 lights to0x0057C080 at draw translation,
adding uniform RGB without a normal term. Its
factor is `L[n]*(radius²-d²)*intensity/radius²`, capped at1 per source. Terrain loops
`0x0057B6D0/0x0057B0E0` copy sources through `0x0057BCB0` using instance
mask `+0x2C`. Preparation0x0057AFF0 selects all-bits0x60 sources into+0x24,
precomputes their vertex light, and stores all-bits0xA0 selection at+0x28.
Update0x0057AC10 selects all-bits0x01 sources and ORs+0x28 into+0x2C.
Thus static drawing combines cached0x60 light with0xA0/0x01 light; animated
0x100/0x04 selection is a separate path. Selection-cache invalidation and exact
floating-point addition order remain requirements for an equivalent publisher.

CONFIRMED: initializer 0x0043A1E0 stores both current and base ushort RGB,
uint radius and byte intensity separately. Radius above 10111 becomes 10112.
The conversion to renderer coefficients is resolved in section5. These field
widths alone do not establish a Unity falloff mapping.

## 4. Startup and current URP boundaries

CONFIRMED (0x00447D50 whole read, 2026-10-14): startup calls bridge slot +0xA4
for slots +0x33E, +0x35D and +0x35B with RGB/intensity respectively
(511,511,511)/255, (255,255,255)/128 and (500,500,500)/128.
The first two use position (2048,2048,8704); the third uses (2048,2048,4608).
Slot +0x3BC uses that same factory with (255,255,255)/128 and the first position.
The first two also receive post-creation flags/animation setup. Followup reads
on 2026-10-15 distinguish these handles from global ambient/fill:

- `+0x33E`: presentation update moves it to an interaction position, sets RGB
  to 511 for target tile flag `0x20` and otherwise zero. Selection state gates
  its mesh visibility. This is not an unconditional world fill.
- `+0x35D`: the per-tile render pass reuses it for the selection rectangle's
  boundary tiles, setting edge bits and moving it to each boundary tile centre.
- `+0x35B`: reused per-tile terrain point light; section9 owns its inputs and
  selection boundaries.
- `+0x3BC`: the per-tile pass reuses its animated mesh at a visible torch's
  position with height offset `0x4B0`, not as an ambient source.

The tile-light consumer and remaining selection boundary are in section9;
these handles do not establish replacement Unity directional lighting.

The tracked DK2 renderer's mode 2 is Forward+ in the installed URP source
(`UniversalRenderer.cs`, package 8457e85b8184), not ordinary Forward. Its
serialized four-lights-per-object setting therefore does not establish a
four-light cause for reported camera-dependent torch disappearance. Confirm
the runtime renderer and capture a fixed-world camera comparison before tuning.

## 5. Point-light inputs and reference response

STRONGLY INFERRED (whole reads and table initializer assembly, 2026-10-15):
0x00599960 and 0x00599C60 convert authored RGB by255, byte intensity by128,
and radius/position by4096. Engine Z is negated. The submitted list colour
multiplies the normalized RGB back by255; radius squared and its reciprocal
are stored at +0x24/+0x28, intensity at +0x34. Together with 0x0057BCB0 this
resolves the inputs consumed by 0x0057BF00.

Initializer 0x0058F480 builds 256 lookup values at 0x007818A0:
`L[n] = 0.25 + 16/n`, including infinity at n0. It is not reciprocal square
root. For ordinary finite distances, the consumer's float-bit indexing is
equivalent to nearest-even rounding of `16*d² - 0.49999`. Its contribution
factor is `dot(normal,toLight) * intensity * L[n] * (radius²-d²)/radius²`.
Outside the radius and for negative incidence it contributes nothing; it caps
each light factor at1 before adding RGB, without capping the running sum.

Independent assembly review (2026-10-15) confirms sign-bit tests at
0x0057BFFF and 0x0057C011: negative incidence, including negative zero,
skips the light. Positive incidence in bin0 multiplies by infinity and
takes the full-RGB branch. Zero incidence there raises x87 invalid; with
masked invalid the negative indefinite NaN also reaches full RGB, but the
game's exception mask is not established. The reference rejects that case.

`ClassicPointLight` exposes normalized inputs and a headless reference of that
vertex contribution. It does not claim bit-identical x87 arithmetic or final
framebuffer transfer. Native clamped radii keep lookup indices below256;
input conversion requires that clamp.

## 6. Vertex RGB packing before texture modulation

STRONGLY INFERRED (whole reads, 2026-10-15): 0x0057B6D0 sends light-sum
triples through pointer0x00779358. Selector 0x0058A970 installs one of
0x0058B2A0/0x0058B370/0x0058B440/0x0058B680. The first two retain colour/UV
triples for clipped vertices and delegate output to the latter two.

Both output sinks convert each 0..255-unit channel with the float-bit rounding
idiom, optionally multiply the resulting unsigned integer by a per-vertex
16.16 factor when flag0x10 is set, then cap each unsigned channel at255.
That multiplication retains only the low32 bits before logical shift16; RGB
has no signed lower clamp. Negative/huge inputs are not ordinary saturation.
They combine those bytes with the separately prepared alpha into ARGB. This
packs the diffuse vertex colour before the material texture modulation already
documented in kmf-materials.md. The two sinks differ in vertex/UV output layout,
not this RGB conversion. No nonlinear gamma operation occurs in these sinks.

Thus material/instance brightness and separate point lights may add above255
internally, but the submitted per-channel diffuse colour is bounded before
texturing. The producer of the optional multiplier, downstream framebuffer
transfer and exact x87 exceptional cases remain outside this read. This does
not establish a complete replacement for Unity's light evaluation.

Independent binary review (2026-10-15) confirms the selector, conversion,
unsigned cap, alpha composition and absence of sink-local gamma. Exceptional
and negative input behavior remains outside the ordinary RGB contract.

## 7. Opt-in Unity integration

The Classic Vertex Lighting Inspector preview uses the section5 torch response
and section6 ordinary nonnegative diffuse packing before texture modulation.
Its legacy per-vertex torch diagnostic does not match either animated native
arm; the supplied tile-table shader uses section3's classified responses.
The legacy diagnostic retains URP light selection. Ordinary publication instead
uses native visibility and selected tile tables, including terrain sources.
Changed entries upload, Clear removes them for resync, and runner destruction
disposes buffers. Globals assume one active DungeonScene.

URP's screen-cluster lookup is used only for vertices inside the viewport and
beyond the near plane. Other vertices iterate the bounded visible-light array;
they cannot safely use a fragment-oriented screen-tile lookup. Ordinary Forward
uses its per-object list. This fallback needs runtime performance measurement.

The initial-ambient diagnostic follows section8; the accepted brighter preview
retains its scene lighting fallback. Selected-table lighting excludes unrelated
URP punctual lights. Singular zero-incidence remains outside the native contract;
framebuffer transfer and matched original brightness remain open.
The preview stays disabled by default pending rendered comparisons.

Controlled GPU results, campaign captures and CPU measurements are maintained
in [classic-lighting-verification.md](classic-lighting-verification.md).
Legacy arithmetic/camera entry points remain `ClassicLightingProbe.Run` and
`ClassicLightingRenderProbe.Run` in DK2.Unity.Editor. They do not save owner settings.

## 8. Initial ambient and the placeholder sun

CONFIRMED (whole reads, callback instructions and writer references, 2026-10-15):
`0x005747E0` stores supplied RGB multiplied by255 in the vertex-light globals.
`0x005983C0` calls it with zero after `0x00574310` installs temporary values.
Those two routines are the only direct writers found for each ambient global;
the setter's only call references are initialization and `0x005986F0`.

`0x00405010` requests command87 with delta+512 or-512 for the ambient keys.
The callback computes `a=clamp(((storedAmbient>>12)+delta)/4096,0,0.5)`,
persists `floor(a*4096)<<12` through `0x005662A0`, then supplies grey RGB(a,a,a).
Thus each key changes normalized ambient by0.125 until clamped. Graphics
settings load/reset (`0x00564B00`, `0x00563FB0`) default the `Ambient Light`
field at+0x4C to512, not40; the preset loader `0x00565CF0` also writes it.
None of these read settings bodies applies the value to the ambient setter.
This does not establish every possible indirect startup command or saved setting.

The initial-ambient diagnostic therefore retains zero and excludes Unity's
placeholder directional/sky lighting; material/terrain additions and point lights
still contribute. The owner accepted the brighter preview with this diagnostic off.
Do not substitute an invented startup fill: compare an original initial view and
the same view after ambient-key presses before attributing black tops/arches to
missing global ambient. `ClassicAmbientLight` and the adapter's +/− keys implement
that command math without registry persistence; matched-original parity remains pending.

## 9. Per-tile terrain point lights

CONFIRMED: `0x00441CD0` submits renderer slot `+0xE0`, which is `0x00599960`
in vtable `0x00670574`. Therefore the terrain source uses the same point-light
coefficient path as a torch, not a flat ambient addition. Startup requests
radius `0x1200` (1.125 tiles) and intensity128 (normalized1). Its initial RGB500
is overwritten by selected terrain runtime RGB `+0x1AF/+0x1B1/+0x1B3` (or a
highlight override) before submission at the tile centre. Flags
`0x8052` lack engine shadow-source flag `0x4000`, so it receives renderer flags4
before classification, rather than a shadow-source light's flags.

The 32-bit fixed-point height loader `0x004F66E0` copies intermediate `+0x244` to runtime `+0x1BF` when field-present
word `+0xA8` is nonzero. With the confirmed intermediate shift `+0xB8`, this is
file offset `+0x18C`, in units1/4096. An absent field leaves the previous runtime
value intact; its initial default has not been established.

The complete-record reader/writer exposes this signed value as optional
`TerrainDef.TerrainLightHeight`; null preserves the writer's template bytes.
Thirty terrain tests pass, including signed extremes, depth independence,
offset-only edits and shipped-file byte identity. The field is not yet submitted
to the renderer, pending the light-table gathering rule below.

Eligibility is `0x00452DC0`: actual rooms take precedence and need byte `+0xC == 0xFF`
and TerrainLight. Plain scanned/unexplored terrain selects RevealThroughFog terrain
or ClaimScan38, testing TerrainLight without querying doors. Other plain terrain
uses its actual definition, TerrainLight and no door. The caller admits only explored,
scanned, Sight-visible or possession-visible tiles; this selector never substitutes Fog27. The selected
terrain supplies point RGB; its AmbientLight colour remains a separate
instance-colour input described in room-floors-and-liquids.md and wall-pieces.md.
That byte is the actual construction piece code, not a universal room-instance
neighbour test. The selector-specific writers are in
[room-floors-and-liquids.md section1](room-floors-and-liquids.md#1-the-writer-of-a-room-tiles-byte-0xc-0x004edc10).
In particular, normal/quad/double-quad masks compare terrain kind and owner,
not room id; other construction selectors encode positions instead.

For source-list order, `0x0043C310` traverses existing tile light objects
before submitting the terrain source. Since `0x00571A90` prepends, terrain
light appears first in the cached source list, followed by qualifying existing
objects in reverse tile-chain traversal order.

`0x00571A90` appends these sources to current **tile cache entry** `+0x34`,
not a global frame list and not one immediate mesh draw. The current entry is
set by `0x005722A0`; it retains that list until invalidation/rebuild. The
dynamic source list at `+0x38` instead clears during the next frame's
`0x005723D0` update. Thus the helper names alone do not imply that sources
exist for every world tile or light every neighbouring draw.

CONFIRMED (whole reads, 2026-10-15): `0x00576230` gathers the draw-light table
for each visible tile. It reads static and dynamic lists from the 7x7 tile-cache
window centred on that tile (offsets -3..+3). Source eligibility here is strict
horizontal squared distance from tile centre `(x+0.5,y+0.5)` less than
`(sourceRadius+1)^2`; source height is not part of this gathering test. This
producer performs no ray/LOS query and no explicit map-edge clipping. Its
inputs are prepared cache entries, not arbitrary sources from unseen map tiles.

The first pass retains at most 12 static sources. On overflow it replaces the
first farthest existing slot only when the incoming source is strictly closer
to tile centre. Equal-distance candidates do not replace. Static sources are
not subsequently sorted. The dynamic pass appends sources after the static
ones, up to 24 total, using the same nearest replacement within the dynamic
portion only. Static slots are protected from dynamic replacement. Counts live
at cache `+0xC4/+0xC8`, with source pointers at `+0xFC`. Dynamic entries refresh
each frame. Static invalidation uses unsigned target table stamp `+0x10`
versus source-cell build stamp `+0x0C`. A fresh window sets each column's dirty
bit if any cell has a strictly newer build stamp. Rolling replaces only the
incoming column's bit; the other six bits retain their preceding values.
Any set bit rebuilds all static selections and stamps the target with current
cache frame `0x00760B10`; no bits means its static table is retained. Static
cell rebuild `0x005722A0` and adjoining-cell build `0x005723D0` write `+0x0C`.

Encounter order is significant for ties: each source list is traversed
head-to-next. Static gathering iterates rows, then physical window columns;
dynamic gathering iterates physical columns, then rows. A freshly filled
window maps physical columns0..6 to targetX-3..targetX+3 and sets cursor0.
For consecutive+x targets, physical column[cursor] receives targetX+3 at all
seven rows, then cursor advances modulo7. Consequently the next target's
physical order is `[x+3,x-3,x-2,x-1,x,x+1,x+2]`; traversal never starts at the
cursor. Preserve this order when reproducing overflow ties.
The window resets at each native `0x00576230` invocation (one visible-tile
enumeration), on a different target Y or
nonconsecutive target X. Normal
polygon enumeration `0x005735A0` visits increasing Y, then increasing X over
each projected polygon scanline; the first X is polygon-dependent. Alternate
camera enumeration `0x005737E0` uses ring queries instead. Starting every row
at map X zero is therefore not an established substitute. Cache preparation
`0x005725D0` builds each visible tile, then updates an inclusive rectangle:
each side extends three tiles unless the immediately adjoining tile has
visible flag8, in which case that bound stays at the target coordinate.
The combined rectangles cover the needed border; this does not establish
that every authored map source is prepared.

`0x00572CF0` passes a context whose `+0x08` points at cache `+0xC4` to both
static and dynamic primitive lists through virtual slot+8. Animated preparation
`0x00584900` copies this pointer into draw `+0x5C`; moving creatures therefore
receive the table associated with their current cache tile. Primitive/material
lighting flags still determine how that supplied table is used; receiving the
table does not establish that an explicitly unlit primitive uses every source.
`0x0057BBF0` then applies required source flags and strict 3D sphere overlap
`(sourceRadius+drawRadius)^2`, yielding the 32-bit mask consumed downstream.
Instructions `0x00584B25..0x00584B95` select masks requiring `0x100` and `0x04`;
the latter feeds radial RGB at draw translation through0x0057C080. Terrain sources classify to
`0x64`, including `0x04`. Wall torch classification depends on global
`0x00759AF8`: the bridge's input flags `0x12` or `0x14` become `0x1B2` or
`0x74` through `0x00599BD0`, respectively. The former belongs to the `0x100`
arm rather than the `0x04` uniform radial arm. The global is settings object
`0x00759A78 +0x80`, persisted as `Cheap Lighting`: `0x005666B0` stores and
saves it, and `0x00564B00` loads it with default zero when the key is absent.
Thus animated preparation selects the `0x100` arm by default; nonzero Cheap
Lighting selects its `0x04` uniform radial arm. A preview evaluating every torch per
vertex does not reproduce that animated default arm (section3). Terrain's
source-mask producer remains unaudited. The loader/setter establishes the option
and missing-key default, not the current owner's installed registry value.

The remaining implementation boundary is native cell visibility/cache
enumeration; the input source eligibility and gathering itself are now resolved
(Q597). The projected-region boundary is in [classic-light-visibility.md](classic-light-visibility.md)
and Q602. An unrestricted sum of all nearby authored tiles would bypass both
that boundary and the confirmed 12-static/24-total capacity.

`ClassicLightTable` implements the bounded selection core for an assigned tile.
`ClassicLightWindow` reads explicitly prepared cache cells in the confirmed
rolling order, without clipping border cells. Its caller resets it before each
visible enumeration and supplies typed renderer-frame/build stamps. The window
applies native dirty-column invalidation, retaining42 list-head snapshots on a
horizontal advance and querying only seven entering cells; backing storage stays
stable until Reset. `ClassicWorldLightCache` supplies visibility/preparation. Headless cases
cover overflow, ring wrap/reset, retained dirty/clean bits, unsigned stamps,
empty-cell invalidation, dynamic refresh and zero warm-gather allocations.
`ClassicPointLight.FromTerrain` converts the confirmed source inputs. DungeonScene
publishes WorldView terrain sources and classified torches through the automatic
cache, removes tables leaving the view, and reuses visibility geometry until the camera changes.

## 10. Selected-table Unity verification

The bridge design and whole-grid authority are in ADR-0484. Current controlled
fixtures, automatic campaign-source checks, batch anchors and measurement limits
live in [classic-lighting-verification.md](classic-lighting-verification.md).
