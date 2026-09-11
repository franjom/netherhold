# The Prison: capture, withering, skeletons and the jail break

| | |
|---|---|
| Source(s) | `DKII.EXE` v1.70 (facts in our own words, ADR-0046) — `FUN_004e70a0` and `FUN_00490240` (the shipped debug dumps), `FUN_0047b7c0` (the drain), `FUN_004e2e70` (`prison.cpp`'s jail break), `FUN_00473550` / `FUN_00473750` (the food hunt), `Room.cpp:1717` (the step), the state and category tables; 1999 manual p.23; owner play 2026-09-05 to 2026-09-07 |
| Implementation | `PrisonRules`, `Systems/PrisonSystem`, `Systems/PrisonerWanderSystem`, `Systems/PrisonerEatSystem`, the enemy branch of `CarryRules.TryDestinationFor`, the ownership exception in `HandRules.CanPickUp`, `CreatureState.PrisonOwnerPlayerId`, `EatingRules.EatInPrison`. Decisions: ADR-0064, ADR-0070 to ADR-0075 |
| Tests | `PrisonTests`, `EatingTests`; `scenarios/creatures/an_imp_drags_an_enemy_to_prison.yaml`, `scenarios/creatures/a_keeper_drops_a_chicken_in_a_prison.yaml` |
| Confidence | **CONFIRMED**: the drain and its hunger gate, the capacity rule, the arrival health, the wander loop, the food race, the jail-break trigger, and that only an imp captures while the hand may lift a prisoner out. **STRONGLY INFERRED**: the Skeleton on death, imps stopping when prisons are full, a lost prison freeing its enemies — the manual states each. **UNKNOWN**: the pick-up handler's real ownership gate (Q161), what the two refused creature states are, which shipped field the occupancy byte is |
| Known exceptions | Ours drains in single seconds without the accompanying anger, rolls the jail break once a second (Q168), frees prisoners where they stand rather than at the door, and has no keeper-set door bar and no torture chamber — §5 |
| Open questions | Q161 (the pick-up handler's real ownership gate), Q164, Q166, Q168 (our jail-break cadence), Q171, Q184, Q185, Q186, Q188 |

Milestone M8. The other branch of DK2's imp task **kind 13**, and the half of carrying that `creature-death.md` and ADR-0063 had to stop short of.
## 1. What DK2 keeps, in its own words

The executable ships its debug dumps as `printf` calls naming each field. **The
room dump** (`FUN_004e70a0`) prints every room's common fields — three separate
occupancy lists by head index (`CreatureList`, `ObjectList`, `DeadBodyList`),
`TotalCapacity` and `TotalFighterCapacity`, the four `Used*Capacity` counters,
and `Functional`, `Size`, `Destroyed`, `Health` — then switches on the room type.
The **Prison** (type `0xb`) adds:

| field | what it is |
|---|---|
| `SafeCreatureCapacity` | **the door bar** — the manual's *"bar that stops further prisoners being delivered"*, so the limit a keeper sets is a stored number, not a gesture |
| `PrisonJailBroken` | a flag, so a break is a state the room remembers |
| `DoorTileX`, `DoorTileY`, `DoorOpen`, `DelayAutoCloseDoorCount` | the door is a place, and a jail break puts the freed at it (§4) |
| `DripEffectCounter`, `FliesEffectCounter` | shared with the Torture Chamber and the Combat Pit |

**A room's capacity is stored, not derived** — how DK2 computes it was Q166, now
read (§5). And **the Torture Chamber keeps nothing but two effect counters**, so
torture's state lives on the creature, where the creature dump puts it:
`TorturedDuration`, `TortureVoluntary`, `PlayerTorturedCreatureTypeListNode`.
**The creature dump** (`FUN_00490240`) names 162 fields by offset; four sit
together:

| offset | DK2's name | what it is |
|---|---|---|
| `0x3c3` | `CarryingId` | what this creature is carrying — an **object** |
| `0x3c5` | `DraggingId` | what it is dragging — a **creature** |
| `0x3c7` | `BeingDragged` | the other end of it |
| `0x3cb` | **`PrisonOwner`** | the keeper whose prison holds this creature |

`PrisonOwner` being a separate field from the owner is the whole shape of the
mechanic: **a prisoner changes jailers, not keepers.** Reproduced as
`CreatureState.PrisonOwnerPlayerId`, with `DraggingId`/`BeingDragged` as
`CarriedCreatureId`/`CarriedByCreatureId` (ADR-0063, which guessed the pair
before the dump named it). `CarryingId` is still free, and it is the field an
object-carrying task would use — kind 8.

## 2. Who may pick a creature up

A captured creature arrives at **a sixth of its maximum health** — DK2's `ModifyHealth` writes `max / 6` when it takes a creature below zero, so a Goblin
arrives at **250** of 1500 and turns to bone in **83** game seconds (Q167,
ADR-0071). The owner drew the line on who may carry it, 2026-09-05:

> *"a corpse can not be picked up by hand of evil and dropped to a graveyard,
> only an imp can carry it to its grave, also an injured creature, lying on the
> floor bleeding out, can't be picked up by hand of evil and dropped to its lair,
> only an imp can carry it to its bed to recover, likewise only an imp can drag
> an enemy creature to prison, a hand of evil can then pick up that creature from
> prison and drop it around the dungeon, back in prison, or onto a torture rack
> in the torture chamber."*

The pick-up handler `FUN_004bc500`'s creature branch is four tests:

```
if (SneakingAs != 0 && owner != me)      refuse, with the "no" feedback
if (StateFlags1 & 0x200000)              refuse
if (state == 0x33)                       refuse
if (state == 0x34)                       refuse
```

Bit `0x200000` of the first state-flag word is bit 21, and the creature dump's own
list of that word's bits gives bit 21 as **`CreatureDying`**, so the original
refuses a dying creature outright, exactly as the owner says. ADR-0062 had
recorded the opposite — *"DK2 lets the hand take a collapsed creature"* — and
refused one anyway while calling the refusal an invented narrowing (Q161): a
claim nothing checked, and the pattern is a standing review question in
`GOAL_ALIGNMENT.md`.

**What is still not read is the ownership test.** `SneakingAs` (`0x4a3`) is a
creature-type id a creature is disguised as, so taken literally the test refuses
an enemy only when it is *disguised*. That cannot be the whole rule, so the real
gate is somewhere this reading has not reached, most likely the virtual call the
handler makes first (`vtable + 0x28`). **This project refuses every creature its
keeper does not own, with one exception: a creature whose `PrisonOwner` is the
keeper.** That is what the owner describes, and `PrisonOwner` existing as a field
is what makes it a cheap test rather than a search of every prison.

The two refused states, `0x33` and `0x34`, are also unread. The state field
`0x25e` takes values across the whole byte range in the binary's comparisons
(`0x6`, `0x4c`, `0x74`, `0x7e`, `0x88`, `0xc1`, `0xcb`, `0xf2`, `0xfc` and more)
— one class per `*State.cpp` file, forty in `source-file-map.md`.

## 3. Capture

DK2's imp Tier A job table is `14, 8, 9, 1, 3, 13, 12` — whole-map, first that
takes (`imp-task-priority.md` §4). **Kind 13** is *carry an unconscious
creature*, and its handler carries a pair of room ids, **2** and **11**: a Lair
for the carrier's keeper's own, a **Prison** for anybody else's — one task, one
priority, two destinations. Three consequences reproduced here:

- **Capture sits below claiming.** An imp takes no prisoners while there is
  unclaimed ground: in `an_imp_drags_an_enemy_to_prison` the goblin lies on the
  floor while the imp claims the whole map first.
- **The cell is chosen before the walk starts**, like a bed and like a graveyard
  tile. A keeper with no prison has an imp that never sets off — the manual's
  *"imps stop collecting prisoners"* falling out of the same rule.
- **The rescue window is a real deadline**: in that run the capture lands ten
  ticks before the window expires. A prison too far from the fighting collects
  corpses instead of prisoners, which is dungeon design rather than an oversight
  (Q164).

## 4. What the prison does

From the manual (p.23) and the shipped variables:

| rule | where it comes from | built |
|---|---|---|
| *"whilst in Prison, creatures gradually lose health as they wither away"* | `PrisonModifyCreatureHealthPerSecond` = **−3** | yes, once a game second |
| *"any creatures that perish in Prison will rise as Skeletons under your control"* | `RoomDef.CreatedCreatureId` = **9** | yes, and the skeleton is the **jailer's** — but *perish* may not mean every death: see Q306, Q307 |
| Chickens dropped on a prisoner keep it alive | `manual-1999.md` §Hatchery | yes (ADR-0074, ADR-0075, §7) |
| The door bar stops further deliveries | `SafeCreatureCapacity` on the room | **no** — no keeper-set limit here |
| Full prison → next nearest with space → none → stop collecting | manual | yes, via the destination test |
| Jail break on overcrowding | `OvercrowdingJailBreakPercentagePerExtraCreature` = **50** | the rule is built; its once-a-second cadence contradicts DK2's one roll per over-capacity insertion (Q168) |
| Jail break on selling part of a prison in use | manual | yes, by a different route — below |
| Losing a prison frees every enemy, and keeps every friendly | manual | yes |
| *"Motionless in prison"* | `MotionlessInPrisonSeconds` = 8 | no — and **the engine does not read it either** (§6.2), so it is dead in v1.70 |

**Selling and losing are one rule here, not two.** `PrisonSystem` asks each game
second whether a prisoner still stands on a tile of a prison belonging to the
keeper who imprisoned it, and frees it if not — covering the sale, the tile being
dug away, a rival claiming it and the room being destroyed, without hooking any
of those paths. The same choice `RoomInstance` makes: derived state cannot drift.

**A prisoner that dies in a prison does not die the ordinary death.** No
collapse, no rescue window, no corpse — it is replaced where it stands, which is
why `CreatureHealthSystem` skips prisoners entirely: one system settles a
prisoner's second.

**The jail break's destination is the door.** `prison.cpp`'s routine
(`FUN_004e2e70`) walks the prison's slabs, takes each creature standing on one
that is not flagged `CreatureDying`, and releases it at `DoorTileX`/`DoorTileY`
with an effect. This project has no door and frees prisoners where they stand —
cosmetic today, and not cosmetic the moment prisons have walls a creature has to
come out through.

## 4b. The drain

**`PrisonModifyCreatureHealthPerSecond` is real and is read.** Its global is
`0x006F2621`, a **signed 16-bit** slot, and two places read it: `FUN_0047b7c0`,
which applies it, and `FUN_004b2ce0`, the engine's general "change this thing's
health by N", which tests `delta == variable55 * 4` to **recognise the prison
drain by its amount** and suppress the ordinary hurt reaction. Using the
magnitude as an identifier means no other effect in the game may deal exactly
that number.

The applier's cadence and gate are its own prologue:

```
  if (counter != 0) return           ; counter at state + 0xab
  counter = variable19 * 4           ; 4 ticks a game second, so 16 ticks
  if (HungerFill != 0 && creature.IsInARoom) {
      health -= variable55 * 4       ; -3 * 4 = -12
      anger  += variable78, reason 9 ; ModifyAngerInCompanyOfHatedCreaturesPerSecond, 150
  }
```

So the original applies **−12 every 16 ticks** — −12 every four game seconds, an
average of exactly the −3 a second the variable's name promises. **This project
applies −3 every game second**: the same average, four times finer, so a prisoner
here dies at the same moment rather than in the same steps, and **without the
anger**, which is not built. The only way to tell the cadences apart is to watch
a health bar tick.

### It is the prison's, and it is hunger

**Every creature state carries a category byte, and there is a table of 32
categories at `0x00699688`** — 0x40 bytes each, with an **enter**, an **update**
and a **leave** hook. **`FUN_0047b7c0` is category 14's update hook**, and
category 14 is exactly the six states of `PrisonState.cpp` (0x72–0x77). So it is
the prison's beyond argument: it runs on a creature because that creature is in a
prison state, which is why it needs no room-type test. Its siblings are the
category's enter hook `FUN_0047b740` and leave hook `FUN_0047b920`. **Q173
answered** (`eating.md` §10).

The gate's "per-creature-definition byte scaled by a per-level percentage" is
**`HungerFill`** — creature stat row 9, the pair `eating.md` §4 pins.

**The drain is hunger.** A creature that never eats never withers — the Imp, the
Vampire, the Skeleton and Horny — and the **Skeleton** is what a prison *makes*,
so the room's own product is immune to the room. The owner guessed this on
2026-09-05 (*"probably from hunger"*) as explicit speculation, and the binary says
it was right. Built: `PrisonRules.WitherPerSecond` takes the
creature and returns 0 for one that cannot get hungry, and `PrisonTests` has both
halves — a Skeleton sitting at its arrival health for sixty seconds, a Goblin on
the same map and clock that does not.

**Two dead variables here.** `MotionlessInPrisonSeconds` (`0x006F25E9`) is written
by its dispatch stub and read nowhere; so is
`CreatureCriticalHealthPercentageOfMax` (`0x006F294A`), which had supplied the 20%
a captured creature was thought to wake at — the owner's *"two petals"* stands as
an observation, but the flower has six petals and 20 has no shipped-variable
support (Q171).

## 5. What is ours, stated

- **The drain is applied in single seconds rather than four-second lumps**
  (§4b), and **without the anger that accompanies it in the original.**
- **The jail break is rolled once a game second here, and that is wrong**
  (Q168). DK2 rolls only in the room-insertion path, once, after a creature joins
  a Prison whose occupancy now exceeds its safe capacity: `random(100)` against
  `extra creatures × 50`, with no re-roll while occupancy is unchanged. One extra
  creature gets one 50% chance; two or more make that insertion certain.
  Re-read independently 2026-09-07, two nuances: the room's occupancy counters
  grow by a per-creature-type byte from the definition (runtime `+0x672`) rather
  than by one, so "extra" is in those units and which shipped field that byte is
  remains UNKNOWN; and an overcrowding event is raised on every over-capacity
  insertion regardless of the roll.
- **No keeper-set door bar.** `SafeCreatureCapacity` is a real stored field with
  no interface here to set it, so overcrowding is only ever a keeper dropping a
  prisoner into a full prison by hand.
- **Freed where they stand**, not at the door (§4).
- **No torture chamber**, so the third destination the owner named for a lifted
  prisoner does not exist: dropped anywhere that is not a prison tile, a
  prisoner goes free; dropped back onto a prison tile it is imprisoned again
  (CONFIRMED, owner, both halves).
- **A prisoner is not slappable.** The ownership exception is for lifting one out
  of a cell, and nothing observed extends it to slapping a rival's creature.

**Capacity is the inner tiles plus one** (Q166, ADR-0070), and DK2 derives it
once per room at build time rather than continuously. The prison's case walks the
room's own tiles, counts the ones a general *"nothing is standing here"*
predicate accepts — everything but the rim, the tiles its bars and gate have not
taken — and **adds one if that count is non-zero**, into `SafeCreatureCapacity`.
`TotalCapacity` is left at **−1** for a prison, which is why a keeper can
overcrowd one by hand at all. Below 3x3 the count is zero, the "+1" never fires,
and the room does nothing. The owner built a 3x3 and read **two** off the bar.

**The rendering shows the same split, at both observed sizes.** The rim is the
stone band and the fenced floor is the interior, so a prisoner never reaches it
(ADR-0139). A 5x5 fences a 3x3 and reads **6 / 10**; a 3x3 fences **one tile** and
reads 0 / 2, so on the smallest prison nobody can move (owner, 2026-09-11).

## What this opens

Three mechanics have nothing to stand on without the prison. **Torture**:
`TortureHpChange`, `TortureMoodChange` and `TortureTimeToConvert` are
per-creature fields parsed and unread, with `TortureChanceOfDyingWhenConverted`
= 0 and `TortureSpeedUpDurationSeconds` = 40 in the balance table, and the
Mistress the exception to every unhappiness rule about it. **Conversion**, which
is how a keeper gets a creature type its portal will never offer, and which the
Temple's *"converted enemy creatures produce twice as much Mana"* already prices.
And **combat** from the other end: everything here begins after a fight, and
there is no fight, which is why `an_imp_drags_an_enemy_to_prison` places its
goblin at zero health.

## 6. What a prisoner does between arriving and dying — CONFIRMED

Read out of `DKII.EXE` on 2026-09-06 under the corrected state-table frame
(`creature-state-table.md`). The prison occupies **states 114–126**, all of
category **14**; torture is 127–136, category 15. Three states are the loop:

| state | label | what it does |
|---|---|---|
| **114** (`0x72`) | `being held captive` | Arrival. Finds the room of type `0xb`, checks it with `FUN_00454110` — the same tile predicate the capacity routine counts with — records the jailer, enters 115 |
| **115** (`0x73`) | `being held captive` | The decision. Hungry enough *and* past `TimeBetweenReattemptingHungerSeconds` → state **117**, the hunt and not the meal (§7). Otherwise ask for a neighbouring tile, issue a move to it, enter 116 |
| **116** (`0x74`) | `being held captive` | The walk, and the pause after it. Each tick, `random(100) < 20` → back to 115 |

### 6.1 The step

`Room.cpp:1717` picks it, and the decompiler garbles that function badly enough
that this was read from the instructions. It takes a neighbour **count**, draws
`random(count)` as a **starting index**, and scans `(start + i) % count` forwards
through a table of offset pairs — the **four orthogonal** offsets at `0x006BC350`
when the count is 4, the eight at `0x006BC370` otherwise. The prison passes
**4**. The first candidate that passes wins, and a candidate must:

1. have a non-zero room id;
2. belong to the **same room instance** the creature is standing in;
3. have a terrain flag at `TerrainTable[terrain] + 0x17b` clear.

Rule 2 is what keeps a prisoner in its cell, and why nothing needs a separate
*"do not leave the prison"* test: a step out of the room is never a candidate.
Nothing checks whether another prisoner is already on the tile, so **two
prisoners may share one** — which costs nothing, capacity being the inner tiles
plus one. The table order is **north, east, south, west**, and it is not
cosmetic: it decides which way a prisoner goes when its drawn direction is a wall.

### 6.2 `MotionlessInPrisonSeconds` is a dead variable

This project guessed the shipped `MotionlessInPrisonSeconds` = 8 was *"how long
one stands still between moves"*. A raw `.text` scan for its global
`0x006F25E9` returns exactly one hit, `MOV [0x006f25e9],EAX`, the setter stub
writing it. **Nothing in DK2 reads it.** That makes it the **third** shipped
balance variable found dead, after `CreatureCriticalHealthPercentageOfMax` —
which cost a wrong prisoner clock for a day (ADR-0071) — and
`CriticalHealthPercentageForHeroLair`. The real pause is the 20-in-100 roll in
state 116, which is code rather than a table entry.

### 6.3 What is not modelled

- State 116 also plays idle flourishes on a second roll and turns to look at
  something in the room on a ten-tick cadence: both cosmetic, both gated on an
  animation slot (`creature+0x3a9`) with no counterpart here.
- A **narrower variant of the step roll** takes rolls 10..19 instead of 0..19
  when a per-creature flag is set, halving the rate to 10 in 100 — flag unread,
  Q188.
- The three unread function pointers in every state record, and the 32 category
  records category 14 indexes (Q184, Q186).

## 7. The chicken in the cell — Q195

The owner watched three things in a prison (Q172); two are §6, and this is the third: *"they rush toward that chicken, the first one to reach it eats it, and its health replenishes."* **Every state number here reads one higher than it would have before ADR-0072** — under the old frame state 117's handler was filed at 116 and the whole search looked like a variant of the walk.

### 7.1 The three states

| state | what it does |
|---|---|
| **115** (`0x73`) | Hungry (`HungerNeed >= HungerFill`, level-scaled) **and** past `TimeBetweenReattemptingHungerSeconds` since the last try → stamp the clock, go to **117**. Otherwise wander |
| **117** (`0x75`) | `FUN_00473550`. Walk the prison's own tile chain; on each tile walk that tile's object list; keep every object of kind `2` whose definition carries flag `0x80` at `+0xf4`; rank by **straight-line squared distance** and take the smallest. Issue a move to it, resuming in **118**. Found nothing → apply `AngerNoFood`, tell the interface, drop back to **116** |
| **118** (`0x76`) | `FUN_00473750`. A food-flagged object on the tile underfoot: destroy it, and pay off hunger by **the whole of `HungerFill`**. Still owing → back to **117**. **Nothing there → also back to 117** |

**117 is a walk, not a meal**: `FUN_00471890(object + 0x1a, 0x76, 0x80)` is a move order with a resume state, and 0x76 is 118.

### 7.2 The race is the absence of a reservation

Nothing marks the chosen bird. The chooser writes the object on itself and on
nothing else, so two hungry prisoners pick the same chicken and one arrives to an
empty tile — whereupon 118's loop falls through to 117 and it looks again. That
is the whole of what the owner described, and it needs no mechanism beyond
leaving one out. Two consequences: **the ranking is straight-line, not path
length**, so a bird two tiles away through a wall beats one three tiles down a
corridor (inside a prison the two rarely disagree); and **the tie is broken by
the room's own tile order**, the comparison being strict `<`, where ours breaks
it by object id (ADR-0001) — so the original's answer depends on the order a
room happens to hold its tiles in and ours may legitimately differ.

### 7.3 One bird fills a prisoner

State 118 passes the level-scaled `HungerFill` to the routine its **hatchery**
counterpart passes a literal `1` to, so a caged Bile Demon, which owes five
helpings, is filled by a single chicken where a free one eats five.
`EatingRules.EatInPrison` carries the number; `PrisonerEatSystem` calls it.

### 7.4 The anger is a quarter of the tabled figure

State 117's empty-handed branch calls the anger routine with the creature
definition's loaded `AngerNoFood` — and **the loader divided it by four on the
way in**. The same register multiplies `TimeAwake` and `TimeSleep` into ticks two
dozen instructions earlier, which is what pins it: what the engine holds is
**anger per DK2 tick**, and DK2 runs four ticks to a game second on every shipped
level (Q1/Q132).

A routine that adds one such unit *per tick* delivers the tabled figure per
second — which is what the lair's anger does and what the owner measured. **This
one adds one per attempt**, and attempts are gated to at most one a game second.
So a hungry prisoner in an unstocked cell angers at **25 a second where a bedless
creature angers at 100**. The loader floors the quotient at 1 whenever the source
is non-zero, so the Imp, the Dwarf and Horny keep their 1 rather than being
rounded to nothing.

### 7.5 A prisoner does get hungry, and the bit that says so

DK2's needs routine (`FUN_00477c20`) suppresses the hunger clock when
`stateFlags & 8` is set, the flag being the state record's byte at `+0x18`. **The
fifteen states that set it are exactly drinking, gambling and dancing** — the
Casino, not the Prison. Bit `0x10` beside it suppresses the tiredness clock over
the same fifteen. Every prison state has both clear. That is what makes state
115's hunger test reachable at all, and it is the first thing anything has been
read to do with the flag byte (Q185).

**But a prisoner never gets hungry *in* a cell**: a Goblin's `HungerRate` is 180
game seconds and the drain turns it to bone in 83. The mechanic is reachable
because `HungerNeed` accumulates over a creature's **whole life** and is paid off
only by eating, so anything dragged in after a few minutes on the map is hungry
the moment it lands.

### 7.6 The general needs routine routes to the prison, and does not check imprisonment

`FUN_004633d0` — the "go and eat" attempt the needs routine calls — asks *which
room am I standing in*, and if it is type `0xb` it goes straight to state 117
instead of looking for a hatchery. **It does not ask whether the creature is a
prisoner.** So any hungry creature standing in a prison eats from the prison's
floor rather than walking to a hatchery. Only prisoners loiter in prisons, so
nothing observable turns on it — but it is the original's rule and not a
prisoner-specific one, and this project's version is prisoner-specific.

### 7.7 What is ours in the implementation

- **The route to the bird is restricted to the cell's own tiles.** DK2 issues an
  ordinary move and relies on the bird being in the room the prisoner stands in;
  ours would route an L-shaped prison's corner through the corridor outside,
  which would look exactly like an escape. A narrowing, and named.
- **The search runs from a system rather than a state machine.**
  `PrisonerEatSystem` runs before `PrisonerWanderSystem` because the original's
  single handler asks about hunger first.
- **`CreatureAngerRules.CanBeAngered`** is DK2's own gate: `CreatureFlags2.NeverAngered`,
  bit 2 of CREATURES `0x1545`, carried by the Skeleton and Jack (ADR-0087, Q210).
  The Imp, the Dwarf and Horny are not flagged and express it with a rate of 1.
