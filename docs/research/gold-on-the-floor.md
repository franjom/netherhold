# Gold on the floor: the fourth place a keeper's gold can be

**Source:** `DKII.EXE`, the `Treasure.cpp`, `Player.cpp`, `Creature.cpp` and
`ImpUtils.cpp` translation units, read on 2026-09-05, plus `Objects.kwd` and
`GlobalVariables.kwd`. Facts in our own words; ADR-0046 governs the boundary.

**Status: researched and built, both ends.** `FloorGold` implements the pile,
the split a dying creature makes, and the paths that put gold on the floor;
`ImpTaskSystem` implements the imp that picks it back up (ADR-0056, superseded on
the ordering by ADR-0058).

**Implementation:** `FloorGold`, `GoldStore`, `ObjectState.GoldHeld`, and the
drops in `RoomSellSystem`, `TileWorkSystem` and `CreatureHealthSystem`.
**Tests:** `FloorGoldTests`, `ImpFetchesGoldTests`, `RoomSellTests`.
**Scenario:** `scenarios/imps/an_imp_fetches_dropped_gold.yaml`.

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

**Gold Bag is created by nothing** reachable that way: every call to the
object-create function whose type argument is a literal 1, 2 or 3 was checked and
**none passes 2**. So a Gold Bag arrives from a level file or from somewhere that
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

The variable is **2000**, and its global has exactly **three readers in the
whole binary**. All three are imp code choosing where to put a load down, and
all three make the same test: a candidate spot holding a pile whose `GoldHeld`
is **strictly greater** than the variable is **not chosen**. Note the strictness
— a pile sitting exactly on 2000 is still a valid target.

**Nothing anywhere clamps a pile to it.** Two of the three paths in §4 and §5
routinely make piles bigger: a sold treasury tile holding 3000 becomes a pile of
3000.

So the variable answers "where will an imp put gold down", not "how big may a
pile be". Enforcing it as an invariant is the trap: no test could catch it,
because every test would agree with it. `MaxFreeRangeChickensPerPlayer` has the
same shape (hatchery.md §5). Ours exposes it as `FloorGold.MaxPile` and
`FloorGold.AcceptsMore` and never clamps.

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
death, which is the only teardown this project has. Leaving through a portal is
**not** one: a creature keeps its gold when it is thrown through (Q137,
ANSWERED — owner, 2026-09-07), so nothing in `PortalSystem` drops.

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
- **a miner's overflow** goes down at the imp's own position, merging into a pile
  within 1/64 of a tile of it (§10). The **twelve slots** (four faces by three
  slots, each a third of a tile, 0x555) are where an imp *stands* to work a wall,
  not a put-down layout; the outside-treasury limit governs which of them a miner
  will stand in. This note first read them as a put-down layout (corrected
  2026-09-28).

Our `FloorGold` merges on that one path (`PutDown`), per tile rather than per point.

## 8. Who owns a floor pile

All three paths pass **the same global** as the new object's owner, and it is
emphatically **not** the room's owner or the dead creature's keeper. That global
is written **once, at level load**, from a field of the level structure, and
read from 165 places.

**It is the neutral player, `0x006F2548` (Q136, Q266; STRONGLY INFERRED, read
2026-09-25).** Of the 72 calls to the object creator `0x004B7A90`, eleven create gold.
All eight floor piles (type 1) push `word[0x006F2548]` as owner, including the Hand's
untargeted drop and the computer keeper's make-happy path (`0x004F9710`). The two
exceptions are treasury piles (type 3) in `0x004F3C60` and `0x004F4090`, which take
the owner from `+0x1D` of the structure in `ESI`, and ours keeps treasury gold on the
tile (ADR-0015). Ours uses `PlayerIds.Neutral`, now DK2's rule rather than a guess.

**Both ends of it now agree, which is new.** The original's gold-fetching task
requires the object's own owner to equal that same global before an imp will
take it (`imp-task-priority.md` §5), so the id that creates a pile and the id
that is looked for when collecting one are the same field. That does not say
*which* id it is; it does say the two halves are consistent, which is what the
question was really about.

## 9. What is built, and what it waits on

**Built** — `FloorGold`, and every number in it is the original's:

- the pile as object 1 carrying `ObjectState.GoldHeld`;
- the one/two/three split and its destroyed remainder;
- `MaxPile` and `AcceptsMore` **exposed and never applied as a clamp**;
- drops wired into selling a store, destroying a store, and a creature dying.

**An imp fetches a pile and carries it to a treasury** (ADR-0056), so the drops
above are a delay rather than a loss. **Where that sits in its priorities is
Tier B, below marked excavation and claiming** — not, as ADR-0056 first had it,
ahead of a marked wall (ADR-0058, Q146, Q147; `imp-task-priority.md` §1 and §5).
Against reinforcing it is decided by **distance**, not order: a wall beats gold
on the same tile, a nearer pile beats a farther wall.

**Not built, and named:**

