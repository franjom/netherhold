# How often a DK2 miner strikes, and where the gold goes (Q480)

**Source:** the owner's Level 2 recording `l2win928`, replayed once with `-PLOAD`
on 2026-09-28 and recorded in full by `reverse-engineering/dk2record.py`
(ignored `dk2runs/level2-fullstate-20260928/`). Every tick of the playback,
1–3536, is in the capture: no torn passes and no missed ticks. The format and
decoders are in `reverse-engineering/README.md`, "Full-state captures".

Everything below is read from the running game's memory: tile health (tile
`+0x06`), each imp's state (`+0x25E`) and carried gold (`GoldHeld`, `+0x3FB`),
and each floor pile's amount (object union `+0x14A`). The original measurements
are capture outcomes; the dated reconciliation below also checks binary fields.

## 1. The strike cadence: E+2, E+6, then every tick

**CONFIRMED (running game, one playback, five imp-on-seam cases).** An imp that
enters the mining state 26 on tick E strikes on tick **E+2**, then on **E+6**,
then **on every tick** after that until the seam is gone or the imp leaves.

| imp slot | seam | entered 26 | strikes |
|---|---|---|---|
| 451 | (19,33) | 145 | 147, 151, 152, 153, 154, 155 |
| 449 | (19,33) | 148 | 150, 154, 155 |
| 448 | (19,33) | 149 | 151, 155 |
| 451 | (19,32) | 162 | 164, 168, 169, 170, 171, 172 |
| 448 | (19,32) | 163 | 165, 169, 170, 171, 172 |

A strike is identified by the imp's `GoldHeld` rising by 272 or 273 on the same
tick that the tile loses 60 health. A full imp's strike instead shows up as a
new or larger floor pile at its feet. Every tile drop in the window is a whole
number of strikes: 60, 120 or, on the last tick, what was left.

So a seam's time is set by this start-up delay, not by the number of workers.
The seam at (19,33) holds 660 health, which is 11 strikes of 60. It fell from
660 at 146 to 1 at 155. Its three imps entered 26 at 145, 148 and 149 and
struck 11 times between them. Most of the 10 ticks went on the first two
strikes of each imp.

**Why E+2 and E+6 is UNKNOWN.** It looks like a wind-up and a first swing that
are longer than the swings after them, but nothing here says so. The
binary's state-26 handler (`0x00465870`, `gold-on-the-floor.md` §10) is where to
read it.

**Ours** applies one strike per worker per tick from the first tick in the
state, so three imps take the same seam in 4 ticks.

## 2. A strike's gold, and the pile at the miner's feet

**CONFIRMED (running game).**

- A strike frees **272 or 273** gold. The 11 strikes on (19,33) total exactly
  **3000**: imp 451 got 273, 272, 273, 273 and 272 in hand, and on its last
  strike 37 in hand plus 235 on the floor; imp 449 got three of 273; imp 448 got
  two of 273. The split is 3000 over 11 with the remainder spread, not a
  constant 273.
- **Gold is conserved at the full imp.** Its hand limit is 1400. On tick 155,
  imp 451 held 1363. The strike's 272 went 37 into its hand and **235** into a
  new floor pile at (19,34), owner 2 (slot 1173). On tick 170, imp 448 held 1364.
  Its 273 went 36 into its hand and 237 into a new pile. A full imp's whole strike
  goes to the floor: piles of 273 appear at 164 and 168.
- **A pile in the miner's cell is picked up by an imp there.** STRONGLY INFERRED
  from timing. On tick 156 pile 1173 (235) vanished and imp 449, still in state
  26 on that tile, gained exactly 235. Pile 1174 (273) went the same way into
  imp 448 on tick 165, the same tick as its own strike (+546). This fits the
  floor-pile check before each strike that `gold-on-the-floor.md` §10 read.
- Of 142 increases of type-1 floor piles over the whole playback, 98 are one
  strike (64 × 273, 34 × 272). 17 leave the amount unchanged while another
  column (position or owner) changes. The other 27 are not strikes: 21 rises
  of 545 to 12,982 and 6 falls of 109 to 1610. Which put-downs and
  pickups they are was not decoded. So "a pile grows 272–273 a strike" holds
  for strikes and only for strikes.

## 3. The mined-out tile, then the claim

**CONFIRMED (running game), seam (19,33).** The tile stays gold (terrain 6),
owner 2, until the strike that empties it. On that tick (155) it becomes terrain
3 at **health 1**, owner 2. It sits there until tick 171, then gains **164 a
tick** (165, 329, 493, 657). At 175 it becomes **Claimed Path (8), owner 3, at
660**. Which imp claimed it and its state were not decoded.

