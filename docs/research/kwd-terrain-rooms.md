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
u8 terrainId    u8 playerId    u8 flags (bridge terrain)    u8 unknown
```

CONFIRMED across every campaign map: all tile terrain ids resolve against the
TERRAIN table (asserted in tests). Example — `level1Map.kld`, 30 x 38:

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

Combined with the VARIABLES health deltas already extracted
(`MineGoldHealth=-300`, `ClaimTileHealth=825`, `DigRockHealth=-5000`,
`ReinforceWallHealth=50`, `RepairWallHealth=22`), this gives the complete
numeric model for digging, claiming and reinforcing — pending confirmation of
how often the deltas are applied (tied to open question Q1, the tick rate).

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

`DK2.Data.Model.RoomTypeId` now carries the complete set. This is a small
contribution back: our data resolves unknowns in the best existing binary RE.

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

## Confidence

CONFIRMED — record sizes, all parsed field offsets, the transition graph, room
ids, and grid layout. Asserted across 10 tests including whole-corpus map
validation.

UNKNOWN — MAP tile byte 3 (`flags`, bridge-related) and byte 4; the meaning of
`maxHealthTypeTerrainId` for already-claimed path tiles (Claimed Path points at
Rock, which does not fit the "upgrade" reading); MAP `playerId` numbering
(level1 shows p1=32, p2=1048, p3=60 — which id means neutral is not yet known).

## Implementation

`DK2.Data.Kwd.KwdTerrainRooms`, `DK2.Data.Kwd.MapGrid`,
`DK2.Data.Model.TerrainDef`, `DK2.Data.Model.RoomDef`,
`DK2.Data.Model.RoomTypeId`

## Tests

`DK2.Data.Tests.KwdTerrainRoomsTests` (10 tests)
