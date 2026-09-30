# Imp task stealing: who takes a held task, what the loser does, what ends a trade

**CONFIRMED** unless a paragraph says otherwise. DKII.EXE v1.70 (GOG), read-only Ghidra
12.1.4/PyGhidra on a worker copy of Claude's project, 2026-09-30. Decompiler output stayed
under `reverse-engineering/` (ADR-0046); every fact below is in our words with its
address. No capture was compared; §10 names the checks. `imp-task-priority.md` owns the
selection order and §7 there is superseded by this note. `travel-states.md` owns the walk
protocol, `state-categories.md` the category records this note leans on.

Distances everywhere are DK2's squared straight-line distance: per axis `|d|² >> 12`
with `d` in 4096ths of a tile, summed. `0x32` in that unit is about 0.11 tile.

## 1. The rule in one paragraph

Every handler that finds a task reads the pool entry's `Worker[side*3+slot]`
(`0x00508110`). A zero slot is taken at once. A held slot is taken when the holder is a
live creature with a state machine, is not teleporting (51, 52), may enter state 10 by
the transition rule (`0x00477310`, run on the holder's own state object at
`creature+0x24E`), and is farther from the handler's measuring point than the taker,
strictly except for the dig handler's `0x32` slack. Taking it means `SetState(10)` on the
holder through the creature wrapper `0x0048AD30`, which adds `0x24E` and calls
`0x00476D30(state, 0)`, then the taker writes its own id into the slot (`0x00508160`).
The holder's job block, route and last-kind byte are left as they were; only its state
changes. Nothing marks a fresh taker, a worker at work or a state set this tick as
protected. There is no give-up in a walk, no timeout, and no explicit anti-ping-pong
rule: trades end because the comparison is strict, the holder keeps moving toward the
point while the loser has to reselect, and reselection is capped at four imps per
keeper per tick.

## 2. Every handler that steals

| kind | handler | slot read | point measured | takes a held slot when | kick vs own walk | dead or non-creature holder |
|---|---|---|---|---|---|---|
| 1 from state 10 | dig walk `0x00469AF0`, kind-1 arm | each of 3 slots on each of 4 sides | taker: the task tile's centre (its ranking distance); holder: the side tile's centre | taker < holder, strictly | kick first, no re-measure | free |
| 2 from state 10 | dig walk `0x00469AF0`, kind-2 arm (compare after `0x0046A217`) | as above | both to the side tile's corner, the taker from where it stands | taker < holder, strictly | kick first, no re-measure | free |
| 1, 2 per tile | dig handler `0x0046A550` | as above | the slot's stance point (`0x00698A80`) | taker < holder + `0x32` | kick first | free |
| 3 | claim `0x0046AB50` | `[0][0]` | tile centre | strictly nearer | own walk first, then kick | **refused** |
| 14 → 3 | just-dug claim `0x00469600` | `[0][0]` of the kind-3 task it creates | tile centre | strictly nearer | kick first | refused |
| 6, 16 | reinforce/repair `0x0046AEA0` | `[side][0]` | the wall tile's centre, not the reinforce point | strictly nearer | own walk first, then kick | refused |
| 7 | gold `0x0046B6D0` | `[0][0]` | the gold pile's own position | strictly nearer | kick first | free |
| 8 | objects `0x0046B9C0` | `[0][0]` | the object's position | strictly nearer | kick first | free (STRONGLY INFERRED, skimmed) |
| 11 | `0x0046CB80` | `[side][1]` | the side's stance point | taker < holder + `0x32` | kick first | free (STRONGLY INFERRED, skimmed) |

The holder test is the same text in all eight (`0x0046AD30`–`0x0046AD68` in the claim
handler): liveness byte at `0x006ED540[id]`, thing kind `+0x12 == 0`, `creature+0x252`
non-zero, state `+0x25E` not `0x33`/`0x34`, then `0x00477310(10)` with `ECX = creature
+ 0x24E`. `0x0048AD30` is called with `ECX` = the holder creature in every site
(`pre_48ad30`, 8 sites); its result is `SetState`'s.

