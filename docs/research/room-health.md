# Room health: conversion, demolition and repair (Q500)

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 (read 2026-10-01 and 2026-10-02, addresses below); the Level 1 and Level 2 full-state captures (`reverse-engineering/dk2runs/level1-live1/seed01`, `level1-fullstate-20260928`, `level2-fullstate-20260928`), room record `+0x35`; `Rooms.kwd`, `Terrain.kwd`, `Creatures.kwd`, the shipped `*Things.kld` |
| Confidence | **CONFIRMED** by capture: the applier, growth, the conversion rate and both arms' arithmetic, the takeover and its reset. **STRONGLY INFERRED** (binary): the removal share, action 11 and who starts it, action 12 and that nothing starts it |
| Implementation | `RoomHealthRules`, `TileState.RoomHealthLost`, `SlabChain`, `Systems/RoomHealthSystem`, `Systems/TileWorkSystem` (ADR-0336); `RoomAttackRules`, `Systems/HeroSystem`, `TileWorkKind.AttackRoom` and `RepairRoom` (ADR-0339) |
| Tests | `RoomHealthTests`, `RoomConversionTests`, `RoomAttackTests`, `RoomHealthOnRealDataTests` |
| Open questions | Q523 (job 11's room attack, unbuilt), Q525 (Level 2's party walk) |

Field names are DK2's own (`room-object.md`, dumper `0x004E70B6`): room `+0x35` `Health`,
`+0x1D` `PlayerId`, `+0xA7` `Size`, `+0x9A` bit 0 `Destroyed`. Terrain `+0x1AB` is
`StartingHealth` and `+0x1AD` `MaxHealth` (`kwd-terrain-rooms.md`), reached through the room
definition's terrain byte `+0x44E`. Room definition flags sit at `+0x43D` in the file's order:
bit 0 `PlaceableOnWater`, bit 2 `PlaceableOnLand`, bit 7 `Buildable`.

## 1. One word per room, one applier

`Room_ApplyHealthDelta` (`0x004E5300`, thiscall, a delta pointer and a player): a negative
delta subtracts, and a result not above 0 hands the room whole to the player through
`Room_TransferOwnership` (`0x004E5650`) and then sets `StartingHealth × Size`. A non-negative
delta adds and clamps at `MaxHealth × Size`. The transfer never writes `+0x35`: its only
writers are `Room_Construct`, this applier and `Room_SetHealth` (`0x004E53A0`), so a room
handed over on defeat keeps its damage.

Every shipped room terrain has `StartingHealth = MaxHealth`: 10000, the two bridges 2000, the
Hero Stone Bridge 208, the gates 100 (the FrontEnd gate 144). A room's cap is that times its size.

## 2. What moves it

| event | address | effect |
|---|---|---|
| a new one-tile room | `0x004EC5A0` | `Room_SetHealth(StartingHealth)` |
| a tile joins | `0x004EC650` | `+StartingHealth`, clamped at `MaxHealth × new Size` |
| two rooms merge | `0x004ECB70` | `+` the absorbed room's health, clamped |
| a tile leaves | `0x004ED440` | each piece gets the old room's share, below |
| the room tick | `0x004E4B00` | under the cap, `+HealthGain` (Rooms.kwd `0x41D`): 0 in all 26 rooms |
| level start, Neutral's rooms | `0x0050B966` | `+Health / Size`, clamped: changes nothing while start = max |
| a pre-placed room thing | `0x005215BA` | scaled to its percentage word if not 100; the heart is built by `0x004DB700` instead |
| action 10, conversion | `0x00497BF0` | §3 |
| action 12, repair | `0x00498120` | §6 |

**A tile leaving keeps the damage proportional.** `0x004ED440`, reached only through
`Player_SellRoomTile` (`0x004C5DB0`), takes the share before the tile goes:
`pct = ((Health << 12) / (MaxHealth × Size)) × 100 >> 12`, a whole percent. `0x004ED7B0`
removes the tile and rebuilds the pieces, and each gets `max(1, MaxHealth × its Size × pct / 100)`.
An undamaged room cut in two stays full.

Of the 275 room things in the 66 shipped maps, two are not at 100: Level 7's (1) and Level
8's (0), both Dungeon Hearts. The map loader's arm is therefore inert on shipped data.

**Capture check.** Level 1 seed 1: the Hatchery reads 10000, 30000, 60000, 80000, 90000 as it is
built to 1, 3, 6, 8, 9 tiles; the Lair grows to 150000 at 15; the heart reads 250000 for 25
tiles; the heroes' 2×2 gate 400.

## 3. Conversion, action 10

`0x00497BF0`, started only from imp state 18 (`0x00466220`). It hits the room under the action
point, never the tile:

- the gate is `Tile_IsEnemyRoom` (`0x00454040`, `imp-claim-path.md`);
- a tile owned by the Neutral player (`[0x006F2548]`) takes `[0x006F257C]`, anyone else's
  `[0x006F2574]`;
- the delta is `(value << 12) × rate >> 12 >> 12`, the rate being imp `+0x3AF` plus `+0x3B3`
  (`tile-work-efficiency.md`), a floor;
- `0x004E5300` applies it with the imp's owner. The action continues while the gate holds.

**`0x006F257C` is variable 136, `ConvertNeutralRoomHealth`, not `Unknown_17`.** The per-second
handler `Variables_SetPerSecond` (`0x0051CB80`) divides by variable 19 and routes id 136 to
`0x006F257C` (byte table `0x0051CCD8`, jump table `0x0051CC94`). Id 17 lands on the no-op in both
variable handlers; it is the research list (`0x0051DB00`). `balance-variables-in-the-binary.md`
listed both names at one address.

