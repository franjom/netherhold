# Experiments

Controlled experiments comparing our simulation against original DK2 v1.70
(the behavioural oracle). Methodology: `DK2_REIMPLEMENTATION_PLAN.md` §6.

- Scenario definitions live in `/scenarios` (declarative YAML; one scenario
  drives both the original game and our simulation).
- Recorded results live in `docs/experiments/<scenario-id>/` as
  `oracle.json` + `ours.json`.
- Divergences are triaged as: our bug / unknown mechanic / accepted deviation
  (accepted ones need a written justification here).

No experiments recorded yet. First target (milestone M5): measure the
simulation tick rate (open-questions Q1).
