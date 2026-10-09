# Format and engine: the floor and plain-terrain pieces DK2 draws for a tile (Q71)

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra (pyghidra-mcp), 2026-10-06, checked against the
  disassembly where the decompiler mis-tracks registers
- The shipped `Data/editor/Rooms.kwd`, `Terrain.kwd` and the names in `Meshes.WAD`

Original version: DK2 v1.70 (GOG)

Presentation only, and not built. The companion of `wall-pieces.md`, whose conventions (the
direction table, the piece list, the labels) and room art layout this note uses; its section
numbers continue from there.

## 4. 0x00443610's arms (TileConstruction +0x452)

Arguments: (x, y, tile, terrain, room, list). CONFIRMED (whole decompile, plain
switch).

| TC | Builder | What it draws | Calls the wall chooser |
|---|---|---|---|
| 1 Complete | inline | one whole piece, handle +0x2A (art 2 itself), rot 0; no shipped room uses TC 1 | yes |
| 2 Quad (bridges) | 0x00443910 | 4 quarters from pieces 0..3 by the tile byte +0xC neighbour mask, table 0x0066DDA0 (0x0066DE60 for type 0x11); then a water/lava bed under it through 0x0043E440 | no |
| 3 3×3 (Portal, Hero Portal) | 0x00446140 | one whole piece = piece (tile byte +0xC), rot 0 | no |
| 4 3×3 rotated | 0x00446210 | piece = high nibble of +0xC, rot = low nibble | no |
| 5 Normal | 0x00444970 | see below | yes |
| 6 CenterPool | none | nothing | no |
| 7 DoubleQuad (Prison, Temple, Combat Pit) | 0x00443F20 | see below | yes |
| 8 5×5 rotated (Heart) | 0x004462E0, or 0x004463B0 when room +0x9A bit 0 (destroyed) | piece = high nibble (4 = centre, nothing drawn), rot = low nibble; the destroyed variant reads +0x280 + n·0x1B (Dungeon_Destroyed) and, for n = 5, piece 0 or +0x307 by room +0x63 | no |
| 9, 12 | 0x004464C0 | piece = high nibble, rot = low nibble; plus art 8 (+0x60, IMPEN ROCK TOP) at origin offset (1, 5) | yes (type 0x12 or 0x15 arm) |
| 10 | 0x00446640 | piece 0 on each open explored side, rot (s−2)&3, plus art 8 | no |
| 11 | 0x00446800 | piece = high nibble, or +0x280 + n·0x1B when room +0x9A bit 0; rot = low nibble | no |
| 13 | 0x00446800 then 0x00444F10 | as 11 | yes (type 0x17 arm) |

### Normal floor, 0x00444970

CONFIRMED in the decompile, which here does not depend on mis-tracked registers.

```
m = tile(x,y).byte0C                  # 8-neighbour same-room mask, bit k = DIR[k]
if m != 0xFF:                         # edge tile: four quarters
    for q in 0..3:                    # dirs (2q-2, 2q-1, 2q): q0=W,NW,N  q1=N,NE,E  q2=E,SE,S  q3=S,SW,W
        i = bit(first)<<2 | bit(diagonal)<<1 | bit(last)
        (piece, rot) = T[i]           # T = 0x0066DDA0:
                                      # 0:(1,3) 1:(0,3) 2:(1,3) 3:(0,3) 4:(0,0) 5:(2,1) 6:(0,0) 7:(3,0)
        quarter += { piece, rot = (rot + q) & 3, quadrant = q }
elif Room_IsFloorSlot(...) (0x00454110) and not (Work Shop and furnitureChar == '.'):
    whole += { piece 9, rot 0 }       # interior furniture-slot centre
else:
    for q in 0..3: quarter += { piece 3, rot = q, quadrant = q }   # plain interior
```

- Piece 0 is the edge quarter, 1 the outer corner, 2 the inner corner and 3 the
  interior quarter.
- The Work Shop test uses the same position hash as §5: two calls to 0x005558D0
  (range 0x2286 and 0x16F2, seeded from x and y), XOR, then mod 3. The result
  indexes the room's object list (definition +0x431 = file 0x39A, "10 0F 2E…").
  0x2E is object 46, which reads as '.', so a slot whose hashed object is 46
  gets no centre piece. STRONGLY INFERRED: the decompile mixes up which seed is
  x and which is y.