| | per application at 80% (`0xCCC`) | at 90% (`0xCCC + 0x199`) |
|---|---|---|
| a rival's room, −4000 / 4 = −1000 | −800 | −900 |
| a neutral room, −20000 / 4 = −5000 | −4000 | −4499 |

**CONFIRMED by capture.** Level 1 seed 1's neutral portal (room slot 1125): 90000, then 4000 off
at tick 890 and every tick from 893 to 913, and at 914 the 23rd application hands it from
player 2 to 3 at 90000. `l1win927b` and `l2win928` show the 4499 step, 21 applications, then
90000. That is also Q92's time: the measured 24–26 ticks of state 18 (`imp-claim-path.md`
§7) are these 23 or 21 applications and their start.

| room | rival's, 80% | neutral, 80% |
|---|---|---|
| 1 tile | 13 | 3 |
| 2 tiles | 25 | 5 |
| 3×3 | 113 | 23 |
| ruined 5×5 heart | 313 | — |

## 4. Demolition, action 11

`0x00497DF0` (not a Ghidra function; entry after `0x00497DE2`'s `RET`, its own SEH frame,
installed as action record 11 at `0x004952CF`). It damages the **tile's** health, through
`Map_ApplyTileWork` (`0x004508D0`) and `Tile_ApplyWork` (`0x00452F60`), never the room's word:

- the tile must carry a room whose definition is `Buildable`;
- a creature of the Good player (`[0x006F254C]`) applies `AttackRoomHealth / 4 × 10` = −3750
  with no rate and with experience, and posts message 0x22 to the room's owner while the tile
  stands, then 0x24 (the owner's last room of the type) or 0x25 when it falls;
- anyone else applies `AttackRoomHealth / 4 × rate` (−300 at 80%) with no experience or message.

A room tile's own health is its terrain's 10000 (2000 a bridge): three applications by a hero,
34 by anyone else at 80%. At 0 `Tile_ApplyWork` calls `Player_SellRoomTile` on the tile's owner
with pay flag 0, so the tile is unmade the way a sale unmakes it with **no refund**, then turns
into its destroyed terrain (Claimed Path, the owner's; a bridge leaves its water or lava,
`BridgeRules.RevertsTo`). The room goes **tile by tile**, the survivors keeping their share of
health (§2), and a treasury tile's gold falls to the floor (`gold-on-the-floor.md` §4).

## 5. Who starts action 11

Three state handlers, and nothing else: all 109 calls of the four action starters are in
`.text`, none in `cseg`, and 105 push a literal (`creature-action-table.md` §4).

- **State 267 (destroying rooms), from the creature update.** `Creature_Update`
  (`0x00488930`) at `0x00488F61`–`0x0048902E`: on entering a new tile, a creature of the Good
  player with `DestroysRooms` (`+0x3EA` bit 7), not dying, not a pit fighter, passing
  `Combat_MayLookForFight(0)`, on a tile owned by neither Good nor Neutral whose room is
  `Buildable` and `PlaceableOnLand`, walks to that tile's centre (`0x004585C0`) and resumes in
  267. `0x00458620` then starts action 11 there while the tile has a room, and ends when it has
  none. That is the only fixed-bit reader of `DestroysRooms`. Party members get it from `Flags2`
  bit 0, which 825 of the 4,545 shipped Good members set, and every creature a trigger creates
  has it (`trigger-dispatch-tables.md`): those heroes break the rooms they walk across.
- **States 179–181 (attacking rooms), from job 11.** `Job_DestroyEnemyRooms` (`0x004582C0`, the
  dispatcher's only arm for job 11) picks a target keeper and a room (`0x0050EA20`,
  `0x004C60E0`, unread), and 179–181 walk there and run action 11 tile after tile; a leader in
  180 hands job 11 to its idle followers. Job 11 is `DestroyEnemyRooms`; only Prince Tristran's
  unhappy table and the Stone Knight's angry table list it, and no shipped party has it as an
  objective. Unbuilt (Q523).
- **Imps never.** Imp states 17 and 18, also "destroying rooms" in the state table, run action
  10. An enemy imp converts a room; it does not knock one down.

## 6. Repair, action 12

`0x00498120`: on the tile's room, when `Room_NeedsRepair` (`0x004E6C00`) holds, that is the room
is the creature's keeper's and its health is below `MaxHealth × Size`, it adds `RepairRoomHealth`
(20000 / 4 = 5000) × rate through the applier, which clamps at the cap. **Nothing starts it**: no
literal 12 among the 105 pushes, and the four computed indices are 0x41–0x46 (Casino, Combat
Pit) and the two tables at `0x00699F30`/`0x00699EB8`, whose actions are 0x47–0x4E.
`RepairRoomHealth` has one reader, this handler (`0x004981A4`); action 1 (`0x00496E00`) reads
only variables 8, 9 and 10. With `HealthGain` 0 everywhere, **a damaged room is never repaired**:
a half-converted room stays at what was left, and the next imp continues from there.

## 7. Not built, and why

- The messages 0x22, 0x24 and 0x25, here and in imp state 17: their rows are unread, and the imp's own
  conversion message is not built either.
- The level-start pass and the map loader's percentage (§2): inert on the shipped data.
- Job 11 and states 179–181 (Q523).
