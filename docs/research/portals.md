# Portals — where creatures come from

| | |
|---|---|
| Source(s) | `Rooms.kwd`, `Terrain.kwd`, `CREATURES`, per-level `VARIABLES` (shipped v1.70); `DKII.EXE` v1.70 (`Entrance.cpp` unit, ADR-0046); owner observations 2026-09-04 and 2026-09-07; 1999 manual p.22; OpenKeeper `CreatureSpawnSystem` (GPLv3, facts only, `THIRD-PARTY-NOTICES.md`) |
| Confidence | Every *number* is CONFIRMED from the shipped data. The interval, its reset, the per-portal clock and the two rosters are CONFIRMED (binary plus owner). The pool's meaning is STRONGLY INFERRED; how attraction composes is CONFIRMED from the binary (§3) |
| Implementation | `PortalRules`, `Systems/PortalSystem.cs`; `TileState.EntranceIdleTicks` and `TileState.EntranceCreaturesGenerated` on the centre tile; `PortalRules.OnOwnerChanged`, called from `TileWorkRules.Convert`. ADR-0042, ADR-0047, ADR-0086 |
| Tests | `PortalTests` (38), `PortalOnRealDataTests` (16), `RoomConversionTests`, `RuinedHeartTests`; `scenarios/creatures/a_portal_fills_a_dungeon.yaml`, `scenarios/creatures/an_imp_claims_a_portal.yaml` |
| Open questions | Q87 (what a `CreaturePool` entry is a quota of), Q89 (attraction below `RecommendedSize`), Q90 (weighting), Q91 (what players 1 and 2 do), Q98 (a deliberate capacity divergence) |

Split by subject, because this note was three times its budget:

- `portals-interval.md` — the arrival interval: the six instructions, three timed runs, the reset.
- `portals-mercenary.md` — the Hero / Mercenary Portal: one implementation, two rosters.
- `portals-room-handler-table.md` — the per-room-type handler table at `0x006a1e9f`, its slots and its job ids (cited from `docs/DECISIONS.md` ADR-0088 and `job-selection.md`).

---

## What the shipped data says (CONFIRMED)

| Fact | Where |
|---|---|
| A portal is 3x3, costs 0, and is **not buildable** | `Rooms.kwd` id 3: flags `0x1004` = PlaceableOnLand + HasFlame, and nothing else. No Buildable, no Attackable, no HasWalls |
| Its construction is `ThreeByThree` | it and Hero Portal (25) are the **only two** rooms in the game that are |
| Its terrain is 12, `ownable, room`, 10000 health | `Terrain.kwd` |
| A creature arrives on a per-portal interval built on `EntranceGenerationSpeedSeconds` | 25 game-wide, and **five levels override it** — one of the 26 ids a level may (ADR-0040). level1 **18**, level2 **20**, level3 **22**, Level10 **30**, Secret1 **10000**. The first three ramp; Secret1's portal is switched off |
| One portal supports 15 creatures, each extra one 5 more | `CreaturesSupportedByFirstPortal` = 15, `CreaturesSupportedPerAdditionalPortal` = 5. The 1999 manual states the same rule in prose (p.22) |
| Who may enter is per level and per player | `CreaturePool` — level 1's entire pool is `creature 7 = 15, player 3` |
| Whether they are unlocked is per level and per player | `Availability` kind 2 (CREATURE) — level 1 unlocks exactly one creature |
| What attracts them | `CreatureAttraction` at CREATURES `0x0DAC`, **two** slots of `(u32 present, u16 roomId, u16 roomSize)` |
| Sacking was meant to shift a type's odds | `DecreaseEntrancePersentageOfLastSackedCreatureType` = 33, `IncreaseEntrancePersentageOfNewCreatureTypes` = 33. Both variables are **cut** in v1.70 — see §"Which creature, when several qualify" |

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
| Fairy, Dark Angel | Temple, any size | |
| Bile Demon, Giant | Workshop | Hatchery ≥ 25 tiles |
| Royal Guard | Torture Chamber | Training Room ≥ 25 tiles |
| Maiden Of The Nest | Library | Temple ≥ 9 tiles |
| Imp, Dwarf, Horny, every Lord, every unique | nothing | |

Two consequences:

- **Level 1 produces nothing until its keeper builds a lair.** The Goblin's only
  attraction is a Lair, which is what makes the tutorial's order — dig, claim,
  lair, *then* creatures — a mechanic rather than a script.
