# Format and engine: which wall and floor pieces DK2 draws for a tile (Q71)

Source(s):
- Retail v1.70 `DKII.EXE`, read in Ghidra (pyghidra-mcp), 2026-10-06, checked against the
  disassembly where the decompiler mis-tracks registers
- The shipped `Data/editor/Rooms.kwd`, `Terrain.kwd` and the names in `Meshes.WAD`

Original version: DK2 v1.70 (GOG)

Presentation only: nothing here changes a rule. Built so far: the general room wall chooser
(§2) for Normal and DoubleQuad rooms (ADR-0392); the rest is the spec for what follows.

Labels: CONFIRMED (read in the disassembly, or whole in a decompile that does not depend on
mis-tracked registers), STRONGLY INFERRED, LIKELY or UNKNOWN.

Conventions used below:
- **Direction table** `0x006BC370`: 8 entries of (dx, dy), index k = 0 N (0,−1),
  1 NE, 2 E (1,0), 3 SE, 4 S (0,1), 5 SW, 6 W (−1,0), 7 NW. **Side table**
  `0x006BC350`: 4 entries N, E, S, W. A side index s (0..3) uses direction d = 2s.
  CONFIRMED (bytes read).
- **Piece list** (the 4th or 5th argument of every builder) holds two arrays:
  - the **quarter array** at list+0: 27-byte entries, count at list+0x1B0;
  - the **whole array** at list+0x1B4: 22-byte entries, count at list+0x2E8.
  Every entry stores the mesh handle at +0, the rotation 0..3 at +4, an RGB tint
  at +8..+0xA, a word at +0xC (0x90 on wall-top pieces, otherwise 0), a byte 0xFF
  at +0x10, a texture or animation value at +0x11 and a flag byte. Quarter entries
  also store a **quadrant** at +0x16 and their flag byte at +0x1A; whole entries
  store their flag byte at +0x15. CONFIRMED (stores). The quadrant numbering is
  0 NW, 1 NE, 2 SE, 3 SW. That is STRONGLY INFERRED: each floor builder writes
  quadrant q from the neighbour directions (2q−2, 2q−1, 2q), and the wall halves
  agree with it. How 0x0043C310 consumes the two arrays (vt+0xF0 and vt+0xF4) was
  **not** re-read.

---

## 1. The runtime room definition's art layout

### The loader

The room loader is **0x004EACC0** (thiscall: runtime definition, intermediate
record, …). It works from an intermediate record, which is the file record
shifted by **+0xD4**. The intermediate starts with 0xD4 bytes of "field present"
dwords, its checksum loop covers 0x4F3 = 0xD4 + 1055 bytes, and the file's torch
art at 0x3C7 appears at intermediate 0x49B. CONFIRMED.

Every art goes through 0x00504D80, which builds a 27-byte record (the handle at
+0xF, the flags at +0xB, the type at +0xA). Destinations, from the `LEA ECX`
before each call:

| File art (file offset) | Runtime record | Handle at | Evidence |
|---|---|---|---|
| art 0 (0x20), the GUI icon | +0x00 | **+0x0F** | 0x004EB82A..0x004EB861; loaded with flags \| 0x80000 |
| art 2 (0xC8), loaded whole **only when TileConstruction = 1** | +0x1B | **+0x2A** | 0x004EAE7C..0x004EAEC3 |
| art 7 (0x26C) | +0x36 | +0x45 | 0x004EB916..0x004EB95D |
| art 8 (0x2C0) | +0x51 | +0x60 | 0x004EB9FA..0x004EBA41 |
| torch (0x3C7) | +0x6C | +0x7B | 0x004EBADE..0x004EBB25 |
| torch again, as a type-5 light mesh from the second mesh bank | +0x467 | +0x476 | 0x004EBE58..0x004EBEE1 |
| art 9 (0x314) | +0x87 | +0x96 | 0x004EBBC5..0x004EBC0F |
| arts 1, 3, 4, 5, 6 | not loaded here | — | — |

Each loaded art also gets optional `<name>Start` and `<name>End` handles at
record +0x13 and +0x17, when flag bits 0x8 and 0x10 are set. The format strings
are at 0x006975CC and 0x006975C4. CONFIRMED. So **+0x7F is the torch art's
"Start" slot** (0x6C + 0x13), not a piece slot in its own right (§3).

