---
systems: [data-formats]
---
# Mechanic: TERRAIN(110), ROOMS(120) and MAP(100) records

Source(s):
- ufdada/dk2-tools `terrains_struct.bt`, `rooms_struct.bt`, `map_struct.bt`
- DiaLight/Flame `src/dk2/room_type.h` (room ids, from DKII.EXE v1.70)
- Direct parsing of the reference install

Original version: DK2 v1.70 (GOG)

## Record sizes — self-validating

| Record | Items | Content bytes | Per record |
|---|---:|---:|---:|
| TERRAIN | 41 | 22,632 | **552** |
| ROOMS | 26 | 27,430 | **1,055** |

Summing the template's declared field widths reproduces both numbers exactly,
using ArtResource = 84 bytes (64-byte name + 4 flags + 12 union + 4 tail). The
84-byte figure is itself STRONGLY INFERRED from the creature record's own offset
anchors: `portraitResource` at 0xD40, `Light` at 0xD94, difference 0x54 = 84.

## MAP(100) — the dungeon grid

The chunk header carries `u32 width, u32 height`; content is exactly
`width * height * 4` bytes, row-major, 4 bytes per tile:

```
u8 terrainId    u8 playerId    u8 flags    u8 unknown
```

STRONGLY INFERRED across every campaign map: all tile terrain ids resolve against the
TERRAIN table (asserted in tests). Example — `level1Map.kld`, 30 × 38
(4,560 content bytes):

```
terrain  1 Impenetrable Rock   776    terrain  8 Claimed Path     18
terrain 30 Edge of Map         132    terrain  3 Dirt Path         9
terrain  2 Rock                113    terrain  6 Gold              9
terrain  9 Reinforced Wall      45    terrain 12 Portal            9  (3x3)
terrain 14 Dungeon Heart        25    terrain 33 Hero Gate 2x2     4  (2x2)
                              (5x5)
```

Tile counts independently confirm room geometry: Dungeon Heart occupies exactly
25 tiles (5x5) and Portal exactly 9 (3x3), matching those rooms'
`RecommendedSizeX/Y` and `TileConstruction` from the separate ROOMS record.

**Byte 3 (`flags`) is the bridge hazard.** Over all 66 shipped maps it takes no
value but 1 or 2; 2 occurs only on bridge terrain and every such tile borders
lava, every 1 bridge tile touching a hazard borders water. A sold bridge
reverts to what it spanned (ADR-0031, `BridgeRules`,
`BridgesOnRealDataTests`). Behaviour STRONGLY INFERRED; that this byte is where the
original keeps it is STRONGLY INFERRED.

