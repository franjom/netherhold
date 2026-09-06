# The Hatchery: coops, eggs, and one unresolved unit

**Source:** `DKII.EXE`, `Hatchery.cpp`'s translation unit, read on 2026-09-04,
plus `Objects.kwd`. Facts in our own words; ADR-0046 governs the boundary.

**Status: researched, and the determined parts are built.** `HatcheryRules`
implements the coop count, the spacing, the coops as objects, the egg placement
and the hatch, with 21 tests. Q133 and the free-range question are **answered by
the owner** (2026-09-05); Q135 replaces the latter. What is *not* built is the clock: the laying interval
is multiplied by a balance variable no shipped file supplies, and a guessed unit
would be inventing DK2 behaviour, which the brief forbids. See §4 and §6.

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
lays an egg, and something turns the egg into a chicken.**

**CONFIRMED by the owner** (2026-09-05), who watched one: *"it produces eggs that
hatch, that entire animation of eggs appearing and hatching lasts about 2
seconds, and after that a chicken appears and roams around the hatchery."* So the
step is real and the delay is **about two seconds** — timed from the animation,
which is the caveat: whether the simulation holds an egg in a state for exactly
that long, or the art covers a shorter transition, is not settled and does not
matter until something ticks it.

The objects' `StartState`s corroborate the shape — 0 for the egg, 10 for the
chicken — so both have a state machine to do it in, and DK2's object carries a
`StateAge` to time it (objects.md §7). `HatcheryRules.Hatch` performs the
replacement; ours has no object state machine yet, so it swaps one object for
another rather than advancing a state. **Q133, closed.**

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
- **`V19` is variable 19, the engine's ticks per game second — 4 on every
  shipped level** (Q1/Q132, answered 2026-09-05 from the level header; see
  `balance-variables-in-the-binary.md`). So the game-second interval the table
  below gives becomes a tick count by multiplying by four.

**So a bigger hatchery lays faster**, on a curve rather than linearly. With the
shipped 16:

| tiles | pow(tiles, e) | 16 / pow, truncated |
|---|---|---|
| 1 | 1.00 | 16 |
| 4 | 1.55 | 10 |
| 9 | 2.00006 | **7** |
| 16 | 2.40 | 6 |
| 25 | 2.76 | 5 |
| 36 | 3.10 | 5 |
| 64 | 3.71 | 4 |
| 100 | 4.28 | 3 |

**CORRECTED 2026-09-06: the nine-tile row said 8 and is 7.** The header of that
table says *"truncated"* and the row had been rounded. `pow(9, e)` is
**2.0000566**, a hair *above* two, so `16 / 2.0000566` is **7.99977** and a C
cast takes **7**. The error mattered more than a row usually would, because 3x3
is the smallest room the game allows and therefore the most common hatchery
there is.

**The exponent was chosen so that nine tiles gives <i>almost</i> exactly 2**, and
the almost is the quirk. The value is not `log 2 / log 9` (0.3154648), so it was
typed rather than derived — and it was typed **just high enough to miss**. A
designer deciding *"the smallest hatchery should be half the speed of a one-tile
one"* would have got 8; the shipped constant gives 7. **That is a difference of
one second on every 3x3 hatchery in the game, caused by the sixth decimal
place**, and it is reproduced rather than rounded away (Q134).

It is also why `HatcheryRules` uses a **fixed-point table rather than
`Math.Pow`**: the number decides when an egg appears, eggs are hashed, and
IEEE-754 `pow` is not guaranteed bit-identical across runtimes. A difference in
the eighth decimal place would hand one machine 8 and another 7 and diverge the
run. `HatcheryClockTests` regenerates the table from `Math.Pow` and compares, so
the table is provably the formula while the simulation never touches a float.

**The unit was the last thing missing and it is answered.** `V19` is the
engine's ticks per game second, supplied by the **level header** rather than by
any variables file, and all 66 shipped levels carry **4** (Q1/Q132, 2026-09-05).
So a 3x3 hatchery lays every 7 game seconds and a 6x6 every 5, which are
plausible game numbers.

