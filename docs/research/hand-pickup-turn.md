---
systems: [keeper-spells, creatures, combat]
---
# A pick-up takes a turn, and the stun's handlers follow the fight scan

## Observation (CONFIRMED, Level 5 seed-1 capture)

All twelve hand pick-ups in frames 1400 to 1900. The counter is the fight-scan
counter (`+0x3E6` bits 22 to 25).

| Case | Frames | Counter |
|---|---|---|
| No other hand command the next turn (4) | 75 on the command's turn, 78 the next | counted on both turns |
| Another pick-up the next turn (5) | 75, then 78 | counted on the 75 turn only |
| Another pick-up the same turn (3) | 78 at once, no 75 frame | not counted |

The counter stands still in 78, 76 and 255 and on the turn 257 is entered,
then counts each turn in 257; at ten the scan runs and the creature goes to
199 before 257's facing step. Four dropped Warlocks (tags 437 to 441, turns
1682 to 1705) fit turn for turn.

## Reading (STRONGLY INFERRED, binary 2026-11-10)

- The hand's action record is `+0x186` (state), `+0x18A` (turn entered) and
  three arguments. The table at `0x006E50B8` holds 0x36 bytes a state: enter
  `+0x00`, update `+0x10`, exit `+0x20`, a duration byte `+0x34` and an
  interruptible byte `+0x35` (filled by [004C06F0](functions/004C06F0.md)).
- State 1: enter [004C0BE0](functions/004C0BE0.md) (prepare, state 75), exit
  [004C0EA0](functions/004C0EA0.md) (the insert), duration 1, interruptible.
  States 4 (drop), 5 and 6 (slap) last 3 turns and are interruptible too.
- [004C09F0](functions/004C09F0.md) leaves the current state before entering
  the next ([004C0B50](functions/004C0B50.md) runs the exit callback), so the
  next hand command inserts the prepared creature first.
- Otherwise [004C0AA0](functions/004C0AA0.md), in the keeper's frame, runs the
  exit once `now - entered >= duration`. The capture puts that after the
  creatures' updates of the following turn.
- The scan `0x0045E9F0` runs before the state handler (`0x00488930`) and
  counts only past its gate `0x0045A6D0`, which refuses state 76 and the
  states flagged 0x40 (255, 256), not 75 or 257. Which of its tests stops a
  held creature is not settled (`+0x16 == 2`, Q364).

## Built (ADR-0731, hash/save 460)

- `HandSystem.PickUp` prepares: state 75, the sound, the job, load and marks
  let go, the body stopped, a pending landing cancelled. The keeper's
  `PendingLiftCreatureId` and `PendingLiftTick` are hashed and saved.
- `HandSystem.CompleteLift` inserts: before that keeper's next hand command
  once that command has passed the test DK2's action makes before entering a
  state (a live creature or object, gold on the tile, a hand that already holds
  something for a drop, the counter's pile made), or at the start of the second
  tick after the command, stamped with the turn before. A drop with only the
  lift pending does nothing.
- `TakeCreatureCommand` is the computer keeper's lift: its routines call
  `0x004BFBD0` and then `0x004BC500` with no hand state, so ours prepares and
  inserts in the command's tick. The five keeper steps issue it; a network
  peer may not. Its refusals (dead, held, states 51 and 52, a full hand, a disguise)
  are tested there.
- `CreatureStatusSystem.Recovery` holds the handlers of 255, 256 and 257 and
  runs after `combat.States` in the creature's update.
- A prisoner in 75 does not roll its wander.

Deviations: the insert is at the next tick's start, not in the keeper's frame;
a creature that left 75 before the insert is still taken (78's kick is not
modelled); 75's own height (0x2000 each turn, `0x0046EA60`) is not set; a
mint-wage command does not leave the hand's state (unread). The view still
counts a queued creature pick-up as holding, so a quick second click sends a
drop the simulation ignores.

## Control

`F:/DK2Evidence/studies/q712-pickup-scan-phase--2026-11-10--001`,
`controlled460-v3`: the four Warlocks' counters and their turns into 199 match
DK2's; the ordered draw prefix stays 5360 through turn 1719. The boundary at
1720 draw 3 is the death fall's direction pick
([0048F410](functions/0048F410.md)), not this.
