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
