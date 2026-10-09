# Mechanic: the terrain lattice, DK2's runtime bending of walls and floors (Q72)

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra (pyghidra-mcp and the headless runner),
  2026-10-09; register flow checked in the disassembly wherever the decompiler lost
  `ECX` or the stack offsets (the per-layer loops of `0x005A3590`, `0x005A37F0`,
  `0x0059E480`, `0x0059E750`)
- The shipped `Data/editor/Terrain.kwd` (41 records of 552 bytes), dumped for the
  wibble and lean bytes, 2026-10-09

Original version: DK2 v1.70 (GOG)

Presentation only, and not built. Nothing here changes a rule; it is how the view
bends the straight authored pieces (`wall-rendering.md`, `floor-and-terrain-pieces.md`)
into DK2's uneven walls. Facts in our own words; no decompiler output is reproduced
(ADR-0046). Every name below is ours.

Labels: **CONFIRMED** (read in the code, the data flow traced to the store),
**STRONGLY INFERRED** (read, one link resting on an earlier note or an unread
callee's name), **LIKELY**, **UNKNOWN**.

---

## 1. Where the lattice sits in the draw

- The terrain renderer `0x0043C310` hands each piece to the render sink, the static
  object at `0x00793388` (vtable `0x00670574`, built by `0x00598270`): whole pieces
  through `0x0059A050`, quarter pieces through `0x0059A200`. **CONFIRMED.**
- A whole piece is placed at **(x + 0.5, y + 0.5, 0)** in tile units (the code
  subtracts −0.5, so the sign is plus). A quarter piece is placed at (x, y) plus the
  quadrant offset from the table at `0x007956D0` (12 bytes a quadrant, filled by
  `0x005981F0`): q0 (0.25, 0.25, 0), q1 (0.75, 0.25, 0), q2 (0.75, 0.75, 0),
  q3 (0.25, 0.75, 0). The piece's scale is its integer scale word / 4096. The rotation
  is one of four 3×3 float matrices at `0x00795640` + rot × 0x24 (filled by
  `0x00598080`), stored as nine floats m0..m8 in this order:
  rot 0 (1,0,0, 0,1,0, 0,0,1); rot 1 (0,1,0, −1,0,0, 0,0,1);
  rot 2 (−1,0,0, 0,−1,0, 0,0,1); rot 3 (0,−1,0, 1,0,0, 0,0,1). Whether m is applied
  to row or column vectors was not read. **CONFIRMED** (values); multiplication
  convention **UNKNOWN**.
- Both queue a draw item through `0x00585F90`; the mesh instance `0x0057AD20`
  transforms every vertex position by the piece's matrix and placement and then
  passes it through the lattice sampler `0x0058F030` (`ECX` = the lattice global
  `0x00781088`). Normals are transformed in a second loop that does not touch the
  lattice. **CONFIRMED** (position); normals **LIKELY**.
- The deformed geometry is built once per tile and cached (§7), not per frame.
  **CONFIRMED.**

## 2. The lattice and its sampling

The lattice object at `0x00781088` holds three words: a pointer to a 3×3 table of
point blocks (stride 4 pointers a column of tiles, so block (tx, ty) is entry
tx·4 + ty, tx, ty ∈ 0..2), and the window origin x and y as floats. `0x0058EF60`
sets them. **CONFIRMED.**

A point block belongs to one tile and holds **20 points** (x, y, z floats, 240 bytes):
2 columns in x × 2 in y × 5 levels in z, point index i·10 + j·5 + k, i, j ∈ {0, 1}
(half-tile column inside the tile), k ∈ 0..4 (level). **CONFIRMED.**

The window for a tile (x0, y0) is the 3×3 tiles around it, origin (x0 − 1, y0 − 1),
so it is a grid of 6 × 6 columns at half-tile spacing and 5 levels: 5 × 5 × 4 cells.

Sampling a position p (`0x0058F030`):

- u = 2·(p.x − origin.x), v = 2·(p.y − origin.y), w = 2·(p.z + 2.5).
- Cell indices are the floors of u, v, w (computed with the 1.5·2²³ float trick,
  which rounds u + 0.50001 and subtracts one; it equals floor except within 1e-5 of
  the next integer). Fractions fu, fv, fw are the remainders.
- If the cell is outside 0 ≤ iu < 5, 0 ≤ iv < 5, 0 ≤ iw < 4, p is returned unchanged.
- Otherwise the result is the **trilinear blend of the cell's 8 lattice points**.
  The points are absolute positions, so an undisturbed lattice returns p.

The corner lookup table at `0x00780E78` (8 bytes per (iu, iv), index iv + iu·8,
built by `0x0058EE60`): bytes 0..3 are the blocks of the four corners (iu, iv),
(iu+1, iv), (iu, iv+1), (iu+1, iv+1), each (column ÷ 2)·4 + (row ÷ 2); bytes 4..7
the point offsets inside them, ((row & 1) + (column & 1)·2)·5. Level k is then added.
**CONFIRMED.**

So every lattice point is a half-tile column (X2, Y2) = (2x + i, 2y + j) at level k,
owned by tile (X2 ÷ 2, Y2 ÷ 2), and two neighbouring tiles' pieces sample the same
points: the field is continuous across tile seams. **CONFIRMED** (from the layout).

## 3. The per-tile cell and its rest positions

`0x005A3590` (called through `0x0059A5E0`, `ECX` = the sink) builds one tile's
20 points:

- **Undeformed path** when the sink's dword `+0xC5` is 0 or the game mode byte
  `0x0075AD4B` is 5: `0x0058EF80` writes the identity grid,
  point(i, j, k) = (x + i/2, y + j/2, −2.5 + k/2). **CONFIRMED.**
  `+0xC5` is set to 1 by the sink's initialiser `0x005983C0` (`0x00598616`) and no
  other store to it was found (StoreAt scan of `+0xC5`; the four other hits are in
  unrelated classes). So in a game the deformation is always on. Mode 5 is entered
  by `0x00525CD2` together with the "FrontEnd3DLevel" switch `0x0075AEAF = 1`, so it
  is very probably the 3D front end. **STRONGLY INFERRED.**
- **Deformed path**: rest positions from `0x005A3220`, then the offsets of §5 and §6
  added to levels k = 1, 2, 3; levels 0 and 4 keep their rest positions.
  **CONFIRMED.**

Rest positions of the deformed path (`0x005A3220`):
point(i, j, k) = (x + i/2, y + j/2, −2.5 + k·s + o), where

- s = 1/2, o = 0 (the identity) when any of `0x00759AE8`, `0x00759AD0`,
  `0x0075AEAF` is non-zero;
- **s = 1/4, o = 0.75 when all three are zero**: the levels sit at
  −1.75, −1.5, −1.25, −1.0, −0.75 instead of −2.5 … −0.5.

**`0x00759AD0` is the "HighWalls" display option**: field `+0x58` of the display
settings object `0x00759A78` (built by `0x00565FD0`), loaded by `0x00564B00` from the
config key "HighWalls" (string `0x006BDA58`; **1 when the key is absent**, `0x00565135`)
and saved back by `0x00563FB0`. It is written through the object, which is why no store
by absolute address exists. **CONFIRMED** (loader, saver, default, offset; the shipped
config was not read). `0x0075AEAF` is set by the `-FE3D` switch and the other
"FrontEnd3DLevel" launches (`0x00401600`, `0x00401660`, `0x00525CC2`) and cleared by its
sibling switch. `0x00759AE8` is a latch of the possession cameras (modes 2, 10, 11; mode
2 follows the possessed creature): `0x0044ACE0` clears it on every request, and the
commit `0x0044BD70` sets it only when entering those modes from outside them, so a
request between two of them leaves it clear; each flip invalidates every cell
(`0x00571DA0`). **CONFIRMED** for the code paths.

**So the walls stand a full tile above the floor by default**, in the keeper's view and
in possession alike. Only with HighWalls off, outside possession and the 3D front end,
do the levels rest at half spacing: in this project's renderer frame (up = −Z, pieces
spanning heights 1 to 2; `RenderMesh.ToRenderFrame`) level k sits at 2.5 − k/2 with
the identity, k = 3 the floor (1.0) is fixed either way, and the wall top drops from 2
to 1.5. The owner's Level 1 screenshot, a wall about 2.7 times an imp (0.36 tile), fits
the full height.