- per-slot piles and merging within 1/64 of a tile (§10): ours merges per tile;
- treasury gold as an object (§2), deliberately;
- sub-tile position (§7). `ObjectView` carries a tile and no finer position, so a
  renderer draws a pile at the tile centre wherever the original would offset it.

**Open here:** Q136 (whose a floor pile is), Q139 (which tile an overflowing
refund uses), Q143 (whether the pick-up costs time), Q145 (whether fetching
interrupts a swing in progress). Q138 and Q140 are answered in §10.

**Answered here:** Q137 — a creature thrown through a portal keeps its gold
(§6). Q142 — an imp does top up a part-load in the original; ours never does,
because an imp with gold is already delivering before job selection is reached
(`imp-task-priority.md` §5). Q144 — the refusal is **ownership**, not
reachability: the original refuses a tile that is neither water nor the worker's
own before it looks at what is on it, which is why gold mined on unclaimed
ground never comes home. Ours reproduces the outcome by another route, both
searches having to walk.

## 10. Gold a miner cannot hold goes down at its feet (Q138, Q140)

Read 2026-09-28 with Ghidra 12.1.4. STRONGLY INFERRED from the binary; the
outcome is CONFIRMED by the owner's Level 2 recording (`navigation-captures.md`).

- **The strike** (`0x004971D0`, the mining action's effect) takes gold out of the
  seam, adds it to the owner's `GoldMined` (`+0x15A`), then hands the imp as much
  as it can still hold: its level-scaled maximum (`+0x69C`, raw at level 1, else
  scaled by the level table) less what it carries (`+0x3FB`), through
  `0x0048AE70`. **The rest goes to `0x004510C0` at the imp's own position.**
- **The put-down** (`0x004510C0`, a map method) adds the amount to a floor pile
  (object 1) lying within `0x40` of that point, Chebyshev, or creates a new pile
  there owned by the neutral global. Its other caller, `0x004A4710`, spreads a
  lump of gold (`0x006F25B4`) over the Treasuries and Heart and puts the rest down
  the same way.
- **The miner's state** (26, `0x00465870`). A nearly full imp first tries the
  store chooser `0x00464400`, which picks the nearest reachable Treasury or intact
  Heart with room and otherwise posts message `0x29`, `0x2A` or `0x2B` and returns 0.
  Delivering or not, before the next strike it checks the floor piles in its own
  third-of-a-tile cell. One over `MaxGoldPileOutsideTreasury` gives the task up
  (state 10). Otherwise it strikes again, so a miner with no store keeps mining.
- **Choosing a spot.** The dig and mine chooser `0x00469AF0`, and `0x0046A550`
  beside it, score the twelve standing slots of the target wall. For mining, they
  skip a slot whose cell holds a pile over the limit. These three are the
  variable's only readers (`.text`, `cseg` and all data blocks scanned, 226-hit
  control), and none of them puts gold down.

**Built:** a miner holds one of the twelve slots (`MiningSlots`,
`CreatureState.WorkSlot`), and a floor pile records its cell (`ObjectState.Cell`).
`TileWorkSystem.HandTo` puts the overflow into the slot's cell through
`FloorGold.PutDown`. The miner gives the slot up once that cell passes the limit,
and the chooser hands out only a free slot whose cell is at or under it (ADR-0264).
Deviates: the cell stands for DK2's exact point, merging where it merges within
1/64 of a tile, and the hand limit is unscaled, which agrees below level 5.

## 11. A store sweeps loose gold on its own tiles back in (not built)

Read 2026-09-28. STRONGLY INFERRED from the binary; the outcome matches the
owner's Level 2 play. They dropped 20,000+ on one Heart tile. The Heart took
16,000, the rest lay on that tile, and it drained into the Heart as they spent.

- **The overflow.** The deposit `0x004F3C60` walks the room's tiles along its
  chain from the given tile, filling each tile's store pile (object 3) to 3000, or
  to 1000 on a Heart's rim tiles (`0x004DA1B0`). What is left after a full pass
  becomes a floor pile (object 1, neutral) on the starting tile, at one of §7's
  eight points.
- **The sweep.** `0x004F47E0` walks the room's tiles. Every gold-flagged object
  there that is not a store pile (def `+0xF4` bit `0x40`) is deposited through
  `0x004F3C60` and destroyed. A pile the store cannot take therefore comes straight
  back down, less what fitted. For a Heart it runs only when room `+0x51` is
  non-zero (unread, Q472).
- **When.** The Treasury update `0x004F39A0` runs the sweep every 16th turn
  (`turn & 0xF == 0`, four seconds) while the room is not full. The Heart's update
  `0x004D9690` does the same, but compares its gold with 16 × 3000, the Treasury
  tile cap, where its capacity is 16 × 1000. So that test always passes, and the
  sweep runs whether the Heart is full or not.
- **`0x004F3C40`**, reached through a virtual slot, recounts the store, zeroes it
  and sweeps floor piles (object 1) in with `0x004F3B60`. Probably a rebuild path.

Ours has no sweep. Imps fetch a pile on the keeper's own ground (§9), which reaches
the same end more slowly and only while imps are free.
