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
| `AngerNoWork` | +50/s | **YES, since ADR-0082** - the happy chooser charges it when every job in the creature's happy table has failed. **And only the happy one**: the unhappy and angry choosers have the identical tail with the call missing, so a creature that is already unhappy is never made crosser by idleness. `job-selection.md` |
| `AngerNoPay` | +60/s | **YES, since ADR-0079** — `FUN_00478d00`, the need-anger routine, reached just before the needs routine. **Being owed is the whole trigger**: unlike its two siblings it has *no room-availability clause*, so a keeper cannot excuse itself by having no treasury. Charged additively with `AngerNoLair`, and **not at all to a captive** — bit 1 of the state flag byte suppresses the whole routine and all eleven captive states carry it. `payday.md` §6 |
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

**Recovery is 5.5× faster than accrual.** The −550 instalment is applied once a
game second in all seven states listed below, against +100 a second for having
no lair. Sleeping is only three of those states.

**Where the −550 is applied in the original (binary, completed 2026-09-07;
Q123).** Both in-lair modifiers — health (75) and anger (76) — are read by one
shared routine, which heals up to the level-scaled maximum and then applies the
anger modifier. Its three direct callers are the per-tick **category update
hooks** for categories 6, 9 and 10, not three individual state handlers. Each
calls it when `tick % variable 19 == 0` (once a game second, Q1), with no terrain
or state-id test around the call. The complete state table maps those categories
to:

- category 6: looking for the Lair (`0x4f`), making a home (`0x50`) and going to
  bed (`0x51`);
- category 9: the three sleeping states (`0x52`–`0x54`);
- category 10: sulking (`0x55`).

This is **CONFIRMED from the binary**. Cooling is state-category-gated rather
than simply terrain-gated, but it is not restricted to sleeping or to occupying
one's own bed. In particular, sulking is an awake-in-lair state, and the three
category-6 states begin before sleep. The current `AsleepInOwnBed` rule therefore
undercools. The healing branch is reached from the same seven states, but its
per-creature virtual predicate is still unread; whether awake states actually
receive health therefore remains **UNKNOWN** and is not established by Q123.

**Independently re-read on 2026-09-07, from the raw address scan up, and
confirmed.** The three callers are at `0x0047AC00`, `0x0047AC40` and
`0x0047AC70`; the fill run for the category table stores them at offset `+0x14`
(the per-tick update hook, `eating.md`) of records 9, 6 and 10. Two details that
do not affect cooling: the category-9 hook does one extra thing when the state
id is `0x53`, and the category-10 hook returns the result of a bucket-5 anger
query.

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

---

## Anger is six counters, not one (2026-09-06, ADR-0081)

Every anger site in DKII.EXE goes through one wrapper, `FUN_00478b80`, into one
adder, `FUN_00487030`. The adder's receiver is an `int[8]` at `creature+0x26e`:
**six counters**, then the unhappy threshold, then the mood band.

```
add amount to counter[index]
clamp counter[index] to [0, 0xFFFD]
worst = max(counter[0..5])
band  = worst < counter[6] ? content : worst > 0x7FFF ? angry : unhappy
```

**Nothing sums them.** The mood is the largest single grievance.

### The four that are certain

Exactly one creature-record field feeds each, and every one of the sixty call
sites agrees:

| Bucket | Field | Offset |
|---|---|---|
| 1 | `angerNoFood` | `def+0x6c3` |
| 2 | `angerNoLair` | `def+0x6c1` |
| 3 | `angerNoWork` | `def+0x6c7` |
| 4 | `angerNoPay` | `def+0x6c5` |

Those are exactly the four the need-anger routine charges (`payday.md` §6), so
the bucket taxonomy and the routine agree without being made to.

### The two that are not (Q202)

| Bucket | What reaches it |
|---|---|
| **5** | `angerSlap`, `angerInHand`, `ModifyAngerOfCreatureInLairPerSecond`, `ModifyAngerOfCombatPitVictor`, `GamblingModifyCreatureAngerMoney/Smiles`, the torture sites |
| **0** | `ModifyAngerInCompanyOfHatedCreaturesPerSecond`, a share of the casino and of pit spectating, and `angerNoWork` at one site inside the combat pit |

