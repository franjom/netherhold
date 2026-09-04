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
| `DecomposeValue` | corpse worth, for the Vampire threshold | 200 for most, 0 for the undead |

And from the balance table:

| Variable | Value |
|---|---|
| `ModifyHealthOfCreatureInLairPerSecond` | **100** |
| `CreatureSleepsWhenBelowPercentHealth` | 50 |
| `CreatureCriticalHealthPercentageOfMax` | 20 |
| `CreatureDyingStateDurationSeconds` | 60 |
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
- Lair healing at 100 a second — **the weakest reading here, see §7**.
- Hunger's clock, and the "needs N chickens" flag.
- `Eat` — counts one chicken down, applies `HpFromChicken`, and resets the clock
  **only when the last one goes down**, because DK2 resets on being *full*, not
  on each chicken. Nothing calls it yet.
- Death at zero health: removal, with an event naming what it will one day
  branch on.
- Two thresholds computed and not yet read: critical (20%) and
  sleep-from-wounds (50%).

## 5. What is deliberately absent

| | Why |
|---|---|
| **Eating** | Chickens are objects and the OBJECTS record is unparsed (M6). Hunger arrives and never leaves. Honest, and it costs no health |
| **Sleep** | Needs a lair a creature *owns* — a specific tile with a bed object on it. That is the next mechanic, not this one |
| **Anger** | The actual consequence of hunger. Its own need, its own record fields, its own step |
| **Levels** | `Hp` is the maximum because `CreatureStats` scales stats by level and levels do not exist. When they do, the maximum becomes a function of level and `Health` stays the current value |
| **Dying, bodies, graveyards** | `CreatureDyingStateDurationSeconds` is 60 and `GenerateDeadBody` is a flag we read, but a body is an object (M6) |

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

### Q101 — the lair, and this is the one that matters

Implemented reading: **a creature heals at 100 a second while standing on a lair
tile its keeper owns.** That is a proxy and it is knowingly wrong in two ways.

1. **Does a creature have to be *asleep* to heal**, or is standing in the lair
   enough?
2. **Does it have to be *its own* lair?** DK2 gives each creature a personal
   lair tile with its own bed. Does a wounded goblin heal in any lair its keeper
   owns, or only in the one that belongs to it — and does it heal at all if it
   has no lair of its own?

*How to see it:* wound a creature (drop it in a fight, or on a rival's land),
then drop it on a lair tile belonging to a *different* creature and watch
whether its health bar climbs while it stands there awake.

### Q99 — is zero health death?

Implemented: the creature is removed. But `CreatureDyingStateDurationSeconds` is
**60**, which strongly suggests a window. OpenKeeper models an *unconscious*
state that becomes death after that timer and can be interrupted by rescue.

*How to see it:* let one of your creatures lose a fight and watch what is left
on the floor. Does it vanish, or lie there? Can it be picked up, and does it
recover if dropped in a lair? Does it matter whether it was a fight or something
else that killed it?

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
