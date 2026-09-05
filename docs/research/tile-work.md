# Tile work: digging, mining, claiming, reinforcing

Milestone M3. What happens to a tile when somebody works on it.

Implemented by `DK2.Simulation.TileWorkRules`, `TileWorkSystem` and
`TileTagSystem`. Every number below comes from the reference install; none is
written into the code.

---

## Summary of confidence

| Claim | Status |
|---|---|
| The ten health deltas, the terrain table's health/transition fields, the taggable and fill-inable flags | **CONFIRMED** — DK2's own data |
| Health reaching zero converts a tile to its `destroyedTypeTerrainId` | **CONFIRMED** by the data's shape (see below) |
| Upward conversion is triggered by the *work*, not by the health level | **STRONGLY INFERRED** — the level-triggered reading is refuted by the data |
| `maxHealthTypeTerrainId` is the destination of whichever upward job that terrain admits | **STRONGLY INFERRED** — resolves Q18 |
| A gold seam releases gold in proportion to the rock removed | **CONFIRMED** (Q21) — 273 gold a swing is 60 health of a 660/3000 seam, measured in the original |
| A gem face yields `GoldMinedFromGems` per collapse | **STRONGLY INFERRED** — see the gems argument below |
| Tagging is per player | **STRONGLY INFERRED** |
| **How often an application happens: a fifth of the tabled delta each time** | **STRONGLY INFERRED** (Q47, ADR-0020) — from 273 gold a swing, corroborated by eight of ten deltas dividing by five and by claiming's four swings. **The fifth is now read rather than derived** (2026-09-05): work is `delta x workRate / 4`, the worker's work rate is **0.8** and 4 is ticks per game second — see "The work rate" below |
| **A worker's work rate is 0.8, and it is a multiplier the handlers read** | **STRONGLY INFERRED** — read from the claim and reinforce handlers, with DK2's own `IncreasedWorkRate*` variables naming the concept |
| **The work rate does not change with the worker's level** | **CONFIRMED** (owner, 2026-09-05) — a level-3 imp and a fresh level-1 imp reinforce the same wall in 13.1 s and 13.3-13.4 s |
| The deltas are per second | **STRONGLY INFERRED** (Q19) — follows from the above; how long a second is in ticks is still Q1 |
| A fully mined seam yields exactly its `goldValue` | **STRONGLY INFERRED** — gold taken from running totals, so truncation shows between swings and never as a shortfall |
| Whether one worker applies the whole delta | **UNKNOWN** — untouched by the above, which measured one imp working alone |
| `DigRockHealth` | **DOES NOT FIT, and is now CORRECTED rather than reproduced** (Q20, ADR-0023) - the shipped -5000 cannot produce the two swings the owner measured, so `ObservedCorrections` reads it as -500. The only such override in the project |
| **An upward job needs the terrain's whole `maxHealth` worth of work** | **CONFIRMED and IMPLEMENTED** (Q62, ADR-0024) - the owner stopwatched reinforcing at 13.38 s, which is 66 swings, which is exactly Rock's `maxHealth` of 660 at 10 a swing. The same rule gives claiming its measured 4 swings (4 x 165 = 660 = Dirt Path's `maxHealth`). The rule it replaced fitted claiming only by luck and missed reinforcing by 16 swings |

---

## The work rate — READ FROM THE BINARY 2026-09-05, and it explains Q47's fifth

**A work application is the tabled delta times the worker's own work rate, and a
worker's work rate is 0.8.** Read out of the imp's claim handler
(`FUN_004974c0`) and, in the identical shape, its reinforce handler
(`FUN_00497ce0`-ish), both computing in 4096 fixed point:

```
work = <tabled delta> x (creature[0x3af] + creature[0x3b3])
```

`FUN_004363f0` is `(a * b) >> 12` and `FUN_0043d750` is `a + b`, so the shape is
rate x (base + bonus), and both fields are pinned at creation:

| Field | At spawn | Meaning |
|---|---|---|
| `0x3af` | `0xccc` = **exactly 0.8** | the **base work rate** |
| `0x3b3` | **0** (`EBX`, the zero register, in the block that also zeroes `0x3b7`) | a **temporary bonus** |

**The name is DK2's own.** The balance table carries four percentage modifiers
of a thing it calls the work rate — `IncreasedWorkRatePercentageFromSlapping`
(50), `IncreasedWorkRateDurationFromSlappingSeconds` (60),
`IncreasedWorkRatePercentageWhenCasinoBigWinOccurs` (35) and
`IncreasedWorkRatePercentageOfPlayersTorturedCreatureTypes` (25). `0x3b3` is
where those land, which is why it starts at zero: **slapping an imp to make it
dig faster is a work-rate bonus**, and it is the oldest mechanic in the series.

### What it does to Q47

Work accrues **once a tick**, at `delta x rate / 4` — the 4 being ticks per game
second (Q1, variable 19). At a worker's 0.8 that is **a fifth of the tabled
delta per application**, which is exactly the granularity Q47 measured from 273
gold a swing.

**So the fifth is explained rather than fitted.** Q47 got the right number from
one measurement and had to infer "five applications a second" to justify it;
what the binary says is *four applications a game second, one per tick, each
0.8/4 of the delta*. The arithmetic the code performs is unchanged — a fifth of
the delta per application — and the implementation stays correct. What changes
is that the fifth is now a read constant instead of a derived one.

### The measurements it fits

Owner measurements, 2026-08-30 and 2026-09-05, wall clock by hand. Claim times
include the post-work linger (Q49); reinforce times do not, which is why
reinforce is the sensitive instrument here.

| Measured | Model | Predicted |
|---|---|---|
| Mining: **273 gold a swing** | 300 x 0.2 = 60 health an application, x 3000/660 | **272.7** |
| Claim, level 1: **1.30-1.40 s**, 3 visible jumps | 660 / 165 = **4 applications** = 0.8 s, + ~0.5 s linger | 1.3 |
| Reinforce, **fresh level-1 imp: 13.3-13.4 s** | 660 / 10 = **66 applications** | **13.2 s** |
| Reinforce, level-3 imp: **~13.1 s** | the same 66 applications | **13.2 s** |
| **Slapped imp mining: 341 gold a swing**, reverting to 272-273 after a minute | work rate 0.8 + **0.2**, for `IncreasedWorkRateDurationFromSlappingSeconds` = 60 s | **340.9** |

**Q62 is now confirmed with a level-1 imp.** The rule that an upward job needs
the terrain's whole `maxHealth` (660 for Rock, not the 495 deficit) was measured
on an imp of unknown level in August; the fresh-imp reading reproduces it to
within a hand-timer's reaction time, so the level confound that this section's
first draft raised is closed.

### The slap confirms the whole model, and quantises the bonus - 2026-09-05

**A slapped imp mines 341 gold a swing where an unslapped one mines 273, and it
reverts after a minute.** Owner observation in a fresh game, so no levelling
confound, and the minute is
`IncreasedWorkRateDurationFromSlappingSeconds` = **60**.

341/273 is **1.25 exactly**, which is 1.0/0.8: the work rate goes from the read
constant **0.8** to exactly **1.0**, and 300 x 1.0 / 4 = 75 health a swing =
340.9 gold. So the fixed-point multiplier this section reads out of the handlers
is not an interpretation - **it is observable in the game to three significant
figures**, and the 0.8 it starts at is real.

**The bonus comes in steps of 0.1, and the two measurements are one step
apart.** The routine that writes `0x3b3` (`FUN_00488930`) builds the bonus from
**hardcoded** increments rather than from the balance table: `0x199` = 409 =
**0.0999** per source, twice over, plus `205` = **0.05** per unit of a byte
counter held on another object the creature points at. Both owner measurements
land exactly on that grid:

**MEASURED, same day, fresh game, level-1 imp: a slap is +0.1, and it does not
stack.** The owner slapped repeatedly and the reading never moved past the first
step. Which also fixes the per-application rounding, because the first step does
not land on a whole number:

| | Work rate | Health an application | Gold | Observed |
|---|---|---|---|---|
| normal | 0.80 | 60.0 exact | 272.7 | **272-273** |
| slapped | 0.90 | 67.5 -> **68** | 309.1 | **309-310** |
| the 2026-09-05 mining run | 1.00 | 75.0 exact | 340.9 | 341 |

**So the health delta of one application is rounded to a whole number** rather
than carried as a fraction: 67.5 becomes 68, which is the whole of the 3-gold
gap between the 307 this section first predicted and the 309-310 the game shows.
Our own `delta / 5` is identical to `delta x 0.8 / 4` at the base rate, so
everything unslapped was already right.

Slapped reinforce times (11.9 s in one run, 11.5-11.6 s in another, against
13.3-13.4 unslapped) bracket the 11.75 s that +0.1 predicts and are consistent
with it; a 13-second job timed by hand resolves about ten times worse than the
gold counter and settles nothing on its own.

**WITHDRAWN, same day, by the owner's own control test.** This section briefly
claimed that the slap bonus scales with level, on the strength of a level-1 imp
reading 309-310 slapped and a level-2 imp reading 341. **A level-3 imp then read
309-310 UNSLAPPED, and a freshly created level-1 imp read 309-310 as well.** A
brand-new imp cannot have levelled, so the number is not a property of the imp
at all: **something dungeon-wide moved between the start of that session and its
end**, and every level comparison taken across that gap is confounded.

What the six readings actually are, in the order taken:

| # | Imp | Baseline | Slapped | Implied base rate |
|---|---|---|---|---|
| 1 | level 1, fresh game | 273 | 309-310 | 0.80 |
| 2 | level 2, same game | 273 | 341 | 0.80 |
| 3 | level 3, later | **309-310** | 341 | **0.90** |
| 4 | **new level-1 imp**, same moment as 3 | **309-310** | - | **0.90** |

Reading 4 is the one that decides it. **The slap is most likely +0.1 throughout**
(0.8 -> 0.9 in reading 1, 0.9 -> 1.0 in reading 3) and the base moved under
everything else.

**What moved is not identified.** Three candidates, and they are not all about
the work rate:

- **Something dungeon-wide raises the base**, in a step of 0.05 or 0.1. The
  bonus routine's own census term is *not* it: the counter at `+0x1094` is
  decremented for creature type `0x10`, which is **id 16, the Wizard**, and this
  dungeon has none.
- **The reading is not a full swing.** An imp's hands hold 1400 and a swing at
  0.8 frees 272.7, so a load is five swings (1363.6). If a partial last swing is
  being read, `1400 - 4 x 272.7` = **309.1** - which is exactly the number in
  question, and would mean nothing changed at all.
- **A different seam**, if a level can vary a gold tile's value.

**The diagnostic, and it needs no timing:** count the swings in one full load
before the imp runs to the treasury. **Five** swings means the rate is 0.8 and
309-310 was a remainder; **four** means the rate really is 0.9 or 1.0. And read
several consecutive swings rather than one: a rate repeats, a remainder does
not.

### The work rate does NOT change with level — a refuted prediction

The level-up routine `FUN_0048b120` adds a level-scaled stat to a field at
`0x3af`, which predicted that a levelled imp works measurably faster: at the
stat table's level-3 percentages a 13-second reinforce should finish in 7.5 to
11 seconds. **The owner measured a level-3 imp and a freshly created level-1 imp
reinforcing the same wall: 13.1 s against 13.3-13.4 s.** There is no level
effect, and a 13-second job could not have hidden one.

So the reading of `FUN_0048b120` is wrong somewhere — it is not the imp's path,
or the stat it scales is zero for imps, or the field it writes is not the field
the work handlers read. **The measurement is the fact and the decompile is the
hypothesis.**

An earlier pair of claim readings (level 1 at 1.30-1.40 s, level 2 at
1.16-1.20 s) appeared to show a level effect. It is **not supported**: the
difference is 0.17 s of hand timing on a job whose work phase is 0.8 s, against
a 13-second job that shows nothing at all. Recorded here so that nobody
re-derives it from the same two numbers.

### What is still open

- **The wall-clock length of a tick.** Timing cannot separate *5 ticks a second
  at rate 0.8* from *4 ticks a second at rate 1.0* — they are the same speed —
  and the 273 gold pins only the per-application fraction (0.2 of the delta).
  Q1 needs the game loop traced, not another stopwatch. `FUN_00517490` is the
  "now" the gates read and it is a bare field load,
  `*(*(this + 4) + 4)`; whoever increments that field answers Q1.
- **What moved the base rate mid-session** - see the withdrawn claim above.
  Until that is known, no reading taken across it can be compared with another,
  and the level question is open again rather than answered either way.
- **What the two bonus flags are**, and what the 0.05-per-unit counter counts.
- **Where a per-swing gold number appears on screen.** The owner reads it
  directly and this repo has never recorded *where* - see the note in
  `observation-checklist.md`. Every claim in this section rests on that readout,
  so it is worth writing down.

Confidence: **CONFIRMED** for the numbers the owner watched - 273 unslapped, 341
slapped, reverting after 60 s, and reinforcing unchanged by level. **STRONGLY
INFERRED** for the mechanism behind them: the fixed-point multiply, the 0.8
constant and the per-tick division are read from the binary and agree with the
observations to three significant figures, but only the observations are
watchable.


---

## The data

Each terrain type carries `startingHealth`, `maxHealth`,
`maxHealthTypeTerrainId` and `destroyedTypeTerrainId`. The seven types that
tile work touches:

```
 id name                health  maxHp   gold  ->maxHp  ->dead  flags
  1 Impenetrable Rock        1      1      0        1       1  solid, impenetrable
  2 Rock                   165    660      0        9       3  solid, taggable, dwarfCanDigThrough
  3 Dirt Path                1    660      0        8       3  fillInable
  6 Gold                   660    660   3000        6       3  solid, taggable, dwarfCanDigThrough
  7 Gems                   100    100   3000        7       7  solid, impenetrable, taggable
  8 Claimed Path           660    660      0        2       3  ownable, attackable, fillInable
  9 Reinforced Wall        900    900      0        9       2  solid, ownable, taggable, attackable
```

And the ten deltas, from `GlobalVariables.kwd`:

```
  4 ClaimTileHealth          825      12 ReinforceWallHealth     50
  5 AttackTileHealth        -140      13 RepairWallHealth        22
  6 RepairTileHealth         125      11 FillInHealth             5
  7 MineGoldHealth          -300      20 GoldMinedFromGems       25
  8 DigRockHealth          -5000
  9 DigOwnWallHealth        -130
 10 DigEnemyWallHealth        -8
```

Note what the *names* do and do not say. Their neighbours in the same table are
explicit about units — `PayDayFrequencySeconds`, `ImpExperienceGainPerSecond`,
`HealthDecreaseWhenOnFirePerSecond`. None of the ten work deltas is. That silence
was the whole of Q19, and it is now answered from the other end: **they are per
second, and an application is a fifth of one.** See "How much one swing does"
below - the answer came from a measurement in the original rather than from the
names, which is why it took until 2026-08-30.

## The rule

One application of work:

1. adds the kind's delta to the tile's health;
2. at or below zero the tile becomes its `destroyedTypeTerrainId`;
3. at or above `maxHealth` it clamps, and — **for upward work only** — becomes
   its `maxHealthTypeTerrainId`;
4. the new tile starts at its own terrain's `startingHealth`, and keeps its dig
   markers only if the new terrain is still `Taggable`.

Step 4's second half was refined once the first worker existed (2026-08-29). The
markers used to be cleared on every conversion, which is right for rock becoming
floor and wrong for a gem face, whose destroyed type is itself: mining a marked
gem face collapsed it, cleared the mark, and left the imp with nothing to do,
where the original mines a marked gem face indefinitely from a single mark.
STRONGLY INFERRED, on the same flag the mark is validated against. See
`imp-tasks.md`.

Ownership is written only when upward work converts a tile into an ownable
type. It is never cleared: DK2's map data gives every tile an owner id including
solid rock, and which id means "nobody" is UNKNOWN (Q17), so leaving the byte
alone lets the level file's own neutral id propagate through a dug-out tile
instead of us inventing one.

### Why the upward conversion is a property of the work

The obvious reading — "a tile at full health becomes its max-health type" —
cannot be right. Claimed Path ships at 660 out of 660 with a max-health type of
Rock. Under a level-triggered rule, every claimed floor tile in every level
would turn to rock on the first tick. Six terrain types have this shape.

Making the conversion belong to the work instead resolves it, and each of the
three upward jobs lands exactly where the data points:

| Terrain | Upward job available to it | `->maxHp` | Result |
|---|---|---|---|
| Rock | Reinforce | 9 | Reinforced Wall |
| Dirt Path | Claim | 8 | Claimed Path |
| Claimed Path | Fill in | 2 | Rock |
| Mana Vault | Claim | 32 | Claimed Vault |

That table is also the answer to **Q18**. There is no second meaning and no
second field: `maxHealthTypeTerrainId` is the destination of whichever upward
job that terrain admits, and which job that is follows from the terrain's own
flags. The `FillInable` flag is set on exactly five types — Dirt Path, Claimed
Path, Mana Vault, Claimed Vault and Hero Lair — all of them floor, which is what
"fill in" would have to mean.

### Why gems are inexhaustible

Gems are impenetrable and their `destroyedTypeTerrainId` is *themselves*. A gem
face at 100 health takes two swings of 60 (Q47), collapses, and immediately
reappears at full health. The separate `GoldMinedFromGems` variable
(25) is what each of those cycles yields, against a seam's 3000. The data
therefore reproduces the thing every DK2 player knows — gems never run out and
are far slower than a seam — without anything being special-cased for them.

---

## How much one swing does — the measurement that settled it

**CONFIRMED (2026-08-30, owner measurement of the original): an imp mining a
gold seam frees 273 gold a swing.**

One number, and it propagates through the whole table:

```
a gold seam        660 health, 3000 gold  (terrain 6, shipped)
observed           273 gold a swing
  => health a swing = 273 * 660 / 3000 = 60.06, i.e. 60
  => swings a seam  = 660 / 60          = 11
  => MineGoldHealth = -300, and -300 / -60 = 5
  => work lands FIVE times a second, a fifth of the tabled delta each
```

Two things corroborate it that were not looked for, which is what makes this
CONFIRMED rather than a fit:

**Eight of the ten tabled deltas divide by five exactly** — 825, −140, 125,
−300, −5000, −130, 5, 50. Chance would give two. And no other small rate comes
close: **825 is odd**, which on its own kills the "twice a second" reading that
this replaces, and kills four and ten with it.

| rate | deltas that divide exactly |
|---|---|
| 2 | −140, −300, −5000, −130, −8, 50, 22 (not 825, not 125, not 5) |
| 4 | −140, −300, −5000, −8 |
| **5** | **825, −140, 125, −300, −5000, −130, 5, 50** |
| 10 | −140, −300, −5000, −130, 50 |

**Claiming lands on a count observed a day earlier, from a different mechanic.**
`ClaimTileHealth` 825 at a fifth is 165 a swing against a Dirt Path running
1→660: **four swings**. Q46, measured by watching an imp jump, said "2-3-4 jumps
MAX".

### What it changed, and what it did not

Times in seconds are **unchanged** — a fifth of the delta five times a second is
the delta per second. What changed is *when* a tile flips and how many swings it
takes:

| job | was | now |
|---|---|---|
| claim a dirt path | 1 | **4** |
| mine out a seam | 3 | **11** |
| reinforce rock | 10 | **50** |
| attack claimed floor | 5 | **24** |
| dig your own wall | 7 | **35** |
| dig an enemy's wall | 113 | **563** |

And one consequence nobody designed: an Imp's `maxGoldHeld` of 1400 is **five
swings** of 273 (1363), so a seam is three trips — 1363, 1364, 273 — which is
what an imp shuttling to and from a rock face looks like in the original. That
answers Q50, which had asked where an imp stops mining, by arithmetic rather
than by the rule that had been guessed for it.

Implemented as `SimulationTiming.WorkApplicationsPerSecond` and
`TileWorkRules.PerApplication` (ADR-0020). Two deltas do not divide by five —
`DigEnemyWallHealth` (−8) and `RepairWallHealth` (22) — so `TileState.WorkRemainder`
carries the fraction between applications; without it an enemy wall would take
900 swings instead of 563 and run 37% slow forever.

**This is not a measurement of the tick rate.** It fixes the ratio between an
application and a tabled per-second delta. How long five applications take in
wall-clock terms is still Q1.

---

## What does not fit, and is implemented anyway

### Q20 — DigRockHealth is 30× the health of the rock it digs

Rock has 165 health. `DigRockHealth` is −5000. One application destroys it with
4835 to spare, so under any reading where an application is a discrete event,
digging earth is instantaneous. It is not, in the original: a single imp takes
a few seconds.

**It is now the only thing that does not fit, and that is new.** Until 2026-08-30
this was one suspect among several: every rate ran too fast against the original,
and it was reasonable to wonder whether the model was wrong rather than one
number. The 273-gold measurement settled the granularity for the whole table at a
stroke — five applications a second, a fifth of the delta each — and every other
rate fell into place. Rock did not move at all: at −1000 a swing against 165
health one application still ends it, where the owner observed 2–3 hits over 1–2
seconds. So `DigRockHealth` is wrong and **nothing else is wrong at all**.

**By how much, precisely.** The owner settled the count on 2026-08-30, and the
distinction they drew is the useful part: **two swings do the damage**, and a
third animation plays over the block already crumbling. So it is 2, not the
"2-3" a casual count gives.

Two damaging swings against 165 health bounds the per-swing damage `d` from both
sides: `d < 165`, or one swing would finish it, and `2d >= 165`, or two would
not. So `d` is in **[83, 164]**, and at five swings a second the per-second field
should read **415 to 820**. It reads **5000** - **6x to 12x too big**.

**500 is the only round number in that window, and it is exactly 5000/10.** Two
things support it beyond its roundness. `d = 100` gives 165 -> 65 -> destroyed,
which is two swings with no coincidence required. And **every other tile delta in
the table is in the tens or low hundreds** - 825, 125, -300, -130, -8, 5, 50, 22
- so -5000 is the sole four-digit one and -500 would put it back inside the
family it belongs to.

**It is not a different variable.** The balance table was scanned for anything
holding a value in [415, 820] that could plausibly be a dig rate: the six hits
are two payday intervals, a boulder fear amount, a tremor damage, a music
threshold and the mana gain cap. None is a candidate. `DigRockHealth` is the
field, and its shipped value does not produce the shipped behaviour.

**And the model around it is confirmed, which is what isolates this.** The same
five-swings-a-second reading gives a gold seam 273 gold a swing, which is what
the owner measured; it gives claiming four swings, which is what the owner
observed. It is right everywhere it has been checked and wrong here.

**This leaves an open decision, not just an open question.** We currently
implement the field as shipped, so a plain wall here falls in **one** swing where
DK2 takes **two** - a known, measured behavioural deviation. Correcting it means
writing a number the file does not contain, which this project treats as a bug.
See Q20 and `HANDOFF.md`; the owner's call.


The first candidate explanation below is therefore now dead, which is worth
recording because it was the most attractive one:

- ~~an application is much shorter than a second, and the other rates are simply
  slow~~ — **refuted.** An application *is* much shorter than a second, it is a
  fifth of one, and that made every other rate right and left this one exactly
  where it was.

The two that survive:

- a worker contributes a fraction of the delta, scaled by a per-creature work
  rate (`DwarfDiggingMultiplier` = 184 shows that a multiplier concept exists,
  and `f3AF_workModifier` / `f3B3_workEnhancement` are named in Flame — Q6);
- `DigRockHealth` applies to something other than terrain 2, and ordinary earth
  is dug by a variable we have not identified.

The second has got more attractive by elimination: a single number that is wrong
by a hundred, in a table where the other nine are now confirmed correct to the
swing, looks less like a balance value and more like a variable we have
misidentified.

The implementation follows the data exactly and the tests assert the
one-application result, because a number invented to make digging *feel* right
would be worse than a faithful one that is visibly wrong. This is written down
so nobody later mistakes it for a bug in the rules.

### Q21 — how a seam divides its gold — **answered**

Proportional to the rock removed, and no longer an inference: the owner's 273
gold a swing *is* the proportional rule read backwards, since
273 × 660 / 3000 = 60 is exactly the health a swing takes. The alternative — the
whole 3000 in a lump on the final application — is dead, because it would give a
swing 0 gold and one swing 3000.

What did change is *how* the proportion is taken. Truncating each swing on its
own loses a coin a swing, and across eleven swings a seam would yield 2992 of its
stated 3000. Gold is now the difference of two running totals — how much has come
out by the time the seam is down to this health, minus how much had at the last
swing — which makes a fully mined seam yield **exactly** 3000, needs no state to
do it, and reproduces the measurement: 273 on eight of the eleven swings and 272
on the other three.

The earlier note here said the shortfall was "DK2's own truncation, kept rather
than rounded away". That was a guess dressed as fidelity: nothing had been
measured, and the honest reading of a seam being worth exactly what the record
says it is worth is that it yields exactly that.

### Q22 — fill-in's accounting, and why it stopped mattering

`FillInHealth` is 5, which is small enough to suggest many applications, but the
tiles it applies to are already at maximum health. Under the uniform rule one
application converts them. Either fill-in restarts the health accounting or the
tile is damaged first. UNKNOWN; the uniform rule is what ships, tagged
HYPOTHESIS.

**Downgraded to a curiosity (2026-09-04).** The owner, unprompted: *"once a tile
is dug out, and maybe claimed, there is no possible way to fill in that tile to
rock again. DK2 doesn't have a game mechanic for filling up dug out tiles."*
CONFIRMED. So this accounting question is about a transition no keeper can
trigger.

The data is not wrong and has not been changed — `FillInHealth`, the `FillInable`
flag on five terrain types, and Claimed Path's route back to Rock are three
coordinated pieces of shipped data, which is not an accident. The reading is that
the engine carries the concept and the shipped game exposes no way to reach it.
What changed is the status of the *behaviour*: `ImpTaskSystem` never chooses
fill-in, and `DugOutTilesStayDugOutTests` now asserts both halves — that no tile
ever goes from open back to solid over a long dig, and that no worker ever queues
the job.

**Two of the three candidate explanations were then closed** (2026-09-04, at the
owner's request). **OpenKeeper carries both fields and uses neither** —
`FILL_INABLE(0x00008000)` and `FILL_IN_HEALTH(11)` are each declared once and
referenced nowhere in its source, and its worker task list has no fill-in task.
And the level script turned out to close tiles by a **different** mechanism:
`ALTER_TERRAIN_TYPE`, a direct terrain write with no health and no rate. Sweeping
all 66 shipped maps (`dk2tools kwd alterterrain`), 94 of its 160 uses go the other
way — `Impenetrable Rock → Dirt Path`, opening sealed passages — and **exactly two
turn an open tile solid**, both in `Secret3`, both Claimed Path → Reinforced Wall.

So the fill-in *health* mechanic has no user anywhere. Level editor or cut
feature; Q74 keeps that half open. **M6 note:** `ALTER_TERRAIN_TYPE` must be able
to write solid terrain onto an open tile, so the invariant above is about workers
and not about the engine. Full argument: `manual-1999.md`.

---

## Tagging

A dig marker is state on the tile, one bit per player. Per-player rather than
global is STRONGLY INFERRED rather than confirmed: DK2 is a multiplayer game in
which each keeper marks and sees their own dig orders, and the terrain table
carries a per-type `Taggable` flag rather than a shared list. The bitmask is our
representation, not a claim about the original's storage.

Marking is refused on terrain that is not `Taggable`. Unmarking is always
allowed — terrain can change under a marker, and a player has to be able to
clear one that is no longer diggable.

`TagTilesCommand` takes an explicit list of tiles and an explicit tag/untag
intent. The original's interface is a drag-toggle whose exact rule (does a drag
beginning on a marked tile unmark everything it touches?) is UNKNOWN and belongs
to the presentation layer, not to the simulation.

## Who does the work

An imp, since 2026-08-29 — for marked work only. `TileWorkSystem` still drains a
queue that something else fills; the something is now
`DK2.Simulation.Systems.ImpTaskSystem`, which finds the nearest marked tile its
keeper can reach, walks a worker to it and queues one application per work
period. Everything about how it chooses, routes and times that is in
`imp-tasks.md`, and much of it is inferred rather than known.

Tests and the scenario harness can still queue work directly, and that remains
the only way to exercise the kinds a worker does not do on its own — claim,
reinforce, repair, attack, fill in. Every scenario run that injects work says
exactly that under "Not answered by this run", and so does every run that places
a worker.

Gold released by mining goes into the hands of the creature that swung, and that
creature carries it to a treasury or dungeon-heart tile (Q31, ADR-0019). The
rules themselves stay pure: `TileWorkResult.GoldReleased` credits nobody, and it
is `TileWorkSystem` that reads the creature id off the order and fills the right
pair of hands. Work with nothing behind it - a test, the harness - still has its
gold reported and dropped, because nobody was there to pick it up. What an imp
cannot hold is lost rather than left on the floor, because a lump is an object
(M6); where the imp decides to stop mining is Q50.

A tile can also *lose* gold. Room terrain is ownable floor, so a rival can attack
a treasury down to nothing; when it converts to claimed path — which holds no
gold — the amount is reported as `GoldSpilled` and emitted as `gold_spilled`. In
the original that gold stays on the floor of the tile it came off, as an object
— which is what `TileWorkSystem` now does with it (gold-on-the-floor.md §4). It
comes off the keeper's total and stays in the world.

---

## How this was established

Everything here came from the data, in the order the research method prescribes
— nothing needed a disassembler.

- The `maxHealth` column had not been extracted before this milestone; adding it
  to `dk2tools kwd terrain` is what exposed the Claimed Path contradiction and
  therefore forced the work-triggered rule.
- `dk2tools kwd terrainflags` (added here) prints the full flag set. The
  `FillInable` flag falling on exactly the five floor types is what turned the
  fill-in reading from a guess into an argument.
- The tile rules are written against terrain *flags*, never terrain ids. A rule
  written against id 2 would be a rule about the reference install rather than
  about DK2; `DK2.Behavior.Tests.TileWorkOnRealDataTests` re-runs the rules
  against the real database to keep that honest, and asserts that the terrain
  transition graph terminates for every tile of a real campaign level.
