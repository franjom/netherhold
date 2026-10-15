# Classic mesh shadows

Primary binary reads, 2026-10-15. These renderer paths generate mesh silhouettes;
the creature-specific shadow-enable producer remains open.

## Confirmed resource and silhouette path

- `0x0058E330` creates 64 resources named `ENGINE_SHADOW_DYNAMIC_<index>`,
  requesting 32 by 32 pixels, and stores their handles at `0x00780960`.
  `0x0058E2C0` requests a same-size resource keyed by a supplied name and index.
- `0x005808E0` transforms mesh vertices into a shadow-image coordinate system,
  traverses their actual triangles and submits them to `0x0058E080`.
  `0x005855E0` does likewise after sampling animated vertices through
  `0x0057E5B0`. Both skip mesh materials whose flags intersect `0x1023`.
- `0x0058E440` clears a 1024-byte coverage buffer with row stride 32.
  `0x0058E080` accumulates triangle scanline coverage using coordinates eight
  times the destination resolution, saturating byte coverage. `0x0058E470`
  uploads that image through an 8-bit path or expands each coverage byte to
  equal RGB with alpha 255. No separate blur appears in this finalizer.
- `0x0058E3E0` advances the dynamic slot counter and clamps it at 63 rather
  than wrapping, then clears the coverage buffer. The counter's frame reset
  has not been located. The animated caller at `0x00585403` caches a generated
  texture handle in model offset `+0x48` below quality 2 and regenerates at
  quality 2 or above; the submission describes two triangles/four vertices.
- Both silhouette paths choose a cached name/index-zero resource at lower
  quality and a dynamic resource at higher quality. The animated path switches
  resource choice at quality 2; the other at quality 3. At quality 3 and above,
  their projection matrix depends on a selected light's horizontal position;
  lower qualities use a fixed projection. Neither path is a generic blob.

## Receiver, strength and blend

CONFIRMED: `0x0058E640` constructs a four-vertex receiving quad oriented along
the horizontal vector from selected light to transformed model centre. Half
width derives from the model bounding radius times instance scale. Quality
below 3 stretches the quad with light distance; quality 3 and above uses an
angle proportional to horizontal distance: `distance * pi * 0.25 * 1.5 / 2.5`.
The receiver's height comes from the supplied per-draw shadow data; its
ground-height producer is below. The quad uses one receiving height rather
than independently sampling terrain at all four corners.

For nonzero horizontal distance `D`, define `u = (centreXY - lightXY) / D`,
`p = (u.y, -u.x)`, scaled bounding radius `r`, receiving native height `H` and
model-centre native height `z`. At quality 3 or above, with the angle above,
longitudinal centre `a = (H-z) * tan(angle)` and half-length `b = r/cos(angle)`.
Below quality 3, `a = D*(H-z)/2` and `b = D*r/2`. The four corners are
`(centreXY + u*(a +/- b) +/- p*r, H)`. These are native render coordinates.
The binary divides by `D`; coincident light/model horizontal positions remain
an exceptional boundary. The apparent implicit operations in the decompile
are vector copies through `0x0041C440`, checked separately.

Strength is `min(0.7, 1.5 * (fadeDistance - horizontalDistance) / fadeDistance)`;
negative results skip drawing. The same strength feeds alpha and each RGB
channel (scaled by 255). `0x00583CA0` supplies the animated mesh's transformed
centre, radius/scale, selected light and shadow data; `0x00582180` is the
equivalent polygon-mesh caller.

When renderer capability flag `0x00764BB8 & 0x40` is set, the resource's flags
are `0x480`. `0x0057C420` produces state `0x1740`, and `0x00589250` maps this
to source ZERO / destination INVSRCCOLOR, depth test enabled, depth write
disabled and filtering enabled. Thus a sampled coverage value `c` darkens the
existing scene by `1 - c * strength`. The alternative resource flags `0x81`
use alpha blending with the 8-bit conversion path; exact equivalence of that
format remains UNKNOWN. `0x00589140` only touches surface residency through
`0x00593720`; it does not select blending.

## Shadow selection

CONFIRMED: animated preparation `0x00584900` skips shadows when global quality
is nonpositive, then calls `0x0058EC70` to select sources. That selector requires
light flag `0x10`, 3D squared distance below the global range squared and a
positive per-draw capacity byte. If there are too many candidates it retains
the nearest sources and uses the next source distance for fading. Otherwise
the fade distance is the global range. Initialization sets the range to 2.5;
`0x0058E580` can adjust it gradually within 0..2.5, so 2.5 is not invariant.

CONFIRMED: `0x0059C880` supplies capacity 3 when global `0x00759ACC` is nonzero
and RenderInfo `+0x4C` bit 0 is set, otherwise capacity zero. With capacity 3,
it queries world slot `+0x204` at the draw position for floor height and passes
`-floorHeight / 4096 - 0.01` in native render coordinates. `0x00583120` copies
that height into draw `+0x80` and the capacity byte into draw `+0x88`.
The world's floor-height query is established in the record for `0x00446C70`.
The producer deciding which creature RenderInfos enable bit 0 remains unread.
The small class initializer `0x004B4120` preserves that byte; it does not
establish a default shadow-enable value.

CONFIRMED: `0x00599960` maps engine-light `+0x134` bit `0x4000` to initial
render-light flags `0x12` or `0x14`; when unset it supplies `0x04`.
`0x00599BD0` preserves bit `0x10` while adding renderer classification bits.
Thus engine flag `0x4000`, not engine flag `0x10`, determines eligibility for
this shadow-source selector. Wall torch engine flags are established in
[wall-torches.md section 1](wall-torches.md#1-the-rule-for-one-wall-tile).

## Remaining boundaries

The creature-specific RenderInfo enable-bit producer and the alternative
8-bit blend path remain unread. Silhouette
generation alone does not establish Unity directional-shadow-map equivalence.

The implemented preview and its explicit representation limits are in ADR-0482.
`ClassicMeshShadow` owns the confirmed quality-three geometry and fade;
`ClassicCreatureShadows` applies it to the currently sampled mesh. Its GPU
fixture covers asymmetric orientation, multiple sources, atlas growth,
animation changes, framebuffer alpha and caster removal. This is not a
matched-original frame or a full shadow-quality implementation.

The isolated Level1 tick70 probe (2026-10-15) measured warmed CPU shadow
preparation/submission at median0.1761ms, p950.1992ms and zero managed bytes
over30 samples. This is a small-scene editor measurement, not GPU time or a
busy-map performance result.
