# Eating: hunger, the chicken, and the two ways of paying it off

What a hungry creature does about it, read from the original's own
`HungerState.cpp` on **2026-09-06**. Everything here is from the binary unless
it says otherwise; the owner has confirmed one sentence of it, and that sentence
is about prisoners.

Companion to `hatchery.md`, which is the room that makes the food, and to
`creature-needs.md`, which is where the creature record's numbers live.

---

## 1. The short version

```
every HungerRate seconds:       HungerNeed += HungerFill        (nothing caps it)
hungry when:                    HungerNeed >= HungerFill
retry at most:                  once a game second
walk to:                        the nearest reachable Hatchery of your OWN keeper
choose, on arrival:             the nearest chicken by straight-line squared distance
                                — nothing is reserved
eat, within one tile:           health += min(HpFromChicken, maximum - current)
                                HungerNeed -= 1                 (in a hatchery)
                                HungerNeed -= HungerFill        (in a PRISON)
                                the hunger clock restarts
keep eating while:              HungerNeed > 0
```

Two numbers do all the work and both are in the creature record: `HungerRate`
(180 seconds for a Goblin, 220 for a Bile Demon) and `HungerFill` (1 for nearly
everybody, **5** for a Bile Demon, 0 for the four creatures that never eat).

---

## 2. Where it lives in the binary

DK2 ships the file name: `D:\Dev\DK2\Projects\Source\Game\HungerState.cpp` at
`0x006985f8`, pushed by two asserts at `0x00463c6c` and `0x00463d24`. The code
around them is the whole mechanic.

**The creature state object's own debug dump** (`FUN_00479200`) names the
fields, which is how this stopped being guesswork:

| Offset | DK2's name | What it is |
|---|---|---|
| `+0x10` | `CurrentStateId` | index into the state table below |
| `+0x18` | `StartedAt` | tick this state was entered |
| `+0x1c` | `Age` | ticks in this state |
| `+0x48` | `LastAteTime` | the hunger clock's stamp |
| `+0x4c` | **`HungerNeed`** | the accumulator |
| `+0x50` | **`LastAttemptedHunger`** | the retry stamp |
| `+0x54` | `LastSleptTime` | |
| `+0x58` | `SleepNeeded` | |
| `+0x5c` | `LastAttemptedSleep` | |
| `+0x60` | `PayCheckedTime` | |
| `+0x64` | `CurrentlyDoingJobId` | |
| `+0xdf` | `UtilityNewState` | the state a "move to" hands control to |

The dump prints thirty more, including the whole `ImpJobData` block and the
lair/prison sub-blocks; the rest are in `creature-table-dump.md`'s neighbourhood
and are not this document's business.

### 2.1 The state table, and how it was recovered

DK2's creature states are a table of **239 records of 25 bytes** based at
`0x006DA8C0`, filled at run time by a long straight run of `MOV` instructions
between `0x0047bbe9` and `0x0047faeb` — no function contains it, which is why a
caller search finds nothing.

Record layout: `+0` the id (a dword, and it equals the record's index for all
239), `+4` a word, `+6` and `+7` two category bytes, `+8` a flag byte, then
**four function pointers** at `+9`, `+0xd`, `+0x11` and `+0x15`. Only the first
is used by anything read here; it is the state's update.

Recovering it is a two-step: list every write into `[0x006DA000, 0x006DC000)`,
then walk the installer's linear disassembly tracking register values, because
the pointers are stored from `EBP`/`EDI`/`ECX` rather than as immediates. All
239 resolve, none unknown.

`FUN_00476d30(newStateId, out)` is **SetState**. It checks the two category
bytes against a 0x40-byte-per-category table at `0x00699688`, calls that
category's leave and enter hooks when the category changes, then writes
`CurrentStateId`, stamps `StartedAt`, and zeroes `Age` and `SuspendTime`.
Category `2` means "utility state", which saves the previous state in
`StateBeforeUtilityState` and the state to resume in `UtilityNewState`.

### 2.2 The states this mechanic uses

| Id | Handler | What it does |
|---|---|---|
| `0x5A` | `FUN_004634b0` | find a hatchery, MoveToRoom, resume in `0x5C` |
| `0x5B` | `FUN_00463500` | **in the hatchery**: find the nearest chicken, walk to it, resume in `0x5D` |
| `0x5C` | `FUN_00463760` | **eat** the chosen chicken if within a tile |
| `0x5D` | `FUN_00463720` | arriving at the chicken; when `HungerNeed != 0` go to `0x5C` |
| `0x5E` | `FUN_00458680` | wait out the eating animation |
| `0x5F` | `FUN_00463be0` | wander to a random adjacent tile in the hatchery |
| `0x72` | `FUN_004731f0` | the prison's copy of the hunger test |
| `0x74` | `FUN_00473550` | **in the prison**: find the nearest *food-flagged* object |
| `0x75` | `FUN_00473750` | eat it |
| `0x76` | `FUN_00473730` | arriving |

