# Where furniture stands in its tile, and which way it faces

Status: DRAFT, 2026-10-10. Every placement below is **CONFIRMED** by a static read of
`DKII.EXE` (decompile plus the jump tables named) unless marked otherwise. Which way a
model's front is drawn for a given heading is **UNKNOWN** (the renderer is unread); the
heading values and the direction the binary's own trig gives them are read. Which tiles get
furniture, and the coordinate selector, stay in `auto-placed-furniture.md`; the Hatchery's
coop *choice* stays in `hatchery.md` §2.

## 1. Units

- A tile is `0x1000` units on each axis; a tile centre is `tile * 0x1000 + 0x800`. North is
  `-y` (direction table `0x006BC370`: N, NE, E, SE, S, SW, W, NW = (0,-1), (1,-1), (1,0),
  (1,1), (0,1), (-1,1), (-1,0), (-1,-1)).
- A heading is an 11-bit angle, `0x800` = a full turn (every store masks `& 0x7FF`; walls use
  `0x200` per quarter). Objects keep it at `+0xF4` and copy it to `+0x102`.
- The sine table at `0x006C1488` is 16.16 fixed point, 2048 steps per turn (entry 1 = 201,
  entry `0x100` = `0xB504`); the cosine is the same table `0x200` entries on (`0x006C1C88`).
  The corner placers move an object by `(sin h, -cos h) * r`, so heading 0 points north and
  `0x200` east, clockwise (STRONGLY INFERRED as the engine-wide convention: it is how these
  placers use it).
- Objects also carry a signed word triple `+0x95/+0x97/+0x99` and three angle words
  `+0x64/+0x66/+0x68`. The placers below write them, and the save writes the triple
  (`0x0049B9F0`). They are RenderInfo (`thing+0x44`) `+0x20/+0x22/+0x24` and `+0x51..+0x55`:
  `+0x64` turns the mesh about its X, then `+0x68` about its Y, then the heading yaws it;
  `+0x66` is unused by the mesh draw; `+0x95..+0x99` is a **draw offset** x/y/height in world
  axes, added to the point and not to the position (CONFIRMED, `object-drawing.md` §4).

## 2. The floor pass (`Room_Rebuild` `0x004EDFC0`, first loop)

Every floor object is created at its tile's centre, z = `Tile_FloorHeight` (`0x004545C0`) of
that point, and its home position `+0xF6/+0xFA/+0xFE` set to the same. The arm is chosen by
object type (a C switch on `+0x12C`):

| type | rooms | position | heading | other |
|---|---|---|---|---|
| default (Guard Post Weapons 71, Guard Post Table 112, Bucket 16, Anvil 15, Electric Chair 61, Barrel 59, Bar Table 67) | Guard Room, Workshop, Torture, Casino | centre | 0 | |
| 8 Torture Wheel | Torture | centre | `0x004F2950(x, y)`: game turn × `((x+y)&3)+4` × (+1 if x+y odd, −1 if even), `& 0x7FF` | `+0x64 = 0x600`; process state `0x1E` |
| 14 Training Dummy | Training Room | centre | `Choice(x, y, 0x800)` | |
| 46 Work Bench | Workshop | centre | `Choice(x, y, 0x800)` | `+0xD4/+0xD8/+0xDC = 0` |
| 31–34 Tomb Stones | Graveyard | centre | 0 | `+0x68 = (r(0xAA)+0x7A8)&0x7FF`, `+0x66 = (r(100)+0x7CE)&0x7FF` (about −15°..+14° and −9°..+9°; §1 on which is drawn); `+0x97 = −0x555` |
| 49 Skull, 50 Bone, 51 Lavatory | Prison, Combat Pit | centre + (`r(0x800)−0x400`, `r(0x800)−0x400`), set through SetPosition | `r(0x800)` | home position = the moved point |
| 64 Book Case | Library | centre | `((y_tile) & 1) << 10` (0 or `0x400`) | variant `r(3)` to `+0x14A` (ADR-0397) |
| 87 Roulette Table | Casino | centre | not written | also two Roulette Wheels (30) at (cx, cy+0x7D0) and (cx, cy−0x7D0) |

