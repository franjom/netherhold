# Classic terrain-light visibility

Retail v1.70 DKII.EXE, Ghidra12.1.4; bounded binary read, 2026-10-15.
Light selection and invalidation remain in [classic-lighting.md section9](classic-lighting.md#9-per-tile-terrain-point-lights).

## 1. Ordinary-view polygon boundary

CONFIRMED call sequence: renderer frame setup `0x00575A00` stores the camera
position, orientation, lens, viewport bounds and visibility distance in its
working globals, then calls `0x00574820`. When renderer flag1 is clear, its
returned polygon goes to `0x005735A0`; the alternate flag1 branch uses the
camera-ring enumeration instead. Cache preparation follows enumeration,
then light gathering, then primitive preparation.

CONFIRMED whole-body reads of `0x00574820` and its five polygon helpers,
with producer disassembly resolving the hidden vector destinations: let
`hx = viewportWidth / (2*focalPixels)`, `hy = viewportHeight / (2*focalPixels)`,
`d = viewDistance`. The camera-space point set contains four near points
`(+/-1,+/-1,-1)`, four far points `(+/-(hx*d*s+1),+/-(hy*d*s+1),d*s)` and
the far centre `(0,0,d)`. The scale `s` is1 with renderer flag8 clear and
`2/(hx+2)` when set. Each point is transformed by the transposed native
view matrix and translated by camera position. The point list prepends
each submission: near(--), near(+-), near(-+), near(++), far(--), far(+-),
far(-+), far(++), centre. Renderer flag8 is not established as camera record
flag8; adapters must supply the actual renderer mode.

CONFIRMED `0x00575220` intersects every unordered original point pair with
each height plane (lower first, then upper), accepting only strict crossings.
Intersections precede the original points; points outside the inclusive slab
are removed. The initialization call at `0x00598443` passes lower=-9,
upper=0 to `0x00574310`; its two stores are the only instruction references
that write the clipping globals. Native coordinates map to Unity as
`(x,-z,-y)`, so the slab covers Unity heights0 through9.

CONFIRMED `0x00575440` removes a point if a later point differs by less than
0.01 on both horizontal axes, ignoring height. `0x00575180` computes view-depth
limits for projection setup; it does not change this point set.
`0x005754D0` starts at the first minimum-Y point, repeatedly chooses the first
point having the smallest nonnegative angular increment, and closes on its
start. Angles are `atan2(-dy,-dx)+pi`, except a nonnegative horizontal ray
has angle0. This preserves list-order ties rather than imposing a new hull tie
rule. Each horizontal component becomes an integer by subtracting float
0.49998998641967773, adding12582912, storing float and extracting its low23
mantissa bits minus0x400000. The final store is relevant at integer boundaries;
neither truncation nor ordinary nearest-integer rounding is equivalent.

The engine-neutral implementation is `ClassicLightVisibility`; its camera
input uses actual pose/FOV/viewport and the native distance separately from a
presentation far-clip extension. Four floor-plane ray intersections are not
equivalent to the clipped volume above.

## 2. Scan conversion and visible-flag lifetime

CONFIRMED whole-body read of `0x005735A0`: polygon nodes contain integer X/Y
and links in both directions. Starting at a minimum-Y node, the routine
walks both edges, advances signed16.16 X accumulators and visits integer
scanlines in increasingY. Each nondegenerate scanline includes both X
endpoints after arithmetic right shift by16, in increasingX. A flat-Y polygon
produces no targets. The maximum-Y row is excluded when the edge walkers
reach the terminal level. The returned visible chain is linked through cache
entry `+0x24`; each visited entry receives flag8 at `+0x18`.

CONFIRMED `0x00571B00` clears flag8 when initializing a new cache entry, but
its existing-entry frame/LRU refresh does not clear flags. `0x00575700`
increments the32-bit frame counter without a wrap correction or a visible-flag
clear in its own body.

CONFIRMED clearing is later: cached disassembly
`0x00572DDF..0x00572DE8` in `0x00572CF0` clears entry `+0x18` flag mask0x08 after
preparing both primitive lists, before advancing through `+0x24`.
Thus the ordinary completed frame sequence clears the flags it set; frame
advance alone is not the reset operation.

## 3. Adjoining preparation coverage

The exact per-target rectangle bounds and source preparation are documented
in [classic-lighting.md section9](classic-lighting.md#9-per-tile-terrain-point-lights).
CONFIRMED `0x005725D0` invokes the adjoining update for every integer
coordinate in each inclusive rectangle, Y outer/X inner, without a local
map-bound clamp. Its call domain is the union of those rectangles.

This call domain does not establish every cache entry touched: target geometry
building also obtains3x3 lattice entries, and immediate-neighbor tests obtain
cache entries. It does not establish that all authored lights are prepared,
or establish that every authored light is prepared.