- **The Dark Angel's temple has no minimum in v1.70**, against the owner's
  recollection *and* the 1999 manual, both of which say 5x5. Four creatures do
  carry a real minimum, so the mechanism can express one; it simply does not
  here. Q61/Q80.

---

## How they compose (STRONGLY INFERRED — this is where the risk is)

**Four gates.** Three are re-checked at every roll inside `PortalSystem.Pick` —
the creature's own flags, the pool, attraction — and the fourth, capacity, is read
once per tick per keeper. **Availability is not one (ADR-0316):** `0x004DBF90` reads
the pool (`0x00506400`, remaining count non-zero), the flag bits, the attraction and the
capacity, never the creature table at `+0x4E/+0x52`. Level 3's trigger 79 disables the
Firefly, Goblin and Warlock at the start and its portal still produced them (1002, 1506, 1694, 2398).

### 1. The interval grows with the portal's own tally

`(n² + EntranceGenerationSpeedSeconds)` game seconds, `n` being how many
creatures **this portal** has generated. CONFIRMED at `0x004DBBE0` and by three
timed runs on the original. On level 1 (base 18) the first goblin takes 18 s,
the fifth 34, the fifteenth 243. `n` only ever rises; the sole reset is an
ownership change. Full evidence: `portals-interval.md`.

Not `max(base, base × the keeper's living non-workers ÷ 2)` — OpenKeeper's
`CreatureSpawnSystem`, which this project shipped for weeks and which the
stopwatch refuted three ways at once (shape, slope and what `n` counts). Level
1's own `AverageTime` variable is 8 (minutes) and settles nothing.

> **Owner, 2026-09-04, unprompted:** *"goblin interval, seems to be prolonging
> after each goblin arrives."*

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
coincide — two different fifteens. Q87.

### 3. Two slots mean both — and the size is the keeper's total, not one room

**Read from the binary 2026-09-14, and it splits OpenKeeper's two readings.**

**Both slots: CONFIRMED.** `FUN_004DC810` walks the creature definition's
counted list — `byte[def+0x788]` entries of **3 bytes** from `def+0x5DA`,
`{u8 param, u8 amount, u8 kind}`, fetched by `FUN_00494380` — and returns 0
on the first entry that fails, 1 only after all of them pass. Kind 0 is the room
test; kinds 1 and 2 compare the player's `+0x7E` and `+0x82` against the amount,
so the list carries prerequisites beyond rooms.

**One room: REFUTED.** Kind 0 calls `FUN_004C6EB0(player, roomId)`, which
**sums** `room+0xA7` — the slab count (`room-object.md`) — over every room
of that type and requires the sum to reach the amount. Five 5-tile hatcheries
**do** make the Bile Demon's 25. Owning **none** of the room is still refused
whatever the size asks. `PortalRules.IsAttracted` sums from 2026-09-14, and the
test that asserted the opposite was named for the guess it pinned.

**Not gated on `RoomInstance.IsFunctional`**, and the data forces that: the
Maiden Of The Nest wants a Temple of 9 tiles while a Temple's own
`RecommendedSize` is 5x5 = 25, so gating on functional would make her own
requirement unsatisfiable. That leaves a genuine oddity — a 3x3 temple attracts
her while doing nothing as a temple (Q45) — recorded as **Q89** rather than
resolved.

**A portal with no rooms produces nobody — CONFIRMED (owner, 2026-09-10).**
Level 20, five idle minutes, one owned portal, zero rooms built: nothing
arrived. Against a 25 s base that is about a dozen missed intervals, and the
game was demonstrably running, because the same capture shows the trigger clock
advancing 1182 ticks (`docs/experiments/level20-idle-001`). The obvious
confound is out — Level 20's pool is not empty; player 3 has six types, and all
six want a room: Bile Demon and Troll a Workshop, Mistress a Torture Chamber,
Warlock a Library, Dark Elf a Guard Room, Salamander a Training Room.

This matters because it is the first check of the composition *at zero*, and
because it contradicts a stated belief: the owner expected *"at least a goblin
will enter no matter your room layout"*. Level 20's pool has no Goblin in it at
all, so a Lair would not have produced one either. `PortalTests.BuildingThe`
`LairLetsTheBankedWaitThrough` already encodes the rule; this is the first time
the game was asked.

