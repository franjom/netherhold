# Imps, and what mana is actually for

Milestone M3. Where imps come from, what they cost to buy, and what they cost to
keep — the last of which turns mana from a number that only goes up into an
economy.

Implemented by `DK2.Simulation.ImpSupplyRules`,
`DK2.Simulation.Systems.ImpSupplySystem` and
`DK2.Simulation.Commands.CreateImpCommand`.

---

## Summary of confidence

| Claim | Status |
|---|---|
| A keeper always has at least 4 imps | **CONFIRMED** — owner observation, and `MinimumImpThreshold` is 4 |
| A missing imp comes back every 5 seconds | **CONFIRMED** — owner measured it; `TimeBeforeFreeImpGeneratedSeconds` is 5. But the **first** one comes back at once — see below |
| A keeper starts a level with 4 | **CONFIRMED** — owner; falls out of the floor above without a separate rule |
| Create Imp costs 1500 mana | **CONFIRMED** — owner, and it is the cost on DK2's own Create Imp record |
| The cost rises by 1500 each time, and falls back when imps die | **CONFIRMED TWICE** (Q54) — the owner from play, and then the binary: the original computes `base × max(1, imps − MinimumImpThreshold + 1)`, which is our formula operation for operation |
| Imps are created at the dungeon heart | **STRONGLY INFERRED** — and a keeper without one has already lost (Q40) |
| **Imps cost 7 mana a second each to keep** | **CONFIRMED and IMPLEMENTED** (Q53) — owner measured 28 at four imps and 35 at five, and 7 is the upkeep on DK2's own Create Imp record |
| Traps draw mana continuously | **CONFIRMED, field found, not implemented** (Q59) - five traps measured, and `Traps.kwd` offset 0x0202 reproduces all five. A trap is an object and arrives at M6 |
| A claimed mana vault is worth +100 a second | **CONFIRMED** (Q37b) — owner, and `TerrainDef.ManaGain` for Claimed Vault is 100. Already implemented; nothing changed |
| Possession drains 25 a second | **DISPUTED** (Q58) — the data says 25, `ManaCostInFirstPersonPerSecond` says 5, and the owner saw no drain at all |
| **The free-imp clock is a deadline, not a countdown** | **CONFIRMED** (Q56) — owner recording, then the binary. A keeper at full strength is never owed one, their deadline goes stale, and the next loss is replaced **on the spot**. Ours said the opposite for three weeks |
| A free imp costs no mana and ignores availability | **CONFIRMED** — the free flag makes the original skip the availability check, the affordability check and the deduction |

---

## The free four

The cleanest confirmation this project has had. The owner described the
behaviour from the game:

> every keeper gets 4 imps at the start of the game, and 4 imps is the minimum,
> if I drop all imps to a dungeon heart they will auto respawn in approx 5 second
> intervals

and the shipped balance table names both halves, in its own words, with exactly
those numbers:

```
   94  MinimumImpThreshold                            4
   95  TimeBeforeFreeImpGeneratedSeconds              5
```

It is rare for an observation and a variable name to meet this cleanly. Almost
everything else in this project is inferred *from* a variable name and checked
against behaviour, or measured *from* behaviour and matched to a number; here
both arrived independently and agreed.

**Note what it does not need.** The owner's "every keeper gets 4 at the start"
needs no rule of its own: a keeper who begins with none is below the floor, so
the floor fills them in. Whether the original hands them over instantly at level
load or generates them at the same five-second interval is not distinguished by
anything observed, and ours does the latter.

Three more imp variables are parsed and read by nothing:

```
  110  InsufficientManaImpEvaluationPeriodSeconds    10
  144  ImpPopCountdownTimeSeconds                    10
  188  SpecialReceiveImpsAmount                      10
```

The first is the interesting one, and it is a clue about the drain below: a game
that has a period on which to re-evaluate imps *when mana is insufficient* is a
game in which imps cost mana. The third matches the community's description of
the Receive Imps special exactly — it grants ten.

