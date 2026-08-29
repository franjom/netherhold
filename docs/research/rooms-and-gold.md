# Rooms, and where a keeper's gold is

Milestone M3. What a room is, where one may be built, what it costs, and the
consequence that turns out to matter most: **gold is not a number on a player,
it is a thing in a tile.**

Implemented by `DK2.Simulation.RoomRules`, `DK2.Simulation.GoldStore`,
`DK2.Simulation.Commands.BuildRoomCommand` and
`DK2.Simulation.Systems.RoomBuildSystem`.

---

## Summary of confidence

| Claim | Status |
|---|---|
| A room owns a terrain type; a room tile *is* that terrain | **CONFIRMED** — the ROOMS record names it and TERRAIN flags it `Room` |
| Which ground a room may sit on is its `PlaceableOnLand/Water/Lava` flags | **CONFIRMED** — the flags partition the shipped rooms exactly as the game plays |
| Only a `Buildable` room may be built by a player | **CONFIRMED** — the flag exists for this and is off for hearts, portals and hero gates |
| A treasury tile holds 3000; a dungeon-heart tile holds 1000 | **CONFIRMED** — two variables, named for those two rooms |
| A keeper's gold is the sum of what their tiles hold | **STRONGLY INFERRED** — every limit DK2 sets on gold is per tile |
| A room is built on land the player already owns | **STRONGLY INFERRED** — observed, and the tile carries no other candidate |
| `Cost` is per tile rather than per room | **STRONGLY INFERRED** (Q32) |
| Which tile of a treasury a deposit lands in | **UNKNOWN** (Q35) — ours is tile-index order |
| Whether a new room tile must touch the keeper's existing land | **UNKNOWN** (Q33) |
| What a room *does*, beyond being owned terrain | **NOT IMPLEMENTED** — only the treasury and the heart do anything, because only they have a variable |
| Selling a room | **NOT IMPLEMENTED** (Q34) — two candidate refund numbers and no way to tell which applies |
| Where mined gold goes | **UNKNOWN** (Q31) — there is still no carrier |

---

## The data

Rooms, from the reference install (`dk2tools kwd rooms`, `kwd roomflags`):

```
 id name                cost sell% terr  size construction   flags
  1 Treasury             200   100   10   1x1 Normal         0x128C  PlaceableOnLand, HasWalls, Buildable, Attackable, HasFlame
  2 Lair                 300   100   11   1x1 Normal         0x128C
  5 Dungeon Heart          0   100   14   5x5 FiveByFiveRot  0x0204  PlaceableOnLand, Attackable
  8 Wooden Bridge        200   100   17   1x1 QuadConstruct  0x1083  PlaceableOnWater, PlaceableOnLava, Buildable, HasFlame
 17 Stone Bridge         500   100   26   1x1 QuadConstruct  0x1083
 22 Hero Stone Bridge    200   100   36   1x1 QuadConstruct  0x2003  PlaceableOnWater, PlaceableOnLava, IsGood
```

and the terrain those room ids point at:

```
 id name              health   maxHp  ->maxHp  ->dead  flags
 10 Treasury           10000   10000       10       8  Ownable, Room, Attackable
 11 Lair               10000   10000       11       8  Ownable, Room, Attackable
 14 Dungeon Heart      10000   10000       14       8  Ownable, Room, Attackable
 17 Wooden Bridge       2000    2000       17       8  Ownable, Room, Attackable
```

and the three limits the balance table sets on gold:

```
   24  MaxGoldPerTreasuryTile        3000
   32  MaxGoldPileOutsideTreasury    2000
  178  MaxGoldPerDungeonHeartTile    1000
```

---

## Gold is in tiles, not on players

This is the finding that shaped everything else in this note.

DK2's balance table has **no** variable for a player's gold, a starting balance,
or a maximum a keeper may hold. What it has is three limits, and all three are
*per tile*: how much a treasury tile holds, how much a heart tile holds, and how
much a pile on the floor holds. There is a fourth place gold sits — a creature's
hands, bounded by the CREATURES record's own `maxGoldHeld` — and that is per
creature.

Four places, four limits, none of them a player total. So the number DK2 shows a
keeper is the sum of those places, not a counter that treasuries merely cap. That
reading explains, with no further assumptions, three things the original visibly
does:

- a keeper with no treasury cannot keep gold;
- destroying a treasury takes the gold in it out of the game;
- gold appears on the floor when there is nowhere to put it.

A player-level integer would have made all three special cases. `PlayerState`
therefore has no `Gold` field at all — the removal is deliberate and is
ADR-0015 — and `GoldStore.Total` sums the tiles a player owns.

Mana stayed on the player, and the asymmetry is the data's rather than a
convenience: mana's variables (`MaximumManaThreshold`,
`ExcessManaDecreaseRatePerSecond`, `MaximumManaGainPerSecond`) are all rates and
ceilings on one pool, with no per-tile storage anywhere.

### Two of the four places exist

Treasury and heart tiles are implemented, because a variable says how much each
holds and a tile can carry the number.

Floor piles and creatures' hands are **not**. A pile is an OBJECT, and the
OBJECTS record is not parsed until M6; modelling a pile as tile state instead
would be inventing a representation, not implementing a mechanic. So gold that
will not fit is *refused* — `GoldStore.Deposit` returns what it accepted, and
every caller reports the shortfall. A scenario that gives a keeper gold with no
treasury says so in its report.

