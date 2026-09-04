# Portals — where creatures come from

Status: **implemented** (`PortalRules`, `PortalSystem`, ADR-0042).
Confidence: mixed and split below, deliberately. Every *number* is CONFIRMED
from the shipped v1.70 data. How the numbers *compose* is STRONGLY INFERRED, and
most of that inference comes from OpenKeeper rather than from the files.

The portal was built first among rooms for a structural reason, not a
sentimental one: **nothing else could have occupants.** THINGS is unparsed so no
level's own creature roster loads, and `ImpTaskSystem` skips anything without
`IsWorker`, so a non-worker dropped into the world stood still forever. Every
other room's behaviour needs creatures in it. The portal's behaviour *is*
producing them — and it needs no creature health, hunger, mood or experience,
none of which exists.

---

## What the shipped data says (CONFIRMED)

| Fact | Where |
|---|---|
| A portal is 3x3, costs 0, and is **not buildable** | `Rooms.kwd` id 3: flags `0x1004` = PlaceableOnLand + HasFlame, and nothing else. No Buildable, no Attackable, no HasWalls |
| Its construction is `ThreeByThree` | and it and Hero Portal (25) are the **only two** rooms in the game that are |
| Its terrain is 12, `ownable, room`, 10000 health | `Terrain.kwd` |
| A creature arrives every `EntranceGenerationSpeedSeconds` | 25 game-wide, **18 on level 1** — one of the 26 ids a level overrides (ADR-0040) |
| One portal supports 15 creatures, each extra one 5 more | `CreaturesSupportedByFirstPortal` = 15, `CreaturesSupportedPerAdditionalPortal` = 5. The 1999 manual states the same rule in prose (p.22) |
| Who may enter is per level and per player | `CreaturePool` — level 1's entire pool is `creature 7 = 15, player 3` |
| Whether they are unlocked is per level and per player | `Availability` kind 2 (CREATURE) — level 1 unlocks exactly one creature |
| What attracts them | `CreatureAttraction` at CREATURES 0x0DAC, **two** slots of `(u32 present, u16 roomId, u16 roomSize)` |
| Sacking shifts a type's odds | `DecreaseEntrancePersentageOfLastSackedCreatureType` = 33, `IncreaseEntrancePersentageOfNewCreatureTypes` = 33 |

**Attraction, all 46 creatures** (`dk2tools kwd creatures -a`). Twenty-two
creatures have one slot, four have two, and twenty have none:

| Creature | Wants | And also |
|---|---|---|
| Goblin, Firefly | Lair, any size | |
| Warlock, Wizard | Library | |
| Dark Elf, Elven Archer | Guard Room | |
| Mistress | Torture Chamber | |
| Vampire, Monk | Graveyard | |
| Skeleton | Prison | |
| Troll | Workshop | |
| Salamander, Guard | Training Room | |
| Knight, Black Knight | Combat Pit | |
| Thief, Rogue | Casino | |
| Fairy, **Dark Angel** | Temple, **any size** | |
| **Bile Demon**, **Giant** | Workshop | **Hatchery ≥ 25 tiles** |
| **Royal Guard** | Torture Chamber | **Training Room ≥ 25 tiles** |
| **Maiden Of The Nest** | Library | **Temple ≥ 9 tiles** |
| Imp, Dwarf, Horny, every Lord, every unique | nothing | |

Two consequences worth having in front of you:

- **Level 1 produces nothing until its keeper builds a lair.** The Goblin's only
  attraction is a Lair. That is what makes the tutorial's order — dig, claim,
  lair, *then* creatures — a mechanic rather than a script.
- **The Dark Angel's temple has no minimum in v1.70**, against the owner's
  recollection *and* the 1999 manual, both of which say 5x5. Four creatures do
  carry a real minimum, so the mechanism can express one; it simply does not
  here. Q61/Q80, still open.

---

## How they compose (STRONGLY INFERRED — this is where the risk is)

Four gates, all four checked every tick. The gates themselves follow from the
fields. What follows does not.

### 1. The interval grows with the dungeon — and nothing in the files says so

The requirement is `max(base, base × creatures ÷ 2)` ticks, so on level 1 the
first goblin takes 18 seconds, the fifth 36, the fifteenth 126. **This is the
single most consequential inference in the whole mechanic** and it comes
entirely from OpenKeeper's `CreatureSpawnSystem`. There is no
"generation slowdown" variable, no per-creature multiplier, nothing in the
balance table that expresses it.