## The clock is a deadline, and the difference is visible

**This section is a correction.** For three weeks this project ran a countdown
that was held at zero while a keeper was up to strength and restarted from full
when they fell short, and the code defending it said the alternative was
"indistinguishable in the steady state". It is not indistinguishable. It is the
first thing you see.

### What the owner filmed

Recorded 2026-09-05, analysed by a second assistant and passed here:

- the keeper sat at **exactly four imps for about thirty seconds**;
- one imp was picked up and dropped into the dungeon heart — about half a second;
- the replacement appeared **about 1.3–1.5 seconds later**, not five;
- with several imps missing, the ones after it came at **about five-second
  intervals**;
- imps held in the Hand of Evil are **not counted** in the imp portrait's figure.

### What the binary says

The keeper's per-frame update in `Player.cpp` holds one field for this and
writes it in exactly one place. In our own words:

    if (number of creatures of type Imp < MinimumImpThreshold
        and this keeper's nextFreeImpAt < now)
    {
        cast the Create Imp power at the heart, free;
        nextFreeImpAt = now + TimeBeforeFreeImpGeneratedSeconds × <variable 19>;
    }

**Nothing else touches `nextFreeImpAt`.** Not the count going back up, not the
keeper being served, not the level loading. So a keeper who has been at four
imps for thirty seconds carries a deadline thirty seconds in the past, and the
first frame they drop below the threshold the test is *already* true: the
replacement is immediate. Only the second and later ones wait the interval,
because only they are measured from a deadline that was set recently.

That is the recording, line for line, with no extra rule.

### Three things fall out of the same eight instructions

**The deadline is advanced whether or not the imp is actually made.** The write
follows the call with no test of its result. A keeper with nowhere to put an imp
therefore burns the interval rather than retrying every frame. Nothing
observable turns on it in a shipped level, because a keeper without a dungeon
heart has already lost (Q40), but it is what ours does now.

**A free imp is genuinely free.** The power is cast with a flag that makes the
original skip the spell-availability check, skip the mana check and skip the
deduction. The free four cannot be starved out by a poor keeper.

**`TimeBeforeFreeImpGeneratedSeconds` is multiplied by variable 19** — the
223-reader global that no shipped file supplies, and the biggest open question
in the data (Q132). Seconds go in and engine time comes out. This is the
clearest single use of it found so far, in eight instructions with nothing else
going on, and it says plainly what the multiplier is *for*. It does not say what
the value is.

### And the price of a bought imp is now read rather than reported

The same translation unit holds Create Imp's cost, and it is our formula:

    count = number of creatures of the type this power creates
    steps = count + 1 − MinimumImpThreshold
    if steps < 1 then steps = 1
    cost = steps × the power's base cost

The creature type is read off the power's own record rather than hardcoded,
which is the field this project calls `CreatedCreatureId`. **Q54 was CONFIRMED
from the owner's play; it is now confirmed from the executable as well**, and
the two agree including the clamp, which is the part play could not have shown:
a keeper *below* the floor still pays the base price and never less.

### What is still open about it

**Whether an imp held in the Hand of Evil counts.** The count the supply reads
is a per-creature-type tally kept on the keeper, and that tally moves only when
a thing's **owner** changes — nothing in the pick-up path obviously changes an
owner. So the reading that fits the code is that a **held imp still counts**, and
the portrait's figure, which visibly excludes it, is a different count. That is
STRONGLY INFERRED and it is cheap to settle by watching: hold one of exactly four
imps in the hand for ten seconds and see whether a fifth appears. **Q148.**

**Whether the 1.3–1.5 seconds is simulation or animation.** The original casts a
keeper power, and a keeper power plays a creation effect. The deadline is set at
the moment of the *decision*, not the moment the imp exists, so the delay does
not accumulate across replacements either way — but whether the simulation should
hold a pending imp for a second is unread. Ours creates it immediately. **Q149.**

## Buying the rest

Beyond the floor, an imp must be bought. The owner:

> every keeper gets a spell that summons an imp, every summon costs 1500 mana and
> based on number of imps the cost of a summon goes up by 1500, so one imp costs
> 1500, second costs 3000, third 4500 etc. that way the game ensures you can't
> spam infinte number of imps.

**The 1500 is DK2's own number**, and finding it validated a record we do not
parse. `KeeperSpells.kwd` holds 13 spells in 5278 bytes — 406 bytes each,
exactly — and offset **0x11C** of that record holds the mana cost:

```
  Possession        500      Chicken        10000
  Create Imp       1500      Create Gold    15000
  Sight Of Evil    5000      Turncoat       20000
  Heal             5000      Tremor         30000
  Thunderbolt      6000      Inferno        50000
  Call To Arms    10000      Summon Horny  100000
  OneShotReaper       0
```

Every one of those is plausible, and they rank the way the game plays —
possession is the cheapest thing a keeper does and Horny the dearest. One value
being right could be luck; thirteen in a sensible order, with one of them
independently measured in the game, could not be. Asserted by
`KeeperSpellsOnRealDataTests`.

**The escalation is CONFIRMED (Q54)**, and it took a second message to pin down.
Implemented as `base × (imps − minimum + 1)`, so a keeper on their free four pays
1500 for the fifth, 3000 for the sixth, 4500 for the seventh. The obvious
alternative - `base × (imps bought so far + 1)`, which needs a counter - fits the
same three numbers and differs only after a bought imp dies. The owner settled
it:

> four free (starting) imps don't raise mana cost, they are free, base, so if you
> drop down to 4 (something killed your imps), cost also drops to base 1500

Both halves of that matter. The free four do not count towards the price, and
**the price falls back**, which kills the counter reading outright. So the cost
is a function of the imps a keeper has *right now*, needs no state of its own,
and cannot drift from the roster.

One consequence worth knowing before designing anything around it: losing imps is
expensive in work and free in mana. A keeper who is wiped out gets four back for
nothing and rebuilds the rest from the cheap end of the curve.

## The drain — what mana is actually for

**This was the open question that mattered, and it is now answered and built.**
It started with a screenshot. The mana orb in DK2 shows two rates beneath the
counter, one green and one red. The owner's screenshot of an early dungeon
showed roughly **+53 and −28**, and the tooltip read `Mana held: 15081 / 200000`
— which incidentally put Q37's flat ceiling on screen.

Nothing in this simulation drained mana at all. So what was the 28?

### The measurement

The owner settled it with two frames of the same dungeon:

| | imps | green | red |
|---|---|---|---|
| before | 4 | +150 | **−28** |
| after summoning one | 5 | +150 | **−35** |

**Seven a second, per imp.** One reading would have given the rate; two give the
*unit* as well, because a per-imp charge is the only reading under which the
figure moves by exactly one imp's worth when exactly one imp is added.

The same pair of frames confirmed two more things for free. The second shot's
tooltip reads **"Create Imp. Spell Grade 1. Mana Cost 3000"** with five imps on
the roster, which is `1500 × (5 − 4 + 1)` — the escalation formula of Q54,
photographed. And the arithmetic between the frames closes: mana went 2684 →
2369 across a 1500 spend, so the keeper *gained* **1185**; at +122/s before the
cast and +115/s after that is between 9.7 and 10.3 seconds, and the HUD clock in
the two frames reads 38 and 28. Two numbers measured for different reasons agree
to within a rounding error (Q39).

### Where the 7 lives — and why it took so long to find

**It is not on the creature.** That assumption cost a session. An upkeep charged
per imp ought to sit on the CREATURES record, and a scan of all 5449 bytes of it
for an offset holding the Imp's 7 finds four candidates and not one of them is a
cost:

| offset | what it actually is |
|---|---|
| 0x0311, 0x0312 | inside an ArtResource block — 0x0314 onward is the animation's ASCII name, and the Imp's slot in this one is empty |
| 0x0E08 | reads 7 for **all 46 creatures**, immediately before an array of (spell-id, percent) pairs: an array-length header |
| 0x0EBF | a scattered category flag (Imp, Vampire, Skeleton, Horny, Stone Knight…) |
| 0x1333 | `0xCD` filler — uninitialised memory |

It is not in the balance table either: the only four variables holding 7 are two
reporting distances, a gathering distance and a chase duration, and none of the
five `Unknown_*` variables is 7.

**It is on the spell.** `KeeperSpells.kwd`, offset **0x00E2** of the 406-byte
record:

```
  Create Imp         7      Sight Of Evil   0     Turncoat      0
  Possession        25      Heal            0     Tremor        0
  Call To Arms      90      Thunderbolt     0     Inferno       0
  OneShotReaper    125      Chicken         0
  Summon Horny    2000      Create Gold     0
```

**Non-zero for exactly the five spells whose result persists; zero for all eight
instants.** A spell that is over the moment it lands has nothing to keep, and
every one of them reads zero. No offset picked at random behaves like that.

And the column carries **its own second anchor**: 2000 for Summon Horny is the
figure the community reports for a Horned Reaper's drain, arrived at with no
reference to this file — and it is the very number whose absence from CREATURES
was the stated reason for not implementing this. It was on the spell all along,
sitting beside the answer.

### What it changed

Implemented as `ManaRules.ImpUpkeepPerSecond` and
`ManaRules.UpkeepPerSecondByPlayerIndex` (ADR-0022). Income and upkeep are kept
apart all the way through rather than netted, because the original shows them
apart and because `MaximumManaGainPerSecond` (500) caps only the income half —
netting first would let a large enough workforce raise a keeper's income cap.

Every imp is charged, **including the free four**: the owner's starting
complement drains 28, so the floor is free to fill and not free to keep. That is
the pressure the whole imp economy is built on, and its absence is why a keeper
here could previously buy imps until the map was full.

**It also closed the project's last hardcoded balance constant.** Chasing the
upkeep into `KeeperSpells.kwd` forced a parser to exist, and `CreateImpBaseCost`
became a lookup in the same change. The exception granted to that 1500 lasted
exactly one session, which is the right lifetime for an exception.

### What is still open

- **Q57 — what happens when a keeper cannot pay.** Ours floors mana at zero and
  carries on. `InsufficientManaImpEvaluationPeriodSeconds` (10) says the original
  re-evaluates imps on a ten-second clock when mana is short; the obvious guess
  is that imps start dying, and it is a guess.
- **Q58 — possession.** The data says 25 a second and the owner saw no drain at
  all. `ManaCostInFirstPersonPerSecond` says 5, so the data disagrees with itself
  before it disagrees with the observation. The likely explanation is that the
  mana orb is not on screen in first person, which would make the observation an
  absence of HUD rather than an absence of drain — but nothing here depends on
  it, and possession arrives at M7.
- **Q59 — traps.** CONFIRMED by the owner: spike traps and sentry cannons each
  raised the red figure when built, so a trap is a standing charge and not only a
  per-trigger cost. `Traps.kwd` is 16 records over 9264 bytes and unparsed. One
  measured trap would anchor the same scan that found this — `dk2tools kwd
  recscan Traps.kwd <n>` exists for it.
- **The other four upkeeps.** Summon Horny's 2000 a second and Call To Arms' 90
  are parsed and read by nothing, because neither spell exists (M7).

---

## What is not implemented, and is not guessed at

**Everything else that spends mana.** Create Imp is the only spell implemented,
because it is the only one whose effect is a mechanic that already exists.
Possession, Call to Arms, Sight of Evil and the rest arrive at M7. Two fields of
`KeeperSpells.kwd` are parsed now - the cast cost at 0x11C and the upkeep at
0x00E2 - which means the upkeeps of the four other persistent spells are sitting
in the database being read by nothing, waiting for the spells themselves.

**What traps draw** (Q59). Confirmed, measured and located - `Traps.kwd`
offset 0x0202, pinned by a real-data test - but not implemented, because a trap
is an object and objects arrive at M6.

**Death.** `WorldState.RemoveCreature` exists so that an imp count can fall and
the floor can be seen to work, and nothing calls it. Dying in DK2 is a process —
drop what you carry, lie there for `CreatureDyingStateDurationSeconds` (60), be
dragged to a graveyard — and none of that is modelled.

**Dropping an imp into the dungeon heart.** The owner's experiment was to pick
imps up and drop them into the heart, which kills them. Picking creatures up is
the keeper's hand, which is an M4 mechanic; the *consequence* is what is tested
here, via `RemoveCreature`.

**The Receive Imps special** (`SpecialReceiveImpsAmount` = 10) and the imp
population countdown (`ImpPopCountdownTimeSeconds` = 10). Both parsed, neither
read.

---

## How this was established

1. The owner described the free four, the five-second respawn and the escalating
   summon cost from playing the original (2026-08-30).
2. `MinimumImpThreshold` (4) and `TimeBeforeFreeImpGeneratedSeconds` (5) were
   already parsed and already listed in `imp-tasks.md` as "read by nothing". They
   turned out to name exactly what was described.
3. `KeeperSpells.kwd` was scanned for multiples of 1500. Offset 0x11C of the
   406-byte record held 1500 for Create Imp and plausible costs for every other
   spell, which is how a record nobody parses gave up one field.
4. The mana drain was researched on the owner's request. The community figure of
   7 mana per imp per turn was cross-checked against the Receive Imps special
   (10 imps, +70) and against the owner's screenshot (4 imps, −28).
5. The CREATURES record was scanned for an offset holding the Imp's 7 *and* the
   Horned Reaper's reported 2000. **Nothing matched**, so the figure was recorded
   rather than implemented, and the mechanic sat unbuilt for a session.
6. The owner then measured it properly: two screenshots, **four imps at −28 and
   five at −35**. That turned 7 from a community claim into a confirmed number
   and, more usefully, removed the need for the second anchor.
7. CREATURES was rescanned with **7 as the sole anchor**, on the theory that the
   unverified 2000 had been the half that poisoned the search. It had not: all
   four Imp=7 offsets turned out to be an ArtResource frame count, an
   array-length header and uninitialised padding. **A complete negative** — the
   upkeep is not on the creature.
8. `KeeperSpells.kwd` was scanned instead, anchored on Create Imp. **Offset
   0x00E2 held 7**, and the column held 2000 for Summon Horny — the community's
   Horned Reaper figure, which had been the right number in the wrong record all
   along. Zero for all eight instantaneous spells, non-zero for all five
   persistent ones.
9. Implemented, with `KwdKeeperSpells` parsing both 0x00E2 and 0x11C, which also
   retired the project's last hardcoded balance constant.

**The lesson worth keeping.** Step 5 failed because it required *two* things to
be true, and one of them was a forum post. A search that will only report success
on an unverified premise cannot report a useful failure — it had, in fact, found
nothing because it was looking in the wrong file, and the bad anchor hid that.
One measured number (step 6) let the same technique run on one leg, and the
negative it produced was then trustworthy enough to act on. **Anchor a scan on
what you measured, and use the community figure to confirm the hit, never to
qualify it.**

The tooling from steps 7 and 8 was kept: `dk2tools kwd creaturescan`,
`kwd creaturehex` and the general `kwd recscan <file> <value> [record]`. The
last is what `Traps.kwd` needs (Q59).

Sources for step 4, both community rather than data:
[Dungeon Keeper Wiki — Mana](https://dungeonkeeper.fandom.com/wiki/Mana),
[GameFAQs — mana upkeep](https://gamefaqs.gamespot.com/boards/176198-dungeon-keeper-2/43507970).
Treated as leads to verify, not as facts. Both turned out to be right, and
neither was believed until the file said so: the imp figure was confirmed by
measurement first and by DK2's own record second, and the Horned Reaper figure
arrived as corroboration once the offset was already found.