Bucket 5 reads as *what is done to you, and resting it off* — slapping, carrying,
torture and the pit fill it, and a bed drains it. **Which makes "sleep it off"
the specific answer to slapping**, and explains why the shipped calm (−550) is so
much larger than any need charge: it is undoing 3000-point slaps, not 100-point
grievances. Bucket 0 is less coherent and its name here is ours.

### What the split changes

**A bed does not pay off a wage.** The wage fills bucket 4 and the bed drains
bucket 5; the maximum never sees the trade. This retires the arithmetic
ADR-0079 recorded — *a bed outpaces an unpaid wage nine to one* — which was
right about both numbers and wrong about what they do to each other.

**A keeper cannot buy off every grievance with the cheapest one to fix**, which
is what the old pooled counter allowed and what no player would recognise.

**Only a Temple gives anything back.** `ModifyCreatureAngerWhilePrayingPerSecond`
(−275) is applied in six consecutive calls, one per bucket, and it is the only
source in the game that drains a grievance it did not cause.

### And a number above the ceiling

Each counter saturates at **65533**, not at the angry band, and only the maximum
is banded. So a grievance goes on climbing for a second full ceiling's worth
after the creature has already turned angry, and a long-neglected creature does
not calm the moment a keeper does one thing right.

The owner's stopwatch is untouched by this: Q106 measured the *band*, which is
still 16383 and 32767. The saturation above it is invisible to any observation,
which is why measurement could not have found it. Angry is `> 32767` rather than
`>= 32767`, from the routine's own comparison.

### The whole-routine guard

The add is skipped entirely — the mood still recomputed — when
`DAT_006dc424 + 0x72a1 & 1` is clear. A global option, unidentified, presumably
"creatures never get angry". Not modelled; Q202 carries it.


---

## Two creatures have no anger at all, and it is a flag (2026-09-07, Q210)

**Bit 2 of the creature record's second flag field** (`0x1545`, runtime
`def+0x68a`) means *never becomes angry*. **Skeleton and Jack** carry it.

The wrapper this document already identifies as the single entry point —
`FUN_00478b80`, 60 call sites, confirmed by a scan of both executable blocks —
opens with `if ((def[0x68a] & 4) == 0)` and wraps **everything** in it: the call
to the adder, the comparison of the mood band before and after, and the two
messages fired as the band crosses 1 and 2. Three other readers gate the same
follow-on work the same way. So for a flagged creature nothing accrues from any
bucket and no warning is ever raised.

**Jack's anger values are a decoy, and they caught us.** This project's
`CanBeAngered` was inferred from the data — *"the Skeleton is the only creature
whose four `angerNo*` fields are all zero"* — which is true, and true by
accident of that one record.

| field | Jack | Skeleton |
|---|---|---|
| `angerNoLair` / `angerNoFood` / `angerNoPay` / `angerNoWork` | **120 / 110 / 60 / 50** | 0 / 0 / 0 / 0 |
| `angerInHand` | **+500** | 0 |
| `angerSlap` | **−5000** | 0 |

Six populated fields, none of them read. Before this was found, our Jack took
anger on the two paths that did not consult the predicate, and his negative slap
meant **a keeper could calm him by slapping him** — an effect DK2 cannot produce
because it never reaches the adder. `CanBeAngered` now reads the flag and the
gate sits in the adder itself, which is where the original puts it.

**The Imp is a separate mechanism** and is unchanged: not flagged, and carrying
**1** rather than 0, so *"Imps never get angry"* holds in practice through a
small number rather than a switch. Three ways of saying "no anger" — a flag, a
rate of zero, and a rate too small to matter — and only the first is a rule.

---

## Q212 answered: it is the Temple's prayer hook, and the field is mana

