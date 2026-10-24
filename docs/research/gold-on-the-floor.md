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

Ours keeps the same number in `TileState.GoldHeld` (ADR-0015): a store tile holding gold
stands for exactly one such object, since the withdrawal `0x004F4510` deletes a pile with its
last gold. The two behaviours that read the object are built on that mapping (ADR-0534,
[gold-hover-and-sparkle.md](gold-hover-and-sparkle.md) §3, §4): the sparkle, one world-stream
draw per pile, and the Hand's lift, which makes the object from the tile's whole gold.

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

## 7. Sub-tile positions and Hand landing

DK2's creation paths place piles differently. Most legacy piles retain only
a tile/cell here; spell-created and Hand-dropped gold retain PosX/PosY:

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

Mining uses `PutDown` per cell. All three gold types dropped from the Hand use
the shared landing merge; see [keeper-spell-create-gold.md §3](keeper-spell-create-gold.md#3-implementation-boundary).

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

**An imp fetches a pile and carries it to a treasury once its hands are full**
(ADR-0056, ADR-0288), so the drops above are a delay rather than a loss. State 27
(`0x00466850`) takes the piles within `0x555` up to the level-scaled cap, stopping at the
first that fills the hands; the walk ends at the pile's point, where §12's pick-up usually
takes it first (ADR-0382, ADR-0389). 27 then calls the store chooser only for full hands; a part-load goes back to 10 and is
carried until more gold fills it, a dig or a mine ends with nothing more to excavate
(kind 15), or the imp is dropped on a store. **Where that sits in its priorities is
Tier B, below marked excavation and claiming** — not, as ADR-0056 first had it,
ahead of a marked wall (ADR-0058, Q146, Q147; `imp-task-priority.md` §1 and §5).
Against reinforcing it is decided by **distance**, not order: a wall beats gold
on the same tile, a nearer pile beats a farther wall.

**Not built, and named:**

- exact mining-point distance merges (§10): ours retains the cell approximation;
- treasury gold as an object (§2), deliberately: a lifted pile becomes one only in the hand;
- exact legacy creation positions (§7); their views use cell centres where retained.

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

Read 2026-09-28, re-read 2026-10-24 with Ghidra. STRONGLY INFERRED from the binary; the
outcome is CONFIRMED by the owner's Level 2 recording (`navigation-captures.md`).

- **The strike** (`0x004971D0`, the mining action's effect) takes gold out of the
  seam, adds it to the owner's `GoldMined` (`+0x15A`), then hands the imp as much
  as it can still hold: its level-scaled maximum (`+0x69C`, raw at level 1, else
  scaled by the level table) less what it carries (`+0x3FB`), through
  `0x0048AE70`. **The rest goes to `0x004510C0` at the imp's own position.**
- **The put-down** (`0x004510C0`, a map method) adds the amount to a floor pile
  (object 1) at Chebyshev distance strictly less than `0x40`, or creates a new pile
  there owned by the neutral global. Its other caller, `0x004A4710`, spreads a
  lump of gold (`0x006F25B4`) over the Treasuries and Heart and puts the rest down
  the same way.
- **The miner's state** (26, `0x00465870`). An imp the hands test `0x00464360(1)` finds
  full (the level's maximum less variable 74, Q50) first tries the store chooser `0x00464400`, which picks the nearest reachable Treasury or intact
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
1/64 of a tile. `GoldCarryingTests.AnImpWithNowhereToPutGold_KeepsMining_AndGrowsALumpAtItsFeet`
verifies repeated strikes leave one accumulated pile. Different mining positions
may legitimately leave separate piles; DK2 does not merge all gold on a tile.

## 11. A store sweeps loose gold on its own tiles back in (built, ADR-0285)

Read 2026-09-28. STRONGLY INFERRED from the binary; the outcome matches the
owner's Level 2 play. They dropped 20,000+ on one Heart tile. The Heart took
16,000, the rest lay on that tile, and it drained into the Heart as they spent.

- **The overflow.** The deposit `0x004F3C60` walks the room's tiles along its chain from
  the given tile, filling each tile's store pile (object 3) to 3000, or to 1000 on a Heart's
  rim tiles (`0x004DA1B0`); a pile it has to create costs a rand(0x800) on the world stream
  (line `0x14F`, the pile's `+0xF4`/`+0x102`; ADR-0355). What is left after a full pass is a neutral floor pile (object 1) on the starting tile, at one of §7's eight points.
- **The sweep.** `0x004F47E0` walks the room's tiles; each gold-flagged object not a store
  pile (def `+0xF4` bit `0x40`) is deposited through `0x004F3C60` and destroyed, so what the
  store cannot take comes straight back down. For a Heart it needs room `+0x51` non-zero.
- **Heart `+0x51` is `SuckGold`** (Q472, read 2026-10-04): the dumper's overlay (`room-object.md`).
  Heart init `0x004D9610` (slot `+0x01` of the type-5 record `0x006A1F6C`) sets it to 1. Its
  one clearer `0x004F38C0` zeroes it and turns each store pile into a floor pile of the same
  gold, debiting the keeper. It runs from the room-freed hook `0x004D9670` (slot `+0x05`, via
  `Room_OnFreed` `0x004E39C0` only), the heart-destroyed turn (`0x004DAA10`) and the Treasury's
  ownership-change hook `0x004F3C40` (slot `+0x0D`). A save-game load was not checked.
- **When.** The Treasury update `0x004F39A0` sweeps every 16th turn while not full. The Heart's
  `0x004D9690` returns at once on byte `+0x9A` bit 0, else sweeps when `turn & 0xF == 0` and
  GoldTally `+0x49` differs from 16 × 3000, the Treasury cap (`0x004D96A4`–`0x004D96D1`): that
  always passes: with `+0x51` set and that bit clear, it sweeps every 4 s, full or not.

**Seen in a capture (2026-09-29, level2-live1).** The owner dropped a 36,400 pile
on a Heart rim tile at turn 1020. At capture tick 1023, which is turn 1024, the
sixteen rim piles were topped to 1,000 (+15,500) and a 20,900 pile lay on the tile.
Hand drops onto the floor beside the Heart had merged into that pile first.

Built as `StoreSweepSystem` (ADR-0285, ADR-0355) as read above, through `GoldStore.DepositFrom`, on DK2's turn (the tick plus one; Q596);
a full Treasury is not swept. Deviates: `SuckGold` is taken as set.

## 12. A creature takes loose gold it walks over (read 2026-09-30)

**CONFIRMED (owner, 2026-09-30):** a creature that walks onto a tile with loose gold on it
tends to take the gold, which in effect steals it from its keeper. The binary reads below
are **STRONGLY INFERRED** (pyghidra-mcp, Ghidra 12.1.4) unless marked otherwise.

**The mechanism.** It is not a job, a state or a chance. The per-tick creature update
`0x00488930` checks the things on the map tile under the creature. It moves floor gold
straight into the purse (`+0x3FB`), capped at the level-scaled `MaxGoldHeld`: definition
`+0x69C` at level 1, otherwise that value times the level row at `0x006E4154 + 0x13B`,
divided by 100. The purse is written directly rather than through `Purse_Receive`
(`0x0048AE70`), so **the whole take is kept, not half** as with payday and stealing.
Nothing returns early before the block, so it runs every tick, whether or not the creature
changed tile. The block has two arms.

**The worker arm** (`0x0048904E`–`0x00489241`) is for a creature that is not Possessed
(`+0x3E6` bit 7), whose definition is a worker (`+0x686` bit 0), and whose owner is a
keeper (`player+0x10FC` at least 3). It works only while the purse is below `MaxGoldHeld`
less variable 74 (`0x006F28B6`), the margin the imp's hands test `0x00464360` uses. It walks
only its own tile's thing list and takes the **first** neutral pile strictly within `0x555` of
it on both axes, once per tick: what the purse lacks against the full maximum, or the whole
pile if less. The claim on the way (`0x00464170`) runs before it, on a tile change only. So an
imp picks up gold it passes on the way to any job, outside the fetch state 27 (§9).

**The creature arm** (`0x00489246`–`0x0048954B`) takes everyone else: non-workers of any
owner (a keeper's fighters, an enemy's, heroes, neutrals), possessed creatures, and hero or
neutral workers.

1. It does nothing while CasinoBigWinnerGameTickRemaining (`+0x3F2`) is non-zero, or when
   the tile is a Casino room tile (type 15).
2. It walks the **whole** tile's thing list. There is no distance test.
3. A thing is taken if it is an object (`+0x12` = 2) owned by the neutral player
   (`+0x28` = `word[0x006F2548]`, the owner every floor pile gets, §8), is of type id
   (`+0x12C`) 1, 2 or 3, and the creature is not in state 76, the hand-drop landing.
4. On a Treasury tile (room type 1) the pile is left for the store sweep (§11). On any other
   room tile, **Dungeon Heart included**, and off rooms, the pile is taken. The worker arm
   tests no room at all.
5. The take is what the purse lacks, or the whole pile if that is less. The pile loses it
   through `0x0049C760`, and an emptied pile is destroyed. The purse is clamped to
   `0x0048AB60`.
6. The walk goes on to the next pile, so every pile on the tile goes in one tick until the
   purse is full. After that each take is zero.
7. **Side arm:** a neutral Chicken (type 9) is destroyed, and the creature starts action 26
   (`0x1A`, eating) at its own position, except on a Portal (3) or Hatchery (4) tile.

**Conditions (Q2).**

- Any creature with a non-zero `MaxGoldHeld` takes gold. Shipped values include Imp 1400,
  Goblin 5000 and Vampire 7500. The Skeleton has 0, so it takes nothing
  (`creature-table-dump.md`).
- The creature's owner is never compared with anything. The pile only has to be neutral,
  and every floor pile is, so an enemy or a hero on your ground takes your gold the same
  way.
- Every floor pile counts except one on a Treasury or Casino tile (the owner agrees). A Heart's
  own tile piles (object 3) are the keeper's, not neutral, so they are safe; a loose pile there
  is taken. **Seen (captures, 2026-09-30):** walking Goblins (states 7, 8) took whole neutral
  Heart-tile piles in one tick: 800 and 3,388 (level1 seed02), 4,998 (l1win927b). Loose Heart
  gold is only rare: a Hand drop fills the store first and the sweep (§11) pulls in the rest.
- There is no random draw, no definition flag (the worker bit only picks the arm) and no
  cooldown: it runs four times a game second.
- Any state counts except 76.

**Afterwards (Q3).**

- The gold stays in the purse until it leaves one of these ways:
  - destruction spills it as one to three piles (§6);
  - leaving state 196 (`0x0045A0D0`) and the thief's escape state 169 empty the purse
    through `0x0048AF50` before removal, so the creature takes the gold away with it;
  - the Casino takes from the purse and pays into it (`0x0047AD70`);
  - a worker deposits it in state 29 (`0x00466AC0`).
- A full purse caps what payday, stealing (state 165) and later pick-ups can add.
- The keeper's total does not move. Neither arm calls a player routine, floor piles are
  neutral, and the total is recounted from Treasury and Heart rooms (`0x004B9EC0`). What is
  lost is the gold the imps would have fetched (§8, §9).
- There is no anger change, message, sound, trigger event or effect.

**Balance variables (Q4):** none of its own. It uses the definition's `MaxGoldHeld` and
the level row. The worker arm also uses variable 74, which is **340** on every shipped level
(`imp-hands-full.md`).

**Related, not the walk-over** (LIKELY; detail in each function's record):

- A possessed creature's action 26 (`0x00514830`) has a pick-up arm. It takes the smallest
  of the pile, twice what the purse lacks, and 100, and credits half. A possessed creature
  also falls under the creature arm above.
- Gold dropped onto a creature (`0x0049EF90`, landing with a creature id at `+0x142`) pays
  it half through `Purse_Receive` and settles its owed wage and pay anger.

**Unread** (Q494 is the observable check): whether `0x00488930` runs for a creature in the
Hand, whether a falling pile (§6) is on the tile list yet, and what the chicken arm does to
hunger.

**Ours:** both arms built: workers ADR-0293, the creature arm `WalkOverGoldSystem` (ADR-0300;
the take compares unsigned, `0x004893B1`). The walk-over chicken is not built.
