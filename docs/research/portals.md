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

### 1. The interval grows with the dungeon — **CONFIRMED, and nothing in the files says so**

The requirement is `max(base, base × creatures ÷ 2)` ticks, so on level 1 the
first goblin takes 18 seconds, the fifth 36, the fifteenth 126. There is no
"generation slowdown" variable, no per-creature multiplier, nothing in the
balance table that expresses it — it came entirely from OpenKeeper's
`CreatureSpawnSystem`, and it was flagged here as the single most consequential
inference in the whole mechanic.

It also completely changes the pace of a level. Without it, fifteen goblins
arrive in four and a half minutes; with it, in about fifteen — and level 1's own
`AverageTime` variable says 8 (minutes), which sits between the two and settles
nothing.

> **Owner, 2026-09-04, unprompted:** *"goblin interval, seems to be prolonging
> after each goblin arrives."*

**That is the direction CONFIRMED**, and it is the answer to the thing this
document called unfalsifiable. The pace of every level now rests on an observed
behaviour rather than on somebody else's code.

**One line of somebody else's code, for the record.** The formula is not a
reconstruction — OpenKeeper states it outright, and its class comment
independently confirms the two-portals quirk reproduced in §4:

```java
// CreatureSpawnSystem.evaluateAndSpawnCreature, GPLv3 — quoted as evidence
timeSinceLastSpawn >= Math.max(entranceCooldownTime,
        entranceCooldownTime * player.getCreatureControl().getTypeCount() * 0.5)
```

`getTypeCount()` is documented on `PlayerCreatureControl` as *"Get player
creature count. Excluding imps."*, which is what `PortalRules.CreatureCount`
does by the `IsWorker` flag.

#### What is left of Q86 is the slope, and it is sharp

"It gets slower" does not measure `÷ 2`. On level 1 (base 18) the formula
predicts consecutive gaps of **18, 18, 27, 36, 45 seconds — exactly nine more
per goblin**, and nine is `base ÷ 2`. That is the falsifiable claim.

The owner's earlier ~30-second measurement is **consistent with the formula
rather than evidence for it**: 30 sits between the n=3 (27s) and n=4 (36s)
predictions. Two independent observations now agree with the shape, and neither
constrains the slope. What does: two consecutive gaps *with the goblins
counted*, or `Entrance.cpp` in the binary (ADR-0046).

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

---

## Update, later the same day: the portal is claimable, and two defects went with it

Q92 came back from the owner within the hour: *"once an imp claims a portal then
the entire portal gets claimed, all tiles that are assigned to that portal are
immediately claimed … that is a game mechanic that works on claiming
enemy/neutral rooms too, rooms are instantly claimed by imps, not tile by
tile."*

**CONFIRMED**, implemented as ADR-0044, and it changed the section above from "a
portal cannot be claimed" to the full chain working on level 1's real map with
one dig order:

```
t276-324  dig three tiles out of the keeper's own wall and the rock beyond
t343-396  claim the floor they opened
t465      room_converted: (21,28) player=3 Portal tiles=9 spread=8 from=2
t824      creature_entered: type=Goblin ... interval=360
```

`tiles=9 spread=8` is the mechanic in one line: one tile was worked, eight came
with it. And **t465 + 359 = t824**, which is the portal's clock starting at the
claim rather than before it — the decision recorded under
`TileState.EntranceIdleTicks`, now visible in an event log.

### What the mechanic is

`TileWorkKind.ConvertRoom`, reading `ConvertRoomHealth` = **−4000** (an entry
that had sat unused in `VariableTypeId`). Thirteen applications against a room
tile's 10000 health at five a second, so **2.6 seconds** — the "instantly" is
about the propagation, not the duration. `Claim` now excludes room terrain, which
is the fix: it had applied to rooms all along, because room terrain is `Ownable`
and not `Solid` and that is all it asked for. **So rooms had been converting tile
by tile at the wrong rate since rooms existed.**

The mixed direction is the data's: `ClaimTileHealth` is **+825** and builds floor
*up*, `ConvertRoomHealth` is **−4000** and beats a room *down*. Hence
`CompletesAsTakeover` — without it the downward path would send a converted room
to its `DestroyedTypeTerrainId` (Claimed Path, for every room in the data) and
claiming a treasury would demolish it. Demolishing is `AttackRoomHealth`'s job
(−1500) and still has no work kind.

Two things fell out rather than being written: **a worker takes a room by
standing in it** (the stance rule asks what the tile will be when the job
finishes, and a takeover leaves it alone — ADR-0028), and **an imp holding gold
takes a rival's adjacent treasury and then uses it.** That last one prompted the
owner to confirm the rule it might have broken: *"imps fill only their own
keeper's treasury … for SP my imps NEVER fill anyone's treasure but my own."* It
holds structurally — `GoldStore`'s store lookup skips every room whose owner is
not the depositor, and it is the only path into a tile's gold — and is now
asserted both as an ordering (the takeover precedes the first delivery) and as an
invariant over every tick of a run.

