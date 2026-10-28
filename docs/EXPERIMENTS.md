# Experiments

Controlled experiments comparing our simulation against original DK2 v1.70
(the behavioural oracle). Methodology: `DK2_REIMPLEMENTATION_PLAN.md` §6.

- Scenario definitions live in `/scenarios` (declarative YAML; one scenario
  drives both the original game and our simulation).
- Recorded runs live in `docs/experiments/<run-id>/README.md` (one write-up per
  capture; the capture CSVs are not committed, see "Recorded runs"):
  `level1-idle-001`, `level20-idle-001`, `level20-idle-002`, and
  `2026-10-06-packet-replay-seeds/`, the Level 1-3 packet replays' seed spread
  against DK2's seeded playbacks, and `2026-10-12-level4-packet-replay/`, Level 4's, and the Level 1-4 packet
  sweeps `2026-10-24-packet-replay-hash302/`, `2026-10-25-packet-replay-hash303/`
  and `2026-10-25-packet-replay-hash305/` (the latest committed, pinned to `ae14f3b3`; the hash 309 and 320 sweeps are written up under `D:/DK2Evidence/runs/` and summarised in `docs/HANDOFF.md`). `doors-and-traps.md` lists the owner
  observations still to run. Two idle captures of DK2's own keepers are written
  up in `docs/research/computer-keeper-observations.md`.
- Divergences are triaged as: our bug / unknown mechanic / accepted deviation
  (accepted ones need a written justification here).

## Recorded runs

Captures live in `reverse-engineering/dk2runs/` (**gitignored**, so they are not
backed up) as `dk2run<N>.csv`, in the shape `TriggerTurnCsvEmitter` writes. Our
side comes from `dk2tools trigger-run <level> <turns> <out>`. Compare a live
capture with `dk2tools capture-diff <ours> <capture>`, which checks the event set
and aligns timing on a stored timer stamp. `trigger-diff` remains the exact,
turn-paired comparison for two of our own runs (ADR-0117).

### Level 1 played and idle, 2026-09-08 to 2026-09-09

**A played capture may be read for which events occur and for groups that move
as one, never for turn numbers.** Two played runs of level 1 fire the same 52
flags and timers, yet their turn offsets run from -156 to +285 (median 16) and
the order is not preserved (f57 precedes f71 in one, follows it in the other).
Events on one causal chain move together (t3, t11 and t14 are all exactly +85).

Ours against a played run (run 5), by event set over 1176 shared turns, ended at
2 in both, 46 DK2 only, 2 ours only. **All of it was the absent player.** The two
flags we set and DK2 did not (`f1`, `f34`) hang off action point 4, whose
conditions test tiles player 3 has tagged (type 62) and neutral terrain 6 still
standing (type 42); only a human changes either. Level 1's script advances when
the human does the taught action, so no walker closes it.

**Level 1 idle closed it.** Removing the *input* rather than the player (the
owner loaded level 1 and touched nothing for five minutes,
`docs/experiments/level1-idle-001`, predictions recorded before the run): **4 in
both, 0 DK2 only, 0 ours only**. We invent nothing there. After stamp alignment
the turn numbers agree: `f1` and `f34` sit exactly 7 ticks late in DK2 while
being 568 ticks apart, because timer 15 reads the load origin (DK2 stored 7, we
store 1). Six captures put that first stamp at ticks 4, 6, 7, 8, 10 and 11, so
absolute ticks are not comparable (Q272), and `capture-diff` removes the origin.

### Level 20 idle, 2026-09-10

**The first window where nothing differs at all.**
`docs/research/campaign-progress-registry.md` made Level 20 reachable. Its idle
run stamps two timers early and then does nothing, and a stamp is *stored*, so
unlike a flag it cannot be blurred by when the probe looked. Over ticks 36-1218
all 150 columns are identical and both sides stamp timers 0 and 1 at tick 4
(`docs/experiments/level20-idle-001`). No event fired inside the window, so it
rules out a systematic tick offset and settles nothing else. `capture-diff` now
reports an event the other side stopped before as untested, not divergent.

