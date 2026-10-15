# Classic vertex lighting

Source: retail v1.70 DKII.EXE, Ghidra whole reads, 2026-10-14/15.
Existing material/terrain inputs are in kmf-materials.md, tag-highlight.md,
wall-pieces.md and wall-torches.md; this note owns their newly traced light helper.

## 1. Point-light contribution

STRONGLY INFERRED: 0x0057BF00 is a point-light accumulator, not a colour
clamp/packer. It loops over the supplied light list and computes distance
squared from the vertex to each light. Outside the light's squared radius
it adds nothing. Inside, it multiplies the light-direction/vertex-normal dot
product by a light-specific coefficient; negative-facing contributions are skipped.
It combines that factor with a lookup-table value, remaining
squared radius and another light coefficient. The resulting contribution
factor is capped at one, then the light's RGB is added to the running colour.
This caps one light's contribution; it does not clamp the accumulated RGB.

The vertex loop 0x0057B6D0 initializes colour from vertex light, material
brightness, instance tint and global ambient, then calls this helper with
the vertex position and normal before handing colour/UV to a render sink.
The original sum remains documented in tag-highlight.md section6.

The coefficient/lookup reads are resolved in section5 and vertex RGB packing
in section6. Downstream framebuffer transfer, native-versus-URP response and
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

The whole-room and room-wall consumers are documented in
room-floors-and-liquids.md section2 and wall-pieces.md section2. The adapter
adds those confirmed tints through texture-weighted URP emission on materials
that support emission, sharing variants by source material and RGB. It retains
the existing tag addition. Room quarters, plain terrain lighting and unlit
materials and special gate wall builders are outside this scoped tint application. This restores a missing
input, not final colour-transfer or matched-lighting parity.

## 3. Local light lists

STRONGLY INFERRED (whole reads, 2026-10-14): 0x0057BCB0 copies selected source
lights into ten-dword local entries: position, squared radius, attenuation
coefficient, normal coefficient and RGB. Selection uses successive bits of
the supplied mask. 0x0057BD70 instead evaluates attenuation at a supplied
instance position, retaining positive contributions and transforming light
directions through 0x00594E70. Their caller mode selection remains unaudited.

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
It retains URP light selection and shadows. A tile-indexed GPU buffer carries
native torch inputs; changed entries upload on presenter Flush, Clear removes
entries for resync, and runner destruction disposes the buffer. The buffer
assumes one active DungeonScene, matching the current renderer.

URP's screen-cluster lookup is used only for vertices inside the viewport and
beyond the near plane. Other vertices iterate the bounded visible-light array;
they cannot safely use a fragment-oriented screen-tile lookup. Ordinary Forward
uses its per-object list. This fallback needs runtime performance measurement.

The initial-ambient diagnostic follows section8; the ordinary preview retains
its scene lighting fallback until native fill sources are supplied. Non-torch
point lights and zero-incidence singular-bin cases also use URP response. These are explicit
preview limits, not confirmed original behavior. Framebuffer transfer, shadows,
local-light selection parity and original global-light producers remain open.
The preview stays disabled by default pending rendered comparisons.

Verification (2026-10-15): 348 presentation tests and adapter compilation pass.
The shared HLSL arithmetic probe passes; the actual URP camera probe passes
seven cases (native response, shifted camera, placeholder sun, offscreen vertices,
remove, repopulate, clear)
with SRP Batcher enabled and maximum error below0.000001. These are controlled
fixtures, not matched original-game frames or whole-map performance evidence.
Reproduce through the Editor menu DK2 / Classic look / Verify native lighting
on GPU and Verify native lighting through URP; batch entry points are
`DK2.Unity.Editor.ClassicLightingProbe.Run` and
`DK2.Unity.Editor.ClassicLightingRenderProbe.Run` (omit `-quit` for the latter).
Neither saves the owner's scene or project settings. Behind-near-plane vertex
coverage remains pending.

## 8. Initial ambient and the placeholder sun

CONFIRMED (whole reads and callback disassembly, 2026-10-15):
`0x005747E0` stores each supplied ambient channel multiplied by255 into the
three globals consumed by the vertex-colour sum. `0x005983C0` calls it with
zero for all channels after renderer initialization. The initializer
`0x00574310` first installs temporary ambient values; these are overwritten.
Callback `0x005986F0` can subsequently change uniform grey ambient when global
command87 is submitted, clamping its normalized result to0..0.5. That
control's gameplay producer remains untraced and is not implemented here.