### DoubleQuad, 0x00443F20

LIKELY: read from the decompile. The registers that carry the definition are
mis-tracked; its offsets follow the loader.

- A centre tile (from 0x004E3270 for Prison, 0x004D7160 for Combat Pit) draws
  piece 14 whole. Prison's piece also takes an animation value from room +0x4D.
- A fully interior tile (+0xC = 0xFF) of these three types draws piece 13 four
  times.
- Otherwise each quarter takes an inner-ring piece (10, 11 or 12) when one of
  its three neighbours is interior. It takes an outer-ring piece (0..3) from
  the inverted mask otherwise. Both come from table 0x0066DDE0:
  0:(13,2) 1:(10,2) 2:(11,1) 3:(10,2) 4:(10,1) 5:(12,3) 6:(10,1) 7:(12,3) |
  8:(3,2) 9:(0,2) 10:(2,1) 11:(0,2) 12:(0,1) 13:(3,0) 14:(0,1) 15:(1,1).
- Temple sets grid bit 0x4000 on its tiles.

---

## 5. 0x0043E860: non-room terrain

Arguments: (x, y, drawnAsFog, terrain, list). drawnAsFog = 1 when 0x0043E320
substituted terrain 0x1B or 0x26 for an unexplored tile. The frame was re-derived
at 0x0043E860..0x0043E8FC.

### Terrain loader 0x004F66E0

Intermediate = file + 0xB8. CONFIRMED: the flags at file 0x190 land at
intermediate 0x248 and runtime +0x17B.

- In the general case, **art 0 → handle +0x0F, art 1 → +0x2A, art 2 → +0x45,
  art 3 → +0x60**.
- **ConstructionTypeWater (0x1000):** "<art0>0..7" go to +0x88 + k·0x1B (handles
  +0x97 + k·0x1B) instead. These are WATER BED0..7 and LAVA BED0..7
  (0x004F6B6B..0x004F6BDC).
- **ConstructionTypeQuad (0x2000):** only when PlayerColouredWall (0x800) is
  also set, art 1 → +0x2A and art 3 → +0x60 (0x004F6BE3..0x004F6D2A). Arts 0 and
  2 are not loaded.
- **Specials** (0x004F6DCB..0x004F7020):
  - Mana Vault (id 0x1F): "MANA_VAULT" → +0x97, "MANA_VAULT_CLAIMED" → +0xB2.
  - Fog of War (0x1B): "Rock Wall" / "Rock Top" / "Rock Tagged" → +0x97 / +0xB2 /
    +0xCD.
  - Rock (2): "Rock Corner" → +0x97.
- **Definition +0x1CB bit 1 = art 1 and art 2 both named.** It is set at the
  end of the loader.

| Terrain (id) | Flags (relevant) | Art 0 (+0x0F) | Art 1 WALL (+0x2A) | Art 2 TOP (+0x45) | Art 3 (+0x60) |
|---|---|---|---|---|---|
| Impenetrable Rock (1) | AllowRoomWalls, RandomTexture, n = 2 | — | IMPEN ROCK WALL | IMPEN ROCK TOP | — |
| Rock (2) | RandomTexture, n = 3 | Rock Corner | ROCK WALL | ROCK TOP | ROCK TOP TAGGED |
| Dirt Path (3) | — | ROCK FLOOR | — | ROCK TOP TAGGED | ROCK TOP TAGGED |
| Water (4) / Lava (5) | ConstructionTypeWater | WATER BED / LAVA BED (→ BED0..7) | — | Water / Lava0 (type 2) | — |
| Gold (6) | RandomTexture, n = 3 | — | GOLD WALL | GOLD TOP | GOLD TOP TAGGED |
| Gems (7) | — | — | GEMS WALL | GEMS TOP | GEMS TOP TAGGED |
| Claimed Path (8) | Quad, PlayerColouredPath | CLAIMED FLOOR (not loaded) | — | — | (not loaded) |
| Reinforced Wall (9) | Quad, PlayerColouredWall, AllowRoomWalls, Decay, n = 4 | CLAIMED TOP (not loaded) | CLAIMED WALL | — | ROCK TOP TAGGED |
| Fog of War (27) | Solid | Rock | ROCK WALL | ROCK TOP | ROCK TOP TAGGED |
| Edge of Map (30) | AllowRoomWalls, RandomTexture, n = 2 | — | IMPEN ROCK WALL | IMPEN ROCK TOP | — |

