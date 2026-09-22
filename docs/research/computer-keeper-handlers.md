# Computer keeper task handlers and room planner

**Scope:** `DKII.EXE` v1.70, MD5 `2f3bbc21eae8c3aa468136baa303036d`.
Addresses are virtual addresses. This opens the handlers summarized in
`computer-keeper-tasks.md` §3. Decompiled text is not reproduced.

## 1. Evidence and notation

| label | meaning |
|---|---|
| **Known** | Read directly from instructions, DK2's debug dumper, or an assertion. |
| **Strongly inferred** | A behavioural role follows from direct data flow, but DK2 does not name it. |
| **Hypothesis** | A plausible interpretation not established here. |
| **UNKNOWN** | The read did not establish the answer. |

**Known:** DK2's dumper supplies every capitalized computer-keeper field name below.
Lower-case descriptions are ours. `Task`, `NextTask`, `CurrentEventTask` and
`NumberOfEventTasks` are nibbles in keeper `+0x00`; the other bit names in that
dword also come from the dumper. `world` (`+0x0A`) and `player` (`+0x0E`) are our
names for pointers established by their use.

**Known:** all eight requested routines were first passed through `FnStrings.py`.
Only `0x004FC690` and `0x004FCE70` reference the assertion path
`ComputerPlayer.cpp`; none exposes a handler name. Handler descriptions in headings
are therefore ours except `RespondToAttack`, which the dumper prints.

## 2. Shared allowance and command path

**Known:** `0x00500C20` copies `TendancySpeed:u8` (`+0x4E3`) into an unsigned
one-byte allowance at the start of every accepted keeper turn. Each handler receives
the remaining byte and returns the remainder; an arm can therefore run repeatedly in
one turn. A processed coordinate or moved creature normally decrements it once.

**Known:** the creation default is 1. The nine built-in personality rows also all
give 1. Shipped computer-player records contain 1, 2 and 4, so the record, not a
separate difficulty multiplier, determines the per-turn value. See `KEEPER_AI.md`
§3.2 for the table provenance.

**Known:** tagging uses the ordinary action dispatcher `0x0050E5C0` with this
20-byte record (trailing padding is not read here):

| offset | value |
|---:|---|
| `+0x00` | packed tile `(x:u16, y:u16)` |
| `+0x04` | the same packed tile (rectangle end) |
| `+0x08` | zero |
| `+0x0C` | action `0x28` (40), tag for digging |
| `+0x10` | owning player's `TagId:u16` (`player+0x04`) |

**Known:** the dispatcher first offers the record to an optional packet observer,
then indexes its action table by `+0x0C`. A zero action result stops the current
handler turn. Successful tags set `UntagGoldCounter` (`+0x4F7`) to 240.

## 3. Inline room planner — `0x004FC690`

### Prologue and result

**Known:** `ECX` is the keeper; there is no stack argument and no meaningful return.
The routine uses an EBP frame, 0x40 bytes of locals, saves `EBX/ESI/EDI`, and returns
with plain `RET`. The task arm has already spent one allowance unit before calling it.

### Decision rule

**Known:** room choice is the following ordered scan in `0x004F93B0` when
`NextRoomToBuildSet` is clear:

1. Visit `RoomList[0..14]` (`+0x4C6..+0x4D4`), then literal room ids 16..26.
2. Take the first nonzero id whose player availability is 3, for which no unflagged
   owned room already exists, which differs from `CurrentRoomToBuild`, and which is
   not 3, 5, 8, 9, 17 or 22.
3. If none qualifies, repeat for availability 2. This pass accidentally reads
   `RoomList[1..15]`; byte 15 is `SpecialFindChance` (`+0x4D5`). A match stores the
   placeholder `0xE5`, not the candidate id.
4. If neither pass succeeds, set `NextRoomToBuild=3`, `NextRoomToBuildSet`, and
   `AllRoomsBuilt`.

**Known:** thus `CurrentRoomToBuild` comes from the keeper's `RoomList` order plus a
fixed 16..26 tail, filtered by the player's room availability and existing rooms.
No creature list, job, anger, capacity or need is read. The personality setter builds
`RoomList` from world room-definition order; campaign records may supply the list.

**Known:** the planner then performs these steps:

1. Copy `NextRoomToBuild` to `CurrentRoomToBuild`; clear `NextRoomToBuildSet` only
   for room 3, otherwise replace `NextRoomToBuild` with 3.
