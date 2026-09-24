# Mechanic: anger — six counters, the ceiling, and the ladder

Source(s):
- Owner measurement in DK2 v1.70, 2026-09-04 (Q106, Q114) and owner testimony
  2026-08-29 / 2026-09-04 (Q9, Q105, Q115, Q116)
- The reference install's `Creatures.kwd` and `GlobalVariables.kwd`, via `DK2.Data`
- `DKII.EXE` v1.70: the anger wrapper `FUN_00478b80`, the adder `FUN_00487030`,
  the three job choosers, and the category update hooks, read 2026-09-05 to
  2026-09-07; the adder and its percentage sibling re-read 2026-09-23
- OpenKeeper `CreatureMoodSystem` / `CreaturesController`

Original version: DK2 v1.70

Confidence: **CONFIRMED** — the ceiling from two independent owner measurements,
the six counters and the choosers from the binary.

## The two measurements

**One — a stopwatch on a rate.** A Goblin with no lair, everything else
provided, so `AngerNoLair` = 100 a second was the only source running:

| Observed | Anger accrued | Against |
|---|---|---|
| unhappy at ~2:45 (165 s) | 16 500 | **16 383** = 32767 / 2 |
| angry at ~5:30 (330 s) | 33 000 | **32 767** = `short.MaxValue` |

**The 2:1 ratio is the stronger half**, because it pins the structure without
needing the ceiling at all: anger accrues **linearly**, the *angry* threshold is
**100%** of the ceiling — a number in none of the 214 variables — and
`UnhappyThreshold` = 50 really is a percentage. It also refutes 10000 outright,
which would predict 50 s and 100 s.

**Two — a slap count, which closed the gap timing could not.** Duration cannot
separate *(100/s into 32767)* from *(200/s into 65535)*: doubling both preserves
every time. That is **Q47's trap exactly**, so the discriminator had to be a
*discrete* delta, which scales with the ceiling where a rate does not.

A **Salamander** (whose `AngerSlap` is also 3000) took **exactly 6 slaps to
unhappy, then 5 more to angry**. The counts *bracket* the ceiling:

| Observation | Implies |
|---|---|
| 5 not enough, 6 enough | ceiling in (30000, 36000] |
| 10 not enough, 11 enough | **ceiling in (30000, 33000]** |

32767 is the only meaningful number in the intersection; 65535 would have needed
11 slaps to *unhappy* and 22 to angry. **And that fixes the cadence:** with the
ceiling pinned, 165 seconds gives the rate as 16383/165 = **99.3, i.e. 100 once a
second**, exactly as the field name says. No hidden sub-second cadence.

**32767 is the right shape as well as the right number.** It is
`short.MaxValue`, and the six per-creature anger fields are **signed 16-bit**
(ADR-0050) — an s16 accumulator saturates at exactly this; 32767 is **already**
DK2's sentinel in the same record, meaning "never" for `HungerRate` and
`TimeAwake`; and it needs **no invented cadence**.

**OpenKeeper is no help and says so itself:** `CreatureMoodSystem` is an empty
stub, and `CreaturesController` carries `// I guess the initial mood is 10000` —
a guess, in the source, never implemented, and refuted above.

## The ladder — CONFIRMED (owner)

Two confirmations, from two different causes, on two different days:

> **Q9** (2026-08-29, on pay): *"Unpaid → unhappy → angry → leaves; a converted
> creature rebels instead."*
>
> **Q105** (2026-09-04, on lairs): *"If a creature can't get a lair it becomes
> unhappy, it can't go to sleep, then angry, then it leaves the dungeon."*

So the ladder is **content → unhappy → angry → leaves**, driven by unmet needs,
and at least two needs feed it. The second answer also settles something that
had been implemented as a guess: **a creature with no bed does not sleep on the
floor. It cannot sleep at all.** And **no mood latches** (Q115, owner): *"anger
can be cooled down to unhappy, and unhappy can cool down to happy."* A mood is a
pure function of the counters — no hysteresis — which is what
`CreatureAngerRules.MoodOf` computes.

## What the data gives, all of it

**Per creature** (`CreatureDef`, all six signed 16-bit):

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
"converted creature rebels instead" half of Q9. Each creature also carries
**three weighted job tables** — happy, unhappy, angry — so the ladder changes
what a creature chooses to do as well as counting down to leaving
(`job-selection.md`). A Troll's unhappy table is `Sulk(10%), Drink(90%)`; a
Goblin's is `Drink(100%)`. That is the `SulkState.cpp` the executable names.

