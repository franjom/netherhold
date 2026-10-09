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

The placer is `0x004DE220` (`Hatchery_PlaceCoops`), the Hatchery arm (type 4) of
`Room_Rebuild`'s switch (`0x004EE00F`). Read in instructions 2026-10-09. It places
**Chicken Coops**, object 35 (`0x23`), with this rule:

    count = min(Size / 6, 36)            Size = room +0xA7, every tile, rim included
    placed = []
    repeat count times:
        walk the slab chain from FirstSlab (+0x2D/+0x2E), next tile at cell +0x04
        take the first tile that
          - Room_IsFloorSlot (0x00454110) accepts: interior only (cell +0x0C == 0xFF);
            the Hatchery's table entry (type 4 -> byte 9 -> 0x004541E2) accepts every interior cell,
          - has no object 35 on it already (Cell_FindObjectOfType 0x004537E0),
          - and is at squared distance >= 3.0 tiles from every coop in `placed`
        none found -> stop

- **Candidates are interior tiles only.** A rim tile is never a coop. CONFIRMED.
- **Distance:** per coop, `(|dx|^2 >> 12) + (|dy|^2 >> 12)` in 1/4096 units, rejected
  when below `0x3000` unsigned (`0x004DE3A2`). Between tile centres that is
  `dx^2 + dy^2 < 3` in tiles, so a coop may not be any of a placed coop's eight
  neighbours; two apart in a row or column is allowed. The list holds **only the
  coops this pass has placed**. CONFIRMED.
- **Order:** each coop restarts the walk at `FirstSlab`. Since a refused tile stays
  refused, this is one greedy pass in chain order (`room-slab-chain.md`): row-major
  for a pre-placed room, wave order from the drag's second corner for a dragged one.
- **Count:** `Size / 6`, capped at 36 (`> 0x23` gives `0x24`), counted over all tiles,
  while the candidates are interior: the walk can run out first. CONFIRMED.
- **Position:** exactly the tile centre, `x*4096 + 0x800`, `y*4096 + 0x800`, height
  `0xE00`. CONFIRMED.
- **Per coop:** its angle `+0xF4`/`+0x102` is `(selector(x, y) mod 4) << 9`, the
  coordinate selector of `auto-placed-furniture.md` §2 (y with `0x2286`, x with
  `0x16F2`); `+0x14A` is `random(4) << 9` from the **simulation stream** (world
  `+0x15A0`, `0x004DE4E4`), so every placed coop consumes one roll. It is pushed
  onto `AutoPlacedObjectList` (`0x004E6220`) and marked auto-placed (`+0x133 = 1`).
- **When:** every rebuild. The tile adder `0x004ED1A0`, the merge `0x004EC900` and
  the tile remover `0x004ED440` each first delete every auto-placed object through
  `0x004E6BC0` (unlink, then `0x004B7B10`, whose destructor path unlinks the object
  from its tile at once), then run `Room_Rebuild`. So every change of shape
  **replaces all the coops** from scratch; nothing is kept or moved. The
  "object 35 already here" test therefore only refuses a coop that is not on the
  auto-placed list, and such a coop is not in the distance list.

What changed (2026-10-09): this section said the walk covered every tile "in the
room's own tile order"; the floor-slot test, the per-pass list, the rebuild
replacement and the RNG roll were unread.

**Worked examples** (row-major chain, NW tile (20,5)):

| room | Size / 6 | interior | coops |
|---|---|---|---|
| 5×5, x20–24, y5–9 | 4 | 3×3, x21–23, y6–8 | (21,6), (23,6), (21,8), (23,8) |
| 3×3, x20–22, y5–7 | 1 | (21,6) | (21,6) |
| 4 wide × 6 tall, x20–23, y5–10 | 4 | x21–22, y6–9 | (21,6), (21,8): **2**, the walk runs out |
| 6 wide × 4 tall, x20–25, y5–8 | 4 | x21–24, y6–7 | (21,6), (23,6): **2** |

The 5×5 result is the owner's "2×2 grid".

## 3. Where an egg appears