**Built on 2026-09-06** (ADR-0066): `HatcheryRules.LayIntervalSeconds` and
`HatcherySystem`. The interval is returned in **whole game seconds** and the
caller converts, because the truncation happens before the multiply and because
`SimulationTiming.TicksPerSecond` is still a placeholder (Q1) — returning ticks
would bake the placeholder into the arithmetic.

## 5. Free-range chickens

Variable 86 is `MaxFreeRangeChickensPerPlayer`, shipped as **12**, and it is
real: before laying, the coop path checks a **per-player counter** against a
global limit and does nothing when the counter has reached it. The variable maps
to a **byte-wide global with exactly one reader in the whole binary**, which is
that check — so there is no ambiguity about what the number is for.

**But it does not cap a hatchery's flock**, and the owner settled that by
looking: a **5x5 hatchery holds more than twenty chickens** (2026-09-05), where
a limit of 12 on the room's birds would have stopped it at twelve. So the counter
holds something narrower than "chickens this keeper owns" — loose ones, which is
what *free range* means and what the Chicken union's own `FreeRange` field
distinguishes from a hatchery bird. The counter's setter is a function of its
own, called from four places outside `Hatchery.cpp`, which fits a census kept by
the chicken's own create/destroy rather than by the room.

**So what does cap it? ANSWERED the same day — one chicken per room tile.** The
owner counted **exactly nine in a 3x3**, which is the measurement that settles
it, and the record had said so all along: the Chicken carries `RoomCapacity` =
**1**, a field documented as "how many of it a room tile holds" and read by
nothing until there was a flock to limit. `HatcheryRules.FlockCapacity` takes the
number from the definition rather than hardcoding nine.

**One sub-question survives and blocks nothing:** whether DK2 *refuses* to lay
when full, or lays anyway and lets the surplus rot. The owner reports both
continuous production and birds vanishing, so both are in play. Ours refuses —
a 3x3 settling at *exactly* nine is a cap rather than the balance point of two
rates, and refusing cannot run away.

The owner also reports the flock is **stable** — chickens vanish if nothing eats
them. That fits the record exactly: the Chicken carries
`DieOverTimeIfNotInRoom` and **not** `DieOverTime`, so a bird is safe on the
hatchery's tiles and rots once it wanders off them. Production against strays
rotting is an equilibrium, and it is also, presumably, how a bird becomes free
range in the first place.

The object union corroborates the name from the other side: a `Chicken` object's
per-kind state is **`FreeRange`, `PeckingTimer`, `StayThere`,
`ScatterGameTicks`** (objects.md §7). So a chicken is either free-range or not,
it wanders, it pecks on a timer, and it scatters when something disturbs it —
four fields that describe exactly the chickens one remembers milling about a
hatchery.

## 6. What is built, and what it waits on

**Built** — `HatcheryRules`, and every number in it is the original's:

- coop count from tile count, and the spacing rule (as integer tile distances,
  which is exactly equivalent to the original's fixed-point comparison because a
  coop always stands at a tile centre);
- coops as real objects, placed idempotently so the call is safe to repeat;
- the egg at a random coop, or on a random empty tile when the room has none;
- the **hatch**: `Hatch` swaps an egg for a chicken on the same tile, with
  `EggHatchSeconds` (2) recorded beside it;
- the free-range cap **exposed and deliberately not enforced** — and since
  2026-09-05 that is a finding rather than a hedge: a 5x5 hatchery holds more
  than twenty chickens, so a limit of 12 is not the room's limit, and applying it
  would throttle a room DK2 does not throttle.

**Not built, and named:** a room's produced-count, which has nothing to read it
until laying happens on a clock; and the ageing that would carry an egg to
`EggHatchSeconds`, which arrives with the same clock.

**No longer blocked** — Q132 answered 2026-09-05, built 2026-09-06 (ADR-0066).
What follows is the note as it stood: the laying interval, and therefore the room
producing anything on its own.

**Q135 is closed:** one chicken per room tile, measured. `FlockCapacity`,
`Flock`, `Birds` and `FlockIsFull` implement it, and `TryLayEgg` refuses when the
room is full — counting eggs as well as chickens, since an egg is a chicken two
seconds from now.

**Not researched at all:** how a hungry creature chooses a hatchery and walks to
it, what happens when two creatures want the same chicken, and whether an imp
ever carries food. Those are creature-side and belong with the job system.