n = file byte 0x296 → runtime +0x1C3.

### The rule, in short

CONFIRMED in the disassembly at `0x0043F1B6..0x0043FC21` (sides), `0x0043FC21..0x0043FE30`
(top and water) and `0x0043FF5A..0x00440315` (quarters); the secret-door prelude
(`0x0043E8FC..0x0043F1A8`) and the hero-wall scheme are LIKELY (decompile only).

- A terrain with side art (`+0x1CB` bit 1) or PlayerColouredWall draws art 1 on each side whose
  neighbour is **off the map** (CONFIRMED, `0x0043F1F6..0x0043F224`: the flag set before the
  on-map call survives its failure), or explored and not nav-solid, unless that neighbour is a room
  that draws its own walls (room definition `+0x43D` bit 3, set for constructions 1, 5, 7, 9, 12
  and 13, `wall-pieces.md` §1) and this terrain is AllowRoomWalls. The face test has exceptions
  this summary does not settle: it branches on `drawnAsFog`, on vtable `+0xDC` and a flag from
  `0x0044DB70`, and on the neighbour terrain's flag `0x1000000` with tile `+0xB & 0x40`
  (`0x0043F22A..0x0043F315`), so a face can be drawn toward unexplored ground; what those return is
  UNKNOWN. The owner (2026-10-09): unexplored ground is hidden behind the fog, *"essentialy black to
  me"*, until a reveal spell or a creature reveals it, so such a face is not seen in play. (The
  remake's runner draws fog as the Fog of War terrain's rock, not black.) A hero-owned Reinforced Wall instead draws
  `HeroWall_LevelDefaultPart4..8` in `wall-pieces.md` §2's half/whole scheme with 4 and 5 swapped.
- A solid block then draws one art 2 top with the position-hashed texture variant; water and lava go
  to `0x0043E440` (unread); claimed and player-coloured tiles draw four player quarters from table
  `0x0066DA08` by an 8-neighbour mask; anything else draws art 0 whole (Mana Vault by owner).

### The quarters (CONFIRMED, `0x0043FF5A..0x004402F0`)

- Entered for PlayerColouredPath or PlayerColouredWall, when drawn as fog, or for terrain 0x23
  (whose piece comes from vtable `+0x2AC`, unread).
- The mask is the tile byte `+0xC` (written by `0x00450470`: a bit per neighbour off the map, or for
  an open tile of another terrain, or for a solid tile not nav-solid). PlayerColouredWall, or a
  tile drawn as fog, scans live instead (`0x0043FFC8..0x00440088`): bit k when neighbour k is on
  the map, clear of `0x004434F0`'s grid bit 15, not nav-solid (vtable `+0x208`), and explored or
  a flag set.
- Each quadrant reads three bits, (first, diagonal, last) as bits 2, 1, 0, and takes its piece from
  table `0x0066DA08` = 3, 0, 2, 0, 4, 1, 4, 1: 3 the interior, 0 and 4 the two straight edges, 1 an
  outer corner, 2 an inner corner. **The rotation is fixed per quadrant**, not added to a table
  value as the room floors do: directions (6, 7, 0) give quadrant 0 at rotation 2, (0, 1, 2)
  quadrant 1 at 3, (2, 3, 4) quadrant 2 at 0, (4, 5, 6) quadrant 3 at 1. The entry stores the
  rotation at `+4` and the quadrant at `+0x16`.
- The piece comes from the owner's player object (`0x00455990`; an unowned tile gives the neutral
  player): `+0x10AA` + piece·4 for a floor, `+0x10BE` for a solid tile or one drawn as fog. That
  set n of "Claimed Floor{n}_{i}" / "Claimed Top{n}_{i}" is player id − 1 is STRONGLY INFERRED from
  the sets' textures (set 0 T_Claimed_Good, 1 T_Claimed_Neutral, 2.. T_Claimed_Path1..); the
  loader of those tables is unread.
