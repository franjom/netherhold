# The Hatchery: coops, eggs, and one unresolved unit

**Source:** `DKII.EXE`, `Hatchery.cpp`'s translation unit, read on 2026-09-04,
plus `Objects.kwd`. Facts in our own words; ADR-0046 governs the boundary.

**Status: researched, not implemented.** Everything below is known except the
unit of the laying interval, and that one gap is load-bearing — see §4. Building
it with a guessed unit would be inventing DK2 behaviour, which the brief
forbids.

## Why this was worth doing now

Hunger has been half-built for many sittings. The clock runs, `ChickensNeeded`
fills from the creature record's `HungerFill`, `CreatureHealthRules.Eat` is
written and tested, and **nothing calls it**, because there was no food. The
reason recorded for that gap was "chickens are objects and OBJECTS is unparsed",
which stopped being true when OBJECTS was parsed, and stopped being true a
second time when objects got somewhere to live (ADR-0054). What is left is the
room.

## 1. A hatchery lays eggs, not chickens

The routine that produces food creates **object 47**, which `Objects.kwd` names
**Egg** — not object 9, the Chicken. Both decay unless they are in a room
(`DieOverTimeIfNotInRoom`), and the Chicken carries `TypeFood` while the Egg does
not.

So the food chain has a step this project had not accounted for: **the hatchery
lays an egg, and something turns the egg into a chicken.** The egg's own state
machine is the obvious candidate — an object carries a `StartState` and a
`StateAge` (objects.md §7) and the Egg's `StartState` would say where it begins.
What performs the transition has not been found. **Q133.**

## 2. How many coops a hatchery has

The room places **Chicken Coops** — object 35, confirmed by the code branching
on the literal id 35 and by our parse giving 35 the name "Chicken Coop". The
count is computed from the room's tile count:

    coops = min(tiles / 6, 36)

Integer division, and the cap is 36. So a 3x3 hatchery has **one** coop, a 6x6
has **six**, and reaching the cap would need 216 tiles.

Coops are placed with a **minimum separation**: a candidate position is rejected
when the squared distance to an already-placed coop is below **3.0** in DK2's
fixed-point units, which is a spacing of about **1.73 tiles**. The check is
against every coop placed so far, in the room's own tile order.

Two details of the placement worth keeping, because they corroborate machinery
we already have:

- a coop's position is the **tile centre** — tile index times 4096 plus 2048 in
  fixed point, which is exactly `CreatureState.CentreOf` and confirms the 4096
  denominator on a third independent path;
- placement is by walking the room's tiles, so it is deterministic given the
  room, not random.

## 3. Where an egg appears

Two cases, and the code does them in this order:

1. **The room has coops.** Count the coops in the room's object list, pick
   **one at random**, and lay the egg at that coop's position.
2. **The room has no coops.** Walk the room's tiles counting those with no
   object on them, pick **one at random**, and lay the egg at that tile's
   centre.

The room also keeps a **running count of everything it has produced**, bumped on
each successful lay.

**The random pick is behaviour, and it is the first place in this project where
DK2's own RNG position will matter observably** (Q3): two hatcheries of the same
size lay in the same rhythm but not in the same places. Our `SimRandom` is not
DK2's generator and is not trying to be; what matters for the determinism gate
is that the draw happens in a fixed order.

## 4. How often it lays, and the unit that is missing

The interval is computed from the room's tile count, and the formula is exact:

    interval = (int)( ChickenGenerationTimePerHatchery / pow(tiles, 0.315477776) ) * V19

- `ChickenGenerationTimePerHatchery` is variable **18**, shipped as **16**, and
  the code reads it from the global this project mapped to that id — two of the
  three readers of that global are hatchery functions, which is what makes the
  whole variable map believable (see `balance-variables-in-the-binary.md`);
- the exponent is a **double built from two immediates**, exactly
  **0.315477776**;
- the truncation to integer happens **before** the multiply, so the multiplier
  scales an already-rounded number of whole units;
- **`V19` is variable 19, and nothing in the game's data supplies it.**

**So a bigger hatchery lays faster**, on a curve rather than linearly. With the
shipped 16:

| tiles | pow(tiles, e) | 16 / pow, truncated |
|---|---|---|
| 1 | 1.00 | 16 |
| 4 | 1.55 | 10 |
| 9 | 2.00 | 8 |
| 16 | 2.40 | 6 |
| 25 | 2.76 | 5 |
| 36 | 3.10 | 5 |
| 64 | 3.71 | 4 |
| 100 | 4.28 | 3 |

**The exponent is chosen so that nine tiles gives exactly 2.** `pow(9, e)` is
2.0001, and a 3x3 room is the smallest a room can be — so the designers appear
to have picked the curve by deciding that the smallest hatchery should be half
the speed of a one-tile one. That reading is suggestive and no more: the exact
value is not `log 2 / log 9` (which is 0.31546), so it was typed rather than
derived, and **why that number is UNKNOWN** (Q134). It does not matter for
fidelity — the constant is what it is — but it matters for confidence that we
have read it correctly, and the 9-to-2 landing is the check that we have.

**What is missing is only the unit.** If `V19` is the engine's ticks-per-second
then a 3x3 hatchery lays every 8 seconds and a 6x6 every 5, which are plausible
game numbers. If it is something else they are not. `V19` is read from **223
places** in the binary as both a multiplier and a divisor, is written by exactly
one instruction, and appears in no shipped file at all — the full account is in
`balance-variables-in-the-binary.md`, and it is **Q132**.

## 5. Free-range chickens

Variable 86 is `MaxFreeRangeChickensPerPlayer`, shipped as **12**, and it is
real: before laying, the coop path checks a **per-player counter** against a
global limit and does nothing when the counter has reached it. There is a
setter for that counter as its own function, so it is maintained rather than
recomputed.

The object union corroborates the name from the other side: a `Chicken` object's
per-kind state is **`FreeRange`, `PeckingTimer`, `StayThere`,
`ScatterGameTicks`** (objects.md §7). So a chicken is either free-range or not,
it wanders, it pecks on a timer, and it scatters when something disturbs it —
four fields that describe exactly the chickens one remembers milling about a
hatchery.

## 6. What this gives us, and what it does not

**Ready to build**, with no open questions:

- coop count from tile count, and the spacing rule;
- the egg goes at a random coop, or on a random empty tile when there are none;
- the free-range cap per player;
- a room's produced-count.

**Blocked on Q132** (the unit): the laying interval, and therefore the room
actually producing anything on a clock.

**Blocked on Q133** (egg to chicken): what a creature eats. It eats a *chicken*
— `HpFromChicken` is per creature, 375 for a Goblin — and the hatchery lays an
*egg*, so the missing transition sits between them.

**Not researched at all:** how a hungry creature chooses a hatchery and walks to
it, what happens when two creatures want the same chicken, and whether an imp
ever carries food. Those are creature-side and belong with the job system.
