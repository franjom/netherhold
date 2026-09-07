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
| A creature arrives every `EntranceGenerationSpeedSeconds` | 25 game-wide, and **five levels override it** — one of the 26 ids a level may (ADR-0040). level1 **18**, level2 **20**, level3 **22**, Level10 **30**, Secret1 **10000**. The first three ramp; Secret1's portal is switched off |
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

#### The slope was the open half of Q86, and it is now closed — against us

**Read from the binary and then confirmed by stopwatch, both on 2026-09-07.**
The interval is `(n² + base)` game seconds, not `max(base, base × n ÷ 2)`, and
`n` counts *creatures this room has generated* rather than the keeper's living
creatures. The whole account, the seven timed gaps and the two formulas side by
side are further down this page under *"The generation interval is quadratic"*.

**`PortalRules.SpawnIntervalTicks` computes `n² + base` as of ADR-0086**, and the
tally's reset is settled too (an ownership change). This paragraph said the
opposite — *"still what SpawnIntervalTicks computes... known wrong at the call
site"* — for the few hours between reading the formula and shipping it, and was
left standing for a day afterwards. Everything above §1's heading is the
**pre-2026-09-07 account, kept for the lesson in it**; the sections at the bottom
of this page are current.

What that section replaces, and worth keeping visible as a lesson: this
subsection used to say the falsifiable claim was *"exactly nine more per
goblin"* on level 1, and cited the owner's earlier ~30-second measurement as
*"consistent with the formula rather than evidence for it"*. Both sentences were
honest and neither helped — the guess had already shipped and been pinned by a
passing test, and the timing that eventually broke it was volunteered as a side
note rather than run as the named experiment.

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

**Per portal is CONFIRMED by observation** (owner, 2026-09-07): a second portal
produced its first creature **8 seconds after** the first portal produced, at a
point where the required interval was about 60 seconds. One shared clock cannot
emit twice in 8 seconds while it is waiting 60. Before that it was inferred, from
two portals producing on their own schedules and the second raising the cap;
OpenKeeper's class comment calls it *confirmed original behaviour* that two
portals with equal counters produce simultaneously. **What is still not observed
is whether the per-room *tally* is per portal too** — see the discriminator
table further down.

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
base     = EntranceGenerationSpeedSeconds        -- 25 on 61 levels, 18/20/22/30/10000 on five
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

### CONFIRMED BY STOPWATCH, 2026-09-07 — the quadratic is right and the clock is per portal

**The owner timed seven consecutive arrivals from a claimed portal, then a
second portal's first arrival.** Volunteered as a side note, and it settles two
things this document had left open and rules out a third.

| arrival | stopwatch | gap |
|---|---|---|
| 1 | 0:25 | 25 s |
| 2 | 0:51 | 26 s |
| 3 | 1:21 | 30 s |
| 4 | 2:01 | 40 s |
| 5 | 2:42 | 41 s |
| 6 | 3:33 | 51 s |
| 7 (portal 1) | 4:32 | 59 s |
| 8 (**portal 2**) | 4:40 | **8 s after portal 1's** |

The owner's caveat: *"not completely precise, but could maybe be 99% close
enough"*, and the eighth *"caught me off guard"*.

**`base` is 25, so this was not level 1** — level 1 is the *only* one of the 66
shipped levels that overrides `EntranceGenerationSpeedSeconds` (18), so every
other level uses the global 25. That was not stated and did not need to be; the
first gap gives it away.

#### The quadratic against ours, gap by gap

| n | observed | `n² + 25` (binary) | `max(25, 25n/2)` (ours) |
|---|---|---|---|
| 0 | 25 | **25** | 25 |
| 1 | 26 | **26** | 25 |
| 2 | 30 | **29** | 25 |
| 3 | 40 | 34 | 37 |
| 4 | 41 | **41** | 50 |
| 5 | 51 | **50** | 62 |
| 6 | 59 | **61** | 75 |

