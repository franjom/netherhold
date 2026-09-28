# The computer keeper's task dispatcher

**Scope:** `DKII.EXE` v1.70, MD5
`2f3bbc21eae8c3aa468136baa303036d`. This document follows the computer
keeper object named by DK2's debug dumper; see `computer-keeper-object.md`.
Addresses are virtual addresses in that executable. Behaviour below is stated
in our own words; no decompiler output is reproduced.

The planner and handlers summarized in §3 are opened instruction-by-instruction in
[`computer-keeper-handlers.md`](computer-keeper-handlers.md).

## 1. Per-turn entry point

**STRONGLY INFERRED from the binary:** `0x00500C20` advances one computer keeper. Its
call chain is:

1. The game-session update at `0x00526590` invokes the world update through
   virtual slot `+0x18`. The slot contains `0x0050A450` in the world vtable at
   `0x0066E3EC`.
2. `0x0050A450` is the world-turn routine (its own diagnostics say
   `GameTurn`). During an active turn it calls `0x004C2310` directly, without a
   counter or modulus gate.
3. `0x004C2310` walks the world's player list and calls `0x004B9EC0` once for
   each player.
4. `0x004B9EC0` calls `0x00500C20` when the player's computer-control byte at
   `+0x10FA` is nonzero and player status at `+0x19A` is neither 1 nor 3 —
   *conquered* and *lost* (`win-and-defeat.md` §1), so a defeated computer
   keeper stops acting while a winner (2) keeps its turn. It passes the
   computer-keeper pointer stored at player `+0x1101`.

Therefore an eligible computer keeper is advanced **once every game turn**,
not every N turns and not when a countdown expires. `0x00500C20` independently
rechecks statuses 1 and 3, then increments `TaskTimeCounter` at keeper `+0x04`
on every accepted call. If the debug-named `TimesOut` flag is set and the
counter exceeds 1,680, it clears both `Task` and `NextTask`; that timeout changes
task state but does not schedule the routine.

The entry point receives a per-turn work allowance from the byte DK2's dumper
calls `TendancySpeed` at keeper `+0x4E3`. Its sub-operations consume that
allowance, so one invocation may perform several small AI actions even though
the invocation cadence is one per turn.

## 2. `Task`

**STRONGLY INFERRED from DK2's own dumper and the binary:** `Task` is not a separate
field. It occupies bits 22-25 of the computer keeper's dword at `+0x00`:

```text
task = (keeper[+0x00] >> 22) & 0x0f
```

`0x00500C20` extracts that value, rejects values above 8, and dispatches through
a compiler jump table. This is not a record array like the creature-action
table: the id is implicit in the table index and each four-byte entry is a jump
target.

| id | table entry | switch arm | destination |
|---:|---:|---:|---|
| 0 | `0x0050127C` | `0x00501103` | inline idle/planning arm |
| 1 | `0x00501280` | `0x00501171` | calls `0x004FCE70` |
| 2 | `0x00501284` | `0x00501184` | calls `0x004FD030` |
| 3 | `0x00501288` | `0x00501197` | calls `0x004FD2B0` |
| 4 | `0x0050128C` | `0x005011AA` | inline arm |
| 5 | `0x00501290` | `0x005011FF` | calls `0x004FD660` |
| 6 | `0x00501294` | `0x0050120F` | calls `0x004FD750` |
| 7 | `0x00501298` | `0x0050121F` | calls `0x004FDC50` |
| 8 | `0x0050129C` | `0x0050123E` | stops this turn's task loop |

Thus the table base is `0x0050127C`, stride is 4, count is 9, id is the index,
and the stored field is the switch-arm address. The bounds check precedes the
table lookup. An out-of-range id is changed to 4 and processing stops; arm 8
also stops without calling a handler.

The immediate-mask sweep found these computer-keeper writers:

- `0x004F8360`, the object initialiser, clears `Task` and the adjacent
  `NextTask` field.
- The inline idle/planning path `0x004FC690` clears `Task`, starts id 1, or
  starts id 5 according to the planning result.
- Handlers `0x004FCE70`, `0x004FD030`, `0x004FD2B0` and `0x004FD750` finish or
  replace their current task by masked writes to the same dword.
- `0x00500C20` clears it on timeout and when inline id 4 completes, and changes
  an invalid id to 4.
- `0x004FDC50` reads `NextTask`; its relationship to `Task` is covered in §4.

The only other whole-binary hits for the same mask operate on creature offset
`+0x3E6` in the combat update and are unrelated. Apart from DK2's debug dumper,
the extracted four-bit `Task` value is read only by `0x00500C20`; task handlers
change it but are reached through that one switch.

## 3. Task ids

DK2 provides no enum strings for these ids. The names below describe what each
arm does; they are not recovered source identifiers.

