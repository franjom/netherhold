# Anger: the ladder, and the one number that blocks it

**The mechanic is not implemented.** The ladder is CONFIRMED by the owner, every
per-creature rate is in the shipped data, and one number is missing without
which none of it can be turned into seconds. This document is the case for
waiting.

## The ladder — CONFIRMED (owner)

Two separate confirmations, from two different causes, on two different days:

> **Q9** (2026-08-29, on pay): *"Unpaid → unhappy → angry → leaves; a converted
> creature rebels instead."*
>
> **Q105** (2026-09-04, on lairs): *"If a creature can't get a lair it becomes
> unhappy, it can't go to sleep, then angry, then it leaves the dungeon."*

So the ladder is **content → unhappy → angry → leaves**, it is driven by unmet
needs, and at least two needs feed it. The second answer also settles something
that had been implemented as a guess: **a creature with no bed does not sleep on
the floor. It cannot sleep at all** — which is what `CreatureSleepSystem`
already does, so ADR-0049 was right there by luck rather than by evidence, and
is now right on purpose.

## What the data gives, all of it

**Per creature** (`CreatureDef`, all six now correctly signed — see below):

| Field | Goblin | Bile Demon | Warlock | Imp | Skeleton | Jack |
|---|---|---|---|---|---|---|
| `UnhappyThreshold` | 50 | 50 | 50 | 50 | 50 | 50 |
| `AngerNoLair` | 100 | 150 | 100 | 1 | 0 | 120 |
| `AngerNoFood` | 100 | 150 | 100 | 1 | 0 | 110 |
| `AngerNoPay` | 60 | 60 | 60 | 1 | 0 | 60 |
| `AngerNoWork` | 50 | 50 | 70 | 1 | 0 | 50 |
| `AngerSlap` | **+3000** | +3000 | +7000 | 1 | 0 | **−5000** |
| `AngerInHand` | **−250** | −300 | −500 | 0 | 0 | **+500** |

**Every creature shares `UnhappyThreshold` = 50**, across all 46. That is what
makes it a *percentage* rather than an absolute.

**From the balance table:**

| Variable | Value |
|---|---|
| `ModifyAngerOfCreatureInLairPerSecond` | **−550** |
| `ModifyAngerInCompanyOfHatedCreaturesPerSecond` | +150 |
| `ModifyCreatureAngerWhilePrayingPerSecond` | −275 |
| `ModifyAngerWhileSolitaryInPitPerSecond` | +275 |
| `ModifyAngerOfPitSpectatorPerSecond` | −275 |
| `ModifyAngerOfCombatPitVictor` | −16400 |
| `GamblingModifyCreatureAngerSmilesPerSecond` | −2000 |
| `GamblingModifyCreatureAngerMoneyPerSecond` | +100 |
| `RebelLeavingPercentage` / `RebelBecomeGoodPercentage` / `RebelDefectPercentage` | 70 / 15 / 15 |

Those last three sum to 100 and are what a *rebelling* creature does — the
"converted creature rebels instead" half of Q9.

Each creature also carries **three weighted job tables** — happy, unhappy,
angry — so the ladder is not only a countdown to leaving, it changes what a
creature chooses to do. A Troll's unhappy table is `Sulk(10%), Drink(90%)`; a
Goblin's is `Drink(100%)`. That is the `SulkState.cpp` the executable names.

## The signedness bug this found — FIXED

`AngerSlap` and `AngerInHand` were parsed as `u16`, and
`docs/research/kwd-creatures.md` documented the whole run of six as `u16`. Read
that way a Goblin's `AngerInHand` is **65286** and Jack's `AngerSlap` is
**60536** — which are **−250** and **−5000** in a signed 16-bit field.

Signed, the table becomes coherent in a way it simply was not before:

- **Slapping angers** — +3000 for a Goblin, +7000 for a Warlock, +8000 for
  Horny.
- **Being picked up calms** — −250 for a Goblin, −500 for a Warlock. The Hand of
  Evil is soothing.
- **Jack is the exact inverse**: slapping calms it (−5000), being held annoys it
  (+500). Of a piece with its other oddities — 240 health and a chicken worth
  250.

All six are now `short`. The first four are small positives on every shipped
creature (0..150), so signed reads them identically and is strictly safer.
Fixed under ADR-0050 and pinned by `TheAngerFieldsAreSignedAndTheLastTwoAreWhy`.

**Had anger been built before this was noticed, a slap would have added 3000 and
being picked up would have added 65286** — instantly maxing a creature's anger
every time a player touched it.

## Q106 — the blocking unknown: what is anger measured in?

`UnhappyThreshold` is 50, and 50 **percent of what?** There is no anger ceiling
anywhere:

- Not in the balance table. All 214 variables were read; there is no
  `MaxAnger`, no `AngerCeiling`, no `AnnoyanceMax`.
- Not in the creature record.
- **Not in OpenKeeper.** `CreatureMoodSystem` is a stub — an empty `processTick`
  with a `TODO: Currently a stub, collection of modifiers we know we need`. This
  is the first mechanic this project has reached where OpenKeeper has nothing to
  offer at all.

Without the ceiling, the per-second rates cannot be turned into a duration.
Two candidate scales, both self-consistent and hours apart:

| Ceiling | No-lair Goblin becomes unhappy | …and leaves | One slap is |
|---|---|---|---|
| 65535 (a u16) | ~5.5 minutes | ~11 minutes | 4.6% |
| 10000 | ~50 seconds | ~100 seconds | 30% |

Neither is absurd. The second matches the feel of slapping — three slaps to make
a creature visibly cross — and the first matches "a dungeon with no lair limps
along for a while". **A ceiling picked wrong makes every timing in creature mood
wrong**, and mood then feeds job selection, desertion and the whole M4 loop.

### Why this is not being built yet

This is the same shape as **Q47**, and the project has already learned that
lesson once at a cost worth not repeating. Q47 had a plausible answer —
"applications twice a second, half the tabled delta" — that fitted every
observed range and was self-consistent and **wrong**. It was left unbuilt on the
grounds that ranges are not counts. One measured number later the answer was
five a second, and *not having built it made the correction free*.

Anger is that again: a mechanic whose every rate is known and whose unit is not.

### The measurement that settles it

**One stopwatch, and it needs no precision.** Start a level, get a creature, and
give it nowhere to sleep — no lair at all, or a lair already full. Then note:

1. **How long until it looks unhappy** (the sulking/drinking behaviour, or the
   mood indicator).
2. **How long until it walks out.**

Either number pins the ceiling, because the rate is known: a Goblin accrues
`AngerNoLair` = 100 a second. If it leaves after about two minutes the ceiling
is ~10000; if it takes ten, it is ~65535.

**A second, cheaper cross-check:** how many slaps does it take to make a content
creature angry? A slap is +3000, so three or four slaps means a ceiling near
10000, and twenty means 65535.

## What is implemented today, and what it costs

Nothing of anger. The visible consequences, all recorded rather than hidden:

- A creature with no bed stays awake for ever and never recovers from a wound
  (`AWoundedGoblinWithNoLairStaysAwakeAndStaysWounded`). In DK2 it would get
  unhappy and then leave.
- A hungry creature that cannot eat is unharmed for ever
  (`BeingHungryCostsNoHealthAtAll`). In DK2 it would get angry — `AngerNoFood`
  is 100 for a Goblin, joint-largest with `AngerNoLair`.
- Nothing can be slapped or picked up, so the two largest single anger sources
  have no trigger anyway.

The last of those is worth noting as a sequencing point: **anger has no player
input to respond to until picking up and dropping exist**, and those are the
other missing half of M4.
