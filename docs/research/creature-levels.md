# Creature levels: what a level does, and what raises one

A level multiplies a creature's stats by percentages the shipped data states
outright. **Both halves are implemented for workers**: an imp earns experience by
working and gets faster and tougher as it levels. Nothing else earns any, because
training and fighting do not exist.

Evidence classes per ADR-0046 rule 4: **shipped data** (the whole table),
**owner observation** (that it happens at all), **OpenKeeper** (the stat names,
under ADR-0003's second amendment), **the binary** (that a level is per-creature
state).

---

## 1. How this was found, which is the point of it

The owner, while trying to time an imp crossing water for Q118:

> *"imps gain speed as they gain more levels, I will try to time one but I have
> only level 2 as the lowest level imps now in the game"*

That was a caveat about a measurement, not a research answer. Acting on it found
that `GlobalVariables.kwd` carries a **32-stat by 9-level table of percentages**,
that `KwdVariables` has been parsing it into `GlobalVariables.CreatureStats`
since variables were first read, and that **nothing in the project had ever
looked at it** — 320 rows, parsed and ignored, while every stat read in the
simulation was silently the level-1 value.

The binary agrees a level is per-creature: `Creature.cpp`'s debug dump prints
`Level` and `ExperiencePoints` as instance fields.

## 2. The table

One row per (stat, level) for levels **2 to 10**. There is no level-1 row: level
1 *is* the creature record's own value, so `GameDatabase.StatPercent` returns an
implicit 100%. Values are a percentage of that record value — **not** compounded
per level, which is why levelling up and down cannot drift.

| Stat | L2 | L3 | L4 | L5 | L6 | L7 | L8 | L9 | L10 |
|---|---|---|---|---|---|---|---|---|---|
| **Health** | 125 | 150 | 175 | 200 | 225 | 250 | 300 | 400 | **600** |
| **Melee damage** | 125 | 150 | 175 | 200 | 225 | 250 | 300 | 400 | **600** |
| Fear / Threat | 125 | 150 | 175 | 200 | 225 | 250 | 300 | 400 | 500 |
| **Speed / run speed** | 105 | 110 | 115 | 120 | 125 | 150 | 175 | 200 | **250** |
| Pay | 105 | 110 | 115 | 120 | 130 | 140 | 150 | 180 | 250 |
| Research / manufacture | 125 | 175 | 250 | 300 | 350 | 400 | 450 | 550 | 700 |
| **Experience for next level** | 130 | 180 | 250 | 350 | 470 | 600 | 720 | 850 | **1000** |
| Torture time to convert | 105 | 110 | 115 | 120 | 130 | 140 | 150 | 200 | 300 |

**A worker's work rate is NOT one of the levelled stats** (owner, 2026-09-05):
a level-3 imp and a freshly created level-1 imp reinforce the same wall in
13.1 s and 13.3-13.4 s. The prediction that it *would* scale came from reading
the level-up routine as adding a level-scaled stat to the work-rate field, and
the stopwatch refused it on a 13-second job that could not have hidden a 25%
change. See `tile-work.md`, "The work rate".

**Ten of the 32 stats are 100% at every level** — own-land regeneration among
them, so an Imp heals at 67 a second at level 1 and at level 10. "Levels scale
everything" would be wrong about a third of the table, which is the same shape
as mana (three terrains of 41), own-land regeneration (two creatures of 46) and
terrain health deltas (three of 41). *Read the whole column before believing a
rule is general.*

**Stat names are OpenKeeper's** `Variable.CreatureStats.StatType`, and every one
this project uses is corroborated by the shape of the shipped column: the things
called Health and MeleeDamage rise to 600%, the things called Speed and RunSpeed
rise to 250% together, and the thing called ExperienceForNextLevel rises to
1000%. That is what a levelling curve looks like.

### A quirk worth keeping

Stats **10, 11 and 14** read 100% at every level *except* **level 4**, where they
drop to **25%, 20% and 20%**. A three-field dip at one level looks like a
data-entry accident. It is recorded rather than smoothed: this project already
deliberately disbelieves one shipped value (ADR-0023) and the rule there was to
prove the correction from behaviour, which nobody has done for these.