### The piece table: where +0x121..+0x1A8 come from

The wall and floor slots are **not file art slots**. They are meshes the loader
builds by name from the **name of file art 2**, `sprintf("%s%d", art2Name, k)`
(format 0x0069FF1C), each with art 2's flags and type. Piece k goes to record
**+0xC1 + k·0x1B**, so its handle sits at **+0xD0 + k·0x1B**. CONFIRMED.

| TileConstruction | Pieces built | Evidence |
|---|---|---|
| 1 Complete | name minus its last character, k = 0..9, but k = 3..5 are skipped | 0x004EAF6E..0x004EB02F |
| 2 Quad | k = 0..3 | 0x004EB050..0x004EB0C1 |
| 3 3×3 | k = 0..8 | 0x004EB226.. |
| 4 3×3 rotated | k = 0..2 | — |
| 5 Normal | **k = 0..9** | 0x004EB6B6..0x004EB727 |
| 7 DoubleQuad | k = 0..14; plus "Water" for RoomId 13 | 0x004EB0DE..0x004EB20B |
| 8 5×5 rotated | k = 0..3, plus "Dungeon_Destroyed0..5" at +0x271 + k·0x1B | — |
| 9, 12 | numbered **from 1**: 9 pieces "<name>1..9", or 16 pieces 1..16, into k = 0.. | 0x004EB440..0x004EB4B3, 0x004EB5B6..0x004EB627 |
| 10 | art2Name alone ("%s") into k = 0 | 0x004EB62E.. |
| 11 | k = 0..3, plus "%s%s%d" (name, "Destroyed", k) at +0x271 + k·0x1B | 0x004EB4B8..0x004EB59B |
| 13 | k = 0..6, plus "%s%d%s" (name, k, "_DESTROYED") at +0x271 + k·0x1B | 0x004EB72E..0x004EB80B |

The wall chooser's slots are therefore these pieces. CONFIRMED by arithmetic
from the loader:

| Runtime handle | Piece k | Role in 0x00444F10 (§2) |
|---|---|---|
| +0x121 | 3 | Hero Gate Final only (also the interior floor quarter in Normal rooms, `floor-and-terrain-pieces.md` §4) |
| **+0x13C** | **4** | wall half, A end (the half towards direction d−2) |
| **+0x157** | **5** | wall half, B end (the half towards direction d+2) |
| **+0x172** | **6** | wall half, continuing (used on either half) |
| **+0x18D** | **7** | whole straight wall, plain |
| **+0x1A8** | **8** | whole straight wall, decorated (furniture phase) |
| +0x7F + n·0x1B (Hero Gate 3x1) | n − 3 | gate face (§3) |

### Shipped names (read from `Rooms.kwd` by script; each mesh exists in `Meshes.WAD`)

| Room (RoomId) | TC | art 2 | Floor quarters 0,1,2,3 | Walls 4 / 5 / 6 / 7 / 8 | Centre 9 |
|---|---|---|---|---|---|
| Treasury (1) | 5 | Treasury | Treasury0..3 | Treasury4 / 5 / 6 / 7 / 8 | Treasury9 |
| Lair (2) | 5 | LAIR | LAIR0..3 | LAIR4..8 | LAIR9 |
| Hatchery (4) | 5 | HATCHERY | HATCHERY0..3 | HATCHERY4..8 | HATCHERY9 |
| Library (6) | 5 | LIBRARY | LIBRARY0..3 | LIBRARY4..8 | LIBRARY9 |
| Training Room (7) | 5 | TRAINING | TRAINING0..3 | TRAINING4..8 | TRAINING9 |
| Guard Room (9) | 5 | GUARDPOST | GUARDPOST0..3 | GUARDPOST4..8 | GUARDPOST9 |
| Work Shop (10) | 5 | WORKSHOP | WORKSHOP0..3 | WORKSHOP4..8 | WORKSHOP9 |
| Torture Chamber (12) | 5 | TORTURE | TORTURE0..3 | TORTURE4..8 | TORTURE9 |
| Graveyard (14) | 5 | Crypt | Crypt0..3 | Crypt4..8 | Crypt9 |
| Casino (15) | 5 | Bar | Bar0..3 | Bar4..8 | Bar9 |
| Crypt (26) | 5 | Crypt | (same meshes as Graveyard) | | |
| Prison (11) | 7 | PRISON | outer 0..3, inner ring 10..12, interior 13, centre 14 | PRISON4..8 | (9 not drawn by any builder read) |
| Temple (13) | 7 | TEMPLE | same layout | TEMPLE4..8 | |
| Combat Pit (16) | 7 | CombatPit | same layout | CombatPit4..8 | |
| Portal (3) | 3 | PORTAL | PORTAL0..8 by tile | no walls (TC 3 does not call the chooser) | |
| Dungeon Heart (5) | 8 | DUNGEON | DUNGEON0..3 + Dungeon_Destroyed0..5 | no walls | |
| Wooden / Stone / Hero Stone Bridge | 2 | BRIDGE / StoneBridge / Hero_StoneBridge | 0..3 | no walls | |
| Hero Gate Final (18) | 9 | hero door0 | "hero door01..09" = pieces 0..8 | special (§3) | |
| Hero Gate FrontEnd (21) | 12 | 3DFrontend | 3DFrontend1..16 | none (§3) | |
| Hero Gate 2x2 (20) | 11 | Hero_Gate | Hero_Gate0..3 + Hero_GateDestroyed0..3 | no walls | |
| Hero Gate 3x1 (23) | 13 | 3Tile_Hero_Gate | 0..6 + _destroyed | special (§3) | |
| Hero Portal (25) | 3 | heroportal | heroportal0..8 | no walls | |

