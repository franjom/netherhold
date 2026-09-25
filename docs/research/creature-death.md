# Mechanic: dying — two timers, three states

Source(s):
- Owner observation, 2026-09-04 and 2026-09-07 (Q99, Q107, Q109, Q111)
- The reference install's `Creatures.kwd`, `Objects.kwd`, `Rooms.kwd` and
  `GlobalVariables.kwd`, via `DK2.Data`
- `DKII.EXE` v1.70: the common creature-health routine and the two dying-state
  handlers, read 2026-09-07 (Q110)
- OpenKeeper `HealthSystem.processHealthDepleted`, under ADR-0003's second
  amendment
- The DK2 wiki, **Dungeon Keeper 2 section only**, via the MediaWiki API
  (`wiki-as-a-source.md`, including the licence flag on mirroring any of it)

Original version: DK2 v1.70

Confidence: **CONFIRMED (owner)** for the pipeline, **CONFIRMED (data)** for the
flags and the numbers, **CONFIRMED (binary)** for the collapse window's guard.

## 1. The pipeline — CONFIRMED (owner, Q99)

> *"When a creature drops, its petals on the flower above its head are exhausted,
> it remains lying on the ground for a period of time, that gives imps an
> opportunity to return a creature to its bed in the lair for healing, or to
> prison if it is an enemy creature. Once that timer runs out a creature dies and
> its corpse remains there lying, that gives imps an opportunity to carry it to
> graveyard. If that timer runs out and no one carries the corpse it just
> disappears."*

Followed the same day by three corrections that turned one pipeline into three:

> *"Imps are dead right away, they don't have a lying state. Vampires also don't
> drop a body, they reincarnate at their sarcophagus and sleep to recover, but
> they lose a level once they reincarnate. If they were killed at level 1 they
> are permanently dead. Exception to that rule for vampires are monks: if a monk
> kills a vampire it is permanently dead, regardless of its level."*

So dying is a **pipeline**, not an event — but only for creatures that leave a
body:

```
  health -> 0
     |
     v
  COLLAPSED  (rescuable)          CreatureDyingStateDurationSeconds = 60
     |                            imp -> own bed  (friendly, heals)
     |                            imp -> prison   (enemy)
     v
  CORPSE     (carryable)          DeadBodyDiesAfterSeconds = 60
     |                            imp -> graveyard
     v
  gone
```

**Both timers are 60 seconds**, and both variables had been parsed for months
with nothing reading either:

| Variable | Id | Value |
|---|---|---|
| `CreatureDyingStateDurationSeconds` | 26 | 60 |
| `DeadBodyDiesAfterSeconds` | 23 | 60 |

Two independently named variables holding the same value looked like redundancy
before this answer. They are two clocks on two different things, and they share
one field — `CreatureState.DyingTicks` — because they never run at once, reset
at the seam so a corpse gets a fresh sixty seconds.

**Q109, CONFIRMED (owner, 2026-09-07): an enemy collapses the same way** — dying
state first, so imps have time to drag it to a prison if there is one, then death
and a corpse.

## 2. Three deaths, and one flag that picks between two of them

`GenerateDeadBody` (bit 15) decides whether running out of health puts a
creature on the floor at all:

| | Who | What happens |
|---|---|---|
| **Collapse** | the 38 creatures with `GenerateDeadBody` | 60 seconds rescuable, then dead, then a corpse |
| **Instant** | the 8 without it | gone on the spot, nothing left |
| **Reincarnate** | the 2 with `CanBeResurrected` | back at the Graveyard's sarcophagus, asleep, one level poorer |

**The instant-death rule is CONFIRMED from two directions that never met.** The
owner gave it for the Imp. OpenKeeper's `HealthSystem.processHealthDepleted`
opens with exactly the same test, before any unconscious handling:

```java
// No body, just vanish from the world
if (!isLeaveDeadBody(entityId)) {
    entityPositionLookup.getEntityController(entityId).remove();
    return;
}
```

…where `isLeaveDeadBody` reads `GENERATE_DEAD_BODY` and nothing else. That
**the dying state *is* a body on the floor** — a live one — is coherent: a
creature that leaves nothing behind had nothing to lie there. It also explains
why the bodyless set is such an odd mixture. It is not a mixture: a summon,
three undead, a construct, a worker and Jack are exactly the things that would
puff out rather than fall over.

