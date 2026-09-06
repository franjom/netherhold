# Anger: the ladder, the ceiling, and how it was measured

**The mechanic is implemented.** The ceiling that blocked it for four sittings is
**32767**, measured by the owner in the original game by two independent methods
on 2026-09-04 (Q106 and Q114, ADR-0053). This document was the case for waiting;
it is now the record of what waiting bought.

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
11 slaps to *unhappy* and 22 to angry.

**And that fixes the cadence:** with the ceiling pinned, 165 seconds gives the
rate as 16383/165 = **99.3, i.e. 100 once a second**, exactly as the field name
says. No hidden sub-second cadence.

## Why 32767 is the right shape as well as the right number

- It is `short.MaxValue`, and the six per-creature anger fields are **signed
  16-bit** (ADR-0050) — an s16 accumulator saturates at exactly this.
- 32767 is **already** DK2's sentinel in the same record, meaning "never" for
  `HungerRate` and `TimeAwake`. One house style, two meanings.
- It needs **no invented cadence**.

**OpenKeeper is no help and says so itself:** `CreatureMoodSystem` is an empty
stub, and `CreaturesController` carries `// I guess the initial mood is 10000` —
a guess, in the source, never implemented, and refuted above.


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

## What is implemented, and what is not

`CreatureAngerRules` + `CreatureAngerSystem`, once a game second, registered
after sleep (so a creature that claimed a bed this tick is not charged for the
second it arrived) and after health (so a creature that collapsed this second is
already `Dying` and is skipped).

**Only two of the sources are applied, and the rule is one sentence: a source is
applied when the player's own choices can turn it off.**

| Source | Value | Applied? |
|---|---|---|
| `AngerNoLair` | +100/s | **Yes** — lairs are built, sold and filled |
| `ModifyAngerOfCreatureInLairPerSecond` | −550/s | **Yes** — sleep works, so recovery is real |
| `AngerNoFood` | +100/s | **In the prison only** (ADR-0074) — a keeper can stock a cell, so that source is one the player can turn off. Not applied generally: the ordinary hungry creature walks to a hatchery, and charging it would anger everybody on a map with no hatchery |
| `AngerNoWork` | +50/s | No — non-workers have no job system; "no work" is a fact about us |
| `AngerNoPay` | +60/s | No — there is no payday. **Payday itself is now read** (`payday.md`), but *what charges this* was not found in it: the gap is between an unpaid creature and the ladder |
| hated company | +150/s | No — the relation is parsed and unused |

Otherwise anger stops being feedback and becomes a countdown no dungeon
survives — which would say nothing about DK2 and would end every long scenario
in mass desertion. **Each omission is a line in the scenario reports**, not a
silence; when a hatchery can feed a creature, `AngerNoFood` goes in the same day.

**The tabled figures are per second and the loaded ones are per tick, and the
difference is four** (read 2026-09-06). DK2's creature loader divides each of
the four `angerNo*` fields by the level's ticks-per-second as it loads them —
the same register it multiplies `TimeAwake` and `TimeSleep` by two dozen
instructions earlier — so what the engine holds is **anger per DK2 tick**, and a
routine adding one per tick delivers the tabled figure per second. The quotient
is floored at 1 whenever the source is non-zero, which is how the Imp, the Dwarf
and Horny keep their 1.

**That matters wherever a routine applies one unit less often than every tick.**
The prison's is such a routine: state 117 adds one on each failed search for
food, and searches are gated to at most one a game second, so **a hungry
prisoner in an unstocked cell angers at 25 a second where a bedless creature
angers at 100** (`prison.md` §7.4). A quirk, reproduced rather than tidied.

**Recovery is 5.5× faster than accrual.** −550 a second asleep in one's own bed
against +100 without one, which is why a keeper who builds a lair late still
recovers.

**Where the −550 is applied in the original (binary, 2026-09-05; Q123).** Both
in-lair modifiers — health (75) and anger (76) — are read by **one routine**,
which heals up to the level-scaled maximum and then applies the anger modifier.
That routine is called only from the per-tick handlers of **three creature
states**, each gated to `tick % variable 19 == 0` (once a game second, Q1), and
nothing on the way tests the tile underfoot. So the cooling is a property of
being in one of those states, not of standing on lair terrain; one of the three
handlers also counts a per-creature timer down while the state is `0x53`, which
has the shape of the sleep countdown. The states are not yet named, so the
current `AsleepInOwnBed` rule is STRONGLY INFERRED rather than confirmed: exact
if all three are sleeping states, undercooling if one is an awake-in-lair state.

### The consequences

**What an angry creature does comes from its own record**, not from a rule of
ours. A Goblin's three tables:

| Mood | Job table |
|---|---|
| content | `Train(100%)` |
| unhappy | `Drink(100%)` — the Casino |
| angry | `Leave(50%)`, `Rebel(50%)` |

The angry roll comes from the simulation's RNG, so it is part of the
deterministic stream, and it is **rolled afresh on every allowed selection** —
see "The chooser, read from the binary" below, and ADR-0061. It was *drawn once
and kept* until 2026-09-05, on the reasoning that re-rolling would eventually
draw `Leave` by luck and report an unimplemented outcome as an implemented one.
That reasoning describes DK2: with `Leave(50%)`, re-rolling is how an angry
Goblin walks out.

