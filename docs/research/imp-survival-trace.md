# Level 4 seed 8: first additional Imp death

Scope: compare store-only hash 266 with per-worker work visibility, hash 267.
The aggregate results are in [the task audit](imp-task-audit.md#store-only-campaign-check-hash-266).
This trace uses existing replays; no new campaign runs or gameplay changes.

## Identity and the relevant job divergence — CONFIRMED

Hash 267's first keeper Imp death is recorded at turn 3792, creature 40.
Do not compare creature 40 across runs: hash 266's id 40 is a Guard.
Match this Imp by its Create Imp command, turn 3471 at `(12,33)`.
That command is refused for location in hash 266 and succeeds in hash 267.
There is no counterpart Imp in the store-only run.

| Event | Store-only turn | Work-visibility turn |
|---|---:|---:|
| `(12,33)` becomes claimed floor | 3578 | 2798 |
| Turn-3471 Create Imp at `(12,33)` | refused | succeeds, id 40 |
| Guard and two Thieves appear at `(15,29)` | 3799 | 3724 |
| First keeper Imp death recorded | 3892 | 3792 |

In hash 267, Imp 3 finishes mining `(12,32)`, takes a fresh claim on its
next walk at 2778, finishes that claim at 2786 and chooses `(12,33)` at 2789.
The claim completes at 2798. In hash 266, Imp 3 is still mining eastward at
this time; Imp 14 eventually claims the cast tile at 3578.

## Jobs, exposure and retreat — CONFIRMED in hash 267

The cast Imp takes reinforcement at `(13,33)`, `(11,33)`, `(11,34)`,
`(10,34)`, `(10,32)` and `(9,31)`, choosing at turns 3473, 3531, 3589,
3648, 3710 and 3777. It reaches level 2 and 625 health at 3646.

The party born at 3724 is Guard 42 and Thieves 43/44. Guard 42 first
targets the Imp at 3777. The Imp acquires Thief 43 at 3780, flees at 3781
and starts its retreat at 3782 from `(10,31)` toward `(28,38)`.
It passes through `(11,32)` and `(12,32)` to `(13,32)` while pursued.
Thief 43 hits for 341 at 3787; regeneration adds 67 at 3788. Thief 44
hits for 276 and Guard 42 lands the remaining 75 at 3790. The death event
follows at 3792. The Imp did react and retreat; it did not remain at work
until killed.

The party appears 75 turns earlier than in hash 266. Shipped condition 1
(type 42) sets flag 0 after nine Guard Room tiles are owned. Conditions 30/31
then start timer 6 via action 65473. The room converts at 1923 versus 1998;
the stored timer stamps are 1924 versus 1999. Condition 36 tests 450 game
seconds, and action 65394 creates party 5 at action point 13. Both runs use
the same 1800-turn delay, yielding births at 3724 versus 3799. The timer
difference follows earlier room conversion, not an altered timer rule.

## Native control — CONFIRMED observation, different RNG seed

The available original capture is Level 4 seed 1, not seed 8. At capture
frame 3375 `(12,33)` first belongs to player 3. An Imp, original slot 417,
appears there at frame 3471. It repeatedly reinforces: state 22 first at
3478 and still present at 3737. At 3738 it enters withdraw state 203 with
hostile creature 424, type 14, targeted one tile away. At 3739 it is in
state 6 with no fight target. By frame 3792 it is at `(24,45)`, and remains
alive with 625 health at frame 4000. The Guard/Thief party appears at 3830.

These are capture frames, not replay turns. They show that the original
cast succeeds and its Imp also spends a long time reinforcing before
withdrawing. They do not establish a seed-matched route or trigger time.

## Binary checks and source boundaries — STRONGLY INFERRED

- Cast permission: location selector 1 requires allied, non-neutral land
  and the position checks; target 4 adds own-room exclusions. See the
  [location](functions/00517B80.md) and [target](functions/005179D0.md)
  records. Our boundary is `ImpSupplySystem.Buy`, lines 151–157, through
  `KeeperSpellRules.PlaceAllowed` and `LocationOne`. The ownership difference
  explains the observed refusal without changing the cast rule.
- Reinforcement: the [per-tile handler](functions/0046AEA0.md) tests wall,
  standing side, holder and route. Our `TryReinforceAt` and
  `TryTakeReinforce` are in `ImpTaskSystem.Selection.cs`, lines 305 and 401.
  This read and the native control provide no basis for a reinforcement
  duration cap or a new job priority.
- Retreat: the [withdraw handler](functions/004611D0.md) prefers a lair,
  then a random Heart point, rooms and local fallbacks. The Heart point
  uses independent ±2 offsets and a route test, not an escape-direction
  score. Our `FearRules.Withdraw`, lines 483–578, follows that destination
  order. This does not prove exact steering equivalence on the fatal route.

No gameplay fix is established. The first extra death includes an Imp
absent from the old run, and follows a different threat schedule. Keep the
candidate isolated pending broader acceptance. The shipped party trigger
explains the spawn difference without changing worker priorities or fleeing.

## Artifacts

Under `reverse-engineering/`, compare
`imp-store-full-20261017/level4/seed08/` with
`imp-work-visibility-runs-20261017/level4/seed08/`: `events.tsv`,
`ours-creatures.csv` and `entity-map.csv`. The native control is
`dk2runs/level4-live1/seed01/level4.dk2cap`, decoded with `dk2capture.py`.
