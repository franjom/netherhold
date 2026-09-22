# Computer keeper task handlers and room planner

**Scope:** `DKII.EXE` v1.70, MD5 `2f3bbc21eae8c3aa468136baa303036d`. Addresses are
virtual. This opens `computer-keeper-tasks.md` §3; decompiled text is not reproduced.

## 1. Evidence and notation

| label | meaning |
|---|---|
| **Known** | Read directly from instructions, DK2's debug dumper, or an assertion. |
| **Strongly inferred** | A behavioural role follows from direct data flow, but DK2 does not name it. |
| **Hypothesis** | A plausible interpretation not established here. |
| **UNKNOWN** | The read did not establish the answer. |

**Known:** DK2's dumper supplies every capitalized computer-keeper field name below;
lower-case descriptions are ours. `Task`, `NextTask`, `CurrentEventTask` and
`NumberOfEventTasks` are nibbles in keeper `+0x00`; the other bit names in that
dword also come from the dumper. `world` (`+0x0A`) and `player` (`+0x0E`) are our
names for pointers established by their use.

**Known:** all eight routines were first passed through `FnStrings.py`. Only
`0x004FC690` and `0x004FCE70` reference the assertion path
`ComputerPlayer.cpp`; none exposes a handler name. Handler descriptions in headings
are therefore ours except `RespondToAttack`, which the dumper prints.

## 2. Shared allowance and command path

**Known:** `0x00500C20` copies `TendancySpeed:u8` (`+0x4E3`) into an unsigned
one-byte allowance at the start of every accepted keeper turn. Each handler receives
the remaining byte and returns the remainder; an arm can therefore run repeatedly in
one turn. A processed coordinate or moved creature normally decrements it once.

**Known:** the creation default and all nine built-in personalities give 1. Shipped
computer-player records contain 1, 2 and 4, so the record, not a
separate difficulty multiplier, determines the per-turn value. See `KEEPER_AI.md`
§3.2 for the table provenance.

**Known:** tagging uses the ordinary action dispatcher `0x0050E5C0` with this 20-byte
record (trailing padding is not read here):

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

**Known:** keeper R/W `+0x00` directions, `NextRoomToBuildSet`, `AllRoomsBuilt`,
`Task/NextTask`; W `+0x04 TaskTimeCounter`; R `+0x12..+0x1E MaxBuildArea`; W
`+0x72/+0x76 DigRoomAt/Current`; R/W `+0x98/+0x99 Current/NextRoomToBuild` and
`+0x9A CurrentGoldArea`; R `+0x4B2 PlaceRoomsNextToIdeal`/expansion controls,
`+0x4BF DefaultRoomSize`, `+0x4E0/+0x4E1` neutral-room ranges; player R
`+0x36 RoomList`, `+0x7E Money`; room definition R `+0x43F` price.
**Strongly inferred:** keeper `+0x0A/+0x0E` world/player pointers; room
`+0x08/+0x18/+0x27/+0x2D/+0x2E` next/type/position. The two gates at
`player[+0x1105]+0x467A/+0x467C` have **UNKNOWN** names.

### Callees

**Known:** `0x004F93B0` chooses the room; `0x004C5F50` gets
`DungeonHeartPosition`; `0x004C6460` chooses an unflagged room of one type;
`0x004C63D0` chooses a non-bridge room; `0x005558D0` is the RNG. **Strongly
inferred:** `0x004C61C0` finds a typed room near a point, `0x004FC3B0` finds nearby
owned land, `0x004FBE00` derives an expansion strip, world slot `+0x98` fetches a
room definition, and `0x00501C10` rescans gold (§7).
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

**Known:** keeper R/W `+0x00` direction/`Task`, W `+0x04 TaskTimeCounter`; R
`+0x12..+0x1E MaxBuildArea`; W `+0x72 DigRoomAt`, R/W `+0x76 DigRoomAtCurrent`; W
`+0x82/+0x86 JoinPathStart/End`, `+0x8F..+0x93 DigDX/DigDY/InnerLoopXOrY/MaxX/
MaxY`, `+0x94/+0x95 SquaresTarget/Current`; R `+0x98 CurrentRoomToBuild`,
`+0x4B2/+0x4BF/+0x4E4 TilesLeftBetweenRooms/DefaultRoomSize/Openness`.
**Strongly inferred:** runtime tile R `+0x02/+0x08/+0x0A` room/owner/terrain.
Terrain-definition flag offsets `+0x17B/+0x1CB` are **Known**, names **UNKNOWN**.