2. For Portal (3) or Hero Portal (25), seek that neutral-room kind (Portal first,
   Hero Portal second). For another kind, seek a neutral room of that kind. It must
   lie in `MaxBuildArea`; the portal case also accepts it only within
   `NeutralRoomRangeFromHeart` of the heart or within
   `2 * NeutralRoomRangeFromClaimedArea + 3` of owned land. Accepted neutral rooms
   start a route (`Task=7`, `NextTask=4`).
3. Let `price = roomDefinition(CurrentRoomToBuild & 0x7F).u16[+0x43F]`. If
   `player.Money < price * DefaultRoomSize` and the unnamed word at
   `player[+0x1105]+0x467C` is zero, start the gold selector instead of a room.
4. Otherwise start `Task=1`. Rooms below id 6 use `DefendDirection`; later rooms use
   `OpenDirection + random(3) - 1`, modulo four. Select an origin from an ideal-room
   table or a random owned non-bridge room, subject to `PlaceRoomsNextToIdeal` and
   whether the ideal kind exists. An `0x80` expansion request first tries to add one
   strip to an existing room; failure clears the bit and falls back to a new room.

**Known:** `DefaultRoomSize` (`+0x4BF`) does not choose a room type. It is both the
planned tile count in the cost estimate and the area passed to the site finder. The
finder uses `side = floor(sqrt(DefaultRoomSize))` and may add one strip to reach a
non-square target.

**Known:** `TendancyMoney` is not the only money gate. It is not read by this
routine: the outer task dispatcher invokes the same gold selector when idle or after
task 4 and `Money < TendancyMoney`. The planner independently uses the whole-room
estimate above; id 3 also consults the per-tile price.

### Fields

| object/offset | access and meaning | confidence |
|---|---|---|
| keeper `+0x00` | R/W direction bits; `NextRoomToBuildSet`, `AllRoomsBuilt`, `Task`, `NextTask` | **Known** |
| keeper `+0x04` | W `TaskTimeCounter=0` on task changes/refusals | **Known** |
| keeper `+0x0A`, `+0x0E` | R world and player pointers (our names) | **Strongly inferred** |
| keeper `+0x12/+0x16/+0x1A/+0x1E` | R `MaxBuildArea` four bounds | **Known** |
| keeper `+0x72`, `+0x76` | W `DigRoomAt`, `DigRoomAtCurrent` | **Known** |
| keeper `+0x98/+0x99` | R/W `CurrentRoomToBuild`, `NextRoomToBuild` | **Known** |
| keeper `+0x9A` | R/W `CurrentGoldArea` | **Known** |
| keeper `+0x4B2` | R `PlaceRoomsNextToIdeal` and expansion controls | **Known** |
| keeper `+0x4BF`, `+0x4E0`, `+0x4E1` | R `DefaultRoomSize`, `NeutralRoomRangeFromHeart`, `NeutralRoomRangeFromClaimedArea` | **Known** |
| player `+0x36`, `+0x7E` | R `RoomList` head and `Money` | **Known** |
| room `+0x08/+0x18/+0x27/+0x2D/+0x2E` | R next link, type, representative tile and two byte coordinates | **Strongly inferred** |
| room definition `+0x43F` | R unsigned per-tile price | **Known** |
| `player[+0x1105]+0x467A/+0x467C` | R two zero/nonzero gates | **UNKNOWN** names and ownership |

### Callees

| callee | role | confidence |
|---|---|---|
| `0x004F93B0` | choose `NextRoomToBuild` by the ordered rule above | **Known** |
| `0x004C5F50` | return `player+0xB43`, `DungeonHeartPosition` | **Known** |
| `0x004C61C0` | find a room of a requested type near a point | **Strongly inferred** |
| `0x004FC3B0` | find owned land within a supplied range | **Strongly inferred** |
| `0x004C6460` | uniformly choose an unflagged owned room of one type | **Known** |
| `0x004FBE00` | try to derive a one-strip expansion rectangle | **Strongly inferred** |
| `0x004C63D0` | randomly choose an owned room, skipping kinds 8, 17 and 22 | **Known** |
| world slot `+0x98` | fetch a room definition by id | **Strongly inferred** |
| `0x005558D0` | world RNG; only the later-room direction uses `random(3)` here | **Known** |
| `0x00501C10` / gold selector | rescan deposits / start task 5; §7 | **Strongly inferred** |

## 4. Task 1 — select a room footprint (`0x004FCE70`)

### Prologue and result

