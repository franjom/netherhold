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
| `Cost` is per tile, and the ROOMS record's number is the price | **CONFIRMED** (Q32) — five rooms priced in the original, all five matching the record |
| Which tile of a treasury a deposit lands in | **STRONGLY INFERRED** (Q35) — the tile the carrier walked to, spilling into the next |
| Whether a new room tile must touch the keeper's existing land | **UNKNOWN** (Q33) |
| What a room *does*, beyond being owned terrain | **NOT IMPLEMENTED** — only the treasury and the heart do anything, because only they have a variable |
| Selling a room | **NOT IMPLEMENTED** (Q34) — two candidate refund numbers and no way to tell which applies |
| Where mined gold goes | **CONFIRMED** (Q31) — into the mining imp's hands, then carried to a treasury or heart tile |
| How much an imp carries | **CONFIRMED** — the CREATURES record's `maxGoldHeld`; the Imp holds 1400 |
| Where an imp stops mining to go and unload | **STRONGLY INFERRED** (Q50) — when one more swing would not fit, which the shipped numbers make five swings and 1363 gold |
| A dungeon heart stores 16000 | **CONFIRMED, NOT IMPLEMENTED** (Q52) — 16 of its 25 tiles hold gold; ours stores 25000 |
| A floor lump when there is nowhere to store gold | **NOT IMPLEMENTED** (Q31) — lumps are OBJECTS, M6; the imp keeps mining and the overflow is lost |

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

### Three of the four places exist

Treasury and heart tiles are implemented, because a variable says how much each
holds and a tile can carry the number.

**A creature's hands are implemented too**, as `CreatureState.GoldHeld`, bounded
by that creature's `maxGoldHeld`. Adding it was not a modelling choice so much as
the only way to reproduce the confirmed behaviour: DK2's gold does not move from
a seam to a treasury, it moves from a seam to an imp and then from an imp to a
treasury, and an imp that is walking home is a keeper's gold being *somewhere*
for a few seconds. That is worth stating plainly because it has a consequence
nobody would guess from a spreadsheet: **a keeper's spendable total dips while
their imps are in transit**, and it is not a rounding artefact, it is where the
gold is.

Floor piles are **not**. A pile is an OBJECT, and the OBJECTS record is not
parsed until M6; modelling a pile as tile state instead would be inventing a
representation, not implementing a mechanic. So gold that will not fit is
*refused* — `GoldStore.Deposit` returns what it accepted, and every caller
reports the shortfall. A scenario that gives a keeper gold with no treasury says
so in its report.

The same rule catches the other end. A treasury tile that is destroyed becomes
claimed floor, which holds nothing, so `TileWorkRules` reports the gold as
`GoldSpilled` and `TileWorkSystem` emits `gold_spilled`. In DK2 that gold
scatters as piles; here it is lost, loudly.

---

## The round trip

**CONFIRMED (Q31, owner observation of the original.)** If the keeper has room in
a treasury or a dungeon heart, the mining imp takes the gold into its own hands
up to `maxGoldHeld`, runs to the drop spot and adds it to the store. If there is
nowhere to put it, the imp keeps mining and a lump grows on the floor in front of
it.

Two things follow that are worth stating because they are easy to get wrong:

**Carrying is the tail of a mining job, not a job.** An imp does not choose
between "mine" and "carry" — it mines, and carrying is what happens next. That is
why `CreatureActivity.DeliveringGold` is a state and there is no `JobPriority`
entry for it, and it is the one place the confirmed priority order (ADR-0018)
deliberately does not apply.

**A pile is the fallback, not the first step.** The obvious wrong model — mine,
drop on the floor, have somebody collect it later — is not what the original
does, and it would have made floor lumps a prerequisite for any gold economy at
all. They are not: they are what happens when the economy is full.

### Where an imp decides to stop mining

Was the one part of this that was HYPOTHESIS (Q50). The rule implemented is *one
more swing would free more gold than I can hold*, which is the only stopping
point that neither wastes gold nor overfills a pair of hands. It now has a reason
as well as a justification - see below.

The shipped numbers are worth laying out, because one of them is a surprise:

```
a gold seam        660 health, 3000 gold
MineGoldHealth     -300 per SECOND, at five applications a second (Q47)
  -> a swing takes 60 health and frees 273 gold, measured in the original
  -> a seam is 11 swings and yields exactly 3000
Imp maxGoldHeld    1400
  -> five swings fill it (1363), so a seam is three trips: 1363, 1364, 273
```

1363 against 1400 was a near-miss, and near-misses in shipped data usually mean
something. **They did here, and the something turned out to be Q47.** The owner
then measured 273 gold a swing, which put a swing at 60 health and an application
at a fifth of the tabled delta (ADR-0020) - and 1400 is **five** swings of 273,
which is 1363. So an imp fills after five swings and a seam of eleven swings is
exactly three trips: 1363, 1364, 273.

That also answered Q50, which had asked where an imp decides to turn round, by
arithmetic rather than by the rule guessed for it. The rule was right; it now has
a reason.

### Which tile the load lands in (Q35)

The owner's answer was *the nearest tile, spilling into another empty treasury
tile*, flagged as belief rather than observation. What is implemented is not a
rule at all, which is why it is now STRONGLY INFERRED rather than a guess: the
imp walks to the nearest tile with room, puts down as much as fits, and if it is
still carrying anything it looks again. Spillover is the consequence, not a
clause. `GoldStore.DepositInto` — one tile, the carrier's — is what makes that
possible, and `GoldStore.Deposit`'s tile-index order survives only for the two
cases with no carrier in them: paying for a room, and refunds.

On the shipped level 1 that produces exactly the original's behaviour: 1000 into
one heart tile, 363 into the next, and back to the seam. See
`scenarios/imps/an_imp_carries_gold_home.yaml`.

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


### What a room costs (Q32) — **confirmed from the game**

The owner priced five rooms in the original (2026-08-30) and every one matches
the ROOMS record exactly:

```
              original   record
Treasury          200      200
Lair              300      300
Hatchery          300      300
Library           600      600
Training Room     500      500
```

— and the owner stated they are **per tile**, which is what the implementation
already assumed. Nothing changed; what changed is the evidence. `Cost` could have
been per room, or a base that a room's `RecommendedSize` modified, and neither
would have shown up until somebody built a big room. Five independent matches is
a different kind of claim from one plausible reading, and
`RoomsOnRealDataTests.ARoomCostsWhatTheOriginalChargesForIt_PerTile` holds it.

Note the three at 3×3 — Hatchery, Library, Training Room. That is
`RecommendedSize`, which does **not** constrain building (Q36) and probably does
describe the size at which a room starts to function (Q45).

### What a dungeon heart holds (Q52) — **confirmed, and we are wrong**

A keeper with no treasury tops out at **16000 gold** (owner, 2026-08-30).
`MaxGoldPerDungeonHeartTile` is 1000, so **16 tiles of a heart store gold**. A
heart is 5×5, which is 25 tiles, and 25 − 16 = 9 = 3×3 — the raised centre the
heart structure itself stands on. A screenshot of the original shows exactly
that: a 3×3 platform with the heart on it, ringed by 16 floor tiles with gold
piled on them.

**Ours stores 25000**, because a room here is terrain tile by tile with no notion
of an instance or an origin, so nothing can ask which of 25 tiles is a middle one.
`RoomDef.TileConstruction` is `FiveByFiveRotated` for the heart and is unread; it
is the field that would answer it. This lands with room instances and room shape,
alongside Q36 and Q45. Recorded with a test that asserts the arithmetic of the
gap rather than left as a comment.


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

**Rooms are sold** (ADR-0029), and every number in it came from the data.
`RoomSellValuePercentageOfCost` is **50**, the owner independently observed a
50% refund per tile, and `RoomDef.ReturnPercentage` is a per-room gate on top of
it: 100 for every buildable room and **0 for exactly the hero gates**, which a
keeper cannot sell. Sellable means buildable, so the rules name no room. See
*Selling* below.

**The objects a room places on its tiles** (`RoomDef.ObjectIds`) are not read.
M6.

