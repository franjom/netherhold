# DK2 v1.70 terrain and room tables

Extracted from the reference install on 2026-08-29.
Layout and validation: docs/research/kwd-terrain-rooms.md

## Terrain types (Terrain.kwd)
```
 id name                      health   maxHp  gold  mana  dmg ->maxHp  ->dead  flags
  1 Impenetrable Rock              1       1     0     0    0       1       1  solid,impenetrable
  2 Rock                         165     660     0     0    0       9       3  solid,taggable
  3 Dirt Path                      1     660     0     0    0       8       3  
  4 Water                          1       1     0     0  100       4       4  impenetrable,water
  5 Lava                           1       1     0     0  100       5       5  impenetrable,lava
  6 Gold                         660     660  3000     0    0       6       3  solid,taggable
  7 Gems                         100     100  3000     0    0       7       7  solid,impenetrable,taggable
  8 Claimed Path                 660     660     0     1    0       2       3  ownable
  9 Reinforced Wall              900     900     0     1    0       9       2  solid,ownable,taggable
 10 Treasury                   10000   10000     0     0    0      10       8  ownable,room
 11 Lair                       10000   10000     0     0    0      11       8  ownable,room
 12 Portal                     10000   10000     0     0    0      12       8  ownable,room
 13 Hatchery                   10000   10000     0     0    0      13       8  ownable,room
 14 Dungeon Heart              10000   10000     0     0    0      14       8  ownable,room
 15 Library                    10000   10000     0     0    0      15       8  ownable,room
 16 Training Room              10000   10000     0     0    0      16       8  ownable,room
 17 Wooden Bridge               2000    2000     0     0    0      17       8  ownable,room
 18 Guard Room                 10000   10000     0     0    0      18       8  ownable,room
 19 Work Shop                  10000   10000     0     0    0      19       8  ownable,room
 20 Prison                     10000   10000     0     0    0      20       8  ownable,room
 21 Torture Chamber            10000   10000     0     0    0      21       8  ownable,room
 22 Temple                     10000   10000     0     0    0      22       8  ownable,room
 23 Graveyard                  10000   10000     0     0    0      23       8  ownable,room
 24 Casino                     10000   10000     0     0    0      24       8  ownable,room
 25 Combat Pit                 10000   10000     0     0    0      25       8  ownable,room
 26 Stone Bridge                2000    2000     0     0    0      26       8  ownable,room
 27 Fog Of War                     1       1     0     0    0      27      27  solid,impenetrable,taggable
 28 Hero Gate [ Final ]          100     100     0     0    0      28       8  ownable,room
 29 Hero Gate [ Tile ] [<not used>]     100     100     0     0    0      29       8  ownable,room
 30 Edge of Map                    1       1     0     0    0      30      30  solid,impenetrable
 31 Mana Vault                     1    2000     0     0    0      32      31  
 32 Claimed Vault               2000    2000     0   100    0      32      31  ownable
 33 Hero Gate [ 2x2 ]            100     100     0     0    0      33       8  ownable,room
 34 Hero Gate [ FrontEnd ]       144     144     0     0    0      34       8  ownable,room
 35 Hero Lair                    660     660     0     0  -67      35      35  ownable
 36 Hero Stone Bridge            208     208     0     0    0      36       8  ownable,room
 37 Hero Gate [ 3x1 ]            100     100     0     0    0      37       8  ownable,room
 38 Claim Scan                     1       1     0     0    0      38      38  solid,impenetrable,taggable
 39 Mercenary Gate [<not used>]     100     100     0     0    0      39       8  ownable,room
 40 Hero Portal                10000   10000     0     0    0      40       8  ownable,room
 41 Crypt                      10000   10000     0     0    0      41       8  ownable,room
-- 41 terrain types
```

### Full terrain flags

The flag column above is abridged. The complete set, which the M3 tile rules
read structurally rather than by terrain id (`dk2tools kwd terrainflags`):