**Known:** `ECX` is the keeper and the sole stack argument is the allowance byte.
The routine saves a scratch copy of `ECX` plus `EBX/ESI/EDI`, has no EBP frame, and
returns with `RET 4`. `AL` is the remaining allowance; upper return bits are
incidental. `DefaultRoomSize` is square-rooted by `0x0065FAE0`, not randomized.

### Decision rule

**Known:** `0x005012A0` searches deterministically from `DigRoomAtCurrent` along the
current direction (N, W, S, E table order):

1. Skip at most `2 * side` tiles that are already tagged or belong to a room.
2. Require an in-map landing tile; reject a solid tile whose descriptor combination
   marks it unsuitable. Record the approach endpoints in `JoinPathStart/End`.
3. If `TilesLeftBetweenRooms` is nonzero, advance that many further suitable tiles.
4. Test a `side * side` area centred across the approach line and extending forward.
   Every tile must be in-map and be either suitable solid terrain, owned empty floor,
   or carry terrain descriptor `+0x1CB` bit 0.
5. Only when `TilesLeftBetweenRooms != 0`, count open perimeter samples and reject
   when `100 * open / samples > Openness`.
6. If `side^2 < DefaultRoomSize`, try one additional strip. Require both rectangle
   corners inside `MaxBuildArea`.

**Known:** there is no heart-distance test and no dedicated gold test. Ownership is
tested only for nonsolid interior floor: it must be this player and have no room.
Walls are not required on all sides; the conditional perimeter-open percentage is
the only surrounding-wall rule.

**Known:** success writes the rectangle traversal fields and starts `Task=2`, costing
one allowance unit. On failure, the handler visits the four directions in a fixed
order around `DefendDirection` (room id < 6) or `OpenDirection` (other rooms). After
the fourth failure, `random(100) >= 5` (95%) re-seeds `DigRoomAtCurrent` from a random
owned non-bridge room and retries; values 0..4 clear the task. Candidate-square
search itself makes no RNG call. Both calls are `0x005558D0` on the world seed at
`world+0x15A0`.

### Fields

| object/offset | access and meaning | confidence |
|---|---|---|
| keeper `+0x00/+0x04` | R/W direction and `Task`; W `TaskTimeCounter=0` | **Known** |
| keeper `+0x12..+0x1E` | R `MaxBuildArea` | **Known** |
| keeper `+0x72/+0x76` | W `DigRoomAt`; R/W `DigRoomAtCurrent` | **Known** |
| keeper `+0x82/+0x86` | W `JoinPathStart`, `JoinPathEnd` | **Known** |
| keeper `+0x8F/+0x90/+0x91/+0x92/+0x93` | W `DigDX`, `DigDY`, `InnerLoopXOrY`, `MaxX`, `MaxY` | **Known** |
| keeper `+0x94/+0x95` | W `SquaresTarget`, `SquaresCurrent` | **Known** |
| keeper `+0x98` | R `CurrentRoomToBuild` | **Known** |
| keeper `+0x4B2/+0x4BF/+0x4E4` | R `TilesLeftBetweenRooms`, `DefaultRoomSize`, `Openness` | **Known** |
| runtime tile `+0x02/+0x08/+0x0A` | R room id, owner, terrain id | **Strongly inferred** |
| terrain definition `+0x17B/+0x1CB` | R placement flags | **Known offsets; UNKNOWN flag names** |

### Callees

| callee | role | confidence |
|---|---|---|
| `0x0065FAE0` | integer floor square root | **Known** |
| `0x005012A0` | deterministic ray/rectangle validator above | **Strongly inferred** |
| `0x005558D0` | `random(100)` for retry versus abandonment | **Known** |
| `0x004C63D0` | random owned non-bridge room for a new origin | **Known** |
| site helper slots `+0x1FC/+0x1D8/+0x208/+0x6C` | bounds/existence, tile lookup and tile/terrain predicates | **Strongly inferred** |

## 5. Task 2 — tag the footprint (`0x004FD030`)

### Prologue and result

**Known:** `ECX` is the keeper; the stack byte is the allowance. The routine installs
an SEH frame, reserves 0x14 local bytes, saves `EBX/EBP/ESI/EDI`, returns the
remaining allowance in `AL`, and uses `RET 4`.

### Decision rule

