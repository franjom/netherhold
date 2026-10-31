# Reinforcement selection and work (Q696, Q564)

## Selection

STRONGLY INFERRED from whole `0x00453E90`, including its unsigned health
comparisons, read 2026-10-31. Full executable-block CallScanAll finds only
`0x0046AEF1`, in the reinforce/repair handler `0x0046AEA0`.

Every accepted tile is Solid, penetrable and goldless. The owner branches are:

- Own tile: must also be Ownable and health strictly below MaxHealth shifted
  right one. It does not ask the own-floor helper in this branch.
- Other owner: must be exactly the neutral global. Ownable terrain requires
  health below that same half threshold and `0x00453B30` touching own floor.
  Non-Ownable terrain requires health unequal to MaxHealth, not strictly less.
- Any rival's tile fails.

The handler tries this kind6 pass before kind16 repair. The standing-side and
route requirements remain separate ([imp-task-stealing.md](imp-task-stealing.md)).

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

## Built scope

Built at hash/save 349 (ADR-0607) through `ReinforcementRules.CanStart`,
`TileWorkRules` and the Imp selection/work systems. Held kind-6 arrival
does not repeat the fresh-search health threshold; dig cancellation remains.
No new persistent fields. Native Level 7 outcome and broader replay parity
have not been measured for this change.