## 4. What the capture itself showed

- The two old probe passes of this recording, and this one, start timer 14 at
  three different turns: 81, 9 and now 82 (condition 72, interface state).
  Despite that, the creature change log for slots 400–469 **equals the old
  creature pass row for row from tick 2 to 3535** (22,490 rows). The object
  log for slots 1150–1719 equals the old object pass up to tick 3404. It first
  differs at 3405, where one gold object sits a tile away.
- At tick 1 this capture caught the level still loading (all 448 creature
  records registered and zeroed, no objects), where the old creature pass saw
  the placed creatures. From tick 2 they agree.
- At tick 2 the Heart's 16 rim tiles, (17,36)–(21,40), each hold one store pile
  (object 3) of 1000.

## 5. The other work kinds, rock, and the state 26 read (2026-09-28)

**CONFIRMED (same capture, solo imps).** Every entry state (13, 15, 21, 25) lasts
exactly one tick (416 cases). From the work state's first tick E, the first
application lands at E+2 for every kind in this sample. The second follows
4 ticks later for mining and digging, 3 for reinforcing (seven cases),
and 1 for claiming (ten cases). After that, one application a tick.

### Digging reconciliation, 2026-10-18

**CONFIRMED measurements:** all 15 complete multi-hit digs among 153 state-14
runs in the original Level2 capture have a four-turn first-to-second gap.
All 18 complete multi-hit digs among 126 runs in Level4 frames 2–1101 agree.
Per-worker experience at `+0x3FF` identifies hits; a promotion at `+0x403`
resets experience, as [the adder](functions/00457880.md) confirms. Neither
capture has a torn frame in the examined range.

On Level2's early `(19,34)` wall, worker 451 hits on 121/125/126/127,
448 on 124/128/129/130, and 449 on 127/131/132/133. Tile-health drops on
121 and 124 therefore belong to different workers. The previous three-turn
dig claim is unsupported by this capture: aggregate damage cannot establish
an individual worker's cadence.

