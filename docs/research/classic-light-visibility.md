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

CONFIRMED whole-body read of `0x00574820`: it derives normalized side vectors
and corner ratios from viewport extents and focal length, transposes the view
matrix, transforms vectors into world space and adds camera position. It also
uses visibility distance and a renderer flag8-dependent scale. It submits
nine points to a helper and passes the result through several more helpers
before returning a polygon. This is insufficient evidence for replacing the
producer with four viewport-corner intersections against a floor plane.

UNKNOWN: the exact clipping planes, clipping sequence, point rounding and
ground/height limits used by those helpers. The next bounded query is listed
in the producer's [function record](functions/00574820.md). Do not treat
Unity camera-frustum visibility as established native source eligibility.

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
or settle the unresolved projected polygon itself (Q602).