The art 8 of Hero Gate Final and FrontEnd is "IMPEN ROCK TOP" (+0x60), which
0x004464C0 and 0x00446640 draw (`floor-and-terrain-pieces.md` §4). Temple's torch art is empty.

The "<name>k" names, and the slots taken from the loader, are CONFIRMED. That
piece 9 is the **interior centre piece** and pieces 0..3 are the floor quarters
is CONFIRMED for Normal rooms (`floor-and-terrain-pieces.md` §4).

### Which constructions draw room walls

The loader sets definition `+0x43D` bit 0x8 for TileConstruction 1, 5, 7, 9, 12
and 13 (the switch right after the +0x452 copy in 0x004EACC0). CONFIRMED. These
are exactly the 0x00443610 arms that call the wall chooser. The terrain renderer
tests the same bit to **suppress the neighbouring wall's own face** (`floor-and-terrain-pieces.md` §5).

---

## 2. The per-side decision in 0x00444F10 (general rooms)

Signature: thiscall on the bridge, with arguments (x, y, roomTileTerrain, room,
list). The room definition is room+0x19, and the room type (= RoomId) is
room+0x18. The decompiler drops one argument. The frame was re-derived from
0x00444F10..0x00444FB3.

**Dispatch** (0x00444F7F..0x00444F99): type 0x12 → §3, type 0x15 → return,
type 0x17 → §3, everything else → the general path. CONFIRMED.

**Per-piece extras**: +0x11 = 0x004419D0(tile, roomTerrain). For a terrain with
flag 0x20000 (Decay; only Reinforced Wall has it) it is a damage stage,
`max(0, health − 2) / (MaxHealth / n)`, as 4.12. For room terrains it is 0.
The tint comes from the **neighbour's** terrain colour words +0x1B9/+0x1BB/+0x1BD
when that terrain has flag 0x20000000 (AmbientLight). CONFIRMED.

### Pseudocode (general rooms; CONFIRMED against the disassembly, 0x00444F9F..0x004458B1)