Mean absolute error per gap: **1.4 s for the binary formula, 6.4 s for ours**,
and ours *diverges* — by the seventh gap it is 16 seconds long and getting
worse, because `base × n / 2` grows by 12.5 s per creature where `n²` grows by
`2n + 1`. Two gaps discriminate on their own: at n = 1 and n = 2 ours predicts
**no change at all** (both floored at the base) and the owner measured 26 then
30 against the quadratic's 26 and 29.

**So Q86's slope is no longer read-only.** It was CONFIRMED from six
instructions; it is now confirmed independently by observation, and the formula
this project ships is refuted by the same data. Note also what the fit
incidentally pins: **one game second is one real second** at normal speed, which
`variable19` and `OriginalTicksPerGameSecond` had only ever been assumed to
compose into.

#### The clock is per portal, and that is the owner's actual question

*"Is it possible that each portal has a timer for itself?"* — **yes, and this
data proves it rather than inferring it.** Portal 2 produced 8 seconds after
portal 1 did. At that point the required interval was ~60 seconds. **One shared
clock cannot emit twice in 8 seconds when it is waiting 60**, whatever formula
sits on top of it. Two independent clocks is the only shape that fits.

That was already in §4 above as STRONGLY INFERRED, on OpenKeeper's class comment
plus the fact that a second portal raises the cap. It is now observed.

#### What it does NOT settle: whether the *tally* is shared

The clock being per portal and the counter `n` being per portal are two claims,
and only the first is now observed. Portal 1's seventh gap (59 s) fits n = 6
either way, because portal 2 had generated nothing yet at that point.

**The discriminator is portal 2's next two arrivals, and the split is enormous:**