### Callees

**Known:** `0x0065FAE0` is integer square root, `0x005558D0` is `random(100)`, and
`0x004C63D0` chooses the new room origin. **Strongly inferred:** `0x005012A0` is the
validator; world slots `+0x1FC/+0x1D8/+0x208/+0x6C` provide bounds, tiles and
terrain predicates.
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

**Known:** keeper R/W `+0x00 Task/NextTask`, W `+0x04 TaskTimeCounter`, R/W
`+0x72/+0x76 DigRoomAt/Current`, `+0x82/+0x86 JoinPathStart/End`; R
`+0x8F..+0x93` traversal, `+0x98 CurrentRoomToBuild`; W `+0x4F7
UntagGoldCounter`; player R `+0x04 TagId`. `[player+0x1105]+0x4650` is a
**Strongly inferred** task-pool free count (>20).

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

**Known:** keeper W `+0x00/+0x04 Task/TaskTimeCounter`, R/W `+0x72/+0x76` and
`+0x8F..+0x93` rectangle state, R `+0x98 CurrentRoomToBuild`, W `+0x4F7
UntagGoldCounter`; player R `+0x04 TagId`, R/W `+0x7E Money`, W `+0x177` spent
gold (**Strongly inferred** name); room definition R `+0x43F` price. Runtime tile
R `+0x02/+0x08/+0x0A` and task-pool `+0x4650` roles are **Strongly inferred**;
terrain flag `+0x17B` name is **UNKNOWN**.

### Callees

| callee | role | confidence |
|---|---|---|
| world slot `+0x22C` | room placement predicate | **Strongly inferred** |
| world slots `+0x208/+0x1D8` | tile obstruction and runtime-tile lookup | **Strongly inferred** |
| `0x004C5B40` → `0x004C5C30` | human-path room build wrapper and builder | **Known data flow; Flame calls `0x004C5C30` `buildRoom`** |
| `0x00508290` | task kind at a coordinate | **Known offset behaviour; kind names established in `player-task-pool.md`** |
| `0x0050E5C0` | dispatch action `0x28` for solid leftovers | **Known** |
## 7. Task 5 — choose the start of a gold tunnel (`0x004FD660`)

**Prologue/result — Known:** `ECX` is the keeper and the stack byte is allowance. It
reserves 0x10 bytes, saves `EBX/EBP/ESI/EDI`, returns allowance minus one in `AL`, and
uses `RET 4`. It has no SEH frame.

**Rule — Known:** the target was selected by `0x004FD590`: take
`GoldDeposits[CurrentGoldArea]` when its size dword is nonzero, copy its `(x,y)` to
`DigRoomAt`, reset `CurrentGoldArea=0`, rescan deposits, then start task 5. The scan
flood-fills gold areas, uses each centroid, scores it by squared tile distance to the
nearest owned room, and retains the four lowest scores in ascending order. Thus the
normal index 0 is the nearest remembered deposit; no random choice occurs.

**Known:** task 5 walks `player.RoomList` and minimizes
`(deposit.x-room.x)^2 + (deposit.y-room.y)^2`; `<`, not `<=`, leaves a tie with the
earlier list entry. It writes that room's representative tile to `DigRoomAtCurrent`,
or `DungeonHeartPosition` when there is no owned room, then sets `Task=7`,
`NextTask=6`. The requested “nearest owned creature” premise is refuted: no creature
list is read.

**Fields — Known:** keeper `+0x00/+0x04` (`Task`, `NextTask`, `TaskTimeCounter`),
`+0x2E..+0x4D` (`GoldDeposits[4]`), `+0x72/+0x76` (`DigRoomAt/Current`), `+0x9A`
(`CurrentGoldArea`); player `+0x36` (`RoomList`) and `+0xB43`
(`DungeonHeartPosition`); room `+0x08/+0x27` next/position (**Strongly inferred**).
**Callee — Known:** `0x004C5F50` returns the heart-position field.
## 8. Task 6 — expose a gold area (`0x004FD750`)