The same rule catches the other end. A treasury tile that is destroyed becomes
claimed floor, which holds nothing, so `TileWorkRules` reports the gold as
`GoldSpilled` and `TileWorkSystem` emits `gold_spilled`. In DK2 that gold
scatters as piles; here it is lost, loudly.

---

## Where a room may go

The ROOMS record answers this itself, and the answer needs no code that knows
what a bridge is:

```
if the terrain is water → the room needs PlaceableOnWater
if the terrain is lava  → the room needs PlaceableOnLava
otherwise (land)        → the room needs PlaceableOnLand,
                          and the terrain must not be solid or already a room
```

On the shipped data exactly three rooms carry water or lava placement — Wooden
Bridge, Stone Bridge, Hero Stone Bridge — and none of them also carries
`PlaceableOnLand`. `RoomsOnRealDataTests` asserts both halves of that against the
install, so a data set that broke the partition would fail a test rather than
quietly changing the rules.

**The land rule is that you build on ground you already own.** STRONGLY
INFERRED. It is how the original plays — a keeper claims floor with imps and only
then puts a room on it — and what makes it more than an observation is that the
data has no other candidate: nothing in the ROOMS record mentions ownership, so
the rule has to come from the tile, and `Ownable` plus an owner id is the only
thing the tile carries.

Water and lava are exempt because that terrain is not `Ownable` at all. A bridge
is built out over ground nobody can own, which is why the ownership test has to
be skipped there rather than being satisfied some other way.

**Not checked (Q33):** whether the original also requires a new room tile to
touch something the keeper already holds. On land, every tile such a rule would
reject is already rejected for not being the keeper's; the gap is visible only
for bridges, where our rules let a keeper start a bridge anywhere on the water.

---

## What a room costs

`Cost` is charged **per tile**, and a tile is refused if the keeper cannot pay
for it. STRONGLY INFERRED (Q32): DK2 charges as the keeper paints, and a per-room
reading would make a 5x5 temple cost what one tile of it does. It has not been
measured against the original, and it is one multiplication away from being
changed.

The gold comes out of the keeper's tiles, which produces a consequence worth
stating because it falls out rather than being designed: **a keeper's first
treasury is paid for out of the dungeon heart.** A treasury cannot be bought with
treasury gold when there is no treasury, and the heart is the only other place
the data gives gold a home. `MaxGoldPerDungeonHeartTile` is what makes the
opening move of a level possible at all.

Charging is all-or-nothing per tile (`GoldStore.TryWithdraw`): a tile that was
paid for but not laid, or laid but not paid for, would both be worse than a
refusal.

---

## What is not implemented, and is not guessed at

Read this before adding to `RoomRules`.

**A room does nothing.** A treasury holds gold and a heart holds gold, because a
variable says how much. A lair, a hatchery, a library, a training room, a temple
and the rest are inert terrain that a player owns. Each of them needs its own
research note; none of them can be guessed from the ROOMS record, which carries
art, size and cost and nothing about behaviour.

**Rooms have no shape.** `RecommendedSizeX/Y`, `TileConstruction` and the
`HasWalls` / `SpecialTiles` / `SpecialWalls` flags describe how a room meshes
itself together and grows walls. All of that is presentation and adjacency, and
none of it is read. A 1x1 temple is buildable here and would not be in the
original — recorded as Q36 rather than fixed by inventing a size rule.

**Rooms are not built over time.** In DK2 an imp walks to the tile and constructs
it. That is another worker job with the same UNKNOWN trigger as every other
unprompted imp job (Q24), so building here is instant and the report says so.

**Rooms cannot be sold.** Two numbers claim to govern the refund and they are not
obviously the same thing: `RoomDef.ReturnPercentage` (100 for every room) and the
global `RoomSellValuePercentageOfCost`. Both being 100 makes selling free, which
is not how the original plays. Q34.

**The objects a room places on its tiles** (`RoomDef.ObjectIds`) are not read.
M6.

---

## How this was established

1. `dk2tools kwd rooms` and a new `dk2tools kwd roomflags` dumped the 26 room
   records and their placement flags. The flag partition — three bridges over
   water and lava, everything else on land — was the finding that made the
   placement rule data rather than a switch on room type.
2. `dk2tools kwd creature Imp` was extended to print `maxGoldHeld` while looking
   for a player gold variable. There is none; there are four per-place limits.
   That negative result is what moved gold onto tiles.
3. The Imp's `maxGoldHeld` is **1400**, against 5000–7500 for everything else in
   the game. It is not used yet — nothing carries gold — but it is recorded here
   because it is the only per-creature number that looks like a carrying capacity
   rather than a wallet, and it is where Q31 will start.
4. The room→terrain link was checked both ways against the install: every room's
   terrain is flagged `Room`, and no two rooms share a terrain id. Both are
   asserted by `RoomsOnRealDataTests`, because `GameDatabase.RoomsByTerrain` is a
   dictionary and would silently lose a room if either failed.

---

## What does not fit

**Selling refunds everything.** `ReturnPercentage` is 100 for all 26 rooms and
`RoomSellValuePercentageOfCost` is 100 as well, which would make a room free to
try. The original does not play that way. One of the two numbers is not what its
name says, or selling applies a rule neither of them describes. Q34; selling is
not implemented, so nothing depends on the answer yet.

**The treasury costs the same as a wooden bridge.** 200 apiece, where a lair is
300 and a temple 3000. Nothing wrong with it — just worth noting before anyone
"corrects" the treasury's cost on the grounds that it feels too cheap for the
room a keeper cannot start without.