The first animation cycle has phase 1365, 2730, 4095, then 1364 with the
cycle flag set. [The runner](functions/00496AA0.md) observes that flag on
the following poll; matching looping art retains it on subsequent action
starts. The [Level4 boundary](imp-hatchery-work.md#initial-dig-arrival-and-cadence-are-separate)
records the clock and diagnostic outcomes. These measurements support
correcting ordinary digging's empirical gap to four; they do not establish
a general action scheduler or altered-speed animation behavior.

Ignored reproduction: `build/imp-acceptance/reverse-engineering/imp-hatchery-work-20261018/`
contains `audit_dig_gap.py` and `dig-gap-audit.json` with all runs, hit stamps
and initial clocks. The candidate and its acceptance limits are ADR-0510.

### Isolated gap correction: acceptance

The `fixes/imp-dig-first-cycle` candidate changes only the dig kinds' first
gap from three to four, plus hash/save 275 and its pin; it introduces no fields
or allocations. A real own-wall simulation regression fails on baseline at
relative hit turns 2/5/6/7/8 and passes at native 2/6/7/8/9. All 3576
simulation tests pass. This preserves the empirical timer model; a general
action scheduler and altered animation rates remain outside its scope.

Eight paired packet runs against main `bda4952e`, seeds 1/5, have consecutive
turns and no placed-type mismatch. Win turns and player-3 Imp deaths:

| Level / seed | Baseline win / deaths | Candidate win / deaths |
|---|---|---|
| 1 / 1 | 1804 / 0 | 1820 / 0 |
| 1 / 5 | 1804 / 0 | 1812 / 0 |
| 4 / 1 | 6732 / 3 | no win by 6804 / 13 |
| 4 / 5 | no win by 6804 / 35 | no win by 6804 / 11 |

**Integrated under ADR-0512.** The bounded cadence correction is supported.
Level4 seed 1 loses its baseline win. These are unmatched RNG replays with
reused entity maps, not exact native trajectories or a seed-distribution result.
`paired_dig.py` and `paired-dig/results.json` reproduce the table; TRX files
under `tests/` preserve red, green and full simulation results. No full solution
suite, profiler or Unity visual run was performed for that isolated experiment.
The later [Portal visibility control](imp-hatchery-work.md#portal-regression-a-work-visibility-boundary)
isolates part of the changed allocation history but does not recover the win.

These one-tick entries are captured outcomes. The binary's wall-work entries
turn toward the target centre and hold until facing; see imp-state-exits.md §2.

**Rock falls to one strike.** Every rock tile (terrain 2, 165 health) in the
capture goes from 165 to a dug path at health 1 in one step, on the first strike
of the imp beside it (for example (15,33) at 333). So the shipped `DigRockHealth`
(-5000, one application -1000) is right. The two swings the owner counted
(2026-08-30) must be the long first animation, and `ObservedCorrections`'
-500 is wrong.

**The mechanism, read (STRONGLY INFERRED).** State 26 (`0x00465870`) holds while
an action runs, then checks its task, hands and cell pile before restarting.
Mode 3 requires both handler and animation completion; a handler that returns
busy can run on multiple polls. Cycle flags can complete the animation before
its rate becomes zero. See [the runner](functions/00496AA0.md) and
[the exhausted mining action](mining-finish.md) for the current mechanism.

**Built (ADR-0280, ADR-0282; Q481).** Built alone, the cadence and the uncorrected rock matched
this capture's early imp time (turns 1–635: walking 968 ticks against 976, digging 365 against
367, mining 374 against 413) but lost Level 1's packet, whose Hatchery at 324 landed on ground
not yet dug or claimed; what else had to move is Q481's answer (§6, ADR-0282).

## 6. Level 1's full-state playback: rest, and how long a work state holds (2026-09-28)

A second capture (`l1win927b`, 1905 ticks, none torn or missed) answers part of
Q481. The numbers below are imp runs in each state, from both captures.

**The idle actions last one play of their clip (CONFIRMED, ADR-0515).** After a
wander trip, state 12 lasts 1 tick (a wander at once), 20 ticks (31 cases) or
25 to 26 ticks (17 cases). The split is not by action: each install flips a coin and
on a 1 plays IMP-Idle4, 19 turns, instead of slot 20 (24) or 31 (25); one more tick
hands over. The earlier reading that 43 is the 20 and 44 the 25 was wrong. The
Level 4 capture shows both actions at both lengths. Mechanism and clip arithmetic:
[animation-clock.md](animation-clock.md#idle-actions).

**Resting too briefly broke the hand drop** (before ADR-0280). With the parked cadence build, three
of our imps take (10,28) together at 41-48 while DK2's are still resting or
wandering. So (14,32), which DK2 digs from 61, falls at 236, and the player's drop
there at 221 is refused as unwalkable. With 43 at 19 and 44 at 24 ticks (our
count, one short of the run) the drop lands. The Hatchery at 324 is still refused,
because its tiles are dug 50-100 ticks late and claimed 100 late.

**A dig holds after the strike (CONFIRMED as outcomes).** A dirt or rock dig runs
6 ticks in state 14, with its one strike at E+2 (34 of 40 in Level 1, 62 in Level
2). The imp stays until E+5 and leaves at E+6. A claim applies every tick from E+2
and leaves one tick after the last: 6 ticks (four applications) or 8 (five). Mining
runs 8-9 in Level 1. ADR-0280 builds the hold (`WorkCadence.ActionTicks`: dig 6,
claim 2); before it ours left once the tile was gone.

**DK2's idle loop takes two more ticks.** It runs 1, then 10, then 12, one tick
each, and each entry state (13, 15, 21, 25) lasts one tick. ADR-0280 builds both
(`WorkCadence`, `ImpTaskSystem.UpdateIdle`).

Before ADR-0280 (2026-09-29) ours dug too fast early and rested too briefly, and the
two cancelled; that ADR builds the hold, the idle lengths and the entry states together.

## 7. How long a digger stays after its wall falls (2026-10-01)

**Measured (seven seeded Level 1 captures, to tick 1500).** A digger leaves state 14 on a
multiple of 6 ticks after it entered (461 of 630 exits), so the 6-tick dig action stands.
From the fall to each digger's exit: DK2 0/1/2/3/4/5 ticks in 25/42/84/40/374/65 cases;
ours (20-seed replays, seeds 1–7) 1/2/3/4/5/6 in 92/26/45/346/65/61. Means 3.4 and 3.7.
The one-strike Rock tiles (the 4 peak) agree. DK2 can leave on the fall's own tick and never
at 6: a strike lands inside the striker's update, so a later digger whose action ends that
tick already sees the tile gone. Main applies strikes after all workers choose,
so that digger waits a whole action: a 0.3-tick mean difference in this measurement.
ADR-0503 resolves work after each worker; intra-creature
action/state ordering remains incomplete. The kind-14 consequence is measured in
`imp-task-audit.md` (`digexit.py`, `ourdigexit.py` in the session scratchpad).
