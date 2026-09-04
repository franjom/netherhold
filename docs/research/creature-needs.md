# Creature needs: health and hunger

What a creature *has*, as opposed to where it is standing. Health is
implemented; hunger's clock is implemented and eating is not, for a reason given
below.

Evidence classes used here, per ADR-0046's rule 4: **shipped data** (all the
numbers), **OpenKeeper** (the shape, under ADR-0003's second amendment), **the
binary** (attempted, and mostly refused — §6), and **owner observation**
(pending — §7 is the list of questions put to the owner).

---

## 1. Why health, and why first

Every unbuilt mechanic reads it. The balance table has **21 variables** whose
names contain "health", and they are the lair, the prison, torture, praying,
fire, possession, the dungeon heart and the boulder. `CreatureSleepsWhenBelowPercentHealth`
makes *sleep itself* a function of health. None of that could be written against
a creature that had none, and each becomes roughly one line once it does.

It is also the substrate combat needs, and combat is M7.

## 2. The numbers, all from the creature record

| Field | Meaning | Examples |
|---|---|---|
| `Hp` | maximum **and** starting health | Imp 500, Goblin 1500, Bile Demon 6000, Jack 240 |
| `HpFromChicken` | health restored per chicken eaten | Goblin 375, Bile Demon 200, **Firefly 0**, **Jack 250** |
| `HungerRate` | seconds between meals, fp32 | Goblin 180, Warlock 200, Bile Demon 220 |
| `HungerFill` | chickens needed to be full | Goblin 1, Warlock 2, Bile Demon 5 |
| `TimeAwake` / `TimeSleep` | the sleep cycle, seconds | Goblin 240/9, Troll 330/10 |
| `OwnLandHealthIncreasePerSecond` | regeneration on your keeper's land | **Imp 67, King Reginald 400, everyone else 0** |
| `DecomposeValue` | corpse worth, for the Vampire threshold | 200 for most, 0 for the undead — but **it is not what decides whether a corpse exists**: `GenerateDeadBody` is, and the two disagree for Horny and Jack, which carry 200 while leaving no body at all |

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

## 3. The findings that were not in any list

### 32767 means "never", per need, and it is not one flag — **CONFIRMED (data)**

This had been an open question since creatures were parsed: *is `hungerRate == 32767`
a general sentinel or an Imp quirk?* It is general, and it is finer-grained than
a flag would be.

- **Eight never get hungry:** Imp, Vampire, Skeleton, Horny, Stone Knight, and
  the unique Firefly, Skeleton and Vampire.
- **Six never sleep:** Imp, Skeleton, **Dwarf**, Horny, Stone Knight, Unique
  Skeleton.

**Neither set contains the other.** The Vampire sleeps and never eats; **the
Dwarf eats and never sleeps**. No single "undead" flag can produce that, so the
two needs really are switched off separately in the record.

> Both sets were guessed wrong before the install was asked. The first guess had
> four never-hungry creatures and missed the Stone Knight and all three uniques;
> the second had five sleepless and missed the Dwarf. Both are now asserted by
> name in `CreatureNeedsOnRealDataTests`, which is the only reason the numbers
> above can be trusted.

### Own-land regeneration is real and almost unused — **CONFIRMED (data)**

`OwnLandHealthIncreasePerSecond` is non-zero for **exactly two of 46 creatures**:
the Imp at 67 a second and King Reginald at 400. Everyone else is zero.

This is the same shape as mana, where three terrain types out of 41 generate any
(ADR-0017), and it is the reason to read a whole column before believing a rule
is general. Implemented as data — so it is a no-op for 44 creatures without
needing a special case — and tested with a creature that has it *and* one that
does not, because a suite that only tested the Imp would read as though the rule
were universal.

> It also caught a fixture bug on the way in: `Dk2TerrainFixture` had the Imp's
> value transcribed as **0**. The drift guard has now earned its keep twice on
> this one record — the Imp's hunger rate was once wrong by a factor of 4096.

### A chicken is not a healing item — **CONFIRMED (data)**

- The **Firefly** eats two chickens and gains **0** health from either. So
  `HpFromChicken` cannot be read as "the reason a creature eats"; hunger is its
  own need, and health is a side effect that sometimes happens to be zero.
- **Jack** has 240 health and a chicken worth **250** — more than its entire
  maximum, and it eats two. This makes the clamp in `Apply` **load-bearing for
  correctness rather than tidiness**: without it Jack finishes a meal above its
  own maximum and anything later keyed off "at full health" never fires for it.

That test was written the other way round — asserting no creature could be
over-healed — and the install refuted it.

### Hunger costs no health, on purpose — **STRONGLY INFERRED (data, by absence)**

Searched: 21 health variables and nine anger variables. **There is no
starvation.** What going unfed costs is *anger*, and that is per creature in the
record — `angerNoFood` is 100 for a Goblin, 110 for Jack, 1 for the Imp.

So a hungry creature here takes no damage, and that is a deliberate negative
rather than an omission. It is tested (`BeingHungryCostsNoHealthAtAll`) so that
nobody later "fixes" it into starvation.

### `TerrainDef.Damage` is a signed health delta, and only the stopped pay it — **CONFIRMED (owner)**

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
heals everywhere in this file.

**Crossing a hazard you can cross is free.** The owner, 2026-09-04: *"no creature
that can traverse through water looses health"*. The first implementation charged
whatever stood in water, which would have drowned in five seconds the imp the
owner had just watched wade a lake to mine a gem. The flag that decides
passability decides the cost, one hazard at a time — so `WorldPathGrid` and this
rule read the same bit for the same reason.

**Then who pays?** A creature that cannot cross is never routed into water, but
it can be *put* there — dropped from the keeper's hand, or in the original sent
by a patrol route drawn across it. And the roster says water charges almost
nobody: `CanWalkOnWater` is on **44 of the 46** creatures. The two without it are
the **Vampire** and the **Unique Vampire**.

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
to some, others are merely hindered by it."* The two clauses split the roster
2 / 44.

**Lava is where the rule earns its keep.** Seven creatures carry
`CanWalkOnLava` — Salamander, Firefly, Giant, Fairy, Horny and the unique Firefly
and Salamander — so the other 39 burn. The Imp is the clean pair: free in water,
dead in lava in five seconds.

**Two things are still ours.** The **cadence** is per game second like every
other rate here, and nothing states it — and the owner's answer removed the cheap
way to check, because nothing they can walk into water is charged at all; it
needs a Vampire dropped in a lake (Q118). The **slowdown** is CONFIRMED to exist
(*"water visibly slows imps and other creatures passing through it"*) and is not
implemented, because no variable for it exists in any of the 214. The standing
hypothesis is the unread third speed in every creature record, `ShuffleSpeed`:
Imp 4096 against its 7536 walk, Vampire 1638 against 2867 — not a constant ratio,
so a per-creature speed rather than a global multiplier, which is the shape a
hindrance needs.

This is also why the fixture has a **Vampire**: it is the only creature a water
test can honestly use. Adding it caught the Goblin missing `CanWalkOnWater` — the
fourth flag the drift guard has found absent from that one record.

## 4. What is implemented

`CreatureHealthRules` + `CreatureHealthSystem`, registered **last** in the tick
so a creature that arrives on tick N is not aged on the tick it arrives. Once a
game second, on the same phase as `ManaSystem`, because every number above is
written per second and dividing them by twenty would invent precision — and
would lean on `TicksPerSecond`, still a placeholder (Q1).

- Health seeded from `Hp` at spawn, clamped to `[0, Hp]`. **`WorldState.SpawnCreature`
  now takes the `GameDatabase`** for that one line: a creature spawned at zero
  health dies on the tick it arrives, so there must be no overload that leaves
  health out. That cost 25 call-site changes and is worth it.
- Own-land regeneration, per the record.
- **Hazard and healing terrain** (`TerrainHealthPerSecond`), gated on the
  creature's own crossing flag — see §3. Applied to a collapsed creature as well
  as a standing one, which is currently a no-op because the clamp at zero cannot
  take it lower.
- **Beds and sleep** (`LairRules` + `CreatureSleepSystem`): one bed per lair
  tile, claimed on arrival, walked to when the creature wants to sleep, and
  **healing at 100 a second only while asleep in its own bed**. CONFIRMED by the
  owner — see §7. This is also the **first driver any non-worker has ever had**;
  it reuses the imps' pathfinder and movement system rather than growing a
  second way to walk.
- Hunger's clock, and the "needs N chickens" flag.
- `Eat` — counts one chicken down, applies `HpFromChicken`, and resets the clock
  **only when the last one goes down**, because DK2 resets on being *full*, not
  on each chicken. Nothing calls it yet.
- **Collapse and death** (`CreatureDeathRules`): zero health is a 60-second
  window on the floor, not death — CONFIRMED by the owner (Q99, ADR-0051). A
  collapsed creature keeps its owner, tile, gold and bed and does nothing at
  all; only when `CreatureDyingStateDurationSeconds` elapses does it die. The
  corpse that should follow is an OBJECT and is missing (M6). See
  `docs/research/creature-death.md`.
- **Sleep-from-wounds (50%) is read** — it is half of `LairRules.WantsToSleep`,
  so a hurt creature goes to bed without being tired. Critical health (20%) is
  computed and still unread; nothing flees yet.

## 5. What is deliberately absent

| | Why |
|---|---|
| **Eating** | The Chicken is object 9 and the world has a list to put one in, but no **Hatchery** lays one — and the binary says a hatchery lays an **egg** (object 47), so there is a transition to chicken nobody had accounted for. Hunger arrives and never leaves. Honest, and it costs no health. See `hatchery.md` |
| **Anywhere to sleep but a bed** | A creature with no bed stays awake indefinitely and never recovers. In DK2 that is what `angerNoLair` is for — 100 for a Goblin, joint-largest in its record — and anger does not exist yet (Q105) |
| **Anger** | The actual consequence of hunger. Its own need, its own record fields, its own step |
| **Levels** | `Hp` is the maximum because `CreatureStats` scales stats by level and levels do not exist. When they do, the maximum becomes a function of level and `Health` stays the current value |
| **Bodies, graveyards, the Vampire** | Dying itself is now implemented (Q99 ANSWERED, ADR-0051): a creature collapses for 60 seconds before it dies. What is still missing is what comes *after* — a corpse is an OBJECT (M6), so nothing lies on the floor to be carried and nothing accumulates toward a graveyard |

## 6. What the binary was asked, and refused to say

Under ADR-0046. `HungerState.cpp` and `LairState.cpp` both appear in the
executable's own `__FILE__` strings — at `0x006985f8` and `0x00698cc0` — so the
code exists and is named. **Getting at it failed**, and the method is recorded so
the next attempt starts further along:

1. `DumpNearString.py` found the strings but **zero referencing functions**: the
   assert loads the address indirectly, so Ghidra's reference analysis never
   links it.
2. `ScanForAddr.py` scanned initialised memory for the raw little-endian address
   and found the load sites — `0x00463c6d`, `0x00463d25` for hunger and
   `0x0046f5d7` for the lair — but **Ghidra has no function covering any of
   them**. The creature state machine is reached purely through vtables, so
   auto-analysis never discovers those bodies.
3. `ForceFunc.py` walked back to the nearest MSVC prologue and forced a function
   there. The created function **did not extend to the target**.
4. Re-running full analysis with **Aggressive Instruction Finder** enabled
   changed nothing.

**Next thing to try:** the vtables themselves. DK2's creature states are a class
hierarchy with ~25 leaves (`executable-strings.md`), so there is a table of
function pointers somewhere, and reading it would name every state handler at
once instead of one at a time. That is a bigger, more valuable job than hunger,
and it is the route into the whole `*State.cpp` family.

So **nothing in this document rests on the binary.** The one thing it did
confirm, indirectly, is that `Hatchery.cpp` exists as its own translation unit
with two functions reachable from its assert — kept in `reverse-engineering/`
for when chickens are built.

## 7. For the owner — questions playing the game would settle

Ranked by how much they would change.

### Q101 — the lair — **ANSWERED, and it replaced the implementation**

**CONFIRMED by the owner**, 2026-09-04:

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
   rim, `2(w+h) - 4` (Q60). Capacity is per room type, and the lair's happens to
   be the obvious one.

Built as `LairRules` + `CreatureSleepSystem` (ADR-0049), which is also **the
first driver any non-worker creature has ever had** — before it, a goblin out of
a portal stood on the hole forever.

The bed object is `LairObjectId` in the creature record: 22 for a Goblin, 26 for
a Vampire (a coffin), 18 for a Bile Demon, and **0 for ten creatures** — the Imp,
both Skeletons, Horny, Jack and the five hero lords. The owner confirmed the two
interesting ones: *"skeleton does not have a lair"*, and *"horny is only a
summonable creature, it is not in a regular roster of creatures, and only on
special conditions/maps when conditions are met"* — which is also why Horny
carries no `AvailableViaPortal`.

### Q99 — is zero health death? **ANSWERED: no**

**CONFIRMED by the owner** (2026-09-04). A creature at zero health collapses and
lies there rescuable for `CreatureDyingStateDurationSeconds` (60); only then does
it die, leaving a corpse that lies carryable for `DeadBodyDiesAfterSeconds`
(also 60). Two timers, three states, and both numbers were already parsed with
nothing reading either.

The collapse and the death are implemented; the corpse is not, because it is an
OBJECT (M6). See **`docs/research/creature-death.md`** and ADR-0051 — including
the defect this answer exposed, in which a creature at zero health could walk to
bed and heal, and an Imp could regenerate off the floor and become unkillable on
its own land.

Also note what this changed about the guard here: **own-land regeneration is now
suppressed at zero health, and lair healing is not**, because the owner's answer
gives exactly one route off the floor and a bed is it.

### Q100 — does regeneration care what the creature is doing?

Implemented: own-land regeneration and lair healing **add**, and both apply
regardless of activity.

*How to see it:* an imp is the easy subject, since it is one of the two
creatures that regenerate. Wound one, then watch whether it heals *while
digging* on your own land, or only when idle. If a wounded imp heals mid-job,
the reading is right.

### Q102 — how fast does hunger actually arrive?

The record says 180 seconds for a Goblin, and the clock starts at spawn.
UNKNOWN whether DK2 starts it at spawn, at the first meal, or at level start.

*How to see it:* start a level, note the clock when your first goblin arrives,
and note when it first heads for the hatchery. Three minutes would confirm it.

### Q103 — is the sleep cycle really 240 seconds up and 9 down?

The record says `TimeAwake` 240 and `TimeSleep` 9 for a Goblin, and both are
read as seconds. Four minutes awake and **nine seconds asleep** looks wrong
until you multiply: nine seconds at 100 health a second is **900**, well over
half a Goblin's 1500. So a short sleep is a big heal and the ratio stops looking
odd — but it is still a reading.

*How to see it:* watch a healthy goblin in a dungeon with a lair. Does it go to
bed roughly every four minutes, and is it up again in under ten seconds?

### Q104 — a bed it can never sleep in

The **Dwarf** and the **Stone Knight** both carry a bed object *and* a
`TimeAwake` of 32767. By the record they occupy a lair tile and never sleep in
it. `LairRules.NeedsBed` gates on the object id, so here they claim a bed and
never use it — which also means they take a bed away from something that would.

Nothing depends on which field DK2 gates on, because a creature that never
sleeps never heals in a bed either way. It is checked
(`TwoCreaturesOwnABedTheyCanNeverSleepIn`) rather than assumed away.

*How to see it:* Dwarves are hero creatures, so this needs a level where you can
capture and convert one. Does a converted dwarf take a lair tile?

### Q105 — what happens to a creature with no bed?

Implemented: it stays awake, keeps its awake counter climbing, and never
recovers. `angerNoLair` is **100** for a Goblin — joint-largest of its four
anger sources — so DK2's answer is clearly *anger*, and anger does not exist
here yet.

*How to see it:* two questions really. Does a creature with no free lair tile
sleep on the floor, or not sleep at all? And is "no lair" the anger source that
makes creatures leave a dungeon whose lair is too small?
