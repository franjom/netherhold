# Dying: two timers, three states, and the half that cannot be built

**Status:** **both timers are implemented** (2026-09-05, ADR-0063) and the
pipeline runs end to end: collapse, rescue-or-die, corpse, bury-or-rot. The
second timer spent five sittings as *"a real number in the shipped data with
nothing able to read it"*, on an explanation — *"a corpse is an object"* — that
turned out to be false.

## The answer — CONFIRMED (owner, Q99, 2026-09-04)

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
before this answer. They are two clocks on two different things, which is why
`CreatureDeathRules.BodyWindowTicks` was defined years before it could be called:
the number was pinned so that the second timer would be missing *visibly*. **It
has a caller now** (ADR-0063), and the two clocks share one field —
`CreatureState.DyingTicks` — because they never run at once, reset at the seam so
a corpse gets a fresh sixty seconds.

**What was actually blocking it** was not OBJECTS but the absence of an imp task
that carries a creature. DK2 has two: **kind 13** for the unconscious and **kind
12** for corpses, the last two entries of its Tier A job table
(`imp-task-priority.md` §4). Both are built.

## Three deaths, and one flag that picks between two of them

`GenerateDeadBody` (bit 15) decides whether running out of health puts a
creature on the floor at all:

| | Who | What happens |
|---|---|---|
| **Collapse** | the 38 creatures with `GenerateDeadBody` | 60 seconds rescuable, then dead, then a corpse |
| **Instant** | the 8 without it | gone on the spot, nothing left |
| **Reincarnate** | the 2 with `CanBeResurrected` | back at the Graveyard's sarcophagus, asleep, one level poorer |

### A corpse is not an object — CORRECTED

This document, ADR-0051 and several code comments gave **"a corpse is an OBJECT,
and OBJECTS is unparsed (M6)"** as the reason nothing lies on the floor after a
death. The OBJECTS record was parsed on 2026-09-04: **145 objects, and not one of
them is a corpse.** The nearest are decoration — Skeleton (48), Skull (49), Bone
(50) — and none of them carries a type flag.

Everything else already pointed the same way and nobody had put it together:

- both timers are **creature** variables (`CreatureDyingStateDurationSeconds`,
  `DeadBodyDiesAfterSeconds`);
- `DecomposeValue` is a **creature** field;
- the binary's per-creature debug dump carries **`HasBeenKnockedOut`** and
  **`DieInstantly`**, and `Creature.cpp` owns the dying state.

**A corpse is a creature in a state.** So the graveyard is buildable without any
new record: it wants an imp task that carries a *creature* whose dying window has
run out, and a room that consumes it for its `DecomposeValue`. Considerably
smaller than "parse OBJECTS first" implied — and it was hiding behind a claim
nobody had checked. See `docs/research/objects.md` §4.

### Where a vampire comes back — CORRECTED

The owner's first wording was *"they reincarnate at their sarcophagus"*, and this
project read "their sarcophagus" as the vampire's own bed, because its
`LairObjectId` had been pinned as a coffin. That was wrong, and the owner
retracted it themselves on the same day: *"sarchopagus at the graveyard, I made a
wrong claim earlier."*

Two shipped facts back the correction, neither of which was looked at when the
wrong one was written down:

- The **Graveyard's `CreatedCreatureId` is 8 — the Vampire.** Only two rooms in
  the table create a creature at all; the other is the Prison, whose 9 is the
  Skeleton.
- The **Graveyard's object ids are 31-34**; the Vampire's `LairObjectId` is
  **26**. Two different objects.

The executable settles the vocabulary: `Object.cpp`'s union names `Lair` and
`TombStone` among its object kinds, and the words *sarcophagus* and *coffin*
appear nowhere in its strings (binary, ADR-0046 - facts only). A bed is a `Lair`
object whatever art it wears. The vampire's coffin-shaped bed and the graveyard's
sarcophagus are two objects that look alike, and reading one as the other was a
pun rather than a finding.

Consequence for the day this is built: reincarnation puts the creature on a
**Graveyard** tile, so it needs a Graveyard under the keeper's control - which
the DK2 wiki also gives as a third permanent-death condition, and which is now
corroborated rather than merely repeated. Q113 closed.

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

…where `isLeaveDeadBody` reads `GENERATE_DEAD_BODY` and nothing else. So **one
flag decides both halves**, which is coherent in a way worth saying out loud:
**the dying state *is* a body on the floor** — a live one. A creature that
leaves nothing behind had nothing to lie there in the first place.

That also retires the puzzle from the section below about why the bodyless set
is such an odd mixture. It is not a mixture at all: a summon, three undead, a
construct, a worker and Jack are exactly the things that would puff out rather
than fall over.

### The vampire, and why implementing it would be a mistake

`CanBeResurrected` (bit 23) is set on **exactly the two Vampires and nobody
else**, so the owner's rule is data-driven rather than a name check. Two things
fall neatly into place:

- **The sarcophagus is the vampire's lair bed.** Its `LairObjectId` is a coffin,
  which `TheGoblinsBedIsObjectTwentyTwoAndTheVampiresIsACoffin` already pinned
  for an unrelated reason. So reincarnation lands a creature in its own bed and
  heals it there — which is `CreatureDeathRules.Rescue` with no imp involved.
- **The vampire having no dead body stops being an oddity.** There is no corpse
  because the creature is not finished.

**And it is deliberately not implemented.** The cost of reincarnation is a level,
and levels do not exist. A vampire that reincarnated without paying would come
back for ever — the immortal Imp again, in a cape. Both exits are also out of
reach: level 1 needs levels, and *"a monk killed it"* needs the killer's
identity, which needs combat (M7).

**So instant death is not a placeholder for vampires — it is correct for the
world that currently exists.** With no levels every creature is level 1, and a
level-1 vampire is permanently dead by the owner's own rule. The flag is read
and tested and nothing branches on it; it will be needed the day levels arrive
and not before (Q111).

**OpenKeeper does not implement or even read `CanBeResurrected`.**

### What the DK2 wiki adds, and where it disagrees

Read through the MediaWiki API, **Dungeon Keeper 2 section only** — the page
covers four games and tags each claim, and they differ. See
`wiki-as-a-source.md`, including the licence flag on mirroring any of it.

> *"Although not completely immortal, Vampires can only be eternally 'laid to
> rest' by Monks (hence their unbridled hatred of the shaven-pated holy ones).
> Each time a Vampire is slain, it loses an experience level before
> re-incarnating in the Graveyard. If you are not in control of a Graveyard when
> a Vampire is slain, or if the Vampire is level 1, then it is lost for good."*

That **confirms three of the owner's four points** — loses a level, level 1 is
final, only a Monk makes it final regardless — and adds two things:

**A third permanent-death condition, which the owner did not mention: no
Graveyard.** So a vampire dies for good if it is level 1, *or* a Monk killed it,
*or* its keeper controls no Graveyard at that moment. Three conditions, and the
third is the only one that is a property of the dungeon rather than of the
creature or its killer.

**And a conflict worth resolving before any of this is built (Q113): the wiki
says it reincarnates *in the Graveyard*; the owner said *at their
sarcophagus*.** These are not obviously the same place — a lair bed and a
Graveyard are different rooms — and the vampire's `LairObjectId` really is a
coffin, so both readings have data behind them. It matters because it decides
what a reincarnation needs to find: a free lair tile, or a graveyard tile.

**A third source disagrees with both on health.** The page's *lead paragraph*
says a resurrected vampire returns "at full health" — but the lead is
game-agnostic prose covering all four titles, so it is not a DK2 claim, and the
DK2 section says nothing about health. The owner's account is that it *sleeps to
recover*. Untagged lead prose is the weakest of the three and is not being
followed.

**Two numbers the wiki gave that the shipped data confirms exactly**, both now
pinned by tests:

- **Five corpses in a Graveyard make a Vampire.** The data never says "five": it
  says `DecomposeValue` 200 a corpse and `DecomposeValueNeededForVampire` 1000.
  1000 ÷ 200 = 5. Two independent sources on one integer, and it is worth more
  than either alone — it confirms the wiki *and* confirms that our reading of
  `DecomposeValue` as a per-corpse worth is right, which nothing else could
  check. (DK1 needs ten, which is on the adjacent line of the same infobox
  field. Read carelessly that page supports either number.)
- **Two Black Knights sacrificed make a Vampire.** Present in the shipped temple
  table as `BlackKnight + BlackKnight → rewardType=1 value=8`, and creature 8 is
  the Vampire. This is the first thing in the project ever to read
  `SacrificeEntry`, so it doubles as a layout check on that record.

One more, unimplementable and worth knowing: **at level 10 a Vampire raises any
corpse it sees into a Skeleton**, which competes with its keeper's own supply of
corpses for making more Vampires. That is `SkeletonArmyDurationSeconds` and
`RaiseDead` territory, and needs levels, corpses and combat first.

So the vampire is now the best-sourced unimplemented mechanic in the project:
owner testimony, a wiki section, and two arithmetic confirmations from the
install — against zero help from OpenKeeper.

## What is implemented

`CreatureActivity.Dying`, `CreatureState.DyingTicks`, `CreatureDeathRules`, and
the collapse/die pair in `CreatureHealthSystem`. A collapsed creature keeps its
owner, its tile, its gold and its bed, and **does nothing at all** — no
movement, no work, no sleep, no hunger, no regeneration.

The bed is held for the whole window on purpose: the rescue carries the creature
*back to that bed*, so a lair tile freed on collapse would be a tile somebody
else claims while its owner lies on the floor waiting to be carried to it. It is
released at death.

## What is NOT implemented, and why

| Missing | Why |
|---|---|
| **The corpse** | **Not an object** — see the correction above; none of the 145 is a corpse. It is a creature in a state, with a `DecomposeValue` a graveyard counts 1000 of. What is missing is an imp that can carry one |
| **Rescue** | Nothing can be carried. `CreatureDeathRules.Rescue` is written and tested and has no caller |
| **Prison for enemies** | No prison, no capture. `MotionlessInPrisonSeconds` (8) and `PrisonModifyCreatureHealthPerSecond` (−3) are parsed and unread |
| **The Vampire** | Downstream of the corpse. Nothing accumulates, so no Vampire can be made |
| **The petals** | Whether the flower above a creature's head is the anger meter or a separate indicator is UNKNOWN |