### The signedness that makes the table coherent

Read as `u16` — as `kwd-creatures.md` documented all six — a Goblin's
`AngerInHand` is **65286** and Jack's `AngerSlap` is **60536**, which are
**−250** and **−5000** signed. Signed: **slapping angers** (+3000 Goblin, +7000
Warlock, +8000 Horny), **being picked up calms** (−250 Goblin, −500 Warlock),
and **Jack is the exact inverse** — slapping calms it, being held annoys it, of
a piece with its 240 health and 250-health chicken.

All six are `short`. The first four are small positives on every shipped
creature (0..150), so signed reads them identically and is strictly safer. Fixed
under ADR-0050 and pinned by `TheAngerFieldsAreSignedAndTheLastTwoAreWhy`. Read
unsigned, a slap would have added 3000 and being picked up 65286 — instantly
maxing a creature's anger every time a player touched it.

## Anger is six counters, not one (ADR-0081)

Every anger site goes through one wrapper, `FUN_00478b80(bucket, amount, selector)`
— the **selector is a third thing, not the bucket** (`state-categories.md` §7.2) —
into one adder, `FUN_00487030`, receiver an `int[8]` at `creature+0x26e`: **six
counters**, then the unhappy threshold, then the mood band.

```
add amount to counter[index]
clamp counter[index] to [0, 0xFFFD]
worst = max(counter[0..5])
band  = worst < counter[6] ? content : worst > 0x7FFF ? angry : unhappy
```

**Nothing sums them.** The mood is the largest single grievance.

**Both ends of that clamp are DK2's**: a signed test against zero, then a
saturate at `0xFFFD` — anger does not bank below zero. The percentage sibling
`0x004870e0` (`counter * pct / 100`) clamps alike; ties keep the lowest index.

### Which bucket takes what

Buckets 1–4 are certain: exactly one creature-record field feeds each, and every
one of the sixty call sites agrees. They are exactly the four the need-anger
routine charges (`payday.md` §6), so the bucket taxonomy and the routine agree
without being made to. Buckets 5 and 0 are **Q202**.

| Bucket | What reaches it |
|---|---|
| 1 | `angerNoFood`, `def+0x6c3` |
| 2 | `angerNoLair`, `def+0x6c1` |
| 3 | `angerNoWork`, `def+0x6c7` |
| 4 | `angerNoPay`, `def+0x6c5` |
| **5** | `angerSlap`, `angerInHand`, `ModifyAngerOfCreatureInLairPerSecond`, `ModifyAngerOfCombatPitVictor`, `GamblingModifyCreatureAngerMoney/Smiles`, the torture sites |
| **0** | `ModifyAngerInCompanyOfHatedCreaturesPerSecond`, a share of the casino and of pit spectating, and `angerNoWork` at one site inside the combat pit |

Bucket 5 is *what is done to you, and resting it off* — slapping, carrying, torture
and the pit fill it, a bed drains it, **so "sleep it off" is the specific answer to
slapping** and the shipped calm (−550) dwarfs any need charge. Every *periodic* site
is a category update hook, and each one's bucket is now read off (§7.2 there).

### What the split changes

**A bed does not pay off a wage.** The wage fills bucket 4 and the bed drains
bucket 5; the maximum never sees the trade. This retires the arithmetic ADR-0079
recorded — *a bed outpaces an unpaid wage nine to one* — which was right about
both numbers and wrong about what they do to each other. **A keeper cannot buy
off every grievance with the cheapest one to fix.** And
`ModifyCreatureAngerWhilePrayingPerSecond` (−275) is applied in six consecutive
calls, one per bucket: the one source that drains a grievance it did not cause
(`job-selection.md` §5). Two events wipe their own bucket to zero instead: a bed
placed (Lair) and a wage paid in full (Pay), both `0x00486F80` (ADR-0179).

### A number above the ceiling, and the whole-routine guard

Each counter saturates at **65533**, not at the angry band, and only the maximum
is banded. So a grievance goes on climbing for a second full ceiling's worth
after the creature has already turned angry, and a long-neglected creature does
not calm the moment a keeper does one thing right. The owner's stopwatch is
untouched by this: Q106 measured the *band*, which is still 16383 and 32767, and
the saturation above it is invisible to any observation. Angry is `> 32767`
rather than `>= 32767`, from the routine's own comparison.