**Kick order matters twice.** Claim and reinforce start their own walk
(`0x004651A0`) before kicking; if the kick is refused they return 0 with the thief
already walking under state 6 and its job block still naming its previous task. The
kick-first handlers (dig, gold, objects, 11, just-dug claim) leave a holder in state 10
whose slot still names it when their own walk then fails; §7 has the consequence.

**A free slot beats a held one only within one task.** In the dig walk a task replaces the
best so far only when strictly nearer, whether its slot is free or held (`local_48 =
local_40` on both paths). Within a task the sides are tried from the one facing the imp
(`0x004805F0`); a free slot ends that task's search at once, a stealable held slot is
remembered and the search goes on, so a free slot later in the side order wins over a
stealable one earlier, and among stealable slots the last one wins.

**A returning miner ranks from its last seam but steals from where it stands.** With
`+0x98 == 2` the kind-2 arm measures each seam from the last task tile (`+0x8C/+0x90`)
for the nearest-seam ranking and for the side order, and from the imp's own position in
the steal compare. A miner walking back from the treasury is therefore far from every
held slot and takes none; a model that measures the steal from the last seam makes every
returning miner win and produces chains of miners taking one slot in turn.

## 3. The claim path (Q1)

`0x00468520(3, radius)` runs the ring scan `0x00468D10` with `0x0046AB50` as its per-tile
callback (`0x00468591`) and `0x00469340` as the start-side chooser; the drop scan reaches
the same handler through the nine-step chain `0x00469240`. The per-tile switch
`0x004687C0` reaches it too, but its only caller is `0x00464170`, which passes kind 14.

The ring scan (call sites `0x00468DA0`, `0x00468ED8`, `0x00468FB0`, `0x0046908D`,
`0x00469175`; the same stack slot every time) calls the handler on the imp's own tile with
no filter, then on each ring tile whose byte `+0xB` bit `0x20` is clear or which carries a
kind-1 or kind-2 task (`0x00468E99` and its three siblings). It reads no worker slot.

**Bit `0x20` is "nothing owned within one tile" (Q487, writers found).** Loaders set the
byte to `0x20` (`0x005D3EF0`, `0x005D3FA5`, `0x005D57AD`). The only recompute is
`0x004502D0(x, y)`, called from the tile-change notifier `0x00455030` (four sites) and
`0x0044FC40`: it clears the bit on each of the eight neighbours when this tile is owned,
not solid and not liquid, and on the tile itself when any of the nine is owned, the tile
is not liquid and something in the nine is passable; otherwise it sets it. So for claims
the filter only repeats the handler's own adjacency test.

The handler's gates, in order: tile owner not the imp's; terrain flags `0x81` and `0x100`
clear; world slot `+0x21C(x, y, 0)` returns 0, which is `0x005092F0` → `0x004528E0` →
`0x004D1A20`: the navigation byte's high nibble, a closed door's owner
(`structure-blocks.md`), so **a tile with a door is never claimed**; owner neutral or not
allied (`0x004BF3D0`); one orthogonal neighbour is the imp's passable floor
(`0x00453B30`); a kind-3 task found or raised; the tile is plain floor (state 15), an
enemy room (17) or enemy floor (19). Then the slot test of §2. **There is no "someone is
walking here" check**: the only thing that stands between two claimers is the slot and the
distance.

## 4. Reinforce, gold and Tier B (Q2)

Tier B is the whole-map ring scan with `0x00469580` per tile: reinforce/repair, gold,
objects, kind 11, first taker wins. Reinforce needs `0x00453E90(owner)` on the wall;
repair `0x00453E30`. Each walks the four sides from the facing one; a side counts when
world slot `+0x1FC` passes, the side tile is not solid and belongs to the imp. The task is
raised only when a side is taken. On a held side the comparison is to the wall's centre;
a nearer held side is stolen before a later free side is tried. After a successful walk
the fields are `+0x8A` id, `+0x8C/+0x90` wall, `+0x94` side, `+0x96` 0, `+0x98` 6 or 16.
When the walk fails on a fresh task the entry is freed again (`0x00508040`).