**Eleven minutes, Q273 answered by the game itself**
(`docs/experiments/level20-idle-002`). Timer 1 restamps inside the window: DK2
stored **1724**, we store 1724, and the probe first *saw* it at **1725**. The
probe's sample labelled N shows state before tick N's trigger pass, so capture
row N is our row N-1. Applied to three events it was not fitted to (Level 20's
`f6`, level 1's `f1` and `f34`), every trigger event captured from DK2 across two
levels matches ours exactly. `capture-diff` compares each timer's stored-stamp
sequence, the only unsampled part of a capture (ADR-0119). The run was not idle
in the game's sense: heroes invaded and attacked the heart with no trigger
firing, so Level 20 is a still world for only about seven minutes (Q275).

### Which level to capture, and what idle captures can reach

`dk2tools kwd oracle` ranks every level by what a headless run moves past turn 1,
and `dk2tools kwd unreached` accounts for every shipped trigger record from a real
run. Two findings stand. Reach is bound by conditions and by the player, not by
action types: DK2 retires an action that succeeds, but a walk descends only
through `ChildTriggerId` and every shipped action record has zero there, so
unblocking actions moved nothing (ADR-0107). And an idle capture can exercise
only about a third of the script; the largest identifiable block waits on player
3, the human keeper, to dig, claim and build (ADR-0125, ADR-0126). What M5 needed
next was an input channel, not more mechanisms. Re-run the two commands rather
than trusting any figure written here.

What the owner can settle by playing is not here. It is
`docs/research/observation-checklist.md`, the working list of single-question
experiments; this file is for paired runs of the same scenario. Q1 is the
standing example of the difference: no stopwatch could separate *5 ticks a second
at work rate 0.8* from *4 at 1.0*, so it was settled by reading the binary
instead — a tick is 250 ms (ADR-0089).

### Level 1 played with recorded input, runs 10-15, 2026-09-10

**The input channel works.** `dk2watch.py --tags` records DK2's player task pool,
so a marked tile becomes a `tag` or `untag` at a coordinate and a tick
(ADR-0127, `player-task-pool.md`). Replaying a capture's own commands took
DK2-only events from 28 to 7 in run 10, and from 3 to 0 in runs 13 and 14 (two
captures of the same three tiles, 20 Hz, 60 s), with nothing of ours invented
(ADR-0131). The extracted commands matched the marks exactly.

**The Worker count does not tell a cancelled dig from a finished one**; the claim
does. A dug tile is claimed (pool kind 3) and a cancelled one stays rock. Against
the labelled run 11 that rule keeps both cancellations and rejects all nine
completions (Q293).

**Run 15 read the tile's own health 20 times a second**, which settled the work
rates. Each is one tile, tick by tick:

| tile | trace |
|---|---|
| claim (10,28) | `1 -> 165 -> 329 -> 493 -> 657 ->` Claimed Path: **+164, five times** |
| reinforce (14,33) | `165 -> 174 -> ... -> 651 ->` Reinforced Wall: **+9, fifty-five times** |
| mine (14,24) | `660 -> 600 -> ... -> 60 ->` Dirt Path: **-60 a worker** |
| dig (14,32) | `900 -> 874 -> ...`: **-26 a worker**, in steps of 26 and 52 |

One mechanism gives all four: the tabled delta times Efficiency in 4096ths,
arithmetic-shifted down 14 (ADR-0133, Q292). Nothing is carried between swings
(54 consecutive reinforce steps of exactly 9 against a tabled 9.6), so
`TileState.WorkRemainder` was deleted. The pool task brackets the work with an
approach: a claim task enters the pool about 5 ticks before the health first moves
and leaves 2 after (Q296). Reinforcing is 55 applications over 56 ticks, which at
this run's 254 ms tick is 14.2 s against the owner's 13.1-13.8 s stopwatch (Q62).
Digging a Reinforced Wall ends at Dirt Path, and the intermediate Rock survives to
a sample only by queue position (Q297, `runtime-map.md`). The offsets are
confirmed live: `map = world + 0x9DA`, tiles `+0x14`, health `word +0x06`.

**A game second costs 1.02-1.05 wall seconds.** The probe times the game's own
turn counter against the wall clock: 3.80-3.94 ticks/s over four clean runs
(254-263 ms a tick). Four ticks per game second is not in doubt (variable 19 reads
4 and the trigger timers agree exactly); the turn is frame-paced. So a stopwatch
reading is biased 2-5% long against a tick count converted at 250 ms. A run where
the owner alt-tabbed fell to 0.7 ticks/s and is excluded.

**Run 15 against our replay, tile for tile** (`dk2tools replay ... --tiles`
writes the same shape the probe does):

| tile | marked | first swing, DK2 | first swing, ours | dug, DK2 | dug, ours |
|---|---|---|---|---|---|
| (10,28) | 21 | **26** | **26** | **44** | **45** |
| (14,24) | 25 | 64 | 55 | 87 | 67 |
| (14,32) | 31 | 63 | 46 | 90 | 87 |

The first tile is exact, so the work model is not the problem. The divergence is
redeployment: DK2 finishes (10,28) at 44 and lands its next swings at 63 and 64,
the eight-tile walk around the dungeon heart at the Imp's 1.84 tiles a second,
where ours sends a spare worker at once. Counting workers per tile (a tick's
health change over the per-worker delta):

| | 1 worker | 2 | 3 | 4 |
|---|---|---|---|---|
| DK2 | 34 ticks | 32 | 51 | **never** |
| ours | 41 ticks | 29 | 15 | **26 ticks** |

DK2 never puts a fourth imp on anything in 117 working ticks although it had four
(the owner confirmed the count), so its fourth imp had no task while two marked
tiles waited. What an imp does when the job it wants is full is Q299, answered
and built (ADR-0289, `imp-task-stealing.md`); imp scheduling has changed since
(ADR-0280, 0282, 0288, 0289, 0296, 0297), and this capture has not been re-scored
against it.

### Later work

The Level 1-4 packet replays over our seeds against DK2's own runs, the
two same-map keeper captures and the pending door and trap observations are
written up where the header says; their numbers live there and are not repeated
here.