```
  1 Impenetrable Rock                0x11050043  Solid, Impenetrable, Torch, AllowRoomWalls, RandomTexture, RevealThroughFogOfWar, TerrainLight
  2 Rock                             0x00840009  Solid, Taggable, RandomTexture, DwarfCanDigThrough
  3 Dirt Path                        0x10008000  FillInable, TerrainLight
  4 Water                            0x11101082  Impenetrable, Water, ConstructionTypeWater, TerrainColorGreen, RevealThroughFogOfWar, TerrainLight
  5 Lava                             0x11081102  Impenetrable, Lava, ConstructionTypeWater, TerrainColorRed, RevealThroughFogOfWar, TerrainLight
  6 Gold                             0x11840009  Solid, Taggable, RandomTexture, DwarfCanDigThrough, RevealThroughFogOfWar, TerrainLight
  7 Gems                             0x1100000B  Solid, Impenetrable, Taggable, RevealThroughFogOfWar, TerrainLight
  8 Claimed Path                     0x1000A424  Ownable, Attackable, PlayerColouredPath, ConstructionTypeQuad, FillInable, TerrainLight
  9 Reinforced Wall                  0x1003286D  Solid, Ownable, Taggable, Attackable, Torch, PlayerColouredWall, ConstructionTypeQuad, AllowRoomWalls, Decay, TerrainLight
 10 Treasury                         0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 11 Lair                             0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 12 Portal                           0x20000034  Ownable, Room, Attackable, AmbientLight
 13 Hatchery                         0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 14 Dungeon Heart                    0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 15 Library                          0x10000034  Ownable, Room, Attackable, TerrainLight
 16 Training Room                    0x10000034  Ownable, Room, Attackable, TerrainLight
 17 Wooden Bridge                    0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 18 Guard Room                       0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 19 Work Shop                        0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 20 Prison                           0x10000034  Ownable, Room, Attackable, TerrainLight
 21 Torture Chamber                  0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 22 Temple                           0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 23 Graveyard                        0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 24 Casino                           0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 25 Combat Pit                       0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 26 Stone Bridge                     0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
 27 Fog Of War                       0x0000000B  Solid, Impenetrable, Taggable
 28 Hero Gate [ Final ]              0x20000014  Ownable, Room, AmbientLight
 29 Hero Gate [ Tile ] [<not used>]  0x20000014  Ownable, Room, AmbientLight
 30 Edge of Map                      0x01050043  Solid, Impenetrable, Torch, AllowRoomWalls, RandomTexture, RevealThroughFogOfWar
 31 Mana Vault                       0x10008000  FillInable, TerrainLight
 32 Claimed Vault                    0x10008024  Ownable, Attackable, FillInable, TerrainLight
 33 Hero Gate [ 2x2 ]                0x00800014  Ownable, Room, DwarfCanDigThrough
 34 Hero Gate [ FrontEnd ]           0x00000034  Ownable, Room, Attackable
 35 Hero Lair                        0x0000A404  Ownable, PlayerColouredPath, ConstructionTypeQuad, FillInable
 36 Hero Stone Bridge                0x00000034  Ownable, Room, Attackable
 37 Hero Gate [ 3x1 ]                0x00000014  Ownable, Room
 38 Claim Scan                       0x1000000B  Solid, Impenetrable, Taggable, TerrainLight
 39 Mercenary Gate [<not used>]      0x00000014  Ownable, Room
 40 Hero Portal                      0x20000034  Ownable, Room, Attackable, AmbientLight
 41 Crypt                            0x30000034  Ownable, Room, Attackable, TerrainLight, AmbientLight
-- 41 terrain types
```

### Mana per terrain

The `mana` column above is `ManaGain`. `MaxManaGain` sits beside it in the
record and is dumped here too, because its emptiness is the evidence
(`dk2tools kwd terrainmana`). Every terrain not listed is zero in both columns:

```
 id name                             manaGain maxManaGain
  8 Claimed Path                            1           0
  9 Reinforced Wall                         1           0
 32 Claimed Vault                         100         100
-- 3 of 41 terrain types generate mana
```

Three paying terrains out of 41, and they are exactly the ownable non-room
ones. That partition is what `DK2.Simulation.ManaRules` rests on, and
`ManaOnRealDataTests` asserts it by exhaustion rather than by spot check. See
`mana.md`; `MaxManaGain` is Q37.


## Room types (Rooms.kwd)

```
 id name                      cost sell% terr spawn  size construction         flags
  1 Treasury                   200   100   10     0   1x1 Normal               buildable,walls
  2 Lair                       300   100   11     0   1x1 Normal               buildable,walls
  3 Portal                       0   100   12     0   3x3 ThreeByThree         
  4 Hatchery                   300   100   13     0   3x3 Normal               buildable,walls
  5 Dungeon Heart                0   100   14     0   5x5 FiveByFiveRotated    
  6 Library                    600   100   15     0   3x3 Normal               buildable,walls
  7 Training Room              500   100   16     0   3x3 Normal               buildable,walls
  8 Wooden Bridge              200   100   17     0   1x1 QuadConstruction     buildable
  9 Guard Room                 600   100   18     0   1x1 Normal               buildable
 10 Work Shop                  600   100   19     0   3x3 Normal               buildable,walls
 11 Prison                     750   100   20     9   5x5 DoubleQuad           buildable,walls
 12 Torture Chamber           1500   100   21     0   3x3 Normal               buildable,walls
 13 Temple                    3000   100   22     0   5x5 DoubleQuad           buildable,walls
 14 Graveyard                 2000   100   23     8   3x3 Normal               buildable,walls
 15 Casino                     750   100   24     0   3x3 Normal               buildable,walls
 16 Combat Pit                 750   100   25     0   5x5 DoubleQuad           buildable,walls
 17 Stone Bridge               500   100   26     0   1x1 QuadConstruction     buildable
 18 Hero Gate [ Final ]          0     0   28     0   1x1 HeroGate             walls
 19 Hero Gate [ Tile ] [<not used>]     0     0   29     0   1x1 HeroGateTile         good,walls
 20 Hero Gate [ 2x2 ]            0     0   33     0   1x1 HeroGate2By2         
 21 Hero Gate [ FrontEnd ]     600   100   34     0   1x1 HeroGateFrontEndTile 
 22 Hero Stone Bridge          200   100   36     0   1x1 QuadConstruction     good
 23 Hero Gate [ 3x1 ]            0     0   37     0   1x1 HeroGate3By1         
 24 Mercenary Gate [<not used>]     0     0   39     0   1x1 HeroGate2By2         
 25 Hero Portal                  0   100   40     0   3x3 ThreeByThree         
 26 Crypt                     2000   100   41     0   5x5 Normal               walls
-- 26 room types
```