**One flag does not decide both halves, though**, and the quotation above is
only OpenKeeper's *first* branch: it goes on to send tortured and imprisoned
entities straight to death, keyed on entity state where DK2 keys on the room
tile. Corrected 2026-09-11 — Q303, ADR-0138, `external-resources.md`.

### 2.1 Four gates on the collapsed window, in DK2's own order

`ModifyHealth` (`0x004B2CE0`) runs these between `0x004B2F2E` and `0x004B2F96`,
once health has reached zero and the creature is not already dying. **Any one
sends it straight to dead**; the corpse still follows, gate 1 having passed.

| # | gate | at |
|---|---|---|
| 1 | the definition lacks `GenerateDeadBody`, flags bit 15 | `0x004B2F2E` |
| 2 | the creature is `Possessed` | `0x004B2F37` |
| 3 | it stands in a **Prison** or **Torture Chamber** — room `TypeId` 11 or 12 | `0x004B2F81` |
| 4 | it stands in **no room** and `DieInstantly` is true | `0x004B2F8D` |

**3 and 4 are exclusive, and that is the quirk.** The room is asked first, so a
creature inside any *other* room is never asked about `DieInstantly`: a Lord Of
The Land killed in a Treasury lies there rescuable, one killed outside does not.

`DieInstantly` is `+0x3EA` bit 11. Its getter `0x0048E110` answers true for
creature types 21, 28 and 29 — King Reginald, Stone Knight and Lord Of The
Land — **before** it reads the bit; trigger action type 1 sets the bit from
its `Flag` byte's bit 2 (`trigger-dispatch-tables.md`). Entering the window, DK2
writes `CreatureDyingStateDurationSeconds × 4` = 240 into the health field
(`0x004B2FA0`), so health doubles as the rescue clock; we hold a separate
`DyingTicks` and leave health at zero.

### 2.2 A corpse is not an object

This document, ADR-0051 and several code comments gave **"a corpse is an OBJECT,
and OBJECTS is unparsed (M6)"** as the reason nothing lies on the floor after a
death. The OBJECTS record was parsed on 2026-09-04: **145 objects, and not one of
them is a corpse.** The nearest are decoration — Skeleton (48), Skull (49), Bone
(50) — and none of them carries a type flag.

Everything else already pointed the same way:

- both timers are **creature** variables (`CreatureDyingStateDurationSeconds`,
  `DeadBodyDiesAfterSeconds`);
- `DecomposeValue` is a **creature** field;
- the binary's per-creature debug dump carries **`HasBeenKnockedOut`** and
  **`DieInstantly`**, and `Creature.cpp` owns the dying state.

**A corpse is not an object, nor a creature state:** DK2 makes a `CDeadBody` thing
(`dying-states.md` §2). Ours keeps the creature in `CreatureActivity.Dead`, with no state id.

### 2.2 Where a vampire comes back — the Graveyard (Q113)

The owner's first wording was *"they reincarnate at their sarcophagus"*, which
this project read as the vampire's own bed, because its `LairObjectId` had been
pinned as a coffin. The owner retracted it the same day: *"sarchopagus at the
graveyard, I made a wrong claim earlier."*

Two shipped facts back the correction:

- The **Graveyard's `CreatedCreatureId` is 8 — the Vampire.** Only two rooms in
  the table create a creature at all; the other is the Prison, whose 9 is the
  Skeleton.
- The **Graveyard's object ids are 31–34**; the Vampire's `LairObjectId` is
  **26**. Two different objects.

The executable settles the vocabulary: `Object.cpp`'s union names `Lair` and
`TombStone` among its object kinds, and the words *sarcophagus* and *coffin*
appear nowhere in its strings. A bed is a `Lair` object whatever art it wears.
The vampire's coffin-shaped bed and the graveyard's sarcophagus are two objects
that look alike, and reading one as the other was a pun rather than a finding.

So reincarnation puts the creature on a **Graveyard** tile, which needs a
Graveyard under the keeper's control — which the DK2 wiki also gives as a third
permanent-death condition.

### 2.3 The vampire — routine found 2026-09-16 (Q111)

`CanBeResurrected` (bit 23) is set on **exactly the two Vampires and nobody else**,
so the owner's rule is data-driven rather than a name check, and the vampire having
no dead body stops being an oddity: there is no corpse because it is not finished.

