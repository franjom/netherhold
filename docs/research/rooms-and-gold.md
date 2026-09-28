# Rooms, and where a keeper's gold is

Milestone M3. What a room is, where one may be built, what it costs, and the
consequence that shapes the rest: **gold is not a number on a player, it is a
thing in a tile.**

**Source(s).** The reference install's ROOMS, TERRAIN and VARIABLES records
(`dk2tools kwd rooms`, `kwd roomflags`, `game-database-dump.md`); owner
observation of the original (2026-08-29, 2026-08-30, 2026-09-03); `DKII.EXE`
reads recorded in `gold-on-the-floor.md`.

**Implementation** (`DK2.Simulation`). `RoomRules`, `RoomInstance`, `RoomIndex`,
`GoldStore`, `FloorGold`, `BridgeRules`, `Commands/RoomCommands`
(`BuildRoomCommand`, `SellRoomCommand`), `Systems/RoomBuildSystem`,
`Systems/RoomSellSystem`, `Systems/BridgeBurnSystem`.

**Tests.** `RoomTests`, `RoomInstanceTests`, `RoomSellTests`, `RoomConversionTests`,
`FloorGoldTests`, `GoldCarryingTests`, `ImpFetchesGoldTests`, `BridgeTests`
(`DK2.Simulation.Tests`); `RoomsOnRealDataTests`, `GoldOnRealDataTests`,
`BridgesOnRealDataTests` (`DK2.Behavior.Tests`);
`scenarios/imps/an_imp_carries_gold_home.yaml`, `an_imp_fetches_dropped_gold.yaml`.

---

## Summary of confidence

| Claim | Status |
|---|---|
| A room owns a terrain type, and `PlaceableOnLand/Water/Lava` decide the ground; `Buildable` decides what a player may build and sell | **CONFIRMED** — the flags partition the shipped rooms exactly as the game plays |
| A treasury tile holds 3000; a dungeon-heart tile holds 1000 | **CONFIRMED** — two variables, named for those two rooms |
| A keeper's gold is the sum of what their tiles hold | **STRONGLY INFERRED** — every limit DK2 sets on gold is per tile |
| A room is built on land the player already owns | **STRONGLY INFERRED** — observed, and the tile carries no other candidate |
| A new room tile must touch land the keeper already holds | **CONFIRMED (Q33), NOT IMPLEMENTED** — the gap is bridges |
| `Cost` is per tile, and the ROOMS record's number is the price | **CONFIRMED** (Q32) — five rooms priced in the original, all five matching |
| Which tile of a treasury a deposit lands in | **READ from the binary** (Q35); ours differs |
| `RecommendedSizeX/Y` gates what a room *does*, not what may be built | **STRONGLY INFERRED, BUILT** (Q36, Q45, ADR-0027); the Prison contradicts it, Q179 |
| Selling a room returns half its cost, per tile | **CONFIRMED, BUILT** (Q34, ADR-0029) |
| Mined gold goes into the mining imp's hands, then a treasury or heart tile | **CONFIRMED** (Q31); the Imp's `maxGoldHeld` is 1400 |
| Where an imp stops mining to unload | **HYPOTHESIS, implemented** (Q50) — when one more swing would not fit: five swings, 1363 gold |
| A dungeon heart stores 16000, not 25000 | **CONFIRMED, BUILT** (Q52, ADR-0027) — 16 of its 25 tiles hold gold |
| A pile on the floor is the fourth place gold sits | **STRONGLY INFERRED, BUILT** — object type 1; `gold-on-the-floor.md` |

---

## Gold is in tiles, not on players

All **26** ROOMS records — cost, size, terrain id, placement flags — and the
TERRAIN records they point at are dumped in `terrain-room-dump.md`. Every room's
terrain is flagged `Room` and no two rooms share a terrain id, both asserted by
`RoomsOnRealDataTests`: `GameDatabase.RoomByTerrain` is a dictionary and would
silently lose a room if either failed.