```
roomId = tile(x,y).roomWord & 0xFFF
for s in 0..3:                                   # N, E, S, W
    d  = 2*s
    N  = (x,y) + DIR[d]                          # the tile outside this side
    # 1. the outside neighbour must be visible and be a "room wall" tile
    if not (N.explored(+9 bit 0x20) or bridge.SightVisible(N) /*vt+0xDC*/): continue
    if not (terrain(N).flags & AllowRoomWalls(0x10000) or closedSecretDoorCell(N)): continue
       # closedSecretDoorCell = presentation-grid cell bit 15 (0x004434F0, bridge+0x326)
       # no room-id test on N: a room tile is never AllowRoomWalls
    highlight = cell(N).bit13(0x2000) /*dig-tag highlight*/
                or (N inside bridge drag rectangle [+0x10..+0x1C) and terrain(N) is Taggable(0x8))
    # 2. does the wall continue past this tile on each end?
    contA = continues(d-2, d-1)     # towards the "A" end
    contB = continues(d+2, d+1)     # towards the "B" end
    where continues(side, diag):
        S = (x,y)+DIR[side]; D = (x,y)+DIR[diag]
        return S.roomWord == roomId
           and (world.navSolid(D) /*vt+0x208*/ or closedSecretDoorCell(D))
           and (terrain(D).flags & AllowRoomWalls or D.byte0B bit0 /*secret door on tile*/)
    # 3. choose pieces
    if contA and contB:                            # middle of a straight wall: one whole piece
        decorate = (s even and room.phaseNS(+0x92) >= 0 and (x + phaseNS) even)
                or (s odd  and room.phaseEW(+0x96) >= 0 and (y + phaseEW) odd)
        whole += { piece 8 (+0x1A8) if decorate else piece 7 (+0x18D), rot = s, flag = highlight }
    else:                                          # an end or a corner: two half pieces
        quarter += { (contA ? piece 6 : piece 4), rot = s, quadrant = s,       flag = highlight }
        quarter += { (contB ? piece 6 : piece 5), rot = s, quadrant = (s+1)&3, flag = highlight }
```

The four half-piece cases, with their addresses:

| contA | contB | quadrant s | quadrant s+1 | Address |
|---|---|---|---|---|
| 0 | 0 | piece 4 (+0x13C) | piece 5 (+0x157) | 0x00445754..0x00445890 |
| 0 | 1 | piece 4 | piece 6 (+0x172) | 0x00445622..0x00445745 |
| 1 | 0 | piece 6 | piece 5 | 0x004454D8..0x00445605 |
| 1 | 1 | piece 7 or 8 (whole array) | — | 0x004452D5..0x004454CB |

### Notes

- **Side order** is N, E, S, W. Each side's A end is the counter-clockwise
  neighbour (d−2): W for N, N for E, E for S and S for W. The B end is d+2. For
  side N, quadrant s = 0 is the NW quarter and s+1 = 1 the NE quarter, so A is
  the west half. CONFIRMED for the arithmetic; the quadrant semantics are STRONGLY
  INFERRED.
- **The rotation +4 is s for every piece of side s** (0 N, 1 E, 2 S, 3 W).
  CONFIRMED.
- **The highlight flag is bit 0, not bit 1.** It is ORed into +0x1A (quarter
  entries) or into list+0x1B4 + n·0x16 + 0x15 = +0x1C9 (whole entries). It marks
  the wall tile behind the face as dig-tagged: either cell bit 0x2000, or the
  tile is inside the live selection rectangle at bridge+0x10/+0x14/+0x18/+0x1C
  and its terrain is Taggable. CONFIRMED for the tests (0x00445280..0x004452B7).
  That bit 0x2000 is the dig-tag highlight is STRONGLY INFERRED: 0x004484A0
  clears it on non-Taggable terrain.
- **0x004435E0** returns a pointer to the 16-bit cell of the bridge's per-tile
  presentation grid (bridge+0x326; width +8, height +0xC, base +4), or null off
  the map. Cell bits 0..12 are the head of the tile's engine-object (light) list
  (0x00439B00, 0x00446C70). Bit 13 (0x2000) is the dig-tag highlight. Bit 14
  (0x4000) is cleared by every refresh (0x004484A0) and set on Temple
  DoubleQuad tiles (0x00443F20); its meaning is UNKNOWN. **Bit 15 (0x8000)**
  is read by 0x004434F0 and written only by 0x00443590, whose one caller is
  0x004488A0. That function finds a door thing (type 4) whose definition has
  +0x8B bit 0x20 (`Secret`, `DoorDef.cs`), toggles its open bit and writes bit
  15. So bit 15 = **a closed secret door, drawn as wall**. LIKELY: the value
  written comes from a register the decompiler mis-tracks, so open versus closed
  was not pinned.
