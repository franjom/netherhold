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
side comes from `dk2tools trigger-run <level> <turns> <out>`. Compare a live
capture with `dk2tools capture-diff <ours> <capture>`, which checks the event set
and aligns timing on a stored timer stamp. `trigger-diff` remains the exact,
turn-paired comparison for two of our own runs (ADR-0117).

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

**2026-09-09, level 1, ours vs run 5 — after the creature counter (ADR-0113),
a second false positive.** Re-measured by event set over the same 1176 turns:
**2 fire in both, 46 DK2 only, 2 ours only**. Only the last number moved.
Answering condition type 19 let the walk reach deeper, and `f34` joined `f1`.

**It is not the creature count.** `f34`'s chain is trigger 328 — a **type 72**
condition, `next = 0` — then action 65456, then the write. Type 19 introduced no
fault; it made an existing gate reachable, which is what the `Assumed` census
exists to surface. Deepening the walk is expected to cost false positives before
it pays them back, and this is the first instance.

**Corrected the same day: it is the same cause as `f1`.** The first reading
stopped at the type-72 gate and called the cause unseparated. Reading one level
further up settles it — trigger 328's parent is trigger **127**, a type 1 testing
**`flag 0 == 0`**, and flag 0 is the one action point 4 sets when the player tags
a wall. DK2's run set it at turn 10; ours never does, so the gate stays open and
we walk into the write. Type 72 is on the path but decides nothing here.

So **both false positives are the absent player**, and neither is evidence
against the Q242 assumption. Reaching for the nearest unknown — the assumption we
already knew was there — instead of walking up the chain is what produced the
wrong first answer.

**2026-09-09, level 1 idle — the first clean two-sided comparison.** Removing
the *input* rather than the player: the owner loaded level 1 and touched nothing
for five minutes (`docs/experiments/level1-idle-001`, protocol and predictions
recorded before the run). Event set over 1176 ticks: **4 in both, 0 DK2 only,
0 ours only**, against 2 / 46 / 2 for the same level played.

So the 46 were the player all along, and so were `f1` and `f34` — the two flags
this morning's entry above was still calling ours. **We invent nothing on level
1**, and neither does the type-72 assumption cost anything while nobody plays.

**And turn numbers are readable after stamp alignment.** `f1` and `f34` sit
exactly 7 ticks late in DK2 while being 568 ticks apart. Timer 15 reads the
origin: DK2 stored **7** where we store **1**. Six captures put that first stamp
at ticks 4, 6, 7, 8, 10 and 11, so the load origin jitters and absolute ticks are
not comparable (Q272). `dk2tools capture-diff` removes that origin and reports a
`+1` residual on both flags; its source remains open as Q273.

**2026-09-10, Level 20 idle — the first window where nothing differs at all.**
Reachable only after `docs/research/campaign-progress-registry.md`. Chosen
because its idle run stamps two timers early and then does nothing: a stamp is
*stored*, so unlike a flag it cannot be blurred by when the probe looked. Over
ticks 36–1218 **all 150 columns are identical**, and both sides stamp timers 0
and 1 at tick **4** (`docs/experiments/level20-idle-001`).

That kills one of the two explanations for Q273's residual: our tick numbering
carries no systematic offset. It does not settle the other, because **no event
fired inside the window** — the first was due 506 ticks after the capture
stopped. A silent window is worth recording and is not worth over-reading.

The same run exposed a defect in `capture-diff`: our `f6`, due at 2404, was
counted as a divergence against a capture that ended at 1218, so a run matching
in every column reported "event set differed". An event the other side stopped
before is untested, not divergent, and is now reported as such.

**2026-09-10, Level 20 for eleven minutes — Q273 answered by the game itself.**
Timer 1 restamps inside this window, and a timer slot holds the tick the game
recorded when it acted, so it cannot be moved by reading it late. DK2 stored
**1724**, we store 1724, and the probe first *saw* it at **1725**: one event
carries the ground truth and the observation together, and they differ by
exactly the disputed tick (`docs/experiments/level20-idle-002`).