**`FUN_0048FFC0` is the reincarnation, and the owner's sentence is its guard
list** (binary, 2026-09-16): level 1 is permanent, a Monk's kill is permanent,
and it comes back at the level-scaled maximum one level down. The routine, its
guards and how the Monk's blow reaches it are in `combat.md` §6.3 — it is reached
from the damage sink, so it is read there.

**DK2 never records a killer**, which is how the monk half works at all. This
document used to say that exit *"needs the killer's identity"* — right about the
problem, wrong about the shape of the answer. **Still not implemented here**, and
since ADR-0151 the destination is no longer the reason: a Graveyard tallies and
raises (Q165). What is left is that **nothing kills a vampire**, because there is
no combat (M7).

**OpenKeeper does not implement or even read `CanBeResurrected`.**

### 2.4 What the DK2 wiki adds, and where it disagrees

The wiki's Vampire page, in our own words — the four-sentence quotation that
stood here until 2026-09-16 broke `wiki-as-a-source.md`'s own rule against a
large block quote of CC BY-SA prose. It **confirms three of the owner's four
points** — loses a level, level 1 is
final, only a Monk makes it final regardless — and adds a **third permanent-death
condition the owner did not mention: no Graveyard.** So a vampire dies for good
if it is level 1, *or* a Monk killed it, *or* its keeper controls no Graveyard at
that moment. The third is the only one that is a property of the dungeon rather
than of the creature or its killer. Two of the three are now read from the binary
(§2.3); **the Graveyard condition is not in `FUN_0048FFC0`'s guards** and is
either elsewhere or wrong (Q329).

**On health the wiki is not followed.** Its *lead* says a resurrected vampire
returns at full health, but the lead is game-agnostic prose covering all four
titles and the DK2 section says nothing about health. §2.3 settles it from the
binary anyway: `FUN_0048FFC0` writes the level-scaled maximum.

**Two numbers the wiki gave that the shipped data confirms exactly**, both pinned
by tests:

- **Five corpses in a Graveyard make a Vampire.** The data never says "five": it
  says `DecomposeValue` 200 a corpse and `DecomposeValueNeededForVampire` 1000.
  Two independent sources on one integer, worth more than either alone — it
  confirms the wiki *and* confirms our reading of `DecomposeValue` as a per-corpse
  worth, which nothing else could check. (DK1 needs ten, on the adjacent line of
  the same infobox field; read carelessly that page supports either number.)
- **Two Black Knights sacrificed make a Vampire.** In the shipped temple table as
  `BlackKnight + BlackKnight → rewardType=1 value=8`. The first thing in the
  project ever to read `SacrificeEntry`, so it doubles as a layout check.

One more, unimplementable: **at level 10 a Vampire raises any corpse it sees into
a Skeleton**, competing with its own keeper's supply for making more Vampires.
`SkeletonArmyDurationSeconds` and `RaiseDead` territory; needs corpses and combat.

## 3. The bodies: eight creatures leave none

`CreatureFlags.GenerateDeadBody` (bit 15) is clear on eight of 46: **Imp,
Vampire, Skeleton, Horny, Stone Knight, Unique Skeleton, Unique Vampire, Jack.**

The Vampire is the coherent member twice over — a vampire is what bodies are
spent to make, *and* it leaves no corpse because it is not finished (§2.3).

**This is the third near-miss grouping in this record**, and the third time the
shortcut "these are the undead, one flag would do" is refuted by one creature:

| Set | Size | The member that breaks the pattern |
|---|---|---|
| never eats | 8 | **Unique Firefly** — never eats, *does* leave a body |
| no body | 8 | **Jack** — eats, leaves *no* body |
| never sleeps | 6 | **Dwarf** — never sleeps, leaves a body, and eats |

Seven shared between the first two, one each way. **There is no undead flag.**
Each need is its own column. All three sets are asserted by name in
`CreatureNeedsOnRealDataTests` because every one of them was guessed wrong before
it was read.

### 3.1 Two records carry unreachable data

`Horny` and `Jack` both have `DecomposeValue = 200` while generating **no body**,
so those 200s can never be read by anything. The other six bodyless creatures
are 0, as expected.

