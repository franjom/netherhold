# Mechanic: the Hatchery — coops, eggs and chickens

Source(s):
- `DKII.EXE` v1.70, `Hatchery.cpp`'s translation unit, read 2026-09-04
- The reference install's `Objects.kwd` and `GlobalVariables.kwd`, via `DK2.Data`
- Owner observation, 2026-09-05 (Q133, Q135, and the free-range measurement)

Original version: DK2 v1.70

Facts in our own words; ADR-0046 governs the boundary. Companion to `eating.md`,
which is the creature side of the same food chain.

Confidence: **CONFIRMED** for the coop count, the spacing, the egg, the laying
interval and the flock cap; **UNKNOWN** for the one sub-question in §5.

---

## 1. A hatchery lays eggs, not chickens

The routine that produces food creates **object 47**, which `Objects.kwd` names
**Egg** — not object 9, the Chicken. Both decay unless they are in a room
(`DieOverTimeIfNotInRoom`), and the Chicken carries `TypeFood` while the Egg does
not. So the food chain has a step: **the hatchery lays an egg, and something
turns the egg into a chicken.**

**CONFIRMED by the owner** (2026-09-05), who watched one: *"it produces eggs that
hatch, that entire animation of eggs appearing and hatching lasts about 2
seconds, and after that a chicken appears and roams around the hatchery."* The
delay is **about two seconds**, timed from the animation — whether the simulation
holds an egg in a state for exactly that long, or the art covers a shorter
transition, is not settled.

The objects' `StartState`s corroborate the shape — 0 for the egg, 10 for the
chicken — so both have a state machine to do it in, and DK2's object carries a
`StateAge` to time it (`objects.md` §7). Q133, closed.

## 2. How many coops a hatchery has

The room places **Chicken Coops** — object 35, confirmed by the code branching
on the literal id 35 and by our parse giving 35 the name "Chicken Coop". The
count comes from the room's tile count:

    coops = min(tiles / 6, 36)

Integer division; the cap is 36. A 3x3 hatchery has **one** coop, a 6x6 has
**six**, and reaching the cap would need 216 tiles.

Coops are placed with a **minimum separation**: a candidate position is rejected
when the squared distance to an already-placed coop is below **3.0** in DK2's
fixed-point units, a spacing of about **1.73 tiles**. The check is against every
coop placed so far, in the room's own tile order.

Two details of the placement:

- a coop's position is the **tile centre** — tile index times 4096 plus 2048 in
  fixed point, which is exactly `CreatureState.CentreOf` and confirms the 4096
  denominator on a third independent path;
- placement walks the room's tiles, so it is deterministic given the room, not
  random.

## 3. Where an egg appears

Two cases, in this order:

1. **The room has coops.** Count the coops in the room's object list, pick
   **one at random**, and lay the egg at that coop's position.
2. **The room has no coops.** Walk the room's tiles counting those with no
   object on them, pick **one at random**, and lay the egg at that tile's centre.

The room also keeps a **running count of everything it has produced**, bumped on
each successful lay.

The random pick is behaviour, and it is the first place in this project where
DK2's own RNG position would matter observably (Q3): two hatcheries of the same
size lay in the same rhythm but not in the same places. Our default generator is
not DK2's; the original's is available as `Dk2Lcg` (`dk2-random.md`, ADR-0275).
What matters for the determinism gate is that the draw happens in a fixed order.

## 4. How often it lays

The interval is computed from the room's tile count, and the formula is exact:

    interval = (int)( ChickenGenerationTimePerHatchery / pow(tiles, 0.315477776) ) * V19

- `ChickenGenerationTimePerHatchery` is variable **18**, shipped as **16**, read
  from the global this project mapped to that id — two of the three readers of
  that global are hatchery functions, which is what makes the whole variable map
  believable (`balance-variables-in-the-binary.md`);
- the exponent is a **double built from two immediates**, exactly
  **0.315477776**;
- the truncation to integer happens **before** the multiply, so the multiplier
  scales an already-rounded number of whole units;
- **`V19` is variable 19, the engine's ticks per game second — 4 on every
  shipped level** (Q1/Q132, from the level header). So the game-second interval
  below becomes a tick count by multiplying by four.

**A bigger hatchery lays faster**, on a curve rather than linearly. With the
shipped 16:

| tiles | pow(tiles, e) | 16 / pow, truncated |
|---|---|---|
| 1 | 1.00 | 16 |
| 4 | 1.55 | 10 |
| 9 | 2.0000566 | **7** |
| 16 | 2.40 | 6 |
| 25 | 2.76 | 5 |
| 36 | 3.10 | 5 |
| 64 | 3.71 | 4 |
| 100 | 4.28 | 3 |

**The exponent was chosen so that nine tiles gives *almost* exactly 2**, and the
almost is the quirk. The value is not `log 2 / log 9` (0.3154648), so it was
typed rather than derived — and typed **just high enough to miss**. `pow(9, e)`
is **2.0000566**, a hair above two, so `16 / pow` is **7.99977** and a C cast
takes **7**, where a designer deciding *"the smallest hatchery should be half the
speed of a one-tile one"* would have got 8. That is one second on every 3x3
hatchery in the game, caused by the sixth decimal place, and 3x3 is the smallest
room the game allows and therefore the most common hatchery there is. Reproduced
rather than rounded away (Q134).