## 4. The tile inputs

Everything the deformation reads about the map, for a tile (x, y):

**Closed test** `0x0059ECF0(world, tx, ty)` returns 1 (closed) unless all of these
hold, and then 0 (open):
the tile is in bounds (world slot `+0x1FC`, `0x00509090`); the tile's explored bit
(`tile+8` word bit `0x2000`, `fog-reveal-sources.md`) is set; the navigation solid
bit (world slot `+0x20C`, `0x005090C0`, nav byte bit 3) is clear at (tx·0x1000,
ty·0x1000); and world slot `+0x220` (`0x005092D0` → `0x004528C0` → `0x004528E0`, the
door nibble of `imp-claim-path.md` §4) returns 0 at that position. **CONFIRMED** for
the calls; that `+0x220` is the door test is **STRONGLY INFERRED** from the earlier
note's reading of `0x004528E0`. An unexplored tile counts as closed.

`0x005A3590` reads the 3×3 tiles around (x, y) into s(dx, dy), dx, dy ∈ −1..1
(stored row-major, rows of four dwords; the fourth is unused). Below, NW = s(−1,−1),
N = s(0,−1), NE = s(1,−1), W = s(−1,0), C = s(0,0), E = s(1,0), SW = s(−1,1),
S = s(0,1), SE = s(1,1).