`FUN_004633d0` is not a state; it is the attempt, called from the needs routine.

---

## 3. The clock — `FUN_00477c20`

One routine drives hunger, sleep and pay, in that order, and each block returns
early on success. So **hunger is asked before tiredness**, and a creature that
sets off to eat does not also consider its bed this tick.

```c
if (stateTable[CurrentStateId].byte6 == 7) return 0;     // a category that suppresses needs

fill = levelScale(def.HungerFill, statRow9);
if (fill != 0) {
    if (now >= LastAteTime + def.HungerRateTicks
        && (stateTable[CurrentStateId].byte8 & 8) == 0) {
        LastAteTime = now;
        HungerNeed += fill;                              // ADDS. Nothing caps it.
    }
    if (HungerNeed >= fill
        && category != 8
        && now >= LastAttemptedHunger + 4) {
        LastAttemptedHunger = now;
        if (FUN_004633d0()) return 1;                    // go and eat
    }
}
// ... then the sleep block, then pay
```

**Three things worth stating separately.**

**It adds rather than sets.** A creature with nowhere to eat accumulates, and
owes every helping it missed when a hatchery finally appears. This project had
it the other way round until 2026-09-06: `ChickensNeeded` was *set* to
`HungerFill`, which saturates. Observable, and one test now says so.

**The retry is four game ticks.** Written as a literal `4` here; the prison's
copy of the same test (`FUN_004731f0`) reads
`TimeBetweenReattemptingHungerSeconds`, which **is** 4. Four ticks is one game
second on every shipped level (Q1/Q132), so the two paths agree whichever way
the variable's name is read.

**The units corroborate Q132 for free.** The record loader (`FUN_00492320`)
writes

```c
def.HungerRateTicks = (file.hungerRate  * V19) >> 12;   // fp32 seconds -> ticks
def.TimeAwakeTicks  =  file.awake       * V19;          // plain seconds -> ticks
def.TimeSleepTicks  =  file.sleep       * V19;
```

so `V19` is the ticks-per-game-second multiplier, `hungerRate` is fixed-point
seconds and `awake`/`sleep` are plain integers — three independent facts this
project had already inferred, arriving together from a single load routine.

---

## 4. The three creature-record fields, pinned

The in-memory creature definition is **not** the file record's layout, so the
identification is not a matter of matching offsets by eye. It was pinned through
the loader, which copies from a staging struct at a fixed `+0x3F0` from the file
offsets this project already parses:

| Memory | Loader source | File offset | Field | Scaled by |
|---|---|---|---|---|
| `def+0x66d` (u8) | `+0x133e` | `0xF4E` | **`HungerFill`** | creature stat row **9** |
| `def+0x692` (u16) | `+0x12fc` | `0xF0C` | **`Hp`** | stat row **1** (Health) |
| `def+0x694` (u16) | `+0x12fe` | `0xF0E` | **`HpFromChicken`** | **not scaled** |
| `def+0x5ce` (u32) | `+0x12ac` | `0xEBC` | `HungerRate`, in ticks | — |
| `def+0x5d2` (u32) | `+0x12b0` | `0xEC0` | `TimeAwake`, in ticks | — |
| `def+0x5d6` (u32) | `+0x12b4` | `0xEC4` | `TimeSleep`, in ticks | — |

The three deltas — 2, 0x42, 4, 8 — match the file's exactly, which is what makes
this a pin and not a guess.

**Stat rows are 18 bytes** (nine shorts, levels 2 to 10) and the code indexes
`base + level*2`, which places row *n* at `0xE5 + (n-1)*18`. Health's row is
therefore `0xE1 + level*2` and row 9's is `0x171 + level*2` — 144 bytes and
exactly eight rows apart, which is the arithmetic that identifies row 9 as row 9.

**Row 9 is 100% at every level in the shipped table**, so scaling `HungerFill` is
invisible. Applied anyway, because the original applies it, and a test asserts
the shipped table is flat so that the day it stops being flat is a data change
rather than a silent one.

---

## 5. The health a chicken is worth — `FUN_00463760`

```c
gain = min(HpFromChicken, levelScale(Hp, healthRow) - currentHealth);
ApplyHealth(gain);
```

