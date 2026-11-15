---
systems: [camera-lighting, presentation-unity]
---
# Is the first-person possession picture warped? (projection read)

DKII.EXE v1.70, fresh reads recorded 2026-11-15, Ghidra 12.1.4 (pyghidra-mcp and the
headless runner); corrected the same day after an independent review. Question: the owner's
1600x1200 captures (`F:\DK2Evidence\captures\visual\possession-first-person--2026-11-15--001`)
show straight ceiling, wall and floor edges as curves, and near things stretched at the
sides ([possession-client.md](possession-client.md) §6b). Where does DK2 make that, and how?

## 1. Verdict

**STRONGLY INFERRED (read): every vertex that goes through the sinks of
`Renderer_SelectVertexSinks` (`0x0058A970`) is projected by a plain pinhole.** A scan for the
writers of the vertex screen-position slots finds four routines: the two projection sinks
`0x0058ADC0` and `0x0058AF70`, the screen-space copier `0x0058B190` and the 2D clipper
`0x0058CC40`; the 3D clipper `0x0058BB60` re-projects through `0x0058ADC0`. None has a
square root, table, lens-dependent term or possession branch in the position maths: screen =
centre + f x (x / z), likewise y. The terrain, creature, object and effect draws that call
`0x0058A970` (mesh, sprite, ceiling-grid and shadow draws; callers in its record) use it; the HUD's
draws were not read. What possession changes
in this path is the focal length (half the ordinary one), a per-vertex distance fade (§4) and
which tiles and ceiling items are drawn; none bends a line. The reads that this verdict does
not cover are listed in §7.

So the remake's plain perspective is the right *kind* of projection. What it gets wrong is the
**angle** (§3); the curved lines the owner sees have two geometric causes the remake can
reproduce (§6), neither a screen-space distortion (HYPOTHESIS until a capture is fitted).

## 2. The projection (STRONGLY INFERRED, assembly read whole)