**The cost, stated plainly:** a creature that dies vanishes instead of leaving a
corpse for 60 more seconds. The death event publishes `leaves_body=` so the gap
is in the log rather than only here.

## The line: a dying creature is a creature, a corpse is not

A collapsed creature has health, an owner, a tile and a bed, and recovers by
being healed — everything that reads a creature keeps working on it, so it stays
in `WorldState.Creatures`. A corpse has none of those.

Modelling the corpse as a creature was considered and rejected (ADR-0051): it
would put a thing with no needs into the creature list, where every present *and
future* system would have to remember to skip it.

## The bug this found: guard on health, not on state

The collapse is *booked* once a game second, which leaves a window in which a
creature has no health and is still `Idle`. Guarding on "is dying" let two
things happen in that window:

- **A creature at zero health near its bed** decided it was wounded enough to
  sleep (`CreatureSleepsWhenBelowPercentHealth` = 50), walked there, and healed —
  so it **never collapsed at all** whenever a bed was in reach.
- **An Imp at zero health on its own land** regenerated its 67 a second before
  the collapse was booked, climbed back to 500, and **could not be killed at
  home**.

Both were found by a test written for the second. So every system guards on
`CreatureDeathRules.IsDown` — *no health, or already dying* — not on the state.
This matters beyond the current cadence: **combat will subtract health per tick,
not per second.**

Two corollaries of the same reasoning:

- **Own-land regeneration is suppressed at zero health; lair healing is not.**
  The owner's answer gives exactly one route off the floor, so passive
  regeneration must not be a second one — and lair healing must still work at
  zero, because that *is* the rescue.

  > The Imp's instant death then *removed* the guard's original subject, which
  > would have made it look pointless. It is not: **King Reginald** regenerates
  > **400** a second on his own land and does leave a body, so he collapses and
  > would heal back up every second he lay there. He is the only creature in
  > the shipped data with both properties, which is why
  > `KingReginaldIsTheCreatureTheDownGuardNowProtectsAgainst` asserts that he
  > is the only one rather than merely that he is one.
- **The order inside `CreatureHealthSystem` is load-bearing.** Healing applies
  *before* the collapse test, so a creature rescued at zero health is above zero
  by the time anything asks. Reversed, a rescue would collapse the creature it
  just saved. `ARescuedCreatureWakesInItsBedAndIsHealedRatherThanCollapsingAgain`
  is the test that goes red.

## The bodies: eight creatures leave none

`CreatureFlags.GenerateDeadBody` (bit 15) is clear on eight of 46: **Imp,
Vampire, Skeleton, Horny, Stone Knight, Unique Skeleton, Unique Vampire, Jack.**

The Vampire is the coherent member twice over — a vampire is what bodies are
spent to make, *and* it leaves no corpse because it is not finished (above).

**This is the third near-miss grouping in this record**, and the third time the
shortcut "these are the undead, one flag would do" is refuted by one creature:

| Set | Size | The member that breaks the pattern |
|---|---|---|
| never eats | 8 | **Unique Firefly** — never eats, *does* leave a body |
| no body | 8 | **Jack** — eats, leaves *no* body |
| never sleeps | 6 | **Dwarf** — never sleeps, leaves a body, and eats |

Seven shared between the first two, one each way. **There is no undead flag.**
Each need is its own column. All three sets are asserted by name in
`CreatureNeedsOnRealDataTests` because every one of them was guessed wrong
before it was read.

### And two records carry unreachable data

`Horny` and `Jack` both have `DecomposeValue = 200` while generating **no body**,
so those 200s can never be read by anything. The other six bodyless creatures
are 0, as expected.

Both are records where a template value would plausibly survive unedited — Horny
is a summon rather than a roster creature (CONFIRMED by the owner) and Jack's
record runs backwards in several other places (240 health with a 250 chicken;
slapping calms it; being held annoys it).

**The flag governs.** A graveyard counting `DecomposeValue` instead of asking
`LeavesBody` would be fed by two corpses that never exist.

## Open

| | |
|---|---|
| **Q107** | Can a creature whose bed is sold while it lies dying be rescued? We say no — a collapsed creature is not allowed to claim a replacement, because claiming is a walk and a placement in the owner's own description. **This decides a real outcome now** (ADR-0063): a rescue's destination is the creature's own bed, so no bed means no imp comes |
| **Q108** | Is the "flower petals" indicator the anger meter, or a separate health/consciousness one? Bears on Q106 |
| **Q109** | Does an *enemy* creature collapse the same way, or go straight to a capturable state? The answer mentions prison as the imp's alternative destination, which implies the same collapse either way |
| **Q110** | Does anything shorten the dying window — a second hit while collapsed, or a keeper's spell? |