The lay routine `0x004DE620` first asks whether the room has capacity for one more
Chicken (`0x004E53B0` with the Chicken's room capacity, `0x004DE669`); read in
instructions 2026-10-02:

1. **Capacity left.** Walk the room's tiles counting those with no object on them,
   pick **one at random**, and lay the egg at that tile's centre.
2. **No capacity** (`0x004DE8AE`). Only while the keeper's byte `+0x1C1` is below
   the global at `0x006F28CB` (§5) and the room has auto-placed objects: count the
   coops (object 35) on `+0x0E`, pick **one at random**, and lay the egg there.

So the coop is where a full room lays, which fits §5's free-range counter.
`HatcheryRules.TryLayEgg` still follows an earlier reading (coops first, nothing
when full): see *Known exceptions*.

The room also keeps a **running count of everything it has produced**, bumped on
each successful lay: `ChickensGenerated`, `room+0x51` (the dumper's name), zeroed by
the room's init `0x004DE060` and incremented at `0x004DE83A` and `0x004DE9DB`.
The Hatchery finder reads it: an empty hatchery asks for more room (42) only once
it has laid (`computer-keeper-messages.md` §2).

**What a change of shape does to the count** (binary, 2026-10-04, Q522):
- *Growth.* `Room_AddTile` (`0x004EC650`) links the new tile into the room of
  its first neighbour, and that room keeps its count.
- *Merge.* `Room_Absorb` (`0x004ECB70`) writes no overlay field. The surviving
  room keeps its count and the absorbed room's count is dropped with it.
- *Losing a tile, by sale or otherwise.* `Room_RemoveTile` (`0x004ED440`) frees
  the room and builds what is left as new rooms. A new Hatchery goes through
  `Room_Construct` and then `0x004DE060`, so its count restarts at 0 and its
  laying clock at that turn. The rebuild copies overlay state across only for
  types 8, 11 and 15 (`room-object.md`). CONFIRMED.

Ours keeps the count on the room's lowest tile (ADR-0335). It therefore also
loses the count when that one tile changes, but keeps it when any other tile is
sold, which DK2 does not.

A room has two object lists. Coops are pushed onto `AutoPlacedObjectList`
(`+0x0E`, `0x004E6220`), which the coop path above walks. The finder tests the
other, `ObjectList` (`+0x0C`, `0x004E5FF0`), whose hatchery adders take only a
Chicken, object 9. `0x004DEB70` adopts the chickens on the room's tiles. It
runs from the Hatchery's tile pass `0x004DE560`, which first recomputes the
generation interval `+0x4D` from `Size`, and from its ownership-change slot
`0x004DE5D0`, which first empties `+0x0C` through `0x004E6130` (2026-10-04). It
gives a chicken owned by someone else to the room's owner, and lists it only
when the chicken's room word `+0x138` is 0. `0x004A1F87` adds a chicken standing
on its owner's Hatchery. No adder for an egg was
found, so an egg does not stock a hatchery; that the list holds nothing else is
STRONGLY INFERRED, its other adders being other rooms' (Q522). Ours counts the
chickens on the room's tiles that are neither carried nor in the hand
(`EatingRules.HoldsAChicken`); the adder's capacity refusal is not modelled.

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

The produced count is `TileState.ChickensGenerated`, on the room's lowest tile like
the laying clock; a tile that changes terrain or owner drops it (ADR-0335).

## Tests

`HatcheryTests` and `HatcheryClockTests` (`tests/DK2.Simulation.Tests/`);
`ObjectsOnRealDataTests` and `Dk2TerrainFixture` cite §4 for the shipped
constants. Scenario `scenarios/rooms/a_hatchery_lays_and_hatches.yaml`.

## Known exceptions

- The nine-tile truncation to 7 rather than 8 (§4), reproduced.
- `MaxFreeRangeChickensPerPlayer` parsed, exposed and not applied (§5) — the
  second of the three balance variables this project has caught not meaning what
  its name implies, after `MaxGoldPileOutsideTreasury`.
- Ours lays at a coop whenever the room has one and refuses a full room; DK2 lays
  on a free tile while the room has capacity and at a coop, under the free-range
  cap, when it has none (§3). Unbuilt.
- DK2 deletes every auto-placed coop on each change and places them all again,
  with new ids in the new chain order, each with its angle and one
  simulation-stream roll (§2). Ours keeps the coops still wanted and adds the
  rest, so after a growth the coop list's order, which the lay's random pick
  indexes, can differ from DK2's; the angle and the roll are not built.

## Open questions

- What `+0x1C1` counts, and which global `0x006F28CB` is, beyond §5's reading of
  them as the free-range counter and `MaxFreeRangeChickensPerPlayer`.
- Q134: why the exponent is that value. Does not affect fidelity.
