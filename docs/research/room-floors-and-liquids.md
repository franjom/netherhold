# Format and engine: room floors, liquid beds and the liquid surface (Q71)

**Grounded creature placement (2026-10-12).** The classic view evaluates the
liquid bank at the body's sub-tile position and retains both tick heights for
interpolation with horizontal movement. Bridges use 0x1199; ordinary floors
use 0x1000. Flying creatures retain their existing height (steering-behaviours.md
§7.5); attached victims retain their device pose. Full creature vertical physics
remains unbuilt.

**CONFIRMED (binary, `0x004545C0`, water/lava arm; instructions rechecked
2026-10-12):** the liquid floor starts at 0xA66. The same-liquid neighbour mask
includes bridges over that liquid and off-map cells (`0x00450470`). The bearing
from tile centre selects an octant; its even pair selects a rotated N/NE/E
three-bit group. With no dry neighbour in that group, the floor stays 0xA66.
For even octants, a dry cardinal bit selects max(abs(dx),abs(dy)); otherwise min.
For odd octants the next cardinal bit selects max; otherwise min. Add
distance*0x59A/0x800, truncated, reaching 0x1000 at the bank edge. The liquid
surface is near 0xE66, so feet stay submerged in open water. The earlier flat
centre projection omitted this rise; Unity also held destination height while
interpolating only X/Y.

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra (pyghidra-mcp), 2026-10-09, checked against the
  disassembly where the decompiler mis-tracks registers
- The shipped `Data/editor/Rooms.kwd`, `Terrain.kwd` and the names in `Meshes.WAD`

Original version: DK2 v1.70 (GOG)

Presentation only. Split from `floor-and-terrain-pieces.md` (its §4 lists the builders' arms; its
§5 the plain terrain), whose conventions and labels this note uses. Built in
`src/DK2.Presentation/WorldView.cs` and `unity/DK2.Unity/Assets/DK2/LiquidSurfaces.cs` (classic
look, plan step 2; the 2×2 and 3×1 hero gates' floors, §7, step 3).

Coordinates: (dx, dy) grow east and south, as the direction table does (N = (0, −1)).

## 1. The writer of a room tile's byte +0xC: 0x004EDC10

`0x00450470` hands every room tile (cell word `+2 & 0xFFF` non-zero) to `0x004EDC10`. It does
nothing off the map, otherwise switches on the definition's `+0x452`. CONFIRMED (whole decompile;
the selector-8 polarity checked at `0x004EDD42`).

| Sel | Writer | Byte +0xC |
|---|---|---|
| 1, 10 | inline | 0 |
| 2 Quad, 5 Normal, 7 DoubleQuad | `0x004EFCE0`, `0x004EFF00`, `0x004EFDE0` | the kind mask below |
| 3 3×3 | `0x004EF730` | cx + 3·cy, or left unchanged |
| 4 3×3 rotated | `0x004EF980` | piece << 4 \| rot, from table `0x0066DB98` |
| 6 | none | left unchanged |
| 8 Heart | `0x004EFBE0`, or `0x004EFC60` when room `+0x9A` bit 0 | 5×5 table by offset from the first slab |
| 9 Hero Gate Final | `0x004EFFE0` | 3×6 table `0x0066DCC8`; outside it 0x10 |
| 11 Hero Gate 2×2 | `0x004F0070` | (dx + 2·dy) << 4, rot 0 (table `0x0066DD88`); outside 0x10 |
| 12 Hero Gate FrontEnd | `0x004F02D0` | 3×6 table `0x0066DD28`: piece dx + 3·dy for dy 0..4, row 5 = 0xF0, 15, 0xF0; outside 0x10 |
| 13 Hero Gate 3×1 | `0x004F00F0` | k << 4 \| (o + 1) & 3, k in 3..5 (below) |

**The kind mask** (Quad, Normal, DoubleQuad). CONFIRMED in the disassembly of `0x004EFF00`
(`0x004EFF48..0x004EFFBA`); the other two decompile to the same loop. Bit k (direction k) is set
when the neighbour is on the map (world vt `+0x1FC`), its terrain byte (cell `+0xA`) equals this
tile's terrain id (definition `+0x1C4` from vt `+0x6C`), and its owner (cell `+8 & 0xFFF`)
equals this tile's. **Off the map counts as 0.** The test is room type and owner, **not the
room instance**: two rooms of one type and owner touching only at a corner see each other's tile
(`Room_MergeNeighbours` joins orthogonal neighbours only). The DoubleQuad writer then calls
`NavCache_SetTile(x, y, 4)` (`0x00454FD0`) on a tile `Room_IsFloorSlot` accepts, and
`0x004EDC10` posts bridge command 6 for a rectangle around the tile.

