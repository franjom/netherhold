# level1-idle-001 — level 1 with nobody playing

**Status:** protocol written 2026-09-09, **not yet run**. Predictions below are
recorded *before* the capture on purpose; do not edit them afterwards. Record
what happened underneath.

## Why this run exists

Two earlier captures of level 1 (runs 4 and 5) were **played**, and comparing
them to each other showed turn numbers are not reproducible between two plays:
the offset ran from −156 to +285 turns and order was not preserved
(`docs/EXPERIMENTS.md`). So a played capture can be read for *which* events
occur and never for *when*, which throws away most of what a capture is for.

The variance is the human, not the game. This run removes it by removing the
input, not the player: load the level and touch nothing. Both sides then have
zero input, and if the turn numbers line up, **per-turn comparison becomes
readable for the first time** — which is the thing M5 actually needs.

It also settles a live question. We set two flags DK2's played run never set,
`f1` and `f34`. Both hang off a gate testing a flag that action point 4 sets
when the player tags a wall; DK2's run set it at turn 10, ours never does. If
that reading is right, **a DK2 run with no player must set both**.

## Protocol

1. Start DK2 v1.70 and load **campaign level 1**. Let it reach the point where
   the dungeon is on screen and the game is running.
2. Start the probe, from the repository root:
   ```
   python reverse-engineering/dk2watch.py --seconds 300 --csv D:\dk2run6.csv
   ```
   The level must already be loaded — the probe waits for a real level because
   the front end is itself one.
3. **Do nothing for five minutes.** No tagging, no digging, no picking up, no
   dropping, no slapping, no spells, no building, no possession. Moving the
   camera and zooming are fine: they are not game actions.
4. Note anything that visibly happens **in the dungeon** — a creature arriving,
   a room appearing. The Mentor narrating is expected and needs no note; level 1
   is thick with speech actions and they move no flag we read.
5. Copy the CSV to `reverse-engineering/dk2runs/dk2run6.csv` (gitignored).

Five minutes is ~1180 turns at the measured 3.94/s, which matches the 1200-turn
headless runs, so the two windows are comparable without trimming.

### Why idling makes this level's worst feature harmless

Level 1 ships **49 type-72 records**, over a third of the corpus's 137, and type
72 tests four fields strongly inferred to be modal interface states (Q242). We
answer it *true* because a headless run has no interface. A modal up during a
capture would therefore have DK2 answering a third of this level's script the
other way from us — which is what makes the two **played** runs hard to read,
and it was almost the reason to prefer a different level here.

**It does not apply, because a modal cannot appear on its own.** CONFIRMED
(owner, 2026-09-09): one only opens when the player clicks an element. Click
nothing and DK2 spends the whole run in the state we assume, so the assumption
is not merely uncontrolled-but-hoped-for — it is *exactly matched*.

What the tutorial does do unprompted is talk. Level 1 is full of speech actions,
and they set no flag or timer the probe samples, so narration is noise in the
room and not in the data. Distinguishing "the level does things by itself" from
"the level opens modals by itself" is what turns this level from the worst
candidate into a clean one.

## Predictions, recorded before the run

| # | Prediction | What it tests |
|---|---|---|
| 1 | DK2 sets **`f1`** | that `f1` is the absent player and not our defect |
| 2 | DK2 sets **`f34`** | the same, for the flag ADR-0113 made reachable |
| 3 | DK2 sets `t15` and `f83`, as in run 5 | that removing input does not remove the clock-driven part |
| 4 | DK2 sets **little else** — level 1's chain is gated at trigger 15 on flag 2, which only digging sets | that the tutorial is player-advanced, which is why no condition work closes it |
| 5 | The turns agree with our headless run **within a few ticks**, not the ±156–285 of run 4 vs run 5 | that per-turn comparison is usable once input variance is gone |

**Predictions 1 and 2 are the ones that can embarrass us, and they are
conclusive either way.** With no modal possible, the type-72 escape hatch is
shut: if DK2 idle does *not* set `f1` and `f34`, the chain reading in
`docs/EXPERIMENTS.md` is wrong and there is a real defect to find.

Prediction 5 is the one that decides M5's direction. If turns agree, the
comparer can be sharpened to per-turn and level 1 becomes a usable regression
fixture. If they do not, no passive capture will do and the scenario runner
(§6.2) is the only route.

## Our side