Measured on t1, then checked against three events it was not fitted to — Level
20's `f6` (2405 → 2404) and level 1's `f1` (40 → 39) and `f34` (608 → 607) —
all three land on ours. **Every trigger event captured from DK2 across two
levels now matches exactly.** The probe's sample labelled N shows state before
tick N's trigger pass, so capture row N is our row N−1.

Two things to carry forward. `capture-diff` could not see the restamp at all,
because it kept only each column's first non-zero value; it now compares each
timer's stored-stamp sequence, which is the only unsampled part of a capture
(ADR-0119) — and re-running the ADR-0118 window rule against the earlier
capture caught the same off-by-one hiding in the new code path. And the run was
**not idle**: heroes invaded, killed imps and attacked the heart with no
trigger firing, so Level 20 is a still world for only about seven minutes
(Q275).

## 2026-09-09 — there is no level worth capturing yet, and that is measurable

The plan after level 1 was to find a level whose script runs on a clock rather
than on a player, capture that instead, and finally have a comparison where a
divergence is ours. `dk2tools kwd oracle` was written to pick it. It says not to
bother: **7 of 66 levels move any flag or timer past turn 1, and the best moves
three.**

| level | moved | after turn 1 | could | the file writes |
|---|---:|---:|---:|---:|
| Level20 | 3 | **3** | 9 | 25 |
| level15b | 5 | 1 | 18 | 25 |
| level4 | 4 | 1 | 28 | 36 |
| level5 | 4 | 1 | 11 | 15 |
| Level6a | 4 | 1 | 2 | 20 |
| level1 | 3 | 1 | 7 | 93 |
| Level16 | 3 | 1 | 17 | 39 |
| level3 | 7 | 0 | 15 | 47 |

The table has not moved since it was first measured; ADR-0107 is the change that
was expected to move it and did not.

`moved` is what a headless run of 1200 turns actually sets; **`after turn 1` is
the only part a played capture can be compared against**, because turn 1 is a
level's unconditional opening and both sides agree there for free. `could` is
what a walk that got through every type 1, 2 and 34 gate would set — an upper
bound, not a promise.

**The gap between `could` and `moved` is the finding.** level4 could move 28 and
moves 4; Level16 could move 17 and moves 3. The script is structurally
reachable. It stalls on condition and action types nobody has implemented, and
the walk stops there because a declined record does not descend.

### What is actually in the way

The blocked census, summed over the 57 levels that reach a block at all:

| | records | levels |
|---|---:|---:|
| action 17 | 238 | **38** |
| condition 19 | 182 | 28 |
| condition 53 | 157 | 24 |
| condition 39 | 138 | 29 |
| action 24 | 96 | **37** |
| condition 29 | 77 | 14 |
| condition 47 | 72 | 18 |
| condition 25 | 57 | 21 |

**Only the conditions are in the way, and that was tested, not assumed.** Every
action type here is one DK2's dispatcher succeeds at, so declining them looked
like the cheap half of the problem — un-block them and the walk descends as the
original's does, without anyone working out what they mean.

It buys nothing. Un-blocking every action type (ADR-0107) left the table above
byte-identical: same 54 columns moved, same 9 moving past turn 1, same 777
distinct conditions stalled on. **A walk descends into `ChildTriggerId`, and all
5,408 shipped action records have zero there** — actions are leaves, so their
return value has never decided where a shipped walk goes. The change is still
right (DK2 retires a successful action; declining left the bit set) and the
census is now honest, but the reach was always condition-bound.

**Done, and it behaved as predicted** (ADR-0109). The eight player condition
types whose jump-table entry is the zero-return epilogue now answer false rather
than declining. `moved` and `after1` did not move — false and declined both stop
the descent — and the frontier fell from **777 stalls to 616**, a fifth of it
noise that the binary had already answered.

**And the census now names the walker** (ADR-0121). A type is dispatched by its
own context's table before anything is delegated, so ranking by type alone can
send you to the wrong arm: of the 66 stalls left after ADR-0120, type 64's 12
are in the **world** tree and type 70's 6 are in an **area** tree, and only the
rest are the player arms the type numbers suggest. Reading types 46, 52, 71 and
73 whole took the frontier to **59**.

