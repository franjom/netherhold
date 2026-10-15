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
The first two also receive post-creation flags/animation setup. Their later
movement and renderer interpretation remain untraced; these inputs alone do
not establish replacement Unity directional or ambient lighting.

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

`ClassicPointLight` exposes normalized inputs and a headless reference of that
vertex contribution. The Unity adapter consumes the inputs; its URP falloff
is unchanged. The reference does not claim bit-identical x87 arithmetic or
final colour transfer. Native clamped radii keep ordinary lookup indices
below256; input conversion requires that native radius clamp. The singular
bin is explicitly unsupported by the reference evaluator; its native float
propagation and downstream framebuffer transfer still require comparison before
using this as an active shader replacement.

## 6. Vertex RGB packing before texture modulation

STRONGLY INFERRED (whole reads, 2026-10-15): 0x0057B6D0 sends light-sum
triples through pointer0x00779358. Selector 0x0058A970 installs one of
0x0058B2A0/0x0058B370/0x0058B440/0x0058B680. The first two retain colour/UV
triples for clipped vertices and delegate output to the latter two.

Both output sinks convert each 0..255-unit channel with the float-bit rounding
idiom, optionally multiply the resulting unsigned integer by a per-vertex
16.16 factor when flag0x10 is set, then cap each unsigned channel at255.
They combine those bytes with the separately prepared alpha into ARGB. This
packs the diffuse vertex colour before the material texture modulation already
documented in kmf-materials.md. The two sinks differ in vertex/UV output layout,
not this RGB conversion. No nonlinear gamma operation occurs in these sinks.

Thus material/instance brightness and separate point lights may add above255
internally, but the submitted per-channel diffuse colour is bounded before
texturing. The producer of the optional multiplier, downstream framebuffer
transfer and exact x87 exceptional cases remain outside this read. This does
not establish a complete replacement for Unity's light evaluation.