The add is skipped — the mood still recomputed — when world byte `+0x72a1` bit
0 is clear. Action 44 writes it and the world starts set (`trigger-dispatch-tables.md`).
Every simulated anger source reproduces the gate (ADR-0160); its UI label is unknown.

## Two creatures have no anger at all, and it is a flag (Q210)

**Bit 2 of the creature record's second flag field** (`0x1545`, runtime
`def+0x68a`) means *never becomes angry*. **Skeleton and Jack** carry it.

The wrapper `FUN_00478b80` — 60 call sites, confirmed by a scan of both
executable blocks — opens with `if ((def[0x68a] & 4) == 0)` and wraps
**everything** in it: the call to the adder, the comparison of the mood band
before and after, and the two messages fired as the band crosses 1 and 2. Three
other readers gate the same follow-on work the same way. So for a flagged
creature nothing accrues from any bucket and no warning is ever raised.

**Jack's anger values are a decoy.** This project's `CanBeAngered` was inferred
from the data — *"the Skeleton is the only creature whose four `angerNo*` fields
are all zero"* — which is true, and true by accident of that one record.

| field | Jack | Skeleton |
|---|---|---|
| `angerNoLair` / `angerNoFood` / `angerNoPay` / `angerNoWork` | **120 / 110 / 60 / 50** | 0 / 0 / 0 / 0 |
| `angerInHand` | **+500** | 0 |
| `angerSlap` | **−5000** | 0 |

Six populated fields, none of them read. Before this was found our Jack took
anger on the two paths that did not consult the predicate, and his negative slap
meant **a keeper could calm him by slapping him** — an effect DK2 cannot produce
because it never reaches the adder. `CanBeAngered` reads the flag, and the gate
sits in the adder itself, which is where the original puts it.

**The Imp is a separate mechanism**: not flagged, and carrying **1** rather than
0, so *"Imps never get angry"* (DK2 wiki) holds in practice through a small
number rather than a switch — `AngerNoLair` = 1 against 32767 is **nine hours**,
and an imp is owed no bed at all. Three ways of saying "no anger" — a flag, a
rate of zero, and a rate too small to matter — and only the first is a rule.
**Anything that is down** is also exempt (ADR-0051).

## Where cooling happens in the original (Q123)

Both in-lair modifiers — health (75) and anger (76) — are read by one shared
routine, which heals up to the level-scaled maximum and then applies the anger
modifier. Its three direct callers are the per-tick **category update hooks** for
categories 6, 9 and 10 — at `0x0047AC00`, `0x0047AC40` and `0x0047AC70`, stored
at `+0x14` of category records 9, 6 and 10 — not three individual state handlers.
Each calls it when `tick % variable 19 == 0` (once a game second, Q1), with no
terrain or state-id test around the call. The state table maps those categories
to **6**: looking for the Lair (`0x4f`), making a home (`0x50`), going to bed
(`0x51`); **9**: the three sleeping states (`0x52`–`0x54`); **10**: sulking
(`0x55`).

**CONFIRMED from the binary**, and independently re-read from the raw address
scan up. Cooling is state-category-gated rather than terrain-gated, and it is
**not** restricted to sleeping or to occupying one's own bed: sulking is an
awake-in-lair state, and the three category-6 states begin before sleep. Two
details that do not affect cooling: the category-9 hook does one extra thing when
the state id is `0x53`, and the category-10 hook returns a bucket-5 anger query.
The healing branch is reached from the same seven states, but its per-creature
virtual predicate is unread, so whether awake states receive *health* remains
**UNKNOWN**.

## The chooser, read from the binary (Q122)

`FUN_004784c0` is the angry chooser; `FUN_004782c0` is the unhappy one and
`FUN_004780e0` the happy one, and the three are the same routine three times
(`job-selection.md` §1). What the angry one does, in order:

1. **`InHand` aborts it** before anything else (`+0x3E6` bit 0), and the
   definition flag `0x4000` **`IsHorny`** (Q204) diverts the creature into state
   238 instead of choosing at all, where it loses 10 health a tick
   (`rebellion.md` §7.1).
2. **The five-game-second gate.** `if (last != 0 && now - last < 5 x var19)
   return` — `var19` being ticks-a-game-second (**4**, Q1) and `last` the field
   EA's own debug dump calls **`LastGameTickSuccessfullyChoseAngryJob`**, the
   *only* thing carried between selections.