**Byte 2 (`playerId`) numbering**, STRONGLY INFERRED by sweeping the MAP record of all
66 shipped maps (`dk2tools kwd map <file>` prints one map's owner histogram):

| id | meaning | maps carrying it |
|---:|---|---:|
| 0 | reserved as "unowned" in `PlayerIds` | **0 — it never occurs** |
| 1 | the hero side | 49 |
| 2 | neutral — **undug rock and every unclaimed tile** | 66 |
| 3 | first keeper | 66 |
| 4–7 | further keepers | 40 / 21 / 12 / 3 |

The trap: `DK2.Data.Model.PlayerIds` documents 0 as unmined rock, and no MAP
tile in the campaign is owned by 0 — **2 is** what undug terrain carries.
Nothing depends on the id-0 reading (rules test terrain and ownership, not the
literal), but do not derive "unowned" from it. The 3–7 keeper range is asserted
by `PortalOnRealDataTests.TheReservedPlayerIdsAreWhatTheCampaignSays` from
dungeon-heart tiles, creature pools and availability entries, none of which is
the undug-tile case.

## Significant finding — digging/claiming is a data-driven state machine

Each terrain carries `startingHealth`, `maxHealthTypeTerrainId` and
`destroyedTypeTerrainId`. Together these encode DK2's terrain transitions:

```
Rock (165 hp)          --destroyed-->  Dirt Path
Rock                   --at max hp-->  Reinforced Wall
Reinforced Wall (900)  --destroyed-->  Rock
Dirt Path              --at max hp-->  Claimed Path
Gems (100 hp)          --destroyed-->  Gems        (inexhaustible)
Gold (660 hp, 3000 gold) --destroyed--> Dirt Path
```

Combined with the VARIABLES health deltas (`MineGoldHealth=-300`,
`ClaimTileHealth=825`, `DigRockHealth=-5000`, `ReinforceWallHealth=50`,
`RepairWallHealth=22`), this is the complete numeric model for digging,
claiming and reinforcing. How often the deltas are applied is Q1, the tick
rate; the full table is `terrain-room-dump.md`.

## Finding — room ids resolve gaps in Flame's disassembly

Flame's `room_type.h` left seven ids as `unk_N`. The ROOMS records carry name
strings, which name them:

| id | Flame | Actual name from Rooms.kwd |
|---:|---|---|
| 2 | `unk_2` | **Lair** |
| 7 | `unk_7` | **Training Room** |
| 18 | `unk_18` | **Hero Gate [ Final ]** |
| 19 | `unk_19` | **Hero Gate [ Tile ]** (marked not used) |
| 20 | `unk_20` | **Hero Gate [ 2x2 ]** |
| 22 | `unk_22` | **Hero Stone Bridge** |
| 23 | `unk_23` | **Hero Gate [ 3x1 ]** |

`DK2.Data.Model.RoomTypeId` carries the complete set.

## Finding — the in-memory terrain record is filled field by field (2026-09-05)

Read from the binary while closing Q153. The game does **not** keep the 552-byte
file record in memory. A loader reads the file record at exactly the offsets
this parser uses, marks each field present in a tagged intermediate, and a
second routine copies every present field into its own slot of a smaller
runtime record. Consequences for anyone reading `DKII.EXE`:

- **Offset arithmetic between the two layouts is unsafe.** Two fields that
  happen to sit at a constant shift (the flags word, `GoldValue`) say nothing
  about a third. Q153 was opened on that arithmetic and closed by dumping the
  file — the supposed field at file `0x1c2` is zero for all 41 terrains — and
  then following the loader instead.
- **The mapping, for the fields this project names** (runtime ← file):
  `+0x17b` ← `0x190` Flags · `+0x17f` ← `0x188` Depth (as a short) · `+0x185`
  ← `0x19a` GoldValue · `+0x187` ← `0x19c` ManaGain · `+0x1a9` ← `0x194`
  Damage · `+0x181`/`+0x183` ← `0x196`/`0x198` (the two shorts after Damage,
  unnamed here) · `+0x1ab` ← `0x1d7` StartingHealth · **`+0x1ad` ← `0x1ff`
  MaxHealth** · `+0x1c4` ← `0x1d6` TerrainId · `+0x1c5` ← `0x1d9`
  MaxHealthTypeTerrainId · `+0x1c6` ← `0x1da` DestroyedTypeTerrainId · `+0x195` ← the 32-byte name at `0x1df`
  (`SoundCategory`, resolved to the sound category handle; sound-effects.md section 7).
- The four `ArtResource`s at file `0x20`, `0x74`, `0xc8`, `0x11c` (84 bytes
  each) are copied whole into the intermediate; the runtime keeps them as
  27-byte handles. The name (32 bytes at `0x0`) is copied as a string.

Facts in our own words; no decompiler output is reproduced here (ADR-0046).

## Cross-parser validation

- Every room's `terrainId` resolves to a terrain flagged room/ownable.
- Prison's `createdCreatureId` = 9 = **Skeleton**; Graveyard's = 8 = **Vampire**
  — matching DK2's actual mechanics, and confirming the offset against the
  independently-parsed creature table.
- Room costs match the known game: Treasury 200, Lair 300, Library 600,
  Training 500, Workshop 600, Prison 750, Torture 1500, Temple 3000,
  Graveyard 2000, Crypt 2000. Portal and Dungeon Heart cost 0 (unbuyable).

## Finding — terrain `damage` is signed

Hero Lair reads 65469 as unsigned, which is **-67** signed: the tile *heals*
heroes standing on it. The field is `short`, not `ushort`. Lava/Water are +100.

## Finding — the wibble and lean bytes (2026-10-09)

Read for the terrain lattice (`terrain-lattice.md`, Q72). The loader `0x004F66E0`
fills runtime `+0x1A1` (wibble H), `+0x1A2..+0x1A4` (lean H[3]), `+0x1A5` (wibble V)
and `+0x1A6..+0x1A8` (lean V[3]) from consecutive intermediate bytes
`+0x286..+0x28D`. With the loader's shift for ManaGain (file `0x19C` → intermediate
`+0x254`) these are file bytes **`0x1CE` wibble H, `0x1CF..0x1D1` lean H[3], `0x1D2`
wibble V, `0x1D3..0x1D5` lean V[3]**, the eight bytes just before TerrainId
(`0x1D6`). **STRONGLY INFERRED**: a shift, which the finding above warns about, but
the dump agrees (every record's `0x1D6` is its own id and the values are sensible):

| Terrain | wibble H | lean H | wibble V | lean V |
|---|---|---|---|---|
| Rock, Gold, Gems | 255 | 255, 255, 255 | 128 | 255, 255, 255 |
| Impenetrable Rock, Fog Of War, Edge of Map | 255 | 32, 32, 0 | 128 | 0, 0, 0 |
| Dirt Path | 255 | 96, 96, 0 | 128 | 0, 0, 0 |
| Claimed Path | 102 | 96, 96, 0 | 13 | 0, 0, 0 |
| Reinforced Wall | 102 | 32, 32, 0 | 13 | 0, 0, 0 |
| Water / Lava | 255 | 64, 64, 128 / 128, 128, 0 | 128 | 0, 0, 0 / 40, 0, 0 |
| Most rooms | 102 or 153 | 32 or 48, 0, 0 | 13 or 19 | 4 or 6, 0, 0 |
| Dungeon Heart, Hero Gates | 0 | 0 or 4, 0, 0 | 0 | 0, 0, 0 |

`KwdTerrainRooms` reads all eight into `TerrainDef.Wibble`; [terrain-lattice.md](terrain-lattice.md) uses them.

## Ceiling: the possession-view roof (Q679)

Read in `DKII.EXE` 2026-11-15 (`0x0043C310`, `0x005A1CA0`, `0x00586BC0`, `0x00587060`,
`0x00452340`, `0x0044F880`, `0x005091C0`, `0x00575A00`, `0x005737E0`); records in
`docs/research/functions/`. Facts in our words. Coordinates are the engine frame
(DK2 fixed point 4096 a tile for positions; heights here are tile units, up is negative z, the
floor is z = -1 and a full-height wall top z = -2).

**Which tiles (STRONGLY INFERRED).** The terrain renderer `0x0043C310` submits one ceiling per
tile through sink slot `+0x104` (`0x005A1CA0`; the only call) when the tile is explored (cell
`+8` bit `0x2000`) or a possession camera (modes 2, 10, 11) is active, its terrain lacks Solid
(`+0x17B` bit 0), its cell lacks `+8` bit `0x4000` (pillar), and game mode is not 5. That is
the arm taken when the renderer's third argument is zero; the other arm (`0x0043CA5F`) submits no
ceiling, and which callers take it is UNREAD. Water, lava,
rooms of any height and doors all get one; solid tiles and pillars get none. A tile with a room
uses the room definition's own art record at `+0x87` when its `+0x92` bit 0 is set, else the
common one. HYPOTHESIS: that record is the ceiling art of `Rooms.kwd` (empty in all 26 shipped
rooms), so every room uses the common record; the loader's copy to `+0x87` was not read.

**What is drawn (STRONGLY INFERRED).** One material for everything: view `+0x3CA` holds the
record of `Ceiling` (loaded by `0x0043AD70`, type 1, so the material `S_Ceiling` of the engine
texture `Ceiling`, mips `CeilingMM0..3` in `DK2TextureCache/EngineTextures.dir`). A piece is a
3 x 3 vertex grid at half-tile spacing over the tile, `(x + c/2, y + r/2)`, 8 triangles with the
diagonal alternating by (row + column) parity, UV `(c/2, r/2)` (the whole texture once per
tile; a -64/32768 bias on v is cancelled by a bit of the packing), normals pointing down,
instance tint black. For the quad at row i, column j with corners v00 = 3i + j, v01 = v00 + 1,
v10 = v00 + 3, v11 = v00 + 4: (v01, v00, v11) and (v11, v00, v10) when i + j is even, else
(v01, v00, v10) and (v11, v01, v10). The item has draw
flag 8: `0x00587060` skips it unless frame global `0x00760B60` is set, which is true only in
camera modes 2, 10, 11 (and a camera path started with plain = 0). It is cached with the tile and rebuilt on terrain change; the cell
invalidation at the camera latch flip (`0x00571DA0`) rebuilds it on entering and leaving possession.

**The height (STRONGLY INFERRED).** The vault is a height field, not the lattice. Each tile cell
holds a nibble (word `+2` bits 12..15) = `min(15, isqrt(16 * d2))`, where `d2` is the squared
distance from the tile's min corner `(x, y)` to the nearest solid-or-pillar tile square
(`0x005091C0`: terrain Solid or cell bit `0x4000`). A tile on the
negative side counts one nearer, so every tile touching the corner has `d2` = 0. `d2` is 0, 1, 2,
4, 5, 8, 9, 10, 13, else 16, giving nibble 0, 4, 5, 8, 11, 12, 14, 15 (`isqrt` floors). It is
set for all tiles at level start (`0x0050BE14`), where tiles outside the map are skipped, and
re-derived by `0x00452340` when a tile's work changes its terrain (digging and claiming pass
through it; `Tile_SetTerrain` calls it only with its sixth argument set and world flag `+0x7239`
bit `0x40` clear), pillars are placed or a room is removed. That routine works twice: over
11 x 11 tiles skipping tiles outside the map, then again over the middle 8 x 8 (x-3..x+4,
y-3..y+4) counting tiles outside the map as solid, and the second result stays. The two differ
only within four tiles of a map edge that is not solid; whether any shipped map has one is
not checked.