`Choice(x, y, n)` and `r(n)` are the coordinate-seeded selector of
`auto-placed-furniture.md` §2: seed = `Step(y, 0x2286) XOR Step(x, 0x16F2)`, then each
`r(n)` is a further `Step(seed, n)` on that same seed, in the order listed (tomb stone: `0xAA`
then `100`; prison: x jitter, y jitter, heading). These draws touch no simulation stream.
The Casino's floor pass skips the lever's tile (`0x004D7160`).

## 3. The wall pass (same function, second loop)

`Room_IsWallSlot` (`0x00454230`) returns the wall side s (0 N, 1 E, 2 S, 3 W). Every wall
object gets heading `((s + 2) & 3) << 9` before its arm (`0x004EE9CE`): the side opposite its
wall, so (by §1) it points into the room: north wall `0x400`, east `0x600`, south 0, west
`0x200`. Then the arm, by type through the index bytes `0x004EF130` (types 8–68) and the
table `0x004EF114`, moves it toward its wall through SetPosition (`0x004B5560`) and stores
the home position:

| type | arm | inset toward the wall | other |
|---|---|---|---|
| 8 Torture Wheel | `0x004EEB27` | `0x7C0` | `+0x99 = 0x800`; process state `0x1E` |
| 29 Training Target | `0x004EEBE2` | `0x700` | `+0xD4 = +0xD8 = Random_Next(0x1000)` on the **simulation stream** (world `+0x15A0`, `0x004EECBF`) |
| 58 Library Book | `0x004EECEB` | `0x700` | `+0xD4/+0xD8/+0xDC = 0` |
| 62 Torture Coals, 63 Torture Water | `0x004EED89` | `0x480` | 62 gets process state `0x28`; see below |
| 67 Bar Table | `0x004EEAA9` | `0x700` | |
| 68 Alcove Barrels | `0x004EEA07` | `0x700` | |
| every other type (Workshop's 15 Anvil and 16 Bucket) | `0x004EEE1F` | **`0x700`** | |

Side s moves the point by (0, −d), (+d, 0), (0, +d), (−d, 0). After the arm, `+0x95 = +0x97 =
0` and `+0x99 = 0x800` for a wheel, else 0. A Coals or Water also gets a second object, a
Torture Wheel (8) created at the tile centre with the same heading, `+0x99 = 0x800`, process
state `0x1E`, moved `0x7C0` toward the wall, and `+0x14C = 1` beside Coals, 0 beside Water.
`AutoFurnitureSystem` keeps that companion as a real device; the view suppresses its
decorative fallback when the simulation device exists.

Process state `0x1E` is `0x004A1C60` (table slot `0x006E4D94`, written at `0x0049EA10`). Every
turn, a wheel on a floor slot takes the heading `0x004F2950(x, y)`, so a floor wheel spins; any
other wheel (the wall wheels and the companions above) keeps its heading and turns its third draw
angle `+0x68` instead, which the view draws as roll.

**Correction.** `0x004EEE1F`, the default arm, moves its object `0x700` toward the wall like
the others. The reading that every other type "stays at its tile's centre" (ADR-0404,
`AutoFurnitureRules.FurniturePosition`) was wrong for the wall pass: the Workshop's wall
Anvils and Buckets sit `0x700` toward their wall. FurniturePosition now applies the wall
pass to auto-placed objects using the same classifier as the view (Q581, ADR-0419).
Floor machines stay centred (§2).

With the shipped `Rooms.kwd`, the wall lists are: Library 58; Training Room 29; Workshop 15,
16; Torture Chamber 8, 62, 63; Casino 68. Prison has `SpecialWalls` but no wall id.

## 4. Room-specific passes (the type switch at the head of `Room_Rebuild`)

Corner masks: the slab cell's `+0x0C` byte holds a bit per direction of `0x006BC370` set when
that neighbour is the same room. Mask k passes when `(byte | mask) == mask`, i.e. the three
cleared bits are outside the room: `0xF8` NE corner, `0xE3` SE, `0x8F` SW, `0x3E` NW. A tile
can pass several (a one-tile-wide room).

| room (type) | function | object | where | heading | z |
|---|---|---|---|---|---|
| Guard Room (9) | `0x004DDE80` | 70 Guard Post | every chain tile, each convex corner k: centre + (sin a, −cos a)·`0x82A`, a = `0x10C + 0x200k` → NE (+0x5FB, −0x58E), SE (+0x58E, +0x5FC), SW (−0x5FC, +0x58F), NW (−0x58F, −0x5FB) | not written | `0x1000`; process state 0 |
| Workshop (10) | `0x004F5650` | 80 Workshop Pillar | as the Guard Post | not written | 0 |
| Temple (13) | `0x004F1240` | 111 Temple Candlestick | each convex corner: centre + diagonal × `0x400` | not written | `0x1000`; process state `0x21` |
| Temple (13) | `0x004F1240` | 66 Temple Hand | the **first** chain tile that is a floor slot with all eight neighbours Temple floor slots; centre | `0x400` | 0 |
| Prison (11) | `0x004E32C0` | 109 Prison Door and 116 Prison Door Bar | the tile north of the first floor slot whose N, NW, NE are not floor slots (`jail-break.md` §1); centre, floor height | `0x400` both | words `+0xD4/+0xD8` = `0xFFF` (door: room `+0x5D` = 1; bar: room `+0x59` = 0) else 0 |
| Casino (15) | `0x004D6ED0` | 95 Casino Pay Out Adjuster | the first floor slot with no floor slot N, NW, NE; centre | `0x400` | `0x1000` |
| Graveyard (14) | `0x004DD5C0` | 75 Graveyard Crypt | see its record | `0x200` | |
| Hatchery (4) | `0x004DE220` | 35 Chicken Coop | centre (choice: `hatchery.md` §2) | `Choice(x, y, 4) << 9` | `0xE00` |
| Crypt (26) | `0x004D8CB0` | 142 Crypt Bell | the first chain tile with all eight neighbours Crypt floor slots; centre; stored at room `+0x49..+0x4F` | not written | 0 |

Each coop also gets `+0x14A = Random_Next(4) << 9` on the **simulation stream** (world
`+0x15A0`, source line `0xBB`, `0x004DE4E4`): one draw per coop created. Ours draws nothing.

## 5. Room pillars (`0x004EF1F0`)

Called by Room_MergeNeighbours, the build and remove rectangle closers, `0x004ECDB0` and the
level start. Rooms with pillars and their object (`0x004E4300`, `0x004E4360`): Treasury 76,
Hatchery 78, Torture Chamber 82, Graveyard 84, Casino 85, Combat Pit 79, Crypt 141. Lair,
Library, Training, Guard Room, Workshop, Prison, Temple, Portal, Heart and bridges have none
(Lair Pillar 77 is never placed here).

For each chain tile and each diagonal k = NE, SE, SW, NW (dx, dy): the tile qualifies when
(x−dx, y) and (x, y−dy) are not this room (it is the corner opposite k) and all nine tiles
from (x, y) to (x+2dx, y+2dy) are on the map and this room, and the tile holds no object
whose definition has flag `0x40000` (`0x00453760`). The pillar stands at the centre moved
toward k by `isqrt(0x200000)` = `0x5A8` at angle `0x100 + 0x200k`: (+0x3FF, −0x400),
(+0x3FF, +0x3FF), (−0x400, +0x3FF), (−0x400, −0x400). Heading `((k−1) & 3) << 9` (`0x600`,
0, `0x200`, `0x400`); `+0x95/+0x97` = the negated offset, `+0x99 = 0`; z 0. The tile's word
`+8` bit `0x4000` (the movement gate, `structure-blocks.md` §5.4) is set, and the tile is
refreshed (`0x00452340`) when that bit changed. So a room shorter than three tiles on either
axis gets no pillars, and a 3x3 gets four, one in each corner tile, a quarter tile inward.
**The pillar is drawn at its tile's centre:** the negated offset cancels the move (only the
position, not the drawing, is inward). `Torture_Pillar` (and `Prison_Pillar`) is a column
0.07..0.5 in model +x/+y from floor to one tile up, under a whole-tile ceiling plate; the
heading uses the mesh conversion in `object-drawing.md` §4. Level 8's Torture Chamber
(a plus around the 3x3 at (11..13, 12..14)) gets exactly four, at (11,12), (13,12),
(11,14), (13,14), as ours places and draws them at tile centres.