Per frame `Renderer_RunView` (`0x00598880`) builds the frame block and `Renderer_SetFrameParameters`
(`0x00575A00`) copies it to `0x00760AB8..`; `View_ApplyCameraProjection` (`0x0058A6F0`) derives the
sink constants. Block dwords: 0..2 eye (floats, tile units), 3..11 view rotation, 12 focal length f,
13/14 viewport left/right, 15/16 top/bottom, 17/18 projection centre (cx, cy), 0x13 view distance V,
0x14 flag bits. The centre is the midpoint of the viewport (stores at `0x00575B46`, `0x00575B4E`; an
earlier copy of the caller's dwords at `0x00575A60`/`0x00575A69` is overwritten).

```
f      = lens * 320 / 4096      pixels   (12.12 multiply of camera +0xD8E by 320.0, 0x00598D3C..0x00598DE2)
halfW  = cx - left,  halfH = cy - top           (0x0077F4CC, 0x0078093C)
D      = diag(f/halfW, f/halfH, 1)              (applied after the rotation, to the output x and y)
(x,y,z) = D * (R_view * (P_world - eye))        (0x0058ACB0 / 0x0058AD10; linear)
sx = cx + 0.25 + halfW * (x / z)                =  cx + 0.25 + f * X_view / Z_view
sy = cy + 0.25 + halfH * (y / z)                =  cy + 0.25 + f * Y_view / Z_view
rhw = 1 / z                                     (D3D7 pre-transformed vertices; no affine-texture state set)
depth = z <= 0.5 :  [0x0077F490] * z + [0x0077F4D0]
        z >  0.5 :  [0x007793A0] - [0x0077F934] / z        then clamped to at most 0.99993896
```

Sinks. `0x0058AF70` first computes four outcode bits in the normalised coordinates (x < -z, x > z,
y < -z, y > z: the exact viewport frustum, no guard band; any vertex with z < 0 gets a non-zero
outcode, since z+x and z-x sum to 2z) and skips the projection when one is set; `0x0058ADC0` has no
outcodes. Both fill a 0x28-byte slot of the table at `0x0076F358` (+0x0C sx, +0x10 sy, +0x14 depth,
+0x18 rhw, +0x1C outcode, +0x24 fade word). Which pair is used comes from `0x0058A970`'s sixth argument
(zero: `0x0058ACB0` -> `0x0058AF70` with `TriSink_Clipping`; non-zero: `0x0058AD10` -> `0x0058ADC0` with
`TriSink_CullAndEmit`, no clipping and no distance cull). `TriSink_Clipping` (`0x0058B940`) drops a
triangle whose three vertices share an outcode bit and queues one with any outcode for
`Renderer_FlushPreparedTriangles` (`0x0058AC20`), which clips by `0x0058A970`'s **second** argument
(stored at `0x00779378`): non-zero (3D callers `0x00587642`, `0x00588512`, `0x00581C4D`, `0x0058370B`,
`0x0057F3EF`) runs `0x0058BB60`, a view-space clipper against x = -z, x = +z, y = -z, y = +z (helpers
`0x0058C450`, `0x0058C680`, `0x0058C890`, `0x0058CA80`, interpolating position and UV linearly, outcode
bits 4, 8, 1, 2) and re-projects the survivors through `0x0058ADC0`; zero (2D callers `0x00578C1B`,
`0x00577A48`) runs `0x0058CC40`, which clips in pixel space against the viewport rectangle.
All of these are straight-edge cuts. The picking and sphere helpers (`Render_ProjectSphere`,
`Renderer_ScreenPointOnHorizontalPlane`, `Renderer_ProjectWorldPoint`) read the same f and centre.

## 3. The two parameter values (STRONGLY INFERRED for the formula, data for the lens)

| | ordinary dungeon camera | possession (modes 2, 10, 11) |
|---|---|---|
| lens `+0xD8E` (preset id 3 / id 2) | 8192 | 4096 |
| focal length f (absolute pixels) | 640 | **320** |
| hFOV, vFOV at 1600 x 1200, full height | 102.7 deg, 86.3 deg | **136.4 deg, 123.9 deg** |
| eye pull-back (zoom) | zoom * 640 / W along the view axis | none (preset zoom 0) |
| distance fade (§4) | off | on |

The branch in `0x00598880` is the same code for both (modes 7 and 0x12 have other arms). f has no
screen-size term, so the angle grows with the resolution (the remake pins it to the 1600 baseline);
only the ordinary camera's eye pull-back has a 640/W term (`0x00598D10..0x00598D22`,
[classic-camera.md](classic-camera.md) §2). The preset's lens can be replaced by script
(`0x0044A020` set, `0x0044A070` add, both clamped to `+0xD58`..`+0xD5C - 1`; callers `0x00442432`,
`0x00442482`), and `Camera_Update` blends it during the timed move.

**The remake gap (consequence, not a binary claim).** `ClassicCamera.Pose` blends toward
`VerticalFieldOfView(lens, new CameraViewport(640, 640, 480), null)`, i.e. f = 320 on a 640 x 480
screen: 73.7 deg vertical, 90 deg horizontal. The original's f = 320 on a 1600 x 1200 screen is
123.9 / 136.4 deg, nearly twice the angle, which is what stretches near walls and pillars at
the sides. The ordinary path already uses `VerticalFieldOfView(lens, viewport, ReferenceScreenWidth)`
(1600); the first-person target should use that call with the possession lens and the real viewport.

## 4. The possession distance fade (STRONGLY INFERRED; implementable)

Frame flag bit 3 (`Camera_InPossessionOrPathMode` `0x0044AA80`; modes 2, 10, 11 and path mode 0x12;
copied to `0x0077F450`) enables a block in both projection sinks. Inputs: the normalised view-space
vertex (x, y, z) of §2, and V, the float at `0x0077F4E8` with 2/V at `0x0077F4E0` (written at
`0x0058A7FF`, `0x0058A844` from frame parameter 0x13). V is `Camera_VisibilityDistance`
(`0x0044D550`) = max(2 * zoom, `+0xD8A`) / 4096 tiles, capped at 32.0 only with frame bit 0 (modes 2, 10,
11); preset 2 gives 12, and a lower graphics quality reduces the preset maximum ([classic-camera.md](classic-camera.md) §1).