## 3. What is implemented

`CreatureLevelRules`, plus `CreatureState.Level` and `ExperiencePoints` (both
hashed and snapshotted; `HashVersion` 16 / `FormatVersion` 16).

- **Maximum health** is the record's `Hp` scaled by the Health row.
  `CreatureHealthRules.MaxHealth` used to return `creature.Hp` flat with a
  comment saying *"until levels exist"*; it now takes the database and the
  state. The old signature was **removed rather than kept as an overload**,
  because a call that forgot the level would silently cap a level-10 Goblin at
  1500 health instead of 9000.
- **Walking speed** is the record's `Speed` scaled by the Speed row, applied in
  `CreatureMovementSystem`. A level-2 imp covers **7912** raw units a second
  against its record's **7536** — 1.93 tiles a second against 1.84, which is
  what the owner saw on screen.
- Run speed and the experience threshold are computed and unread.

Every creature spawns at level 1, and level 1 scales to exactly 100%, so the
pinned determinism hash moved only because the field joined the hash — the
arithmetic is a proven no-op at level 1, which is what
`DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines` now also guards.

## 4. What raises a level — ANSWERED (Q124)

The owner, unprompted, an hour after the scaling landed:

> *"imps get experience by working, while they do their usual things around the
> dungeon, digging, reinforcing walls, mining gold, etc. they get exp and
> passively their level raises. creatures also gain levels through experience,
> mostly training and fighting. creatures can get up to level 4 using the
> training room, up to level 8 using the combat pit, and to get to level 10 they
> need to fight actual fights."*

**Two of those caps are shipped variables, to the number:**

| The owner said | The balance table says |
|---|---|
| training room takes a creature to **level 4** | `TrainingRoomMaxExperienceLevel` = **4** |
| combat pit takes it to **level 8** | `CombatPitMaxExperienceLevel` = **8** |

That is the third time an owner statement and a variable name have met outright,
and it is why the third clause — level 10 needs real fights — is taken as read:
the two claims that *could* be checked both checked out, and the two variables
had been sitting unread with nothing to say what they capped.

### And it looked like it resolved the unit. IT DID NOT — RETRACTED 2026-09-07

**What this section said for three days, and it was wrong:**

> The problem was that the creature record's field this project calls
> `ExpPerSecond` reads **1200** for an Imp against a balance-table
> `ImpExperienceGainPerSecond` of **20**, and 20 x 60 = 1200, so one of the two
> had to be per minute with no way to tell which. [...] the record's 1200 is
> **the same rate expressed per minute**. Two independent fields agreeing on one
> creature's rate promotes that from a coincidence to a reading (STRONGLY
> INFERRED).

**Both fields are per second, the 1200 was a coincidence, and the two numbers
are not about the same mechanic at all** (Q205, `training.md`):

- **`ExpPerSecond` is COMBAT experience.** Its only reader is the per-tick update
  hook of state **category 22** — battle, fleeing, responding to a threat. Which
  is the owner's own third clause, *"to get to level 10 they need to fight actual
  fights"*, in the code. Nothing puts a **worker** into a category-22 state, so
  the Imp's 1200 is **unreachable**.
- **The loader divides it by variable 19 as it stores it**, flooring at 1 — the
  same conversion `angerNoLair` and its siblings go through. That is what a
  per-second number becoming a per-tick one looks like, and it is the positive
  evidence the per-minute reading never had.
- **`ImpExperienceGainPerSecond` is not divided** and is not per second either:
  it is added **per tick of work**, so 80 a game second (Q79).
- And **41 of the 46 shipped creatures have `ExpPerSecond` exactly 5 ×
  `ExpPerSecondTraining`** — the Imp is one of the five that do not, at forty
  times. So the record whose number started this was the wrong record to reason
  from twice over.

**Why the mistake was tempting, and the lesson.** "Two independent fields
agreeing on one creature's rate" is a good argument when the fields are about the
same thing. These were not: one is a global rate for working, the other a record
rate for fighting, and 60 is a plausible-looking factor between any two rates.
**Before treating two matching numbers as corroboration, check that the engine
ever puts them in the same place** — the standing note this project already
keeps, earning its keep for the third time.