**Tile parameters** `0x0059E210`, per tile of the same 3×3 and per layer L ∈ 0..2
(L = 0 is the wall top, level 1; L = 2 the floor surface, level 3), a 20-byte record:

| Field | Default | When in bounds and explored |
|---|---|---|
| +0 `Hw` (int) | variable 45 `DefaultHorizontalTerrainWibble` (`0x006F25F9`, 24576) | terrain `+0x1A1` << 7, or 0 when `tile+8` bit `0x8000` is set |
| +4 `Vw` (int) | variable 46 `DefaultVerticalTerrainWibble` (`0x006F25FD`, 12288) | terrain `+0x1A5` << 7, or 0 when bit `0x8000` is set |
| +8 `Hl` (float) | 0 | terrain `+0x1A2 + L` / 512 |
| +0xC `Vl` (float) | 0 | terrain `+0x1A6 + L` / 512 |
| +0x10 `Lift` (float) | (set later, layer 0 only) | |

The terrain record is the runtime one from the table at `0x006DA4A8`, indexed by the
tile byte `+0xA`; the eight bytes are the file's wibble H, lean H[3], wibble V and
lean V[3] at `0x1CE..0x1D5` (`kwd-terrain-rooms.md`, *the wibble and lean bytes*,
with the shipped values: Rock 255, [255,255,255], 128, [255,255,255]; Dirt Path
255, [96,96,0], 128, 0s; Claimed Path 102, [96,96,0], 13, 0s). **CONFIRMED**
(runtime offsets and their use). What tile bit `0x8000` is: **UNKNOWN**.

**Sub-point parameters** `0x0059E480`: for each layer L and each of the 3×3
sub-points (r, c) ∈ 0..2 at (x + c/2, y + r/2), the four tiles touching it are the
tiles containing the quarter positions (4x + 2c ± 1, 4y + 2r ± 1) (arithmetic shift
by 2, so floor). For c = 0 that is columns x−1 and x, for c = 1 column x twice, for
c = 2 columns x and x+1; the same for rows. Each field of the sub-point's record is
the **minimum over those four tiles** (`0x0059E3A0` for the two ints, `0x0059E400`
for the two floats, each field separately). **CONFIRMED.**