| | per-room tally (the binary's reading) | keeper-wide tally |
|---|---|---|
| portal 2, arrival 2 | `1 + 25` = **26 s** | `8² + 25` = **89 s** |
| portal 2, arrival 3 | `4 + 25` = **29 s** | `9² + 25` = **106 s** |
| portal 1, arrival 8 | `7² + 25` = **74 s** | `8² + 25` = **89 s** |

Half a minute against a minute and a half. **Time portal 2's next two arrivals
and the question closes** — no need to re-run the whole sequence, and no need
for the "claim a second portal and compare" experiment described above, which
this observation has already half-performed.

#### The one residual, and it has a shape

Gap 4 is 40 seconds where the quadratic says 34, and **every gap after it is
correct again** (41/41, 51/50, 59/61). The cumulative residual steps once, from
+1 s to +7 s, and then stays flat.

That is not noise — noise would wander. It is the signature of a **one-off block
of about six seconds**: the portal came due, no creature was eligible, it waited,
and `room[0x49]` was stamped at the *actual* generation, so the delay shifted
every later arrival by six seconds without corrupting a single later gap. §4's
"banks its wait" paragraph predicts exactly this. HYPOTHESIS — the alternative is
one mistimed reading, which the owner's own caveat allows for.


### The reset is an ownership change, and Q86 is now closed

**CONFIRMED both ways, 2026-09-07.** The owner, answering the last open part:

> *"if an enemy keeper takes one of the portals from me, and if they have the
> room to attract more creatures, then yes, it resets to zero for that portal
> and that keeper."*

And then in the binary, which had been recorded here as unreachable.

#### The room handler table

DK2 keeps a table of **per-room-type handler records** in initialised `.data`,
base `0x006a1e9f`, **41 bytes per record**, addressed as
`base + roomTypeId * 41` and guarded by `id != 0 && id <= 26`. Each record is a
type-id byte followed by ten packed unaligned dwords: six function pointers and
four small integers, three of which have their own accessor functions.

All 26 records are present and in id order. **Portal (3) and Hero Portal (25)
carry byte-identical handler sets**, which is why each of the two reset routines
turned up in the table exactly twice.

Six references resolve the slots:

| slot | called from | what that function is |
|---|---|---|
| `+0x01` | `FUN_004E3800` | **the room constructor** — zeroes the struct field by field, writes the type id, links the room into its owner's list |
| `+0x05` | `FUN_004E39C0` | null for portals |
| `+0x09` | `FUN_004E4480` | a tile/effect pass |
| `+0x0d` | `FUN_004E5650` | **the room ownership change** |
| `+0x11` | `FUN_004E3DD0` | null for portals |
| `+0x15` | per-tick | `0x004DBBE0`, the interval arithmetic |

**Corrected 2026-09-07 (later the same day): the table has more users than six.**
A `MaskScan` for the base constant, run with the argument in the form the script
actually wants, returns **thirteen** indexing sites in seven more functions than
the table above names:

```
004e3993 FUN_004e3800    004e39ea FUN_004e39c0    004e3e00 FUN_004e3dd0
004e44be FUN_004e4480    004e4b23 FUN_004e4b00    004e585a FUN_004e5650
004e6cac FUN_004e6c90    004e6cec FUN_004e6cd0    004e6d2c FUN_004e6d10
004e8f48 FUN_004e8da0    004e9003 FUN_004e8da0    004e905d FUN_004e8da0
```

All of them compile the record address as `[EAX + ECX*0x8 + 0x6a1e9f]`. So
`FUN_004E4B00`, `FUN_004E6C90`, `FUN_004E6CD0`, `FUN_004E6D10` and
`FUN_004E8DA0` (three sites) dispatch through this table and **which slots they
call is unread**. Nothing about the reset depends on it — that rests on
`FUN_004E5650`, which is decompiled — but the table above should be read as *the
slots we have resolved*, not as the table's full set of callers. Q210.

For a portal, `+0x01` and `+0x0d` are the two reset routines. Both stamp
`room[0x49] = now` and zero `room[0x4D]`; the constructor's also sets one flag.

#### What the ownership-change routine does, in order

`FUN_004E5650` takes a room and a new owner id, and: decrements a counter on the
old owner, re-links the room from one player's room list to the other's, **writes
the new owner into `room[0x1d]`**, increments the same counter on the new owner,
re-skins it, walks its tiles updating the map, ejects every object in it, **then
fires the type's `+0x0d` handler** — the reset. Afterwards it credits the new
owner with a room count (excluding three room types) and with a value
proportional to tiles, and sends **two different notifications**, one to the old
owner and one to the new, choosing between two message ids by comparing against
what is almost certainly the local player.

So a rival taking a worn portal gets it at the base interval, and so does the
first keeper to claim a neutral one — which is why the owner's second portal
started at 25 seconds rather than inheriting anything.

#### And the "vtable only" claim was mine and was wrong

This document said for a day that both zero-stores were *"reached only through a
vtable"*, with **no direct caller or absolute reference in `.text`**. The second
half was true and the conclusion did not follow: **the scan covered `.text`
alone**, and a handler table lives in `.data`. A scan over every initialised
block finds both addresses immediately. The silence of a scan that could not
have found the thing was read as evidence about the thing.

### The capacity rule, read at last — and both portal types count

`FUN_004DC760` is the capacity routine. It walks the **player's own** room list
twice, counting room type 3 and then room type `0x19`, and returns:

```
total = portals + heroPortals
capacity = total == 0 ? 0
         : first + (total - 1) * perAdditional
```

Same shape this project had, with one difference: **we counted only type 3**, so
a keeper holding a Hero Portal got its arrivals and not its five extra places.
Fixed.

Two more things the read settles:

- **The comparison is strict.** Attraction proceeds only while the keeper's
  creature count is *less than* capacity, and **nothing anywhere culls
  creatures**. So a keeper who loses a portal keeps every creature, drops below
  their new cap, and simply receives nothing until they are under it again —
  the owner's second point, and their *"creatures don't disappear if that
  happens"* is what the absence of a cull looks like from the inside.
- **`CreaturesSupportedPerAdditionalPortal` nearly went on the dead list.** A
  raw scan for `0x006F28B5` finds **one hit, the loader's setter** — the exact
  signature this project has used fourteen times to call a variable cut. It is
  live. Its byte sits immediately after `CreaturesSupportedByFirstPortal`'s and
  is loaded with it, so the second variable's address never appears in any
  instruction. **Any byte or word variable whose global is adjacent to another's
  can be read this way and will scan as dead.** That is a third kind of false
  negative, after ADR-0085's disabled-not-cut and the `.text`-only scan above.

  > **Read directly, 2026-09-07** (`balance-variables-in-the-binary.md`, last
  > section). The load is a **dword**, not a word, and the extraction is
  > `MOV DL,CH`; the arithmetic and the conclusion are unchanged. Two things
  > worth having from the same six instructions. **The routine counts room type
  > `3` and room type `0x19` and adds them** before applying
  > `(n − 1) × 5 + 15` — so *"a claimed Hero Portal raises the keeper's cap"* is
  > now read off the instructions rather than inferred from the byte-identical
  > handler records. And **the trap is bounded**: a folded load reaches four
  > bytes, so a dword variable can never be carried by a neighbour's load, and
  > sweeping all 57 zero-reader globals for the shape leaves **this case and no
  > other**. The exposure this bullet opened is closed.

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

---

## The Hero Portal is an entrance too, and one implementation serves both

**2026-09-07. CONFIRMED by the owner, then read in the binary.** The owner's
words, which set the question:

> *"hero portal IS AN ENTRANCE, but for goodly heroes (rival faction), until it
> is destroyed by evil keepers by digging and claiming tiles around it, if
> possible."*
>
> *"'hero portal' is not something a keeper can use, nor can an 'evil' keeper
> summon 'good' creatures. If a keeper claims a hero portal room that means that
> it is already destroyed by claiming all its adjacent tiles, and it just becomes
> an indestructable dead room that can't be sold or repurposed."*
>
> *"hero portal and evil portal don't look the same, they have different
> textures."*

### The data draws no distinction at all

| | Portal | Hero Portal |
|---|---|---|
| room id | 3 | 25 |
| room flags | `0x00001004` | `0x00001004` |
| terrain id | 12 | 40 |
| terrain flags | `0x20000034` | `0x20000034` |
| handler record | `006a1f1a` | `006a22a0` |

**Both flag words are byte-identical**, and so is the whole 41-byte handler
record: `FUN_004dbb90 | - | FUN_004dcd20 | FUN_004dbbc0 | - | code:004dbbe0 |
197 | 197 | 52 | -`. Same constructor, same ownership-change reset, same
per-tick unit, same three integers. **DK2 ships one portal implementation and
runs it for both room types**, which is exactly what "a hero portal is an
entrance for the other side" predicts.

Being separate *terrains* rather than one terrain with an owner-dependent skin is
what the owner's texture observation reports from the outside, and it carries a
consequence: **claiming a Hero Portal tile does not turn it into a Portal.** The
terrain stays 40 and the room stays type 25, which is what makes the next section
bite.

### What separates the sides is the room type, inside the shared code

The candidate builder `FUN_004dbf90`, per creature type, in one condition:

```
(def+0x686 & 0x8000000) == 0                          // not unique
&& ( ((def+0x686 & 0x400) && room+0x18 == 3)          // portal flag, room type 3
   || ((def+0x68a & 8)    && room+0x18 == 0x19) )     // hero flag,   room type 25
```

`room+0x18` is the room type id. **The room type selects which creature flag is
required, and the two are exclusive.** A Portal can never emit a hero and a Hero
Portal can never emit an evil creature — **by room type, not by owner**, so it
holds however the room changed hands. The uniqueness test sits outside the or and
therefore refuses both sides.

That is the mechanism behind *"not something a keeper can use"*. It is not a
hardcoded prohibition on keepers; it is that a keeper's claimed Hero Portal is
still room type 25, so the only creatures it may emit are ones the keeper does
not have.

### The hero flag: a second flag field, and how it was located

The binary reads `byte ptr [def + 0x68a] & 8`. **The runtime offset is no guide
to the file offset** — the loader does not preserve record layout — and reasoning
from adjacency gave a wrong answer first: file `0xF00`, four bytes after the
known flag dword at `0xEFC`, holds a 3000/2800 value and not flags.

What located it was the **roster**. The flag has to select the creatures a Hero
Portal generates, so `dk2tools kwd creaturebits` (added for this) searched all
5449 bytes x 8 bits of the CREATURES record for a matching set:

```
0x0EFF bit6 EXACT     n=10 : Knight Dwarf Giant Wizard Elven Archer Thief Monk Fairy Guard Royal Guard
0x1545 bit3 off-by-1  n=9  : Knight Giant Wizard Elven Archer Thief Monk Fairy Guard Royal Guard
```

**`0x1545` bit 3 is the flag.** It is bit 3 of a *byte*, which is the shape the
binary tests; `0x1545` is the record's **last dword**, inside the 72-byte tail
Q70 says nobody had accounted for; and only the low four bits are ever set across
all 46 creatures, the upper three bytes being zero everywhere.

**The Dwarf is the discriminator, and it saved a wrong implementation.** Bit 30
of the *first* flag dword (`0x0EFF` bit 6) selects those nine **plus the Dwarf**
and is named `MpdRandomInvader` in our enum, from OpenKeeper. Gating on it —
which was the tempting move, the bit being already parsed and already named —
would give a Hero Portal the heroes' **worker**. Each side's portal excludes its
own worker: the Imp likewise lacks `AvailableViaPortal`. Rule 3 in practice.

**The cross-check that makes it trustworthy.** The nine are *exactly* the nine
that `PortalOnRealDataTests.TheCampaignUnlocksNineCreaturesOnlyTheFlagGateKeepsOut`
arrived at from the opposite direction weeks earlier — the creatures the campaign
makes AVAILABLE and pooled to keepers that only the Portal's flag gate keeps out.
**The two flags partition the portal-generated roster between the sides**, and
two independent reads of the shipped data meeting on the same nine names is the
evidence, not the roster match alone. Corroborating the file-to-runtime mapping
separately: bits 0 and 2 of the same byte have readers at the same runtime offset
with masks 1 and 4, and the def pointer is `creature+0x370`.

### The dead room, and it needs no new rule

Everything the owner describes about a claimed Hero Portal falls out of flags
already parsed:

- **Cannot be sold.** `RoomRules.CanSell` refuses any room without `Buildable`,
  and `0x00001004` has none. Same reason a Portal and a Dungeon Heart cannot be
  sold (Q207).
- **Cannot be repurposed.** `RoomRules.CanBuild` refuses any tile whose terrain
  is a room. Repurposing means selling first, and it cannot be sold.
- **Indestructible.** The room flags carry no `Attackable`, unlike a Lair or a
  Treasury. The *terrain* is `Attackable`, which is tile-level and is how an imp
  claims it.
- **"Already destroyed by claiming the tiles around it."** A `RoomInstance` is
  tiles of one terrain with **one owner**, so claiming the ring leaves the hero
  side the centre and the keeper a ring that fails `CanProduce` — it does not
  hold the tile a creature climbs out of.

**None of this needed a change.** What did need a change is below.

### Two divergences this found, both now fixed

**1. A keeper's claimed Hero Portal was producing the keeper's evil creatures.**
`CanComeFromPortal` took a creature and no room, so it answered the Portal's
question about every entrance. The doc comment sitting on it had *predicted the
bug and dismissed it* — *"a hero portal has its own flag in a second flag word
this project does not parse. It costs nothing today"* — which was true only while
no keeper could own one, and a keeper can. This is the
`a-claim-nothing-checks-may-already-be-false` shape with the claim written down
and reasoned about.

**2. Capacity counted only portals that could produce.** DK2's counter
(`FUN_004dce30`) walks the owner's room list and tests `room[0x18] == type` and
**nothing else** — no size, no centre tile, no notion of the room working:

```
for (room = player->rooms; room; room = room->next)
    if (room->type == wanted) count++;
```

So a broken portal still pays into the cap. Two consequences to expect rather
than treat as bugs, both reproduced under fidelity priority 4:

- **Claiming a dead Hero Portal raises a keeper's creature limit** by
  `CreaturesSupportedPerAdditionalPortal` (5), or sets it to
  `CreaturesSupportedByFirstPortal` (15) if it is their only one — while the room
  emits nothing.
- **A portal cut into two rooms by a rival taking its middle counts twice.**

The count was wrong twice in one day, in two different directions: first counting
only room type 3, then counting only portals that pass `CanProduce`.

### Frosty's Castle: a map where keepers are *meant* to use Hero Portals

The gate is the room type and the creature's flag — **not the owner's
alignment**. So a keeper holding a Hero Portal with a hero creature available and
pooled should receive it. **The shipped data says three maps are built on exactly
that, and it is not a corner case.**

`dk2tools kwd portals` on **Frosty's Castle**:

```
Frosty's Castle        base= 25s  portals=4
    Hero Portal  owner=2 tiles= 9 at (4,80)
    Hero Portal  owner=2 tiles= 9 at (17,3)
    Hero Portal  owner=2 tiles= 9 at (80,93)
    Hero Portal  owner=2 tiles= 9 at (93,16)
```

**Four Hero Portals, no Portal, and every one unowned** (player 2) and therefore
claimable. And the level's own VARIABLES pool, for each of keepers 3, 4, 5 and 6:

```
creature13 creature15 creature16 creature17 creature18 creature19 creature20 creature25 creature30
= Knight Giant Wizard Elven Archer Thief Monk Fairy Guard Royal Guard
```

**Those are precisely the nine `AvailableViaHeroPortal` carriers, and creature 14
— the Dwarf — is pointedly absent.** The map has no other creature source, so a
keeper there *must* claim a Hero Portal, and it hands them Knights.

This is the **third independent line of evidence** for the flag, and the
strongest, because it is a level designer's intent rather than a coincidence of
bits: the binary's gate shape says a byte's bit 3, the roster search finds exactly
one such byte, the campaign's blocked-by-the-Portal set is the same nine, and a
level built around Hero Portals pools the same nine while excluding the one
creature the rejected candidate bit would have added.

**It also means this fix was not cosmetic.** Before it, our `Pick` applied the
Portal's flag gate to every entrance, so on Frosty's Castle a keeper's claimed
Hero Portal produced **nothing at all** — the nine lack `AvailableViaPortal`.
Now it produces them, and only them.

**Not contradicting the owner.** *"An evil keeper cannot summon good creatures"*
is true of the campaign and is carried entirely by the flags plus availability;
these three maps switch its effect off deliberately by granting the roster. The
rule is the flags, not the alignment.

**Still worth one look in the running game** (Q211, observation checklist): that
a keeper on Frosty's Castle really does receive Knights from a claimed Hero
Portal. Everything above is read from files and the binary, and the owner has
played the game — this is a five-minute confirmation of a chain that is otherwise
only very well supported.

**It is a skirmish map** (owner, 2026-09-07: *"frosty's castle is a skirmish map
which I didn't play"*), which resolves the last oddity rather than weakening any
of the above. A skirmish map is free to hand keepers the hero roster because
there is no campaign narrative to contradict — so *"an evil keeper cannot summon
good creatures"* stays exactly true of the campaign, and these three maps are the
designed exception rather than a leak. It also means **the observation is a fresh
one for the owner too**, so Q211 is a real test and not a memory check. Not MPD,
incidentally: `mpd1`..`mpd7` are separate maps.
