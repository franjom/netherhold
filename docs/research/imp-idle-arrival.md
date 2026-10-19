# Idle Imp arrival: native progress and body stop

2026-10-18. Follow-up to [the hash-269 trace](imp-heart-replay.md#first-remaining-trace-differences).
The correction from `fixes/imp-idle-arrival` is included in the hash/save-280
integration branch under ADR-0512 and is on master; "candidate" below names the
branch measurements made before that. Heart routing and core blocking are unchanged.
Source correction: `372a7334`; `9c519967` applies the same change in composition.

## Native comparison and scope

**CONFIRMED observation:** the scoped candidate restores creature 2's state
sequence against native slot 454 for all 26 aligned turns of the initial walk.
It stops on turn 18, retains that position through turn 26 and has zero velocity
on each of those stopped turns. The earlier coordinate discrepancy remains:
the stop is two entity units away on X. This is state/stop agreement, not exact
movement equivalence.

**STRONGLY INFERRED implementation:** [state 6](functions/00470C80.md) tests
progress exactly 1.0 after integration, then stops and resumes the saved state
in that call. The scoped candidate changes only `ImpTaskSystem.UpdateWander`:
use that progress condition, clear the route, reset the body to the committed
entity position and clear velocity before resuming idle. The common
`CreatureState.StopMoving` remains unchanged because its many callers are not
all verified native stop sites. [The native stop](functions/0048ECE0.md) and
[body helper](functions/004D1070.md) were rechecked in full.

The regressions first failed on the late arrival and retained velocity. They
cover incomplete progress, progress complete before `PathFinished`, position
alignment and stopped velocity. Hash/save 270 separates candidate behavior;
there are no new saved fields. No task priority, movement force or Heart policy
changes belong to this candidate.

## Full Level4 replay and controls

**CONFIRMED measured outcomes:** the same recorded input, xoshiro seed 1,
6804 consecutive turns and 707 actions on 647 turns as the prior comparison.
Each variant reconciles its own entity map until stable; at most six passes.
The prior hash-269 result is reused, not rerun.

| Variant | Win turn | Keeper Imp deaths | Unresolved mappings | Mapping passes |
|---|---:|---:|---:|---:|
| Hash 269 | 6732 | 3 | 9 | 5 |
| Progress arrival + shared native stop | 6800 | 9 | 9 | 5 |
| Progress arrival alone | 6768 | 9 | 8 | 6 |
| Shared native stop alone | none | 34 | 9 | 4 |
| Progress arrival + idle-only native stop | none | 38 | 8 | 4 |

The shared-stop control also pins position and clears velocity, but changes
every caller of the common helper. Narrowing it to the verified idle-arrival
site does not recover this replay. The controls do not isolate a sole cause of
the deaths: mappings differ, future RNG draws change and the simulation is not
initialized with the original game's RNG. A stable map does not establish
perfect entity identity. These outcomes diagnose campaign divergence; they do
not veto the verified binary arrival correction. Do not tune a delay to recover
one seed's win.

## Earlier precision lead

**STRONGLY INFERRED binary fact, already implemented:** the velocity getter
[0x004D5AF0](functions/004D5AF0.md) shifts body velocity down four. State 6
passes it to [0x004D59A0](functions/004D59A0.md), which shifts position and
velocity back up four before integration. `CreatureMovementSystem.Advance`
already truncates velocity this way; `WorldSteering.SyncBody` supplies position.
An additional identical truncation in the local probe leaves the 26-turn result
unchanged. Missing velocity truncation is therefore excluded as a fix here.

Q613's cause is established in
[imp-first-integration.md](imp-first-integration.md#cause-of-q613-movement-order-and-crowd-input).
The integration branch also includes the independently verified movement-order
correction, without compensating arrival delays; see ADR-0512.

## Verification and performance limits

Release build passes without warnings. All 3573 simulation tests pass, including
the new regressions and updated hash pin. The solution suite is not green:

- `LicenseHeaderTests.EverySourceFile_StartsWithTheNotice`: five pre-existing
  presentation/Unity files lack the required header spacing; they are unchanged.
- `LevelVariablesOnRealDataTests.LevelOneOverridesReplacesAddsAndKeeps`,
  `Level17KeeperScenarioTests.Level17HarkanCastsTheCallAtItsFighterAndTakesItDownUnaided`
  and `Level11AKeeperScenarioTests.Level11ADrakoBridgesTheLavaWithStoneThenCallsItsArmyAtAnAttackedEnemyHeart_WithAmbientRats`,
  plus scenario `creatures/a_goblin_eats_a_chicken.yaml`, also fail on rebuilt main.
- Scenario `creatures/a_keeper_drops_a_chicken_in_a_prison.yaml` passed on rebuilt
  main and failed on the scoped candidate. The integration now drops the food
  centrally; the edge case's cause is still open ([imp-fidelity-integration.md](imp-fidelity-integration.md#regression-interpretation)).

The correction has no measured performance result. Its source adds a progress
query on idle walks and two body assignments at arrival; it adds no Heart scan,
allocation or mesh operation. Replay wall time is not a benchmark because
mapping passes and concurrent test loads differ.

Local artifacts, all ignored, are under
`build/imp-acceptance/reverse-engineering/imp-idle-arrival-20261018/`:
`replay.py`, four result JSONs and per-variant replay/event/creature CSVs;
`Probe.csproj`, `Program.cs`, `arrival-stages.json`, `scoped-arrival-stages.json`;
`verify.py`, `occupancy.json` and solution/baseline test logs. No original assets
or decompiler output are tracked.