| id | evidence-based name | confidence and evidence |
|---:|---|---|
| 0 | no ordinary task / choose work | **STRONGLY INFERRED behaviour.** Initialisation and completed tasks write 0. The inline arm spends one allowance unit, runs the room planner, and may start a gold search when money is below `TendancyMoney`. |
| 1 | select and validate a room footprint | **STRONGLY INFERRED name.** `0x004FCE70` uses `DefaultRoomSize`, `DigRoomAtCurrent`, the direction fields and the map ownership around a candidate square. Success writes id 2; exhaustion clears the task. |
| 2 | tag the room footprint for digging | **STRONGLY INFERRED behaviour.** `0x004FD030` walks the rectangle described by `DigRoomAt`, `DigRoomAtCurrent`, `DigDX/DigDY`, `InnerLoopXOrY` and `MaxX/MaxY`, issuing game action `0x28`, independently established as tag-for-digging. Completion writes id 3, or clears the task for the special no-room case. |
| 3 | build the room tiles | **STRONGLY INFERRED name.** `0x004FD2B0` walks the same rectangle and submits `CurrentRoomToBuild` at each eligible tile. Its submit helper checks the room's cost against player money; Flame independently calls the callee at `0x004C5C30` `buildRoom`. Completion writes id 4. |
| 4 | finish the room cycle | **STRONGLY INFERRED name.** The inline arm spends one allowance unit, may start the low-money gold fallback, resets `TaskTimeCounter`, and clears id 4 if nothing replaced it. It performs no tile work itself. |
| 5 | choose a tunnel start toward gold | **STRONGLY INFERRED behaviour.** The only writer selects an entry from `GoldDeposits`, then `0x004FD660` chooses the owned room nearest the deposit — the heart when there is none — as `DigRoomAtCurrent` (`computer-keeper-handlers.md` §7) and starts id 7 with id 6 as the continuation. |
| 6 | expose the selected gold area | **STRONGLY INFERRED name.** `0x004FD750` expands a square around the selected deposit and issues tag-for-digging action `0x28` on eligible tiles. It is reached as id 7's continuation from the low-money path. |
| 7 | dig a route, then resume `NextTask` | **STRONGLY INFERRED mechanism and name.** `0x004FDC50` advances `DigRoomAtCurrent` toward `DigRoomAt`, fills `WallHugStart`, `WallHugEnd`, `WallHugDistance` and `WallHugHeading`, and tags obstructing tiles. On completion or refusal it copies `NextTask` into `Task`. |
| 8 | stop/suspended sentinel | **UNKNOWN meaning.** Its arm merely exhausts the allowance and returns. No immediate-value writer of id 8 was found in the executable; save/load or a computed write remains possible. |

The shipped `Text.str` was checked for task, room, gold, dig, corridor and tunnel
vocabulary. It contains UI labels such as `Digging Rock`, `Mining Gold`, `Next
Room` and `tunnelling`, but none is referenced by these handlers and none maps
an id. The handlers and their helpers likewise carry only the source path
`ComputerPlayer.cpp`, not task-name assertions. The text therefore corroborates
the vocabulary but does not raise any inferred name to confirmed.

## 4. `Task` and event tasks

They are separate schedulers sharing one flags dword.

| field | location | role |
|---|---|---|
| `CurrentEventTask` | `+0x00`, bits 14-17 | head index of a three-slot ring |
| `NumberOfEventTasks` | `+0x00`, bits 18-21 | occupied-slot count, constrained to 0-3 by producers |
| `Task` | `+0x00`, bits 22-25 | persistent ordinary-task state from §3 |
| `NextTask` | `+0x00`, bits 26-29 | continuation used by ordinary task 7 |
| `EventTask[3]` | `+0x22`, three dwords | event kind at each ring slot |
| response payloads | `+0x5A`, three 8-byte records | signed threat plus target tile for the matching slot |

**STRONGLY INFERRED:** event production uses tail index `(CurrentEventTask +
NumberOfEventTasks) % 3`, increments the count, stores event id 1 and fills the
same slot's response payload. Producers refuse a fourth entry. DK2's own dumper
prints the payload as `RespondToAttack` when the event id is 1, so that name is
the game's, not an external inference.

At the start of `0x00500C20`, player messages can enqueue responses. A nonempty
event ring is serviced before ordinary `Task`: the routine reads
`EventTask[CurrentEventTask]` and dispatches id 1 to `0x004FE0B0`. That handler
moves eligible creatures to or from the threatened tile, consuming the same
per-turn allowance used by ordinary work. Completion advances the head modulo
3 and decrements the count. The ordinary `Task` bits are not cleared or
replaced, so response work **temporarily preempts a persistent ordinary task**;
it is not a second encoding of that task.

Every normal producer found writes event id 1. If a nonempty head contains any
other id, `0x00500C20` spends no more allowance and does not dequeue it. Such an
id is therefore unsupported by the live producer paths; whether save loading
can introduce one is UNKNOWN.

## 5. Corridor execution and wall-hug audit

Independent instruction check, 2026-09-28; **STRONGLY INFERRED** without a
matching original command trace. The current `LineWalker` approximation differs.

