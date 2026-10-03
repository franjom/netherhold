# How often a DK2 miner strikes, and where the gold goes (Q480)

**Source:** the owner's Level 2 recording `l2win928`, replayed once with `-PLOAD`
on 2026-09-28 and recorded in full by `reverse-engineering/dk2record.py`
(ignored `dk2runs/level2-fullstate-20260928/`). Every tick of the playback,
1–3536, is in the capture: no torn passes and no missed ticks. The format and
decoders are in `reverse-engineering/README.md`, "Full-state captures".

Everything below is read from the running game's memory: tile health (tile
`+0x06`), each imp's state (`+0x25E`) and carried gold (`GoldHeld`, `+0x3FB`),
and each floor pile's amount (object union `+0x14A`). Nothing was read from the
binary for this note, so the rules are the game's outcomes, not its code.

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
application lands at E+2 for every kind. The second follows 4 ticks later for
mining, 3 for digging (one case) and reinforcing (seven), and 1 for claiming (ten
cases). After that, one application a tick.

**Rock falls to one strike.** Every rock tile (terrain 2, 165 health) in the
capture goes from 165 to a dug path at health 1 in one step, on the first strike
of the imp beside it (for example (15,33) at 333). So the shipped `DigRockHealth`
(-5000, one application -1000) is right. The two swings the owner counted
(2026-08-30) must be the long first animation, and `ObservedCorrections`'
-500 is wrong.

**The mechanism, read (STRONGLY INFERRED).** State 26 (`0x00465870`) holds while
an action runs. Otherwise it checks the task, the full-hands store trip and the
cell pile, and starts action 2 while the seam stands. The action runner
`0x00496AA0` runs a mode-3 action (record `+21` = 3; actions 1, 2, 4 and 7) by
calling its handler once, which is the strike (`0x004971D0` for action 2). It then
keeps the action until its animation reports done (`+0xDC` = 0). So one action is
one application, and its length is animation time. Why only the first swing is
long is in the animation engine, unread.

**Built (ADR-0280, ADR-0282; Q481).** Built alone, the cadence and the uncorrected rock matched
this capture's early imp time (turns 1–635: walking 968 ticks against 976, digging 365 against
367, mining 374 against 413) but lost Level 1's packet, whose Hatchery at 324 landed on ground
not yet dug or claimed; what else had to move is Q481's answer (§6, ADR-0282).

## 6. Level 1's full-state playback: rest, and how long a work state holds (2026-09-28)

A second capture (`l1win927b`, 1905 ticks, none torn or missed) answers part of
Q481. The numbers below are imp runs in each state, from both captures.

**The idle actions last 20 and 25 ticks (CONFIRMED as outcomes).** After a
wander trip, state 12 lasts 1 tick (a wander at once), 20 ticks (31 cases) or
25 to 26 ticks (17 cases). A 2:1 split matches 12's roll: 25-74 plays action 43
and 75-99 plays action 44, so 43 is the 20 and 44 the 25 (STRONGLY INFERRED). Ours
plays both for a guessed 8. The imp's animation blocks do not give these lengths:
its two long ones are 171 and 172 frames at 30 fps, 23 ticks each.

**Resting too briefly broke the hand drop.** With the parked cadence build, three
of our imps take (10,28) together at 41-48 while DK2's are still resting or
wandering. So (14,32), which DK2 digs from 61, falls at 236, and the player's drop
there at 221 is refused as unwalkable. With 43 at 19 and 44 at 24 ticks (our
count, one short of the run) the drop lands. The Hatchery at 324 is still refused,
because its tiles are dug 50-100 ticks late and claimed 100 late.

**A dig holds after the strike (CONFIRMED as outcomes).** A dirt or rock dig runs
6 ticks in state 14, with its one strike at E+2 (34 of 40 in Level 1, 62 in Level
2). The imp stays until E+5 and leaves at E+6. Ours leaves once the tile is gone:
2-3 ticks on master and 4 with the cadence. A claim applies every tick from E+2
and leaves one tick after the last: 6 ticks (four applications) or 8 (five). Master
matches that; the cadence build takes 8 where DK2 takes 6. Mining runs 8-9 in
Level 1, against 5-6 on master and 9-11 with the cadence.

**DK2's idle loop takes two more ticks.** It runs 1, then 10, then 12, one tick
each. Ours goes from 1 to 12 in the same tick. Ours has no entry states (13, 15,
21, 25) either, which each last one tick in DK2.

So master's early digs are too fast and its rests too short, and the two cancel
there. Neither the hold nor the idle lengths is built.

## 7. How long a digger stays after its wall falls (2026-10-01)

**Measured (seven seeded Level 1 captures, to tick 1500).** A digger leaves state 14 on a
multiple of 6 ticks after it entered (461 of 630 exits), so the 6-tick dig action stands.
From the fall to each digger's exit: DK2 0/1/2/3/4/5 ticks in 25/42/84/40/374/65 cases;
ours (20-seed replays, seeds 1–7) 1/2/3/4/5/6 in 92/26/45/346/65/61. Means 3.4 and 3.7.
The one-strike Rock tiles (the 4 peak) agree. DK2 can leave on the fall's own tick and never
at 6: a strike lands inside the striker's update, so a later digger whose action ends that
tick already sees the tile gone. Ours applies strikes after every imp has moved
(`TileWorkSystem`), so that digger waits a whole action. Not changed: it is the order of
work within a tick, worth 0.3 ticks on average. It matters for the kind-14 window of
ADR-0305 (`digexit.py`, `ourdigexit.py` in the session scratchpad).
