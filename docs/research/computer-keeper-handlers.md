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

Only `0x004FC690` and `0x004FCE70` reference `ComputerPlayer.cpp`; no handler
name is exposed. Headings are descriptive except `RespondToAttack`, which
DK2's dumper prints.

## 2. Shared allowance and command path

**Known:** `0x00500C20` copies `TendancySpeed:u8` (`+0x4E3`) into an unsigned
one-byte allowance at the start of every accepted keeper turn. Each handler receives
the remaining byte and returns the remainder; an arm can therefore run repeatedly in
one turn. A processed coordinate or moved creature normally decrements it once.

Personality defaults and shipped allowance values: `KEEPER_AI.md` section 3.2.

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

**STRONGLY INFERRED, independently rechecked 2026-09-28:** the planner clears
`CurrentRoomToBuild` (`+0x98`) before calling the chooser. The chooser's exclusion
therefore does not skip the previous plan's room on this path.

**Known:** the planner then performs these steps:

1. Copy `NextRoomToBuild` to `CurrentRoomToBuild`; clear `NextRoomToBuildSet` only
   for room 3, otherwise replace `NextRoomToBuild` with 3.
2. Search the neutral player's nearest matching room, then test `MaxBuildArea`;
   rejection does not search a second candidate. `0x004C61C0` measures distance
   to room bytes `+0x2D/+0x2E`; requests 3 and 25 each match both Portal kinds.
   Thus the two calls (3, then 25 if absent) do not establish Portal priority.
   With no build candidate the chooser's room-3 fallback still enters this path.
   Ordinary rooms require squared heart-to-room-position distance strictly below
   `NeutralRoomRangeFromHeart` squared. Portals instead compare their entrance
   to the nearest owned room's stored position and accept equality. Both can
   fall back to the owned-boundary search; both require an owned room as origin.
   Accepted rooms start Task 7 / NextTask 4.
3. Let `price = roomDefinition(CurrentRoomToBuild & 0x7F).u16[+0x43F]`. If
   `player.Money < price * DefaultRoomSize` and the unnamed word at
   `player[+0x1105]+0x467C` is zero, start the gold selector instead of a room.
4. Otherwise start `Task=1`. Rooms below id 6 use `DefendDirection`; later rooms use
   `OpenDirection + random(3) - 1`, modulo four. Select an origin from an ideal-room
   table or a random owned non-bridge room, subject to `PlaceRoomsNextToIdeal` and
   whether the ideal kind exists. An `0x80` request first tries to enlarge an
   existing room; failure clears the bit and falls back to a new room.

`0x004FC3B0` scans left, right, top, bottom with `h = claimedRange + 1`.
Each fixed edge coordinate is `centre +/- h`; its varying coordinate runs
inclusively from `centre - h` to `centre + h + 1`. This is four extended edges,
not a larger rectangular perimeter. It returns the nearest owned tile by
squared distance, retaining the first on ties, and replaces the route origin.
These comparisons and endpoints were checked against the instructions and
fresh Ghidra output; no original-game boundary experiment has been captured.

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
owned land, `0x004FBE00` derives an expansion rectangle, world slot `+0x98` fetches a
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
   Every tile must be in-map and be taggable, owned without a room, or carry
   terrain descriptor `+0x1CB` bit 0; interior solidity is not tested.
5. Only when `TilesLeftBetweenRooms != 0`, count open perimeter samples and reject
   when `100 * open / samples > Openness`.
6. If `side^2 < DefaultRoomSize`, test an extra strip. Its spacing-dependent
   increment and exclusive-end build-area check are in `computer-keeper-tasks.md` section 7.

**Known:** there is no heart-distance test and no dedicated gold test. The fallback
interior test requires this player as owner and no room, without a solidity test.
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

**STRONGLY INFERRED:** traversal follows `InnerLoopXOrY`, spending one allowance
for the completed rectangle, not per coordinate. With more than 20 free pool entries
it submits action `0x28`; otherwise it skips tagging and advances. A zero action
result returns with unchanged cursor and allowance (`0x004FD0DC..0x004FD0E7`,
`0x004FD28E`). Completion resets the cursor and starts task 3; room `0xE5` instead
clears the task. Initialization and failure implications: `computer-keeper-tasks.md` §6.

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
2. Otherwise, if navigation says solid or the tile is not owned, retain the cursor.
   Optionally tag it when terrain is taggable, task kind is neither 1 nor 2, and
   the pool has more than 20 free entries. Repeat while allowance remains.