`0x004FDC50` spends allowance on the **current cursor tile before advancing**:
water/lava attempt a bridge, taggable terrain attempts a dig tag, other terrain
asks whether the target can route to the cursor. A tile already carrying task
kind 1/2 is retried without advancing (`0x004FDE91..0x004FDE9F`). Thus a corridor
waits for tagged rock to change before proceeding; it does not pre-tag its entire
route. A successful bridge build calls the walker immediately; a failed build
retries the same tile. The dig pool must have **at least 51 free entries**:
`0x004FDEB7..0x004FDEC4` rejects `500-free >= 450`, not `free <= 51`.

The route request (`0x004FDF3B..0x004FDF8D`) passes the target and cursor tile
centres, player tag, then options `1,0,0,0` through `0x00452970` to
`0x004D1700`. It uses the mesh router and current navigation bytes; the current
AI's heart flood-fill through dig tags is not equivalent. The wrapper sets
query globals `0x006EC9D0/D4/D8/DC/E0`, but does not initialize `E4`; that flag
retains a prior query's value. A decision to isolate query context needs an ADR.

In `0x00502230`, the first obstruction establishes distance/heading and the
straight-line exit. **The saved route cursor is still the start**: instructions
`0x0050248A..0x00502491` copy keeper `+0x76` to `WallHugStart +0x7A`, and
`0x005024A8..0x005024AB` pass that same saved cursor into the trial. The code
does not first move it onto the obstruction. The exit test uses navigation
solidity, while actual hugging's open-tile test uses terrain solidity/lava.

`0x005027D0` tries the heading-plus-one side first, then heading-minus-one.
Its segment counter begins at zero and stops after incrementing to 25
(`0x00502AE9..0x00502AF1`, `0x00502E42..0x00502E4A`). An open-ground walk can
visit several tiles within a segment. The first trial returning to its start
selects the other side immediately; the second looping back selects the first.
When the first reaches the exit, its segment count bounds the second trial.
Otherwise compare their minimum squared distances; ties select the second side.
Actual hugging returns zero on a loop to its start; reaching the exit clears
wall-hug and succeeds. The trial's player-tag exclusion receives zero from this
caller; it must not be replaced by the keeper's player id.

## 6. Rectangle traversal audit

Independent instruction check, 2026-09-28; **STRONGLY INFERRED**.
Task 2 initializes when `JoinPathStart.X` is zero: set that word to 1 and restore
both rectangle origin and cursor from `JoinPathEnd`. A zero cursor X instead
swaps the route endpoints, enters task 7 with continuation 2 and resets its clock;
neither this detour nor a zero action result spends allowance. Only completing
the rectangle decrements it (`0x004FD205`, `0x004FD22F`, `0x004FD24F`).
The unchanged failure return could cause immediate repeated attempts in the task
loop; whether that failure is reachable in ordinary play is **UNKNOWN**.

Task 3's blocked/foreign-tile flag bypasses cursor advancement
(`0x004FD42A..0x004FD437`). Build attempts also return without advancing
(`0x004FD550..0x004FD577`). On the next pass, a newly built owned tile advances
only if the placement predicate now refuses it and navigation is open. Our
current `PlaceRoom` scans the whole footprint and can skip a blocked leading
tile; it does not reproduce this cursor discipline.

## 7. Site search audit

`0x004FCE70` tries preferred direction, then preferred +1, −1, +2 (modulo 4).
A failed direction returns zero allowance; success spends one. After four
failures the random roll accepts 5–99 for reseeding, resets the preferred
direction and asks for a non-bridge owned room. Failure to find one clears task 1.

`0x005012A0` rejects approach/gap terrain only when solid and not taggable;
it does not require rock. Interior acceptance is taggable terrain, descriptor
`+0x1CB` bit 0, or owned tiles without a room; that last arm does not test
solidity. The initial square's perimeter is examined before an extra strip.
Build-area validation tests the start and `start + step * extent`, one step
beyond the final tile on each axis, rather than the two included corner tiles.

When the requested count exceeds the square, the extra strip requires untagged
taggable terrain. Only nonzero room spacing enables its dimension increment
(`0x00501934..0x0050197B`, `0x00501A67..0x00501AAA`); spacing zero can accept
the smaller square after testing the unused strip. A rejected strip branches
back into the search (`0x00501B1F..0x00501B23`), rather than accepting the square.
These are **STRONGLY INFERRED** instruction/data-flow results; termination for
all possible rejected-strip configurations is **UNKNOWN**.

## What is left

- Find a writer for task id 8, or establish that it is only loadable state.
- Check save loading for task id 8 or event ids other than 1.

## Confidence

- **STRONGLY INFERRED:** the addresses, call chain, cadence, field layout, jump-table
  contents, bounds check, listed readers/writers, and per-id mechanics are direct
  binary reads. The event-ring shape, capacity, priority,
  id 1, payload association and completion update are also direct binary reads;
  `RespondToAttack` is printed by DK2's own dumper.
- **STRONGLY INFERRED:** the descriptive names marked that way in §3; the
  evidence is each arm's field and game-action use, not a recovered enum.
- **HYPOTHESIS:** none.
- **UNKNOWN:** id 8's meaning and writer, and whether loading can introduce
  other event ids. The player status values are named in `win-and-defeat.md`.