3. **No lair? Go and get one** — `FUN_0046f390`, the same attempt the idle state
   makes (`lair-acquisition.md` §5). If it succeeds the creature does that, the
   timestamp is stamped, and **no roll happens**. The unhappy chooser does the
   same thing one line earlier.
4. **Build the ranges.** For each entry of `AngryJobs` in record order, a triple
   `{low, high, jobId}` with `high = low + chance` — a running total.
5. **"Sort" them** with `qsort(base, count, 12, cmp)`, where `cmp` compares the
   two entries' `high` fields and returns **0 for less-than and 1 otherwise**,
   never a negative. Applied to an array already ascending by construction, it
   cannot reorder anything. **The order is the record's.**
6. **Roll `rand(100)`** — the helper is an LCG per player ending in `% 100`, so
   **0..99**.
7. **First pass:** the first entry with a non-zero job id whose range holds the
   roll, tested `low <= roll <= high` — **inclusive at both ends**. Dispatch it
   (`FUN_00478800`); if the job's own start routine returns 0, keep scanning.
8. **Fall-through:** if nothing started, try **every** entry in record order.
9. **Stamp the timestamp** if something started, and only then. A selection that
   starts nothing leaves the gate open, so the creature tries again at its next
   think.

**Two arithmetic quirks fall out of 4–7 and both are reproduced.** The inclusive
bounds make adjacent ranges *overlap* at their shared boundary and the first
match wins, so a `Leave(50%)`/`Rebel(50%)` table is `[0,50]` and `[50,100]` and
the first entry takes 51 of the 100 rolls. And with the roll stopping at 99 the
last entry's upper bound is unreachable — harmless when the chances sum to 100,
and the reason a table summing to *less* reaches the fall-through at all.

**`Leave` cannot fail at selection.** Job 10's routine (`FUN_00459fd0`) sets two
states and returns success unconditionally: finding a portal is the *leaving
state's* problem, not the chooser's. So a keeper with no portal keeps a creature
that has decided to go (Q116).

**A quirk not reproduced**, because nothing here has an unhappy chooser's jobs
yet: the *unhappy* chooser, on a successful lair attempt, stamps the **angry**
chooser's timestamp (`+0x101`) rather than its own (`+0xf9`), which its
job-selection path stamps correctly. So in DK2 an unhappy creature that goes for
a lair delays its *angry* chooser by five seconds. Recorded so that whoever
builds unhappy jobs does not tidy it away.

**Job ids** are the record's own and match this project's `JobType` enum, checked
at the dispatcher's restricted branch: when the creature's state byte is `0x16`
only jobs **10** (`Leave`) and **0x10** (`Rebel`) may start.

**What an angry creature does comes from its own record**, not from a rule of
ours: a Goblin's tables are `Train(100%)` content, `Drink(100%)` unhappy — the
Casino — and `Leave(50%)`, `Rebel(50%)` angry. The roll comes from the
simulation's RNG, so it is part of the deterministic stream, and it is **rolled
afresh on every allowed selection** (ADR-0061). With `Leave(50%)`, re-rolling is
how an angry Goblin walks out.

## Implementation

`CreatureAngerRules` + `CreatureAnger` (the six-bucket struct, `AngerBucket`) +
`CreatureAngerSystem`, once a game second, registered after sleep — so a creature
that claimed a bed this tick is not charged for the second it arrived — and after
health, so one that collapsed this second is already `Dying` and is skipped.
`Ceiling` is 32767, `Saturation` 0xFFFD, `UnhappyAt` the record's percentage,
`MoodOf` the band, `CanBeAngered` the Q210 flag, `Slap` and `Held` the two
discrete deltas, `ReselectionGateTicks` the five-second gate, `RollPercent` /
`CumulativeRanges` the chooser arithmetic of Q122, and
`CreatureAngerSystem.Leave` hands a deserter to `LeavingRules`: the nearest Portal
or Hero Portal of anyone by straight line (`creature-leaving.md` §1, 2026-09-24).

**Which sources are applied, and the rule is one sentence: a source is applied
when the player's own choices can turn it off.**