### 4. The clock is per portal, counts up, and runs only while owned

**Per portal, CONFIRMED by observation** (owner, 2026-09-07): a second portal
produced its first creature **8 seconds after** the first portal produced, at a
point where the required interval was about 60 seconds. One shared clock cannot
emit twice in 8 seconds while it is waiting 60. The *tally* is per portal too,
from a third timed run (`portals-interval.md`).

**Counted up rather than down**, because the interval it is waiting for is not
fixed: a countdown set at the last spawn would freeze a requirement that has
since changed. **The wait is not banked** (corrected 2026-10-01, ADR-0314,
Q505). This paragraph used to say a portal blocked for want of a lair produces
the moment the lair appears. That was a design choice, never a reading.
`0x004DBBE0` restamps the clock at every due roll, whatever the roll produced,
so a blocked portal waits for its next roll —
`PortalTests.BuildingTheLairWaitsForTheNextRoll`; `portals-interval.md`, *A due
roll that creates nothing*.

**Only while owned by a keeper.** `PortalSystem` skips any player failing
`PlayerIds.IsKeeper`, so the clock of an unclaimed portal (167 of the campaign's)
does not run and a keeper who claims one waits a full base interval. OpenKeeper
does the opposite, but by accident: it resets to Java's `Double.MIN_VALUE`,
which is the smallest *positive* double rather than the sentinel it reads as, so
an `elapsed - lastSpawn` of the whole game clock satisfies any interval.

### The quirk kept on purpose

The keeper's creature count is read **once per tick** and not updated between
that tick's portals, so two portals ripe on the same tick both produce — even
though the first arrival would have raised the required interval and might have
filled the last slot. A keeper with several portals can end a tick one or two
over their stated maximum. OpenKeeper documents this as original behaviour and
replicates it deliberately; so does this (fidelity priority 4, reproduce rather
than fix). `PortalTests.TwoPortalsRipeOnTheSameTickBothProduce`.

### Which creature, when several qualify

**Uniformly at random**, among candidates collected in creature-id order. The
draw is made even when only one creature qualifies, so the RNG advances by
exactly one step per arrival regardless of how many types were in the running —
otherwise the RNG position would become a function of the keeper's rooms, and
two runs differing only in a room built and sold would diverge forever. This is
the **first consumer of `SimulationContext.Rng`**.

The data names a weighting that v1.70 does not run.
`DecreaseEntrancePersentageOfLastSackedCreatureType` (`0x006F2910`) and
`IncreaseEntrancePersentageOfNewCreatureTypes` (`0x006F2912`) are 33 each, and a
raw scan finds **one hit each, both the loader's setter**: nothing reads either.
So the per-type percentage those names describe **is not in v1.70** — either it
is hardcoded inside the portal unit (`0x004DB000`–`0x004DC800`) or there is
none. Two more for the cut list, making fourteen. **Q90.** Modelling it would
also need sacking, and there is no drop-creature command.

The binary does carry *some* per-type value: the candidate builder fills a table
of nine bytes per creature holding a type id and a 12-bit fixed-point weight
built from the keeper's attracting rooms and decremented per creature held. That
read contradicts itself (it caps level 1 at one goblin) and nothing is
implemented from it — `binary-entrance.md` §4, withdrawn under Q97.

---

## The by-product: not every player is a keeper

Chasing "whose portal is this?" established that **DK2's player ids are
reserved.** See `PlayerIds` (ADR-0043).

| id | meaning |
|---|---|
| 0 | unowned — unmined rock, undug dirt |
| 1 | the hero side |
| 2 | **neutral — this is what an unclaimed room belongs to** |
| 3–7 | keepers |

CONFIRMED from three directions, only one of them OpenKeeper's `Player`
constants. The campaign's own data says it twice: across all 66 levels **every
dungeon-heart tile belongs to a player between 3 and 7**, and **every creature
pool and creature-availability entry names a player between 3 and 7**. Level 1
shows all four at once — hero gate player 1, unclaimed portal player 2, keeper's
heart player 3, rock player 0. Asserted in
`PortalOnRealDataTests.TheReservedPlayerIdsAreWhatTheCampaignSays`.

**Not "unclaimed things belong to player 0" — a trap.** Solid rock carries a
*non-zero* owner all over the campaign, because DK2 colours the walls bordering
a keeper's territory in that keeper's colour (`TerrainFlags.PlayerColouredWall`
exists for exactly that), so ownership of rock is a rendering fact rather than a
claim about who holds the tile. And an unclaimed *room* is player 2, not 0.

`ImpSupplySystem` used to loop every player in the world; it now gates on
`PlayerIds.IsKeeper` (ADR-0052). The free-imp deadline is hashed state, so it
had been running for players 0, 1 and 2 on every level — seven scenario hashes
moved when it was fixed, with no imp involved. Q91 keeps the remaining half:
`ManaSystem` still runs for every player, and *"only keepers accrue mana"* has
no source.

---

## What a portal still cannot do

- **Be sacked into by the hand.** Dropping a creature onto the hole removes it
  with its gold (`HandRules`, `creature_left` with `via=thrown_in`, Q88), but
  there is no drop-creature *command* and no odds shift (Q90).
- **Produce for the hero side.** Four shipped portals belong to player 1 and all
  eleven hero portals to player 2. Neither id is ever dealt a creature pool, so
  nothing comes out of any of them. What the original does with them is almost
  certainly level scripting rather than the portal clock. Q91.
- **Feed a rival's cap correctly in every case.** DK2 counts a portal toward the
  cap on room type alone; so does `PortalSystem`, before `CanProduce`. The one
  residual divergence is Q98's.

The centre tile **is not walkable**, here as in the original: `StructureBlocks`
blocks a portal's single centre and a heart's centre 3x3 through
`RoomInstance.IsCentrePlatform`, and `WorldPathGrid` takes it from there
(ADR-0084, `structure-blocks.md`, Q88). Both refuse a room narrower than 3
tiles, because blocking a 1x1 portal's only tile walled off a deserter.

---

## Claiming a portal — and any room somebody else holds

**CONFIRMED (owner, 2026-09-04, unprompted; ADR-0044, Q92):** *"once an imp
claims a portal then the entire portal gets claimed, all tiles that are assigned
to that portal are immediately claimed … that is a game mechanic that works on
claiming enemy/neutral rooms too, rooms are instantly claimed by imps, not tile
by tile."*

`TileWorkKind.ConvertRoom` reads `ConvertRoomHealth` = **−4000** (room health,
`tile-work.md`, Q500: 13 applications for one tile, about 12.5 per tile of the room).
`TileWorkRules.Apply` converts the worked tile and `TileWorkSystem` spreads the
change to the whole `RoomInstance`; the scenario log reports it as
`room_converted … tiles=9 spread=8`, one tile worked and eight coming with it.
The portal's clock starts at the claim, not before it (`OnOwnerChanged`).

The mixed direction is the data's: `ClaimTileHealth` is **+825** and builds floor
*up*, `ConvertRoomHealth` is **−4000** and beats a room *down*. Hence
`TileWorkRules.CompletesAsTakeover` — without it the downward path would send a
converted room to its `DestroyedTypeTerrainId` (Claimed Path, for every room in
the data) and claiming a treasury would demolish it. Demolishing is
`AttackRoomHealth`'s job (−1500) and still has no work kind.

Two consequences that fell out rather than being designed:

- **A worker takes a room by standing in it.** The stance rule asks what the
  tile will be when the job finishes, and a takeover leaves it alone (ADR-0028).
- **An imp holding gold takes a rival's adjacent treasury and then uses it.**
  The rule it might have broken is confirmed: *"imps fill only their own
  keeper's treasury … for SP my imps NEVER fill anyone's treasure but my own"*
  (owner). It holds structurally — `GoldStore`'s store lookup skips every room
  whose owner is not the depositor, and it is the only path into a tile's gold —
  and is asserted both as an ordering and as a per-tick invariant.

**Two traps this work exposed**, both fixed (ADR-0045):

- `Claim` had applied to room terrain all along, because room terrain is
  `Ownable` and not `Solid` and that is all it asked for. So rooms had been
  converting tile by tile at the wrong rate since rooms existed. `Claim` now
  excludes room terrain.
- `Claim` never asked `IsOwnable`, so a keeper digging through their own
  reinforced wall got Dirt Path stamped with their own id — neither claimable by
  them nor their own land, and territory could not grow through it. On level 1
  the only route out of the dungeon heart *is* the keeper's own wall, so nothing
  beyond it was reachable for 130 game-seconds of a test scenario. The stale
  justification was *"which owner id means nobody is UNKNOWN (Q17), so a stale
  byte on unownable terrain is inert"* — and Q17 was answered the same day
  (ADR-0043). A justification resting on an open question rots the day that
  question is answered elsewhere; `GOAL_ALIGNMENT.md`'s third review clause.

Two extrapolations from that one sentence about portals, both since CONFIRMED by
the owner: a **live** dungeon heart cannot be taken this way, though a defeated
keeper's **ruined** one can (Q93, `RuinedHeartTests`); and a multi-tile bridge
converts whole (Q94, `RoomConversionTests`).

---

## The Hero Portal is an entrance too, and one implementation serves both

Moved to `portals-mercenary.md` (cited from `open-questions.md`). In one line:
DK2 ships **one** portal implementation and runs it for room types 3 and 25,
separating them by **room type rather than owner** inside the shared code, so a
keeper's claimed Hero Portal produces the nine `AvailableViaHeroPortal`
creatures and nothing else. It is called the **Mercenary Portal** in the game.

---

## Capacity

`FUN_004DC760` walks the **player's own** room list twice, counting room type 3
and then room type `0x19`, and returns:

```
total = portals + heroPortals
capacity = total == 0 ? 0
         : first + (total - 1) * perAdditional
```

`PortalRules.CreatureCapacity` has that shape, counting both types.
`binary-entrance.md` §2 has the read; its "we count keeper portals only"
divergence is closed.

- **The comparison is strict**, and **nothing anywhere culls creatures**. So a
  keeper who loses a portal keeps every creature, drops below their new cap, and
  receives nothing until they are under it again — owner: *"creatures don't
  disappear if that happens."*
- **A portal counts however broken.** DK2's counter tests `room[0x18] == type`
  and nothing else — no size, no centre tile, no notion of the room working. So
  claiming a dead Hero Portal raises a keeper's creature limit, and a portal cut
  into two rooms by a rival taking its middle counts twice. Reproduced under
  fidelity priority 4; `PortalSystem` counts entrances before `CanProduce`.
- **`CreaturesSupportedPerAdditionalPortal` nearly went on the cut list.** A raw
  scan for `0x006F28B5` finds one hit, the loader's setter — the exact signature
  used fourteen times to call a variable cut. It is live: its byte sits
  immediately after `CreaturesSupportedByFirstPortal`'s, the capacity routine
  loads both in one dword and extracts with `MOV DL,CH`, so the second
  variable's address never appears in any instruction. **Any byte or word
  variable whose global is adjacent to another's can be read this way and will
  scan as dead.** A folded load reaches four bytes, so a dword variable can
  never be carried by a neighbour's load, and sweeping all 57 zero-reader globals
  for the shape leaves this case and no other
  (`balance-variables-in-the-binary.md`, 2026-09-07). Third kind of false
  negative, after ADR-0085's disabled-not-cut and a `.text`-only scan.

The room's own info string is a third confirmation, and the one source a player
could have checked all along — see `portals-mercenary.md`.

---

## Evidence

- `PortalTests` — 38 tests on the transcribed fixture, most of them negative:
  every gate gets a test that it *stops* a creature and one that lifting it lets
  the creature through. A suite that only checked "a goblin eventually arrives"
  would pass against a portal that ignored the pool, the cap and the lair.
- `PortalOnRealDataTests` — 16 tests against the shipped install: drift guards
  on the Portal and Goblin records, level 1 end to end on its own map, and the
  campaign-wide claims the rules rest on (ThreeByThree is exactly the two
  portals; every shipped portal is 3x3 terrain; the reserved player ids; no
  level ever makes a creature merely RESEARCHABLE; no level lists the same
  creature twice in its pool; no level pools or unlocks a unique creature while
  all thirteen uniques carry the portal flag; the nine hero-portal creatures are
  exactly the nine the Portal refuses; Frosty's Castle pools exactly those nine;
  a Hero Portal counts toward the cap).
- `scenarios/creatures/a_portal_fills_a_dungeon.yaml` — level 1's own map,
  portal and balance table.
- `scenarios/creatures/an_imp_claims_a_portal.yaml` — dig, claim, convert, first
  arrival, on level 1's real map from one dig order.

## Known exceptions

- Q98's capacity divergence, now reduced to what `CanProduce` guards.
- The weighting of §"Which creature" is uniform where the original may not be
  (Q90); the two variables that would express it are cut.
