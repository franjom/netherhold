---
systems: [imps-tile-work]
---
# OpenKeeper PR 1021 comparison

Reviewed 2026-11-07 at head `5367c0bfdf7c653369a30f209b1df84b2cf0c869`.
[PR 1021](https://github.com/tonihele/OpenKeeper/pull/1021) includes
[PR 933](https://github.com/tonihele/OpenKeeper/pull/933); animation cadence,
work arithmetic and room conversion belong to that prerequisite.
No source copied. The PR author's native measurements were read, not rerun.

## Verdict

The added scheduling delay, ordinary four-search cap and attack-to-claim
transition agree with the existing native readings. Fresh Ghidra reads of
[state 10](functions/00463DF0.md), [state 20](functions/00465F20.md) and
[state 16](functions/00465CD0.md) preserve those readings (STRONGLY INFERRED).
Existing evidence: [claim path](imp-claim-path.md) and
[idle behavior](imp-idle-and-wandering.md). These comparisons do not establish
the PR's aggregate timings or complete parity in either engine.

## Differences to follow up

- Our `ImpTaskSystem.Work` already defers selection after work ends, caps
  ordinary searches at four per player per tick and exempts just-dropped imps.
  Its throttle enters state 12 with the native random deadline. OpenKeeper
  caps an idle queue and uses its existing idle/wander behavior; the dropped
  exception is explicitly deferred by the PR author.
- OpenKeeper retains batch job scoring. Our selector runs per creature,
  with native kind ordering and aimed/last-kind retries; see
  [task priority](imp-task-priority.md). A four-entry batch cap does not make
  those assignment algorithms equivalent.
- Our application and completion timing gaps are established by the
  [native/modern action-clock controls](imp-claim-action-clock.md). That study
  owns the measured differences and the retained-phase implementation boundary.
- Our reinforcement has an independent action/animation runner; see
  [reinforcement clock](reinforcement-action-clock.md). The PR's remaining
  reinforcement timing error is not evidence that ours has the same error.
- The inherited OpenKeeper room damage path distributes damage over tiles
  and captures on the first depleted tile. This is not equivalent to our
  independent room-health pool when tile health differs; see
  [room health](room-health.md). This is a pre-existing limitation, not a
  new defect introduced by PR 1021's scheduling commits.

Verification: reviewed upstream patch/head source, local implementation and
existing tests; reread the three native handlers above. Original-function
controls are documented in the action-clock study above. No test suite or native
gameplay capture was run, and no simulation behavior changed.
