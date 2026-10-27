# Mechanic: creature needs — health and hunger

Source(s):
- The reference install's `Creatures.kwd`, `GlobalVariables.kwd` and
  `Terrain.kwd`, via `DK2.Data` (every number below)
- Owner observation, 2026-09-04 and 2026-09-05 (Q99–Q105, Q118)
- `DKII.EXE` v1.70 for `HungerState.cpp` and `LairState.cpp`, whose readings
  live in `eating.md` and `lair-acquisition.md`
- OpenKeeper, for the shape only, under ADR-0003's second amendment

Original version: DK2 v1.70

What a creature *has*, as opposed to where it is standing. Health, hunger, sleep,
collapse and death are all implemented; this note is the data behind them and
the owner answers that pinned them.

Confidence: **CONFIRMED (data)** for every table here; **CONFIRMED (owner)** for
the behaviours in §7; **STRONGLY INFERRED** for the two items §3 marks.

## 2. The numbers, all from the creature record

| Field | Meaning | Examples |
|---|---|---|
| `Hp` | maximum at level 1 **and** starting health | Imp 500, Goblin 1500, Bile Demon 6000, Jack 240 |
| `HpFromChicken` | health restored per chicken eaten | Goblin 375, Bile Demon 200, **Firefly 0**, **Jack 250** |
| `HungerRate` | seconds between meals, fp32 | Goblin 180, Warlock 200, Bile Demon 220 |
| `HungerFill` | chickens needed to be full | Goblin 1, Warlock 2, Bile Demon 5 |
| `TimeAwake` / `TimeSleep` | the sleep cycle, seconds | Goblin 240/9, Troll 330/10 |
| `OwnLandHealthIncreasePerSecond` | regeneration on your keeper's land | **Imp 67, King Reginald 400, everyone else 0** |
| `DecomposeValue` | corpse worth, for the Vampire threshold | 200 for most, 0 for the undead — but **it is not what decides whether a corpse exists**: `GenerateDeadBody` is, and the two disagree for Horny and Jack, which carry 200 while leaving no body at all |

The maximum is `Hp` **scaled by the level's Health row** (`creature-levels.md`
§2), not `Hp` flat.

And from the balance table:

| Variable | Value |
|---|---|
| `ModifyHealthOfCreatureInLairPerSecond` | **100** |
| `CreatureSleepsWhenBelowPercentHealth` | 50 |
| `CreatureCriticalHealthPercentageOfMax` | 20 |
| `CreatureDyingStateDurationSeconds` | 60 |
| `DeadBodyDiesAfterSeconds` | 60 |
| `PrisonModifyCreatureHealthPerSecond` | −3 |
| `HealthDecreaseWhenOnFirePerSecond` | −150 |
| `ModifyCreatureHealthWhilePrayingPerSecond` | 33 |
| `ChickenGenerationTimePerHatchery` | 16 |
| `MaxFreeRangeChickensPerPlayer` | 12 |
| `TimeBetweenReattemptingHungerSeconds` | 4 |

**Health is the substrate.** The balance table has **21 variables** whose names
contain "health" — the lair, the prison, torture, praying, fire, possession, the
dungeon heart and the boulder — and `CreatureSleepsWhenBelowPercentHealth` makes
*sleep itself* a function of health. It is also what combat will subtract from
(M7).

## 3. The findings that were not in any list

### 32767 means "never", per need, and it is not one flag — CONFIRMED (data)

The sentinel is general, and finer-grained than a flag would be.

- **Eight never get hungry:** Imp, Vampire, Skeleton, Horny, Stone Knight, and
  the unique Firefly, Skeleton and Vampire.
- **Six never sleep:** Imp, Skeleton, **Dwarf**, Horny, Stone Knight, Unique
  Skeleton.

**Neither set contains the other.** The Vampire sleeps and never eats; **the
Dwarf eats and never sleeps**. No single "undead" flag can produce that, so the
two needs really are switched off separately in the record. Both sets were
guessed wrong before the install was asked — the first guess had four
never-hungry creatures and missed the Stone Knight and all three uniques, the
second had five sleepless and missed the Dwarf — and both are now asserted by
name, which is the only reason the numbers above can be trusted.

### Own-land regeneration is real and almost unused — CONFIRMED (data)