- Every quarter piece is modelled centred on its own origin, a quarter tile each way, at height
  1 (floor) or 2 (top) in the renderer frame; whole pieces span the tile, faces stand on the
  north edge from 1 to 2 (read from the install by `InstallArtProviderTests`).

### What each terrain draws

- **Rock, Gold, Gems, Impenetrable Rock, Edge of Map, Fog of War.** One
  "<X> WALL" (art 1) per face that is exposed, visible and not taken over by a
  room, rotated by side. One "<X> TOP" (art 2) on top. CONFIRMED.
  - Rock and Gold use a 1-in-3 texture variant and Impenetrable a 1-in-2
    variant, picked by a **position hash**. The hash is the LCG 0x005558D0
    (s = s·1664525 + 1013904223, then a 13-bit rotate, mod n), seeded from x and
    y and never from the synced seed 0x006F3FA8. It is deterministic per tile,
    not random. CONFIRMED for the computation. That +0x11 selects a texture
    variant is STRONGLY INFERRED.
  - "Rock Corner" (+0x97) and the TAGGED tops (+0x60) are not read by
    0x0043E860. Their consumer is UNKNOWN. list+0x2EC bit 0 is not the tagged look: it is set
    for every PlayerColouredWall (`0x0043FCC9`), for a tile-grid bit (`+1 & 0x20`) and inside a
    stored rectangle, which reads as a highlight or selection. The owner (2026-10-09): while the
    left button is held, the highlight covers the whole drag rectangle and follows the cursor,
    (un)highlighting tiles as it moves, which fits that rectangle (STRONGLY INFERRED). The remake
    draws art 3 on a tagged tile as a HYPOTHESIS from its name.
- **Reinforced Wall.** "CLAIMED WALL" per face, with +0x11 = 4-stage damage
  frame from tile health (0x004419D0, flag 0x20000, n = 4): `0x00441990` takes (health − 2,
  floored at 0) over MaxHealth as a 12.12 fraction of n, so full health is the highest stage. The
  material lists Brick_DECAY3, Brick_DECAY2, Brick_DECAY, Brick, so the healthy wall is taken to
  be Brick, the last (STRONGLY INFERRED; the consumer that turns the fraction into a texture index
  is unread). The top is 4
  player-coloured quarters "claimed top{owner}_{0..4}" chosen by the live
  open-neighbour mask. For the hero owner the faces are HeroWall_…Part4..8
  instead. STRONGLY INFERRED: the player tables at +0x10AA/+0x10BE were not
  traced to their loader; that they hold the "claimed floor/top P_i" meshes
  rests on the mesh names (7 players × 5).
- **Claimed Path.** 4 quarters "claimed floor{owner}_{i}". The mask is the tile
  byte +0xC, precomputed by the simulation, not a live scan. CONFIRMED for the
  switch; the names are STRONGLY INFERRED.
- **Dirt Path.** One whole "ROCK FLOOR" piece. CONFIRMED.
- **Water and Lava.** 0x0043E440 builds them from WATER/LAVA BED0..7 and the
  tile byte +0xC. Not read: UNKNOWN.
- **Mana Vault.** MANA_VAULT when owned by the neutral player, else
  MANA_VAULT_CLAIMED. CONFIRMED, and the owner sees the two looks (2026-10-09). Not built.

### Room walls versus terrain faces

Room walls are drawn only against AllowRoomWalls terrain (Impenetrable Rock,
Reinforced Wall, Edge of Map) or a closed secret door. Against such a tile the
terrain's own face is suppressed. Against Rock, Gold, Gems and the rest, the
room draws no wall, and the rock tile shows its own ROCK WALL face to the room.
CONFIRMED (both sides of the test).

### Randomness

Nothing here reads the synced RNG or the game turn. The RandomTexture variant and the Work Shop
centre test are position-hashed (`0x005558D0` seeded from x and y); the Reinforced Wall's damage
frame, the Prison centre and the Heart's destroyed set follow state.

---
