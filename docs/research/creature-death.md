# Dying: two timers, three states, and the half that cannot be built

**Status:** the first timer is implemented and CONFIRMED. The second is a real
number in the shipped data with nothing able to read it.

## The answer — CONFIRMED (owner, Q99, 2026-09-04)

> *"When a creature drops, its petals on the flower above its head are exhausted,
> it remains lying on the ground for a period of time, that gives imps an
> opportunity to return a creature to its bed in the lair for healing, or to
> prison if it is an enemy creature. Once that timer runs out a creature dies and
> its corpse remains there lying, that gives imps an opportunity to carry it to
> graveyard. If that timer runs out and no one carries the corpse it just
> disappears."*

So dying is a **pipeline**, not an event:

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
`CreatureDeathRules.BodyWindowTicks` exists and is deliberately uncalled: the
number is pinned so the second timer is missing *visibly*.

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
| **The corpse** | It is an OBJECT — it has a `DecomposeValue`, imps haul it like gold, a graveyard counts `DecomposeValueNeededForVampire` (1000) of them. OBJECTS is unparsed (M6) |
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
  home**. The one creature in the game that would have been immortal on friendly
  ground.

Both were found by a test written for the second. So every system guards on
`CreatureDeathRules.IsDown` — *no health, or already dying* — not on the state.
This matters beyond the current cadence: **combat will subtract health per tick,
not per second.**

Two corollaries of the same reasoning:

- **Own-land regeneration is suppressed at zero health; lair healing is not.**
  The owner's answer gives exactly one route off the floor, so passive
  regeneration must not be a second one — and lair healing must still work at
  zero, because that *is* the rescue.
- **The order inside `CreatureHealthSystem` is load-bearing.** Healing applies
  *before* the collapse test, so a creature rescued at zero health is above zero
  by the time anything asks. Reversed, a rescue would collapse the creature it
  just saved. `ARescuedCreatureWakesInItsBedAndIsHealedRatherThanCollapsingAgain`
  is the test that goes red.

## The bodies: eight creatures leave none

`CreatureFlags.GenerateDeadBody` (bit 15) is clear on eight of 46: **Imp,
Vampire, Skeleton, Horny, Stone Knight, Unique Skeleton, Unique Vampire, Jack.**

The Vampire is the coherent member — a vampire is what bodies are spent to make.

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
| **Q107** | Can a creature whose bed is sold while it lies dying be rescued? We say no — a collapsed creature is not allowed to claim a replacement, because claiming is a walk and a placement in the owner's own description. Costs nothing while carrying does not exist |
| **Q108** | Is the "flower petals" indicator the anger meter, or a separate health/consciousness one? Bears on Q106 |
| **Q109** | Does an *enemy* creature collapse the same way, or go straight to a capturable state? The answer mentions prison as the imp's alternative destination, which implies the same collapse either way |
| **Q110** | Does anything shorten the dying window — a second hit while collapsed, or a keeper's spell? |