The opt-in initial-ambient diagnostic starts explicit ambient RGB at zero and
ignores Unity's placeholder directional/sky lights. Material/terrain additions and
point lights still contribute before packing. This implements the confirmed
startup input rather than treating the current Unity scene's white sunlight
as original illumination. It is disabled by default: missing native fill
sources otherwise leave the upper terrain and Heart arches black. The per-tile
terrain-light response in section4 and the ambient-control producer remain
necessary for full scene parity.

A production Level1 comparison confirms that torches already contributed;
the reported absence was a visual-contrast difference, not a proved missing
source. Keep matched-original comparison open; do not label the entire torch
path broken from that screenshot alone.

## 9. Per-tile terrain point lights

CONFIRMED: `0x00441CD0` submits renderer slot `+0xE0`, which is `0x00599960`
in vtable `0x00670574`. Therefore the terrain source uses the same point-light
coefficient path as a torch, not a flat ambient addition. Startup requests
radius `0x1200` (1.125 tiles) and intensity128 (normalized1). Its initial RGB500
is overwritten by selected terrain runtime RGB `+0x1AF/+0x1B1/+0x1B3` (or a
highlight override) before submission at the tile centre. Flags
`0x8052` lack engine shadow-source flag `0x4000`, so it receives renderer flags4
before classification, rather than a shadow-source light's flags.

The height copied to position is a 32-bit fixed-point field: loader
`0x004F66E0` copies intermediate `+0x244` to runtime `+0x1BF` when field-present
word `+0xA8` is nonzero. With the confirmed intermediate shift `+0xB8`, this is
file offset `+0x18C`, in units1/4096. An absent field leaves the previous runtime
value intact; its initial default has not been established.

The complete-record reader/writer exposes this signed value as optional
`TerrainDef.TerrainLightHeight`; null preserves the writer's template bytes.
Thirty terrain tests pass, including signed extremes, depth independence,
offset-only edits and shipped-file byte identity. The field is not yet submitted
to the renderer, pending the light-table gathering rule below.

Eligibility is `0x00452DC0`, before source creation: plain terrain needs
TerrainLight (bit28) and no door. Scanned/unexplored terrain selects either its
RevealThroughFog terrain or ClaimScan38 before testing TerrainLight. A room
tile needs byte `+0xC == 0xFF` and TerrainLight on its terrain. The selected
terrain supplies point RGB; its AmbientLight colour remains a separate
instance-colour input described in room-floors-and-liquids.md and wall-pieces.md.

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
at cache `+0xC4/+0xC8`, with source pointers at `+0xFC`. Static tables refresh
when a source cache build stamp is newer; dynamic entries refresh each frame.

Encounter order is significant for ties: each source list is traversed
head-to-next. Static gathering iterates rows, then physical window columns;
dynamic gathering iterates physical columns, then rows. A freshly filled
window maps these columns left-to-right, but consecutive+x target tiles reuse
a rolling column buffer: the next physical column is replaced by the incoming
rightmost column before the cursor advances. Thus encounter order is not
always increasing world X. Preserve this order when reproducing overflow ties.

`0x00572CF0` passes a context whose `+0x08` points at cache `+0xC4` to both
static and dynamic primitive lists through virtual slot+8. Animated preparation
`0x00584900` copies this pointer into draw `+0x5C`; moving creatures therefore
receive the table associated with their current cache tile. Primitive/material
lighting flags still determine how that supplied table is used; receiving the
table does not establish that an explicitly unlit primitive uses every source.
`0x0057BBF0` then applies required source flags and strict 3D sphere overlap
`(sourceRadius+drawRadius)^2`, yielding the 32-bit mask consumed downstream.

The remaining implementation boundary is native cell visibility/cache
enumeration; the input source eligibility and gathering itself are now resolved
(Q597). An unrestricted sum of all nearby authored tiles would bypass both
that boundary and the confirmed 12-static/24-total capacity.

`ClassicLightTable` implements the bounded selection core for an assigned tile;
its caller must supply visible 7x7 candidates in the native encounter order and
own static cache refresh stamps. `ClassicPointLight.FromTerrain` converts the
confirmed source inputs. The core is tested but not yet wired into Unity's
source publication; it does not replace that cache/visibility integration.
