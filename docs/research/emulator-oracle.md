# Method: what the emulator oracle has held to DKII.EXE

Source(s):
- `DKII.EXE` v1.70 (GOG) itself, run on the Unicorn emulator by `tools/oracle/dk2oracle.py`
  (ADR-0481); no decompiler output enters this repository (ADR-0046)
- Level 4 seed 1's full-state captures (`level4-live1`, `level4-dump`) and the whole-process
  dump taken at tick 500 of the second, all under the gitignored `reverse-engineering/dk2runs/`

Original version: DK2 v1.70 (GOG)

Questions: **Q600**, **Q601** (raised here).

ADR-0481 owns the decision and its limits; this page owns what has been run and what it found.
Setup, the request format and when to reach for it are in `reverse-engineering/README.md`.

## 1. Pure functions, value by value (`tests/DK2.Oracle.Tests`)

Each is swept over band edges and spread inputs, and fails if one of its arms goes unreached.

| batch | functions | found |
|---|---|---|
| 1 | the square-root, arctangent and sine tables; Math_Isqrt, Math_Atan2, Random_Next | all equal |
| 2 | GateWalk Collapse and AdjustAroundCrossing; Fixed16 Normalise, Truncate, Scale, Over | all equal where the original returns |
| 3 | Angle_Distance, Angle_TurnsUp, the possessed aim cone, the ray-circle test, the 3-vector length | the length threw on an `int.MinValue` component where the original wraps; fixed |
| 4 | Octile, Perpendicular, GateWalk.Intersect, the dig walk's start side | Intersect truncated where the original's divide faults; it now reports no crossing |

Batch 4's candidates came from `tools/oracle/purity.py`: the ported functions that touch no
directly written global and make no indirect call. Its list is of candidates; each was read
from the bytes before testing.

## 2. Our C# on a captured tick

`capture_state.py` exports a capture tick as plain values and `CapturedState` rebuilds our world
from the level, then gives it the tick's tiles and doors. Level 4's first tick equals our level
map tile for tile.

- **`0x00460B50`, the Guard Room test.** Run every tenth tick (`guard_room_on_capture.py`): 542
  calls, 86 targets within a tile of an expanded edge, every answer the rule
  `FearRules.NearOwnGuardRooms` encodes, and each Guard Room's stored bounds equal its tiles'
  extent in 470 samples. Our C# itself (`CapturedGuardRoomTests`), every fiftieth tick: all 80
  compared calls equal. Not checked: the callers' early-outs, variable 190, bounds after a room
  loses tiles. So the Level 4 hero-contact lead is not in this test.

## 3. Our C# on a whole-process dump

A capture omits definitions and other heap; a dump (`process_dump.py`, the recorder's
`--dump-ticks`) holds everything, taken with the game suspended. Level 4 seed 1's dump at tick
500 equals that tick's capture frame byte for byte, and the 5.4 s suspension left the playback in
step with the earlier capture.

- **`Avoid_Walls` (`0x004D35C0`).** Run on the dump with the mover globals written per call, it
  equals `AvoidWallsRules.Compute` on DK2's own navigation bytes for all 13 live bodies and 20,000
  synthetic bodies written into one of them, each with a random mover (every live creature at
  tick 500 walks on water): the corner reflection, the axis margins, the stuck nudge, water
  and doors as walls all reached (`CapturedSteeringTests`).
- **The navigation bytes.** Ours (`WorldSteering`, with the tick's tiles and door states, after
  our first door update) equal DK2's grid on every tile. Two were different until built:
  - **Q600, answered.** Level 4's THINGS file puts a player-1 door on (24,9), a water tile the
    map gives the Neutral player. DK2 has no door there from tick 2 and holds 0 (not water's 1).
    The owner test kills the door on its first update, ours as DK2's, and the removal
    `0x004A66F0(0)` writes 0 whatever the terrain. Ours went back to water; it now keeps floor
    there until the terrain changes (a `WorldState.NavStamps` entry, HashVersion 256), and the test runs
    our first door update before comparing.
  - **Q601, answered.** DK2 holds byte 3, lava, on the Heart's centre (24,54); ours held floor.
    The Heart's builder `0x004DB700` writes it on its chain tile 12 at load (`0x004DBB65`), and
    Level 10's capture logs the same on both its Hearts. Ours stamps it (`WorldState.NavStamps`).
    Steering reads that byte, so DK2's walls include the centre for a creature that cannot walk
    on lava. Level 7's 2026-09-17 dump, which held floor there, is unexplained.
- **The water flag.** The per-tick caller `0x004D5A40` sets every mover global but
  `0x006EC9E4` (water), which keeps the last route request's value. The test sets it per
  creature, as our profile does. Whether the stale value ever changes a steering answer in play
  is not measured.

## Confidence

Every equality above is binary evidence, run rather than read: CONFIRMED for what each function
computes on the inputs run, and a sample between them. `[Dk2Behavior]` still rises no higher than
STRONGLY INFERRED on this alone (ADR-0046 rule 5). Q600's and Q601's answers are the binary's readings, seen on one capture
(Q601 on two).