The balance table's limits on gold are these three, and nothing else:

```
   24  MaxGoldPerTreasuryTile        3000
   32  MaxGoldPileOutsideTreasury    2000
  178  MaxGoldPerDungeonHeartTile    1000
```

There is **no** variable for a player's gold, a starting balance, or a maximum a
keeper may hold; all three limits are *per tile*, and the fourth place gold sits —
a creature's hands, bounded by the CREATURES record's own `maxGoldHeld` — is per
creature. Four places, four limits, none of them a player total. So the number DK2
shows a keeper is the sum of those places, not a counter that treasuries merely
cap, which explains with no further assumptions three things the original visibly
does: a keeper with no treasury cannot keep gold; destroying a treasury takes the
gold in it out of the game; gold appears on the floor when there is nowhere to put
it. A player-level integer would have made all three special cases. `PlayerState`
therefore has no `Gold` field at all (ADR-0015), and `GoldStore.Total` sums the
tiles a player owns. Mana stayed on the player, and the asymmetry is the data's:
mana's variables are rates and ceilings on one pool, with no per-tile storage
anywhere (`mana.md`).

### All four places exist

**Treasury and heart tiles** hold gold in `TileState.GoldHeld`, capped by
`GoldStore.Capacity`. **Deviates:** the original keeps a treasury tile's gold in
object type **3**, "Gold Pile 1", one per tile, created on first deposit
(`FloorGold.TreasuryPileObjectId`, recorded and unused here).

**A creature's hands** are `CreatureState.GoldHeld`, bounded by that creature's
`maxGoldHeld`. The confirmed behaviour needs it: DK2's gold moves from a seam to
an imp and then from an imp to a treasury, so **a keeper's spendable total dips
while their imps are in transit** — that is where the gold is, not an artefact.

**Floor piles** are objects, as they are in DK2: type 1, "Gold", carrying the
amount in `ObjectState.GoldHeld`. `FloorGold` has the rules and
`gold-on-the-floor.md` the evidence, including the pile-splitting thresholds and
why `MaxGoldPileOutsideTreasury` is a *target* rule and not a cap (Q65). **A
floor pile is on nobody's books:** `GoldStore.Total` counts tiles, so a pile is
outside it, and that is the original's own arithmetic — making a floor pile out of
a treasury tile *subtracts* the amount from the player in the same breath.

`GoldStore` *refuses* gold that will not fit rather than dropping it: it knows an
amount and not a place, and the caller drops the surplus. A treasury tile that
stops being a store keeps its contents on the floor of the tile it stood on
(`TileWorkRules.GoldSpilled`, `gold_spilled`; `gold_displaced` on a sale).

---

## The round trip

**CONFIRMED (Q31, owner observation of the original.)** With room in a treasury
or a dungeon heart, the mining imp takes the gold into its own hands up to
`maxGoldHeld`, runs to the drop spot and adds it to the store. With nowhere to put
it, the imp keeps mining and a lump grows on the floor in front of it.

**Carrying is the tail of a mining job, not a job.** An imp does not choose
between "mine" and "carry" — it mines, and carrying is what happens next, so
`CreatureActivity.DeliveringGold` is a state with no `JobPriority` entry and is
the one place the confirmed priority order (ADR-0018) does not apply. Fetching a
pile *somebody else* left is a separate task with a priority of its own
(`ImpTaskKind.FetchGold`, `imp-task-priority.md`). **A pile is the fallback, not
the first step:** the obvious wrong model — mine, drop on the floor, collect it
later — is not what the original does; piles are what happens when a keeper's
stores are full.

### Where an imp decides to stop mining

**HYPOTHESIS, implemented (Q50):** *one more swing would free more gold than I
can hold*, the only stopping point that neither wastes gold nor overfills a pair
of hands. The shipped numbers give it its shape:

```
a gold seam        660 health, 3000 gold
MineGoldHealth     -300 a game second, applied once a tick at a fifth of the
                   tabled delta (ADR-0020, ADR-0089; tile-work.md)
  -> a swing takes 60 health and frees 273 gold, measured in the original
  -> a seam is 11 swings and yields exactly 3000
Imp maxGoldHeld    1400
  -> five swings fill it (1363), so a seam is three trips: 1363, 1364, 273
```

The 1363-against-1400 near-miss is what pointed at Q47. The owner measured 273
gold a swing, which puts a swing at 60 health — a fifth of `MineGoldHealth`'s
−300, that fifth being the loader's divide by variable 19 followed by the worker's
80 % Efficiency. **The fifth is approximate**: the shift floors, so gold's −300 lands on −60 exactly while claiming's 825 lands on 164 (ADR-0133).
**Trap: the fifth is the size of an application, not its rate.** Work lands
**once a tick, four times a game second**
(`SimulationTiming.WorkApplicationsPerSecond`, ADR-0089); "five applications a
second" is what held `TicksPerSecond` at 20 for months.
`ASwingAtARealSeamFreesTheGoldTheOriginalFrees` walks the real seam and pins 11
swings, −60 health apiece and 3000 in total.

### Which tile the load lands in (Q35)

**READ from the binary (2026-09-05; `0x00464400` re-read 2026-09-26).** The carrier
picks the nearest reachable Treasury or Heart below capacity, measuring to its
`FirstSlab`. It starts at a random chain tile (`0x004E4D90`), follows the links,
wraps at the tail and takes the first tile below capacity. Ours routes to that
tile (ADR-0243); `GoldStore.DepositInto` puts the load down where the imp arrives.
`GoldStore.Deposit`'s tile-index order survives only for the two cases with no
carrier in them, paying for a room and refunds, and that order is **ours, not
DK2's**; the withdrawal order is open (Q35). Totals are unaffected either way, and
on shipped level 1 the observable outcome matches the original: 1000 into one
heart tile, 363 into the next, and back to the seam
(`ALoadTooBigForOneHeartTileSpillsIntoTheNext`).

---

## Where a room may go

The ROOMS record answers this itself, needing no code that knows what a bridge is:

```
if the terrain is water → the room needs PlaceableOnWater
if the terrain is lava  → the room needs PlaceableOnLava
otherwise (land)        → the room needs PlaceableOnLand,
                          and the terrain must not be solid or already a room
```

On the shipped data exactly three rooms carry water or lava placement — Wooden
Bridge, Stone Bridge, Hero Stone Bridge — and none also carries `PlaceableOnLand`.
`RoomsOnRealDataTests.OnlyBridgesGoOverWaterAndLava` asserts both halves against
the install.

**The land rule is that you build on ground you already own.** STRONGLY INFERRED:
it is how the original plays, and the data has no other candidate — nothing in the
ROOMS record mentions ownership, so the rule has to come from the tile, and
`Ownable` plus an owner id is all the tile carries. Water and lava are exempt
because that terrain is not `Ownable` at all; a bridge goes out over ground nobody
can own.

**Adjacency is CONFIRMED and not implemented (Q33, Q66).** Binary recheck
2026-09-28: `0x0044F630(x,y,player,room)` rejects occupied room tiles and terrain
ids 31–34/37. Water/lava placement needs the corresponding room flag,
`0x00453B30(player)` cardinal adjacency, and no `0x0044F800` obstruction. Land
instead requires ownership, a clear world `+0x208` predicate and the land flag.
`RoomRules.CanBuild` still permits starting bridges anywhere on water.

---

## What a room costs

`Cost` is charged **per tile**, and a tile is refused if the keeper cannot pay for
it. **CONFIRMED (Q32):** the owner priced five rooms in the original (2026-08-30)
and stated the charge is per tile — Treasury **200**, Lair **300**, Hatchery
**300**, Library **600**, Training Room **500**, every one matching the ROOMS
record exactly. `Cost` could have been per room, or a base that `RecommendedSize`
modified, and neither would have shown up until somebody built a big room;
`RoomsOnRealDataTests.ARoomCostsWhatTheOriginalChargesForIt_PerTile` holds it.