Gold needs terrain flag `0x80` or the imp's own tile, and the imp not nearly full
(`0x00464360(1)`). It walks the tile's things for neutral gold objects (`+0x12 == 2`,
definition flag `0x40`); the kind-7 task is per tile. A held task whose pile is
unreachable (`0x00471630(pile, 0x80)`) is skipped for the next pile. The distance is to
the pile, so two piles on one tile can give two answers against one holder. The kick comes
before the walk; a walk failure then goes on to the next pile.

## 5. What the loser gets (Q3)

`0x0048AD30(10)` is exactly `SetState(10, 0)` on the holder's state object. From a work
state (13–26, category 3) or from state 10–12 that is a same-category commit: `+0x10`
= 10, `+0x18` = the turn, `+0x1C` = 0, `+0x44` = 0, no hook. From a walk (state 6,
category 2) the saved caller at `+0x40` is compared instead, and for a worker that is a
category-3 state, so again a plain commit; `+0xDF` keeps the stale resume.

Nothing else is touched: `+0x8A` task id, `+0x8C/+0x90` tile, `+0x94/+0x96`, `+0x98`
last kind and `+0x9D` aimed kind all stay. The pool slot is overwritten with the taker's
id when the taker's own walk succeeds. The route is not cancelled; the body mover in the
creature update runs for any state whose record flag byte has bit 0, which 10, 11 and 12
have and 13–26 do not (`0x00488930`, the gate before `0x004D5AF0`), so **the loser keeps
walking its old route until a new task loads a new one** (STRONGLY INFERRED: the mover's
path following was not re-read here; `steering-behaviours.md` §5).

**It chooses again at its next dispatch, which is the same tick when it is updated after
the thief** (§8). The dispatcher `0x00476FC0` runs the state handler whenever `+0x44` is
zero; it never compares `+0x18` with the turn. State 10 then does what
`imp-task-priority.md` §4 says, and two of its steps bear on the loser:

- the per-keeper cap `player+0x7A`: past four selections this tick the loser goes to 12
  for `20 + rand(80)` ticks (`0x00463E47`);
- step (a): `+0x98` is still set, so the loser searches the kind it just lost whole-map
  first, and finds the same task with the thief in its slot.

Nothing forbids retaking it. The loser must now be strictly nearer than the thief by the
same measure it lost under, and it is not, unless the two measures differ (dig walk:
wall centre against side centre) or the `0x32` slack applies (dig handler, kind 11).

The category-3 leave hook is `0x004647D0` (`state-categories.md` §7.1; the store is at
`0x0047995F`). It is also what every taker calls on itself before its walk. It ends the
current action, drops a carried object or creature, and clears **only the pool slot of
the kind its current state names** (13/14 → kind 1, 15–20 → 3 or 14, 21/22 → 6, 25/26
→ 2, 27 → 7, 30 → 8, 34/35 → 9, 36/37 → 10, 39 → 11, 40/41 → 12, 42 → 13; a walk is read
through `+0xDF`). It never clears `+0x8A` or `+0x98`. A loser in state 10 hits no case, so
its next take does not wipe the thief's slot.

## 6. Protection (Q4)

There is none for a worker. `0x00477310(10)` is the transition rule of
`creature-state-table.md` §2.2 with the target fixed at 10 (group 8, category 3):

- from 13–26, 10, 11, 12, 27–29 (group 8, category 3): allowed by the equal-category test;
- from a walk (group 9, category 2): judged by the resume `+0xDF`; a resume of category 3
  passes, so every job walk and every deposit walk (28) is stealable;
- refused only from a category whose bit is not in category 3's entry mask `0x3`
  (categories 0 and 1) and which is not 3 itself: a creature in the hand (76, category
  7), in combat (22) or in any non-worker category. Those have already released their
  slot through the leave hook when they left category 3, so the refusal protects nothing
  a worker holds.

