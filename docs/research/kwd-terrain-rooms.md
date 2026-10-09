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
84-byte figure is itself CONFIRMED from the creature record's own offset
anchors: `portraitResource` at 0xD40, `Light` at 0xD94, difference 0x54 = 84.

## MAP(100) — the dungeon grid

The chunk header carries `u32 width, u32 height`; content is exactly
`width * height * 4` bytes, row-major, 4 bytes per tile:

```
u8 terrainId    u8 playerId    u8 flags    u8 unknown
```

CONFIRMED across every campaign map: all tile terrain ids resolve against the
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
`BridgesOnRealDataTests`). Behaviour CONFIRMED; that this byte is where the
original keeps it is STRONGLY INFERRED.

**Byte 2 (`playerId`) numbering**, CONFIRMED by sweeping the MAP record of all
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
  MaxHealthTypeTerrainId · `+0x1c6` ← `0x1da` DestroyedTypeTerrainId.
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

Our parser does not read them yet.

## Confidence

CONFIRMED — record sizes, all parsed field offsets, the transition graph, room
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
