# Level 4 trigger timer stamps

DKII.EXE v1.70, Level4 seed 1, packet `l4live.sav` offset 69. Native capture:
`D:/DK2Evidence/dk2runs/level4-live1/seed01/level4.dk2cap`.
Evidence: `D:/DK2Evidence/runs/trigger-timers-codex-20261028/`.
The reported baseline is `q665-job-choice-2026-10-28/resync-guard/run3790-h322/`.

## Clock and observation boundary

**STRONGLY INFERRED, binary:** action 8 at `0x00519147` reads body byte 0,
calls `0x00517490` and writes that value at world `+0x3FF0 + 4*id`.
The accessor returns the container's turn. `0x00526590` calls the world update
at `0x00526AA7` and increments container `+4` at `0x00526B9E`, after it returns.
There is no clock increment between world, creature, area and player roots.

**CONFIRMED, capture:** the timer-11/12 stamps 107 first appear in header 108;
timer-3/14/15 stamps 1575 first appear in header 1576. Both frames are untorn.
Header 107/1575 still holds zero in the respective timers. This is ADR-0119's
observation boundary: a header holds the preceding completed turn's state.
Compare stored stamps directly; subtract one from native headers when assigning
these changes to an executed turn. Our replay CSV and event TSV use executed,
one-based turns. Merely comparing row numbers would hide the opening difference.

## Timers 11 and 12: camera completion

Player 3's root 2 reaches condition 71 (type 34, level time >= 0), whose
action chain starts path 204 at point 27. That chain reaches condition 48
(type 72, camera still), then action 65415 (hide point 28), 65460 (start timer
11), 65435 (flag 18 := 1), and 65433 (start timer 12). These actions are
siblings under condition 48; both stamps are written in its successful walk.

| Boundary | DK2 | Ours, hashes 322/323 |
|---|---:|---:|
| Condition 48 succeeds / both stamps written, executed turn | 107 | 108 |
| First native capture header with the stamps | 108 | n/a |

**STRONGLY INFERRED:** this is the existing camera-clock approximation, not
an action-8 tick error. Type 72 at `0x004C84B8` tests camera mode `0x12` and
deadlines `+0xDD8/+0xDFA/+0xE20`. `0x0044BED0` advances paths by elapsed real
milliseconds times 0.03; ours uses nominal 250-ms turns (ADR-0496/0498).
The capture's header 107 is already at 27.197 seconds after recorder start,
and header 108 at 27.449 seconds. Recorder start is not path start, and the
capture contains no camera state, so the precise render frame which ended the
path is UNKNOWN. Its first successful trigger turn is established by the stamps.
Do not shorten the path to fit one playback. No camera timing code is changed.

## Timers 3, 14 and 15: Guard Room capture

Action point 19 covers `(43,51)..(45,53)`. Its root 58 (type 42) asks for at
least two player-3 ownable tiles, terrain 0 meaning any ownable terrain. Its
child 65443 sets flag 8 to 1. In the subsequent player pass, condition 60
requires flag 8 == 1, condition 67 flag 7 < 1, and condition 68 flag 0 < 1;
their action chain ends with 65426 (timer 14) and 65504 (timer 3).
Condition 73, another sibling in the same player tree, asks for at least one
owned room instance of type 9, **Guard Room**; actions 65395 and 65393 close
portals and start timer 15. Timer 15 is not a descendant of condition 58.

| Boundary, executed turn | DK2 | Reported hash 322 | Fresh hash 323 |
|---|---:|---:|---:|
| Nine Guard Room tiles and room instance become player 3's | 1575 | 1572 | 1524 |
| Condition 58 first reads true; flag 8 written | 1575 | 1573 | 1525 |
| Conditions 60/67/68 and 73 succeed; timers 3/14/15 stamped | 1575 | 1573 | 1525 |

**CONFIRMED observations:** native headers 1575/1576 change all nine tiles
from owner 2 to 3, the room instance likewise, flag 8 from 0 to 1, and all
three timers from 0 to 1575. Baseline event TSV logs `room_converted` at 1572,
and its tile CSV and trigger CSV establish the modern rows above. The fresh
hash-323 run uses the same packet, entity map and seed, without resync.

**STRONGLY INFERRED, binary-backed rule difference:** DK2's creature work runs
before list-5 action points (`0x004B7830`), then rooms, then player frames
(`0x0050A450`). The player root is at the start of `0x004B9EC0`.
Our single pre-system trigger pass read ownership one turn late. The reported
two-turn lead therefore combines capture three turns early with a one-turn
late read. The newer baseline's different capture turn demonstrates why this
is not a constant stamp correction. ADR-0568 moves the walks to their phases;
the capture drift itself is not attributed or tuned here.

Creature roots also belong after their own state handler and movement, at
`0x0048A480`, before the next creature updates. Standalone `TriggerExecutor.Run`
retains a complete walk for trigger-only tools; the default simulation schedules
the roots during its system sequence. World/party roots retain their early pass.
Player roots remain grouped, rather than interleaved with each player's counters;
this change does not establish equivalence for cross-player counter dependencies.

## Verification

`TriggerSchedulingTests` failed both cases before the fix: same-turn claim
visibility to area/player timers, and a creature's own changed health becoming
visible to its root before the next creature runs. Both pass after the phase
split. A third regression prevents a standalone creature step from walking a
root again after the composite update. The independent reviewer confirmed the
clock, phase order and camera reading, and checked the CSV boundaries.

The final hash-324 build replayed the specified packet/offset/map/seed through
turn 2100, without resync, to `final-fixed*.csv/tsv` in the evidence folder.
Timers 11/12 remain 108. The Guard Room capture remains 1524; flag 8 and
timers 3/14/15 now change on **1524**, the capture turn, rather than 1525.
DK2 remains at 1575: the remaining 51-turn lead is capture-state drift. This
measurement validates same-turn visibility; it does not claim absolute stamp
parity or isolate the creature-timing contribution to that drift.

The merged solution suite passes 6,821 cases, with 28 skips and no failures
(`merged-solution.log`); Simulation contributes 3,937. The Release build has
zero warnings/errors. Documentation and KB gates are required before commit.