### The second defect, which was worse and unrelated

Implementing the first produced a scenario that dug from level 1's heart toward
its portal and **did nothing at all for 130 game-seconds.**

The owner byte survived every terrain conversion, on a recorded reason: *"which
owner id means 'nobody' is UNKNOWN (Q17), and every rule that cares asks
`IsOwnable` first, so a stale byte on unownable terrain is inert."* **Q17 was
answered the same day** (ADR-0043) and **`Claim` never asked `IsOwnable`** — so a
keeper who dug through their own reinforced wall got Dirt Path stamped with their
own id, which is neither claimable by them nor their own land. Territory could
not grow through it. On level 1 the only route out of the dungeon heart *is* the
keeper's own wall, so **nothing beyond it was ever reachable.** Fixed in
ADR-0045; the general lesson — a justification resting on an open question rots
the day that question is answered elsewhere — is `GOAL_ALIGNMENT.md`'s third
review clause.

### Still ours rather than the owner's

- **A dungeon heart cannot be taken this way** (Q93). Inferred from Q40: a heart
  is beaten down, and "rivals claim the rooms and gold" happens after it falls.
- **A multi-tile bridge converts whole** (Q94). A bridge is a room in every way
  the data can tell, but "one imp takes a whole ten-tile span" is a large
  consequence to draw from a sentence about portals. Pinned as behaviour so it is
  one line to change.
- **The rate.** Only the propagation is confirmed.

---

## The generation interval is quadratic, and our slope was OpenKeeper's guess

Read out of DKII.EXE v1.70 on 2026-09-07 (Q86, Q90). The portal's per-tick
routine begins at `0x004DBBE0`, and the arithmetic that decides when the next
creature is due is six instructions:

```
n        = room[0x4D]                            -- a per-ROOM counter
base     = EntranceGenerationSpeedSeconds        -- 25 game-wide, 18 on level 1
dueTicks = (n * n + base) * variable19 + room[0x49]
if (now >= dueTicks) <generate>
```

**So the interval is `(n² + base)` game seconds.** Not a multiple of the base —
an *addition* of n² seconds to it.

| n | DK2, level 1 | What this project had |
|---|---|---|
| 0 | 18 s | 18 s |
| 1 | 19 s | 18 s |
| 2 | 22 s | 18 s |
| 3 | **27 s** | **27 s** |
| 4 | 34 s | 36 s |
| 5 | 43 s | 45 s |
| 10 | 118 s | 90 s |

**The two agree at n = 3 and nowhere else**, which is worth knowing because that
is where the `a_portal_fills_a_dungeon` scenario happened to be looked at: it
logged `interval=360` three times and then `interval=540`, and 540 ticks is 27
seconds. A coincidence at exactly the point of inspection.

### What our formula was, and where it came from

`PortalRules.SpawnIntervalTicks` is `max(base, base × n / 2)`. Its own
documentation is candid about the provenance and is worth quoting, because it
called its own shot:

> *"Nothing in the balance table expresses a slowdown — there is no 'generation
> slowdown' variable — so this was **unfalsifiable from the files** and rested
> entirely on OpenKeeper [...] **The step is still ours.** [...] Q86 is now one
> question — is the step base/2? — and the cheap route to it is two consecutive
> gaps with the goblins counted, or `Entrance.cpp` in the binary."*

The binary route was taken and the answer is **no**. The slowdown is real — the
owner watched it and OpenKeeper was right that it exists — and the slope is
wrong.

### And `n` is not what we count either, which is the bigger difference

Ours counts **the keeper's non-worker creatures**. DK2's `room[0x4D]` is a
**per-room counter incremented once per creature generated** (`FUN_004DBF90`,
`INC dword ptr [EDI + 0x4D]` at `0x004DC5E3`).

Those differ in a way a player would feel: **ours speeds a portal back up when a
creature dies or deserts, and a generation counter only goes up.**

### Which is exactly what is NOT established — and it is observable

`room[0x4D]` is reset to zero by two routines, `FUN_004DBB90` and
`FUN_004DBBC0` (the second also stamps `room[0x49] = now`). **Neither has a
single direct caller or an absolute reference in `.text`**, so both are reached
through a vtable — the shape of a room's virtual `OnBuilt` / `OnClaimed` /
`Reset`. Plausible and unread.

**So the open question is whether a portal ever recovers**, and it is a question
for the game rather than the binary:

> Fill a dungeon until the portal is visibly slow, then let several creatures
> die or desert. **Does the portal speed back up?**
>
> - **Yes** → the counter tracks the population, and something resets or
>   decrements it that has not been found.
> - **No** → the counter only climbs, a portal is a resource that wears out, and
>   at n = 15 the interval is 243 seconds on level 1.

Nothing is built until that is answered, because implementing the confirmed
formula against an unknown reset risks a portal that grinds to a halt where the
original's recovers. The `n² + base` half is CONFIRMED and recorded; the counter's
lifetime is not.

### One scan later, the experiment above is the wrong one

The owner asked whether **throwing creatures back into the portal** (Q88's keeper
power) would serve instead of letting them die. Answering it took one scan, which
should have been run before the paragraph above was written.

**Nothing in the executable decrements `room[0x4D]`.** There are **129**
references to a `+0x4D` field across every structure in the binary and **not one
is a `DEC` or a `SUB`**. The portal's own unit holds exactly two increments (both
on the generate path), two stores of literal zero, and two reads.

So the counter **only climbs or is zeroed wholesale**, and three things follow:

1. **Throwing a creature back and killing it are exactly equivalent** — both
   remove a creature, and neither can lower `n` because no path exists to lower
   it. There is no asymmetry to exploit.
2. **A portal does not speed up when the population falls.** Predicted rather
   than observed, but the prediction is strong: the only route to a faster portal
   is whatever calls the two reset routines.
3. **This project's formula is wrong in a second way** — it counts *living*
   non-workers, so ours accelerates on every death and the original's does not.

**And the experiment is about the room, not the creatures.** Keep any test
**below the 15-creature cap**, because a portal at capacity stops generating
altogether — *"visibly slow"* at a dozen creatures may be *"stopped because
full"*, a different mechanism answering a different question.

### The experiment worth running, third version

**The owner, immediately: a portal cannot be sold or rebuilt.** *"it is a
'resource', a 'room' on a map that a player has to claim. it can be taken over by
a rival keeper, but that's about it."*

Which the shipped data states outright, and which this document had already used
two hours earlier for a different question: **Portal (3), Dungeon Heart (5) and
Hero Portal (25) are the only rooms with cost 0 and no flags at all** — no
`buildable`, no `walls`. `RoomRules.CanBuild` returns `NotBuildable` and
`CanSell` returns `NotSellable` for all three. Q207's whole answer rests on that
flag being absent from the heart. So the sell-and-rebuild experiment was
impossible, and impossible from a table already read.

**The good experiment needs no rival and no skirmish, and it discriminates our
model from DK2's directly:**

> Play a level with **two claimable portals**. Claim the first, let it fill your
> dungeon until it is visibly slow — ten creatures is plenty, and stay under the
> 15 cap. **Then claim the second portal and compare the two.**
>
> - **The new portal runs at the base interval** (18–25 s) while the old one
>   crawls → the tally is **per room**, exactly as the binary reads, and a fresh
>   portal is fast no matter how full the dungeon is.
> - **Both are slow** → the slowdown is **keeper-wide** after all, our
>   population-based formula has the right shape, and `room[0x4D]` is not what
>   drives it.

**Why this is the right test.** It is the sharpest possible split between the two
models and it happens in ordinary play:

| | ours today | DK2, as read |
|---|---|---|
| what `n` counts | the keeper's living non-workers | that **room's** own tally of creatures generated |
| a newly claimed second portal | **just as slow** as the first | **base speed**, independent of the first |
| a creature dying | speeds every portal up | changes nothing |

The difference is unmissable — one portal producing a creature every 20 seconds
beside one producing every two minutes — so a single glance settles it.

**The reset question survives, and only a rival can answer it:** lose a slow
portal to a rival keeper and take it back. If it returns to base speed the reset
is an ownership change, which is what a vtable-only zero-store looks like. That
is the only room event a portal has, now that selling is ruled out.

### One smaller thing, read on the way past

Three game seconds before the creature is due, the same routine computes a
second threshold — `EDI -= variable19 * 3` at `0x004DBC9E`. The shape of a
pre-warning: the animation or sound that plays before a creature appears.
Unbuilt, and it costs nothing to know.

### And Q90's two variables are dead

`DecreaseEntrancePersentageOfLastSackedCreatureType` (`0x006F2910`) and
`IncreaseEntrancePersentageOfNewCreatureTypes` (`0x006F2912`) were the named
entry point to the portal's per-type weighting. A raw scan finds **one hit each,
both the loader's setter**. Nothing reads either.

So the mechanism those two names describe **is not in v1.70**, and Q90's answer
is either that the weighting is hardcoded somewhere in the portal unit or that
there is no weighting at all. That also explains why the owner could not
perceive it: *"I have no idea"* is what a cut feature looks like from the
outside. Two more for the cut list, which makes **fourteen**.
