# Mechanic: creature levels — what a level does, and what raises one

Source(s):
- The reference install's `GlobalVariables.kwd` (the 32 × 9 stat table) and
  `Creatures.kwd`, via `DK2.Data`
- `DKII.EXE` v1.70, the experience adder `FUN_00457880` and the creature loader
  `FUN_00492320`, read 2026-09-07 (`training.md` §7 has the adder clause by
  clause)
- Owner observation, 2026-09-05 (that imps level by working, and the two room caps)
- OpenKeeper `Variable.CreatureStats.StatType`, for the stat names only
  (ADR-0003's second amendment)

Original version: DK2 v1.70

A level multiplies a creature's stats by percentages the shipped data states
outright. Both halves are implemented: an imp earns experience by working and a
non-worker earns by training. Nothing earns by fighting, because combat does not
exist (M7).

Confidence: **CONFIRMED** for the table, the indexing and the adder's clauses;
**CONFIRMED (owner)** that levelling happens at all.

**The runtime side was read on 2026-09-15 and agrees** (`combat.md` §4). The 32 x 9
table lives in the cached global block at `[0x006E4154]` = `0x006F2550`, one row of
nine `int16` per stat at `+0xE1 + (stat - 1) * 18`, and every consumer applies it the
same way: level 1 uses the record value unchanged, and any other level takes
`record * percent / 100` through a real signed divide. `CreatureLevelRules.Scale` and
`GameDatabase.StatPercent` match that instruction for instruction, sign included.
Two stat rows were read off their consumers and four more were **predicted from the
stride and then found**, so the arithmetic is tested rather than fitted.

**One path here is missing: a level can go down** — `FUN_0048AFA0` decrements
`creature+0x403`, and `CreatureLevelRules` only grants. **Q328, answered
2026-09-16**, and the framing was wrong: two of its three call sites are not a
penalty but a **level setter**, §2.1. The third is vampire reincarnation
(`combat.md` §6.3).

---

## 2.1 The Hero Lair sets a level rather than costing one (Q328)

`FUN_0048AFA0` has **three** callers, not the two `combat.md` §4 recorded: the
reincarnation routine, `FUN_0049C2A0`, and a third at `0x005143C6` in a region
Ghidra had not attributed.

The two new ones are the same mechanism written twice. Each resolves the tile
under a thing through virtual slot `+0x1D4` and proceeds only when that tile's
id at `+0x0A` is **`0x23` — terrain 35, `Hero Lair`**. Each then computes a
target level from a byte at `thing+0x14A`, reduced `mod 10` and biased into 1–10,
and **walks the creature's level toward it**: `FUN_0048AFA0` while it is too
high, and the increment `FUN_0048B120` applied repeatedly while it is too low —
nine times from level 1 at one site, ten at the other. Both re-read the level and
loop until it matches.

So the decrement is a **step in an assignment**, not a punishment, and a hero
arriving at its lair is levelled to whatever that lair carries. The two sites
bias the `mod 10` differently (`+9` against `+1`), so they do not agree on the
target for the same byte; which object each reads `+0x14A` from is not settled
here, and that difference is why this is not yet a rule.

`FUN_0049C2A0` itself is a **hit reaction**, reached from `FUN_004C0FE0` and
taking an angle: it plays sound `0xC5`, increments a per-player counter at
`player+0xEE6`, and in its default arm builds a knockback from the sine and
cosine tables at `0x006C1488` and `0x006C1C88` before applying it. The level arm
is one of three it dispatches on a thing-class byte at `+0x12C`.

---

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

`GlobalVariables.kwd` has carried all 320 rows since variables were first parsed;
nothing read them until 2026-09-05, so every stat in the simulation was silently
the level-1 value. The binary agrees a level is per-creature: `Creature.cpp`'s
debug dump prints `Level` and `ExperiencePoints` as instance fields.

**A worker's work rate is NOT one of the levelled stats** (owner, 2026-09-05):
a level-3 imp and a freshly created level-1 imp reinforce the same wall in
13.1 s and 13.3–13.4 s. The prediction that it *would* scale came from reading
the level-up routine as adding a level-scaled stat to the work-rate field, and
the stopwatch refused it on a 13-second job that could not have hidden a 25%
change. See `tile-work.md`, "The work rate does NOT change with level".

**Ten of the 32 stats are 100% at every level** — own-land regeneration among
them, so an Imp heals at 67 a second at level 1 and at level 10. "Levels scale
everything" would be wrong about a third of the table, which is the same shape
as mana (three terrains of 41), own-land regeneration (two creatures of 46) and
terrain health deltas (three of 41). *Read the whole column before believing a
rule is general.*

**Stat names are OpenKeeper's**, and every one this project uses is corroborated
by the shape of the shipped column: the things called Health and MeleeDamage rise
to 600%, the things called Speed and RunSpeed rise to 250% together, and the
thing called ExperienceForNextLevel rises to 1000%. Stat ids **15, 16, 17, 18**
are pinned from the binary as well (`training.md` §5.1).

### 2.1 A quirk worth keeping

Stats **10, 11 and 14** read 100% at every level *except* **level 4**, where they
drop to **25%, 20% and 20%**. A three-field dip at one level looks like a
data-entry accident. It is recorded rather than smoothed: this project already
deliberately disbelieves one shipped value (ADR-0023) and the rule there was to
prove the correction from behaviour, which nobody has done for these.

## 3. Implementation

### Promotion sparkles (2026-10-23)

CONFIRMED by a fresh whole read of `FUN_0048B120` and the request assembly
at0x0048B294–0x0048B2B6: every successful level increment requests effect339
(0x153) through view slot+0x38C, after health rescaling and before the pit-cap
state change. It passes the creature's owner word+0x28, position+0x1A and
heading+0xF4. This is a point request, without creature attachment or an
imp-specific branch. The level10/finished guards precede it. Sound0x349 is a
separate subsequent request; this visual change does not implement sound.

Shipped effect339, "Level up Effect", has generation type4, flags0x4480,
HP32, count3 and generated ids554–557. Random-slot flag0x400 selects one of
the four per pass; at full particle detail and nominal50ms updates this emits
96 sparkles over1.6s. Flag0x4000 scales the native pass count by
global0x0069752C/255, rounded down with a minimum of one for a nonzero count.
The modern consumer uses the full authored count rather than that legacy setting.
The shared generator reading is [004386F0](functions/004386F0.md).
All four use `gsparkle`, art flags0x24, element flag0x8 (dim RGB), HP36–50,
scale409–614 and horizontal/vertical speeds−819–819. Source RGB values are
180/180/250,180/70/100,120/250/120 and255/40/250. None selects the owner
palette. The origin ring is10–40 and height90–150 in the effect's authored
units. With a final2.5s particle lifetime, the nominal envelope is about4.1s;
native frame pacing is not measured by this calculation.

BUILT through `StateHookContext.RequestEffect` in the shared level increment,
including script-created hook contexts; detached setup contexts have no sink.
The existing asset provider and effect consumer retain the authored resources,
motion, colour fade and lifetime. No gameplay random draw is added.
Verification: simulation guards/position/heading, earned and granted levels,
scripted multi-level grants, and the installed-data particle integration test.
The isolated Unity `CreatureLevelEffectProbe` promotes an imp through the grant
command and captures0–5s; observed peak96 particles and none by4001ms for
seed42. The captured coloured sparkle cluster visually matches the supplied
DK2 sequence; pixel parity and native timing remain unmeasured.

### Level and experience state

`CreatureLevelRules` (`Clamp`, `Scale`, `Percent`, `MaxHealth`, `SpeedRaw`,
`WorkExperiencePerHit`, `Grant`, `ExperienceForNextLevel`), plus
`CreatureState.Level` and `ExperiencePoints`, both hashed and snapshotted.

- **A promotion** goes through `CreatureLevelRules.LevelUp`, DK2's `0x0048B120`, from
  `Grant` as from the party spawn: health keeps its share of the maximum across the level
  (the adder calls it at `0x004579D4`; ADR-0234).
- **Maximum health** is the record's `Hp` scaled by the Health row.
  `CreatureHealthRules.MaxHealth` takes the database and the state; the old flat
  signature was **removed rather than kept as an overload**, because a call that
  forgot the level would silently cap a level-10 Goblin at 1500 health instead of
  9000.
- **Speed does not scale with level** (CONFIRMED, ADR-0518; corrected 2026-10-19). The
  Speed and RunSpeed rows have no reader: `Creature_SetSpeedMode` (`0x004887D0`) takes
  the speed from the definition (`creature+0x370`, then `+0x5B4`/`+0x5B8`/`+0x5BC`), one
  record shared by every creature of a type, and `Creature_LevelUp` (`0x0048B120`)
  rescales health only. In the Level 4 capture a walking Imp steps 0.4617 tiles a tick at
  every level from 1 to 8, and nine creature types keep one walking and one running speed
  across their levels. **Higher-level Imps are faster** (the owner's 2026-09-04
  observation stands) **because of Imp Haste** (for the Imp; other casters of `0x0048CAD0` are
  unchecked): from level 4 the task-walk wrapper
  `0x004651A0` casts it, 150% for 60 ticks (ADR-0385). Imps of levels 1-3 were never
  boosted (0 of 1,108 samples); from level 4 they spent 25-49% of their walking ticks at
  0.692. Our replay casts it 326-364 times by turn 6800, DK2 362. This section used to
  read the Speed row into walking speed, which made a level-3 Imp's neighbouring claim
  12 ticks instead of 13.

`TileWorkSystem` pays work experience per hit (`CreatureExperienceSystem.AwardWork`, ADR-0386).
`CreatureExperienceSystem` pays combat experience on the original's tick, registered **last**;
nothing earns there while held, imprisoned, down (ADR-0051) or leaving.

- **A worker earns `ImpExperienceGainPerSecond` once per work hit** whose tile
  test passes — digging, claiming, reinforcing, mining, converting, attacking or
  repairing a room — not per tick of work (Q79, ADR-0386, corrected 2026-10-06 from
  the Level 3 capture). An **idle imp earns nothing**.
- **150 work hits buy level 2** (3000 / 20).
- **A non-worker earns by TRAINING** (ADR-0083): its record's
  `ExpPerSecondTraining` through `SimulationTiming.LoaderRatePerTick`, scaled by
  stat 16, per original tick, capped at `TrainingRoomMaxExperienceLevel` and
  billed to the keeper at five gold a game second. **Fighting does not exist**,
  so nothing reaches level 5 and a test asserts the ceiling rather than leaving
  it implicit.
- Levelling **stops at 10**, because the stat table stops there. The owner's two
  room caps are lower and belong to the *rooms*, so they arrive with the rooms.

`ExperiencePoints` is the first hashed field whose value actually *moves* in the
pinned determinism run.

## 4. What raises a level (Q124)

The owner, 2026-09-05:

> *"imps get experience by working, while they do their usual things around the
> dungeon, digging, reinforcing walls, mining gold, etc. they get exp and
> passively their level raises. creatures also gain levels through experience,
> mostly training and fighting. creatures can get up to level 4 using the
> training room, up to level 8 using the combat pit, and to get to level 10 they
> need to fight actual fights."*

**Two of those caps are shipped variables, to the number:**
`TrainingRoomMaxExperienceLevel` = **4** and `CombatPitMaxExperienceLevel` =
**8**. Both had been sitting unread with nothing to say what they capped. The
third clause — level 10 needs real fights — is taken as read, and the binary
agrees: `ExpPerSecond` is read only by the update hook of state **category 22**
(battle, fleeing, threat-response), so it is combat experience (`training.md`
§5).

### 4.1 The two rates, and their units

| Rate | Where | Unit as stored | Per game second |
|---|---|---|---|
| `ImpExperienceGainPerSecond` (20) | balance table, global `0x006F295E` | **raw**, added once per work hit | as many hits as the work makes |
| `ExpPerSecondTraining` (record) | creature record | divided by variable 19 at load, floored at 1 | the tabled figure, less the truncation |
| `ExpPerSecond` (record) | creature record | divided by variable 19 at load | combat only (states 199/200, `0x0047A870`); a worker earns it only when good, so the hero Dwarf does (owner-confirmed in play, 2026-10-05) and the Imp never; built (ADR-0366) |

**The Imp's `ExpPerSecond` of 1200 against a balance-table 20 is a coincidence,
not a unit.** 20 × 60 = 1200 was written down here as evidence that the record's
rates are per minute. They are not: the loader divides four of these fields by
variable 19 as it stores them, which is positive evidence of a per-second source,
and the two numbers belong to different mechanics — one a global rate for
working, the other a record rate for fighting (Q205, `training.md`). **Before
treating two matching numbers as corroboration, check that the engine ever puts
them in the same place.**

`ImpExperienceGainPerSecond` is the one rate in the family the loader does *not*
divide, and it is not a per-second rate at all: 20 a work hit (Q79, ADR-0386, which
corrects ADR-0083 item 6's 80 a game second).

### 4.2 The adder's clauses (Q127)

Read from `FUN_00457880`; `training.md` §7 has it clause by clause. Two readings
this project had chosen were wrong:

| Chosen here | DK2 |
|---|---|
| the threshold is *spent*, remainder carried | **the balance is ZEROED** — the overshoot is discarded |
| a promotion **loops**, so a big award buys two levels | **one level per call**, however large the award |

The argument for the second was that *"a single-step version would silently drop
levels the day combat pays out in lumps."* It does drop them. That is the game.

Two clauses that were never guessed at:

- **`ExpForNextLevel` of zero refuses everything**, losses included. The field is
  not merely the price of a level, it is the switch that makes a creature capable
  of having experience.
- **A loss larger than the balance is a no-op**, not a clamp to zero.

**The discard makes the cadence load-bearing**, which is why both awards are paid
on the original's tick rather than once a game second: four awards of twenty and
one of eighty leave different numbers behind on any second that crosses a
threshold.

## 5. `ExperienceForNextLevel` is indexed by the CURRENT level — CONFIRMED

Under that reading level 1 gets the implicit 100% and the first level costs
exactly the record's 3000, which is the natural meaning of a field called
*experience for next level*. Indexed by the **target** level instead, the first
level would cost 3900 and the record's own number would appear nowhere. DK2's
adder settles it: it indexes the column with the creature's current level byte
and uses the record's raw value when that level is 1. The one of the three
Q124/Q127 readings this project got right, against two it got wrong.

## 6. What this unblocks

- **Vampire reincarnation** (Q111, Q113). The cost of coming back is a level, and
  creatures can now hold one above 1. Still needs a Graveyard that does something
  with corpses (Q165).
- **Combat.** Melee damage scales to 600%, so a combat system written against the
  record value alone would be wrong for every creature above level 1.
- **Q118's water arithmetic.** The level slope this note once predicted assumed
  level-scaled walking, which ADR-0518 removed; water halves the current speed at
  every level (`steering-behaviours.md` §7.5).

## Tests

`CreatureLevelTests` and `TrainingTests` (`tests/DK2.Simulation.Tests/`);
`ThePerLevelStatTableIsInTheShippedFileAndTheFixtureCopiesItExactly`,
`TheLevelledCurveAppliesToTheShippedCreatureRecords` and
`TheOwnersMeasurementsReproduceOnTheShippedRecord`
(`tests/DK2.Behavior.Tests/CreatureNeedsOnRealDataTests.cs`);
`DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines`.

## Known exceptions

- The level-4 dip in stats 10, 11 and 14 (§2.1), reproduced.
- The loader's truncation and its floor of 1 (`training.md` §4.1, §4.2),
  reproduced through `SimulationTiming.LoaderRatePerTick`.
- The zeroed remainder and the single promotion per call (§4.2), reproduced.

## Open questions

- The Imp's `ExpPerSecond` of 1200 — forty times its training rate where 41 of
  the 46 records are at five times — is **unexplained** (`training.md` §5.2).
- Whether stats 10, 11 and 14's level-4 dip is a data-entry error (§2.1).