```
D = z + 0.5 * (|x| + |y|)         in the normalised coordinates, i.e. z + 0.5*(f/halfW*|X| + f/halfH*|Y|)
t = 2 * (V - D) / V               double constant -0.5 at 0x0066FE80; float 2/V at 0x0077F4E0
t < 0       : fade word 0, flag 0x10 on the vertex, outcode |= 0x20 (0x0058AF70 only)
0 <= t < 1  : fade word = floor(65536 * t) (magic-number rounding with -0.49999), flag 0x10
t >= 1      : nothing (no flag, no fade)
```

Consumers (`0x0058B440` at `0x0058B522`; `0x0058B680` at `0x0058B769`, reviewer read, not re-read by
claude): after the light sum is converted to R, G, B (0..255), when flag 0x10 is set each channel
becomes (c * word) >> 16 (32-bit multiply, logical shift), clamped to 255; alpha is untouched.
`TriSink_Clipping` drops a triangle whose three vertices all have t < 0 (their shared 0x20 bit). So:
full brightness out to D = V/2, linear to black at D >= V, in a diamond metric. Because the lateral
terms carry f/halfW and f/halfH, the fade's shape depends on the resolution and the viewport. The
ordinary camera skips the block. It shades; it moves no vertex.

## 5. Ruled out

- `0x00760B60` (flag bit 1) is read only by the two item draw slots `0x00586190`, `0x00587060`
  (ceiling visibility); `0x00760B84` likewise. Neither touches vertices.
- Math instructions (FSQRT, FPATAN, FSIN, FCOS, FPTAN, F2XM1, FYL2X) across the binary: none is in the
  projection sinks, `Renderer_SelectVertexSinks`, `View_ApplyCameraProjection` or
  `Renderer_SetFrameParameters`. The view rotation is yaw, pitch and roll matrices only
  ([classic-camera.md](classic-camera.md) §2); `0x0044D6D0` was not re-read here.
- The terrain lattice (`0x0058F030`) bends wall tops in world space before projection and is the same
  in possession ([terrain-lattice.md](terrain-lattice.md) §3).
- D3D state (`D3D_ApplyRenderState`, `0x00589250`) sets blend, z and filter states only; no
  texture-perspective or texture-transform state, and `0x00598880` has no frame-buffer read-back or
  second pass in its ordinary arm (mode 7 draws a quad in another arm).

## 6. Why the capture looks curved (HYPOTHESIS, consistent with the reads)

1. **Geometry that is curved in 3D.** The ceiling is a height-field vault, bowl-shaped over open space
   and pinned to the bent wall tops ([kwd-terrain-rooms.md](kwd-terrain-rooms.md), the possession
   roof), so its texture seams are curves in the world; wall courses follow the lattice.
2. **A 124 x 136 degree frustum** (§3): a straight edge near the side of the frame is drawn at a very
   oblique angle and textures smear radially, which reads as a bulge. A plain perspective draws it
   faithfully; it only looks "warped" next to a narrow one.

A rough check of SHOT0017's wall rim and brick courses against rays from the corridor's vanishing point
found them straight to the eye; an automated fit was too noisy to count. Nothing here is a fitted capture.

## 7. UNREAD, and the next leads

- Not read, so outside the verdict: the D3D device and viewport set-up (users of `DAT_0076C2C4`),
  `Lightning_BuildRibbon` (reads f itself), `0x0044D6D0`, `0x0058A570`, the mode-7 and mode-0x12 arms
  of `0x00598880`, and the draw code outside `0x0058A970`'s callers.
- A fit of f to a capture (two or three orthogonal vanishing points on a shot with known right angles)
  would turn §3 into CONFIRMED and settle the viewport rectangle in possession (`+0xE82`); the captures
  put the corridor horizon at about row 717 of 1200. If a measured straightness test shows a bowed
  line, look next at the D3D device set-up.
- Whether any shipped level overrides preset 2's lens; who writes `+0xD58`/`+0xD5C`.
- The 2D clipper's helpers (`0x0058D580`, `0x0058D790`, `0x0058D990`, `0x0058DB70`).
- `0x00597760` fills three 256-entry WORD tables, the layout of a DirectDraw gamma ramp (HYPOTHESIS:
  a screen colour effect, not geometry; its consumers are unread).
