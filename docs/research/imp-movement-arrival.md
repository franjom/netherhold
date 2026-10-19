# Combined movement order and idle arrival

2026-10-18: `fixes/imp-movement-arrival`, ADR-0508, measured hash/save 272.
The hash/save-280 integration branch includes two corrections under ADR-0512:
the newest-first advancing pass from
[imp-first-integration.md](imp-first-integration.md#movement-order-implementation-and-acceptance)
and the scoped progress/zero-velocity idle arrival from
[imp-idle-arrival.md](imp-idle-arrival.md). Shared stopping, steering axes,
task selection and Heart policy were unchanged in the measured composition.
Hash 272 composes those two fixes; it is not a third correction. The turn-22
RNG override below remains diagnostic and is absent from integration.

## Native startup comparison

**CONFIRMED observation:** the compiled candidate, without diagnostic order or
axis overrides, matches all four original Level4 workers' entity positions,
full velocities, headings and states over aligned turns 4–21: 72 observations.
The comparison contains 92 observations through turn 26. Ten differ, beginning
at turn 22: workers 3 and 4 enter state 87 while native remains in state 12.
Their positions, headings and zero velocities still agree on that turn; movement
diverges on turn 24. The independent axis discrepancy remains unchanged.

This startup trace does not establish complete worker or campaign equivalence.

## Cause of the turn-22 state difference

**STRONGLY INFERRED (2026-10-18): different idle random decisions.** The handler branches and the controlled computation below are confirmed; the native roll itself is inferred from the action chosen. Re-read
[state 12](functions/00464240.md) in full and checked its instructions.
Before this turn, workers 3/4 are in state 12, have no action running, have
nonzero wander stamps and deadlines still in the future in both engines.
The stamps and deadlines differ numerically; their branch conditions agree.
Native clears their stamps and starts action 44 at native epoch 22. This arm
requires a roll in 75–99 and an available action. Exact native rolls are not
captured. The modern RNG wrapper records two rolls of 20, so `Rest` in
`ImpTaskSystem.Wander.cs` correctly chooses state 87 for those inputs.
This boundary is not evidence of a different work priority or missing task.

**CONFIRMED controlled computation:** execute the original handler with turn,
random result, availability and transition/action setters controlled. With
otherwise identical gates, roll 20 requests state 87; 25/74 start action 43;
75/99 start action 44. The original action-running predicate executes unchanged.
This is a branch experiment, not a restored native world or action-duration test.

In the compiled candidate, override only those two turn-22 idle results from
20 to 75 while still consuming the original draws. All four workers' positions,
full velocities, headings and states then match all 92 native observations over
turns 4–26. The wrapper's uncontrolled run reproduces the prior candidate trace
exactly. This establishes the immediate cause, not RNG-stream equivalence:
default modern xoshiro and native LCG do not supply identical decisions.
Further draws can differ because different branches consume different ranges.

Do not fix this sample by forcing rolls, changing priorities or delaying an Imp.
For subsequent behavior comparisons, control the decision input or compare
conditional outcomes; equal seed labels alone do not establish equal inputs.
The independently proven axis discrepancy was later corrected in hash 274;
the campaign losses remain unresolved.

Ignored evidence under `reverse-engineering/imp-rest-choice-20261018/` in this
worktree: `export.py`, `Program.cs`, native/modern/controlled input JSONs,
modern/controlled draw JSONs, `idle_oracle.py`, `idle-oracle.txt` and `verify.py`.
The verifier checks both branch gates, actual/controlled draws, all 92 controlled
observations and the five original handler results. No gameplay code changed
during this investigation; no campaign replay or broad suite was repeated.

## Replay acceptance

**CONFIRMED outcome:** the same Level4 packet, xoshiro seed 1, 6804 consecutive
turns and 707 actions on 647 turns produces no win and 39 keeper Imp deaths.
Three mapping passes produce the same replay SHA256:
`1c1bcdcfe9c5aca316fdb93fcc81d19884c1ece144a90f8f939e8b156def8528`.
Stop after these three unchanged outcomes. The entity map has not stabilized;
the final reconstruction leaves ten unresolved mappings. Do not describe this
as a stable replay comparison. Different mappings and unmatched original RNG
limit attribution of campaign losses to either correction.

The reused hash-269 baseline wins on turn 6732 with three Imp deaths; its
provenance is [imp-heart-replay.md](imp-heart-replay.md). The individual
candidates' measurements remain in the documents linked above. This composition
also fails the sampled campaign outcome. ADR-0512 retains that diagnostic result
while integrating the two native-backed corrections. Do not tune invented
timing to recover the sampled win.

## Verification and limits

Release build: zero warnings/errors. All 3575 simulation tests pass, including
the two creation-order cases and three idle-arrival cases. The full solution
suite fails on the known baseline license-header, LevelVariables and goblin
eating cases, plus the prison-chicken scenario that passes on main. No scenario
expectation was changed. No dedicated performance benchmark was run; the
composition adds no sorting, allocation or Heart scan.

Ignored artifacts: `build/imp-acceptance/reverse-engineering/imp-movement-arrival-20261018/`
contains `replay.py`, `candidate/`, `native.json`, `modern.json`, `build-info.json`,
`Probe.csproj`, `Program.cs`, `verify.py`, `replay-observations.json` (captured
stdout), `build.log` and `solution-tests.log`.
The verifier checks the compiled version, 72-observation prefix, first state
differences, three identical replay outcomes, unresolved mapping and test failures.
