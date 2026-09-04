# Creature levels: what a level does, and what raises one

A level multiplies a creature's stats by percentages the shipped data states
outright. **The multiplying is implemented; gaining a level is not**, and the
reason is a unit ambiguity in the data rather than a missing mechanic.

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

`CreatureLevelRules`, plus `CreatureState.Level` (hashed, snapshotted,
`HashVersion` 15 / `FormatVersion` 15).

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

## 4. What is NOT implemented, and why (Q124)

**Nothing raises a level.** The rate's unit is ambiguous, and guessing it would
put an invented number inside a determinism hash:

| Source | Value |
|---|---|
| Creature record, the field this project calls `ExpPerSecond` | **1200** for an Imp, 200 for a Goblin |
| Balance table, `ImpExperienceGainPerSecond` | **20** |

**20 x 60 = 1200.** So one of the two is per *minute* and the two names cannot
both be right. The consequences are three orders apart:

- at 1200 a second an imp reaches level 2 in **two and a half seconds**;
- at 20 a second, in **two and a half minutes**.

The second is plausible and the first is absurd, which is a reason to suspect
the record is per minute — but "one of these is absurd" is not evidence about
which field is misnamed, and the record's 200 for a Goblin does not divide as
cleanly (200 / 60 = 3.33 a second).

**How to settle it:** the owner times a fresh imp from level 1 to level 2. Two
and a half minutes confirms the global's 20 a second; anything near instant
confirms the record. This is on the observation list as **Q124**.

Also unimplemented, and separate: the two room caps are read by nothing —
`TrainingRoomMaxExperienceLevel` is **4** and `CombatPitMaxExperienceLevel` is
**8**, so neither room takes a creature to 10 and something else must (combat,
presumably).

## 5. One reading we had to choose

`ExperienceForNextLevel` is indexed by the creature's **current** level. Under
that reading level 1 gets the implicit 100% and the first level costs exactly
the record's 3000, which is the natural meaning of a field called *experience for
next level*. Indexed by the **target** level instead, the first level would cost
3900 and the record's own number would appear nowhere.

Nothing reads it yet, so the choice costs nothing today. It is asserted in a test
so that whoever implements levelling knows a choice was made rather than
inheriting it silently.

## 6. What this unblocks

- **Vampire reincarnation** (Q113, ADR-0051). The cost of coming back is a
  level, and a level is now a known quantity. Still needs the Graveyard.
- **Combat.** Melee damage scales to 600%, so a combat system written against
  the record value alone would be wrong for every creature above level 1.
- **Q118's water arithmetic.** `ShuffleSpeed` has **no row in the table**, so if
  water runs a creature at shuffle speed the slowdown *deepens* with level:
  45.7% slower at level 1, 48.2% at level 2, 78% at level 10. The owner
  eyeballed 50-60% on a level-2 imp. That makes the level slope the
  distinguishing test rather than the absolute figure.
