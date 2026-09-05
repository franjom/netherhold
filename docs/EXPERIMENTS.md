# Experiments

Controlled experiments comparing our simulation against original DK2 v1.70
(the behavioural oracle). Methodology: `DK2_REIMPLEMENTATION_PLAN.md` §6.

- Scenario definitions live in `/scenarios` (declarative YAML; one scenario
  drives both the original game and our simulation).
- Recorded results live in `docs/experiments/<scenario-id>/` as
  `oracle.json` + `ours.json`.
- Divergences are triaged as: our bug / unknown mechanic / accepted deviation
  (accepted ones need a written justification here).

No experiments recorded yet, and **Q1 can no longer be settled by measuring**
(2026-09-05): a stopwatch cannot separate *5 ticks a second at work rate 0.8*
from *4 ticks a second at 1.0*, because they are the same wall-clock speed. It
is a reading question now, not a timing one — see Q1 for where in the binary.

**What the owner can settle by playing is collected in
`docs/research/observation-checklist.md`**, which is the working list; this file
stays for recorded oracle-vs-ours runs, of which there are none.