It is also why `HatcheryRules` uses a **fixed-point table rather than
`Math.Pow`**: the number decides when an egg appears, eggs are hashed, and
IEEE-754 `pow` is not guaranteed bit-identical across runtimes. A difference in
the eighth decimal place would hand one machine 8 and another 7 and diverge the
run. `HatcheryClockTests` regenerates the table from `Math.Pow` and compares, so
the table is provably the formula while the simulation never touches a float.

So a 3x3 hatchery lays every 7 game seconds and a 6x6 every 5.

`HatcheryRules.LayIntervalSeconds` returns **whole game seconds** and the caller
converts, because the truncation happens before the multiply.

## 5. Free-range chickens, and what caps a flock

Variable 86 is `MaxFreeRangeChickensPerPlayer`, shipped as **12**, and it is
real: before laying, the coop path checks a **per-player counter** against a
global limit and does nothing when the counter has reached it. The variable maps
to a **byte-wide global with exactly one reader in the whole binary**, which is
that check.

**It does not cap a hatchery's flock.** The owner settled that by looking: a
**5x5 hatchery holds more than twenty chickens** (2026-09-05), where a limit of
12 on the room's birds would have stopped it at twelve. So the counter holds
something narrower — loose ones, which is what *free range* means and what the
Chicken union's own `FreeRange` field distinguishes from a hatchery bird. The
counter's setter is a function of its own, called from four places outside
`Hatchery.cpp`, which fits a census kept by the chicken's own create/destroy
rather than by the room.

**What caps it is one chicken per room tile** (Q135, ANSWERED 2026-09-05). The
owner counted **exactly nine in a 3x3**, and the record had said so all along:
the Chicken carries `RoomCapacity` = **1**, a field documented as "how many of it
a room tile holds". `HatcheryRules.FlockCapacity` takes the number from the
definition rather than hardcoding nine.

The flock is **stable** — chickens vanish if nothing eats them. That fits the
record: the Chicken carries `DieOverTimeIfNotInRoom` and **not** `DieOverTime`,
so a bird is safe on the hatchery's tiles and rots once it wanders off them.
Production against strays rotting is an equilibrium, and presumably also how a
bird becomes free range in the first place.

The object union corroborates the name from the other side: a `Chicken` object's
per-kind state is **`FreeRange`, `PeckingTimer`, `StayThere`,
`ScatterGameTicks`** (`objects.md` §7).

## 6. Implementation

`HatcheryRules` + `HatcherySystem` (ADR-0066), once a game second, hatching
before laying so an egg is not aged on the second it is laid. Every number in it
is the original's:

- coop count from tile count, and the spacing rule as integer tile distances,
  which is exactly equivalent to the original's fixed-point comparison because a
  coop always stands at a tile centre (`CoopCount`, `CoopTiles`, `PlaceCoops`);
- coops as real objects, placed idempotently so the call is safe to repeat;
- the egg at a random coop, or on a random empty tile when the room has none
  (`TryLayEgg`);
- the laying clock, `LayIntervalSeconds` / `LayIntervalSecondsForTiles`, from the
  fixed-point table of §4, with `MaxTilesForInterval` = 256;
- the **hatch**: `Hatch` swaps an egg for a chicken on the same tile after
  `EggHatchSeconds` (2);
- the flock cap: `FlockCapacity`, `Flock`, `Birds`, `FlockIsFull`, and
  `TryLayEgg` refusing when the room is full — counting eggs as well as
  chickens, since an egg is a chicken two seconds from now;
- the free-range cap **exposed and deliberately not enforced** (`FreeRangeCap`):
  a 5x5 hatchery holds more than twenty chickens, so a limit of 12 is not the
  room's limit, and applying it would throttle a room DK2 does not throttle.

**Not built:** the room's produced-count, which nothing reads.

## Tests

`HatcheryTests` and `HatcheryClockTests` (`tests/DK2.Simulation.Tests/`);
`ObjectsOnRealDataTests` and `Dk2TerrainFixture` cite §4 for the shipped
constants. Scenario `scenarios/rooms/a_hatchery_lays_and_hatches.yaml`.

## Known exceptions

- The nine-tile truncation to 7 rather than 8 (§4), reproduced.
- `MaxFreeRangeChickensPerPlayer` parsed, exposed and not applied (§5) — the
  second of the three balance variables this project has caught not meaning what
  its name implies, after `MaxGoldPileOutsideTreasury`.

## Open questions

- Whether DK2 **refuses** to lay when the flock is full, or lays anyway and lets
  the surplus rot. The owner reports both continuous production and birds
  vanishing, so both are in play. Ours refuses — a 3x3 settling at *exactly* nine
  is a cap rather than the balance point of two rates, and refusing cannot run
  away. **UNKNOWN**, and it blocks nothing.
- Q134: why the exponent is that value. Does not affect fidelity.