`OwnLandHealthIncreasePerSecond` is non-zero for **exactly two of 46 creatures**:
the Imp at 67 a second and King Reginald at 400. Everyone else is zero.

Same shape as mana, where three terrain types out of 41 generate any (ADR-0017),
and the reason to read a whole column before believing a rule is general.
Implemented as data — a no-op for 44 creatures without a special case — and
tested with a creature that has it *and* one that does not, because a suite that
only tested the Imp would read as though the rule were universal. It also caught
a fixture bug on the way in: `Dk2TerrainFixture` had the Imp's value transcribed
as **0**.

### A chicken is not a healing item — CONFIRMED (data)

- The **Firefly** eats two chickens and gains **0** health from either, so
  `HpFromChicken` cannot be read as "the reason a creature eats"; hunger is its
  own need and health is a side effect that is sometimes zero.
- **Jack** has 240 health and a chicken worth **250** — more than its entire
  maximum, and it eats two. That makes the clamp in `CreatureHealthRules.Apply`
  **load-bearing for correctness rather than tidiness**: without it Jack finishes
  a meal above its own maximum and anything later keyed off "at full health"
  never fires for it. The test was written the other way round, asserting no
  creature could be over-healed, and the install refuted it.

### Hunger costs no health, on purpose — STRONGLY INFERRED (data, by absence)

Searched: 21 health variables and nine anger variables. **There is no
starvation.** What going unfed costs is *anger*, per creature in the record —
`angerNoFood` is 100 for a Goblin, 110 for Jack, 1 for the Imp
(`creature-anger.md`). So a hungry creature takes no damage, and that is a
deliberate negative rather than an omission. Tested
(`BeingHungryCostsNoHealthAtAll`) so that nobody later "fixes" it into
starvation.

### `TerrainDef.Damage` is a signed health delta, and only the stopped pay it — CONFIRMED (owner)

The field is named for damage and is not damage. Three of the 41 terrains carry
a non-zero one:

| Terrain | `Damage` | What it means |
|---|---|---|
| Water | +100 | −100 health a second, to a creature that cannot cross water |
| Lava | +100 | −100 health a second, to a creature that cannot cross lava |
| **Hero Lair** | **−67** | **+67 health a second**, to anything standing on it |

Hero Lair is the entry that settles the sign: it is the hero side's answer to a
keeper's lair, and it heals. So the column is a per-second health delta, and
`CreatureHealthRules.TerrainHealthPerSecond` returns it negated so that positive
heals everywhere in that file.

**Crossing a hazard you can cross is free.** The owner, 2026-09-04: *"no creature
that can traverse through water looses health"*. The flag that decides
passability decides the cost, one hazard at a time — so `WorldPathGrid` and this
rule read the same bit for the same reason.

**Then who pays?** A creature that cannot cross is never routed into water, but
it can be *put* there — dropped from the keeper's hand, or in the original sent
by a patrol route drawn across it. `CanWalkOnWater` is on **44 of the 46**
creatures; the two without it are the **Vampire** and the **Unique Vampire**.

Three sources that never met agree on those two records:

- **The owner, from play, before the column was read:** *"I think vampires can't
  cross through water, they need bridges"* — a two-element set out of 46, named
  from memory, in a record whose flag layout this project reconstructed rather
  than read anywhere.
- **The shipped `Creatures.kwd`:** the flag is missing on exactly those two.
- **The DK2 wiki:** its warning that water may leave a creature *"damaged,
  resurrect, and even die"* is on the **Vampire page** (facts in our own words,
  per `wiki-as-a-source.md` — the page's prose is CC BY-SA and is not vendored).

So water is not a barrier with exceptions but a **hindrance with two
exceptions**, which is what the game says in its own tooltip: *"Water. Impassable
to some, others are merely hindered by it."*

**Lava is where the rule earns its keep.** Seven creatures carry `CanWalkOnLava`
— Salamander, Firefly, Giant, Fairy, Horny and the unique Firefly and Salamander
— so the other 39 burn. The Imp is the clean pair: free in water, dead in lava
in five seconds.

