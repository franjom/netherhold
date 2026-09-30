# The four trigger walkers and what drives them (Q239)

**Scope:** which code walks each kind of trigger tree, how often, and where each
tree's root and repeat argument come from. Traversal *inside* a tree is
`trigger-tree.md`; what a condition or action *means* is `trigger-semantics.md`.

A level does not keep its script in one tree. Campaign level 1's world root
spawns two parties and ends; the chain that runs the level hangs off its single
**player** root. An executor that walks only the world tree will produce an empty
result on that level and it will not be a finding.

## Confidence

| Claim | Confidence | Basis |
|---|---|---|
| All four trees are walked from the world frame, so all four run once per game turn | **Strongly inferred** | Direct instruction read of `0x0050A745`–`0x0050A7F5`; the frame's own cadence is Q228 |
| Each tree's root id and repeat argument come from the fields in the table below | **Strongly inferred** | Direct instruction read of each frame |
| Creature and action-point objects reach their walker through vtable slot `0x14` | **Known** | Their frames have no other reference in any initialised block |
| Those objects are the ones in the world's eight entity lists | **Strongly inferred** | `FUN_004B7830` is the only per-turn slot-`0x14` driver reached from the world frame, and both classes' vtables share base-class entries with it |
| An action point's repeat argument is its root record's own byte, snapshotted when the root is attached | **Strongly inferred** | `0x004B0C90` is the only writer of `+0x36` besides the constructor's zero; direct instruction read |
| The area context computes no action type at all | **Known** | `0x004B0CC0` calls the world action dispatcher `0x00518AD0` directly; there is no second table |
| Action dispatch is a chain: creature → player → world | **Known** | Each default arm is a direct `CALL` to the next dispatcher — `0x0049B155`, `0x004C8A45` |
| Condition dispatch is the same chain | **Known** | Creature default loads owner `+0x2A` and calls `0x004C7B20` at `0x0049AC0C`; player default calls world |
| An action's return value cannot steer any shipped walk | **Known** | The walk descends only into `ChildTriggerId`, and all 5,408 shipped action records have zero there |

## One driver, five calls

`FUN_0050A450`, the world frame, is called once per game turn from a single site
(Q228). Within it, in this order:

| at | what it walks |
|---|---|
| `0x0050A75B` | the world tree: root `+0x7310`, repeat `+0x731A` |
| `0x0050A778` | the 24 party roots: `+0x4030 + i * 0x208`, repeat literal 1 |
| `0x0050A7D7` | `FUN_004B7830` — every entity in eight lists, through vtable slot `0x14` |
| `0x0050A7F5` | `FUN_004C2310` — every player |

`FUN_004B7830` reads eight list heads as `u16` at world `+0x08`, walks each
through the thing table at `0x006EE540` following entity `+0x08`, and calls slot
`0x14` on every element (`0x004B7851`), then slot `0x20` on any that returned
zero. **Creatures and action points are both in it**: their frames are slot
`0x14` of their own vtables and are referenced from nowhere else in the
executable.

`FUN_004C2310` walks the player list from world `+0x7A` through link `+0x3A`,
calling the player frame `FUN_004B9EC0` on each.

## The action point's frame is five instructions and its repeat is a snapshot

`0x004B05E0` reads the root id at `+0x34`, returns 1 if it is zero, and otherwise
passes the byte at `+0x36` as the repeat argument to `0x004B0CC0`. Nothing else.

`+0x36` is **not** read from the level file. The setter `0x004B0C90` writes both
fields together: it stores the id at `+0x34`, resolves it in the **condition**
array (`FUN_00518090`, whatever the sign), and copies that record's own `+0x0F`
into `+0x36`. The constructor `0x004B0420` zeroes both, and the nine bytes from
`+0x2E` round-trip through save and load as one block.

So the action point sits between the world and the player: the world's repeat is
copied once at load and never revisited, the player's is re-read from the record
every turn, and the action point's is copied once **when the root is attached**.
Re-arming a root later moves the player's number and not the action point's.

`0x004B0CC0` is the world walker's shape with one dispatcher swapped: a positive
id resolves through `FUN_00518090` and is decided by `FUN_004B0D70`, the area
condition dispatcher; a negative id goes straight to `FUN_00518AD0`, the
**world** action dispatcher. There is no area action dispatcher — the area
context computes no action type of its own.

## Where each tree's root and repeat come from

| tree | root id | repeat argument | walker |
|---|---|---|---|
| world | world `+0x7310` | world `+0x731A` | `0x00518150` |
| party (×24) | slot `+0x00` | **literal 1** | `0x00518150` |
| player | player `+0x10DA` (`0x004B9F07`) | the resolved record's own `+0x0F`, read at call time (`0x004B9F04`) | `0x004C79A0` |
| creature | creature `+0x419` (`0x0048A480`) | creature `+0x4F9` | `0x0049A980` |
| action point | point `+0x34` (`0x004B05E0`) | point `+0x36` | `0x004B0CC0` |

