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
| A prisoner loses `PrisonModifyCreatureHealthPerSecond` = **-3** a game second | **STRONGLY INFERRED** — the manual states the effect in words, the variable is named for it, and the owner watched it happen |
| A captured creature wakes at **two petals** — 20% of its maximum | **CONFIRMED** (owner, 2026-09-05, Q167). Built as full health for one afternoon before the correction |
| The flower above a creature's head is a **health** readout | **CONFIRMED** (owner, twice, Q108) |
| A prisoner **walks about its cell and races the others to a dropped chicken** | **CONFIRMED** (owner, Q172) and **not built** — prisoners here are inert |
| A prisoner that runs out **rises as a Skeleton for the jailer** | **STRONGLY INFERRED** — the manual states it, and the Prison's `CreatedCreatureId` is 9 |
| Imps stop collecting when every prison is full | **STRONGLY INFERRED** — the manual states it; it falls out of the destination being chosen before the walk |
| Losing a prison frees every enemy in it | **STRONGLY INFERRED** — the manual states it |
| A jail break on overcrowding, at `OvercrowdingJailBreakPercentagePerExtraCreature` = **50** | **STRONGLY INFERRED** for the rule, **ours** for the cadence (Q168) |
| **One prisoner per tile** | **OURS** (Q166) — the weakest inference here |

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
| *"Motionless in prison"* | `MotionlessInPrisonSeconds` = 8 | no — parsed, unread |

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

## 5. What is ours, stated

- **One prisoner per tile** (Q166). DK2 stores capacity rather than deriving it,
  so this is a guess shaped like the lair's one-bed-per-tile. **The owner can
  settle it in a minute**: build a 3x3 prison, capture until the imps stop, and
  count.
- ~~**Full health on capture.**~~ **Corrected the same day** (Q167): the owner
  says *"its flower gets two petals"*, and two petals of ten is
  `CreatureCriticalHealthPercentageOfMax` = 20. A Goblin arrives at 300 of 1500
  and becomes a Skeleton in a hundred game seconds rather than five hundred.
  What is still ours is the **petal count** the arithmetic rests on (Q171).
- **A prisoner does nothing at all**, and in the original it does three things:
  it moves about its cell, it races the other prisoners to a chicken dropped
  among them, and it eats one (Q172). All three wait on the same thing — an
  object a keeper can pick up and drop.
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