The gold comes out of the keeper's tiles, and a consequence falls out: **a
keeper's first treasury is paid for out of the dungeon heart.** A treasury cannot
be bought with treasury gold when there is no treasury, and the heart is the only
other place the data gives gold a home. Charging is all-or-nothing per tile
(`GoldStore.TryWithdraw`): a tile paid for but not laid, or laid but not paid for,
would each be worse than a refusal. The treasury costs the same as a wooden
bridge, 200 apiece, where a lair is 300 and a temple 3000 — worth knowing before
anyone "corrects" it for feeling cheap.

### What a dungeon heart holds (Q52)

A keeper with no treasury tops out at **16000 gold** (owner, 2026-08-30).
`MaxGoldPerDungeonHeartTile` is 1000, so **16 tiles of a heart store gold**. A
heart is 5×5, which is 25 tiles, and 25 − 16 = 9 = 3×3 — the raised centre the
heart structure itself stands on. A screenshot of the original shows exactly that:
a 3×3 platform with the heart on it, ringed by 16 floor tiles with gold on them.

**Reproduced (ADR-0027).** `GoldStore.Capacity` asks the room *instance* whether
the tile is `IsCentrePlatform`, found from the bounding box's centre, so it needs
nothing from `RoomDef.Construction` (`FiveByFiveRotated`, which we do not read)
and a half-destroyed heart degrades sensibly rather than by a hardcoded 16.
`GoldOnRealDataTests.AKeepersHeartHoldsExactlySixteenThousand` reads the real
campaign heart; `TheHeartOnTheRealMapIsOneFiveByFiveInstance` checks the flood
fill has not split it, so the capacity cannot pass for the wrong reason. The
tile-only `Capacity(GameDatabase, in TileState)` overload cannot see room shape
and over-reports a heart by 9000; it survives for the one caller with a tile and
no map — the spill when a tile stops being a store — where a platform tile can
never have had gold to spill.

---

## What a room does

A room is an instance: a connected group of tiles with the same room terrain and
the same owner, derived on demand by `RoomIndex` and never stored (ADR-0027).
`RoomInstance` answers size, bounding box, rim (`EdgeTileCount`), interior and
`IsFunctional` — `RecommendedSizeX/Y` being the size below which a room does
nothing (Q45), measured against the bounding box so a wide enough L-shape counts;
which the original does is UNKNOWN, and the Prison contradicts the reading (Q179).

Rooms with behaviour of their own, each with its own note: Lair (`LairRules`,
`lair-acquisition.md`), Hatchery (`HatcheryRules`, `hatchery.md`), Library
(`ResearchRules`, `library-and-research.md`), Training Room (`TrainingRules`,
`training.md`), Prison (`PrisonRules`, `prison.md`), Portal (`PortalRules`,
`portals.md`). Treasury and Dungeon Heart hold gold, above. The rest are inert
terrain that a player owns, and none of them can be guessed from the ROOMS record,
which carries art, size and cost and nothing about behaviour. A rival takes a room
**whole** rather than tile by tile — `TileWorkKind.ConvertRoom` at
`ConvertRoomHealth`'s −4000, and the gold changes hands with it (ADR-0044;
evidence in `portals.md`).

### What is not implemented, and is not guessed at

Read this before adding to `RoomRules`. **Rooms have no shape:** `Construction` and
the `HasWalls` / `SpecialTiles` / `SpecialWalls` flags describe how a room meshes
itself together and grows walls — presentation and adjacency, none of it read. A
1x1 temple is buildable here and in the original (Q36), so this is a rendering gap
rather than a rule gap. **Rooms are not built or sold over time:** in DK2 an imp
walks to the tile and constructs it, and walks over to dismantle it; both are
instant here. **A room's `ObjectIds` are placed by nothing** — the list is read,
`ObjectCarryRules` using it to decide which room an object belongs in, but no rule
furnishes a newly built room with the objects the record names.