The player is the odd one: it resolves its root record every turn and takes the
repeat byte off the record, where the other three read a byte the loader stored
beside the id. The action-point frame is nothing else — five instructions, a
null check and the call.

The creature setter `0x0049A950` stores the root and, when it is non-zero,
resolves it in the condition array and copies that record's current `+0x0F` to
creature `+0x4F9`. The constructor zeroes both fields. **STRONGLY INFERRED**
from the complete writer set; this is the same snapshot shape as an action point.

## The second path: events, and it is not a tree walk

Each of the three entity walkers has a second caller, and it traverses
differently. `FUN_0049AA30` (creature) and `FUN_004C7A50` (player) take an
**event id** and, over the root's **top-level sibling chain only**:

- resolve each positive id (`0x0049AA54`), skipping action ids — `JLE` exits;
- compare the record's type byte `+0x0E` against the event id and skip a
  mismatch (`0x0049AA60`);
- skip a record whose active bit is clear (`0x0049AA64`);
- walk that record's **child** chain with the record's own repeat
  (`0x0049AA74`) — the condition itself is never evaluated, because its
  occurrence *is* the condition.

This is why `trigger-semantics.md` finds condition types whose eight-byte body
no handler reads. The repeat-decrement helper `0x00508640` sits behind the walk
at `0x0049AA7F`, but the child walker `0x0049A980` returns 0 on every path
(both its exits are `return 0`), so **an event never decrements a repeat** —
settled 2026-09-25; the world walker is the same (`trigger-tree.md`, "Repeat
and active state"). A record with repeat 1 still fires once, because the child
action clears its own active bit.

### Who fires what (2026-09-25, every call site of both filters)

The creature filter `0x0049AA30(creature, type, 0, 0)` has **24 call sites**
(`PreCall`, 14-instruction windows; 20 functions and 3 unattributed spans).
The player filter `0x004C7A50(player, type, targetType, targetId)` has 12; only
type 53 reads the pair, comparing it with the record's first two body bytes,
and `(0, 0xF1)` also calls world slot `+0x358`. The names are the level
editor's; `CreatureTriggerEvent`/`PlayerTriggerEvent` carry them in code.

| type | editor name | fired by |
|---|---|---|
| 3 | creature created | level initialiser `0x0050B5B0` per placed creature; action 14 per member (`0x0051A5A1`) |
| 4 | killed | death bookkeeping `0x00487C70` |
| 5 | slapped | the slap `0x0048B8A0` (two sites), with player 60 |
| 6 | attacked | fight-starter `0x0045A550`, on the target |
| 7 | imprisoned | `0x0047B740` |
| 8 | tortured | state 130 `0x00484110` |
| 9 | converted | `0x004F2800` |
| 10 | claimed | `0x0048D0ED`, unattributed span |
| 11 | angry | anger adders `0x00478B80`/`0x00478C30` on mood 2 (with notifications 17/18), setter `0x0048B870`; never for `def+0x68A` bit 2 |
| 12 | afraid | creature update `0x00488930` |
| 13 | steals | stealing booty 162/168 (`0x00482880`, `0x00482C50`) and two spans beside them |
| 14 | leaves | **no site**; no shipped record |
| 15 | stunned | `0x00491830(255)` |
| 16 | dying | countdown start `0x0048B770` |
| 54 | hunger sated | hunger decrement `0x00477AD0` at zero |
| 63 | sacked | entrance arrival 196 `0x0045A0D0`, gate 197 `0x00459D50`, with player 61 |
| 65 | picked up | hand pick-up `0x004BFBD0`, with player 58 |
| P46 | pay day | `0x004BE1A0`, after notification 46 |
| P53 | GUI button | `0x00513EB0` |
| P56/57 | dungeon breached / enemy breached | `0x00498BB0`: a creature on a tile owned by another player — 56 on its owner, 57 on the tile's, after notification 12 |
| P59 | creature dropped | hand drop `0x0048B3A0` when the thing is class 0 |

Shipped creature-event records: 3 (22), 4 (37), 6 (158), 8 (88), 10 (4),
12 (2), 13 (4), 16 (30); player 46/56/57/58/60 once each (`kwd trigger-types`).

**Built (ADR-0204):** `TriggerExecutor.FireCreatureEvent`/`FirePlayerEvent`
are the two filters; `SimulationContext.FireCreatureEvent` is the systems' hook.
Wired: 3 at executor construction and from the party spawn, 6 from a new
engagement (DK2 fires on every fight-start), 15 from the stun, 63/61 from the
entrance arrival, 16 and 4 from the health system's collapse and death, 13 from
a gold steal. Not yet wired, their sites above: 5/60, 7, 8, 9, 10, 11, 12, 54,
65/58, 46, 53, 56/57, 59 — each waits on the system that owns
the moment. An event walk's blocked and partial records are not reported.

## A tree's context decides only part of its meaning

Each context has its own condition and action dispatcher. A creature delegates
both kinds through its owning player's dispatcher; the player then delegates
what it does not own to the world. The area dispatcher delegates straight to
the world.

| context | conditions | base, range | delegates to | actions | base, range | delegates to |
|---|---|---|---|---|---|---|
| player | `0x004C7B20` | 19, `0x37` (19–74) | `0x00518270` at `0x004C856C` | `0x004C8620` | 10, `0x28` (10–50) | `0x00518AD0` at `0x004C8A3E` |
| creature | `0x0049AAA0` | 3, `0x3F` (3–65) | **`0x004C7B20`** at `0x0049AC0C` | `0x0049AC80` | 15, `0x1C` (15–43) | **`0x004C8620`** at `0x0049B151` |
| area | `0x004B0D70` | 39, `0x1F` (39–70) | `0x00518270` at `0x004B0EC9` | none | — | `0x00518AD0`, from the walker itself |

**Both entity dispatchers delegate in a chain, not a star.** The creature
default loads its owning player from `+0x2A`, then calls the player dispatcher;
the player's default passes on to the world. A creature tree therefore has
three tables to fall through for conditions and actions alike.

So a flag test (type 1), a timer test (2) and a level-time test (34) mean the
same thing in a player tree as in the world tree, over the same level-global
banks — 1 and 2 because they fall below the base, 34 because its own arm is the
default. Likewise action types 7, 8 and 14 fall below the player action base and
reach the world. **Reusing the world evaluator inside a player tree is correct,
not a convenience.**

What each context owns is its own:

- **creature conditions:** types 3–16, 50, 54, 55, 63 and 65 return false on
  the per-turn path; 36 compares health percent, 37 carried-gold percent and 49
  level. The event filter is the separate path above.

- **player conditions:** 19–33, 47, 48, 52, 53, 56–61, 68, 71, 72, 73, 74.
  Eight of them — 48, 53, 56–61 — have a jump-table entry that **is** the
  dispatcher's zero-return epilogue at `0x004C850C`: `XOR EAX,EAX`, five pops, a
  `RET 4`. Not an arm that computes false, an arm that is the return. 163 shipped
  records, 159 of them type 53 (ADR-0109). Type 73 is the contrast: its arm
  starts six instructions earlier at `0x004C8500`, tests the player's `+0x134`,
  and only *falls into* the same epilogue when that word is zero.
- **player actions:** 10, 11, 24–28, 31, 34, 37, 38, 42, 50. Type 34's arm is
  bare `MOV EAX,1` (`0x004C8A37`): in a player tree it does nothing and succeeds.
- **creature actions:** 15, 20, 21, 29, 30, 33, 35, 36, 42, 43. Every one of
  these returns success, including guarded no-op exits, and is refused by the
  world dispatcher. **A type is not dead because the world has no arm for it**
  — type 15 alone has 565 shipped records.
- **area conditions:** 39, 40, 41, 42, 62, 69, 70. All seven measure something
  over the point's own inclusive rectangle; `trigger-semantics.md`, "The area
  context", has each one's quantity and comparison.

**Type 72 is not universally unhandled.** `trigger-semantics.md` establishes that
no *world* arm reaches it and that all 137 shipped records have an empty body,
both of which hold. But the player context **does** own it, at `0x004C84B8`: it
reaches an interface object through the world and returns 1 unless one of four
fields is set — `+0xDD8`, `+0xDA8 == 0x12`, `+0xDFA`, `+0xE20`. The game's cancel
handler at `0x00405362` tests the same four in the same order to decide what to
close, so they are **modal interface states** and the condition means roughly
"nothing modal is open". What each field is remains UNKNOWN (Q242).

That matters more than its obscurity suggests: 49 of campaign level 1's records
are type 72, they sit in the player tree, and one of them gates the action that
starts the timer the level's opening depends on.

## What a reimplementation gets from this

The cadence question is closed for all four: **one walk per tree per game turn**,
in the order above, from one place. Nothing here needs a second clock.

What is not closed is the event path. Its trigger is a game event, not a turn, so
it cannot be driven from the frame — every one of those 26 call sites is a
separate behaviour, and until the events themselves exist there is nothing to
fire. Level 1's script hangs off a player root and reaches it through the
per-turn path, so the per-turn path is the one worth building first.
