# Imp tasks: the objects handler, the secret door, the kicked imp, the walk's end

**CONFIRMED** unless a paragraph says otherwise. DKII.EXE v1.70 (GOG), read-only Ghidra
12.1.4/PyGhidra on Claude's clone, 2026-09-30. Decompiler output stayed under
`reverse-engineering/` (ADR-0046); every fact below is in our words with its address.
Captures (`level1-live1/seed01`, `level2-live2/seed01`, player 3) were used only to check
what the binary says. This note finishes four rows `imp-task-stealing.md` left skimmed or inferred and
uses its vocabulary (holder test, kick, slot, `+0x98`); read that note first.

## 1. The objects handler, kind 8 (`0x0046B9C0`)

Read in full, 2026-09-30, with its disassembly for the flag order. It is the third step of
the per-tile chain `0x00469580` (Tier B) and of the drop chain `0x00469240`, called with
the tile's `x`, `y` and tile record.

**Tile gate.** The tile's owner (word `+0x08`, low 12 bits) is the imp's keeper, or the
tile's terrain carries Water (flag `+0x17B` bit 7). Otherwise it returns 0 at once. This is
the gold handler's gate too (`imp-task-stealing.md` §4).

**Which things.** It walks the tile's thing list (head in the tile's first word, next at
thing `+0x0C`) in list order, and a thing is a candidate when all of these hold:

1. it is an object (`+0x12 == 2`) owned by the neutral player (`+0x28 == [0x006F2548]`);
2. it is not being dragged: the object class's vtable `0x0066D654` slot `+0x74` is
   `0x004B7250`, bit 1 of `+0x146` (`BeingDragged`, `objects.md`);
3. its definition's flags (`+0x12D` → `+0xF4`) carry one of three bits, tested in this
   order, first match wins:
   - **`0x04`** (`TypeSpecial` in `ObjectFlags`): skipped when the tile is part of a room
     (tile word `+0x02`, low 12 bits, non-zero) and the object's `RoomId` (`+0x138`) is
     that room. Otherwise it needs a Library (room type 6) with space.
   - **`0x10`** (`TypeCrate`): first the crate branch `0x00468910` (`workshop-crates.md`),
     and the handler returns 1 if that took a job; otherwise it needs a Workshop (type 10)
     with space.
   - **`0x08`** (`TypeSpellBook`): it needs a Library (type 6) with space.
   - none of them: the next thing.

"With space" is `0x004C6610` on the keeper (`ECX` = creature `+0x2A`): the keeper's rooms
of that type (list head player `+0x36`, next room `+0x08`, type byte `+0x18`), each with
capacity for the definition's byte `+0x114` (`Room_HasCapacity`; a Prison has its own
count test), the one whose centre tile (`+0x2B`, `+0x2C`) is nearest the imp by squared
distance and reachable (`0x00471630` with options 0). The handler only uses success; the
room is written to its own argument slot and never read, so the carry state chooses
again. **A failed search is remembered for the rest of this tile**: one latch for
`0x04` things and one shared by `0x10` and `0x08` things, so a tile where the Workshop
search failed also skips its books.

**Reach and task.** The object's position must pass `0x00471630(point, 0x80)`, else the
next thing. Kind 8 is filed **per tile**: find the entry at the tile (`0x00508200`) or
raise one (`0x00507BB0`); if neither (the pool has no free entry) the handler returns 0
for the whole tile.

**Steal rule.** Slot `[0][0]` (`0x00508110`). It is free when zero, when the id is not
live (`0x006ED540`) or when it is not a creature (`+0x12 != 0`); a dead or reused holder
therefore counts as free. A held slot runs the holder test of `imp-task-stealing.md` §2; if the test fails the
handler goes on to the next thing. If it passes, both imps are measured to **the
object's own position** (`+0x1A`, `+0x1E`), and the taker must be strictly nearer. Then
`0x0048AD30(10)` on the holder; a refused kick returns 0 for the tile.

**Take.** The leave hook `0x004647D0` on itself, then `0x004651A0(object position, resume
30, options 0xE8, 0)`. On success: `+0x8C`/`+0x90` = the entry's tile, `+0x8A` the id,
`+0x94` = 0, `+0x96` = 0, slot `[0][0]` = its own id, `+0x98` = 8, return 1. On failure it
goes on to the next thing; a freshly raised entry is **not** freed (reinforce frees it,
`imp-task-stealing.md` §4) and a kicked holder stays kicked (`imp-task-stealing.md` §7, ghost 1).