**And then the frontier stopped being the answer** (ADR-0125). `dk2tools kwd unreached`
accounts for every one of the 9,161 shipped records from a real run: 2,669 ran, 3,824 were
walked to and answered false, 1,138 hang off creature roots nothing walks, 864 off party
subtrees nothing arms, 513 off no root at all — and 153, **1.7%**, are the whole condition
frontier. An idle capture therefore has a **one-third ceiling** on the script it can exercise, and
ADR-0126 names what holds the rest: the largest identifiable block is 696 records waiting
for **player 3 — the human keeper** — to dig, claim and build. Idle captures are exhausted
as a source of signal; what M5 needs next is an input channel, not more mechanisms.

### So the oracle is not blocked on choosing a level

It is blocked on **conditions**. Until enough of them are implemented for some
level to run its script without a player, a recorded run measures the player, as
level 1's did. `dk2tools kwd oracle` re-scores every level in one command; its
`stalls` column is the frontier and the only one that grows when a condition
lands.

**What the owner can settle by playing is not here.** It is
`docs/research/observation-checklist.md`, which is the working list of
single-question experiments; this file is for paired runs of the same scenario.
Q1 is the standing example of the difference: no stopwatch could separate *5
ticks a second at work rate 0.8* from *4 ticks a second at 1.0*, so it was
settled by reading the binary instead — a tick is 250 ms (ADR-0089).

**2026-09-10, level 1 played, run 10 — the input channel, measured.** The first
two-sided comparison of a *played* capture. One capture, two of our runs, both
against DK2's own trigger state over 500 turns:

| our run | in both | DK2 only | ours only |
|---|---:|---:|---:|
| without input | 3 | **28** | 0 |
| with the recorded input | **24** | **7** | 0 |

**Replaying the capture's own commands recovers 21 of the 28 events only a
player can produce**, and we still invent nothing. That is what ADR-0126 said
M5 needed, delivered and sized.

**The seven that remain are one thing.** Every one is a terrain-count
condition — six of type 42 and one of type 41, the "keeper activity" family.
So the gap is no longer input but *consequence*: the commands arrive, and then
our imps dig and claim at our rates rather than DK2's, so the counts do not
cross the same thresholds at the same time. It is also why stamp alignment went
from agreeing to 80 ticks out.

Caveat: run 10 predates the probe's `workers` column, so a dig whose work kind
was never sampled reads as the player letting go. That inflates untags and does
not affect the event-set counts above.

**And the same capture times DK2's claiming.** Pool kind 3 is an imp claiming
the floor a dig left behind — every kind-3 task follows that tile's own dig
completing, 14 of the 15 tiles in one drag (the last was dug too late to claim
inside the window). They run strictly one at a time, each starting the tick
after the last ended, and each takes **exactly 10 ticks** — fourteen times over,
2.5 game seconds. Two other clusters took 12 and one 20, which is consistent
with travel being included and varying with distance, so **10 ticks is an upper
bound on the work itself**.

Ours completes a claim in **4 ticks**: `ClaimTileHealth` 825 over the
efficiency divisor is 165 a swing, and Dirt Path climbs 1 → 660 in four. The
gap is real; its size is not yet pinned, because DK2's figure still contains
the walk (Q292).

**2026-09-10, level 1, run 11 — labelled play, and the discriminator it broke.**
The first capture carrying ground truth: the owner marked single tiles and left
them to be dug, then cancelled two deliberately — one before any imp reached it
and one *while three were digging it*. Both were named in advance.

| tile | tagged | gone | workers at removal | claimed after |
|---|---:|---:|---:|---|
| (8,30) cancelled, no imp | 109 | 119 | 0 | no |
| (11,27) cancelled, three imps | 127 | 141 | **3** | no |
| nine dug to completion | | | 1–3 | yes, all nine |

**The `Worker` count does not tell a cancellation from a completion** — (11,27)
carries the same three as a finished dig. The column was added that morning for
exactly this job and does not do it (Q293).

**The claim does.** A dug tile is claimed and a cancelled one stays rock, so a
removal is a completion exactly when a kind-3 task follows on that tile before
it is marked again. Against the labelled run that keeps both cancellations and
rejects all nine completions: sixteen commands, **no false untags**. One
questionable pair remains — (15,23) leaves and returns seven ticks later under a
different task id, which is DK2 re-allocating rather than the owner.