Then for layer 0 only, `Lift(r, c) = −0.15 − 0.125 · sin(((2x + c) + (2y + r)) · 0.5)`
(radians, x87 `FSIN`; constants 0.5 at `0x006706AC`, 0.125 at `0x006706F4`, −0.15 as a
double at `0x006706F8`). **CONFIRMED.**

## 5. Lean (`0x005A37F0`)

Per layer L, nine local offsets Q(r, c), all zero to start; a = `Hl` and z0 = −`Vl`
of that sub-point's record.

*Corners* (`0x0059F2F0`), each with the four tiles around it in the order
(nw, ne, sw, se): Q(0,0) with (NW, N, W, C); Q(0,2) with (N, NE, C, E); Q(2,0)
with (W, C, SW, S); Q(2,2) with (C, E, S, SE). With north = nw | ne, south =
sw | se, west = nw | sw, east = ne | se (any closed tile in the pair):

- if east ≠ west: if also north ≠ south, a = a · 1.4; x += a when west is open,
  else x −= a; z += z0, and z0 is then spent;
- if north ≠ south: y += a when north is open, else y −= a; z += z0 (if not spent).

*Edge midpoints*: Q(0,1) between N and C (`0x0059E700`): if they differ, y −= a when
N is the closed one, else y += a, and z += z0. Q(2,1) between C and S: if they
differ, y −= a when C is closed, else y += a. Q(1,0) between W and C
(`0x0059EC90`): if they differ, x −= a when W is closed, else x += a. Q(1,2) between
C and E: if they differ, x −= a when C is closed, else x += a. Each adds z0 when it
moves.

So a point on an open/closed boundary moves **into the closed side** by the lean, and
up (−z) by the vertical lean; a convex corner moves diagonally by 1.4 × the lean on
each axis. A point with no boundary (all four the same) does not move.

*Smoothing*, in this order: Q(0,1).x += (Q(0,0).x + Q(0,2).x)/2;
Q(1,0).y += (Q(0,0).y + Q(2,0).y)/2; Q(1,2).y += (Q(0,2).y + Q(2,2).y)/2;
Q(2,1).x += (Q(2,0).x + Q(2,2).x)/2; then the centre
Q(1,1).x = (Q(1,2).x + Q(1,0).x + Q(0,1).x + Q(2,1).x)/4 and
Q(1,1).y = (Q(2,1).y + Q(0,1).y + Q(1,2).y + Q(1,0).y)/4; its z stays 0.

