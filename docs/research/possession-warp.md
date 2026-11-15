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
ordinary camera skips the block. It shades; it moves no vertex. Built engine-free as `PossessionFade`; no
adapter applies it yet.

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
- `0x00597760` fills the display's gamma ramp, a screen colour effect and not geometry:
  [screen-colour-changes.md](screen-colour-changes.md).

## 8. Which tiles are drawn in the possession cameras

Read 2026-11-15 (`0x00575A00`, `0x005737E0`, `0x00572F60` whole in disassembly, `0x00573470`,
`0x005733D0`, `0x00572E00`, `0x0059A570`, `0x00509200`, `0x005725D0`, `0x005723D0`, `0x0043CC30`,
`0x0044AA20`, `0x0044AA80`; records in `functions/`). Q679. The ordinary camera scans the whole view
polygon ([classic-light-visibility.md](classic-light-visibility.md) §1-2, `0x005735A0`); the
possession cameras keep only the polygon tiles that are in line of sight in the plane. Native tile
coordinates throughout (tile (x, y) spans [x, x+1) x [y, y+1); the eye is in tile units).

### 8.1 When it applies (STRONGLY INFERRED)

`0x00575A00` calls `0x005737E0` instead of `0x005735A0` when frame flag bit 0 is set. Bit 0 is
`0x0044AA20`: modes 2, 10 and 11 steady, and during a timed move only when **both** ends are one
of them. So the flights into and out of possession (2000 ms) use the ordinary polygon scan, with
no line of sight, but with bit 3 (either end, `0x0044AA80`) so the scaled polygon of §8.2; path
mode 0x12 has bit 3 and not bit 0. Modes 2, 10 and 11 take the same code; they differ only in the
preset's view distance (12 tiles; 8 for mode 10, [possession-client.md](possession-client.md) §3;
mode 11's own value was not read). The set is rebuilt every frame: `0x00575A00` is called once per
`Renderer_RunView`, resets the list head (`0x00760B14`) and the cache clears each tile's flag 8
after the frame (`0x00572CF0`). Nothing is kept between frames. A viewport of 32 pixels or less in
either direction skips the whole frame set-up.

### 8.2 The view polygon (STRONGLY INFERRED)

Built by `Renderer_BuildVisibilityPolygon` exactly as for the ordinary camera
(`ClassicLightVisibility.BuildPolygon`, BUILT) with two differences:

- V is `min(32, Camera_VisibilityDistance)` tiles (the cap is only with bit 0; presets give at most
  12, so it never bites in the shipped data).
- Bit 3 sets a scale `s = 2 / (hx + 2)`, `hx` = half the viewport width over the focal length
  (`0x00574820`; `scaleViewVolume: true`). It multiplies the distance of the four far corner
  points only: they sit at camera-space depth `s*V`, lateral `+-(hx*s*V + 1)`, vertical
  `+-(hy*s*V + 1)`; the far centre point stays at depth V. With f = 320 and a 1600 px wide
  viewport hx = 2.5, s = 0.444; at 640 px hx = 1, s = 0.667; s < 1 for every width. Why is not
  read (HYPOTHESIS: it keeps the polygon of the 136 degree lens inside the 128 x 128 map below).

How a tile meets the polygon: `0x00572E00` scan-converts the integer boundary into a 128 x 128
byte map `0x0075CA98` (1 before the walk, 0 after), indexed `(x - camTileX + 64) * 128 + (y -
camTileY + 64)`. The scan is the one of `0x005735A0`: rows from the lowest vertex y up to but not
including the highest, per row every tile column from the left edge's integer x to the right
edge's (16.16 edges, arithmetic shift), both ends included; a flat polygon marks nothing (read
from the decompile of `0x00572E00`; not compared instruction for instruction with `0x005735A0`).
So a tile is "in the polygon" when its index (x, y) is in the marked set: no centre, corner or
margin test. `ClassicLightVisibility.EnumerateTiles` of the same polygon is that set. Nothing
bounds-checks the map; with V <= 12 the polygon radius stays under 2V + 2 = 26 tiles.

### 8.3 The enumeration (STRONGLY INFERRED, whole disassembly of `0x005737E0` and `0x00572F60`)