**Prologue/result — Known:** `ECX` plus an allowance byte; SEH frame, 0x3C local
bytes, saved `EBX/EBP/ESI/EDI`, `AL` remainder, `RET 4`.

**Rule — Known:** starting at `DigRoomAt`, examine the centre and then an expanding
square spiral. The state uses `DigDX/DigDY`, `InnerLoopXOrY`, `MaxX/MaxY`; the first
post-centre state is `(dx,dy)=(0,1)`, inner axis 2, extents 1/1. Tag a tile only when
it exists, terrain word `+0x185` is nonzero, and terrain flags `+0x17B` include bit 3.
Each examined coordinate costs one allowance. When the next extent exceeds 6, clear
`Task`; action refusal pauses without retiring the task.

**Fields — Known:** keeper `+0x00/+0x04`, `+0x72/+0x74` target, `+0x76/+0x78`
cursor, `+0x8F..+0x93` spiral, `+0x4F7` counter; player `+0x04` `TagId` and the
unnamed task-pool word at `[+0x1105]+0x4650` (>20). Terrain flag names are
**UNKNOWN**. **Callees — Strongly inferred:** world slots `+0x1FC/+0x6C` test
existence/fetch terrain; **Known:** `0x0050E5C0` submits §2's tag action.

## 9. Task 7 — route and wall-hug (`0x004FDC50`)

**Prologue/result — Known:** `ECX` plus allowance byte; SEH frame, 0x2C locals, saved
`EBX/EBP/ESI/EDI`, `AL` remainder, `RET 4`.

**Rule — Known:** reject a target outside `MaxBuildArea`; a continuation other than
0 or 4 becomes task 4. Otherwise spend one allowance per walker result until one of:

1. Current equals target, a direct route succeeds, a required bridge cannot be built,
   or the walker fails: copy `NextTask` to `Task`, reset `TaskTimeCounter`.
2. On solid/taggable terrain, submit action `0x28` only with more than 51 free task
   entries; water (`+0x17B` bit 7) builds room 8 and lava (bit 8) room 17 when that
   room's availability is 3 and placement accepts it.
3. During an attack (`BlibBlubBlob` low nibble nonzero), if `0x004FDB90` finds an
   enemy-owned tile in the current tile's 3x3 neighbourhood, decrement that enemy's
   `TunnelsLeft`, store current→target in its first empty eight-slot tunnel entry,
   clear the low nibble, and resume `NextTask`.

**Wall-hug — Known:** `0x00502230` first steps toward the target on the axis with the
larger absolute difference (ties use Y). Heading is 0=N, 1=E, 2=S, 3=W. On an
undiggable obstacle it records `WallHugStart` at the first obstruction,
`WallHugEnd` at the straight-line exit, and `WallHugDistance` as the first
obstruction's squared tile distance to the target. `0x005027D0` trials both sides;
its boolean sets `WallHugLeft`, and `WallHugHeading` is turned one quarter left or
right. Edge following tries that side first, rotates through at most four headings,
returns diggable rock for tagging, and ends wall-hug at `WallHugEnd`. Returning to
`WallHugStart` fails. Each side trial is bounded to 24 turns.

**Fields — Known:** `MaxBuildArea +0x12..+0x1E`; `DigRoomAt/Current +0x72/+0x76`;
`WallHugStart/End/Distance/Heading +0x7A/+0x7E/+0x8A/+0x8E`; `BlibBlubBlob +0x4BA`;
`UntagGoldCounter +0x4F7`; enemy breaches begin `+0x9F` (dumper names
`EnemyBreaches[].TunnelsLeft/Tunnels[].Start/End`).

**Callees — Known roles:** `0x004FDB90` enemy-neighbour test; `0x00502230` line and
wall walker; `0x00508290` coordinate task lookup; `0x0050E5C0` tag dispatch;
`0x004BAB20` room availability; `0x004C5B40` room/bridge build; `0x00452970` route
query wrapper. World slots `+0x6C/+0x22C` fetch terrain/test room placement
(**Strongly inferred**).

## 10. Event 1 — `RespondToAttack` (`0x004FE0B0`)

