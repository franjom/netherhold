---
systems: [creatures, simulation-core]
---
# Bedless acquisition and the sleep need

## The turn1425 defect

The [counted idle-clock continuation](idle-action-clock.md) ends at1425 with
Warlock438 leaving for a Lair before native does. CONFIRMED from the capture:
it has no bed and no sleep debt at1400 and1424–1426. At1424 both runs have
state1/resume80 and position58057,102761; both creatures are at full health.
The hash433 sleep system nevertheless starts a Lair search from healthy,
untired idle, before state1's handler can request89.

STRONGLY INFERRED from fresh whole primary reads:

- [004773D0](functions/004773D0.md) sends an ordinary keeper creature from1
  to89 and returns. The next handler is a later dispatcher turn.
- [004778E0](functions/004778E0.md) makes the bedless acquisition attempt
  in89, before the dropped job, remembered job and happy chooser.
- [00477C20](functions/00477C20.md) asks for sleep with debt, through its
  category/busy gates and four-turn retry; its wound arm has no retry wait.
- [0046F440](functions/0046F440.md) routes a bedded creature only with
  sleep debt. A bedless call instead invokes
  [0046F390](functions/0046F390.md), whether the call came from debt or wounds.

This is a misplaced autonomous decision, not a missing bed relationship in
this actor's1400 input. No future deadline or pose is needed to demonstrate it.

## Built boundary (ADR-0702)

CreatureSleepSystem now requires a sleep need for bedless creatures too.
Tired attempts share NextSleepTryTick and spend their four-turn retry even
when busy; a wound can bypass that retry. Ordinary healthy acquisition stays
with the idle/job chooser. A tired or wounded eligible researcher can still
seek a Lair; a bedded wound without debt does not start sleeping.

Hash/save434 changes behavior without adding state fields. Older snapshots
are rejected. The [hash pin](hash-versions.md) is a remake regression value,
not native evidence. Other dispatcher/needs ordering and Lair-finder
approximations retain their existing scope.

## Controlled continuation

The two valid paired runs use the preceding declared1400 inputs, plus the
captured initial clocks of neutral447/449. That extra restore replaces their
legacy projected deadlines with their actual action identity, requested art,
phase, rate, flags, count and handler completion. It uses only snapshot1400.
The candidate's changed prehistory exposed an un-restored action43 marker on
449; leaving it in place caused an earlier1411 divergence. The same clock
input is applied to both baseline and candidate.

| Build | Ordered site/preseed match | First different ordered site |
|---|---|---|
| Baseline433 with captured clocks |332 draws,1401–1424 |1425 draw1 |
| Candidate434 with captured clocks |347 draws,1401–1425 |1426 draw1 |

Both438 state/resume/XY match at1425:89/80,58057,102761. At1426 the remake's
89 starts another Lair search (`Room.cpp:1509`), while native starts the happy
chooser roll (`State.cpp:1700`) and ends travelling toward research. The next
supported defect is [0046F390](functions/0046F390.md)'s unconditional refusal
when stored resume is80, even in raw89. Captured438 retains it at completed1425,
entering1426; hash434's Activity-only guard lets it through. STRONGLY INFERRED from primary instructions,
independently re-read by the reviewer. This gate was unimplemented in434;
[ADR-0703/hash435](lair-resume-gate.md) builds it and owns the next boundary.
No full-state or Heart outcome equivalence follows from the bounded draw match.

Evidence is add-only under
`F:/DK2Evidence/{studies,builds}/q712-lair-idle-boundary--2026-11-06--001`.
The paired runs are `baseline433-clock-restored` and
`candidate434-clock-restored`. `baseline433` failed in diagnostic clock JSON
serialization and is excluded. `candidate434` lacks the new initial-clock
restore and is not an extended parity control.

For corrected historical site labels, see the [mapper correction](library-capacity-draws.md#diagnostic-control-production435-unchanged).

## Verification

All five new BedlessSleepNeedTests fail against433 and pass with the change.
They cover the1→89 handover and deferred acquisition, no healthy sleep-need
draw, the shared tired retry, busy retry spending, and wounded bedless research.
The geometry/arrival unit fixtures explicitly request the public Lair service;
the real idle/job scheduling regression uses the production systems.
The selected sleep/Lair/determinism run passes58 cases and the cross-fixture run125.
Independent review reports no findings. The Release solution build has zero warnings/errors;
all ten suites pass with8466 passed,30 skipped and zero failed. Unity compiles with
zero errors and two existing obsolete-API warnings. The Goblin meal scenario passes
with incidental birds17/7 at720 and375 healing; timing and healing assertions are retained.