So `HpFromChicken` is a **cap on the top-up**, and the ceiling it is capped
against is the *levelled* maximum while `HpFromChicken` itself never levels. Our
`CreatureHealthRules.Apply` already clamps to the levelled maximum, so this was
right before it was read; it is written down now because "the clamp is
defensive" and "the clamp is the rule" are different claims.

Two records make the clamp load-bearing rather than theoretical: **Jack** has 240
health and a chicken worth **250**, and the **Firefly** eats two chickens for
**0** health each. A chicken is not a healing item; hunger is its own need.

---

## 6. Finding the bird, and the two searches that do not agree

### In a hatchery — `FUN_00463500` (state `0x5B`)

1. The tile underfoot must be a room of **type 4** owned by **this creature's own
   keeper**. The "allow allied" argument is passed as **false**, so an ally's
   hatchery is no use to you.
2. Walk the room's tile chain. On each tile take the first thing that is an
   object **whose object type id is 9** — the Chicken, by id.
3. Rank by **straight-line squared distance** in the engine's fixed point
   (`dx² + dy²`, each term `>> 12`). Not a path length. Not a ring search.
4. A candidate is skipped when its tile carries an object whose definition flags
   have `0x4000` — **UNKNOWN what that bit means**, and not modelled here.
5. No chicken: re-enter `0x5B` and try again next update.
6. A chicken: remember **its thing id on the creature** (`+0xaf`) and route to
   it, resuming in `0x5D`.

### In a prison — `FUN_00473550` (state `0x74`)

Same shape, same ranking, but the test is `objectDef.flags & 0x80` —
`ObjectFlags.TypeFood` — rather than the id. And when it finds nothing it applies
the creature's `AngerNoFood`.

### The difference is real

**Two of the 145 OBJECTS records carry `TypeFood`: the Chicken (9) and the
Torture Pendulum (65).** So a pendulum standing in a prison is food to a
prisoner and one standing in a hatchery is not. This repository asserted *"only
the Chicken carries `ObjectFlags.TypeFood`"* in a doc comment until 2026-09-06,
when somebody counted; a real-data test counts it now.

Whether a pendulum can ever be in a prison is a separate question and the answer
is probably no — its home room is the Torture Chamber. The point is that the two
searches are not the same predicate, and reading one and assuming the other is
how a difference like that gets lost.

### Nothing is reserved

The chosen bird's id lives on the creature and nowhere else. Two hungry
creatures can pick the same one; the second to arrive finds it gone and searches
again. **CONFIRMED by the owner** for prisoners (Q172, 2026-09-05): *"if I drop
a chicken in a prison with creatures in they rush toward that chicken, the first
one to it gets it, eats it, and its health replenishes by some amount"*.

---

## 7. Eating, and the two amounts

Within a squared distance of `0x1001` — one tile in a fixed point whose tile is
4096 — the creature eats. In order: an effect on the bird, the health top-up
above, the subtract, an effect on the creature, the bird destroyed,
`LastAteTime` stamped.

**The subtract is where the two paths differ, and the difference is in the
binary rather than in the reading:**

| Where | Argument to the subtract |
|---|---|
| Hatchery (`FUN_00463760`) | literal **1** |
| Prison (`FUN_00473750`) | the level-scaled **`HungerFill`** |

So a caged Bile Demon is filled by a single bird where a free one eats five.
Reproduced, not reconciled.

The subtract itself (`FUN_00477ad0`) is
`HungerNeed = max(0, HungerNeed - n)`, fires an event when it reaches zero, and
**returns the remaining need**. The eat state stores `HungerNeed != 0` in a byte
on the state object and the eating states branch on that byte to go round again
— which is what makes `HungerFill` mean *chickens needed to be full* even though
each bird pays off one: **entering** costs a whole helping, **continuing** costs
nothing.

Afterwards the original goes to `0x5E` (wait out the animation) and has `0x5F`
for wandering a random adjacent tile inside the room, chosen with an RNG call
that carries `HungerState.cpp` and the line number — 367 and 413 — in its
arguments.

---

## 8. What is UNKNOWN

- **How a hungry creature that is not already standing in a hatchery gets
  there.** `FUN_004633d0` tests that a suitable hatchery exists and then enters
  `0x5B` — whose handler fails outright when the creature is not in one. State
  `0x5A` does exactly the missing walk, and the only reference to it found is a
  fallback for one particular player id at `0x004775ac`. **Q174**, and two
  hypotheses are now dead rather than untested:
  - **not a category-enter hook.** The category table (§10) gives category 8 —
    the eating group — no enter hook at all. Categories 4, 5, 9, 12, 13, 14, 15,
    16, 17, 22, 25 and 31 have one; 8 does not.
  - **and a failing handler does not stall.** The creature update
    (`FUN_00476fc0`) calls the state's own handler and, when it returns 0,
    **`SetState(1)`** — so a creature whose `0x5B` fails is dropped to state 1
    and asks again a second later. It does not get stuck; it simply does not
    walk. Which leaves `FUN_004ded00`'s discarded out-parameter as the last
    unexplained thing in the attempt.
