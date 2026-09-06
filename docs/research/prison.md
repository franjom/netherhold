# The Prison: capture, withering, skeletons and the jail break

Milestone M8. The other branch of DK2's imp task **kind 13**, and the half of
carrying that `creature-death.md` and ADR-0063 had to stop short of.

Implemented by `DK2.Simulation.PrisonRules`, `PrisonSystem`, the enemy branch of
`CarryRules.TryDestinationFor`, and the ownership exception in
`HandRules.CanPickUp`. Decisions: **ADR-0064**.

Read out of the original, the 1999 manual and the owner's play on
**2026-09-05**. Addresses below are `DKII.EXE` v1.70 and are **facts about the
binary in our own words** — no decompiler output enters this repository
(ADR-0046).

---

## Summary of confidence

| Claim | Status |
|---|---|
| An imp carries an unconscious **enemy** to a Prison, and its keeper's own to a Lair — one task, two rooms | **STRONGLY INFERRED** from the task handler's room-type pair (2 and 11), and **CONFIRMED** in words by the owner (Q99) |
| **Only an imp captures.** The keeper's hand cannot take a body off the floor at all | **CONFIRMED** (owner, 2026-09-05) and corroborated by the binary — see §2 |
| **The hand may lift a creature out of a prison**, even an enemy's | **CONFIRMED** (owner, 2026-09-05) |
| A prisoner dropped back on a prison tile is imprisoned again; dropped anywhere else it goes free | **CONFIRMED** (owner) for both halves; the torture rack is the third destination and is unbuilt |
| A prisoner loses `PrisonModifyCreatureHealthPerSecond` = **-3** a game second | **CONFIRMED from the binary** (§4b): the variable is read, and applied as **-12 every four game seconds**, which averages to -3 a second. This project applies -3 a second, which is the same average at a finer grain |
| The drain also applies **anger** | **CONFIRMED from the binary** (§4b) and **not built** |
| That the routine applying it is the *prison's* | **CONFIRMED from the binary** (§4b, 2026-09-06): it is the prison state category's own update hook, which is why it needs no room-type test. Q173 answered |
| The drain applies **only to creatures that can get hungry** | **CONFIRMED from the binary** (§4b) and **built**: a Skeleton in a cell never withers, and a Skeleton is what a prison makes |
| A captured creature wakes at **a sixth of its maximum** | **CONFIRMED from the binary** (Q167, ADR-0071): DK2's `ModifyHealth` writes `max / 6` when it takes a creature below zero. It was 20% for a day, from a variable nothing reads and a petal count that was wrong |
| The flower above a creature's head is a **health** readout | **CONFIRMED** (owner, twice, Q108) |
| A prisoner **walks about its cell** | **CONFIRMED** (owner, Q172) and **BUILT** (ADR-0073): one tile at a time, orthogonally, never out of the room, with a 20-in-100 roll per tick deciding the pause between steps. Read out of DK2's states 114-116 |
| A prisoner **races the others to a dropped chicken** | **CONFIRMED** (owner, Q172) and **not built** — the last piece of Q172. It used to wait on movement; since ADR-0073 it waits only on a keeper being able to drop a bird into a cell |
| A chicken eaten in a prison pays off **the whole of `HungerFill`**, not one | **CONFIRMED from the binary** (`eating.md` §7) — `EatingRules.EatInPrison` carries the number and nothing calls it |
| A prisoner that runs out **rises as a Skeleton for the jailer** | **STRONGLY INFERRED** — the manual states it, and the Prison's `CreatedCreatureId` is 9 |
| Imps stop collecting when every prison is full | **STRONGLY INFERRED** — the manual states it; it falls out of the destination being chosen before the walk |
| Losing a prison frees every enemy in it | **STRONGLY INFERRED** — the manual states it |
| A jail break on overcrowding, at `OvercrowdingJailBreakPercentagePerExtraCreature` = **50** | **STRONGLY INFERRED** for the rule, **ours** for the cadence (Q168) |
| **A prison holds its inner tiles plus one** — so a 3x3 holds **two**, and below 3x3 it is not a prison at all | **CONFIRMED** (owner, 2026-09-06, off DK2's status bar) and **the arithmetic is read** (ADR-0070). Was *"one prisoner per tile"*, which on a 3x3 was nine |

---

## 1. What DK2 keeps, in its own words

The executable ships its debug dumps, and they are `printf` calls with the field
name in the format string and the field itself as the argument. Two of them
matter here.

**The room dump** (`FUN_004e70a0`) prints every room's common fields and then
switches on the room type. Every room carries:

| Field | What it is |
|---|---|
| `CreatureList`, `ObjectList`, `DeadBodyList` | three separate occupancy lists, by head index |
| `TotalCapacity`, `TotalFighterCapacity` | how much the room holds |
| `UsedCapacity`, `UsedCreatureCapacity`, `UsedObjectCapacity`, `UsedDeadBodyCapacity` | and how much of it is taken |
| `Functional`, `Size`, `Destroyed`, `Health` | the room's own condition |

and the **Prison** (type `0xb`) adds:

| Field | What it is |
|---|---|
| `SafeCreatureCapacity` | **the door bar** — the manual's *"bar that stops further prisoners being delivered"*, so the limit a keeper sets is a stored number and not a gesture |
| `PrisonJailBroken` | a flag, so a break is a state the room remembers |
| `DoorTileX`, `DoorTileY`, `DoorOpen`, `DelayAutoCloseDoorCount` | the door is a place, and a jail break puts the freed at it (§4) |
| `DripEffectCounter`, `FliesEffectCounter` | shared with the Torture Chamber and the Combat Pit |

Two things fall out of that list immediately. **A room's capacity is stored,
not derived** — so how DK2 computes it is a separate question this project has
not answered (Q166). And **the Torture Chamber keeps nothing but two effect
counters**, so torture's state lives on the creature, which is where the creature
dump puts it: `TorturedDuration`, `TortureVoluntary`,
`PlayerTorturedCreatureTypeListNode`.

**The creature dump** (`FUN_00490240`) names 162 fields by offset. The four that
this mechanic needed sit together:

| Offset | DK2's name | What it is |
|---|---|---|
| `0x3c3` | `CarryingId` | what this creature is carrying — an **object** |
| `0x3c5` | `DraggingId` | what it is dragging — a **creature** |
| `0x3c7` | `BeingDragged` | the other end of it |
| `0x3cb` | **`PrisonOwner`** | the keeper whose prison holds this creature |

`PrisonOwner` being a *separate field from the owner* is the whole shape of the
mechanic: **a prisoner changes jailers, not keepers**. This project reproduces it
as `CreatureState.PrisonOwnerPlayerId`, and reproduces `DraggingId`/
`BeingDragged` as `CarriedCreatureId`/`CarriedByCreatureId` (ADR-0063, which
guessed the pair before the dump named it). `CarryingId` is still free, and it is
the field an object-carrying task would use — kind 8.

---

## 2. Who may pick a creature up, settled

The owner drew the line on 2026-09-05:

> *"a corpse can not be picked up by hand of evil and dropped to a graveyard,
> only an imp can carry it to its grave, also an injured creature, lying on the
> floor bleeding out, can't be picked up by hand of evil and dropped to its lair,
> only an imp can carry it to its bed to recover, likewise only an imp can drag
> an enemy creature to prison, a hand of evil can then pick up that creature from
> prison and drop it around the dungeon, back in prison, or onto a torture rack
> in the torture chamber."*

**ADR-0062 had the first half backwards for a day.** It recorded *"DK2 lets the
hand take a collapsed creature"* and refused one anyway, calling the refusal a
narrowing of ours invented to excuse a missing mechanic (Q161). The reading
behind that claim was the pick-up handler `FUN_004bc500`, whose creature branch
is four tests:

```
if (SneakingAs != 0 && owner != me)      refuse, with the "no" feedback
if (StateFlags1 & 0x200000)              refuse
if (state == 0x33)                       refuse
if (state == 0x34)                       refuse
```

Three of those had been written down as *"two creature states and one state flag,
none of which this project has yet"* — **without naming the flag**. Bit
`0x200000` of the first state-flag word is bit 21, and the creature dump's own
list of that word's bits gives bit 21 as **`CreatureDying`**. So the original
refuses a dying creature outright, exactly as the owner says, and the claim that
it did not was a claim nothing checked. Fourth time on this project; the pattern
is in `GOAL_ALIGNMENT.md` as a standing review question.

**What is still not read** is the ownership test. `SneakingAs` (`0x4a3`) is a
creature-type id a creature is disguised as, and taken literally the test refuses
an enemy only when it is *disguised*. That cannot be the whole rule — DK2 does
not let a keeper pick up a rival's creature at large — so the real ownership gate
is somewhere this reading has not reached, most likely the virtual call the
handler makes before any of this (`vtable + 0x28`). Recorded as unread rather
than guessed at. **This project refuses every creature its keeper does not own,
with one exception: a creature whose `PrisonOwner` is the keeper.** That is what
the owner describes, and `PrisonOwner` existing as a field is what makes it a
cheap test rather than a search of every prison.

The two refused states, `0x33` and `0x34`, are also unread. The state field
`0x25e` takes values across the whole byte range in the binary's comparisons
(`0x6`, `0x4c`, `0x74`, `0x7e`, `0x88`, `0xc1`, `0xcb`, `0xf2`, `0xfc` and
more), which is a large state machine — one class per `*State.cpp` file, and
`source-file-map.md` lists forty of them. Naming those two is a task in its own
right.

---

## 3. Capture

DK2's imp Tier A job table is `14, 8, 9, 1, 3, 13, 12` — whole-map, first that
takes (`imp-task-priority.md` §4). **Kind 13** is *carry an unconscious
creature*, and its handler carries a pair of room ids, **2** and **11**: a Lair
for the carrier's keeper's own, a **Prison** for anybody else's. One task, one
priority, two destinations.

Three consequences this project reproduces:

- **Capture sits below claiming.** An imp takes no prisoners while there is
  unclaimed ground. Visible in `an_imp_drags_an_enemy_to_prison`: the goblin is
  on the floor at t19 and the imp does not come for it until **t1111**, having
  claimed the whole map first.
- **The cell is chosen before the walk starts**, like a bed and like a graveyard
  tile. A keeper with no prison has an imp that never sets off — which is the
  manual's *"imps stop collecting prisoners"* falling out of the same rule
  rather than needing to be written twice.
- **The rescue window is a real deadline.** In that same run the capture lands at
  **t1209** against a window that expires at t1219: ten ticks. A prison too far
  from the fighting collects corpses instead of prisoners, which is a piece of
  dungeon design rather than an oversight (Q164).

---

## 4. What the prison does

From the manual (p.23) and the shipped variables:

| Rule | Where it comes from | Built |
|---|---|---|
| *"whilst in Prison, creatures gradually lose health as they wither away"* | `PrisonModifyCreatureHealthPerSecond` = **-3** | yes, once a game second |
| *"any creatures that perish in Prison will rise as Skeletons under your control"* | `RoomDef.CreatedCreatureId` = **9** | yes, and the skeleton is the **jailer's** |
| Chickens dropped on a prisoner keep it alive | `manual-1999.md` §Hatchery | **no** — a chicken is an object nothing can pick up |
| The door bar stops further deliveries | `SafeCreatureCapacity` on the room | **no** — there is no keeper-set limit here |
| Full prison → next nearest with space → none → stop collecting | manual | yes, via the destination test |
| Jail break on overcrowding | `OvercrowdingJailBreakPercentagePerExtraCreature` = **50** | yes; the cadence is ours (Q168) |
| Jail break on selling part of a prison in use | manual | yes, by a different route — see below |
| Losing a prison frees every enemy, and keeps every friendly | manual | yes |
| *"Motionless in prison"* | `MotionlessInPrisonSeconds` = 8 | no — and **the engine does not read it either** (§4b), so it is dead in v1.70 rather than merely unread here |

**Selling and losing are one rule here, not two.** `PrisonSystem` asks each game
second whether a prisoner is still standing on a tile of a prison belonging to
the keeper who imprisoned it, and frees it if not. That covers the sale, the
tile being dug away, a rival claiming it, and the room being destroyed —
without hooking any of those paths. It is the same choice `RoomInstance` makes:
derived state cannot drift.

**A prisoner that dies in a prison does not die the ordinary death.** No
collapse, no rescue window, no corpse — it is replaced where it stands. That is
what the room is for, and it is why `CreatureHealthSystem` skips prisoners
entirely: one system settles a prisoner's second.

**The jail break's destination is the door.** DK2's `prison.cpp` routine
(`FUN_004e2e70`) walks the prison's slabs, takes each creature standing on one
that is not flagged `CreatureDying`, and releases it at `DoorTileX`/`DoorTileY`
with an effect. This project has no door and frees prisoners where they stand.
The difference is cosmetic today and stops being cosmetic the moment prisons have
walls a creature has to come out through.

---

## 4b. The drain, read on 2026-09-06

The owner's read was that a prisoner loses health *"probably from hunger"* and
they were explicit that it was speculation. The binary was asked instead, and
answers most of it.

**`PrisonModifyCreatureHealthPerSecond` is real and is read.** Its global is
`0x006F2621`, a **signed 16-bit** slot, and two places read it:

- **`FUN_0047b7c0`**, which applies it, and
- **`FUN_004b2ce0`**, the engine's general "change this thing's health by N",
  which tests `delta == variable55 * 4` in order to **recognise the prison
  drain by its amount** and suppress the ordinary hurt reaction. Using the
  magnitude as an identifier is a quirk worth knowing about: it means no other
  effect in the game may deal exactly that number.

**The applier's cadence is in its own prologue:**

```
  if (counter != 0) return           ; counter at state + 0xab
  counter = variable19 * 4           ; 4 ticks a game second, so 16 ticks
  ...
  health -= variable55 * 4           ; -3 * 4 = -12
  anger  += variable78, reason 9
```

So the original applies **-12 every 16 ticks**, which at four ticks a game
second is **-12 every four game seconds** — an average of exactly the **-3 a
second** the variable's name promises. **This project applies -3 every game
second**: the same average, four times finer, and a prisoner here dies at the
same moment rather than in the same steps. Recorded as a deliberate difference
rather than a defect, because the only way to tell them apart is to watch a
health bar tick.

**The drain comes with anger, and this project has none.** The same routine
applies `ModifyAngerInCompanyOfHatedCreaturesPerSecond` (variable 78, 150) with
reason 9 immediately after the health. Not built.

### It is the prison's, and it is hunger — settled the same day, from eating

The gap this section named is closed, and it closed sideways: reading
`HungerState.cpp` turned up the table that explains it (`eating.md` §10).

**Every creature state carries a category byte, and there is a table of 32
categories at `0x00699688`** — 0x40 bytes each, with an **enter** hook, an
**update** hook and a **leave** hook. **`FUN_0047b7c0` is category 14's update
hook**, and category 14 is *exactly* the six states of `PrisonState.cpp`
(0x72–0x77). So it is the prison's beyond argument: it runs on a creature
because that creature is in a prison state, which is why it needs no room-type
test of its own. Its siblings are the category's enter hook `FUN_0047b740` and
leave hook `FUN_0047b920`. **Q173 answered.**

**And the "per-creature-definition byte scaled by a per-level percentage" is
`HungerFill`** — creature stat row 9, the same pair `eating.md` §4 pins. So the
gate reads:

```c
if (HungerFill != 0 && creature.IsInARoom) { health -= var55 * 4; anger += var78, reason 9; }
```

**The drain is hunger.** A creature that never eats never withers — the Imp, the
Vampire, the Skeleton and Horny — and the **Skeleton** is what a prison
*makes*, so the room's own product is immune to the room. The owner guessed this
on 2026-09-05 (*"probably from hunger"*) and said explicitly that it was
speculation; the binary says the speculation was right.

**Built 2026-09-06**: `PrisonRules.WitherPerSecond` now takes the creature and
returns 0 for one that cannot get hungry, and `PrisonTests` has both halves —
a Skeleton that sits at its arrival health for sixty seconds and a Goblin on the
same map and the same clock that does not.

**`MotionlessInPrisonSeconds` is cached and dead.** Its global `0x006F25E9` is
written by its dispatch stub and read nowhere in the executable. Parsed, named,
and unused in v1.70.

**And a caution about this project's own Q167 arithmetic**: the 20% a captured
creature wakes at comes from `CreatureCriticalHealthPercentageOfMax`, whose
global `0x006F294A` is **also written and never read**. The owner's *"two
petals"* is an observation and stands; the number 20 is now only a reading of
it, and its shipped-variable support is gone (Q171).

---

## 5. What is ours, stated

- ~~**One prisoner per tile**~~ (Q166) — **corrected 2026-09-06 to inner tiles plus one**, and the reason DK2 stores capacity rather than deriving it is now read: it derives it once, per room type, at build time. The prison's case walks the room's own tiles, counts the ones a general *"nothing is standing here"* predicate accepts — which is the tiles its bars and gate have not taken, i.e. everything but the rim — and **adds one if that count is non-zero**, into `SafeCreatureCapacity`. `TotalCapacity` is left at **-1** for a prison, which is why a keeper can overcrowd one by hand at all. Below 3x3 the count is zero, the "+1" never fires, and the room does nothing.
  **The owner ran exactly the experiment this bullet used to ask for** — build a
  3x3, capture until the imps stop, count — and read **two** off the status bar,
  which is what the routine computes. Nothing here is ours any more.
- ~~**Full health on capture**, then ~~20% of maximum~~.~~ **Both gone**
  (Q167, ADR-0071). The 20 came from
  `CreatureCriticalHealthPercentageOfMax` read as *"two petals of ten"*, and
  both halves failed on 2026-09-06: the flower has **six** petals (Q171,
  screenshot), and a raw `.text` scan finds one reference to that variable's
  global — the loader writing it. The number is now **read** rather than fitted:
  DK2's `ModifyHealth` writes `max / 6` when a creature goes below zero, so a
  Goblin arrives at **250** of 1500 and turns to bone in **83** game seconds.
- ~~**A prisoner does nothing at all.**~~ **It wanders, as of ADR-0073**, and
  that half is no longer ours: one tile at a time, orthogonally, never out of
  the room, with the pause between steps set by a **20-in-100 roll per tick**
  read out of the original's state 116. See §6.
  **What is still missing is the bird**: nothing drops a chicken into a cell and
  nothing races for it. Its own eating number is already written down in
  `EatingRules.EatInPrison`, because **one bird pays off the whole of
  `HungerFill`** in a prison where a hatchery bird pays off one, which is in the
  binary and not a simplification. **The original's own search is
  `FUN_00473550`** — state **117**, the one the hunger check jumps to instead of
  wandering —: it walks the prison's slabs for the nearest *object* whose
  definition carries flag `0x80`, picks the nearest by straight-line distance,
  and **when there is none it applies the creature's `AngerNoFood`** — so a
  prisoner with nothing to eat gets angry by the same rule a hungry creature
  anywhere does. That is the mechanism behind what the owner watched.
- **The drain is applied in single seconds rather than four-second lumps**
  (§4b), and **without the anger that accompanies it in the original.**
- **The jail break is rolled once a game second** (Q168), on the same clock every
  other repeated chance here rides (ADR-0043). The variable names a percentage
  per extra creature and says nothing about how often.
- **No keeper-set door bar.** `SafeCreatureCapacity` is a real stored field and
  there is no interface here to set it, so overcrowding is only ever a keeper
  dropping a prisoner into a full prison by hand.
- **Freed where they stand**, not at the door (§4).
- **No torture chamber**, so the third destination the owner named for a lifted
  prisoner does not exist: dropped anywhere that is not a prison tile, it goes
  free.
- **A prisoner is not slappable.** The ownership exception is for lifting one out
  of a cell and nothing observed extends it to slapping somebody else's creature.

---

## 6. What this opens

The prison is the door into three mechanics that have nothing to stand on
without it:

- **Torture** — `TortureHpChange`, `TortureMoodChange`, `TortureTimeToConvert`
  are per-creature fields that have been parsed and unread for months, and
  `TortureChanceOfDyingWhenConverted` = 0, `TortureSpeedUpDurationSeconds` = 40
  are in the balance table. The Mistress is the exception to every unhappiness
  rule about it.
- **Conversion**, which is how a keeper gets a creature type its portal will
  never offer, and which the Temple's *"converted enemy creatures produce twice
  as much Mana"* already prices.
- **Combat**, from the other end: everything here begins after a fight, and there
  is no fight. `an_imp_drags_an_enemy_to_prison` places its goblin at zero health
  because nothing can put it there.

## 6. What a prisoner does between arriving and dying — CONFIRMED

Read out of `DKII.EXE` on 2026-09-06, under the corrected state-table frame
(`creature-state-table.md`). The prison occupies **states 114–126**, all of
category **14**; torture is 127–136, category 15.

Three of them are the loop:

| State | Label | What it does |
|---|---|---|
| **114** (`0x72`) | `being held captive` | Arrival. Finds the room of type `0xb`, checks it with `FUN_00454110` — the same tile predicate the capacity routine counts with — records the jailer, enters 115 |
| **115** (`0x73`) | `being held captive` | The decision. Hungry enough *and* past a cooldown → state **117** and eat where it stands. Otherwise ask for a neighbouring tile, issue a move to it, enter 116 |
| **116** (`0x74`) | `being held captive` | The walk, and the pause after it. Each tick, `random(100) < 20` → back to 115 |

### 6.1 The step

`Room.cpp:1717` picks it, and the decompiler garbles that function badly enough
that this was read from the instructions instead. It takes a neighbour **count**,
draws `random(count)` as a **starting index**, and scans `(start + i) % count`
forwards through a table of offset pairs — the **four orthogonal** offsets at
`0x006BC350` when the count is 4, the eight at `0x006BC370` otherwise. The
prison passes **4**. The first candidate that passes wins, and a candidate must:

1. have a non-zero room id;
2. belong to the **same room instance** the creature is standing in;
3. have a terrain flag at `TerrainTable[terrain] + 0x17b` clear.

Rule 2 is what keeps a prisoner in its cell, and it is why nothing needs a
separate *"do not leave the prison"* test: a step out of the room is never a
candidate. Nothing checks whether another prisoner is already on the tile, so
**two prisoners may share one** — which costs nothing, because capacity is the
inner tiles plus one and never was one-per-tile.

The table order is **north, east, south, west**, and it is not cosmetic: it
decides which way a prisoner goes when its drawn direction is a wall.

### 6.2 `MotionlessInPrisonSeconds` is a dead variable

This project guessed that the shipped `MotionlessInPrisonSeconds` = 8 was *"how
long one stands still between moves"*. It is not: a raw scan of `.text` for its
global `0x006F25E9` returns **exactly one hit**, and disassembling it gives
`MOV [0x006f25e9],EAX` — the setter stub writing it. **Nothing in DK2 reads it.**

That makes it the **third** shipped balance variable found dead, after
`CreatureCriticalHealthPercentageOfMax` — which cost this project a wrong
prisoner clock for a day (ADR-0071) — and `CriticalHealthPercentageForHeroLair`.
The real pause is the 20-in-100 roll in state 116, which is code rather than a
table entry.

### 6.3 What is not modelled

- State 116 also plays idle flourishes on a second roll and turns to look at
  something in the room on a ten-tick cadence. Both are cosmetic and both are
  gated on an animation slot (`creature+0x3a9`) with no counterpart here.
- A **narrower variant of the step roll** takes rolls 10..19 instead of 0..19
  when a per-creature flag is set, halving the rate to 10 in 100. The flag has
  not been read. Q188.
- The three unread function pointers in every state record, and the 32 category
  records the prison's category 14 indexes (Q184, Q186).
