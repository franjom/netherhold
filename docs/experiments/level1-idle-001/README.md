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
4. Note anything the game does **on its own**, with a rough time:
   - any message, objective box or tutorial prompt that appears;
   - whether you dismissed it, and when;
   - anything that visibly happens in the dungeon.
5. Copy the CSV to `reverse-engineering/dk2runs/dk2run6.csv` (gitignored).

Five minutes is ~1180 turns at the measured 3.94/s, which matches the 1200-turn
headless runs, so the two windows are comparable without trimming.

### The one thing that could spoil it

**A modal box is not nothing.** Condition type 72 tests four interface fields
that are strongly inferred to be modal interface states (Q242), and we answer it
*true* because a headless run has none. If level 1 pops an objective box and it
sits there, DK2 answers those conditions differently from us for as long as it
is up — which is a real difference, not a defect, but it has to be recorded to
be told apart from one. Hence step 4.

## Predictions, recorded before the run

| # | Prediction | What it tests |
|---|---|---|
| 1 | DK2 sets **`f1`** | that `f1` is the absent player and not our defect |
| 2 | DK2 sets **`f34`** | the same, for the flag ADR-0113 made reachable |
| 3 | DK2 sets `t15` and `f83`, as in run 5 | that removing input does not remove the clock-driven part |
| 4 | DK2 sets **little else** — level 1's chain is gated at trigger 15 on flag 2, which only digging sets | that the tutorial is player-advanced, which is why no condition work closes it |
| 5 | The turns agree with our headless run **within a few ticks**, not the ±156–285 of run 4 vs run 5 | that per-turn comparison is usable once input variance is gone |

**Predictions 1 and 2 are the ones that can embarrass us.** If DK2 idle does
*not* set `f1` and `f34`, then the chain reading in `docs/EXPERIMENTS.md` is
wrong and we have a real defect to find.

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

## Results

*Not yet run.*