It also completely changes the pace of a level. Without it, fifteen goblins
arrive in four and a half minutes; with it, in about fifteen — and level 1's own
`AverageTime` variable says 8 (minutes), which sits between the two and settles
nothing.

**It is unfalsifiable from the files and only observation can check it.** Q86: a
stopwatch on two consecutive arrivals at two different dungeon sizes does it in
one sitting.

### 2. The pool is a standing limit, not a draining quota

`CreaturePool` says `creature 7 = 15`. Under the implemented reading that means
"no more than fifteen goblins at once", so sacking one lets another in. Under
the alternative it means "fifteen goblins, ever".

Three things point at the standing reading:

- OpenKeeper compares the pool value against the keeper's *current* count.
- The manual says a sacked creature *"[is] replaced by a new creature"*, which a
  spent quota could not do.
- The pool is stored as one value per type, with nowhere in the file to record
  depletion.

Against it: the manual's *"multiplayer pools are finite"*, which is about
skirmish rather than campaign. **On level 1 the two readings are
indistinguishable**, because the pool (15) and the first portal's capacity (15)
coincide. Q87.

### 3. Two slots mean both, and the size means one room

A creature that fills both attraction slots needs **both** rooms — the Bile
Demon wants a Workshop *and* a 25-tile Hatchery. And the size is **one room's
tile count**, not the keeper's total: five 5-tile hatcheries do not add up to
25. Both are OpenKeeper's readings, and both are the reading under which the
second slot and the size field mean anything at all.

**Not gated on `RoomInstance.IsFunctional`**, and the data forces that: the
Maiden Of The Nest wants a Temple of 9 tiles while a Temple's own
`RecommendedSize` is 5x5 = 25, so gating on functional would make her own
requirement unsatisfiable. That leaves a genuine oddity — a 3x3 temple attracts
her while doing nothing as a temple (Q45) — recorded as **Q89** rather than
resolved.

### 4. The clock is per portal, counts up, and runs only while owned

Per portal because two portals produce on their own schedules and the second
raises the cap; OpenKeeper's class comment calls it *confirmed original
behaviour* that two portals with equal counters produce simultaneously.

**Counted up rather than down**, because the interval it is waiting for is not
fixed: a countdown set at the last spawn would freeze a requirement that has
since changed. Counting elapsed time against a requirement recomputed each tick
means a creature dying shortens the wait, and a portal blocked for want of a
lair **banks** its wait and produces the moment the lair appears. Both are
consequences worth knowing about; the second is asserted in
`PortalTests.BuildingTheLairLetsTheBankedWaitThrough`.

**Only while owned by a keeper**, so claiming a portal that has stood idle does
*not* hand the new owner a creature at once. UNKNOWN, and OpenKeeper does the
opposite — but by accident: it resets to Java's `Double.MIN_VALUE`, which is the
smallest *positive* double rather than the sentinel it reads as, so an
`elapsed - lastSpawn` of the whole game clock satisfies any interval. Q86.

### The quirk kept on purpose

The keeper's creature count is read **once per tick** and not updated between
that tick's portals, so two portals ripe on the same tick both produce — even
though the first arrival would have raised the required interval and might have
filled the last slot. A keeper with several portals can end a tick one or two
over their stated maximum. OpenKeeper documents this as original behaviour and
replicates it deliberately; so does this (fidelity priority 4, reproduce rather
than fix). `PortalTests.TwoPortalsRipeOnTheSameTickBothProduce`.

### Which creature, when several qualify

**Uniformly at random**, and this is a placeholder for something the data says
exists. `DecreaseEntrancePersentageOfLastSackedCreatureType` and
`IncreaseEntrancePersentageOfNewCreatureTypes` are 33 each, so DK2 carries a
*per-type percentage* that shifts as a keeper sacks and receives. That weighting
is not modelled and cannot be until sacking exists — there is no drop-creature
command. Uniform is what the weighting starts as before anything has moved it.
**Q90.**

This is also the **first consumer of `SimulationContext.Rng`**. The shared RNG
has been carried, hashed and snapshotted since M1 with nothing drawing from it.
The draw is made even when only one creature qualifies, so the stream advances
by exactly one step per arrival regardless of how many types were in the
running — otherwise the RNG position would become a function of the keeper's
rooms, and two runs differing only in a room built and sold would diverge
forever.