**Prologue/result — Known:** `ECX` is keeper, one stack allowance byte; EBP frame,
0x48 locals, saved `EBX/ESI/EDI`, `AL` remainder, `RET 4`.

**Rule — Known:** the current payload is `{remainingThreat:i16@+0x5A,
threatenedX:u16@+0x5E, threatenedY:u16@+0x60}` at stride 8. Negative means withdraw:
scan owned creatures in list order within squared fixed-point distance `<0x19000`
(strictly less than five tiles) of that tile, excluding descriptor flag `0x4000`, the
`0x004888C0` state predicate, creatures in hand and state `0x4C`, plus the exact
gate `(creature.u16[+0x46D] <= 300 && turn <= 300)`. Move the first eligible creature beside the heart and add its
level-scaled threat. The semantic name of `+0x46D` is **UNKNOWN**.

**Known:** nonnegative means reinforce. Scan owned creatures in list order; require
descriptor bits 0 and `0x4000` clear, `0x004888C0==0`, generic eligibility
`0x0045A6D0(0)`, health above half of level-scaled maximum (the quarter-health and
state-category 7/3 checks are redundant before that), category !=22, not in hand,
state !=`0x4C`, and not one of states `0xD1/0xFF/0x100/0x101`. If the threatened tile
is not owned, replace the payload tile with the first owned, unobstructed candidate
from an expanding square spiral bounded by twice the smaller map dimension. Pick and
drop one creature there, then subtract its level-scaled threat.

**Known:** threat is descriptor `u16 +0x698` at level 1, otherwise multiplied by the
signed per-level percentage at table `0x006E4154 + 0x105 + 2*level`, divided by 100.
One creature costs one allowance unit, so this handler moves at most
`TendancySpeed` creatures in a turn. Zero allowance pauses the event. Residual crossing
zero, an empty creature list, or no eligible candidate retires it: zero the residual,
advance `CurrentEventTask=(head+1)%3`, decrement `NumberOfEventTasks`.

**Fields — Known:** keeper event nibbles and payload above; player `+0x04/+0x06/+0x16`
`TagId/ThingsOwnedList[0]/TotalNumberOfOwnedThings[0]`; creature `+0x04/+0x10`
tag/next, `+0x1A/+0x1E` position, `+0x40` health, `+0x24E` state subobject,
`+0x25E` state, `+0x370`
definition, `+0x403` level, `+0x46D` unnamed; definition `+0x686/+0x692/+0x698`
flags/base health/threat (**Strongly inferred** roles except threat formula).

**Callees — Known roles:** `0x004888C0`, `0x0045A6D0`, `0x00478050`, `0x0048F350`
are eligibility/state predicates; `0x00517490` gets the turn; `0x004BCAE0` tests the
hand; `0x0048E6A0` tests keeper interaction; `0x004BFBD0` prepares a creature,
`0x004BC500` picks it up, `0x004BC710` drops it; `0x004C5F50` gets the heart;
`0x00454110` rejects obstructed room slabs. World slots
`+0x1CC/+0x1D0/+0x1D4/+0x1FC` supply dimensions, tile-at-position and bounds
(**Strongly inferred**).

## 11. Other submitted game actions and remaining unknowns

**Known:** besides §2 tags, these routines use the human command path for room builds
`player.0x004C5B40(x,y,roomId,0,0)` (including bridges 8/17), pick-up by thing tag
`0x004BC500(tag)`, and drop `0x004BC710({xFp,yFp,zFp}, {0,...})`. Reinforcement adds
`0x100` to target X/Y; withdrawal uses the heart tile at `+0x900`. With no deposit,
the gold selector may cast keeper spell 13 (Create Gold) at one tile beside the heart.

**UNKNOWN:** retail names for terrain bits `+0x17B/+0x1CB`, keeper/world words
`+0x4650/+0x467A/+0x467C`, creature `+0x46D`, and most state constants; which eligible
strip wins an irregular-room expansion; and unused bytes/padding in the action record.
Resolve names only from another DK2 dumper/assertion or their complete reader/writer
sets; resolve strip order with instruction-level branch tracing or a rectangular-room
experiment; resolve packet padding by opening the action-40 consumer and serializer.
