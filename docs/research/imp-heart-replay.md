# Heart routing: one paired Level 4 replay

2026-10-18. This is a compatibility check of the owner's recorded inputs,
not an original-versus-modern determinism claim. The matched chooser and
frozen-world controls live in [imp-boundary-route.md](imp-boundary-route.md).

## Paired replay — CONFIRMED observations

Both versions run Level4 `l4live.sav`, seed 1, all 6804 turns, default xoshiro
simulation RNG. The original capture uses DK2's generator; these streams are
not matched. Each replay consumes 707 actions on 647 turns. Entity maps are
rebuilt from that version's creature output until stable; no placed-type
mismatch is reported.

| Version | Win turn | Keeper Imp deaths | Stable-map placeholders |
|---|---:|---:|---:|
| Previous gameplay, hash 265 | No win | 25 | 5 |
| ADR-0505, hash 269 | 6732 | 3 | 9 |

The preserved baseline worktree at `647c43e8` has identical simulation and
pathfinding source trees to pre-change `c8b3f69e`. Its new replay matches the
archived seed-1 replay CSV byte-for-byte. The current run uses the merged
gameplay at `2367fd35`. Baseline mapping is stable on its first pass; current
mapping stabilizes on pass five. Placeholders mean some recorded entity
commands can miss. The result supports this one replay, not a seed spread,
nor a claim that the remaining three deaths are faithful.

The first paired coarse position difference is turn 6, during initial idle
walks, before the first marked job. The changed walking history subsequently
changes which worker takes the first dig: baseline Imp 4 on turn 133, current
Imp 2 on turn 137. Both choose `(28,54)`.

## Task occupancy and movement — measured, limited comparison

Counts below are living keeper Imp-turns over the common first 1100 aligned
turns, summed across workers. Populations and job histories need not match.
Native recorder frame N is compared with modern turn N−1. The native capture
has no torn frames in this sample.

| State group | Native | Hash 265 | Hash 269 |
|---|---:|---:|---:|
| Idle: 1, 10–12, 87 | 704 | 617 | 693 |
| Walk: 6 | 2102 | 1965 | 2001 |
| Dig: 13–14 | 1098 | 1071 | 1126 |
| Mine: 25–26 | 803 | 587 | 581 |
| Claim: 15–16 | 776 | 685 | 832 |
| Reinforce: 21–22 | 112 | 0 | 399 |
| Deposit: 28–29 | 34 | 0 | 0 |
| Other states | 31 | 24 | 26 |

The walking tile-coordinate L1 sum is 915 native, 814 baseline and 866 current.
Both traces are reduced to tiles and only consecutive state-6 samples count;
this is a coarse travel proxy, not physical path length. Sparse modern logs
cannot recover sub-tile distance. Longer job sequences and per-worker counts
remain in `analysis.json` locally. Early reinforcement is still higher than
native despite the improved replay result; priorities have not been changed.

## First remaining trace differences

**CONFIRMED observation:** original slot 454 / modern creature 2 first differs
in raw entity coordinates on aligned turn 6: native `(103032,217720)`, current
`(103031,217719)`, in 1/4096 units. Its movement-order cause is established in
[imp-first-integration.md](imp-first-integration.md#cause-of-q613-movement-order-and-crowd-input). The
[native position getter](functions/004D5AC0.md) uses an arithmetic shift, so
round-to-nearest is not an explanation.

The first state difference for the initial four Imps at hash 269 was turn 18:

| Aligned turn | Native slot 454 | Current creature 2 |
|---|---|---|
| 18 | State 1, stopped at `(101555,236823)` | State 6, progress 1.0, `PathFinished=false`, `(101557,236823)` |
| 19 | State 10, position unchanged | State 1, after another move to `(101690,236367)` |
| 20 | State 12 | State 10 |

**STRONGLY INFERRED mechanism, binary rechecked:** state 6
[0x00470C80](functions/00470C80.md) integrates, reads route progress and, on
exactly 1.0, stops and resumes its saved state in that same call.
`ImpTaskSystem.UpdateWander` instead returns while `creature.HasRoute`, which
requires `PathFinished` rather than checking progress. The stage probe sees
progress 1.0 at both the movement and ImpTask boundaries on turn 18, yet state
6 remains. Job walks already use the progress condition in `UpdateMoving`.

At hash 269, `CreatureState.StopMoving` cleared the route but retained velocity;
[native stop](functions/0048ECE0.md) requests the current position with zero
velocity. That trace still held velocity `(2134,-7286)` after its turn-19 stop,
versus native zero.

Hash 280 (ADR-0512) corrects the idle and store arrivals through a scoped stop
([imp-idle-arrival.md](imp-idle-arrival.md)). The shared
`CreatureState.StopMoving` still retains velocity for its other callers; mapping
those callers onto the native stop's call sites is unverified, so it is not changed.

## Unity observation

The isolated Step5 capture project uses the merged hash-269 simulation DLL,
classic assets, Heart camera focus and ordinary idle campaign initialization.
Both levels sample actual simulation turns 1–2000, not 2000 render frames.

| Level | Imp samples | Goblin samples | Samples inside platform square | Samples inside central core |
|---|---:|---:|---:|---:|
| 1 | 8000 | 0 | 619 | 0 |
| 7 | 7891 | 3489 | 811 | 0 |

The closest observed creature-centre distances are 1.157847 tiles on Level1
and 1.059127 on Level7. The core test uses object 13's actual width/2; platform
samples use the middle 3×3. This checks positions, not mesh overlap or native
motion equivalence. Level1 spawns no Goblins in this idle setup, so it does not
reproduce the owner's Level1 Goblin report. Level7 does exercise Goblins.
Q585 remains open for owner judgment and more representative play.

The six images per run show the current renderer over this interval; Heart
arches occlude part of the interior, so images alone cannot establish every
creature's clearance. The first Level1 camera drifted after the authored camera
update; the corrected capture reapplies the Heart focus before rendering.
No owner scene or main Unity project is edited, and no images are redistributed.

## Local artifacts (ignored)

Under `build/imp-acceptance/reverse-engineering/imp-heart-replay-20261018/`:

- `pair.py`, `results.json`, `baseline/` and `current/`: replay logs, all turns,
  events, creature changes and final entity maps; each mapping pass has a log.
- `analyze.py`, `analysis.json`: per-worker occupancy, completed bouts, tile
  travel proxy, first differences and full task-choice sequences.
- `Probe.csproj`, `Program.cs`, `arrival-stages.json`: normal initialization,
  observed at movement and ImpTask boundaries for creature 2 through turn 26.
- `arrival_native.py`, `arrival-native.json`: original slot 454 and raw body
  state from recorder frames 2–27, aligned by subtracting one from frame ticks.
- `unity-level7/`: preliminary 400-turn capture; `unity-level7-2000/`: extended
  capture, assembly identity, movement audit and gameplay images.
- `unity-level1-2000/`: full idle Level1 position audit; `unity-level1-heart-fixed/`:
  corrected camera framing. `unity-probe-source/` holds the local capture source.
- `verify.py`, `getter-oracle.txt`: artifact assertions and three signed native
  position-getter cases.
