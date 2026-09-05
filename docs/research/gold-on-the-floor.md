# Gold on the floor: the fourth place a keeper's gold can be

**Source:** `DKII.EXE`, the `Treasure.cpp`, `Player.cpp`, `Creature.cpp` and
`ImpUtils.cpp` translation units, read on 2026-09-05, plus `Objects.kwd` and
`GlobalVariables.kwd`. Facts in our own words; ADR-0046 governs the boundary.

**Status: researched and built, both ends.** `FloorGold` implements the pile,
the split a dying creature makes, and the paths that put gold on the floor;
`ImpTaskSystem` implements the imp that picks it back up (ADR-0056, and the
owner supplied that trigger the following day). 31 tests and a scenario.

## Why this was worth doing now

`GoldStore` has carried the same paragraph since it was written: *"Floor piles
are not [modelled]: a pile is an OBJECT and the OBJECTS record is not parsed
until M6."* Both halves of that stopped being true — OBJECTS is parsed, and
objects have somewhere to live (ADR-0054). What was left was the rule, and three
places in the simulation were reporting gold they had to throw away because of
it: a sold treasury tile, a destroyed one, and a creature that died carrying.

## 1. A pile is an object, and which object depends on where it is

The object's union has a **`Gold`** member: a single dword at the union's first
offset, which the game's own object dump prints as **`GoldHeld`**. The same four
bytes a bed reads a creature id out of (objects.md §7).

Three of the 145 records carry `TypeGold`, and the code tells two of them apart
by **id**, not by flag:

| Id | Name | Where it is used |
|---|---|---|
| **1** | Gold | **on the floor** — every path below creates this one |
| 2 | Gold Bag | **created by nothing** the binary reaches through the ordinary object-create call |
| **3** | Gold Pile 1 | **in a treasury or a dungeon heart** — one per tile |

The negative on **Gold Bag** is worth stating because it is the kind of thing
this project has been wrong about before: every call to the object-create
function whose type argument is a literal 1, 2 or 3 was checked, and **none of
them passes 2**. So a Gold Bag arrives from a level file or from somewhere that
does not pass a literal, and what it is for is **UNKNOWN**.

Two of the records corroborate the split from the data side. `Gold Pile 1`
carries **`RoomCapacity` = 1** — the same field that turned out to cap a
hatchery at one chicken per tile — and it is the only one of the three with
`Obstacle` rather than `Placeable`. **One gold object per treasury tile** is
exactly what the deposit code does, found independently.

## 2. A treasury tile IS one of these objects

This is a difference between DK2 and this project, and it is recorded rather
than acted on.

The original's deposit walks the room's tiles in the room's own linked order,
starting from the tile it was given and wrapping through the room's first tile
until it comes back to where it started or runs out of gold. On each tile:

- find the object with type id 3; **if there is none, create one at the tile
  centre**;
- the cap is `MaxGoldPerTreasuryTile` (**3000**), or
  `MaxGoldPerDungeonHeartTile` (**1000**) when the room is a dungeon heart;
- fill to the cap and carry the remainder to the next tile.

Ours keeps the same number in `TileState.GoldHeld` (ADR-0015). The behaviour is
the same because the cap is per tile either way, and **no behaviour has been
found that separates them**; the place it would show is a keeper picking a
treasury pile up in the hand, which does not exist here. Written down so the
difference is a known one.

**A dungeon heart's capacity is `MaxGoldPerDungeonHeartTile << 4`.** Hardcoded,
as a shift — not as a count of the room's tiles. That is **16000**, which is
what the owner measured for a keeper with no treasury (Q52, ADR-0027), and it is
an independent confirmation both of the number and of the reading behind it:
sixteen of a heart's 25 tiles store gold and the middle 3×3 platform stores
none. A treasury's capacity, by contrast, **is** counted:
`tileCount × MaxGoldPerTreasuryTile`.

## 3. `MaxGoldPileOutsideTreasury` is a target rule, not a limit

**This is the finding that mattered most, and it is the same shape as the
hatchery's flock cap two days earlier.**

The variable is **2000**, and its global has exactly **three readers in the
whole binary**. All three are imp code choosing where to put a load down, and
all three make the same test: a candidate spot holding a pile whose `GoldHeld`
is **strictly greater** than the variable is **not chosen**. Note the strictness
— a pile sitting exactly on 2000 is still a valid target.

**Nothing anywhere clamps a pile to it.** Two of the three paths in §4 and §5
routinely make piles bigger: a sold treasury tile holding 3000 becomes a pile of
3000.

So the variable answers "where will an imp put gold down", not "how big may a
pile be", and enforcing it as an invariant would have been wrong in a way no
test could have caught — every test would simply have agreed with it. Compare
`MaxFreeRangeChickensPerPlayer`, which was nearly enforced for the same reason
and turned out to cap something else entirely (hatchery.md §5).

**Q65 is ANSWERED by this.** It asked what happens to the 1000 surplus when a
full treasury tile is sold, and offered three candidates: spread to neighbours,
lost, or the cap does not apply. **The third**, and for a plainer reason than
"does not apply" — the code on that path never looks at the cap at all.

## 4. A store that stops being one leaves its contents on the floor

For every gold object in the room being unmade, the original:

1. creates an object **1** at **the same tile's centre**;
2. moves the amount across;
3. **subtracts it from the player's gold total**;
4. destroys the old type-3 pile.

So the gold is neither lost nor refunded. It becomes a floor pile the keeper no
longer owns and an imp can fetch back — which is exactly what the owner
described on 2026-09-03, now matched step for step in the binary.