**Two things are still ours.** The **cadence** is per game second like every
other rate here and nothing states it — and the owner's answer removed the cheap
way to check, because nothing they can walk into water is charged at all; it
needs a Vampire dropped in a lake (Q118). The **slowdown** is CONFIRMED to exist
(*"water visibly slows imps and other creatures passing through it"*) and is not
implemented, because no variable for it exists in any of the 214. The standing
HYPOTHESIS is the unread third speed in every creature record, `ShuffleSpeed`:
Imp 4096 against its 7536 walk, Vampire 1638 against 2867 — not a constant ratio,
so a per-creature speed rather than a global multiplier, which is the shape a
hindrance needs. `ShuffleSpeed` has no row in the level table, so the slowdown
would deepen with level (`creature-levels.md` §6).

This is also why the fixture has a **Vampire**: it is the only creature a water
test can honestly use. Adding it caught the Goblin missing `CanWalkOnWater`.

## 4. Implementation

`CreatureHealthRules` + `CreatureHealthSystem`, once a game second, registered
after every creature's update (`CreatureUpdateSystem`, ADR-0554), so a creature that claimed a
bed this tick is not charged for the second it arrived, and before experience.

- Health seeded from `Hp` at spawn and clamped to the level-scaled maximum.
  **`WorldState.SpawnCreature` takes the `GameDatabase`** for that one line: a
  creature spawned at zero health dies on the tick it arrives, so there must be
  no overload that leaves health out.
- Own-land regeneration, per the record, **suppressed at zero health** (§7, Q99).
- **Hazard and healing terrain** (`TerrainHealthPerSecond`), gated on the
  creature's own crossing flag — §3.
- **Beds and sleep** (`LairRules` + `CreatureSleepSystem`, ADR-0049 then
  ADR-0060): one bed per lair tile, the room chosen by distance and alignment,
  the tile found on arrival, and **healing at 100 a second only while asleep in
  its own bed** (`AsleepInOwnBed`). See `lair-acquisition.md`. This was the first
  driver any non-worker ever had; it reuses the imps' pathfinder and movement
  system rather than growing a second way to walk.
- **Hunger and eating** (`EatingRules` + `CreatureEatSystem`, ADR-0067): the
  accumulator, the once-a-second retry, the walk to a hatchery, the nearest
  chicken and the two subtract amounts. See `eating.md`.
- **Sleep-from-wounds (50%)** sends only a creature with no bed to sleep untired; with a
  bed it waits until tired (ADR-0543). `CreatureCriticalHealthPercentageOfMax`
  (20%) is computed and still unread; nothing flees yet.
- **Collapse, death and the corpse** (`CreatureDeathRules`, ADR-0051 then
  ADR-0063): zero health is a 60-second window on the floor, then death, then a
  corpse for 60 more, then gone — and an imp can carry the creature to its bed,
  to a prison, or to a Graveyard. See `creature-death.md`.

## 5. What is deliberately absent

| | Why |
|---|---|
| **Starvation** | There is none in DK2 (§3), and a test pins the absence. |
| **Sleeping anywhere but a bed** | A creature with no bed stays awake indefinitely and never recovers, which is CONFIRMED (owner, Q105): it cannot sleep at all. What it does instead is accrue `angerNoLair`. |
| **Hated company** | Built under ADR-0479 (the mixed-alignment room rule); `creature-anger.md` has the bucket. |
| **Vampire reincarnation** | Graveyard creation is built (Q165); reincarnation at a sarcophagus remains Q111. |
| **`ShuffleSpeed` and the water slowdown** | No variable governs it (§3, Q118). |

## 6. The method that got at the binary, kept because it is reusable

`HungerState.cpp` and `LairState.cpp` appear in the executable's own `__FILE__`
strings, at `0x006985f8` and `0x00698cc0`. Ghidra finds **zero referencing
functions** for either — the assert loads the address indirectly, and the
creature state machine is reached purely through vtables, so auto-analysis never
discovers those bodies. `ScanForAddr.py` scanning initialised memory for the raw
little-endian address finds the load sites (`0x00463c6d`, `0x00463d25` for
hunger; `0x0046f5d7` for the lair), and Ghidra covers none of them with a
function.

**The question that worked was not "which function contains this instruction"
but "what is compiled either side of it".** Decompiling the *neighbourhood* of
the scan hit gave the lair acquisition code, the tile search, the room choice and
the creature-to-bed link in about ten minutes, and the same move gave hunger.
`source-file-map.md` now indexes all 72 translation units by name.

## 7. The owner answers that pinned this

### Q101 — the lair — CONFIRMED, and it replaced the implementation