Both are records where a template value would plausibly survive unedited — Horny
is a summon rather than a roster creature (CONFIRMED by the owner) and Jack's
record runs backwards in several other places (240 health with a 250 chicken;
slapping calms it; being held annoys it).

**The flag governs.** A graveyard counting `DecomposeValue` instead of asking
`LeavesBody` would be fed by two corpses that never exist.

## 4. The collapse window rejects further damage — CONFIRMED (binary, Q110)

The cached `CreatureDyingStateDurationSeconds` value has one live reader, in the
common creature-health routine, **`FUN_004B2CE0`** (`combat.md` §1, where its "mode" argument is named). On the first blow that puts an eligible creature
down, that routine loads `60 * 4` into the creature's health/countdown field and
sets the `CreatureDying` flag. The two dying-state handlers are states `0xd8` and
`0xd9`; `0xd9` advances that countdown by calling the same health routine with
`-1` in its explicit dying-countdown mode.

The same routine has an early return for a creature already flagged
`CreatureDying` when that mode is absent, and the ordinary combat, spell and
environmental-damage callers all use the ordinary mode. Therefore a second hit or
a keeper spell does **not** finish a collapsed creature early: **the 60-second
rescue window is inviolable to further gameplay damage.**

The exceptional callers are bookkeeping, not extra attacks: the dying-state
countdown itself, another state handler that also decrements by one, a trigger
action that adjusts a spawned creature to a requested health value, and loading
paths. Rescue and healing remain the only way out before the countdown expires.

**The states** are 216, the fall, then 217; a healed creature leaves 217 for state 1
(`dying-states.md`, ADR-0206).

## 5. Implementation

`CreatureActivity.Dying` and `Dead`, `CreatureState.DyingTicks`,
`CreatureDeathRules` (`HasCollapsed`, `HasDyingState`, `LeavesBody`,
`CanBeResurrected`, `IsDying`, `IsCorpse`, `IsDown`, `DyingWindowTicks`,
`BodyWindowTicks`, `DyingWindowElapsed`, `BodyWindowElapsed`, `Collapse`,
`BecomeCorpse`, `Rescue`), and the collapse/die pair in `CreatureHealthSystem`
(ADR-0051, then ADR-0063). A collapsed creature keeps its owner, its tile, its
gold and its bed, and **does nothing at all** — no movement, no work, no sleep,
no hunger, no regeneration.

The bed is held for the whole window on purpose: the rescue carries the creature
*back to that bed*, so a lair tile freed on collapse would be a tile somebody
else claims while its owner lies on the floor waiting to be carried to it. It is
released at death.

**The imp tasks are the two DK2 has**, the last two entries of its Tier A job
table (`imp-task-priority.md` §4): `ImpTaskKind.CarryUnconscious` is DK2's
**kind 13** and `CarryCorpse` its **kind 12**, routed by
`CarryRules.NearestGraveyardTile` and reported as the `corpse_buried` event. The
absence of a task that carries a creature — not OBJECTS — was what actually
blocked this for five sittings.

| Stage | State |
|---|---|
| The corpse | **Built** (ADR-0063). A creature in `CreatureActivity.Dead`, lying for `DeadBodyDiesAfterSeconds` |
| Rescue | **Built** (ADR-0063). `CreatureDeathRules.Rescue` had been written, tested and callerless for five sittings so the gap would be visible; kind 13 is its caller |
| Prison for enemies | **Built** (ADR-0064, `docs/research/prison.md`): an imp drags an unconscious enemy to a cell, it withers at `PrisonModifyCreatureHealthPerSecond` (−3) a game second, and at nothing it rises as a **Skeleton for the jailer**. A prisoner arrives at **max / 6** health (Q167, ADR-0071). `MotionlessInPrisonSeconds` (8) is still parsed and unread |
| The Vampire | **Built (ADR-0151).** A Graveyard keeps the room dump's per-instance `DecomposeValue` dword; burial adds the corpse's value, and shipped variable 25 sets 1000 as the threshold for its `CreatedCreatureId`, Vampire. Five Goblins contribute 5 × 200, independently agreeing with the five-corpse account (Q165) |
| Chickens for the dying | **Not built.** A keeper keeps a *prisoner* alive by dropping chickens on it, so a prisoner's countdown always runs out here (ADR-0064) |

