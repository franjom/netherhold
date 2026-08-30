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
| A missing imp comes back every 5 seconds | **CONFIRMED** — owner measured it; `TimeBeforeFreeImpGeneratedSeconds` is 5 |
| A keeper starts a level with 4 | **CONFIRMED** — owner; falls out of the floor above without a separate rule |
| Create Imp costs 1500 mana | **CONFIRMED** — owner, and it is the cost on DK2's own Create Imp record |
| The cost rises by 1500 each time, and falls back when imps die | **CONFIRMED** (Q54) — the free four are free, and the price is a function of the imps a keeper has, not of what they have bought |
| Imps are created at the dungeon heart | **STRONGLY INFERRED** — and a keeper without one has already lost (Q40) |
| **Imps cost about 7 mana a second each to keep** | **STRONGLY INFERRED, NOT IMPLEMENTED** — Q53, and the number is in no file we parse |
| Traps draw mana continuously | **REPORTED, not investigated** — Q53 |
| Whether the free-imp clock runs while a keeper is at full strength | **UNKNOWN** (Q56) — ours runs only while short |

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

**This is the open question that matters**, and it started with a screenshot. The
mana orb in DK2 shows two rates beneath the counter, one green and one red. The
owner's screenshot of a level-1-ish dungeon showed roughly **+53 and −28**, and
the tooltip read `Mana held: 15081 / 200000` — which incidentally put Q37's flat
ceiling on screen.

Nothing in this simulation drains mana at all. So what is the 28?

**Community answer: imps cost 7 mana a second each.** Two independent search
results agree, and one of them carries a cross-check that is not circular — the
Receive Imps special grants ten imps and is described as raising consumption by
**70 a turn**, which is 7 × 10 and matches our own parsed
`SpecialReceiveImpsAmount` of 10.

**And the arithmetic lands on the screenshot.** A keeper's starting complement is
four imps. 4 × 7 = **28**. That is a third line of evidence, independent of the
community source and of the data, arriving at the same number.

Together with `InsufficientManaImpEvaluationPeriodSeconds` — a variable that only
makes sense in a game where imps cost mana — that is enough to call the mechanic
STRONGLY INFERRED.

### Why it is not implemented

**The 7 is in no file we can read.** Searched:

- the balance table: no variable holds 7 in any imp- or mana-related role
  (the four that hold 7 are gathering and reporting distances, and a chase
  duration);
- the CREATURES record: **no offset holds the Imp's 7 against the Horned Reaper's
  reported 2000**, which is the test that would have found a per-creature mana
  upkeep field. Two independent anchors, and nothing matches both.

So implementing it means writing 7 into simulation code, and this project treats
a hardcoded balance constant as a bug. There is exactly one such exception in the
codebase — Create Imp's 1500 — and it earns its place by being read back out of
the real file by a test. 7 cannot be, because there is no file to read it from.

**What would settle it**, cheapest first:

1. **Two screenshots.** Note the red figure with four imps, summon a fifth, and
   note it again. 28 → 35 confirms 7 per imp and confirms the reading is per
   second. Anything else is more interesting still.
2. **Where the number lives.** If it is per creature it is in CREATURES at an
   offset we have not identified; if it is global it is in one of the four
   `Unknown_*` variables (17, 61, 62, 63, 233) or in a variable whose name we
   have misread. A second anchor — what a Horned Reaper actually drains, measured
   rather than quoted — would find it the same way the Create Imp cost was found.
3. **Traps.** The owner reports traps draw mana continuously as well as per
   trigger. `Traps.kwd` is 16 records over 9264 bytes and is unparsed; the same
   anchor-and-scan that found the spell costs would find a trap's mana field, but
   it needs a known number to anchor on.

### What its absence costs

A dungeon full of imps is free to run here and is not free in the original. That
is not a rounding error — it is the whole reason a DK2 keeper cannot simply buy
imps until the map is full, and it is why `InsufficientManaImpEvaluationPeriodSeconds`
exists. Every scenario run that earns mana now says so.

---

## What is not implemented, and is not guessed at

**Imp upkeep**, above. The largest gap in the mana economy.

**Everything else that spends mana.** Create Imp is the only spell implemented,
because it is the only one whose effect is a mechanic that already exists.
Possession, Call to Arms, Sight of Evil and the rest arrive at M7 with the
SPELLS record; `KeeperSpells.kwd` is unparsed and its 0x11C is the only field
anybody here has identified.

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
5. The CREATURES record was then scanned for an offset holding the Imp's 7 and
   the Horned Reaper's reported 2000. **Nothing matched**, which is why the
   figure is recorded rather than implemented.

Sources for step 4, both community rather than data:
[Dungeon Keeper Wiki — Mana](https://dungeonkeeper.fandom.com/wiki/Mana),
[GameFAQs — mana upkeep](https://gamefaqs.gamespot.com/boards/176198-dungeon-keeper-2/43507970).
Treated as leads to verify, not as facts: nothing from either is implemented, and
what was checkable against our own data (the Receive Imps count) checked out.