**Implemented:** `RoomSellSystem` and `TileWorkSystem` both drop the displaced
amount on the tile it came off.

## 5. Gold with nowhere to go lands where the caller says

The player-level "give this keeper gold" routine spreads the amount **evenly
across the player's treasuries and dungeon heart**, room by room, repeating
while any of them is still taking. Only when every room has refused does it
**create an object 1 at the position it was given** and put the remainder there,
with an interface message.

Two things follow that are worth keeping:

- **the caller chooses the tile**, so "where does an overflowing refund land" is
  a property of the call site rather than of the gold rules;
- **DK2 spreads a deposit across rooms**, where `GoldStore.Deposit` works in
  tile-index order. Ours already says that order is ours and not DK2's; this is
  what DK2's actually is, and closing the gap is a separate change because it
  moves the state hash.

The mirror image is the withdrawal path, which spreads the same way: an even
share from each gold room, then another round for whatever is left.

## 6. A creature drops its gold in one, two or three piles

The cleanest rule of the lot. On creature teardown, if it is carrying anything:

    piles = 1 if gold < 1000, 2 if gold < 3000, else 3
    each pile gets gold / piles     <- integer division

- every pile is an object **1** on the creature's own tile;
- **the remainder is destroyed** — 2999 carried leaves two piles of 1499 and one
  gold that ceases to exist;
- the position is drawn uniformly across the whole tile, and the pile is spawned
  **three tiles up** so that it falls;
- the creature's hands are then zeroed.

The routine is called from creature destruction, so it covers **every** way of
leaving the world rather than dying specifically. Two of its three callers are
in the casino's state code, which is presumably how a creature loses its money
gambling.

**Implemented:** `FloorGold.DropFrom`, called from `CreatureHealthSystem` on
death — the only teardown this project has (Q137).

## 7. Sub-tile position, which is not modelled

The original is precise about where within a tile a pile lands, and our
`ObjectState` has a tile index and no finer position, so all of this collapses
to "on this tile". Recorded because it is presentation the day there is a
renderer for objects, and because the three layouts differ:

- **an overflowing treasury** puts the pile at one of **eight fixed points**,
  chosen at random: the ring of quarter-points around the tile centre, each
  coordinate one of 0x3FF, 0x7FF or 0xBFF, all nine combinations except the
  centre;
- **a dying creature** draws both coordinates uniformly over the whole tile;
- **an imp putting a load down** uses a **twelve-slot** layout — four directions
  by three slots along an edge, each slot a third of a tile (0x555) — and this
  is the one place the original **merges** rather than making a new pile, which
  is what the outside-treasury limit governs.

Our `FloorGold` therefore **never merges**: every path it implements is one the
original does not merge on, and the merging path is an imp task that does not
exist yet.

## 8. Who owns a floor pile

All three paths pass **the same global** as the new object's owner, and it is
emphatically **not** the room's owner or the dead creature's keeper. That global
is written **once, at level load**, from a field of the level structure, and
read from 165 places.

**Which player id it is has not been read out — Q136.** Ours uses
`PlayerIds.Neutral` and says so in the code.

**Both ends of it now agree, which is new.** The original's gold-fetching task
requires the object's own owner to equal that same global before an imp will
take it (`imp-task-priority.md` §4), so the id that creates a pile and the id
that is looked for when collecting one are the same field. That does not say
*which* id it is; it does say the two halves are consistent, which is what the
question was really about.

## 9. What is built, and what it waits on

**Built** — `FloorGold`, and every number in it is the original's:

- the pile as object 1 carrying `ObjectState.GoldHeld`;
- the one/two/three split and its destroyed remainder;
- `MaxPile` and `AcceptsMore` **exposed and never applied as a clamp**;
- drops wired into selling a store, destroying a store, and a creature dying.

**Built the following day** (2026-09-05, ADR-0056), once the owner supplied the
trigger: **an imp fetches a pile and carries it to a treasury, and it does that
before it will touch a marked wall.** See `imp-tasks.md`; the drops above are
now a delay rather than a loss.

**Not built, and named:**

- the twelve-slot merge, which arrives with an imp that puts a load *down*
  rather than picking one up;
- treasury gold as an object (§2), deliberately;
- **objects are invisible to the renderer.** A bed, a coop, an egg and now a
  pile are all in the world and none of them is in `WorldView`, so a scenario
  picture shows a tile with gold on it as bare floor. That predates this work
  and is not gold-specific.

**Open questions raised here:** Q136 (whose a floor pile is), Q137 (whether
leaving by portal drops gold, as dying does), Q138 (whether mined gold an imp
cannot hold falls on the floor), Q139 (which tile an overflowing refund uses),
Q140 (whether an imp with a full treasury puts its load down).

**And more from building the imp that collects it:** Q142 (topping up a
part-load), Q143 (whether the pick-up costs time), Q145 (whether fetching
interrupts a swing in progress), Q146 (where fetching sits below digging).

**Q144 is answered, and not the way this document first guessed.** The owner
corrected their own report — gold mined on ground that was neither connected nor
claimed never came home — and this document offered "reachability or ownership,
one bridge separates them". It is **ownership**: the original refuses a tile that
is neither water nor the worker's own before it looks at what is on it. Ours
reproduced the outcome by accident, through both searches having to walk.

**And Q147 was answered the same day, by the owner and then the binary:** DK2
chooses in two tiers — digging and claiming whole-map by kind, and only then a
ring scan in which reinforcing beats gold on the same tile and a nearer pile
beats a farther wall. So gold sits **below claiming**, and against reinforcing
it is decided by **distance**, not order. `imp-task-priority.md` and ADR-0058.