**3×3, `0x004EF730`.** CONFIRMED (disassembly read whole). If any of the four orthogonal
neighbours is off the map, nothing is written. Otherwise, with "same" = room word non-zero, equal
to this tile's and the same room thing (`0x004559B0`):
cx = 0 if W is not same, else 1 if E is same, else 2; cy likewise from N and S. The byte is
cx + 3·cy (table `0x0066DB88` is the identity). A 3×3 Portal therefore gets
**code = dx + 3·dy** from its NW tile, drawn as PORTAL\<code\> at rotation 0. A wider room repeats
the middle column (1); a 1-wide room gives 0, a 2-wide 0 then 2. Selector 4 indexes table
`0x0066DB98` (5-byte entries) with the same cx + 3·cy: corners piece 0 (NW rot 0, NE 1, SE 2,
SW 3), edges piece 1 (N 0, E 1, S 2, W 3), centre piece 2 rot 0. No shipped room uses selector 4.

**Heart, `0x004EFBE0` / `0x004EFC60`.** CONFIRMED (decompile clean, tables read).
dx = x − room `+0x2D`, dy = y − room `+0x2E` (the first slab, the NW tile of a placed 5×5:
`0x004DA150` takes the inner 3×3 as first slab + 1..3). Outside 0..4 nothing is written. The byte
is piece << 4 | rot from table `0x0066DBC8` (normal) or `0x0066DC48` (destroyed), entry
(dx + 5·dy), 5 bytes each. (piece, rot) by row dy, columns dx = 0..4:

| dy | normal | destroyed |
|---|---|---|
| 0 | (3,3) (2,3) (2,3) (2,3) (3,0) | same |
| 1 | (2,2) (0,0) (1,0) (0,1) (2,0) | (2,2) **(5,0)** (1,0) (0,1) (2,0) |
| 2 | (2,2) (1,3) (4,0) (1,1) (2,0) | same |
| 3 | (2,2) (0,3) (1,2) (0,2) (2,0) | same |
| 4 | (3,2) (2,1) (2,1) (2,1) (3,1) | same |

So the outer ring is piece 3 at the corners and 2 on the edges, the inner ring piece 0 at the
corners and 1 on the edges, and the centre piece 4. The outer ring's rotation runs one step behind
the inner's (outer N edge 3, inner N edge 0).

**Hero Gate Final, selector 9.** Table `0x0066DCC8`, (dx + 3·dy), dy 0..5: columns dx 0 and 2 are
(2,0) and (2,2) for dy 0..4; the middle is (0,0) at dy 0 and (1,0) at dy 1..4; row 5 is 0xF0, (8,0),
0xF0. **Hero Gate 3×1, selector 13** (CONFIRMED, §7): o = room `+0x9B` (8 = none, nothing
written). Along x for o = 0 and 2, along y for 1 and 3, with "gate" = a tile of type 0x17 and this
owner: o = 0 or 1, the tile two back is gate → k = 3, else one back → 4, else 5; o = 2 or 3 the
reverse (two back → 5, one back → 4, else 3). This settles `wall-pieces.md` §3: the face piece
k − 3 is 0, 1 or 2.

## 2. The whole-tile builders

All four put one entry in the **whole** array (list `+0x1B4`, count `+0x2E8`), with the tint from
the room terrain's colour when it has AmbientLight. CONFIRMED (decompiles that do not depend on
mis-tracked registers; `0x004463B0` checked in the disassembly).

- `0x00446140` (3×3): piece = the whole byte, handle definition `+0xD0 + byte·0x1B`, rot 0.
- `0x00446210` (3×3 rotated): piece = high nibble, rot = low nibble.
- `0x004462E0` (Heart): piece = high nibble, rot = low nibble; **piece 4 adds nothing**. So the
  living Heart is DUNGEON0..3 only, laid as the table above, with the centre left to the Heart
  object.
- `0x004463B0` (destroyed Heart): piece n = high nibble, rot = low nibble. n ≠ 5 draws
  Dungeon_Destroyed n (`+0x280 + n·0x1B`), **including the centre n = 4**. n = 5 (only at (1, 1))
  draws DUNGEON0 (`+0xD0`) when room `+0x63` is 0, else Dungeon_Destroyed5 (`+0x307`). What
  `+0x63` holds is UNKNOWN.