Only Q(r, c) for r, c ∈ {0, 1} (the tile's own four columns) are added to the
offsets; the far row and column exist to feed the averages. Then the layer-0
offsets get z += `Lift(r, c)`. **CONFIRMED** (constants 1.4 at `0x00670700`,
−0.5 at `0x00670704`, 0.25 at `0x006706E4`).

The minimum over the four tiles is what makes the lean modest: at a Dirt Path/Rock
boundary the top and middle layers lean by 96/512 = 0.1875 tile (the path's value,
not the rock's 255) and the floor layer by 0. Against an unexplored tile the lean is
0 (its `Hl` is 0) while the tile still counts as closed.

## 6. Wibble (`0x0059E750`)

Per layer L and each own sub-point (r, c), r, c ∈ {0, 1}:

- seed = (2x + c) + 16·L + 0x10000·(2y + r), a fresh local per call
  (`0x005A3590` builds 48 of them, of which these 12 are read). **CONFIRMED.**
- With the game's draw `Random_Next` (`0x005558D0`: seed = seed·0x19660D +
  0x3C6EF35F, result rotr(seed, 13) mod n) on that one seed, in this order:
  dz = Vw ≠ 0 ? rand(Vw) : 0; dy = Hw ≠ 0 ? rand(Hw) − Hw/2 : 0;
  dx = Hw ≠ 0 ? rand(Hw) − Hw/2 : 0 (Hw/2 a signed integer halving; a zero
  range draws nothing). Offset += (dx, dy, dz) · 2⁻¹⁷ (`0x00670708`, the double
  7.62939453125e-6). **CONFIRMED.**
- Corner (0,0) (NW, N, W, C, the same pairs as §5): A = Hw(0,0) · 2⁻¹⁷ / 2. If
  east ≠ west: if also north ≠ south, A = A · 1.4; x += A when west is open, else
  −A. If north ≠ south: y += A when north is open, else −A (using the possibly
  scaled A). No z.
- Edge (0,1) between N and C: A = Hw(0,1) · 2⁻¹⁸, y −= A when N is closed, else
  += A, when they differ. Edge (1,0) between W and C: A = Hw(1,0) · 2⁻¹⁸, x −= A
  when W is closed, else += A, when they differ. (1,1) gets no push.

Magnitudes: Rock's wibble H 255 gives Hw = 32640, a horizontal jitter within
±0.1245 tile and a boundary push of 0.1245 (0.174 at a corner); wibble V 128 gives
Vw = 16384, z jitter in [0, 0.125). Claimed Path (102, 13) gives ±0.0498 and
[0, 0.0127). Because each field is the minimum over the four tiles around a point,
a wall point next to claimed floor jitters by the floor's amount, not the rock's.

The draws do **not** touch any game random stream: the seeds are stack locals made
from the coordinates, so the same inputs give the same numbers. The 16·L term
collides between columns 16 apart (column X2 at layer 1 has the same seed as column
X2 + 16 at layer 0, on the same row): a DK2 quirk to keep, not fix.

## 7. The cache and when a cell is rebuilt

- 64 × 64 buckets at `0x00760B90`, bucket (x & 63)·64 + (y & 63), entries of 0x160
  bytes chained through `+0x14`, keyed by the dword x | y << 16 (`0x00571B00`).
  `+4` is a frame stamp, `+0x1C`/`+0x20` an LRU list (head `0x00760B6C`, tail
  `0x00764BD8`), `+0x18` flags, **`+0x3C` the 20-point block** (null until built),
  `+0x40` the Tremor block (§8). **CONFIRMED.**
- `0x00760B10` is the frame counter, bumped once per frame by `0x00575700` (which
  also measures frames per second with `timeGetTime`). A stale stamp only moves the
  entry to the head of the LRU list; it **does not** discard the points.
  **CONFIRMED.** (When the engine heap is full, its allocator `0x0057CEB0` retries
  after calls to `0x005742F0`, unread, which may drop old entries; a dropped cell
  would be recomputed identically.)
- `0x00571F00` fills the lattice for tile (x0, y0): for each of the 3×3 tiles it gets
  the entry, builds its points when `+0x3C` is null (`0x005A3590`, committed by
  `0x00575FA0`, which copies the 240 bytes into a fresh engine-heap block), records
  the block in the tile entry's table, and sets the window to (x0 − 1, y0 − 1).
  `0x005722A0` calls it before building the tile's meshes, which it does only when the
  entry is dirty (`+0x18` bit `0x10`) or not yet built (bit 2). **CONFIRMED.**
- **Invalidation**, by commands forwarded to the sink's slots: slot `+0x28`
  (`0x00598800`) handles id 3 (the 3×3 around the tile: `0x00571E60(x−1, y−1, 3, 3)`),
  id 5 (one tile, `0x00571E00`) and id 6 (a rectangle, `0x00571E60`); slot `+0x24`
  (`0x005986F0`) handles id 0x67 (every cell, `0x00571DA0`). Each frees `+0x3C` and
  sets the dirty bit. The terrain setter `0x0044FC40` posts command 3 for the changed
  tile (`wall-torches.md` §6), so **digging or building re-derives the changed tile
  and its eight neighbours** — exactly the reach of the 3×3 inputs. The command
  ids are **CONFIRMED** in the handlers; that the terrain setter's command 3 reaches
  this sink is **STRONGLY INFERRED** (the bridge forwards every command to the next
  sink's `+0x28`, `wall-torches.md` §6). Who posts 5, 6 and 0x67 (exploration?) is
  **UNKNOWN**.
- `0x00571C50` flushes the whole cache (called from `0x00574310`).

## 8. Tremor's second lattice (not the wall shape)

Each entry also has `+0x40`, filled by `0x005A3310` (through `0x0059A5C0`) when the
bridge's slot `+0x13C` (`0x00441420`) says the tile's centre lies inside an active
Tremor (the eight 20-byte records at world `+0x9BB0`, `keeper-spell-tremor.md` §2,
distance ≈ max + min/2 below the radius). It jitters levels 1..3 of the rest grid
with draws on a **running global seed** `0x00795854`, incremented per call: two
ranges from rand(0x4000), then per point rand(b + 1)·2⁻¹⁷ in z and
(rand(a) − a/2)·2⁻¹⁷ in x and y. It sets entry flag `0x40`; `0x00572060` then gathers
the 3×3 Tremor blocks (identity from `0x0058EF80` where no Tremor) into a table at
entry `+0x58`. **CONFIRMED** for the build; how the Tremor table is used in the draw
**UNKNOWN**. This is the only non-deterministic deformation, and it is transient.

## 9. Q72: deterministic, a pure function of the map

**Answer: yes.** The wall and floor shape is a pure function of the tile's position
and its 3×3 neighbourhood's terrain, exploration, navigation solidity and doors (plus
the view's rest scale, §3). Its only randomness is the game's LCG run on seeds built
from the coordinates (§6); it reads no game random stream, no time and no frame
counter. A wall dug out and rebuilt in the same surroundings comes back with the same
bend. A neighbour's change does alter the tile (the 3×3 invalidation), and an
unexplored neighbour differs from an explored one. Tremor adds a separate, random,
transient wobble (§8). **CONFIRMED** (for the deterministic part).

For this project: a renderer can reproduce it from the map alone; the simulation
needs no field, as Q72 required. It does need the explored bit, the navigation-solid
test and door positions, which are all already in the world state.

## 10. Pseudocode (ours)

```
// Inputs: the map; terrain table; variables 45/46; view flag fullHeight
// (the HighWalls option, on by default, or the possession latch, or the 3D front end).
const float K17 = 1f / 131072f;                       // 2^-17

bool Closed(tx, ty) =
    !InBounds(tx, ty) || !Explored(tx, ty) || NavSolid(tx, ty) || HasDoor(tx, ty);

Params TileParams(tx, ty, L):
    p.Hw = Var45; p.Vw = Var46; p.Hl = 0; p.Vl = 0
    if InBounds && Explored:
        t = Terrain(tx, ty)
        if Flag8000(tx, ty): p.Hw = 0; p.Vw = 0
        else: p.Hw = t.WibbleH << 7; p.Vw = t.WibbleV << 7
        p.Hl = t.LeanH[L] / 512f; p.Vl = t.LeanV[L] / 512f
    return p

Params SubPoint(x, y, L, r, c):                        // r, c in 0..2
    xs = { (4x+2c-1) >> 2, (4x+2c+1) >> 2 }; ys = { (4y+2r-1) >> 2, (4y+2r+1) >> 2 }
    return field-wise min of TileParams over xs × ys

Vec3[20] BuildCell(x, y):
    if !deformOn || gameMode == 5: return Rest(x, y, s: 0.5, o: 0)
    (s, o) = fullHeight ? (0.5, 0) : (0.25, 0.75)
    cell = Rest(x, y, s, o)                             // (x+i/2, y+j/2, -2.5 + k*s + o)
    n = 3x3 of Closed(x+dx, y+dy)
    for L in 0..2:
        Q = Lean(x, y, L, n)                            // §5, 3x3 local offsets
        for r, c in {0,1}:
            off = Q[r, c]
            if L == 0: off.z += -0.15 - 0.125 * sin(((2x+c) + (2y+r)) * 0.5)
            off += Wibble(x, y, L, r, c, n)             // §6
            cell[c*10 + r*5 + (L+1)] += off
    return cell

Vec3 Wibble(x, y, L, r, c, n):
    p = SubPoint(x, y, L, r, c); seed = (2x+c) + 16L + 0x10000*(2y+r)
    dz = p.Vw != 0 ? Rand(ref seed, p.Vw) : 0
    dy = p.Hw != 0 ? Rand(ref seed, p.Hw) - p.Hw/2 : 0
    dx = p.Hw != 0 ? Rand(ref seed, p.Hw) - p.Hw/2 : 0
    d = (dx, dy, dz) * K17
    if (r, c) == (0, 0): d += CornerPush(p.Hw * K17 / 2, NW, N, W, C)   // x/y only
    if (r, c) == (0, 1): d.y += EdgePush(p.Hw * K17 / 2, N, C)
    if (r, c) == (1, 0): d.x += EdgePush(p.Hw * K17 / 2, W, C)
    return d

uint Rand(ref uint s, uint n) { s = s * 0x19660D + 0x3C6EF35F; return RotR(s, 13) % n; }

Vec3 Sample(p, window):                                 // window origin = (x0-1, y0-1)
    u = 2(p.x-ox); v = 2(p.y-oy); w = 2(p.z+2.5)
    (iu, iv, iw) = floor; if outside [0,5)x[0,5)x[0,4): return p
    return trilinear of the 8 lattice points around (iu, iv, iw)
```

`CornerPush` and `EdgePush` are §5's rules without z: into the closed side, ×1.4 at a
corner where both axes change, 0 where the tiles agree.

## 11. Constants and tables

| What | Where | Value |
|---|---|---|
| Lattice object | `0x00781088` | block table pointer, origin x, origin y |
| Corner tables | `0x00780E78` (by `0x0058EE60`) | 8 bytes per (iu, iv), 64 entries |
| Cache, frame stamp, current entry | `0x00760B90`, `0x00760B10`, `0x00760B80` | §7 |
| Rotations, quarter offsets | `0x00795640`, `0x007956D0` | §1 |
| Deform switch, undeformed mode, rest-scale flags | sink `+0xC5`; `0x0075AD4B` == 5; `0x00759AE8`, `0x00759AD0`, `0x0075AEAF` | §3 |
| Lattice z origin | −2.5, level step 0.5 | `0x0058F030`, `0x005A3220` |
| Half-height rest | step 0.25, offset 0.75 | `0x005A3220` |
| Default wibbles | `0x006F25F9`, `0x006F25FD` | 24576, 12288 |
| Lean scale | 1/512 | `0x006706E0` |
| Wibble scale | 2⁻¹⁷ | `0x00670708` (double), `0x00670710` |
| Corner factor | 1.4 | `0x00670700` |
| Lift | −0.15 − 0.125·sin((X2 + Y2)/2) | `0x006706F8`, `0x006706F4`, `0x006706AC` |
| LCG | ×0x19660D + 0x3C6EF35F, rotr 13 | `0x005558D0` |
| Tremor seed | `0x00795854` | running |

## 12. UNKNOWN

- The rotation matrices' multiplication convention (row or column vectors).
- What `tile+8` bit `0x8000` is (it zeroes the wibble of an explored tile).
- The shipped config's HighWalls value (1 when the key is absent, §3), and whether play
  ever requests one possession camera from another (the latch then stays clear).
- Who posts render commands 5, 6 and 0x67, in particular whether exploring a tile
  invalidates its cell (if nothing does, a cell built while a neighbour was
  unexplored would keep that shape until something else dirties it).
- How the Tremor table at entry `+0x58` enters the draw, and whether it is rebuilt per
  frame.
- `0x0059F3C0`, a sibling rest-grid builder (3 × 3 × 3 points, offset 0.5 and −0.05
  with the same flags): its caller and purpose were not read.
- The new-entry flag from `0x0059A570` (entry `+0x18` bit 0) was not read.