**2026-09-07.** The routine that reads `PercentageOfManaGainedBySacrificing`
(id 112, shipped **0**) is **category 12's per-second update hook**, and
category 12 is **praying**. Read whole rather than through the window that
raised the question.

**How the category was pinned.** The state-category table (`0x00699688`, 32
records of `0x40`) gives category 12 a `+0x14` update hook at `0x0047B970`, and
`0x0047BB4E` lies inside it with no other function start between. The hook's
first act settles what it is: **six calls to the anger wrapper, one per bucket,
each passing `ModifyCreatureAngerWhilePrayingPerSecond`** — which is exactly
what `JobRules.PrayingCalm` already does, arrived at independently. Category 15's
hook (`0x0047B590`) contains the torture read, so ADR-0085's attribution there
was right too.

**`player + 0x10EE` is the player's mana.** Fifteen references, and the
corroboration is that one of them is `FUN_004C10E0` — the routine ADR-0085
identified as the reader of `HypnotismManaDrainPerSecond`. A field that a mana
drain decrements and a prayer increments is mana.

### What the hook does, once a game second

The cadence is a per-creature countdown at `creature+0xAF`, reloaded with
variable 19, so the whole block runs **once a game second**:

1. **Calm all six anger buckets** by `ModifyCreatureAngerWhilePrayingPerSecond`.
2. **Heal**, by `min(ModifyCreatureHealthWhilePrayingPerSecond, missing health)`
   — id 115, shipped **33**, which is the number this project already uses. The
   ceiling it measures "missing" against is the creature's **level-scaled** max
   Hp, not the record's.
3. **Grant mana** to the creature's player: `ManaGeneratedByPrayerPerSecond`,
   level-scaled, clamped to signed 16-bit, **then doubled** when creature state
   flag `+0x3EA` bit 4 is set — `x += x * (pct + 100) / 100` with `pct = 0`.

**So the arithmetic stands and the name does not.** A variable called
`PercentageOfManaGainedBySacrificing` is read in the **praying** path, and the
only thing gating it is a creature state flag whose meaning is **UNKNOWN**. What
is certain: for a creature carrying that flag, praying pays **double** mana, and
a reimplementation reading the shipped 0 as "no bonus" would halve it. Nothing
is built on it here — prayer mana is not implemented, only prayer's calm and
heal — so this is a trap laid for whoever builds it, not a defect.

**What remains open** is narrower than Q212 was: what does `creature+0x3EA`
bit 4 mark? It is a *state* flag rather than a definition flag, so it is set by
something the creature does. Bit 7 of the same byte is already noted as UNKNOWN
in Q69's answer, which makes this byte worth a pass of its own.

### Two things the same read confirmed, for free

**The level-scaling model is right, from the binary side** (Q119,
`CreatureLevelRules`). Both scaled values in this hook take the same shape:
`level == 1` uses the record's value **raw**, and any other level computes
`table[level] × record ÷ 100`. That is precisely this project's implementation —
*"a percentage applied to the record's value rather than compounded per level"* —
and it was reasoned from the file, not read from the code. The table is reached
through a pointer at global `0x006E4154` and is read from **83 distinct
functions at 172 sites**, so the scaling is applied at each point of use rather
than to a cached copy.

**Two of the 32 stat rows are now located** in that structure: `+0xE1` scales
the field at runtime `+0x692` and `+0x183` scales runtime `+0x6A2`. Run through
the loader (source = file + `0x3F0`), those are file `0xF0C` and `0xF18` —
**`Hp`** and **`ManaGeneratedByPrayerPerSecond`**. Their offsets differ by
`0xA2`, which is nine 18-byte rows, so the rows are one stat apart per 18 bytes
and the two stats sit nine apart in the table's own order.

That last check was worth running for its own sake: the doc comment on
`CreatureLevelRules` says **ten of the 32 stats never scale, own-land
regeneration among them**, and a hook that scales a heal looked like it
contradicted that. It does not — the field being scaled is max **Hp**, and
health is one of the stats that does scale. **The apparent contradiction was
mine, and identifying the field settled it in two commands** rather than leaving
a doubt in the record.