### What is implemented

`CreatureExperienceSystem`, once a game second, registered last so a creature is
paid for a second it actually worked:

- **A worker earns `ImpExperienceGainPerSecond` (20) per ORIGINAL TICK while its
  activity is `Working`** — digging, claiming, reinforcing, mining — so **80 a
  game second** (Q79, corrected 2026-09-07; this said "a second" and therefore
  ran at a quarter speed). An **idle imp earns nothing**, which is the half of
  the owner's sentence that makes it a rule.
- **About 37 seconds of work buys level 2** (3000 / 80), and roughly **29 minutes
  of continuous work** buys level 10. Both numbers were four times too large
  here until the rate was read.
- **A creature that is not a worker earns by TRAINING** (ADR-0083): its record's
  `ExpPerSecondTraining` divided by variable 19, per original tick, capped at
  `TrainingRoomMaxExperienceLevel` and billed to the keeper at five gold a game
  second. **Fighting still does not exist**, so nothing reaches level 5 and a
  test asserts the ceiling rather than leaving it implicit.
- Levelling **stops at 10**, because the stat table stops there. The owner's two
  room caps are lower and belong to the *rooms*, so they arrive with the rooms.

`CreatureState.ExperiencePoints` is hashed and snapshotted (`HashVersion` 16,
`FormatVersion` 16). This is the first hashed field whose value actually *moves*
in the pinned determinism run: the fixture's working imps bank 20 a second, so
500 ticks is 500 experience — not enough for a level, enough to move the hash.

### Two readings that had to be chosen (Q127) — AND BOTH WERE WRONG

Read out of DK2's adder on 2026-09-07 (`training.md` §7). What this section
chose, against what the original does:

| Chosen here | DK2 |
|---|---|
| **The threshold is spent**, so the balance drops to the remainder | **The balance is ZEROED.** The overshoot is discarded |
| **A promotion loops**, so a big award buys two levels | **One level per call**, however large the award |

The argument written down for the second one was that *"a single-step version
would silently drop levels the day combat pays out in lumps."* It does drop them.
That is the game.

Two clauses that were never guessed at and are now known:

- **`ExpForNextLevel` of zero refuses everything**, losses included. The field is
  not merely the price of a level, it is the switch that makes a creature capable
  of having experience.
- **A loss larger than the balance is a no-op**, not a clamp to zero.

**And the discard makes the cadence load-bearing**, which is why both awards are
now paid on the original's four-a-second tick rather than once a game second:
four awards of ten and one of forty leave different numbers behind on any second
that crosses a threshold.

## 5. One reading we had to choose — and it was right (CONFIRMED 2026-09-07)

`ExperienceForNextLevel` is indexed by the creature's **current** level. Under
that reading level 1 gets the implicit 100% and the first level costs exactly
the record's 3000, which is the natural meaning of a field called *experience for
next level*. Indexed by the **target** level instead, the first level would cost
3900 and the record's own number would appear nowhere.

**DK2's adder settles it**: it indexes the column with the creature's current
level byte and uses the record's raw value when that level is 1. So this is now
a fact rather than a choice — the one of the three Q124/Q127 readings this
project got right, against two it got wrong. Worth keeping the reasoning that
led here, because the reasoning was the same in all three cases and only the
answer differed: **a plausible reading is not evidence, and the only way to tell
which of your plausible readings are the good ones is to read the code.**

## 6. What this unblocks

- **Vampire reincarnation** (Q113, ADR-0051). The cost of coming back is a
  level, levels are now a known quantity **and creatures can hold one above 1**.
  Still needs the Graveyard, and still needs a vampire, which needs corpses (M6).
- **Combat.** Melee damage scales to 600%, so a combat system written against
  the record value alone would be wrong for every creature above level 1.
- **Q118's water arithmetic.** `ShuffleSpeed` has **no row in the table**, so if
  water runs a creature at shuffle speed the slowdown *deepens* with level:
  45.7% slower at level 1, 48.2% at level 2, 78% at level 10. The owner
  eyeballed 50-60% on a level-2 imp. That makes the level slope the
  distinguishing test rather than the absolute figure.
