---
systems: [simulation-core, heroes]
---
# The level start's world-seed draws (Level_InitTriggersAndStart)

`Level_InitTriggersAndStart` (`0x0050B5B0`) makes three direct calls of `Random_Next` (`0x005558D0`), all on the world
seed (`world + 0x15A0`) and all logged against `World.cpp` (string `0x006A4BA0`): `0x0050C572` (line 0x969),
`0x0050C5F6` (line 0x971) and `0x0050C76C` (line 0x99D). STRONGLY INFERRED: whole disassembly `0x0050B5B0-0x0050C800`
read 2026-11-05; a forward direct-call search from every callee to `0x005558D0` (depth 7) found no other direct call
in the body. Indirect (vtable) calls were not followed.

## 1. The Hero Lair sweep (lines 0x969 and 0x971)

After the load-time torch call and before the transfer creature, `0x0050C4B5-0x0050C643` walks every tile, row by
row with x inside y (`+0x9E6` width, `+0x9EA` height, 16-byte cells from `+0x9EE`). STRONGLY INFERRED.

1. After the torch test (`0x00452D80`, `wall-torches.md` section 6), the tile continues only if its terrain byte
   `+0x0A` is `0x23` (terrain 35, Hero Lair) and its byte `+0x0C` is zero.
2. It asks `Cell_FindObjectOfType` (`0x004537E0`, type `0x71`) of each of the eight neighbours, in the order of
   the table at `0x006BC370` ((0,-1), (1,-1), (1,0), (1,1), (0,1), (-1,1), (-1,0), (-1,-1)), through world slot `+0x1D8`
   (no bounds test, `World_TileCell`). One found ends the tile's turn without a draw. The tile's own cell is not asked.
3. `random(4)` (line 0x969); a non-zero result ends the turn.
4. On zero, `Object_Create` (`0x004B7A90`; arguments type, owner, position, out) makes object `0x71` for the player
   in `[0x006F254C]` (the hero player, Q266) at the tile centre, height 0. If it succeeds, `random(0x800)` (line 0x971),
   masked with `0x7FF`, is stored in the new thing's `+0xF4` (heading).

The scan is not gated by world `+0x7239` bit `0x40`: DK2 runs it on a restore too. Objects made earlier in the scan
count for later tiles, and so do objects the map placed.

**Byte `+0x0C` is zero only for an interior tile.** `Map_TileNeighbourCodes` (`0x00450470`) sets it, and
`Tile_SetTerrain` (`0x0044FC40`) calls it for the 3x3 around the tile at `0x00450293` (radius 1) when its argument 5 is
non-zero, and the map loader (`0x0051EC60`) passes 1 at all three of its calls; so at the end of the map load every
tile holds the live mask. For a tile with no room, a terrain that is neither Solid
nor ConstructionTypeWater and no door, bit k is set when neighbour k is off the map or has another terrain, and
nothing else sets it. Zero therefore means all eight neighbours are on the map and are terrain 35. A tile with a
room word goes to `Room_TileCode` and its byte is not written here, so the claim that no room uses terrain 35 carries
weight. HYPOTHESIS for the interior rule (decompile of `0x00450470`, the call site read in disassembly): the step
that no later writer (`Map_SeedLocalExploration`, `Fog_ExploreAll`, `Level_LoadDoorThing` also call it) leaves a
terrain-35 tile's byte non-zero before the scan was not checked. The draw sites, their order and the neighbour test
are STRONGLY INFERRED. Terrain 35 carries Ownable, PlayerColouredPath,
ConstructionTypeQuad and FillInable only, and no room uses it (shipped data, 2026-11-05; `HeroLairOnRealDataTests` pins it).

**What object `0x71` is.** `Hero Armoury Trestle` in the shipped OBJECTS (flags Obstacle, BoulderCanRollThrough,
Placeable). Nine shipped levels also place it through THINGS (Level 4 has 12).

**Where it bites.** 13 shipped maps (those with a Variables.kld) hold terrain 35; the tiles with all eight neighbours also terrain 35 number: Level 16
1, Level 17 1, Patrol 2, TrapTime 2, mpd1 9, mpd2 8, mpd3 9, mpd4 3, mpd5 6, mpd7 2 (`HeroLairOnRealDataTests`). No other
shipped map has an interior tile, so Level 4 and the other campaign levels draw nothing here.

Built as `HeroLairRules.InitializeLevelStart` (ADR-0673), called after the trigger executor is made, which is after
the Hatchery coops (`0x0050BC9D`) and event 3 (`0x0050BDAC`) in DK2's order.

## 2. The transfer creature's facing (line 0x99D)

`0x0050C67D-0x0050C7BF` is as `campaign-map-and-progression.md` section 4 says. The draw is
`random(4) << 9` after `Creature_KickToState` (`0x0048AD30`, state 1) and before the `Creature_LevelUp` loop. The block
is already built: `TransferCreatureRules.Arrive` (`src/DK2.Simulation/CampaignCarry.cs`), called by the Unity host
(`DungeonRunner.LevelEnd.cs`) after the simulation is made, which is after the sweep above, as in DK2. The facing
is one draw after the creature's identity draws. The creature is made by `0x004B78D0` (identity draws through
`0x00491900` and `0x004919C0` in `Creature_Construct`, bound to the stream by `BindCreatureIdentityRandom`).

## 3. Callee census

The callees that reach `Random_Next` by direct calls: `Room_Rebuild` (`0x004EDFC0`), `Object_Create` (through
`Object_Init`, a Chicken's scale only), `Creature_LevelUp`, `Trigger_FireCreatureEvent` (event 3; through trigger
actions), `CreatureManager_CreateAtFreeSlot`, `Room_ApplyHealthDelta` and `World_ApplyModeStartupRules` (through the
heart's health routine, no level-start draw read). `Room_Rebuild` runs per active player and room list (`0x0050BC9D`). Its own body (`0x004EDFC0-0x004EF200`, file string
`RoomManager.cpp`) draws on a stack seed made from the tile coordinates everywhere but one site, and two of its callees
draw on the world seed:

| site | draw | state |
|---|---|---|
| `Hatchery_PlaceCoops` (`0x004DE220`, call `0x004DE4EC`) | `random(4)` per coop | built, ADR-0672 |
| `Graveyard_PickCryptPoint` (`0x004DD9B0`) | `random(count)` | built, `GraveyardRules` |
| `Room_Rebuild` wall pass, Training Target (object 29), call `0x004EECCB`, line 0x881 | `random(0x1000)` into `+0xD4`/`+0xD8`, one per wall target | [neighbor work built at 437](training-target-rebuild.md); level start unbuilt |

The Training Target draw is made at level start, room tile changes and after
neighbor work. The [neighbor-work subset](training-target-rebuild.md) is built
at 437. Startup, room additions/removals, capture merges and the object's
animation phase remain unmirrored.

Not read: `Graveyard_CreateVampire` (`0x004DD820`) draws on the world seed (`0x004DD88C`) and is reachable from
`Graveyard_AdoptCorpses`, which a fresh level should not reach; the trigger actions event 3 runs; and every indirect call.

## 4. Not mirrored

- The restore: DK2 runs the sweep with world `+0x7239` bit `0x40` set; ours skips it (`levelStart` false), so a restored
  world never gains a trestle.
- A door or a room on the tile (the mask's other arms), and a missing definition for object `0x71` (DK2 draws
  `random(4)` and then Object_Create refuses; ours does the same), the object pool failing, and the thing's position and owner fields
  other than the tile and heading.
