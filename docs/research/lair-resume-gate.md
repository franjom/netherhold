---
systems: [creatures, simulation-core]
---
# Stored Lair resume and acquisition

## Binary gate

The [sleep-need continuation](bedless-sleep-need.md) reaches raw state89 on
1425 with Warlock438's stored resume still80. STRONGLY INFERRED from the
primary instructions, independently checked by the reviewer:
[0046F390](functions/0046F390.md) tests state-object+0xDF, or creature+0x32D,
against80 and refuses before the bed/finder checks. The test is unconditional;
it does not require current travel or current Lair activity.

Captured438 is raw89/resume80 at completed1425, entering1426. Hash434's CanAttempt
only checks Activity, so it starts another acquisition instead of falling
through to the job chooser.

## Built scope (ADR-0703)

CanAttempt now refuses stored resume80 independently of current activity.
Other activity and room-finder adapters are unchanged; this is a bounded
gate, not a claim that the entire acquisition helper mirrors native.
Hash/save435 adds no fields and rejects earlier snapshots; its pin is in
[hash-versions.md](hash-versions.md).

## Controlled comparison

Both runs use the unchanged declared1400 inputs of the previous continuation.
No extra restore field, future pose, deadline or seed is supplied here.
The frozen434 baseline is replayed from its archived executable.

| Build | Complete-turn ordered site/preseed prefix | Next1426 site |
|---|---|---|
|434 |347 draws,1401–1425 |draw1: modern Room.cpp:1509, native State.cpp:1700 |
|435 |347 draws,1401–1425 |draw3: modern Room.cpp:1509, native Library.cpp:1068 |

Candidate435 now matches the first two sites and preseeds at1426: the happy
job roll and first shelf search. Fresh primary reads map them through
[004780E0](functions/004780E0.md) and [004E1AC0](functions/004E1AC0.md).
At completed1426, native and candidate438 also agree in state/resume7/53
and XY57469,102967. The whole-turn prefix has not lengthened. The next native site is another
shelf search; the remake's draw is a generic room point during438's research
departure. The native label alone does not identify its actor/caller; that
caller/helper distinction remains to investigate.
No full-state or Heart outcome parity follows from these observations.

Evidence is add-only under
`F:/DK2Evidence/{studies,builds}/q712-lair-resume80--2026-11-06--001`.
The initial paired runs are `baseline434` and `candidate435`.

The [Library capacity diagnostic](library-capacity-draws.md) owns the corrected
RoomWalk site label and subsequent continuation; production435 remains unchanged.

## Verification

The five LairResumeGateTests have three attributable old-code failures and
two positive controls. With the guard, all five pass: saved80 is refused
from1/89/56, while saved1/53 still permit acquisition from89. Selected
sleep/Lair tests pass42 cases with only the expected old-pin failure before
updating the measured pin. Independent review reports no findings. The final
Release solution build has zero warnings/errors; all ten suites pass with8471
passed,30 skipped and zero failed. Unity compiles with zero errors and two
existing obsolete-API warnings. The Goblin meal scenario retains birds17/7
at720 and375 healing.