51 and 52 are excluded by the handlers, not by the rule. No handler reads the state stamp
`+0x18`, the ticks-in-state `+0x1C` or the hold counter `+0x44`, so an imp that took a task
this tick, one mid-swing and one kicked a moment ago are all equal candidates. The dig
walk's kind-2 arm refuses a slot with a gold object in it (thing `+0x12 == 2`, `+0x12C ==
1`, `+0x14A` above `[0x006F25CD]`); nothing refuses a slot because a creature stands on it.

## 7. Deadlocks and ping-pong (Q5)

**No walk gives up.** State 6 (`0x00470C80`) resumes when `0x004D54F0` reports 1.0,
which it does when the path is finished or when there is no path at all (`+0xFA == 0`);
otherwise it returns 1 and waits. A route that cannot be built fails at take time
(`0x00471890` → `0x004D0F60`), and the handler moves on. Being stuck behind another imp
is steering's business: `Follow_Path` re-requests the route after five stuck ticks in
fifteen (`steering-behaviours.md` §5.1), with no state change.

**Arrival revalidates the task, not the slot.** 13 (`0x004654F0`) and 25 (`0x00465790`)
need the id at `+0x8A` to still be a task of their kind (`0x00508070`) and the imp within
one tile of it, else they release and go to 10 with `+0x8A` cleared. 21 (`0x004662E0`)
checks the kind only. 15 (`0x00465BC0`) needs the imp on the tile. 14 (`0x004655D0`)
swings while the tile is still diggable. **None checks that `Worker` still names the
imp**, which is safe only because every taker kicks the holder first or right after its
own walk. The "waits if its stance is occupied" of `imp-state-exits.md` for 13, 21 and 25
is wrong: `0x004B2AE0` turns the creature's facing (`+0xF4`) toward the point by at most
its turn rate (`definition+0x6A0 & 0x7FF`) and returns the remaining angle; the states
wait for the turn, never for another imp (STRONGLY INFERRED on the rate field, CONFIRMED
on the shape).

**What stops a trade.** A steal needs the taker strictly nearer than the holder to one
point, and the holder is walking to that point while the loser has to be dispatched, pass
the four-per-tick cap and rescan. Two imps can trade at most while their distances differ
by less than the measure's bias: none for claim, reinforce, gold, objects and mining; the
wall centre against the side centre in the dig walk's kind-1 arm (an imp beside the wall
but not beside the side tile can take from one at the same distance); `0x32` in the dig handler and kind 11,
which only the drop scan and `0x00464170` reach. Each trade moves the new holder toward
the stance, so the window closes within a few ticks. This is arithmetic, not a rule; the
capture check in §10 is what would show a long trade.

**Two ghosts the code allows.**

1. A kick-first handler kicks the holder and then its own walk fails: the holder is in 10,
   the slot still names it, and it may keep walking its old route. Every later candidate is
   measured against that imp's position; the imp itself retakes the slot only where the
   measure is biased in its favour (the kind-1 arm's two centres, the `0x32` handlers), never
   under the one-point rules, since it is not strictly nearer than itself. The slot frees when the
   ghost's next take runs the leave hook from a work state, or when the task is removed.
2. Claim and reinforce return 0 when the slot names a dead or reused id (`0x0046AD36`,
   `0x0046AD42` and the reinforce twins), while dig, gold and objects treat it as free. A
   kind-3 or kind-6 task whose worker died is untakeable until the entry is removed (16
   removes competing claim tasks; the wall's neighbour changes re-raise kind 6). Slots are
   reused (`an-entity-id-is-a-slot`), so the id can even come to name an object.

## 8. Update order (Q6)

Not slot order. The world turn `0x0050A450` calls `0x004B7830` at `0x0050A7D7` with the
eight `u16` list heads at world `+0x08`; it walks each list through `thing+0x08` and calls
vtable slot `+0x14` (the creature update `0x00488930`) on every element, then a second
pass with `+0x28`/`+0x30` for removals. The linker `0x004B4A10(kind)` pushes a new thing at
the **head** of list `kind` (`thing.next = head; head.prev = thing; head = thing`, heads at
`[0x006E504C] + kind*2`, the object the constructor `0x004B5A10` zeroes); `0x004B4AB0`
unlinks. So within kind 0, creatures are updated **newest first**, and a reused slot moves
to the front. Imps created by the same drop or portal in the same tick are updated in
reverse creation order, and a thief older than its victim has its victim reselect in the
same tick; a thief newer than its victim leaves it until the next tick. STRONGLY INFERRED
that `[0x006E504C]` is world `+0x08`: the walker takes `LEA ECX,[world+8]` and the
constructor is the only writer of eight zeroed heads.

## 9. What differs from the earlier reading

- Reinforce measures to the wall tile's centre and is per side; a nearer held side is
  stolen before a later free side is tried. Gold measures to the pile, skips an
  unreachable pile, and kicks before walking.
- The dig walk's held slot is a fallback only inside one task; across tasks the nearest
  task wins, free or held. Mining compares both imps to the side tile's corner from where
  they stand; the last seam only ranks the seams and orders the sides.
- Claim and reinforce kick after their own walk and refuse a dead or reused holder;
  dig, gold, objects and 11 kick first and treat such a holder as free.
- The loser keeps its whole job block, its route and `+0x98`; only the slot changes hands.
- 13, 21 and 25 wait to turn toward the point, not for an occupied stance.
- Bit `0x20` of tile byte `+0xB` has one writer, `0x004502D0`, and means nothing owned
  within one tile.
- Creatures are updated newest first, not in slot order.
- World slot `+0x21C(x, y, 0)` is the door nibble; a claim never targets a door tile.

## 10. Capture checks

Captures: `reverse-engineering/dk2runs/level1-live1*/`, `level2-live2/`; decode with
`dk2decisions.py` (a `lost` line is a kicked holder) and compare with `dk2tools decide`.

1. §2 rules: for every `lost` line, recompute both distances from the frame before with
   the point of the handler that took it; the taker must be nearer under that handler's
   rule, and no other imp nearer than the taker may have had a free slot on that task.
2. §5 same-tick reselection: a loser with a lower creation order than its thief should
   leave state 10 in the same frame; one with a higher order in the next.
3. §5 route: a loser that finds nothing should still move along its old path for a few
   frames before idling.
4. §7 trades: count chains of `lost` lines on one task alternating between two imps; the
   claim kinds should show none, digging only chains of a few frames.
5. §8: read the eight heads at world `+0x08` from a frame and check the creature list is
   in descending creation order.
6. §3: no kind-3 task should ever be taken on a tile whose navigation byte's high nibble
   is set.

## 11. Addresses read in full

`0x00463DF0`–`0x00464232` (state 10 and `0x00464170`), `0x00468D10`, `0x00469240`,
`0x00469580`, `0x00469600`, `0x00469AF0`, `0x0046A550`, `0x0046AB50`, `0x0046AEA0`,
`0x0046B6D0`, `0x004647D0`, `0x004651A0`, `0x00470C80`, `0x00476D30` (via
`travel-states.md` §3), `0x00476FC0`, `0x00477310`, `0x00478050`, `0x00478080`,
`0x0048AD30`, `0x004B2AE0`, `0x004B4A10`, `0x004B4AB0`, `0x004B7830`, `0x004502D0`,
`0x004528E0`, `0x004D0F60`, `0x004D1A20`, `0x004D54F0`, `0x004654F0`, `0x004655D0`,
`0x00465BC0`, `0x004662E0`, `0x00465790`, `0x00466850`, `0x004653F0`, `0x004654A0`,
`0x00464240`, `0x00508040`, `0x00508070`, `0x00508110`, `0x00508160`, `0x00508200`,
`0x00507EF0`, `0x005081C0`, `0x005080E0`. Skimmed: `0x0046B9C0`, `0x0046CB80`,
`0x00488930` (the mover gate and the call sites of `0x00476FC0` and `0x00464170`).