Angles are in 2^24 per turn: `A(dx, dy) = trunc(atan2(dx, dy) * 2^24 / (2 pi)) & 0xFFFFFF`
(zero along +y, growing toward +x; `trunc` toward zero, so a negative angle wraps by the mask).
The visible list starts as one interval [0, 2^24].

```
tx = trunc(eyeX), ty = trunc(eyeY)      the camera tile; always output FIRST, with no test
list = [(0, 2^24)]
for r = 1, 2, 3, ...:                    the ring around (tx, ty), four sides, a dead side stays dead
  side A (alive?): (tx, ty-r), then for i = 1..r: (tx-i, ty-r), (tx+i, ty-r)
  side B:          (tx, ty+r), then for i = 1..r: (tx-i, ty+r), (tx+i, ty+r)
  side C:          (tx-r, ty), then for i = 1..r-1: (tx-r, ty-i), (tx-r, ty+i)
  side D:          (tx+r, ty), then for i = 1..r-1: (tx+r, ty-i), (tx+r, ty+i)
  a side's flag = OR of Test(x, y) over its tiles; Test returns the polygon byte whether or not the
  tile was hidden. A side with flag 0 is not walked again; stop after a ring in which every side
  is dead or returned 0. There is no radius limit: the polygon ends the walk.
```

`Test(x, y)`, in order:

1. `dx = x + 0.5 - eyeX`, `dy = y + 0.5 - eyeY`, `c = A(dx, dy)`, `rho = sqrt(dx^2 + dy^2)`,
   `w = trunc(2^21 / rho)` (half a turn is 2^23, so the half width is (pi/4)/rho radians:
   wider than the tile's true angular half width, which is about 0.5/rho to 0.71/rho; rho >= 0.5
   for every tile but the camera's, so w <= 2^22).
2. `lo = c - w`, `hi = c + w`, each masked with 0xFFFFFF unless exactly 2^24, then swapped so
   lo <= hi. If `hi - lo > 2^23` (the arc crosses angle 0) the arcs [0, lo] and [hi, 2^24] are
   tried, else [lo, hi]. The tile is angularly visible when one tried arc overlaps one node
   [s, e] of the list with `lo < e and hi > s` (positive overlap; touching does not count).
3. Visible and `polygonByte(x, y) != 0` -> append the tile to the output.
4. Visible and the tile itself is a blocker (§8.4) -> for each neighbour that is also a blocker,
   in the order (x+1, y), (x, y+1), (x-1, y), (x, y-1), remove one arc from the list: from
   `A(P - eye)` to `c`, where P is the midpoint of the edge shared with that neighbour:
   (x+1, y+0.5), (x+0.5, y+1), (x, y+0.5), (x+0.5, y) respectively (`0x00573470`).
5. Return `polygonByte(x, y)`.

The output can hold tiles off the map (they block, and are appended like any visible tile in the
polygon); the per-tile pass draws nothing for them, so a user of the list skips them. Built as
`PossessionVisibility` (engine-free; no adapter uses it yet).

Removing an arc (`0x00573470`): the two ends are masked as above and ordered; when they are more
than 2^23 apart the removed set is [0, lo] and [hi, 2^24] instead. The removed interval [lo, hi] is
closed: a node [s, e] with `lo > e` or `hi < s` is left alone; `lo <= s` and `hi >= e` deletes the
node; `lo <= s` alone sets its start to hi; `hi >= e` alone sets its end to lo; otherwise it splits
into [s, lo] and [hi, e].

What follows from it, each a consequence of the steps and not a separate read:

- An occluder is drawn when it is reached: its own arc is tested (step 2) before its own removal
  (step 4), so the wall that hides things is in the list.
- The wall's own hiding is not the tile's width. A solid tile removes only the half-segments from
  its centre to the midpoint of each shared edge with another solid tile: a straight wall hides
  what lies behind its centre line, a wall's end hides half a tile, a lone solid tile hides nothing.
- Tiles hidden by a nearer wall are dropped; a tile survives when any positive part of its
  widened arc is still open, so tiles beside a doorway's wedge and at grazing angles stay.
- The order is part of the rule: a tile is tested against the arcs removed by tiles processed
  before it, which includes the tiles of its own ring on earlier sides. A tile at a wall's corner can
  be listed because the wall tile that would cover it sits on a later side of the same ring (a
  prototype of these steps, not DK2, showed this at a grazing 45 degree angle).
- The enumeration is not bounded by the angles; only the ring flags are (a hidden tile in the
  polygon keeps its side alive). It costs the polygon's area every frame.

### 8.4 What blocks (STRONGLY INFERRED)

`Test` reads cache entry `+0x18` bit 0 of (x, y) and of the neighbours (`0x00571B00` finds or makes
the entry). A new entry gets the bit from `0x0059A570`: world slot `+0x1FC` (`0x00509090`, tile
inside the map) false gives 1; otherwise the result of slot `+0x208` (`0x00509200`), which is bit 3
of the navigation byte at the tile. So a blocker is **off the map, or a tile whose navigation byte
has bit 3: terrain with the Solid flag** (writer `0x0044FC40`, [structure-blocks.md](structure-blocks.md)
§5.4). Not blockers: doors (open or closed, their byte never has bit 3), pillars (cell bit
`0x4000`), water, lava, rooms of any height, objects, creatures. The bit is re-read when a dirty
entry is rebuilt (`0x005722A0`, `0x005723D0`); a terrain change dirties the 3 x 3 around the tile
(HYPOTHESIS for the chain, [terrain-lattice.md](terrain-lattice.md) §7), so the set follows digging
within a frame.

### 8.5 Who reads the list (STRONGLY INFERRED)

The list is the chain from `0x00760B14` through entry `+0x24`, each entry with flag 8. The xrefs
of the head find three readers, `0x005725D0`, `0x00572CF0` and `Lighting_GatherTileSources`
(`0x00576237`); flag 8 itself is read by `0x005725D0` and `0x005723D0` and cleared by `0x00572CF0`
(the other readers of `+0x18` were not scanned). So it limits **everything the tile cache draws**: the ceiling and terrain pieces, the
liquids, and through the per-tile pass `0x0043CC30` (view slot `+0x134`, reached from
`0x005723D0` by `0x0059A630`) every creature, object and effect element on a listed tile, drawn
without the fog test in possession. `0x005725D0` also visits the tiles up to 3 around each listed
tile (a side stops at its neighbour when that is listed); for those unlisted tiles `0x005723D0`
passes the pass a non-zero third argument, which takes an arm that walks only the cell's grid
objects (things with `+0xE4` set and `+0xF4` = 2) through sink slot `+0xE8` and never reaches the
creature slot `+0xEC` or the terrain slots; it reads as light submission (HYPOTHESIS), so a
torch just outside the picture still lights it. The order of the output is the walk order; no sort
was found.

### 8.6 The recipe for the remake

Inputs: eye (x, y), the pose and viewport (for the polygon), V in tiles, `blocked(x, y)`.
Order: (1) polygon = `BuildPolygon(pose, viewport, min(V, 32), scaleViewVolume: true)`, and the
in-polygon set of its `EnumerateTiles`, as a lookup; (2) the ring walk of §8.3 with an interval
list of integers, the angle function and the steps in the order given; (3) draw terrain, ceiling,
liquids, creatures, objects and effects only for the output tiles. In the flights in and out use
the whole polygon from step 1. The field of view enters only through the polygon (hx, hy, f).
Pin hx to the viewport/focal pair the picture is rendered with (the remake's 1600 baseline gives
hx = 2.5); DK2's own depends on the window width.

### 8.7 Not read

Mode 11's view distance; whether any shipped level's camera records change the possession
preset; the call chain that dirties an entry on a terrain change (§8.4); the arm of `0x0043CC30`
for unlisted tiles beyond the sink call it makes; draws outside the tile cache (the 3D HUD and
whatever else does not go through `0x0043CC30`); `0x00572E00` against `0x005735A0` at
instruction level; the exact floating-point rounding of the angles (the native keeps the offsets
in 80-bit registers and the edge angles through 32-bit stores), so a tile whose arc touches an
interval end to within a unit of the 2^24 angle may differ. No capture of the original's tile list
exists to check the recipe against (HYPOTHESIS that it reproduces the picture; each step is read).