Because the entry is per tile and the distance per object, two objects on one tile can
give two answers against one holder, as with gold. The handler reads nothing about what
the imp is carrying or holding.

**What our engine must change** (`ImpTaskSystem.TryTakeObject`; built in ADR-0294 except the
in-its-room skip and the walk to the object's own position):

- take the tile when it is the keeper's **or water**; ours takes only its own (Q170);
- drop the refusals ours adds: DK2 does not refuse an imp that holds gold or carries
  something here;
- pick by the handler's order: tiles in the ring scan's order, then the tile's thing list
  in list order, the flag order `0x04`, `0x10`, `0x08`, and the two latches; ours ranks by
  path length and breaks ties by the lower object id;
- skip a `0x04` object already in the room it stands in, and a dragged object;
- file one kind-8 entry per tile with slot `[0][0]`, steal by the object-position rule
  and kick first; ours has no entry and no stealing for kind 8;
- walk to the object's own position with resume 30, not to the tile.

`0x04` things go to a Library in the binary. `ObjectFlags` calls bit 2 `TypeSpecial`, and
objects 117–129 and 140 carry it: the specials. The owner has seen imps drag them to a
Library (Q495, `specials.md` §1).

## 2. Kind 11 is the secret door (`0x0046CB80`)

Read in full, 2026-09-30, with its raiser, its two work states and the flag's writers.
This answers Q288.

**The flag.** Tile byte `+0x0B` bit 0 marks **a Secret Door on the tile**. It has one
setter, `0x00450FF0(x, y, value)`, which copies bit 0 and then refreshes the eight
neighbours through `0x004522A0`. It is set to 1 in two places: the level loader
(`0x0050BA11`), for every door whose definition flags (`def+0x8B`) carry `0x20`, the
Secret Door's bit (`doors-and-traps.md`); and state 37, "installing a Door"
(`0x00467460`, call at `0x00467604`), when the door it places from a crate has that bit,
right after posting tile notification `0x11`. `0x004A55F0` and `0x004C3670` write 0;
`0x004A5290` writes a computed value (not read). The door's navigation byte already reads
this bit (`doors-and-traps.md`: "2 if the tile's flag bit 0").

**What raises kind 11.** The marking routine `0x00512D20`, the keeper's dig tag: a tile
whose byte `+0x0B` bit 0 is set is filed as **kind 11**, before the terrain is looked at;
otherwise a Taggable tile (terrain `+0x17B` bit 3) is filed as 2 when its terrain has a
gold value (`+0x185`) and as 1 when not. All three arms need more than 20 free entries
(pool word at `+18000`); below that the routine only posts a message (0, 1 or 2) through
`0x00517480`. The three arms reach one shared call of `0x00507BB0`, which is why no
immediate `PUSH 0xB` exists (the trap `player-task-pool.md` warns about). Nothing else
raises kind 11: `0x0046CB80` only finds an existing entry.

**Input eligibility.** The owner recalls that rivals see a reinforced wall and can tag it,
while the owner cannot tag its own door. The native marker and rectangle handler have no
owner gate; where the input path refuses it remains UNKNOWN. The simulation command
mirrors the marker and adds no guessed ownership restriction.

**The handler.** Called with the tile. Its steps, in order:

1. find the kind-11 entry at the tile (`0x00508200`), else return 0;
2. the tile's byte `+0x0B` bit 0 must still be set, else the entry is freed
   (`0x00508040`) and it returns 0;
3. the start side from the imp's tile offset (`0x004805F0`), then the four sides in that
   order; a side counts when the side tile's terrain is not Solid (`+0x17B` bit 0) and the
   side tile's centre passes `0x00471630(point, 0x80)`;
4. the point measured is the side's **stance point**: the side tile's corner plus the
   side's offset from the stance table (`0x00698A88`/`0x00698A8C`, stride `0x18`), the
   table the dig handler uses;
5. the slot is `[side][1]`; free, holder test and `0x32` slack as in the dig handler
   (taker < holder + `0x32`), kick first, a refused kick returns 0;
6. take: leave hook, `0x004651A0(stance point, resume 38, options 0xE8, 0)`; on success
   `+0x8A` the id, `+0x8C`/`+0x90` the tile, `+0x94` the side, `+0x96` = 1, the slot
   written, and **`+0x98` = 1**: the imp records kind 11 as kind 1, so its next state 10
   searches kind 1 whole-map first (`imp-task-priority.md` §4). A failed walk goes to the
   next side.

**The id it passes is the entry's own (Q496, 2026-10-05).** Both slot calls, the read at
`0x0046CD06` and the write at `0x0046CF17`, take the dword at `[EBP-0x43]`. That is filled
through a pointer: at `0x0046CC39` the handler calls `0x005081C0(id, EBP-0x64)`, which copies
the found entry's 36 bytes (`REP MOVSD`, 9 dwords) when it is allocated (`+0x23`) and its id
word (`+0x21`) matches, and `EBP-0x64 + 0x21` is `EBP-0x43`. `0x00508110` and `0x00508160`
use only the low 16 bits, so both calls address the kind-11 entry itself: it records a
holder, and a second imp is refused or kicks under the `0x32` slack (`0x0046CE4F`, kick at
`0x0046CE63`). CONFIRMED. An earlier frame scan missed the callee's write and called the word
unwritten.

**The work.** State 38 (`0x00467650`): the entry must still be kind 11 (`0x00508070`)
and the imp within one tile of it on both axes, else the leave hook runs and it goes to 10
with `+0x8A` cleared; it turns toward the tile's centre (`0x004B2AE0`) and waits for the
turn; then state 39, and if byte `+0x0B` bit 0 of **the tile it stands on** is clear it
starts action 1 (`0x00496A30(1, 1, …)`). State 39 (`0x00467790`) waits while an action
runs (`0x00496D00`), then clears bit 0 of the task tile, posts tile notification `0x11`
with the tile and the imp's owner, and frees the entry. One visit ends the job; there is
no health or swing count. That the cleared bit is the door becoming visible to its enemy
is STRONGLY INFERRED from the flag's meaning; notification `0x11` is posted through the simulation-to-presentation bridge (`0x00517480`).

**Whole-map kind-11 dispatch (`0x0046C720`).** Existing entries, newest first; stale
flags remove their entries. Minimise squared distance to the task tile centre, retaining
the first at a tie. Try sides from the worker-relative start, measuring a held slot
from the side tile centre: the worker-to-task-centre distance must be strictly less
than holder-to-side-centre distance, without the tile handler's slack. A free side
ends that entry's side scan; an eligible held side can be replaced by a later side.
Kick the selected holder before the one walk attempt; a failed kick/walk ends the scan.

**Built (ADR-0536, hash/save 304).** `TileState.SecretDoor` is global;
`SecretDoorTasks` is each keeper's separate kind-11 entry. Loading and crate installation
set the flag; removal clears it. Rectangle untag only removes kinds 1/2 (`0x00512A20`),
so it leaves kind 11. The native marker selects 11 before terrain; ordinary excavation
never consumes these entries. Tier B tries it after objects; the drop chain after corpses.
Both selectors reserve the entry's middle face slot and record last kind **1**, not 11.
The tile handler retries an occupied, unstealable side without advancing, but its four
iterations remain bounded. State 38's underfoot-secret rejection and state 39's zero
return fall to state 1 through the dispatcher (`0x00476FC0`), not directly to 10.
The ordinary floor case plays one counted dig clip and exposes the door without changing
door or tile health. `InputLog` replays additions; removals cannot be player untag.
Navigation bit 1 reads the global flag through `0x00450FC0`, not a keeper's dig tag.

**Interruption and insertion order (review, 2026-10-25).** Each keeper's kind-11 entry
keeps its own insertion order and four middle-slot holder ids in `WorldState.SecretDoorEntries`.
Another keeper's tag or an ordinary dig tag cannot reorder it. Holders are not derived from
the creature's current task: `0x004647D0` resolves travel 6/7 through the resume, omits state
38, and its state-39 arm at `0x00464949` clears **slot 0**, not the selected slot 1
(`PUSH 0` at `0x00464962` and `0x00464964`, then side/id and `0x00508160`). The middle
reservation therefore survives combat and later reassignment until overwritten or the entry
is freed. Dispatch runs only state 38/39 or state 6 resuming 38; a retained job block in
combat or idle cannot execute state 39. A new job resets the creature's selected-kind marker
without deleting the old entry's holder. These entry orders and holders are hashed and saved.

**Remaining limits.** The existing tag model has no finite task-pool capacity, so the native
20-free-entry floor remains unmodelled. Matched original-game acceptance is pending.
Automatic reveal is a separate, unbuilt `0x004A55F0` arm: an opening completed Secret Door
checks nearby enemies, category/fight eligibility and line of sight, then clears the flag,
posts presentation command 0x11 and rewrites navigation. The Imp completion reports its
notification as a simulation event; the presentation refresh is outside this change.
The counted action uses action 1's existing diggable-wall guard and owner-dependent rate:
a flagged solid, diggable tile receives one strike; ordinary door floor receives none.

**Verification (2026-10-25).** The independent verifier reran 606 focused simulation
checks: zero failures or skips. Coverage includes selection order, stealing/slack,
stored actions, solid/floor completion, combat interruption and reassignment,
per-keeper insertion order, retained holders, snapshot/hash sensitivity and pin.
The Unity adapter compiles with zero warnings/errors. Tool build, documentation
budgets, KB check (2,317 records), verification ledger and coverage checks pass.
No campaign sweep or live original-game acceptance was run for this change.
The subsequent combined-tree integration results and skipped-fixture counts are
recorded in [the handoff](../HANDOFF.md), separately from the focused review.
The focused command targets `DK2.Simulation.Tests` in Release with filter
`Imp|TileWork|TaskStealing|StateCoverageTable|Snapshot|InputLog|DoorNavigation|Determinism|CombatCategoryHooks`
(each term is `FullyQualifiedName~...`), `-m:2 --nologo --no-restore`.

## 3. A kicked imp stops where it stands

Read 2026-09-30: the creature update's mover gate, every caller of the mover, and the
three idle states.

**Who moves an imp.** The body is integrated by `0x004D5A40` (skipped when the body's max
speed at `+0xE2` is zero; it sets the steering globals and calls `Steering_Integrate`). It
has exactly four callers (`CallScanAll`, all executable blocks): state 6 `0x00470C80`,
state 7 `0x00471460`, state 265 `0x004769D0`, and the creature update at `0x0048A287`.
The update's gate (`0x0048A202`–`0x0048A248`) moves a creature only when `0x004739C0` is
0 (state 116 with `+0x2FA` set), it is not possessed (`+0x3E6` bit 7), **its type id
`+0x36F` is not 1**, and its state's flag byte (`0x006DA8C8 + state × 25`) has bit 0. The
Imp's type id is 1. State 6 is the mirror: it integrates the body itself **only** when
`+0x36F == 1`. So an imp moves only while it is in 6, 7 or 265.

`0x004D5AF0`, which the earlier note called the mover, is a getter: it returns the body's
velocity (`+0x8C`, `+0x90`) shifted down four. The steering behaviours are one global list
(`0x004D56E0`: avoid walls, repel obstacle, follow path, separation), so there is no
per-state steering to switch off.

**States 10, 11 and 12.** None of the three calls the mover or the stop routine. State 10
is `imp-task-stealing.md` §5. State 11 (`0x00463DB0`) sets 10 with `+0x98` = 0, `+0x99` = 1 (the next pass runs
the drop scan) and `+0xA2` = 0. State 12 (`0x00464240`) waits while an action runs; goes
to 10 when the turn reaches `+0x7E`; otherwise, when `+0xA7` is set, it rolls
`rand(100)`: below 25 nothing, 25–74 tries idle action 43, 75 and up idle action 44 (a
started action clears `+0xA7` and ends the tick). Every other path goes to 87
(wandering) and sets `+0xA7` to the turn + 32. 87 walks through `Travel_RouteToPoint`, so a wandering imp moves again only in
state 6.

**What a kicked imp keeps.** Nothing touches its body: the route, the velocity at `+0x8C`
and `path_finished` stay as they were. The stop routine `0x0048ECE0` (clears `+0x3E6`
bits 17 and 18, then `0x004D1070`: a route to its own position and a zero velocity) is
what state 6 runs on arrival, and state 6 is no longer running. The next walk loads a new
route through `0x004D0F60`, which zeroes the stale velocity before loading it (read
2026-10-19; built in ADR-0513).

**Capture check** (`level1-live1/seed01` and `level2-live2/seed01`, player 3, every
frame): an imp was in 10, 11 or 12 in two consecutive frames 53 times in level 1 and 1,976
times in level 2, and its position changed **0** times. 25 walkers dropped from 6 to 10
while another imp took their task id; in all 25 the position in the kick frame equals the
one after it, and the imp moves only once a new walk has begun.

So for `imp-task-stealing.md` §5 and `imp-task-stealing.md` §7: a loser does not walk on toward the point it lost; it stands, chooses
again, and if it gets nothing goes to 12 and then 87.

**What our engine must change.** `CreatureMovementSystem` moves any creature with a route
loaded, whatever its state, and `TakeTaskFrom` → `ClearJob` → `StopMoving` loads an empty
route, which leaves a braking step to the follow-path force. For an imp, move the body
only while it is in 6, 7 or 265 (for other creatures keep the flag-byte rule), and let a
kick change the state only: no stop, no braking step, the old route and job block kept
(`imp-task-stealing.md` §5). The job block difference (ours clears the target, DK2 keeps `+0x8A`, `+0x8C`,
`+0x90`, `+0x98`) is `imp-task-stealing.md` §5's and still stands.

## 4. Where a claim walk ends (Q490)

Read 2026-09-30: state 6 `0x00470C80`, the progress function `0x004D54F0`, the route
loader `0x004D5100`, with `steering-behaviours.md` §5.1–§5.2 and §7.

**The rule.** After the imp's own integration in a tick, state 6 calls `0x004D54F0` on the
body (`creature+0x124`, through the thunk `0x004D5B20`) and resumes (stop routine, then
`SetState(+0xDF)`, then `+0xDF` = 1) **only when it returns exactly 1.0**. That happens
in three cases:

1. `path_finished` (body `+0x10E` bit 6) is set. For an imp the follow-path imp arm sets
   it on the last point when the body began this tick's integration within `0x1999`
   (0.1 tile, octile) of the route's target;
2. the route total `+0xFA` is zero (no route);
3. the remaining distance is zero: the octile distance from the body to the current point
   plus `+0xFE` and `+0x102`, each **truncated to whole sixteenths of a tile** (`>> 12` of
   16.16). On the last point with nothing beyond, that is the body standing within
   1/16 tile (octile) of the target after this tick's move.

It never looks at the tile the imp stands on. Separately, state 6 sets or clears bit 0 of
`+0xF1` by whether the imp is within `0x200` (about 0.35 tile) of the walk target at
`+0xE3`/`+0xE7` and sets `+0x3DB`; that is not an arrival test.

**The claim target is the tile's centre.** In `seed01` all 74 claim walks whose target
could be read (`+0xE3`/`+0xE7`, CState `+0x331`/`+0x335` in the record) aimed at the exact
centre, and 84 of 88 claims began with the imp at 0.00 tile from it (the rest at 0.01 to
0.05).

**The ticks.** 49 claims followed a claim on an orthogonal neighbour by the same imp:

| from one claim's first state-15 frame to the next | count | states between (frames) |
|---|---|---|
| 13 | 41 | 16 ×8, 10 ×1, 6 ×3 |
| 12 | 8 | 16 ×8, 10 ×1, 6 ×2 |
| 41 (39 once) | 10 | through enemy floor, 19 ×1 and 20 ×28 |

The three walk frames of a 13 are one frame standing (the tick state 10 took the task and
set 6: no integration, since the update's mover skips imps), then two moves of 0.461 tile,
and in the third the imp lands exactly on the centre and is in 15. Worked example, imp
452, ticks 708–712: 14.5 (10), 14.5 (6), 14.961 (6), 15.424 (6), 15.5 (15). At 15.424 the
remaining 0.076 tile is one sixteenth, so case 3 fails; the next tick begins inside 0.1
tile, so case 1 holds and the imp is on the point by the end of it. The 12s are later in
the level, when the imps step 0.525 then 0.461: imp 452 at ticks 1534–1536 reads 14.5,
13.975, 13.514, and 13.514 is 0.014 tile from the centre, zero sixteenths, so case 3 ends
the walk one tick early and **off the point**. That walk is the exception: 20 of the 21
two-frame claim walks in `seed01` land exactly on the centre, and how every exact landing
happens — the integrator's arrival snap, which the claim's option word switches on — is
`imp-walk-arrival.md`.

So a neighbouring claim is 3 walk ticks in DK2 (2 when the imp is fast), and the walk
ends near the centre, never on crossing into the tile.

**What our engine must change** (`ImpTaskSystem.UpdateMoving`, `IsInWorkingPosition`,
`FollowPathRules.Progress`):

- end a job walk when the progress is exactly 1.0 after the tick's move, with all three
  cases, not when the imp enters the tile; the "arrived on entering the tile" branch goes;
- compute the remaining distance in truncated sixteenths as DK2 does, so case 3 ends a
  fast imp's walk off the point;
- aim a claim walk at the tile's centre;
- no move on the tick the task is taken; `CreatureMovementSystem` runs before
  `ImpTaskSystem`, which already gives that for a route loaded by the selection, but it
  has to hold for an imp in any non-walking state (§3).

The reverted attempt that "gave 3 ticks sometimes and 7 others" needs its 7s explained
before this is built again; DK2 shows no 7 in this capture.