> *"A lair is a room with tiles, on each tile only one bed/chair/whatever per
> creature can be present. When a creature enters a dungeon through a portal it
> goes to a lair, finds a free tile and occupies it with its bed. When a
> creature is injured it heals while it sleeps in its own bed."*

The first implementation healed anything standing on any of its keeper's lair
tiles. **Both halves of that were wrong**, and the answer settles a third thing
nobody asked:

1. **Asleep, not present.** A wounded creature standing awake in its own lair
   heals nothing.
2. **Its own bed, not its keeper's lair.** One bed per tile, one tile per
   creature. A creature with no bed never heals, whatever it stands on.
3. **A lair's capacity is its tile count** — one bed per tile. Worth stating
   because it is *not* the general rule for rooms: a temple's capacity is its
   rim, `2(w+h) - 4` (Q60). Capacity is per room type.

The bed object is `LairObjectId` in the creature record: 22 for a Goblin, 26 for
a Vampire (a coffin), 18 for a Bile Demon, and **0 for ten creatures** — the Imp,
both Skeletons, Horny, Jack and the five hero lords. The owner confirmed two:
*"skeleton does not have a lair"*, and *"horny is only a summonable creature, it
is not in a regular roster of creatures, and only on special conditions/maps when
conditions are met"* — which is also why Horny carries no `AvailableViaPortal`.

### Q99 — is zero health death? No

A creature at zero health collapses and lies there rescuable for
`CreatureDyingStateDurationSeconds` (60); only then does it die, leaving a corpse
that lies carryable for `DeadBodyDiesAfterSeconds` (also 60). Both numbers had
been parsed with nothing reading either. See `creature-death.md`.

The guard this changed: **own-land regeneration is suppressed at zero health and
lair healing is not**, because the owner's answer gives exactly one route off the
floor and a bed is it. Without that, a creature at zero health near its bed
walked there and healed and never collapsed, and an Imp on its own land could not
be killed at home.

### Q100 — does regeneration care what the creature is doing? No

The owner recorded a wounded imp regenerating **while digging** and **while
running through the dungeon**, so regeneration is not restricted to resting,
idling, sleeping or standing still — which is what this project already did.
**Working** is answered; *fighting* and *being carried* are not, and own-land
regeneration is 67 a second on an imp, meaningful inside a fight and irrelevant
outside one. **Q152**, along with the Hand of Evil and possession.

### Q103 — the sleep cycle is 240 seconds up and 9 down

Both fields are read as seconds. Four minutes awake and **nine seconds asleep**
looks wrong until you multiply: nine seconds at 100 health a second is **900**,
well over half a Goblin's 1500. So a short sleep is a big heal. **Still a
reading**, and `LairRules.WantsToSleep` says so at the call site.

### Q104 — a bed it can never sleep in

The **Dwarf** and the **Stone Knight** both carry the hero bed (6) *and* a
`TimeSleep` of 0 with `TimeAwake` 32767. DK2's lair attempt gates on **both**
fields, so it never sends either for a bed —
`LairRules.NeedsBed` does the same (`lair-acquisition.md` §5). Pinned by
`TheDwarfAndTheStoneKnightCarryABedIdAndAreNeverSentForOne`.

### Q102 — how fast does hunger actually arrive? UNKNOWN

The record says 180 seconds for a Goblin, and ours starts the clock at spawn.
Whether DK2 starts it at spawn, at the first meal, or at level start is not
settled. *How to see it:* start a level, note the clock when your first goblin
arrives, and note when it first heads for the hatchery.

## Tests

`CreatureNeedsTests` (`tests/DK2.Simulation.Tests/`) and
`CreatureNeedsOnRealDataTests` (`tests/DK2.Behavior.Tests/`), which asserts the
three sets of §3 and the two records of §3 by name, plus
`Dk2TerrainFixture`'s drift guard against the shipped install.

## Known exceptions

- `MaxFreeRangeChickensPerPlayer` and `TimeBetweenReattemptingSleepSeconds` are
  parsed and not applied, because the original does not read them the way their
  names imply (`hatchery.md` §5, `lair-acquisition.md` §6).
- Horny and Jack carry `DecomposeValue` 200 while leaving no body, so those 200s
  are unreachable (`creature-death.md`).

## Open questions

- **Q102** (above), **Q118** (the water slowdown and the hazard cadence),
  **Q152** (regeneration in combat, in the hand, under possession).