```
dotnet run --project src/DK2.Tools -c Release -- trigger-run Level1 1200 ours.csv
dotnet run --project src/DK2.Tools -c Release -- trigger-diff ours.csv reverse-engineering/dk2runs/dk2run6.csv
```

Compare by **event set first** — which ids ever move — and only then by turn,
because reading turns is the thing this run is testing rather than assuming.

## A second level is worth having, but is no longer needed here

The earlier draft wanted a quieter level because tutorial modals looked likely
to spoil prediction 5. They cannot, so level 1 idle carries all five predictions
on its own — four events with turns attached is thin but enough to tell a
few-tick agreement from a ±156–285 disagreement.

A second level is still worth capturing once for breadth, and `dk2tools kwd
oracle` ranks the candidates with **Level20** at the top. That waits on one fact
about this install rather than about DK2: whether a mid-campaign level can be
loaded directly, or the campaign has to be progressed to reach it.

## Results — run 2026-09-09, `dk2run6.csv`, 300 s / 1176 ticks

**Every prediction held, and the event set matches exactly.**

| id | ours | DK2 idle | delta |
|---|---:|---:|---:|
| `t15` | turn 1, value **1** | tick 11, value **7** | see below |
| `f83` | turn 1 | tick 11 | see below |
| `f1` | turn 33 | tick 40 | −7 |
| `f34` | turn 601 | tick 608 | −7 |

**4 fire in both, 0 DK2 only, 0 ours only.** Against the played run 5 the same
comparison was 2 / 46 / 2. Removing the input, not the player, removed the whole
divergence — which settles predictions 1 and 2 in the direction that clears us:
`f1` and `f34` were never our defects.

Prediction 4 also held: DK2 idle moved **two** flags in five minutes. Level 1
does nothing on its own beyond its opening, because it is waiting to be dug.

### Prediction 5 held, and the residual is a constant, not drift

`f1` and `f34` are **both exactly 7 ticks late** in DK2, 568 ticks apart. An
error in our tick rate would grow over that span; a fixed offset does not. So
the rate is right and the **start** is not aligned.

`t15` reads the offset directly rather than by sampling: a timer's value *is* a
tick stamp, and DK2 stored **7** where we store **1**. So DK2 ran trigger 1's
action — the first thing level 1 does — at tick 7, and we run it on turn 1. The
7 in the sampled flags is that same 6-tick gap plus a tick of sampling latency.

`t15` and `f83` show as tick 11 only because that is the first sample: the probe
was started after the level, and both were already set. Their true turns are ≤ 11
and the timer's stored 7 is the real answer for `t15`.

**Followed up the same day, and the six is not a rule.** Timer 15's stamp across
all six captures of this level is **4, 6, 7, 8, 10 and 11** — the origin varies
with the load, so there is no startup delay to reproduce and our turn 1 is not
mis-numbered (Q272). What a comparison must do instead is **align on the first
evaluation**, which a timer's stored stamp gives exactly.

Aligned that way the residual is **+1 tick**, on both flags, 568 apart. It is not
the arithmetic — DK2's timer arm computes `now` against `stamp + threshold` with
`SETGE` (`0x00518537`), which is ours — and it is not the comparison codes, whose
table was re-read to check (see below). The open possibility is the probe: a
flag's tick is *sampled* while a stamp is *stored*, and the probe reads the banks
at an arbitrary point within a tick. Q273.

### A trap worth naming: the comparison arms are not in table order

Chasing the +1 turned up an explanation that would have fitted perfectly —
comparison code 5 being `>` rather than `>=` would put us exactly one tick early.
The six arms do sit in memory as `< <= == >= > !=`, so reading them in address
order swaps `>` and `>=`. **The jump table at `0x00519A04` is not in address
order**: 4 goes to `SETG` and 5 to `SETGE`, which is what we already had. Reading
the table rather than the layout is the only reason a correct mapping was not
"corrected" into a wrong one.

### Protocol notes

Run as written except that the level was loaded before the probe, costing the
first 11 ticks, and notifications were closed as they arrived. Neither hurt: the
missed ticks only floor `t15`/`f83`, whose stamp is readable anyway, and clicking
a notification away did not stop DK2 firing both flags. Nothing was dug.

The capture also re-confirmed Q233 live: all 22 of level 1's type-2 thresholds
are stored as exactly 4x their authored seconds. And the tick rate measured
**3.918/s** over 300.18 s, against the 3.94 of the earlier runs.