---

## The by-product: not every player is a keeper

Chasing "whose portal is this?" turned up a fact that has nothing to do with
portals and had been quietly assumed away everywhere: **DK2's player ids are
reserved.** See `PlayerIds`.

| id | meaning |
|---|---|
| 0 | unowned — unmined rock, undug dirt |
| 1 | the hero side |
| 2 | **neutral — this is what an unclaimed room belongs to** |
| 3–7 | keepers |

CONFIRMED from three directions, and only one of them is OpenKeeper's `Player`
constants. The campaign's own data says it twice: across all 66 levels **every
dungeon-heart tile belongs to a player between 3 and 7**, and **every creature
pool and creature-availability entry names a player between 3 and 7**. Level 1
shows all four at once — hero gate player 1, unclaimed portal player 2, keeper's
heart player 3, rock player 0. Asserted in
`PortalOnRealDataTests.TheReservedPlayerIdsAreWhatTheCampaignSays`.

**The obvious argument for it is wrong and worth recording as such.** "Unclaimed
things belong to player 0" fails twice: solid rock carries a *non-zero* owner
all over the campaign, because DK2 colours the walls bordering a keeper's
territory in that keeper's colour (`TerrainFlags.PlayerColouredWall` exists for
exactly that), so ownership of rock is a rendering fact rather than a claim about
who holds the tile — and an unclaimed *room* is player 2, not player 0.

**A latent problem this exposes, not fixed here.** `ImpSupplySystem` loops every
player in the world and would hand free imps to players 0, 1 and 2. It changes
nothing on shipped data — none of them has a dungeon heart, and
`ImpSupplyRules.HeartTileOf` returns none — but the rule is unstated where it
should be explicit. The rest of the test suite also uses player 1 as "a keeper"
and 2 as "a rival", which is now known to name the hero side and the neutral
player. **Q91**, with the fix named: gate on `PlayerIds.IsKeeper` and re-key the
fixtures. Left as its own change rather than bundled into the portal's, because
it touches five hundred tests that were written on the old convention.

---

## What a portal still cannot do

- **Be claimed.** Converting a neutral or rival room is a real DK2 job with its
  own balance value (`ConvertRoomHealth` = −4000) and there is **no
  `TileWorkKind` for it**. So an imp cannot take a portal, and a scenario that
  wants one says so by re-owning the tiles, which the harness reports as a
  shortfall. **Q92.**
- **Be sacked into.** Dropping a creature into a portal is how DK2 gets rid of
  one; there is no drop-creature command, so neither sacking nor its odds shift
  exists. Q90.
- **Stop a creature walking over its hole.** In the original the centre tile is
  not walkable — creatures go round the hole — and OpenKeeper models it as a
  per-room accessibility override. Here portal terrain is ordinary non-solid
  room terrain, so `WorldPathGrid` lets anything stand on all nine tiles. That
  makes spawning on the centre harmless here and would not be in the original.
  **Q88**, not fixed because fixing it means teaching the path grid about rooms.
- **Produce for the hero side.** Four shipped portals belong to player 1 and all
  eleven hero portals to player 2. Neither id is ever dealt a creature pool, so
  nothing comes out of any of them. What the original does with them is almost
  certainly level scripting rather than the portal clock. Q91.

## Evidence

- `PortalTests` — 29 tests on the transcribed fixture, most of them negative:
  every gate gets a test that it *stops* a creature and one that lifting it lets
  the creature through. A suite that only checked "a goblin eventually arrives"
  would pass against a portal that ignored the pool, the cap and the lair.
- `PortalOnRealDataTests` — 10 tests against the shipped install: the drift
  guard on the Portal and Goblin records, level 1 end to end on its own map, and
  the four campaign-wide claims the rules rest on (ThreeByThree is exactly the
  two portals; the reserved player ids; no level ever makes a creature merely
  RESEARCHABLE; no level lists the same creature twice in its pool).
- `scenarios/creatures/a_portal_fills_a_dungeon.yaml` — level 1's own map,
  portal and balance table, 90 seconds. Four goblins arrive, and the fourth
  waits 540 ticks where the first waited 360, which is the interval scaling
  visible in an event log.