**Known:** on first entry it initializes the traversal from `JoinPathStart/End` when
needed. For each allowance unit it visits one rectangle coordinate in the row/column
order selected by `InnerLoopXOrY`. If the player's task pool has more than 20 free
entries, it submits action `0x28`; a refused action stops this turn. Completion resets
the cursor and starts `Task=3`. `CurrentRoomToBuild==0xE5` instead clears the task.
The alternate initialization swaps `JoinPathStart/End`, starts `Task=7` with
`NextTask=2`, and routes to the rectangle before tagging it.

### Fields

| object/offset | access and meaning | confidence |
|---|---|---|
| keeper `+0x00/+0x04` | R/W `Task`, `NextTask`; W `TaskTimeCounter=0` | **Known** |
| keeper `+0x72/+0x76` | R/W `DigRoomAt`, `DigRoomAtCurrent` | **Known** |
| keeper `+0x82/+0x86` | R/W `JoinPathStart`, `JoinPathEnd` | **Known** |
| keeper `+0x8F..+0x93` | R rectangle traversal fields | **Known** |
| keeper `+0x98/+0x4F7` | R `CurrentRoomToBuild`; W `UntagGoldCounter=240` | **Known** |
| player `+0x04` | R `TagId` | **Known** |
| `player[+0x1105]+0x4650` | R task-pool free-count-like word; must exceed 20 | **Strongly inferred** |

### Callees

| callee | role | confidence |
|---|---|---|
| `0x0050E5C0` | dispatch the action-`0x28` record from §2 | **Known** |

## 6. Task 3 — build room tiles (`0x004FD2B0`)

### Prologue and result

**Known:** `ECX` is the keeper and the stack byte is the allowance. It installs an
SEH frame, reserves 0x18 bytes, saves `EBX/EBP/ESI/EDI`, returns the remainder in
`AL`, and uses `RET 4`.

### Decision rule

**Known:** each pass spends one allowance unit on the current rectangle coordinate:

1. If world slot `+0x22C` accepts `(x,y,player TagId, roomId)`, call the room-build
   wrapper and return without advancing the cursor.
2. Otherwise, if the tile is not already acceptable owned room/floor, optionally
   submit action `0x28` when it is taggable solid terrain and the pool has more than
   20 free entries.
3. Advance the rectangle cursor. At its end reset the cursor and start `Task=4`.

**Known:** price is `roomDefinition(roomId).u16[+0x43F]`, per tile. The planner first
checks `price * DefaultRoomSize`, but `0x004C5B40` checks current `player.Money`
against one tile. If enough, `0x004C5C30` validates, builds, debits exactly one price
and increments the spent-gold statistic. If insufficient, the wrapper passes its
explicit bypass flag as 1: the lower helper skips both the money refusal and debit.
Thus an intervening loss of gold does not leave a money-caused partial room; remaining
tiles are built without charge. Buildability or action refusal can still leave a
partial footprint.

### Fields

| object/offset | access and meaning | confidence |
|---|---|---|
| keeper `+0x00/+0x04` | W `Task=4`, `TaskTimeCounter=0` on completion | **Known** |
| keeper `+0x72/+0x76`, `+0x8F..+0x93` | R/W rectangle origin, cursor and traversal | **Known** |
| keeper `+0x98/+0x4F7` | R `CurrentRoomToBuild`; W `UntagGoldCounter` after retagging | **Known** |
| player `+0x04/+0x7E/+0x177` | R `TagId`, R/W `Money`, W spent-gold statistic (name not in player dumper) | **Known offsets; Strongly inferred statistic** |
| runtime tile `+0x02/+0x08/+0x0A` | R room, owner and terrain | **Strongly inferred** |
| room definition `+0x43F` | R unsigned per-tile price | **Known** |
| terrain definition `+0x17B` | R taggable/solid flag bit 3 | **Known offset; UNKNOWN name** |
| `player[+0x1105]+0x4650` | R task-pool free-count-like word | **Strongly inferred** |

### Callees

| callee | role | confidence |
|---|---|---|
| world slot `+0x22C` | room placement predicate | **Strongly inferred** |
| world slots `+0x208/+0x1D8` | tile obstruction and runtime-tile lookup | **Strongly inferred** |
| `0x004C5B40` → `0x004C5C30` | human-path room build wrapper and builder | **Known data flow; Flame calls `0x004C5C30` `buildRoom`** |
| `0x00508290` | task kind at a coordinate | **Known offset behaviour; kind names established in `player-task-pool.md`** |
| `0x0050E5C0` | dispatch action `0x28` for solid leftovers | **Known** |