3. Only an owned, navigation-open, unbuildable tile advances the cursor. At the
   rectangle's end reset the cursor, start task 4 and exhaust the allowance.

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
`GoldDeposits[CurrentGoldArea]` when its total-gold dword is nonzero, copy its `(x,y)` to
`DigRoomAt`, reset `CurrentGoldArea=0`, rescan deposits, then start task 5. The scan
scores gold areas by squared distance to an owned room. Its four slots stay ordered,
but a stale admission cutoff means they need not be the four nearest deposits
(`computer-keeper-management.md` §12). The normal index 0 is the nearest retained
entry; no random choice occurs. The same audit covers centroid correction.

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

**Rule — Known:** a target outside `MaxBuildArea` aborts only when `NextTask` is
neither 0 nor 4: both tasks become 4 and allowance becomes zero. Otherwise:

1. Current equals target, a direct route succeeds, no required bridge is available,
   or the walker fails: copy `NextTask` to `Task`, reset `TaskTimeCounter`.
2. On solid/taggable terrain, submit action `0x28` only with at least 51 free task
   entries. Water and lava ask for a bridge; the choice, placement, retry and walk are
   [`bridge-placement.md` §4](bridge-placement.md#4-task-7-on-water-and-lava-0x004fdc50), built (ADR-0304).
3. During an attack (`BlibBlubBlob` low nibble nonzero), if `0x004FDB90` finds a
   tile in the current tile's 3x3 neighbourhood whose owner `0x004BF390` calls an
   enemy (the neutral player's unowned land included), convert the encoded
   enemy index from one-based to zero-based, store current→target in its first empty tunnel entry,
   clear the low nibble, and resume `NextTask`. Built (ADR-0298); the walker's other
   arms keep their approximations.

**Wall-hug — Known:** `0x00502230` steps on the axis with the larger absolute
difference (ties use Y), with headings N/E/S/W. `WallHugStart` is the saved cursor,
not the first obstruction. The trial has 25 segments and asymmetric early exits;
see `computer-keeper-tasks.md` §5 for the independent instruction audit.

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
0x48 locals, saved `EBX/ESI/EDI`, `AL=0`, `RET 4`.

**Rule — Known:** the current payload is `{remainingThreat:i16@+0x5A,
threatenedX:u16@+0x5E, threatenedY:u16@+0x60}` at stride 8. Negative means withdraw:
scan owned creatures in list order within squared fixed-point distance `<0x19000`
(strictly less than five tiles) of that tile, excluding descriptor flag `0x4000`, the
`0x004888C0` state predicate, creatures in hand and state `0x4C`, plus the exact
gate `(creature.u16[+0x46D] <= 300 && turn > 300)`. Move the first eligible creature beside the heart and add its
level-scaled threat. `+0x46D` is **`LastDroppedCounter`** by DK2's dumper (`creature-object.md`), so the gate reads how recently the hand dropped it.

**Known:** nonnegative means reinforce. Scan owned creatures in list order; require
descriptor bits 0 and `0x4000` clear, `0x004888C0==0`, generic eligibility
`0x0045A6D0(0)` — the fight gate of `combat-acquisition.md` §2 — health above half of level-scaled maximum (a quarter-health test and
a group 7/3 test of the current state's first table byte precede it, redundantly), category !=22 through a walk (`0x00478050`), not in hand,
state !=`0x4C`, and not one of states `0xD1/0xFF/0x100/0x101`. If the threatened tile
is not owned, seek an owned, unobstructed replacement in a square spiral (right, down,
left, up, each side one longer) bounded by twice the larger map dimension. An already owned payload tile, the
readers' usual drop site, skips movement; eligible threat is still subtracted (`computer-keeper-threat.md` §3). Built: ADR-0278.

**Known:** threat scaling, FIFO retirement and the whole-tick allowance rule
are in `computer-keeper-threat.md` §2–3; implementation boundary in §5.

**Fields — Known:** keeper event nibbles and payload above; player `+0x04/+0x06/+0x16`
`TagId/ThingsOwnedList[0]/TotalNumberOfOwnedThings[0]`; creature `+0x04/+0x10`
tag/next, `+0x1A/+0x1E` position, `+0x40` health, `+0x24E` state subobject,
`+0x25E` state, `+0x370`
definition, `+0x403` level, `+0x46D` `LastDroppedCounter`; definition `+0x686/+0x692/+0x698`
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
`+0x4650/+0x467A/+0x467C` and unused action-record padding. Resolve names through
dumpers/readers/writers; padding through the action-40 serializer. Expansion retains
multiple sides in order (`computer-keeper-management.md` §10).
