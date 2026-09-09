# Experiments

Controlled experiments comparing our simulation against original DK2 v1.70
(the behavioural oracle). Methodology: `DK2_REIMPLEMENTATION_PLAN.md` §6.

- Scenario definitions live in `/scenarios` (declarative YAML; one scenario
  drives both the original game and our simulation).
- Recorded results live in `docs/experiments/<scenario-id>/` as
  `oracle.json` + `ours.json`.
- Divergences are triaged as: our bug / unknown mechanic / accepted deviation
  (accepted ones need a written justification here).

## Recorded runs

Captures live in `reverse-engineering/dk2runs/` (**gitignored**, so they are not
backed up) as `dk2run<N>.csv`, in the shape `TriggerTurnCsvEmitter` writes. Our
side comes from `dk2tools trigger-run <level> <turns> <out>`; compare with
`dk2tools trigger-diff <a> <b>`, which pairs rows by **turn**, never by position.

**2026-09-08, level 1, ours vs run 5.** Over 1176 shared turns, 100 of 148
columns identical and 48 divergent — **every divergence one-directional**, DK2
setting what we never set. We invent nothing. Cause: level 1's root is trigger
89, condition type 34, `next = 0`, so the one unimplemented node blocks the whole
tree (`docs/research/trigger-semantics.md`).

**2026-09-09, level 1, run 4 vs run 5 — what the oracle can and cannot settle.**
Two sessions of the same level, both played. **The event set is reproducible**:
52 flags and timers fire, the same 52 in both, none unique to run 5, and the four
unique to run 4 fired past run 5's window. **Turn numbers are not**: the offset
runs from −156 to +285 turns, median 16, and **order is not preserved** either
(f57 precedes f71 in one run, follows it in the other). Events that share a
causal chain move together — t3, t11 and t14 are all exactly +85.

So a divergence report may be read for *which* events occur, and for groups that
move as one. **It may not be read for turn numbers**, and a future comparison
that scores cell-by-cell equality against a played capture will manufacture
divergences that are only the player.

**2026-09-09, level 1, ours vs run 5, after condition 34 and the party roots.**
The event set is **unchanged**: 48 columns fire in DK2, 0 in ours, 0 in ours
alone. Not a regression and not a null result — it is where the diagnosis above
was wrong. Trigger 89 now evaluates and both its type-14 children arm their
parties, but its chain ends there: level 1 keeps its script on its **player**
root, trigger 1, and no walker we have visits it. The world tree of this level
is two party spawns and one party root (trigger 213, a type-64 condition, the
single blocked record left). **Nothing measurable will move on level 1 until the
player walker exists**, so the next comparison to run against these captures is
not another world-tree change.

Method note: judge these by event set. The per-column report from
`trigger-diff` now shows large per-turn difference counts wherever we and DK2
both write a column, and by the calibration above most of that is the player,
not the rule.

**What the owner can settle by playing is not here.** It is
`docs/research/observation-checklist.md`, which is the working list of
single-question experiments; this file is for paired runs of the same scenario.
Q1 is the standing example of the difference: no stopwatch could separate *5
ticks a second at work rate 0.8* from *4 ticks a second at 1.0*, so it was
settled by reading the binary instead — a tick is 250 ms (ADR-0089).