```
S(r, c) = (2-c)(2-r) N[x][y] + (2-c) r N[x][y+1] + c (2-r) N[x+1][y] + c r N[x+1][y+1]
z = -2 - S/16 + S*S/2048               // exact; up = -z; S = 0 is -2, S = 60 is -3.9921875
centre (r = c = 1), and each edge midpoint whose orthogonal neighbour tile is on the map and
open at its centre (not nav-solid, not pillar; `0x00509100`): z = min(z, -2.5)
```

So the roof is 1 tile over the floor where a corner touches a wall, about 1.9 at one tile out
(n = 4: z = -2.875), 2.5 at two, 2.9 at three, and levels off just under 3 tiles above the floor
(z = -4) at four tiles or more from any wall: low at walls, high over open space. Vertices go
through the terrain lattice sampler `0x0058F030` like wall pieces, which moves only those with z
in (-2.5, -2.0]: the corners at a wall and the unclamped edge midpoints near one follow the bent
wall tops, and the centre never moves (HYPOTHESIS that the
lattice window is the tile's own during the build; the call chain from the cache build was not read).
Lighting normals come from height differences with a constant z component of 4.0, spread
bilinearly (`0x005A18A0`); the exact corner pairing is UNREAD, so an adapter may use mesh normals.

**Which tiles are visible in possession (HYPOTHESIS).** In possession `0x00575A00` caps the
view distance at 32 tiles and enumerates tiles by rings around the camera tile (`0x005737E0`,
`0x00572F60`), keeping those inside the view polygon and not hidden by blocked tiles in angle:
a 2D line-of-sight set. The renderer's view distance is camera `+0xD8A` (`0x0044D550` =
max(2 * zoom, `+0xD8A`) / 4096; the commit copies preset `ViewDistance` into it and the lens
into `+0xD8E`); preset 2 gives 12 tiles, 8 for mode 10. The occluder bit (cache entry `+0x18` bit 0)
and the interval store `0x00573470` are UNREAD.