**Quad, `0x00443910`** (quarter array). CONFIRMED (disassembly `0x00443950..0x00443F03`).
Quadrant q takes (first, diagonal, last) of byte +0xC as bits 2, 1, 0, like the Normal floor; T is
`0x0066DDA0`, or `0x0066DE60` for type 0x11 (Stone Bridge), which differs only in entries 4 and 6:
(0, 2) instead of (0, 0). Rotation T.rot + q, quadrant q. A Wooden Bridge (type 8) whose cell word
`+8` has bit 0x1000 (the bridge is over lava, below) gives every quarter `+0x11` from
`0x00441990` with 0 or 0x1000 over 0x2000, chosen by cell `+0xB & 0x1E` below or from 8: two
looks, LIKELY its burning; their meaning is UNKNOWN. Then, for types 8, 0x11 and 0x16, the bed:
bit k when the neighbour is on the map and either its terrain has Water (`+0x17B` 0x80; Lava
0x100 for a lava bridge), or it is a bridge tile over the same liquid (same cell `+8` bit 0x1000).
Off the map counts as 0. The bed terrain is Water (world vt `+0x2BC`) or, with bit 0x1000, Lava
(vt `+0x2C0`), passed to `0x0043E440`.

## 3. The water and lava bed: 0x0043E440

Arguments (x, y, terrain, list, mask); CONFIRMED (disassembly of the entry and table loop, the
rest a clean decompile). Callers: `0x0043E860` at `0x0043FE2B` for plain Water and Lava terrain
with the tile byte +0xC, and the Quad builder above. For ConstructionTypeWater terrain
`0x00450470` sets bit k when the neighbour is off the map, has the same terrain id, or is a room
tile whose definition `+0x43D` has bit 0 or 1 (placeable on water or lava) and whose cell `+8`
bit 0x1000 matches (clear for water, set for lava) (LIKELY, decompile).

```
if terrain is Water (no 0x100) and TranslucentWater == 0 and mask == 0xFF: draw nothing
for i in 0..12: if mask & CARE[i] == WANT[i]:         # 0x0066CC30, first match wins
    whole += { BED b[i] (terrain +0x97 + b*0x1B), rot r[i] }   # 0x0066DEC8; return
for q in 0..3:                                        # no match: four quarters
    i = bit(first)<<2 | bit(diag)<<1 | bit(last)       # q0 = W,NW,N ... as everywhere
    quarter += { BED Q[i].b, rot (Q[i].r + q) & 3, quadrant q }
```

The whole-tile cases ("+" = liquid-like neighbour, "−" = not, others ignored):

| i | Needs | Piece |
|---|---|---|
| 0..3 | +N +NE +E −S −W, then turned: −N +E +SE +S −W; −N −E +S +SW +W; +N −E −S +W +NW | BED1 rot 0, 1, 2, 3 |
| 4..7 | all + except NE, SE, SW, NW respectively | BED2 rot 0, 1, 2, 3 |
| 8..11 | one side − (S, W, E, N) with its other three sides and the two far diagonals + | BED0 rot 0, 1, 3, 2 |
| 12 | all eight + | BED3 rot 0 |

The quarter table Q (`0x0066DEA0`): 0:(5,1) 1:(4,1) 2:(5,1) 3:(4,1) 4:(4,2) 5:(6,3) 6:(4,2)
7:(7,2). `+0x11` is 0 (no animation value). `0x00759AD8` is the **Translucent Water** option
(§6): off, open water (mask 0xFF) draws no bed; on, it draws BED3. Lava always draws its bed.

## 4. DoubleQuad in full: 0x00443F20

CONFIRMED in the disassembly: `0x00443F20..0x0044425F` (dispatch, door tests, inner mask, the
first quadrant) and the later quadrants' offsets (q1 inner +1 at `0x004443A0`, outer +3 at
`0x00444453`; q2 `0x00444553`, `0x00444638`; q3 `0x00444734`, `0x004447D2`).

- **Prison (11):** `0x004E3270` returns room `+0x51`, `+0x55`: the door tile (DoorTileX/Y,
  `jail-break.md` §1: the tile north of the first floor tile whose three northern neighbours are
  off the floor). On that tile: one quarter entry, PRISON14, rot 0, quadrant 0, `+0x11` 0, and
  nothing more. Elsewhere the ring quarters' `+0x11` comes from `0x00441990` with
  (PrisonJailBroken `+0x4D` ≠ 0) << 12 over 0x2000.