**Leaving is implemented. Rebelling is not**: it needs a creature to change sides
and become a hero, and `RebelLeavingPercentage` (70),
`RebelBecomeGoodPercentage` (15) and `RebelDefectPercentage` (15) have nothing to
act on. A creature that draws it stays angry, says so, and rolls again five game
seconds later.

### The chooser, read from the binary — 2026-09-05 (Q122)

`FUN_004784c0` is the angry chooser; `FUN_004782c0` is the unhappy one and
`FUN_004780e0` the happy one, and the three are the same routine three times.
What the angry one does, in order:

1. **A per-creature flag aborts it** before anything else, and a definition flag
   (`0x4000` in the record's flag dword at runtime `+0x686`) diverts the creature
   into a state of its own instead of choosing at all. Neither is identified.
2. **The five-game-second gate.** `if (last != 0 && now - last < 5 x var19)
   return` — where `var19` is ticks-a-game-second (**4**, Q1) and `last` is the
   field EA's own debug dump calls
   **`LastGameTickSuccessfullyChoseAngryJob`**. It is the *only* thing carried
   between selections.
3. **No lair? Go and get one** — `FUN_0046f390`, the same attempt the idle state
   makes (`lair-acquisition.md` §5). If it succeeds the creature does that, the
   timestamp is stamped, and **no roll happens**. The unhappy chooser does the
   same thing one line earlier.
4. **Build the ranges.** For each entry of `AngryJobs` in record order, a triple
   `{low, high, jobId}` with `high = low + chance` — a running total.
5. **"Sort" them** with `qsort(base, count, 12, cmp)`, where `cmp` compares the
   two entries' `high` fields and returns **0 for less-than and 1 otherwise**,
   never a negative. Applied to an array that is already ascending by
   construction, it cannot reorder anything. **The order is the record's.**
6. **Roll `rand(100)`** — the helper is an LCG per player ending in `% 100`, so
   **0..99**.
7. **First pass:** the first entry with a non-zero job id whose range holds the
   roll, tested `low <= roll <= high` — **inclusive at both ends**. Dispatch it
   (`FUN_00478800`); if the job's own start routine returns 0, keep scanning.
8. **Fall-through:** if nothing started, try **every** entry in record order.
9. **Stamp the timestamp** if something started, and only then. A selection that
   starts nothing leaves the gate open, so the creature tries again at its next
   think.

**Two arithmetic quirks fall out of 4-7 and both are reproduced.** The inclusive
bounds make adjacent ranges *overlap* at their shared boundary, and the first
match wins: a `Leave(50%)`/`Rebel(50%)` table is `[0,50]` and `[50,100]`, so the
first entry takes 51 of the 100 rolls. And with the roll stopping at 99, the last
entry's upper bound is unreachable — harmless when the chances sum to 100, and
the reason a table summing to *less* than 100 reaches the fall-through at all.

**`Leave` cannot fail at selection.** Job 10's routine (`FUN_00459fd0`) sets two
states and returns success unconditionally: finding a portal is the *leaving
state's* problem, not the chooser's. So a keeper with no portal keeps a creature
that has decided to go — which is what this project already had, arrived at from
the owner's answer to Q116 rather than from the binary.

**A quirk not reproduced, because nothing here has an unhappy chooser yet:** the
*unhappy* chooser, on a successful lair attempt, stamps the **angry** chooser's
timestamp (`+0x101`) rather than its own (`+0xf9`). Its job-selection path stamps
`+0xf9` correctly. So in DK2 an unhappy creature that goes for a lair delays its
*angry* chooser by five seconds. It is recorded here so that whoever builds
unhappy jobs does not tidy it away.

**Job ids** are the record's own and match this project's `JobType` enum, checked
at the dispatcher's restricted branch: when the creature's state byte is `0x16`
only jobs **10** (`Leave`) and **0x10** (`Rebel`) may start — the two the enum
numbers 10 and 16.

### Who is exempt, without a special case

- **Imps.** The DK2 wiki says flatly *"Imps never get angry."* The data says the
  same arithmetically: `AngerNoLair` = 1, which against 32767 is **nine hours**.
  Moot anyway, since an imp is owed no bed at all.
- **The Skeleton**, the only creature whose four `angerNo*` fields are all zero.
- **Anything that is down.** Something lying on the floor waiting to be carried
  to bed is not accruing a grievance about not having got there (ADR-0051).

## Still open

| | |
|---|---|
| **Q115** | The zero floor is **ours**. Nothing in the data states it, and without it a combat-pit victor (−16400) would be immune to every grievance for three minutes |
| **Q116** | **Where a deserter goes.** DK2 sends it to a portal, so a keeper with no portal keeps a creature that wanted to leave. The wiki's DK2 section describes rebellion as the *fallback* when the exit is unreachable, where the record plainly holds a weighted table — both cannot be right, and the record is the stronger source |
| — | Rebellion itself, and the three percentages that govern its outcome |