| Source | Value | Applied? |
|---|---|---|
| `AngerNoLair` | +100/s | **Yes** — lairs are built, sold and filled |
| `ModifyAngerOfCreatureInLairPerSecond` | −550/s | **Yes** — sleep works, so recovery is real |
| `AngerNoPay` | +60/s | **Yes** (ADR-0079) — `FUN_00478d00`, the need-anger routine, reached just before the needs routine. **Being owed is the whole trigger**: unlike its two siblings it has *no room-availability clause*, so a keeper cannot excuse itself by having no treasury. Charged additively with `AngerNoLair`, and **not at all to a captive** — bit 1 of the state flag byte suppresses the whole routine and all eleven captive states carry it. `payday.md` §6 |
| `AngerNoWork` | +50/s | **Yes** (ADR-0082) — charged by the happy chooser when every job in the creature's happy table has failed. **And only the happy one**: the unhappy and angry choosers have the identical tail with the call missing, so a creature that is already unhappy is never made crosser by idleness. `JobRules.ChargesNoWorkAnger`, `job-selection.md` §1 |
| `AngerNoFood` | +100/s | **In the prison only** (ADR-0074) — a keeper can stock a cell, so that source is one the player can turn off. Not applied generally: the ordinary hungry creature walks to a hatchery, and charging it would anger everybody on a map with no hatchery |
| hated company | +150/s | **No** — the relation is parsed and unused |

Otherwise anger becomes a countdown no dungeon survives. **Each omission is a
line in the scenario reports**, not a silence.

**The tabled figures are per second and the loaded ones are per tick, and the
difference is four.** DK2's creature loader divides each of the four `angerNo*`
fields by the level's ticks-per-second as it loads them — the same register it
multiplies `TimeAwake` and `TimeSleep` by two dozen instructions earlier — so
what the engine holds is **anger per DK2 tick**, and a routine adding one per
tick delivers the tabled figure per second. The quotient is floored at 1 whenever
the source is non-zero, which is how the Imp, the Dwarf and Horny keep their 1.
**That matters wherever a routine applies one unit less often than every tick:**
the prison's state 117 adds one on each failed search for food, and searches are
gated to at most one a game second, so **a hungry prisoner in an unstocked cell
angers at 25 a second where a bedless creature angers at 100**
(`prison.md` §7.4). Reproduced rather than tidied.

**Recovery is 5.5x faster than accrual** - the -550 instalment once a game
second against +100 a second for having no lair. It is the update hook of three
state *categories*, seven states in all, so a creature looking for a lair or
sulking is already cooling (ADR-0171).

**Both shipped angry-table entries are implemented.** `Leave` walks out; `Rebel`
walks out too when a Portal is reachable and otherwise turns the creature Good
after 30 ticks (`rebellion.md`, ADR-0172). The three `Rebel*Percentage` variables
and `RebelMaxFollowers` are read by nothing, so job 16 holds no roll.

## Tests

`CreatureAngerTests` (`tests/DK2.Simulation.Tests/`, which cites
`lair-acquisition.md` §8 for the choosers). In
`tests/DK2.Behavior.Tests/CreatureNeedsOnRealDataTests.cs`:
`TheAngerFieldsAreSignedAndTheLastTwoAreWhy`,
`EveryCreatureBecomesUnhappyAtFiftyPercentOfWhateverAngerIs`,
`TheOwnersMeasurementsReproduceOnTheShippedRecord`,
`ALairlessImpWouldTakeNineHoursToGetAngry`,
`TheSkeletonAndJackCannotBeAngeredAndOnlyTheFlagSaysSoAboutJack`. Scenarios
`a_goblin_with_no_lair_walks_out.yaml`,
`the_keeper_picks_up_an_imp_and_slaps_it.yaml` (`scenarios/creatures/`).

## Known exceptions

- The chooser's inclusive range bounds and its no-op sort are reproduced.
- Job 16's Combat Pit guard is inert; nothing here sets that flag
  (`rebellion.md`).

## Open questions

- **Q202**: buckets 0 and 5's taxonomy, and the whole-routine guard flag.
- The chooser's per-creature abort flag is **`InHand`**, and Q204's `IsHorny`
  diversion lands an angry Horned Reaper in state 238, losing 10 health a tick
  (`rebellion.md` §7.1). Step 1 above is corrected accordingly.
- **Q116 ANSWERED** (`rebellion.md` §6): both accounts were right one level
  apart. The table picks the job; portal-then-Good is inside job 16.
- Whether awake creatures receive the in-lair *heal* as well as the calm (Q123).
- Q410-Q413, rebellion's own remainder (`rebellion.md`).