- **What object flag `0x4000` means** — the per-tile skip in the hatchery search.
- ~~**Whether a sleeping creature wakes to eat.**~~ **ANSWERED, and the answer
  is no** (Q176). The needs routine's first line is
  `if (stateTable[CurrentStateId].byte6 == 7) return 0`, and the sleep states
  `0x52`–`0x54` carry byte6 = 7. Corroborated by the sleep block's own
  `category != 9` refusal, 9 being exactly those three states' category: the
  routine does not re-ask a question you are already answering. What is still
  undecoded is `byte8 & 8`, which suppresses the *increment* rather than the
  attempt.
- **Whether hunger really outranks sleep in play.** It does in the routine's
  source order; whether a player would ever see the difference depends on
  category gating that is not decoded.
- **The `MaxFreeRangeChickensPerPlayer` cap** (12) — checked before laying in
  the original, and nothing here takes a chicken out of a room, so it still has
  nothing to count.

## 9. What this project built from it

ADR-0067, 2026-09-06: `EatingRules`, `CreatureEatSystem`, the accumulator
(`CreatureState.HungerNeed`, was `ChickensNeeded`) and `FoodAttemptTicks`.
`HashVersion` and snapshot `FormatVersion` 26 → 27. Scenario
`a_goblin_eats_a_chicken`.

What is **ours** and said so in the code: composing states `0x5A` and `0x5B` into
one walk-then-search (Q174 above); driving the retry from the eat system rather
than from a needs routine shared with sleep; leaving a sleeper asleep (Q176); and
no post-meal wander (Q175).

---

## 10. The state category table — `0x00699688`

Found while chasing Q174, and it answers **Q173** on the way past, so it is
recorded here rather than left in a scratchpad.

Every state record carries two category bytes. **Byte 7 is the group** — the
mechanic the state belongs to — and there are 32 of them, described by a table
of **32 records of 0x40 bytes based at `0x00699688`**, filled by the same kind of
run of `MOV`s as the state table (`0x004798f4`–`0x0047a773`, no function).

| Offset | What it is |
|---|---|
| `+0x00` | `1 << category` — the group's own bit |
| `+0x04` | **enter** hook, called by `SetState` when the category changes |
| `+0x14` | **update** hook, called by the creature update each tick |
| `+0x24` | **leave** hook |
| `+0x34` | a mask of categories this one may be left for |
| `+0x38` | the category's own id |
| `+0x3c` | a small integer, meaning not established |

**Byte 6 looks like a priority** — 0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 12, 13, 50,
100, with the idle state at 100 — and **7 is the value the needs routine treats
as "do not ask"**.

Groups relevant here:

| Category | States | Enter | Update | Leave |
|---|---|---|---|---|
| 2 | 6, 7, 8, 9, 0x58 | — | — | — | the **utility** move states; `SetState` special-cases this number |
| 8 | 0x5B–0x5F | **none** | `FUN_004b4c20` | — | **eating** |
| 9 | 0x52–0x54 | `FUN_004b4c20` | `FUN_0047ac00` | `FUN_0047ab90` | **sleeping** |
| 14 | 0x72–0x77 | `FUN_0047b740` | **`FUN_0047b7c0`** | `FUN_0047b920` | **the prison** |

**That last row is Q173.** `FUN_0047b7c0` — the routine that applies
`PrisonModifyCreatureHealthPerSecond` and had no callers anywhere — is
category 14's per-tick update hook, and category 14 is exactly the six states of
`PrisonState.cpp`. The "long run of stores at `0x00699990` alongside four sibling
handlers, each with a small integer" that Q173 recorded was this table being
filled, and the small integers were category indices.

**And the dispatcher, from `FUN_00476fc0`:**

```c
Age++;
... category enter/update hooks ...
if (CurrentStateId != 0 && stateTable[CurrentStateId].handler() == 0)
    SetState(1);            // a handler that returns 0 drops the creature to idle
GameTickLastUpdated = now;
```

So **returning 0 from a state handler is "I could not"**, and the answer is
always the same: back to state 1. That is why the eating states re-enter
themselves rather than returning 0 when they merely have nothing to do this
tick.