**Claim timing, cleanly.** Nine claim tasks, each starting with its worker
already assigned, so no walk is in the figure: **11, 11, 11, 11, 11, 11, 12, 16,
17 ticks**. Run 10's fourteen all read 10. Both runs are internally consistent
and differ by one, which is sampling phase against a 3.93 tick/s game at 4 Hz —
a higher rate would settle whether the constant is 10 or 11. Ours is 4 (Q292).

**2026-09-10, run 11 — reinforcing, found and timed.** Pool kind 6 is an imp
reinforcing a wall. Every one in the capture sits on a wall **adjacent to a
tile just claimed**, two queued the tick the claim finished, and the rest wait
for a free imp:

| wall | queued | after claiming | gap |
|---|---:|---|---:|
| (9,28) | t109 | (10,28) at t108 | +1 |
| (14,22) | t235 | (14,23) at t234 | +1 |
| (13,23) | t255 | (14,23) at t234 | +21 |
| (15,22) | t362 | (15,23) at t284 | +78 |

Durations, each starting with its worker already assigned: **61, 61, 70, 76**
ticks. Our model reinforces Rock in **50** applications — 495 of deficit at 10
a swing, one swing a tick — so we are again the faster side, by about a fifth
(Q294). Three more were still running when the capture ended, consistent with
tasks of that length.

This is the second mechanism the task pool has named without a decompile of its
own, after claiming. Both were identified from *what precedes them* rather than
from how they arrive — the reading that failed twice today.

**2026-09-10, run 12 — claiming timed at 20 Hz.** Five samples a tick instead
of one, so a task's ends land on the tick they happened. Sixteen claims:

| | ticks |
|---|---|
| run 12, 20 Hz, n=16 | floor **11**, mode **12**, tail to 25 |
| run 11, 4 Hz, n=9 | floor 11, mode 11 |
| run 10, 4 Hz, n=14 | 10 throughout |

The tail is contention — this run had thirteen tasks live and imps claiming
concurrently, where run 11's went one at a time. **Twelve ticks is three game
seconds; ours is four ticks, one second.**

The arithmetic points somewhere specific rather than at a fudge: Dirt Path's
deficit of 659 over twelve swings is **55 a swing, and `ClaimTileHealth` 825 ÷
55 is exactly 15** — three times our divisor of 5. Three swings of 220 at one a
game second fits the same 12 ticks. Which of those DK2 does is a question for
the claim handler, not for curve-fitting, so the constant has not moved (Q292).

Two notes on method. The probe held 20 Hz with no late samples once each task
entry was read as one 36-byte block rather than eighteen fields. And the owner
alt-tabbed near the end: the game fell to 0.7 ticks/s for the last ten seconds,
plainly visible as `t297` at 75.0 s and `t304` at 84.8 s, and everything after
`t297` is excluded above.

**2026-09-10, runs 13 and 14 - the input channel, complete.** Two played
captures of the *same three tiles* on level 1, 20 Hz, 60 s each: mark (10,28)
and (14,32) rock and (14,24) gold, then leave them entirely alone. A controlled
repeat, and the first captures where the replay leaves nothing on either side.

| | in both | DK2 only | ours only |
|---|---|---|---|
| run 13, no input | 3 | **3** | 0 |
| run 13 + recorded input | **6** | **0** | 0 |
| run 14 + recorded input | **5** | **0** | 0 |

Twenty rows read out as three commands, and twenty-two as three - exactly the
three marks placed each time, with no false positives. The pipeline model of
the pool (ADR-0131) survives a run whose ground truth is unambiguous.

What is left is rate, not input: the two flags an imp drives fire **37 and 28
ticks early**, while `f2`, a trigger timer with no imp in its path, lands at
residual +1, which is exact agreement.

