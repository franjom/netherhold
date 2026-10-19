# Method: what the emulator oracle has held to DKII.EXE

Source(s):
- `DKII.EXE` v1.70 (GOG) itself, run on the Unicorn emulator by `tools/oracle/dk2oracle.py`
  (ADR-0481); no decompiler output enters this repository (ADR-0046)
- Level 4 seed 1's full-state captures (`level4-live1`, `level4-dump`) and the whole-process
  dump taken at tick 500 of the second, all under the gitignored `reverse-engineering/dk2runs/`

Original version: DK2 v1.70 (GOG)

Questions: **Q600**, **Q601**, **Q614** (raised here).

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
    on lava. A fresh idle Level 7 (`level7-dump`, dumps at ticks 7, 100 and 500) holds 3 on both
    Hearts' centres in all three, the 1% Heart's included after its heart object is gone at tick
    21. The 2026-09-17 Level 7 dump that held floor there is unexplained.
- **The water flag (Q614).** The per-tick caller `0x004D5A40` sets every mover global but
  `0x006EC9E4` (water), so a creature's steering reads whatever the flag's last writer left.
  Its one reader is `Nav_StepCost` `0x004C8BD0`, the router's and `Avoid_Walls`' step cost. Its
  four writers: the route request `0x004D0F60` and `Creature_CanRouteTo` `0x004D0E60` (definition
  `+0x686` bit 4), and two step tests, `0x0048EBF0` and the possession one `0x0040F6B0` (that bit
  or `Creature_IsFlying`). Every shipped creature has the bit but the Vampire and the Unique
  Vampire, so the stale value can differ from a creature's own only beside one. The step cost
  refuses a step between a water byte and a non-water one only while the flag is 0: a Vampire
  steering after any other creature's writer may take such steps, and any creature steering after
  a Vampire's request may not. By a survey of the shipped THINGS files and maps (2026-10-15),
  Vampires are placed on 11 maps, eight with water (Level 13: 29 of them, 510 water tiles); a
  Graveyard makes more. The test gives each case its own value, as our profile does.
- **The water flag measured on Level 13 (Q614).** A fresh Level 13 (`level13-dump`, the owner
  building in their own dungeon) dumped at ticks 10, 100, 200, 300, 400, 500, 700 and 1000. The
  flag in memory was 0 at ticks 100 to 400, consistent with a Vampire writing last, and 1 at the others. On each
  dump `Avoid_Walls` was run for all 102 live creatures with the flag at 0 and at 1: the force
  differs for 1 to 5 of them per dump, up to 4 of the 29 Vampires and, beside water, Monks,
  Guards, a Giant, a Dwarf and an Imp. So where a dumped value is the one the next steering call reads, the stale flag changes DK2's answers;
  whether that moves an outcome is not measured. The probe was a scratch script, not committed.

## Confidence

Every equality above is binary evidence, run rather than read: CONFIRMED for what each function
computes on the inputs run, and a sample between them. `[Dk2Behavior]` still rises no higher than
STRONGLY INFERRED on this alone (ADR-0046 rule 5). Q600's and Q601's answers are the binary's readings, seen on one capture
(Q601 on two).