**The petals are health** (Q108), and there are **six** of them (Q171, CONFIRMED
by the owner with a screenshot). Petals empty as a creature is hurt and fill
again when it eats, so the flower is a health readout rather than the anger
meter.

### 5.1 The line: a dying creature is a creature, a corpse is not

A collapsed creature has health, an owner, a tile and a bed, and recovers by
being healed — everything that reads a creature keeps working on it, so it stays
in `WorldState.Creatures`. A corpse has none of those.

Modelling the corpse as a *separate entity* was rejected (ADR-0051), though DK2's is
one (`CDeadBody`): it stays the same creature in `CreatureActivity.Dead`, which every
system tests through `CreatureDeathRules.IsDown`.

### 5.2 Guard on health, not on state

The collapse is *booked* once a game second, which leaves a window in which a
creature has no health and is still `Idle`. Guarding on "is dying" let two
things happen in that window:

- **A creature at zero health near its bed** decided it was wounded enough to
  sleep (`CreatureSleepsWhenBelowPercentHealth` = 50), walked there, and healed —
  so it **never collapsed at all** whenever a bed was in reach.
- **An Imp at zero health on its own land** regenerated its 67 a second before
  the collapse was booked, climbed back to 500, and **could not be killed at
  home**.

So every system guards on `CreatureDeathRules.IsDown` — *no health, or already
dying* — not on the state. This matters beyond the current cadence: **combat will
subtract health per tick, not per second.**

Two corollaries:

- **Own-land regeneration is suppressed at zero health; lair healing is not.**
  The owner's answer gives exactly one route off the floor, so passive
  regeneration must not be a second one — and lair healing must still work at
  zero, because that *is* the rescue. The Imp's instant death removed the guard's
  original subject, but **King Reginald** regenerates **400** a second on his own
  land *and* does leave a body, so he collapses and would heal back up every
  second he lay there. He is the only creature in the shipped data with both
  properties, which is why
  `KingReginaldIsTheCreatureTheDownGuardNowProtectsAgainst` asserts that he is
  the only one rather than merely that he is one.
- **The order inside `CreatureHealthSystem` is load-bearing.** Healing applies
  *before* the collapse test, so a creature rescued at zero health is above zero
  by the time anything asks. Reversed, a rescue would collapse the creature it
  just saved. `ARescuedCreatureWakesInItsBedAndIsHealedRatherThanCollapsingAgain`
  is the test that goes red.

## Tests

`CreatureNeedsTests`, `CarryingTests` and `GraveyardTests` (`tests/DK2.Simulation.Tests/`);
`BothDyingTimersAreSixtySecondsAndTheyAreTwoDifferentClocks`,
`EightCreaturesLeaveNoBodyAndThatSetIsNotTheNeverHungryOne`,
`TheEightCreaturesThatLeaveNoBodyAreExactlyTheOnesWithNoLyingState`,
`ADecomposingBodyIsWorthTwoHundredAndTheUndeadAreWorthNothing`,
`OnlyTheTwoVampiresCanBeResurrectedAndTheirBedIsWhereTheyComeBack`,
`FiveCorpsesMakeAVampire_WhichIsTwoNumbersAgreeingFromTwoSources`,
`TwoBlackKnightsSacrificedMakeAVampire` and
`TheVampiresOwnNumbersMatchTheWikisDk2Column`
(`tests/DK2.Behavior.Tests/CreatureNeedsOnRealDataTests.cs`). Scenarios
`scenarios/creatures/a_goblin_collapses_and_dies.yaml`,
`an_imp_buries_a_corpse.yaml`, `five_corpses_make_a_vampire.yaml` and `an_imp_drags_an_enemy_to_prison.yaml`.

## Known exceptions

- Horny's and Jack's unreachable `DecomposeValue` of 200 (§3.1), read through the
  flag rather than the field.
- **Q107, CONFIRMED (owner, 2026-09-07) and built:** a creature whose bed is sold
  while it lies dying is **not** rescued — *"if its bed gets sold an imp can't
  get that creature to bed, if it is mid flight it gets dropped, and then its
  dying timer runs out and it dies"*. A rescue's destination is the creature's
  own bed, so no bed means no imp comes.

## Open questions

- **Q329**: the wiki's third permanent-death condition — no Graveyard — is not
  among `FUN_0048FFC0`'s guards (§2.3).
