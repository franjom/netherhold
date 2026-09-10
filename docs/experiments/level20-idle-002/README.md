# level20-idle-002 — the game's own record dates the probe's lag

**Status:** run 2026-09-10 (owner), 11 minutes.
Capture `reverse-engineering/dk2runs/dk2run8.csv`, 2634 rows, ticks 33–2619
over 660.24 s (3.917 ticks/s). **Q273 is answered.**

## Why this run exists

`level20-idle-001` showed our tick numbering carries no offset, but every event
inside its window was silence, so it could not say what produced the level-1
residual: DK2's flags landed one tick after ours, identically, 568 ticks apart.
Two explanations survived — our gate fires a tick early, or the probe reports a
tick late.

This run was designed to contain **one stored event and one sampled event on
the same clock**, which is what separates them:

- **t1 restamps** at 1724 (ours). A timer slot holds *the tick the game recorded
  when it acted*, so reading it late cannot move it.
- **f6 sets** at 2404 (ours). A boolean, visible only when the probe looks.

Both turns were pinned in a test named `…_NotYetCaptured` before the run.

## What happened

```
  429.44s 1725       timer1 4->1724
  603.35s 2405       flag6 0->1
```

Read the first line carefully, because the whole answer is in it. The probe
**noticed** at tick 1725. The value the game **stored** is **1724** — and 1724
is our number, exactly.

| | the game's own record | the probe's label | ours |
|---|---|---|---|
| t1 restamp | **1724** | 1725 | **1724** |
| f6 set | (not stored) | 2405 | 2404 |

So one event yields both readings at once, and they differ by exactly the
residual. **The probe is one tick late; our gate is not one tick early.**

Sampling around both events was 1:1 with ticks — 1722, 1723, 1724, 1725, 1726
and 2402…2406 all present — so neither observation sits over a gap. At the
sample labelled 1724 the slot still read 4.

### Why one tick, mechanically

A sample labelled *N* shows the state **before tick *N*'s trigger pass**, so it
reports what tick *N−1* left behind: capture row *N* ≡ our row *N−1*. Whether
that is the probe reading the banks before the game's pass runs, or the tick
counter it reads sitting a phase ahead of the one the pass uses, is **not
separated here** and does not need to be — both say the game's recorded tick is
1724 and the label is one high.

### Applying the measured lag

It was measured on t1, then checked against three events it was not fitted to:

| event | probe said | −1 | ours |
|---|---|---|---|
| Level 20 `f6` | 2405 | 2404 | 2404 |
| Level 1 `f1` | 40 | 39 | 39 |
| Level 1 `f34` | 608 | 607 | 607 |

**Every trigger event captured from DK2 on two levels now matches ours
exactly.** Q273 closes with no residual left in the simulation.

## The run was not idle, and that is a finding

The owner gave no input, but **heroes invaded**: a wall was destroyed, several
imps were killed and the dungeon heart was attacked. No trigger fired for any
of it — the capture's only two changes are the two predicted above.

That is consistent and useful rather than alarming:

- The two compared events are **pure timer gates**, reading no world state, so
  a diverging world cannot reach them. Condition 12 is a type-2 timer whose
  children are flag tests; condition 16 is a type-2 timer with a type-7 action.
- The attack is therefore **not trigger-driven**. Level 20 places 52 hero
  creatures carrying `Objective` 22 with `ObjectiveTargetPlayerId = 3`, plus 83
  at 19, 8 at 23 and 7 at 13 — they act on their own (**Q275**).
- **Level 20 stops being a quiet level after roughly seven minutes.** Any
  future capture wanting a still world must stay inside that.

What this run does **not** test is the invasion itself: our headless run has no
creature AI, so the two worlds diverged enormously in state while agreeing
completely on triggers. The trigger result stands; nothing here is evidence
about combat, pathing or hero behaviour.

## Tooling this exposed

`capture-diff` could not see the restamp at all — it recorded only each
column's *first* non-zero value, so the single exact measurement in the run was
invisible and t1 showed as a censored ceiling event. It now compares each
timer's **sequence of stored stamps** (ADR-0119), which is the only part of a
live capture with no sampling in the path:

```
timer stamps, stored and origin-relative (no sampling in the path):
  id       ours                 capture
  t0     0                    0                    same
  t1     0, 1720              0, 1720              same
verdict: event set matched; stamps agree exactly; largest absolute sampled residual 1 tick
```

Truncating those series to the shorter run's window was needed for the same
reason as ADR-0118: without it, run 7 (which stopped at 1218) reported
`STAMPS DIFFER` against our restamp at 1724 — the identical mistake in a second
place, found only because the fix to the first was re-run against old data.
