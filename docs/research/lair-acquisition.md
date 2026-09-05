# How a creature gets a lair, and why ours does not

**Sources:** the owner's recordings of DK2 v1.70 (2026-09-05, analysed by a
second assistant) and `DKII.EXE` read the same day. Facts in our own words;
ADR-0046 governs the boundary.

**Read this before touching `CreatureSleepSystem`.** What this project does today
is contradicted on two separate counts, and both are recorded here rather than
half-fixed, because the replacement needs a candidate-selection rule that is
still partly UNKNOWN.

## 1. What was watched

**In a large lair.** A Salamander came in from the upper right, **crossed the
room**, and put its bed down near the far side. A second Salamander entered from
the same direction and chose a spot on the **opposite** side from the first. A
Warlock later put its chair down right beside the first Salamander's bed.

**In a 1×5 lair**, which is the controlled version of the same thing, four
creatures claimed tiles in this order:

| | creature | tile |
|---|---|---|
| 1 | Goblin | 1 |
| 2 | Warlock | 3 |
| 3 | Goblin | 5 |
| 4 | Salamander | 4 |

The Salamander was **seen to walk up to an occupied tile, reject it, and carry on
to a free one**. That was not a one-off: creatures were repeatedly watched
visiting taken lair tiles, turning them down and trying others.

**And the trigger is not availability.** A free lair tile existed while a bedless
Goblin and a bedless Bile Demon went off to the **Training Room** instead. The
creature only went looking for a lair later, when its mood turned **Unhappy** with
the displayed reason being that it had no lair.

## 2. What follows, and what it costs us

| Claim | Status |
|---|---|
| DK2 does not simply assign the lowest free lair tile | **CONFIRMED** — the 1×5 order was 1, 3, 5, 4 |
| Acquiring a lair is a **journey**, not a reservation | **CONFIRMED** — creatures cross the room to place a bed |
| Occupancy is tested **on arrival**, not when choosing | **CONFIRMED** — a candidate was rejected in front of the camera |
| A rejected candidate is followed by another attempt | **CONFIRMED** |
| Candidate choice contains randomness, or something that looks like it | **STRONGLY INFERRED** from the watched spread, and **corroborated by the binary** — see §4 |
| A creature remembers what it has already rejected within one search | **HYPOTHESIS** |
| **A happy bedless creature does not claim a free tile** | **CONFIRMED** — it trained instead |
| **`AngerNoLair` accumulating to unhappiness is what starts the search** | **CONFIRMED by observation**, mechanism UNREAD — see §5 |

**Contradicted, and it is ours:** `CreatureSleepSystem` claims a bed on the first
tick one is free, from wherever the creature happens to be standing, with no walk
and no mood test. Both halves are wrong. It was built from the owner's earlier
and entirely accurate description — *"when a creature enters a dungeon through a
portal it goes to a lair, finds a free tile and occupies it with its bed"* — of
which this project implemented the second half and skipped the going.

## 3. The link between creature and bed is two-way, and ours already matches

The creature record carries the **thing id of its lair**, and the lair object
carries the **thing id of its creature** back. A helper that answers *"is this my
own lair?"* checks all of it: the id is set, the thing still exists, it is of the
object kind, it has an owner creature, and that owner is this creature.

That is exactly the relation ADR-0054 moved this project to on 2026-09-04, when
`Lair.CreatureId` on the object replaced a tile index on the creature. Nothing to
change.

## 4. Choosing where to go: a room first, then a tile

**The room is chosen before the creature moves**, and it is chosen by distance.
The acquisition state walks the keeper's rooms, keeps only lairs, and measures
each one from the creature to the **room's centre**. It keeps two answers at
once: the nearest lair of any kind, and the nearest lair that also passes a test
over the objects standing in it. The second wins when it found anything; the
first is the fallback. What that extra test *is* has not been pinned down and
must not be guessed at — **Q151**.

**The tile is chosen on arrival, and starts underfoot.** A second routine takes
the creature's current position and tries, in order:

1. **the tile the creature is standing on**;
2. a short **fixed table of neighbouring offsets** around it;
3. failing both, a **chained walk** from a starting tile through the room, one
   tile handing on to the next, stopping when it finds a free one or arrives back
   where it began — in which case the creature gets nothing this time.

Each candidate is put through a single predicate that decides whether this
creature may put its bed there. So the **search is local first and exhaustive
second**, which is precisely the behaviour that was filmed: a creature that walks
into a lair looks where it is standing, then next door, and only wanders if both
fail.

**And then the answer is jittered.** The chosen tile's centre is offset by a
**random angle**, taken from the engine's seeded random source and turned into a
displacement through sine and cosine tables. That is where "appears random" comes
from, and it is *within* a tile rather than between tiles — so it explains where a
bed sits on its tile, not which tile it lands on.

The spread between the two Salamanders is therefore explained by step 1: they
entered from the same side but were standing in different places by the time the
search ran.

## 5. What starts the search is still unread

The state is entered when the creature has no lair thing and its keeper owns at
least one lair, and there is a further condition on the creature that has not been
identified. **The owner's observation is the authority here and it is
unambiguous** — a happy bedless creature went and trained instead, and the search
began when the mood turned Unhappy with "no lair" as the reason. What is not read
is whether the gate is the anger value, the mood transition, a periodic decision
roll, or a combination. **Q150.**

## 6. A variable that governs nothing

**`TimeBetweenReattemptingSleepSeconds` (id 22, value 4) is read by zero
instructions in the whole executable.** Its dispatch stub stores it into a
dedicated global and nothing ever loads that global back.

This is a real answer to "what uses it": **nothing does.** It is the third
balance variable this project has caught not meaning what its name implies, after
`MaxFreeRangeChickensPerPlayer` and `MaxGoldPileOutsideTreasury`, and the
cheapest of the three to check. Do not build a retry interval on it.

## 7. What this means for the rework

Not built, deliberately. The shape it has to take:

- a creature with no lair does **not** claim one until something makes it want to;
- wanting one puts it into a state that **picks a room by distance** and walks
  there;
- on arrival it tests **where it is standing**, then its immediate neighbours,
  then wanders the room;
- occupancy is decided **at the moment of the test**, so two creatures heading for
  the same room is normal and one of them losing is normal.

The parts that are read (§3, §4, §6) can be built now. The trigger (§5) and the
room-preference test (§4) cannot, and until they are, an implementation that
guesses them would be exactly the failure mode ADR-0018's history warns about:
right outcome, wrong rule.