**Reinforcing occupies 61 ticks.** Run 14's three reinforce tasks each ran
exactly 61; run 13's ran 61, 61 and 75. Walk time is near zero - each task is
created the tick after the adjacent claim ends, with the imp standing on the
tile it just claimed - and run 14's three overlapped yet came out identical, so
61 is work, not scheduling. Against it: our model spends 50 applications, and
the owner's stopwatch (Q62) read 13.1-13.8 s, which at the rate below is
50-54 ticks. **The pool task is 7 to 11 ticks longer than both** (Q296).

**A tick is not 250 ms.** The probe times the game's own turn counter against
the wall clock, and four clean runs disagree with `SimulationTiming`'s note:

| run | ticks/s | ms/tick |
|---|---|---|
| 10 | 3.900 | 256 |
| 11 | 3.937 | 254 |
| 13 | 3.837 | 261 |
| 14 | 3.803 | 263 |

Run 12 is excluded at 3.446: the owner alt-tabbed. Four ticks a *game* second
is not in doubt - variable 19 reads 4 and the trigger timers agree exactly -
but a game second costs **1.02-1.05 wall seconds**, because the turn is
frame-paced. Every stopwatch reading the project has used as evidence is
therefore biased 2-5% long against a tick count converted at 250 ms.

**Claiming: the floor is 9 ticks, not 11.** Runs 13 and 14 add 9, 11, 18, 12,
17 and 22. The 9 is below run 12's floor and breaks the clean arithmetic that
12 had (Q292), which is a reason to distrust the task's ends rather than the
constant. No constant has moved on any of this.

**2026-09-10, run 15 — the tile's own health, and four work rates at once.**
The capture the last three were building towards: `dk2watch.py --tiles` reads
the health word of every tile the pool has work on, 20 times a second. Three
tiles marked and left alone; 235 rows over six tiles.

**The offsets are right.** Terrains came back 2, 3, 6, 8 and 9 — Rock, Dirt
Path, Gold, Claimed Path, Reinforced Wall, exactly the five in play — and every
health sat inside its tabled maximum. `map = world + 0x9DA`, tiles `+0x14`,
health `word +0x06` are confirmed live (`runtime-map.md`).

**The traces.** Each is one tile, tick by tick:

| tile | trace |
|---|---|
| claim (10,28) | `1 → 165 → 329 → 493 → 657 →` Claimed Path — **+164, five times** |
| reinforce (14,33) | `165 → 174 → … → 651 →` Reinforced Wall — **+9, fifty-five times** |
| mine (14,24) | `660 → 600 → … → 60 →` Dirt Path — **−60 a worker** |
| dig (14,32) | `900 → 874 → …` — **−26 a worker**, in steps of 26 and 52 |

All three reinforces ran +9 and all three claims +164, so these are the rates
and not a sample. **One mechanism gives all four**: the tabled delta times
Efficiency in 4096ths, arithmetic-shifted down 14. Our `tabled ÷ 5` matched two
of the four — it is only equal when the value divides by twenty (ADR-0133).

**Nothing is carried.** Fifty-four consecutive reinforce steps of exactly 9
against a tabled 9.6. An accumulator would have shown 9, 10, 10, 9, 10.
`TileState.WorkRemainder` is deleted; `HashVersion` 54.

**Q296, answered.** The pool task brackets the work. A claim task enters the
pool about 5 ticks before the health first moves and leaves 2 after; reinforce
is 55 applications over 56 ticks inside a 61-tick task. The 9–22 tick spread in
claim task lengths was walk time, never the work.

**Q62, closed.** 55 applications is 56 ticks, 14.2 s at this run's 254 ms tick,
against the owner's 13.1–13.8 s stopwatch. The old 50 gave 12.7 s and sat below
every reading for a fortnight. The residual was the +1, not a mechanism.

**It does not move the replay, and that was checked.** Runs 13–15 keep their
residuals (−37, −27, −18): digging is the one delta ADR-0133 leaves alone, and
the lagging flags are dig-driven. What is left is the approach time, Q296.

**One thing raised, Q297.** Digging a Reinforced Wall ends at Dirt Path rather
than the Rock its `DestroyedTypeTerrainId` names — (14,32) showed Rock at a full
165 for a single tick, (10,28) never showed it. Three workers at 26 cannot take
165 off in one tick, so something carries the job through.