**Gold that will not fit in an imp's hands is lost.** In the original the imp
keeps mining and a lump grows on the floor in front of it, up to
`MaxGoldPileOutsideTreasury` (2000), and somebody can come back for it. Here the
imp keeps mining — that half is DK2's — but the overflow is reported as
`gold_uncollected` and dropped, because a lump is an OBJECT (M6). The decision to
keep mining rather than stand still is the important half: a keeper with no
treasury still gets their seams dug out, and simply keeps only what one pair of
hands holds. `TheFloorPileLimitIsInTheDataAndIsReadByNothing` exists so that this
stays a deliberate gap rather than a forgotten one.

**Nobody picks up gold somebody else dropped.** The same missing OBJECT record,
plus the missing job: an imp in DK2 will collect a lump left by a dead creature
or a destroyed treasury, and there is nothing here for it to collect.

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
   the game — the only per-creature number that looks like a carrying capacity
   rather than a wallet. It is now what decides how often an imp breaks off
   mining, and the number turned out to be more interesting than it looked:
   see below.
4. The owner then settled Q31 from the original in one sentence — the imp mines
   into its own hands, runs to the drop spot, and only piles gold on the floor
   when there is nowhere to put it. Two days of speculation about crediting
   players, ended by asking. This is step 0 of the research method for a reason.
5. The room→terrain link was checked both ways against the install: every room's
   terrain is flagged `Room`, and no two rooms share a terrain id. Both are
   asserted by `RoomsOnRealDataTests`, because `GameDatabase.RoomsByTerrain` is a
   dictionary and would silently lose a room if either failed.

---

## What does not fit

**The treasury costs the same as a wooden bridge.** 200 apiece, where a lair is
300 and a temple 3000. Nothing wrong with it — just worth noting before anyone
"corrects" the treasury's cost on the grounds that it feels too cheap for the
room a keeper cannot start without.

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
which is exactly the 50% per tile the owner measured in the original.

**This question was answered wrongly for four sessions and it was our fault.**
The refund was recorded as being in *neither* candidate variable, on the strength
of a `RoomSellValuePercentageOfCost` transcribed as 100 — while
`game-database-dump.md`, generated from the install by this project's own tools,
said 50 throughout. The plan that followed was to write an override for a number
that was never missing. The lesson is in `HANDOFF.md`: check the dump before
recording a number as absent.

**`ReturnPercentage` is a gate, not a rate.** It takes two values across all 26
rooms — 100 and 0 — and the zero rows are **precisely** the hero gates, the rooms
a keeper cannot sell. That coincidence is what makes it more than a guess, and it
is asserted in both directions so "the field is always 100" cannot fit the same
evidence. Whether it multiplies the global rate or replaces it is
**indistinguishable on shipped data**, since no room carries a middle value; the
code multiplies, which is the reading that still behaves sensibly on data we do
not have.

**What happens to gold on a sold treasury tile is CONFIRMED** (owner,
2026-09-03): the tile reverts to claimed floor and the gold **drops onto that
same tile as a pile**, coming off the keeper's spendable total until somebody
fetches it. Two thirds of that is reproduced — the tile reverts, and the total
falls by exactly that much, so the *economy* of a sale is right. The pile is
absent because a pile is an OBJECT (M6, Q31), so a keeper here loses permanently
what a keeper there picks back up. Reported as `gold_displaced`, never hidden.

**Two things are deliberately still missing:**

- **Selling is instant.** In DK2 an imp walks over and dismantles the tile, the
  same missing worker job as building (Q24).
- **A pile may not hold what a treasury tile held.** `MaxGoldPerTreasuryTile` is
  3000 and `MaxGoldPileOutsideTreasury` is 2000, so selling a full tile has 1000
  more gold than a pile can take. What happens to the surplus is UNKNOWN and only
  became askable once selling existed (Q65).

### Bridges: what is underneath

**CONFIRMED** (owner, 2026-09-03): a sold bridge **reverts to the water or lava
it spanned**, not to floor. The terrain table cannot say that on its own —
`DestroyedTypeTerrainId` is Claimed Path for all 26 room terrains, bridges
included — so the hazard is recorded per tile, and it is recorded in a byte this
project had been carrying since M2 without a meaning.

**`TileState.Flags`, the MAP record's third byte per tile, marks a bridge over
lava.** Swept across all **66** shipped level maps:

| claim | result |
|---|---|
| what values does the byte take? | **only 1 or 2**, anywhere in the corpus |
| where does 2 occur? | **only on bridge terrain** — never on the other 38 types |
| what does a `2` bridge tile border? | **lava**, every time, all three bridge types |
| what does a `1` bridge tile border? | **water**, whenever it touches a hazard at all |

The two sets never cross. `BridgeRules` holds the reading and
`BridgesOnRealDataTests` pins every row of that table.

**STRONGLY INFERRED, not CONFIRMED** — and the split matters. The *behaviour* is
confirmed. That *this byte* is where the original keeps it is an inference from a
clean partition, and it could in principle be a rendering hint that correlates
perfectly. Nothing depends on being right about the original's storage, only on
the revert being correct; the project's brief puts internal representation
outside what has to match.

A bridge cannot be built on land — neither bridge carries `PlaceableOnLand` — so
for a bridge tile the byte is a straight binary rather than one reading among
several.

**The near-miss.** The first pass at this compared each level's *remaining* water
and lava counts against its bridges and looked like a refutation: Furnace carries
bridges of both markers and, apparently, no water whatsoever. The reason is that
**a fully bridged pond leaves no water to count** — the bridge tiles *are* the
pond. Counting hazards nearly killed a correct reading. Asking what each tile
**borders** is what made the partition visible, and it is the lesson to carry into
the next map-data question.

**One rule, one place.** `BridgeRules.RevertsTo` is the only thing that decides
what a removed tile leaves, and both callers use it — selling, and being beaten
down by a rival (`AttackTile` already applies to a bridge tile, since it is
non-solid ownable terrain). Those two used to consult the destroyed-type field
separately and were wrong in the same way.

**A player-built bridge writes its own marker**, since the level file speaks only
for bridges it shipped with; selling clears it again so a tile bridged twice
cannot inherit the first hazard.

**What this unlocked.** `WoodBridgeLifeOnLavaSeconds` is 30: a wooden bridge over
lava burns away. That rule needs to know which bridges are over lava and nothing
here could tell before. Still not implemented, because the shape of the countdown
is UNKNOWN — Q67.

### A wooden bridge over lava burns

**CONFIRMED** (owner, 2026-09-03). `WoodBridgeLifeOnLavaSeconds` is **30**, and
the detail that made it implementable is that **the clock starts when the bridge
is built** — not on the first footstep, and not as damage to the tile's health.
The variable's name fits all three readings equally well; only an observation
picks one.

Implemented as a countdown of its own (`TileState.BurnTicksRemaining`,
`BridgeBurnSystem`) rather than as health decay, because health is already what
a rival attacking the bridge reduces, and a wooden bridge over **water** has
identical health and never burns.

**Which bridge burns is read from the room type**, and it is the only rule in the
bridge code that names one. DK2's own variable names it — `WoodBridge…` — and the
terrain table genuinely cannot help: Wooden Bridge and Stone Bridge carry
identical flags and identical health, and neither carries `Decay`. Asserted
against the install both ways.

**The larger half is confirmed and not implemented.** Everything on the tile
falls into the lava; a dead body disappears; a living creature burns until it
gets out or its health reaches zero; some creatures are immune. None of that is
here because **creature health does not exist**, and lava's harm is not a bridge
rule in any case — it is `TerrainDef.Damage` at 100 a second, the same field that
makes a Hero Lair heal at −67, so it belongs with creature health and terrain
damage generally.

**The immunity was already in the data and it extends the owner's list.**
`CanWalkOnLava` falls on exactly **seven** creatures: the Salamander and Giant
they named, the two unique variants, plus Firefly, Fairy and Horny. Two fliers
and the Horned Reaper is exactly the set one would guess, so it corroborates a
partial list rather than contradicting it. The **Imp is not immune**, which is
what makes a cheap wooden bridge a real trap.

A creature caught on a burning bridge is unharmed here but **not stranded**: the
pathfinder enters a mover's starting tile without testing walkability, so it can
walk out of the lava. That rule predates anything that could turn the ground
under a creature impassable, and this is its first real cause.

**What happens to gold on the tile is UNKNOWN and not worth an observation yet** —
a pile is an OBJECT (M6), so the answer could not be modelled either way. Q67.