### Full room flags

Which ground a room may sit on, and whether a player may build it at all, are
read from these rather than from the room's identity — see
docs/research/rooms-and-gold.md and ADR-0016 (`dk2tools kwd roomflags`):

```
  1 Treasury                         0x0000128C  PlaceableOnLand, HasWalls, Buildable, Attackable, HasFlame
  2 Lair                             0x0000128C  PlaceableOnLand, HasWalls, Buildable, Attackable, HasFlame
  3 Portal                           0x00001004  PlaceableOnLand, HasFlame
  4 Hatchery                         0x0000128C  PlaceableOnLand, HasWalls, Buildable, Attackable, HasFlame
  5 Dungeon Heart                    0x00000204  PlaceableOnLand, Attackable
  6 Library                          0x000011AC  PlaceableOnLand, HasWalls, SpecialTiles, Buildable, SpecialWalls, HasFlame
  7 Training Room                    0x000013AC  PlaceableOnLand, HasWalls, SpecialTiles, Buildable, SpecialWalls, Attackable, HasFlame
  8 Wooden Bridge                    0x00001083  PlaceableOnWater, PlaceableOnLava, Buildable, HasFlame
  9 Guard Room                       0x000010A4  PlaceableOnLand, SpecialTiles, Buildable, HasFlame
 10 Work Shop                        0x000013AC  PlaceableOnLand, HasWalls, SpecialTiles, Buildable, SpecialWalls, Attackable, HasFlame
 11 Prison                           0x000013AC  PlaceableOnLand, HasWalls, SpecialTiles, Buildable, SpecialWalls, Attackable, HasFlame
 12 Torture Chamber                  0x000013AC  PlaceableOnLand, HasWalls, SpecialTiles, Buildable, SpecialWalls, Attackable, HasFlame
 13 Temple                           0x0000108C  PlaceableOnLand, HasWalls, Buildable, HasFlame
 14 Graveyard                        0x000010AC  PlaceableOnLand, HasWalls, SpecialTiles, Buildable, HasFlame
 15 Casino                           0x000013AC  PlaceableOnLand, HasWalls, SpecialTiles, Buildable, SpecialWalls, Attackable, HasFlame
 16 Combat Pit                       0x000012AC  PlaceableOnLand, HasWalls, SpecialTiles, Buildable, Attackable, HasFlame
 17 Stone Bridge                     0x00001083  PlaceableOnWater, PlaceableOnLava, Buildable, HasFlame
 18 Hero Gate [ Final ]              0x00000E4C  PlaceableOnLand, HasWalls, NormalTiles, Attackable, Unk10, Unk11
 19 Hero Gate [ Tile ] [<not used>]  0x00002E4C  PlaceableOnLand, HasWalls, NormalTiles, Attackable, Unk10, Unk11, IsGood
 20 Hero Gate [ 2x2 ]                0x00000244  PlaceableOnLand, NormalTiles, Attackable
 21 Hero Gate [ FrontEnd ]           0x00000204  PlaceableOnLand, Attackable
 22 Hero Stone Bridge                0x00002003  PlaceableOnWater, PlaceableOnLava, IsGood
 23 Hero Gate [ 3x1 ]                0x00000244  PlaceableOnLand, NormalTiles, Attackable
 24 Mercenary Gate [<not used>]      0x00000244  PlaceableOnLand, NormalTiles, Attackable
 25 Hero Portal                      0x00001004  PlaceableOnLand, HasFlame
 26 Crypt                            0x0000102C  PlaceableOnLand, HasWalls, SpecialTiles, HasFlame
-- 26 room types
```

## Sample level grids

```
$ dk2tools kwd map maps/level1Map.kld
30 x 38 tiles
terrain histogram:
  terrain   1:    776 tiles
  terrain  30:    132 tiles
  terrain   2:    113 tiles
  terrain   9:     45 tiles
  terrain  14:     25 tiles
  terrain   8:     18 tiles
  terrain   3:      9 tiles
  terrain   6:      9 tiles
  terrain  12:      9 tiles
  terrain  33:      4 tiles
owners: p1=32, p2=1048, p3=60

$ dk2tools kwd map maps/AlcatrazMap.kld
61 x 61 tiles
terrain histogram:
  terrain   2:   1438 tiles
  terrain   5:    543 tiles
  terrain   1:    490 tiles
  terrain   8:    409 tiles
  terrain   9:    358 tiles
  terrain  30:    240 tiles
  terrain   6:     89 tiles
  terrain  20:     60 tiles
  terrain  14:     50 tiles
  terrain  12:     36 tiles
  terrain  36:      4 tiles
  terrain   7:      4 tiles
owners: p1=783, p2=2840, p3=49, p4=49
```
