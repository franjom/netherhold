# Experiments

Controlled experiments comparing our simulation against original DK2 v1.70
(the behavioural oracle). Methodology: `DK2_REIMPLEMENTATION_PLAN.md` §6.

- Scenario definitions live in `/scenarios` (declarative YAML; one scenario
  drives both the original game and our simulation).
- Recorded results live in `docs/experiments/<scenario-id>/` as
  `oracle.json` + `ours.json`.
- Divergences are triaged as: our bug / unknown mechanic / accepted deviation
  (accepted ones need a written justification here).

**No oracle-vs-ours run has been recorded**, which is why M5 is not started.
`docs/experiments/` is empty; `find scenarios -name '*.yaml' | wc -l` is the
number of scenarios available to drive one.

**What the owner can settle by playing is not here.** It is
`docs/research/observation-checklist.md`, which is the working list of
single-question experiments; this file is for paired runs of the same scenario.
Q1 is the standing example of the difference: no stopwatch could separate *5
ticks a second at work rate 0.8* from *4 ticks a second at 1.0*, so it was
settled by reading the binary instead — a tick is 250 ms (ADR-0089).