**Darkening (HYPOTHESIS).** No fog write, depth term or fog string was found. Default
ambient is 0 (`classic-lighting.md` section 8) and the ceiling's tint is black, as the terrain
pieces' own is, so the surface would be lit only by the point lights its mesh overlaps
(`0x0057AC10`), falling to black beyond their radius; how a tint combines with light is unread:
the roof 3 tiles up over a wide cave may be out of every light's reach. That floors and walls
also fade is not explained by a read term; the 12-tile polygon end is a hard cut. Not read: the
body of `0x00587060` after the queue, the vertex light loop for this item class.

**Not read:** wall faces or tops set only for possession (the item flag `0x10` setters for
terrain pieces; only the Sight fog mesh, flags `0x2090`, is known to carry it, so it is hidden
in possession); the cull mode against the triangle order; the meaning of material flag `0x20000`.

Earlier audit, still true: all 26 shipped room records have zero `+0x368` and empty ceiling
art (file `+0x314`); `0x004EACC0` keeps only its low byte at runtime `+0x455`, which no reader
was found for. The room ceiling height is not an input to the drawn roof. OpenKeeper's distance curve
is not used: this section replaces it.

## Confidence

STRONGLY INFERRED — record sizes, all parsed field offsets, the transition graph, room
ids, grid layout, and the MAP `playerId` and `flags` bytes.

UNKNOWN — MAP tile byte 4; the meaning of `maxHealthTypeTerrainId` for
already-claimed path tiles (Claimed Path points at Rock, which does not fit the
"upgrade" reading).

## Implementation

`DK2.Data.Kwd.KwdTerrainRooms`, `DK2.Data.Kwd.MapGrid`,
`DK2.Data.Model.TerrainDef`, `DK2.Data.Model.RoomDef`,
`DK2.Data.Model.RoomTypeId`, `DK2.Data.Model.PlayerIds`

## Tests

`DK2.Data.Tests.KwdTerrainRoomsTests` (10 test methods, including whole-corpus
map validation)
