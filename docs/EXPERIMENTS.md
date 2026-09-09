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

**2026-09-09, level 1, ours vs run 5 — the first columns we get right.**
Measured by event set over 1176 shared turns, as the calibration above requires;
`trigger-diff`'s per-turn counts are not readable against a played capture.

| after | fire in both | DK2 only | ours only |
|---|---:|---:|---:|
| condition 34, type 14's arming half, the party roots | 0 | 48 | 0 |
| …and the player walker | **2** (`t15`, `f83`) | 46 | **1** (`f1`) |
| …and the action-point walker | **2** | 46 | **1** |

The first row is where the earlier diagnosis was wrong: trigger 89 evaluates and
arms its parties, but its chain ends there, and level 1 keeps its script on its
player root. The second row is the player walker paying that off.

**2026-09-09, level 1, ours vs run 5 — the action-point walker changes nothing
here, and that is the finding.** Adding the fourth walker and five area condition
types leaves the event set at exactly 2 / 46 / 1. Level 1 walks 15 action-point
roots now, and none of them moves a flag in a run with no player.

**The one false positive is the player, not a missing walker.** We set `f1` at
turn 33; DK2 never does. The chain is timer 15 → flag 0 == 0 → flag 2 == 0 →
type 72 → write flag 1, and DK2 closes it by setting flag 0 at turn 10. Flag 0
and flag 2 both hang off **action point 4**, one tile at (14,24), neutral terrain
6 — and both of its conditions are things only a human does:

- triggers 4 and 5, type **62**: how many tiles in that rectangle **player 3 has
  tagged**. Zero until somebody marks the wall; DK2's run at turn 10.
- trigger 14, type **42**: how many tiles there are still **neutral terrain 6**.
  One until the wall is dug out; DK2's run at turn 35.

So this is the hazard the calibration above warns about, in its other form: not a
turn number that only the player set, but an *event* that only the player caused.
It cannot be closed by any walker, and it should not be. The remaining 46 are the
census `trigger-run` prints: unimplemented condition and action types, plus the
creature trees, plus everything downstream of a player who never plays.

**What the owner can settle by playing is not here.** It is
`docs/research/observation-checklist.md`, which is the working list of
single-question experiments; this file is for paired runs of the same scenario.
Q1 is the standing example of the difference: no stopwatch could separate *5
ticks a second at work rate 0.8* from *4 ticks a second at 1.0*, so it was
settled by reading the binary instead — a tick is 250 ms (ADR-0089).