**Gold a mining imp cannot carry is lost. UNKNOWN (Q138).** In the original the
imp keeps mining and a lump grows on the floor in front of it. Here the imp keeps
mining — that half is DK2's — but the surplus is reported as `gold_uncollected`
and dropped, because whether the original leaves it on the floor has not been
read. Keeping mining rather than standing still is the important half: a keeper
with no treasury still gets their seams dug out. **An imp with a full treasury
keeps its load** and is sent to look for other work still carrying it (Q140,
UNKNOWN).

---

## Selling

**CONFIRMED, and the whole of it is data** (Q34, ADR-0029).

| what | where it comes from | shipped value |
|---|---|---|
| the standard rate | `RoomSellValuePercentageOfCost` | **50** |
| the per-room gate | `RoomDef.ReturnPercentage` | **100** buildable, **0** hero gates |
| what a tile cost | `RoomDef.Cost`, per tile (Q32) | 200 treasury … 3000 temple |
| what a tile becomes | `TerrainDef.DestroyedTypeTerrainId` | 8, Claimed Path, for every room terrain |
| doors and traps | `DoorSellValuePercentageOfCost`, `TrapSellValuePercentageOfCost` | **50** each |

So a treasury tile returns 100 of its 200 and a temple tile 1500 of its 3000,
exactly the 50 % per tile the owner measured in the original. **Trap:
`RoomSellValuePercentageOfCost` is 50, not 100** — a mistranscription had it at
100 for four sessions and nearly produced an override for a number that was never
missing. Check `game-database-dump.md` before recording a number as absent.

**`ReturnPercentage` is a gate, not a rate.** It takes two values across all 26
rooms — 100 and 0 — and the zero rows are **precisely** the hero gates, asserted in
both directions by
`RoomsOnRealDataTests.ReturnPercentageIsZeroForExactlyTheRoomsAKeeperCannotSell`
so that "the field is always 100" cannot fit the same evidence. Whether it
multiplies the global rate or replaces it is **indistinguishable on shipped
data**, no room carrying a middle value; `RoomRules.RefundPerTile` multiplies,
per-room gate first, and every shipped room divides exactly either way. Sellable
means `Buildable`, so `RoomRules.CanSell` names no room: on shipped data it admits
the 17 rooms a keeper builds and refuses the portals, hearts and hero gates.

**Gold on a sold treasury tile: CONFIRMED and reproduced** (owner, 2026-09-03;
binary, 2026-09-05). The tile reverts to claimed floor and the gold drops onto that
same tile as a pile, coming off the keeper's spendable total until somebody fetches
it. `RoomRules.Unmake` returns the displaced amount, `RoomSellSystem` drops it and
emits `gold_displaced`, and an imp can fetch it (`ImpTaskKind.FetchGold`). The pile
is over `MaxGoldPileOutsideTreasury` deliberately — that variable is not a cap
(Q65). A refund with nowhere to go also lands on the floor, at the last tile sold,
and emits `refund_overflowed`; the original's add-gold-to-player takes a position
and makes a pile there once every store has refused, but **which** tile is ours,
not a reading (Q139).

### Bridges: what is underneath

**CONFIRMED** (owner, 2026-09-03): a sold bridge **reverts to the water or lava it
spanned**, not to floor. The terrain table cannot say that on its own —
`DestroyedTypeTerrainId` is Claimed Path for all 26 room terrains, bridges
included — so the hazard is recorded per tile (ADR-0031).

**`TileState.Flags`, the MAP record's third byte per tile, marks a bridge over
lava.** Swept across all **66** shipped level maps:

| claim | result |
|---|---|
| what values does the byte take? | **only 1 or 2**, anywhere in the corpus |
| where does 2 occur? | **only on bridge terrain** — never on the other 38 types |
| what does a `2` bridge tile border? | **lava**, every time, all three bridge types |
| what does a `1` bridge tile border? | **water**, whenever it touches a hazard at all |

The two sets never cross. `BridgeRules` holds the reading (`SpansWater` = 1,
`SpansLava` = 2) and `BridgesOnRealDataTests` pins every row. A bridge cannot be
built on land — neither bridge carries `PlaceableOnLand` — so for a bridge tile the
byte is a straight binary. **The revert is CONFIRMED; the storage is only STRONGLY
INFERRED:** that *this byte* is where the original keeps it is an inference from a
clean partition, and could be a rendering hint that correlates perfectly. Nothing
depends on the original's storage, only on the revert being correct.

**Trap: count what a tile borders, not what a level contains.** The first pass
compared each level's *remaining* water and lava counts against its bridges and
looked like a refutation, because **a fully bridged pond leaves no water to
count** — the bridge tiles *are* the pond.

`BridgeRules.RevertsTo` is the only thing that decides what a removed tile leaves,
and every caller uses it: selling, burning, and the destroyed path. **A
player-built bridge writes its own marker**, the level file speaking only for
bridges it shipped with; selling clears it again, so a tile bridged twice cannot
inherit the first hazard.

**A rival does not beat a bridge down — it claims it.** CONFIRMED (owner,
2026-09-03), BUILT (ADR-0033, Q68): a bridge is only ever taken by a rival
keeper's imps jumping on it and claiming it, adjacent to their own territory, and
it does not go neutral first, so `TileWorkKind.Claim` applies to any non-solid
tile that is not already yours. A claimed burning bridge keeps burning.

### A wooden bridge over lava burns

**CONFIRMED** (owner, 2026-09-03; Q67, ADR-0032). `WoodBridgeLifeOnLavaSeconds` is
**30**, and the detail that made it implementable is that **the clock starts when
the bridge is built** — not on the first footstep, and not as damage to the tile's
health. The variable's name fits all three readings equally well; only an
observation picks one. Implemented as a countdown of its own
(`TileState.BurnTicksRemaining`, `BridgeBurnSystem`) rather than as health decay,
because health is already what a rival attacking the bridge reduces, and a wooden
bridge over **water** has identical health and never burns.

**Which bridge burns is read from the room type**, and it is the only rule in the
bridge code that names one. DK2's own variable names it — `WoodBridge…` — and the
terrain table genuinely cannot help: Wooden Bridge and Stone Bridge carry
identical flags and identical health, and neither carries `Decay`. Asserted
against the install both ways.

**What happens to what is standing on it is CONFIRMED and only partly built.**
Everything on the tile falls into the lava; a dead body disappears; a living
creature burns until it gets out or its health reaches zero; some creatures are
immune. Lava's harm is not a bridge rule — it is `TerrainDef.Damage` at 100 a
second, the same field that makes a Hero Lair heal at −67, and it is
`CreatureHealthRules.TerrainHealthPerSecond`. Its cadence is ours and UNKNOWN:
per second or per tick (Q118). A creature caught on a burning bridge is **not
stranded**: the pathfinder enters a mover's starting tile without testing
walkability, so it can walk out of the lava.

**The immunity is in the data and it extends the owner's list.** `CanWalkOnLava`
falls on exactly **seven** creatures: the Salamander and Giant they named, the two
unique variants, plus Firefly, Fairy and Horny — two fliers and the Horned Reaper,
exactly the set one would guess, so it corroborates a partial list rather than
contradicting it. The **Imp is not immune**, which is what makes a cheap wooden
bridge a real trap.

**What happens to gold on the tile is UNKNOWN** (Q67): everything on a burning
bridge falls into the lava, so the question is whether the gold falls with it or a
pile survives on whatever the tile becomes. `BridgeBurnSystem` zeroes the tile's
gold and reports `gold_lost`.