- **The decorated piece's parity.** For N and S faces it uses the room tile's x
  plus `+0x92`, with piece 8 on the even sum. For E and W faces it uses y plus
  `+0x96`, with piece 8 on the odd sum. A negative phase (−1 until the room has a
  straight wall run on that axis; not tied to wall furniture, `floor-and-terrain-pieces.md`
  `room-floors-and-liquids.md` §5) means always piece 7. Torches
  (wall-torches.md §2) go on the opposite parity, but only Torture and Casino
  add the phase to the torch test. In other rooms with a phase, a decorated
  wall and a torch can coincide. CONFIRMED (0x004452F1..0x0044535F).
- **Not position-hashed and not random.** CONFIRMED.

---

## 3. Special cases in 0x00444F10

**Type 0x15 (Hero Gate FrontEnd): no walls.** It returns at 0x00444F90.
CONFIRMED.

**Type 0x12 (Hero Gate Final, "hero door01..09").** dx = x − room+0x2D and
dy = y − room+0x2E (the room origin bytes). All pieces go into the whole array,
tinted from the room tile's terrain. Read in the disassembly at
0x00445BD0..0x00445DA0 for dx = 0. The dx = 2 and dx = 1 arms come from the
decompile only, and have the same shape.

| dx | Pieces |
|---|---|
| 0 | piece 6 rot 3, always. Also piece 6 rot 0 when dy = 0, and piece 3 (+0x121) rot 2 when dy = 4. The dy = 0 and dy = 4 branches fall through into the rot-3 piece (CONFIRMED). |
| 2 | piece 6 rot 1, always. Also piece 6 rot 0 when dy = 0, and piece 7 (+0x18D) rot 2 when dy = 4 (STRONGLY INFERRED). |
| 1 | dy = 0: piece 4 rot 0. dy = 5: piece 5 rot 0 **and** piece 5 rot 2. Nothing otherwise (STRONGLY INFERRED). |
| other | nothing |

So: hero door07 down both side columns, door05 on the top middle, door04 and
door08 on the dy = 4 corners, and door06 twice across dy = 5. There is no
neighbour test, so the walls are fixed by the gate's own layout.

**Type 0x17 (Hero Gate 3x1, "3Tile_Hero_Gate0..6").** Disassembly checked at
0x004458BC..0x00445BBD.

```
o = room.+0x9B ; if o == 8: return
face = (o - 2) & 3
def  = world.roomDefById(0x17)              # vt+0x98
k    = tile(x,y).byte0C >> 4
for s in 0..3:
    N = (x,y)+DIR[2s]
    if N.roomWord != 0 and N.roomWord == thisRoom: continue
    if not (N.explored or bridge.SightVisible(N)): continue
    if s == face:  whole += { handle def[+0x7F + k*0x1B] = piece (k-3), rot = (s-1)&3 }
    elif terrain(N) AllowRoomWalls or closedSecretDoorCell(N):
                   whole += { piece 6 (+0x172), rot = (s-1)&3 }
```

- This arm rotates by **(s−1)&3**, where the general arm uses s. CONFIRMED.
- +0x7F + k·0x1B = +0xD0 + (k−3)·0x1B. The facing side therefore draws gate
  piece k−3, while the floor builder 0x00446800 draws piece k from the same
  nibble. The tile writer 0x004F00F0 gives k = 3, 4 or 5 by position along the
  gate (`room-floors-and-liquids.md` §1), so the face is piece 0, 1 or 2
  (LIKELY: that writer was read from the decompile only).

---

## Not settled

1. **How 0x0043C310 consumes the two arrays.** Which array goes to vt+0xF0 and
   which to vt+0xF4, how +0x16 places a quarter, and what +0xC = 0x90 and +0x11
   mean, were not re-read. The quadrant numbering (0 NW … 3 SW) is inferred.
2. **Grid bit 15.** That it means a closed (rather than an open) secret door was
   not pinned (0x004488A0 mis-tracks the value it writes). The secret-door
   prelude of 0x0043E860 was read from the decompile only; the hero-wall scheme's run-on tests
   were read from the instructions (0x0043F3A0..0x0043F50F, floor-and-terrain-pieces.md section 5).
3. **Unread builders.** The player tables
   +0x10AA/+0x10BE and their loader; the "<prefix>" string of 0x004C2620 (the
   mesh name shows "Herowall_LevelDefault"); the consumers of Rock Corner,
   Fog's three meshes and the TAGGED tops. (0x0043E440 and the room tile
   byte +0xC writers were read 2026-10-09: `floor-and-terrain-pieces.md` §6.)
