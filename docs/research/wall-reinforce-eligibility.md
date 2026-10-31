# Reinforcement selection and work (Q696, Q564)

## Selection

CONFIRMED binary predicate from whole `0x00453E90`, including its unsigned health
comparisons, read 2026-10-31. Full executable-block CallScanAll finds only
`0x0046AEF1`, in the reinforce/repair handler `0x0046AEA0`.

Every accepted tile is Solid, penetrable and goldless. The owner branches are:

- Own tile: must also be Ownable and health strictly below MaxHealth shifted
  right one. It does not ask the own-floor helper in this branch.
- Other owner: must be exactly the neutral global. Ownable terrain requires
  health below that same half threshold and `0x00453B30` touching own floor.
  Non-Ownable terrain requires health unequal to MaxHealth, not strictly less.
- Any rival's tile fails.

The handler tries this kind6 pass before kind16 repair, and the kind16 pass
runs after every kind6 pass that took no side (Own-wall retry cycle). The
standing-side and route requirements remain separate
([imp-task-stealing.md](imp-task-stealing.md)).

## Acceptance is not a health application

Whole action7 `0x00497960` first asks `0x00453DC0`: a neutral, Solid,
penetrable, goldless wall at health unequal to maximum, touching own floor.
Only a success awards experience and applies the reinforce health delta.
An own wall selected as kind6 consequently gets no health or work experience
from this action. Q564's selector reading is correct; its proposed own-wall
50-rate repair is contradicted by the action guard. A held kind16 repair is
not reclassified by this fresh-search predicate.

Whole entry21 `0x004662E0` checks the kind6 task and facing, then enters22;
it does not repeat the selection predicate. State22's continuation and corner
tail are in [wall-corner-fill.md](wall-corner-fill.md). Action7 has no direct
caller: its pointer is installed at `0x00495283`; entry21's is installed at
`0x0047C071`.

A neutral Ownable wall accepted below half continues healing beyond half.
At its same-type maximum, the shared applier only clamps health, without
transferring ownership. It therefore stays neutral; see the same-type arm in
[tile-work.md](tile-work.md#q215--there-is-no-work-kind-guard).
Neutral Rock converts to reinforced terrain and takes the worker's owner.
The health writes are those of `0x00452F60`, not the selector.

## Own-wall retry cycle

Mechanism CONFIRMED, observable unverified. Whole state 22 (`0x00466390`)
rejects an own wall at its continuation test and reaches the tail at
`0x004665DB`: it deletes a matching kind-6 entry, clears its held id and
returns to 10. The pool wrapper `0x00508040` and remover `0x00507EF0` only
unlink/free that entry and clear its worker slots; they write no tile,
blacklist, failure deadline or retry flag. `0x00507BB0` can raise kind 6 again.

Whole state 10 (`0x00463DF0`), ring scan `0x00468D10` and Tier-B callback
`0x00469580` supply no per-wall failure memory. The selector's per-player
search throttle can delay a retry, and higher-priority work can interrupt it.
If the same eligible wall is reached with a free reachable side, the handler
`0x0046AEA0` takes kind 6 again. The tile's existing ring-candidate bit is
read, not cleared by task deletion. Thus the inspected native mechanism permits
repeated approaches without health work; this is not a capture proving
indefinite visible idling in a running game.

The kind-16 pass (`TaskPool_Find(0x10)`, predicate `0x00453E30` at `0x0046B1FB`)
runs whenever no kind-6 side started a walk: `0x00453E90` failed, or every side
was not open, not the imp's, held and not stealable, or its walk failed. Kinds
6 and 16 are separate pool entries with their own per-side holder slots
(`0x00508200`, `0x00508110`, `0x00508160`); only a failed `TaskPool_Raise`
returns 0 for the whole handler. So an own wall below half health is repaired by
a second Imp once the first holds its only kind-6 side, and a lone Imp loops on
kind 6. A wall from half up to below maximum is kind 16 only. A held repairer
(states 23/24) revalidates with `0x00453E30` at `0x004667AA` with no half test,
so it keeps repairing below half.

Lock-on, found and not changed: an Imp dropped beside an own below-half wall
gets aim 6 (`0x0046407F`), and its aimed retry (`0x00463ECE`) finds that wall
again before Tier A. Ours has the same structure; neither game has been observed.

Caller inventories: state 22 has dispatch pointer `0x0047C18C`; the handler
has three direct callers and one search callback pointer; delete-by-id has 29
direct sites; remove-by-tile has five. State 10 has dispatch pointer
`0x0047BDCE`, Tier B pointer `0x004640E9`, and the ring scan six direct sites.
These were scanned across both executable blocks before reading the bodies.

Owner experiment: damage an own reinforced wall below half health. Does a lone
idle Imp loop without repairing, and do two Imps reinforce and repair together?
Q564/Q696 remain open for this check.

## Built scope

Built at hash/save 349 (ADR-0607) through `ReinforcementRules.CanStart`,
`TileWorkRules` and the Imp selection/work systems. Held kind-6 arrival
does not repeat the fresh-search health threshold; dig cancellation remains.
The retained Reinforce task now survives every represented state outside 22,
including the approach walk; this removes the former abandon-before-arrival
check rather than changing only state-21 entry.
No new persistent fields. Native Level 7 outcome and broader replay parity
have not been measured for this change.

The two-pass fall-through is built at hash/save 355 (ADR-0611):
`TryReinforceAt` walks the sides for kind 6, then kind 16;
`ImpCrowding.SharesSlots` counts face slots per wall kind and
`ForgetReinforceTask` forgets only the failed kind. `TouchesOwnFloor` is still
applied to own kind-6 walls although `0x00453E90`'s own branch has no
`0x00453B30` call; it cannot fire, since a wall job already needs the Imp on own
land (ADR-0132). A failed `TaskPool_Raise` is not modelled (no pool capacity).
The [current Level 11A repair-window rerun](computer-keeper-observations.md#second-same-map-comparison-level-11a-drako-2400-ticks-2026-10-06)
retains the historical calibration separately from our hash/save 353 result.
