# Imps, and what mana is actually for

Milestone M3. Where imps come from, what they cost to buy, and what they cost to
keep — the last of which turns mana from a number that only goes up into an
economy. Mana's *income* side is `mana.md`.

**Source(s).** Owner observation and measurement of the original (2026-08-30,
2026-09-05, 2026-09-07); the shipped VARIABLES and `KeeperSpells.kwd` records;
`DKII.EXE` v1.70 (`Player.cpp`'s keeper update).

**Implementation.** `ImpSupplyRules`, `ManaRules`, `Systems.ImpSupplySystem`,
`Systems.ManaSystem`, `Commands.ImpCommands.CreateImpCommand`. Decisions:
ADR-0022, ADR-0057.

**Tests.** `ImpSupplyTests`, `ManaTests`, `ManaUpkeepTests`
(`tests/DK2.Simulation.Tests`); `ImpsOnRealDataTests`, `ManaOnRealDataTests`,
`KeeperSpellsOnRealDataTests`, `TrapsOnRealDataTests`
(`tests/DK2.Behavior.Tests`); `scenarios/imps/a_keeper_never_runs_out_of_imps.yaml`.

---

## Summary of confidence

| Claim | Status |
|---|---|
| A keeper always has at least 4 imps | **CONFIRMED** — owner observation, and `MinimumImpThreshold` is 4 |
| A missing imp comes back every 5 seconds | **CONFIRMED** — owner measured it; `TimeBeforeFreeImpGeneratedSeconds` is 5. But the **first** one comes back at once — see below |
| A keeper starts a level with 4 | **CONFIRMED** — owner; falls out of the floor above without a separate rule |
| Create Imp costs 1500 mana | **CONFIRMED** — owner, and it is the cost on DK2's own Create Imp record |
| The cost rises by 1500 each time, and falls back when imps die | **CONFIRMED TWICE** (Q54) — the owner from play, and then the binary: the original computes `base × max(1, imps − MinimumImpThreshold + 1)`, our formula operation for operation |
| Imps are created at the dungeon heart | **STRONGLY INFERRED** — and a keeper without one has already lost (Q40) |
| **Imps cost 7 mana a second each to keep** | **CONFIRMED, BUILT** (Q53, ADR-0022) — owner measured 28 at four imps and 35 at five, and 7 is the upkeep on DK2's own Create Imp record |
| Traps draw mana continuously | **CONFIRMED, parsed, not charged** (Q59) — five traps measured and `Traps.kwd` offset `0x0202` reproduces all five |
| A claimed mana vault is worth +100 a second | **CONFIRMED** (Q37b) — owner, and `TerrainDef.ManaGain` for Claimed Vault is 100; see `mana.md` |
| Possession drains 25 a second | **UNKNOWN** (Q58) — `KeeperSpells` `0x00E2` says 25, `ManaCostInFirstPersonPerSecond` says 5, and the owner saw no drain at all |
| **The free-imp clock is a deadline, not a countdown** | **CONFIRMED** (Q56, ADR-0057) — owner recording, then the binary. A keeper at full strength is never owed one, their deadline goes stale, and the next loss is replaced **on the spot** |
| A free imp costs no mana and ignores availability | **CONFIRMED** — the free flag makes the original skip the availability check, the affordability check and the deduction |
| An imp held in the Hand of Evil still counts towards the four | **CONFIRMED** (Q148) — owner: holding one of four produces no fifth. The imp portrait's figure is a *different* count and excludes them |

---

## The free four

The owner described the behaviour from the game:

> every keeper gets 4 imps at the start of the game, and 4 imps is the minimum,
> if I drop all imps to a dungeon heart they will auto respawn in approx 5 second
> intervals

and the shipped balance table names both halves, in its own words, with exactly
those numbers:

```
   94  MinimumImpThreshold                            4
   95  TimeBeforeFreeImpGeneratedSeconds              5
```

**Note what it does not need.** The owner's "every keeper gets 4 at the start"
needs no rule of its own: a keeper who begins with none is below the floor, so the
floor fills them in. Whether the original hands them over instantly at level load
or generates them at the same five-second interval is not distinguished by
anything observed, and ours does the latter.

Three more imp variables are parsed and read by nothing:

```
  110  InsufficientManaImpEvaluationPeriodSeconds    10
  144  ImpPopCountdownTimeSeconds                    10
  188  SpecialReceiveImpsAmount                      10
```

The first is a clue about the drain below: a game that has a period on which to
re-evaluate imps *when mana is insufficient* is a game in which imps cost mana.
The third matches the community's description of the Receive Imps special exactly —
it grants ten.

## The clock is a deadline, and the difference is visible

**CONFIRMED (Q56, ADR-0057).** Not a countdown held at zero while a keeper is up
to strength and restarted from full when they fall short — that is what this
project ran for three weeks under a comment claiming the two were
"indistinguishable in the steady state". They are distinguishable on the first
replacement.

### What the owner filmed

Recorded 2026-09-05:

- the keeper sat at **exactly four imps for about thirty seconds**;
- one imp was picked up and dropped into the dungeon heart — about half a second;
- the replacement appeared **about 1.3–1.5 seconds later**, not five;
- with several imps missing, the ones after it came at **about five-second
  intervals**;
- imps held in the Hand of Evil are **not counted** in the imp portrait's figure.

### What the binary says

The keeper's per-frame update in `Player.cpp` holds one field for this and writes
it in exactly one place. In our own words:

    if (number of creatures of type Imp < MinimumImpThreshold
        and this keeper's nextFreeImpAt < now)
    {
        cast the Create Imp power at the heart, free;
        nextFreeImpAt = now + TimeBeforeFreeImpGeneratedSeconds × <variable 19>;
    }

**Nothing else touches `nextFreeImpAt`.** Not the count going back up, not the
keeper being served, not the level loading. So a keeper who has been at four imps
for thirty seconds carries a deadline thirty seconds in the past, and the first
frame they drop below the threshold the test is *already* true: the replacement is
immediate. Only the second and later ones wait the interval, because only they are
measured from a deadline that was set recently. That is the recording, line for
line, with no extra rule.

### Three things fall out of the same eight instructions

**The deadline is advanced whether or not the imp is actually made.** The write
follows the call with no test of its result, so a keeper with nowhere to put an imp
burns the interval rather than retrying every frame. Nothing observable turns on it
in a shipped level, because a keeper without a dungeon heart has already lost
(Q40), but it is what ours does.

**A free imp is genuinely free.** The power is cast with a flag that makes the
original skip the spell-availability check, skip the mana check and skip the
deduction. The free four cannot be starved out by a poor keeper.

**`TimeBeforeFreeImpGeneratedSeconds` is multiplied by variable 19** — the engine's
ticks per game second, **4** on every shipped level (Q1, Q132, ADR-0089; the level
loader sets it from the LEVEL record's `ticksPerSec` field). Seconds go in and
ticks come out, so the deadline is the shipped seconds times four, in ticks. When
it was found this was the clearest single use of the variable — eight instructions
with nothing else going on.

### And the price of a bought imp is read rather than reported

The same translation unit holds Create Imp's cost, and it is our formula:

    count = number of creatures of the type this power creates
    steps = count + 1 − MinimumImpThreshold
    if steps < 1 then steps = 1
    cost = steps × the power's base cost

The creature type is read off the power's own record rather than hardcoded, which
is the field this project calls `CreatedCreatureId`. **Q54 was CONFIRMED from the
owner's play and is now confirmed from the executable as well**, and the two agree
including the clamp, which is the part play could not have shown: a keeper *below*
the floor still pays the base price and never less.

### An imp in the hand counts (Q148)

CONFIRMED by the owner — holding one of exactly four imps produces **no fifth
imp**. The count the supply reads is a per-creature-type tally kept on the keeper,
moved in exactly two places: when a thing gains an owner, and when it loses one.
Picking a creature up does not change whose it is, so the tally never notices. The
behaviour is confirmed and the mechanism STRONGLY INFERRED, and the two were
arrived at independently.

So **DK2 keeps two counts of a keeper's imps for two purposes**: this one, which
includes held imps, and the imp portrait's figure, which visibly excludes them.
Reusing the visible count would be the obvious thing to do and would be wrong —
you could hold an imp indefinitely and farm a fifth.

**Whether the 1.3–1.5 seconds is simulation or animation is open (Q149).** The
original casts a keeper power and a keeper power plays a creation effect. The
deadline is set at the moment of the *decision*, not the moment the imp exists, so
the delay does not accumulate across replacements either way; whether the
simulation should hold a pending imp for a second is unread. Ours creates it
immediately.

## Buying the rest

Beyond the floor, an imp must be bought. The owner:

> every keeper gets a spell that summons an imp, every summon costs 1500 mana and
> based on number of imps the cost of a summon goes up by 1500, so one imp costs
> 1500, second costs 3000, third 4500 etc. that way the game ensures you can't
> spam infinte number of imps.

**The 1500 is DK2's own number**, and finding it validated a record we did not
parse. `KeeperSpells.kwd` holds 13 spells in 5278 bytes — 406 bytes each, exactly —
and offset **0x11C** of that record holds the mana cost, 32 bits wide (Q55):

```
  Possession        500      Chicken        10000
  Create Imp       1500      Create Gold    15000
  Sight Of Evil    5000      Turncoat       20000
  Heal             5000      Tremor         30000
  Thunderbolt      6000      Inferno        50000
  Call To Arms    10000      Summon Horny  100000
  OneShotReaper       0
```

Every one of those is plausible, and they rank the way the game plays — possession
is the cheapest thing a keeper does and Horny the dearest. One value being right
could be luck; thirteen in a sensible order, with one of them independently
measured in the game, could not be. Asserted by `KeeperSpellsOnRealDataTests`.

**The escalation is CONFIRMED (Q54).** Implemented as
`ImpSupplyRules.CreateImpCost`, `base × (imps − minimum + 1)`, so a keeper on their
free four pays 1500 for the fifth, 3000 for the sixth, 4500 for the seventh. The
obvious alternative — `base × (imps bought so far + 1)`, which needs a counter —
fits the same three numbers and differs only after a bought imp dies. The owner
settled it:

> four free (starting) imps don't raise mana cost, they are free, base, so if you
> drop down to 4 (something killed your imps), cost also drops to base 1500

Both halves matter. The free four do not count towards the price, and **the price
falls back**, which kills the counter reading outright. So the cost is a function
of the imps a keeper has *right now*, needs no state of its own, and cannot drift
from the roster. One consequence worth knowing before designing anything around
it: losing imps is expensive in work and free in mana. A keeper who is wiped out
gets four back for nothing and rebuilds the rest from the cheap end of the curve.

### Where a bought imp may land, and what it does first (ADR-0312)

**The cast goes through the position permission** (`0x004BB8A0`, `keeper-spell-casting.md`
§3). The Create Imp record selects location 1 and target 4 (`KeeperSpells.kwd`, bytes `0xEF`
and `0xDA`), so the tile must be the caster's own or an ally's — never neutral or unowned — and
not solid, with the room exclusions the other place casts share. **CONFIRMED by the seeded
replays of `l2live2`:** the recording casts six times at (14,24), (14,23), (12,20) and (11,20)
between ticks 2158 and 2270. Seed 02, where every one of those tiles was still neutral dirt,
made no imp; seed 03 made one, on the one tile the keeper had claimed; seed 04, with all six
claimed, made six. The row must also be AVAILABLE: Level 2 researches the spell, and its script
waits on that (trigger 112, type 29, spell 4). Built in `ImpSupplySystem.Buy`; a table without
the row is read as available, as a fixture database is.

**The new imp is put down as the hand puts one down.** DK2's imps made at (14,24) pass states
214, 11 and 10 and spend the drop's one look around the landing tile — they reinforce the walls
beside them rather than walk nine tiles to the one marked wall. `Spawn` writes the same fields
the drop writes (`JustDropped`, the kind just done zeroed, no chooser cap), for bought and free
imps alike. The two ticks of 214 are not modelled: ours decides one tick earlier (HYPOTHESIS that
nothing depends on it).

## The drain — what mana is actually for

The mana orb in DK2 shows two rates beneath the counter, one green and one red.
The owner's screenshot of an early dungeon showed roughly **+53 and −28**, with
the tooltip `Mana held: 15081 / 200000` — which incidentally put Q37's flat
ceiling on screen. Nothing in this simulation drained mana at all, so what was the
28?

### The measurement

The owner settled it with two frames of the same dungeon:

| | imps | green | red |
|---|---|---|---|
| before | 4 | +150 | **−28** |
| after summoning one | 5 | +150 | **−35** |

**Seven a second, per imp.** One reading would have given the rate; two give the
*unit* as well, because a per-imp charge is the only reading under which the figure
moves by exactly one imp's worth when exactly one imp is added.

The same pair of frames confirmed two more things for free. The second shot's
tooltip reads **"Create Imp. Spell Grade 1. Mana Cost 3000"** with five imps on the
roster, which is `1500 × (5 − 4 + 1)` — the escalation formula of Q54, photographed.
And the arithmetic between the frames closes: mana went 2684 → 2369 across a 1500
spend, so the keeper *gained* **1185**; at +122/s before the cast and +115/s after
that is between 9.7 and 10.3 seconds, and the HUD clock in the two frames reads 38
and 28. Two numbers measured for different reasons agree to within a rounding error
(Q39).

### Where the 7 lives

**It is not on the creature.** An upkeep charged per imp ought to sit on the
CREATURES record, and a scan of all 5449 bytes of it for an offset holding the
Imp's 7 finds four candidates and not one of them is a cost:

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
instants.** A spell that is over the moment it lands has nothing to keep, and every
one of them reads zero. No offset picked at random behaves like that. The column
carries **its own second anchor**: 2000 for Summon Horny is the figure the community
reports for a Horned Reaper's drain, arrived at with no reference to this file — and
it is the very number whose absence from CREATURES was the stated reason for not
implementing this.

**Trap: anchor a scan on what you measured, and use the community figure to
confirm a hit, never to qualify it.** The CREATURES scan was run needing *two*
things to be true — the Imp's 7 and a forum post's 2000 — so it could not report a
useful failure. Re-run on the measured 7 alone it produced a trustworthy negative,
and `KeeperSpells.kwd` was then scanned instead. The tooling was kept:
`dk2tools kwd creaturescan`, `kwd creaturehex`, and the general
`kwd recscan <file> <value> [record]`.

### What it changed

Implemented as `ManaRules.ImpUpkeepPerSecond` and
`ManaRules.UpkeepPerSecondByPlayerIndex` (ADR-0022), charged for anything carrying
`IsWorker` rather than for the Imp by name.

**Income and upkeep stay separate for the income cap and are netted for the
ceiling**, and both halves are observed. `MaximumManaGainPerSecond` (500) caps the
green figure alone, so subtracting upkeep first would let a large enough workforce
raise a keeper's income cap; but the owner's screenshot of a full keeper reads
exactly 200000 with +400 and −297 on the orb, so income beating upkeep pins them at
the top rather than 297 short. `ManaRules.ApplyOneSecond` therefore nets and *then*
clamps, in that order.

Every imp is charged, **including the free four**: the owner's starting complement
drains 28, so the floor is free to fill and not free to keep. That is the pressure
the whole imp economy is built on, and its absence is why a keeper here could
previously buy imps until the map was full.

It also closed the project's last hardcoded balance constant: chasing the upkeep
into `KeeperSpells.kwd` forced a parser to exist, and `CreateImpBaseCost` became a
lookup in the same change.

### What is still open

- **Q57 — mana exhaustion.** The warning, selection and countdown rules are now
  STRONGLY INFERRED from the binary and tested independently; see
  `imp-mana-removal.md`. Live integration and original observation remain open.
  Imps have no gold wage or gold upkeep (owner, 2026-09-07; `payday.md`).
- **Q58 — possession.** `KeeperSpells` `0x00E2` says 25 a second, and
  `ManaCostInFirstPersonPerSecond` says 5, so the data disagrees with itself before
  it disagrees with the owner's report of no drain at all. The likely explanation is
  that the mana orb is not on screen in first person, which would make the
  observation an absence of HUD rather than an absence of drain. Nothing here
  depends on it. Possession is built (ADR-0350, Q58): an Imp's charge is 0.
- **Other spell upkeeps.** Normal Horny is charged; the one-shot variant is excluded
  from that binary path (`keeper-spell-reaper.md`). Possession's per-creature upkeep is built (`keeper-spell-possession.md` §Upkeep).
  Call To Arms' 90 is charged (`call-to-arms.md`, ADR-0279).

---

## What is not implemented, and is not guessed at

**Everything else that spends mana.** Create Imp is the only spell implemented,
because it is the only one whose effect is a mechanic that already exists.
Thunderbolt, Call to Arms, Heal, Inferno and Chicken followed (ADR-0260, ADR-0279, ADR-0287, ADR-0302); Possession, Sight of Evil and the rest have not. Two fields of
`KeeperSpells.kwd` are parsed — the cast cost at `0x11C` and the upkeep at `0x00E2`
(Q55) — so the upkeeps of the four other persistent spells sit in the database
being read by nothing, waiting for the spells themselves.

**What traps draw is CONFIRMED, parsed, and charged to nobody** (Q59).
`KwdTraps` reads `Traps.kwd`'s 16 records and takes the upkeep from offset
`0x0202`; `TrapsOnRealDataTests.EachMeasuredTrapDrainsWhatTheOwnerSaw` pins the
owner's five measurements — Spike 5, Sentry 5, Fireburst 10, Freeze 10, Lightning
25 — and `AGuardPostIsTheOnlyFreeEntry_BecauseItIsNotATrap` pins the one zero.
`ManaRules.UpkeepPerSecondByPlayerIndex` counts workers only, so a built trap
costs a keeper nothing here.

**The Receive Imps special** (`SpecialReceiveImpsAmount` = 10) and the imp
population countdown (`ImpPopCountdownTimeSeconds` = 10). Both parsed, neither
read; `ImpsOnRealDataTests` pins the values so the gap stays deliberate.

**Death is built and the mana consequence is not.** A creature at zero health lies
for a window in which an imp may carry it away and then dies (Q99, ADR-0051,
`creature-death.md`), so an imp count can fall for real and the floor can be seen
to work. What the original does to a keeper whose mana cannot cover their imps is
Q57, above.

**Dropping an imp into the dungeon heart**, which is what the owner's experiment
did. The hand is built (`hand-of-evil.md`), and a creature dropped into a *portal's*
hole leaves the world (Q88), but nothing here kills a creature dropped into a
heart.
