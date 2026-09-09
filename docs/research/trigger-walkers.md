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
no handler reads. It is also the one place the repeat-decrement helper is
reachable: `0x0049AA7F` calls it when the walk returns non-zero, where every
exit from the world walker returns zero (`trigger-tree.md`, "Repeat and active
state"). Whether these walkers can return non-zero is **UNKNOWN**.

The creature event filter has 18 callers, including the level initialiser and
action type 14's party creation, which fires event 3 for each creature it makes
(`0x0051A5A1`). The player filter has 8.

## What a reimplementation gets from this

The cadence question is closed for all four: **one walk per tree per game turn**,
in the order above, from one place. Nothing here needs a second clock.

What is not closed is the event path. Its trigger is a game event, not a turn, so
it cannot be driven from the frame — every one of those 26 call sites is a
separate behaviour, and until the events themselves exist there is nothing to
fire. Level 1's script hangs off a player root and reaches it through the
per-turn path, so the per-turn path is the one worth building first.