## 6. The Dungeon Heart (`DungeonHeart_Build` `0x004DB700`)

Placed by slab-chain index i (row-major for a level's 5x5 Heart, so i = 12 is the centre:
STRONGLY INFERRED from the chain order, not separately checked):

| i | object | position in its tile | heading | other |
|---|---|---|---|---|
| 7, 16, 18 | 94 Arch Pillar | (0x800, 0x4CD), (0x667, 0x19A), (0x999, 0x19A) | not written | z 0 |
| 12 | 100 Dungeon Heart Obstacle | centre | not written | owner = the neutral player `[0x006F2548]` (0 if the room's owner is 0) |
| 12 | 13 Dungeon Heart | centre | not written | health, gold (`0x004DB700` record) |
| 12 | 88 Big Steps × 3 | centre | 0, `0x2AA`, `0x554` (`(n·0x155 & 0x3FF) << 1`, n = 0..2) | `+0x99 = −floor height`; for the local player `+0x9F` bit 0, `+0x8F = 0` |
| 12 | 86 Arches | centre | not written | same local-player bits, then `0x004B3AD0` |

The builder attaches each pillar through `0x004E6220`, which prepends it to
the room's autoobject list. Combat scan E (`0x0045CB00`) follows that list and
keeps strict distance improvements: equal-distance pillars are considered
18,16,7, the reverse of construction order (binary recheck, 2026-10-11).

No Dungeon Heart Plug (96) or Portal Crystal (99) is created with a literal type anywhere
(`PreCall` over all 72 `Object_Create` sites); the Portal gets no furniture from these
passes. UNKNOWN whether either comes from level data. A Portal Gem (28) is created only by
the heart-destroyed handler `0x004DA560` and a trigger action. A saved room's objects are
re-created by `0x004E3C20`, not re-placed.

## 7. Beds and treasury gold

- **Bed** (`0x004E03F0` → `0x0048AD50`): type = the creature's `LairObjectId` (def
  `+0x771`), at the centre of the tile the creature stands on, z 0, heading `Random_Next(0x800)`
  on the simulation stream (source line `0xAE6`); the room's effect `+0x423` plays there.
- **Gold Pile 1** (`0x004F4090`, `0x004F3C60`): a chain tile with no store pile gets one at
  its centre, heading `Random_Next(0x800)` on the simulation stream (line `0x207`), after the
  room effect `+0x425`. Built (ADR-0355) except for the heading itself.

## 8. Open

- **Torture Chamber, ours against DK2** (Level 8, 2026-10-10): the floor wheel must lie flat
  (draw `+0x64 = 0x600`, `object-drawing.md` §4) and spin about the vertical, as it does; the
  wall wheels and the Coals/Water companion must be lifted `0x800` (`+0x99`) and spin about
  their axle by `0x004F2950` into `+0x68`; pillars drawn at the tile centre (§5). All
  presentation.
- What a z of 0 becomes for pillars and the Heart's objects (`object-drawing.md` §4: a resting object falls to its floor height,
  which is `0x1000` on ordinary floor and `0xE00` in a Hatchery, matching the Guard Post,
  candlestick, lever and coop values).
- Which heading a model's front is drawn along.