- **Combat Pit (16):** `0x004D7160` returns `+0x4B`, `+0x4F`, the entry tile (`combat-pit.md` §7,
  found by the same rule); CombatPit14 there, as above.
- **Temple (13):** sets presentation-grid bit 0x4000 and has **no piece-14 tile**; TEMPLE14 is loaded
  but this builder never draws it.
- **Interior** (+0xC = 0xFF): piece 13 four times, rot q, quadrant q.
- **Otherwise**, an inner mask: bit k when neighbour k has +0xC = 0xFF, terrain byte = definition
  `+0x44E` and the same owner. Quadrant q: i = (first, diagonal, last) of the inner mask; i ≠ 0
  takes T[i] at rot T.rot + q; i = 0 takes j from the inverted +0xC and T[8 + j] at rot
  T.rot + q + 2. Every rotation is taken & 3. T (0x0066DDE0, piece, rot): 0:(13,2) 1:(10,2)
  2:(11,1) 3:(10,2) 4:(10,1) 5:(12,3) 6:(10,1) 7:(12,3) | 8:(3,2) 9:(0,2) 10:(2,1) 11:(0,2)
  12:(0,1) 13:(3,0) 14:(0,1) 15:(1,1).

**Where the bars stand, and who crosses them** (2026-10-10). The bars are in the meshes: PRISON10's
bar planes (flags 3, `prisonbars1/2`) are a vertical sheet on the quarter's edge (model y = +0.25),
floor to one tile up (z −1..−2); PRISON11/12 add posts 1.5–1.63 tiles tall at a corner; PRISON14's
gate is a sheet across a whole tile at its far edge (y ≈ 0.75 from quadrant 0's centre). So the
inner-ring quarters of **rim** tiles fence the interior along the rim/interior tile boundary, and the
gate stands on the door tile's edge toward the cell. There are no bar objects and no wall pieces
inside. Navigation: `0x004EFDE0` (selector 7) writes nav byte **4** on every tile Room_IsFloorSlot
accepts, the interior (`+0xC` = 0xFF) of a Prison, Temple or Combat Pit; rim and door tiles keep
their terrain byte (0). The cost function `0x004C8BD0` returns 0 when bit 2 differs between the two
tiles unless `[0x006EC9D8]` (state option bit 2) is set. So everyone walks the rim and the door tile;
only a state passing option bit 2 enters or leaves the interior. `WorldSteering` writes these
interiors using the full eight-neighbour floor test. Delivery uses 0xE4 for the inner leg,
release 121 uses 0x6C; pit waiting 139 uses 0xE8 and Temple summoned 106 uses 0x80
(see travel-state-call-sites.md). The delivery's separate door approach remains unmodelled.
Level 8's Prison (15..19, 20..23; interior (16..17, 21..22) and (18,22)) draws
the table's pieces as DK2 does.

## 5. The wall decoration phase: room +0x92 and +0x96

CONFIRMED (disassembly `0x004ECEB0..0x004ED189`, `0x004544B1..0x00454507`, `0x0050BB63..`).

- `Room_Construct` sets both to −1 (`0x004E3942`).
- `0x004ECEB0` sets them. Bridges (types 8, 0x11, 0x16) get 0 and 0. Otherwise, while either is
  negative, it walks the slab chain from the first slab (next at cell `+4`) and tests each side s
  (N, E, S, W) for a straight wall: the outside neighbour AllowRoomWalls, the two in-room
  neighbours (d − 2, d + 2) in this room, and both outside diagonals nav-solid (vt `+0x208`) and
  AllowRoomWalls. The first hit on an N or S side sets `+0x92` = x & 1; on an E or W side `+0x96` =
  1 − (y & 1); a phase already set is kept.
- Callers: the tile add `0x004ED1A0` (before `Room_Rebuild`), `Room_RemoveTile`,
  `Room_MergeNeighbours`, `0x004ECDB0`, and the level start `0x0050B5B0`, which afterwards sets a
  still-negative phase to 0. The wall-slot test `0x00454230` sets the same values lazily, by the
  same formulas, and accepts a slot when (x + `+0x92`) is even or (y + `+0x96`) is odd.
- So the wall chooser's piece 8 lands on the tiles whose x (N, S faces) or y (E, W faces) has the
  parity of the first straight-wall tile in chain order, the same parity as the wall-furniture
  slots. **It does not depend on wall furniture**: every non-bridge room gets a phase once it has
  a straight wall, so a Lair or Treasury shows piece 8 too. −1 means no straight wall found yet
  (a room built in play), and a level-placed room without one has 0.

## 6. The liquid surface

**Who draws it** (CONFIRMED, disassembly `0x0043D1C5..0x0043D394` in the per-tile pass
`0x0043CC30`, through the visibility gate below). The terrain is
the tile's own, except that a bridge tile (8, 0x11, 0x16) **over water** uses the Water terrain; a
bridge over lava keeps its own, so **no lava surface is drawn under a bridge** (only the bed).
Water (0x80) calls renderer slot `+0xF8` when art 2's flags have bit 0 (Water: 0x20005) or
Translucent Water is off; Lava (0x100) calls `+0xFC` when bit 0 is set (Lava0: 0x20001). Open
water (mask 0xFF) and shore tiles alike. A Temple tile (grid bit 0x4000) calls `+0x100` only when
room definition `+0x261` bit 0 is set (never: loaded with 0x20000) or Translucent Water is off.
The renderer vtable is `0x00670574`: `+0xF8` = `0x005A2570`, `+0xFC` = `0x005A2BE0`, `+0x100` =
`0x005A2F10`. Every call passes colour (0, 0, 0, 255).

**Fog gate, rechecked 2026-10-24.** CONFIRMED in disassembly
`0x0043D187..0x0043D1BF`: liquid drawing is admitted by explored (`tile+9 & 0x20`),
Sight (world slot `+0xDC`), possession camera (`0x0044DB70`), or the conjunction
of terrain flag `0x01000000` and scanned (`tile+0xB & 0x40`). Both shipped Water
and Lava carry that terrain flag. An unscanned, unexplored liquid is hidden;
a scanned liquid can draw without exploration. `0x0043E320` independently
substitutes terrain 27 only for the former, retaining real terrain for the latter.
`FogOfWarViewTests.LiquidVisibilityDistinguishesUnscannedFogFromScannedTerrain`
checks both liquids against these states. This is a binary rule, not a matched
original-game visual comparison. Its Level2 shipped-map test also checks every
Water tile before and after scanning. The owner's Level2 screenshot reports visible
water/black tile shapes under fog; its tiles' scan bits and native appearance
have not been captured (Q643). Hiding every unexplored liquid would contradict
the checked gate and is not implemented.

**Textures.** The loader `0x004F66E0` (`0x004F73C2..0x004F7719`) gives Water and Lava, besides
art 2 at `+0x36`, a copy "\<art2\>SOLID" at `+0x160` as type 1 with art 2's flags and, for flag
bit 2, the frame count at file art `+0x4C` (32 for Water). With Translucent Water **off** the
surface is WaterSOLID / Lava0SOLID (`+0x160`); **on**, water uses "Water" (`+0x36`, type 2). The
cache holds Water0..31, WaterSOLID0..31, Lava0 and Lava0SOLID: water animates over 32 frames (its
frame rate is UNKNOWN), lava does not. The Temple uses the room's "Water" (`+0xA6` as type 1).

**Geometry** (CONFIRMED in the three drawers; render command `0x005873A0`). Per tile a 3 × 3
vertex grid (2 × 2 cells; n = 2 samples per tile, set by `0x00447AF3`) from (x, y) to
(x + 1, y + 1), heights and normals per vertex, alpha argument 0.7. Heights are in tile units with
up = −z, so −0.9 is 0.1 tile below the floor (DK2 z 0xE66) (STRONGLY INFERRED: the beds hang
0.35 tile below the floor). j = vertex index 0..2 along an axis, t = the renderer's frame counter
(`+0xC9`, +1 per frame at `0x0059921D`); the wave is the sum of a row and a column term:

| Surface | Height | Phase per vertex | Period |
|---|---|---|---|
| Water, Sine Wave Water on | −0.9 − 0.03·(sin a_row + sin a_col) | a = ((tile·0x10000 + j·0x8000 + 4096·t) & 0x1FFFF)·π/0x10000 | 2 tiles, 32 frames |
| Water, Sine Wave off | −0.9 − h/131072, h the ripple field (±4096) | — | — |
| Lava | −0.85 + 0.04·(sin b_row + sin b_col) | b = ((j·32 − tile·64 + t) & 0x7F)·π/64 | 2 tiles, 128 frames |
| Temple pool | −0.85 + 0.04·(…) | c = ((j·8 − tile·16 + t) & 0x1F)·π/16 | 2 tiles, 32 frames |

The vertex normal is normalize(cos col, −3, cos row). Ripple water with bump support (renderer
flag `0x00764BB8` bit 0x10) uses renderer `+0x72` (BumpWater) at alpha 1.0 instead; lava with it
adds flag 0x8000. The ripple field (`0x00448C20`, `0x00449140`, `0x00448D80`) is 64 × 64 cells
centred on the camera, blocked where the terrain lacks Water; at most every 50 ms each open cell's
second buffer gains (mean of its four neighbours − its height), clamped to ±4096, and the heights
are then rebuilt through a float step (constant unread). It runs
only with Sine Wave Water off (`0x0043BF7B`); what disturbs it is UNKNOWN. The options are the
settings object `0x00759A78`: `+0x60` Translucent Water (`0x00759AD8`, setter `0x005664A0`) and
`+0x68` Sine Wave Water (`0x00759AE0`, setter `0x005668F0`); their defaults follow a machine-speed
tier (`0x00563FB0`, LIKELY). How the command blends (beyond the 0.7 argument) is UNKNOWN: its
drawer (vtable `0x0066FDDC`) is unread.

## 7. The 2×2 and 3×1 hero gates

**The builder, `0x00446800`** (selectors 11 and 13). CONFIRMED (clean decompile). One whole
entry: piece n = high nibble of byte +0xC, rotation = low nibble, handle definition `+0xD0 + n·0x1B`,
or `+0x280 + n·0x1B` (the loader's destroyed set, `wall-pieces.md` §1) when room `+0x9A` bit 0
is set; tinted like §2. Selector 13 then runs the wall chooser's type 0x17 arm (`wall-pieces.md` §3).

**2×2.** Table `0x0066DD88` (read): entry i is (i, 0), so the gate is Hero_Gate0 NW, 1 NE, 2 SW,
3 SE, unturned, by offset from the first slab. The unused Mercenary Gate is selector 11 too, but its
art 2 is empty.

**3×1: where o comes from.** `Room_Construct` sets `+0x9B` = 8 (`0x004E38F4`, `0x004E3954`); the
tile add `0x004ED1A0` writes its argument unless 8. For a level's gate, the THINGS loader's room
record arm (`0x00521420`, disassembly read) takes a record whose terrain byte (file +18) is 0x25 and
whose player (+19) is non-zero, on a room tile, and switches on its direction (+14) through
`0x005215CC`: 0, 2, 4, 6 set o = 0, 1, 2, 3; an odd one leaves o. It then rewrites every tile code
along the slab chain (`0x004EF6C0` calls `0x004EDC10` per slab). The same arm sends terrain 0xE to
the Heart's builder `0x004DB700` and 0xC to `0x004E5300` by health percentage.

**3×1: the code.** The writer was checked whole in the disassembly (`0x004F00F0..0x004F02B2`):
"gate" is the tile from world vt `+0x1D8`(x, y) passing `Room_TileIsOwnedType`(0x17, room owner
`+0x1D`, 0). So the floor draws 3Tile_Hero_Gate k (3..5) at (o + 1) & 3, and the face side
(o − 2) & 3 draws 3Tile_Hero_Gate k − 3.

**The shipped gates.** All 41 shipped 3×1 gates have one room record each, at the middle tile, with an
even direction; each middle tile has exactly one solid orthogonal neighbour (rock), and it is the face
side. Level 8's gate (32..34, 3) has direction 4: o = 2, k = 3, 4, 5 west to east, rotation 3, face
north. The five gate definitions list no objects, and no object stands on Level 8's gates.

**Built, and not.** `WorldView` draws both floors. It infers o from the rock side, since the world
keeps no room record (tested against all 41 records). `RoomWalls` draws the 3×1 face pieces 0–2
and side piece 6 with their individual rotations. Ruined Hearts use the destroyed table and
asset keys, including centre piece 4; piece 5 uses room +0x63's zero constructor default
(`0x004E3800`), whose later writers and meaning remain UNKNOWN.
Selector12 FrontEnd floors and the entrance cap are built; its sixteen resources are
1-based (`004EACC0.md` in the function records). Selectors9/10 and destroyed gate sets remain unbuilt. Whether any code places an
object on a gate was not searched.

---
