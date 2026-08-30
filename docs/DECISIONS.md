# Architecture Decision Records

## ADR-0001 — Integer/fixed-point gameplay math
**Date:** 2026-08-29 · **Status:** accepted

DK2 v1.70 mixes integer math with x87 floats (Flame ships `fpu_control.h`).
Bit-exact x87 reproduction on modern hardware is not tractable. We use
integer/fixed-point for all gameplay-affecting quantities; floats are
presentation-only. Trades unachievable float-exactness for achievable
cross-platform determinism.

## ADR-0002 — Simulation independent of Unity, enforced at compile time
**Date:** 2026-08-29 · **Status:** accepted

Simulation-side assemblies target netstandard2.1 (Unity-loadable) and are
forbidden from referencing Unity. Enforcement: `ArchitectureTests` fails the
test run if any simulation assembly references an assembly named `Unity*`.

## ADR-0003 — MIT license; clean-room use of reference projects
**Date:** 2026-08-29 · **Status:** accepted (owner decision)

This project is MIT-licensed. Consequences:
- **OpenKeeper (GPLv3):** no code may be copied or transliterated. Facts
  learned from it (format layouts, enum values, record ids) may be used.
- **Flame (no license):** same rule, stricter reason — unlicensed means no
  reuse rights at all. Its symbol maps are consulted as research material.
- Every research note in `docs/research/` records provenance so any borrowed
  *fact* can be traced to its source.

## ADR-0004 — xoshiro256** as the simulation RNG
**Date:** 2026-08-29 · **Status:** accepted

Public-domain, fast, trivially serialisable, platform-independent. Deliberately
NOT DK2's original generator (which is UNKNOWN). If fidelity work later shows
the original RNG's sequence matters observably, a second generator goes behind
the same `SimRandom` surface.

## ADR-0005 — .slnx solution format
**Date:** 2026-08-29 · **Status:** accepted

.NET 10 default. No tooling in this project requires the legacy `.sln`.

## ADR-0006 — Determinism is gated by a pinned state hash, exercised by a chaos system
**Date:** 2026-08-29 · **Status:** accepted

`GameSimulation.StateHash()` is FNV-1a (`DK2.Core.Hash64`) over the world plus
the RNG's position, and CI pins one exact value on both Linux and Windows.

- **RNG state is hashed** because two runs can share visible state while having
  consumed different draws; they are about to diverge, and catching it later is
  far harder.
- **`GetHashCode` is forbidden** in the hash path: .NET randomises string
  hashing per process, which would make the gate pass locally and be
  irreproducible on CI.
- **A test-only `ChaosSystem`** mutates state and consumes randomness each tick.
  Without it, no systems are registered, the world never changes, and the gate
  would pass even with a broken loop, hash or snapshot. It implements no DK2
  mechanic.

Consequence: a deliberate change to simulation state requires bumping
`GameSimulation.HashVersion` and updating the pinned literal in the same commit.
Any other failure of that test means determinism is broken and must not ship.

## ADR-0007 — Our own save format; DK2 save compatibility is not a goal
**Date:** 2026-08-29 · **Status:** accepted

`WorldSnapshot` writes a private binary format. Reproducing DK2's save layout
buys nothing behavioural (plan risk R13) and our state is not shaped like the
original's. `save_struct.bt` remains useful only as a checklist of *what state
exists*.

The invariant that matters is not byte equality but continuation: restore a
snapshot into a fresh simulation and both runs must produce identical state
hashes indefinitely. That proves the snapshot captured everything the
simulation depends on, RNG position included.

## ADR-0008 — YamlDotNet for scenario files (tooling only)
**Date:** 2026-08-29 · **Status:** accepted

Scenarios are YAML (as sketched in `DK2_REIMPLEMENTATION_PLAN.md` §6.2), parsed
with YamlDotNet (MIT). Hand-rolling a YAML subset is a well-known trap and the
regression corpus is exactly the artifact that must not be misread.

Scope limit: the dependency lives in `DK2.Harness` and the test projects only.
No simulation assembly may take it — those stay dependency-free and
Unity-loadable.

The loader reports unrecognised keys instead of ignoring them, and the runner
reports recognised-but-unapplied setup. A scenario that quietly does less than
it claims would corrupt the corpus, which is the one artifact this project
cannot afford to have lying to it.

## ADR-0009 — Tile transitions are triggered by work, not by health level

**Status:** accepted (2026-08-29, M3)

**Context.** Each terrain type carries `maxHealthTypeTerrainId`, which reads
naturally as "what this tile becomes at full health". It cannot mean that. Six
terrain types ship at maximum health while pointing at a different type —
Claimed Path is 660/660 and points at Rock — so a level-triggered rule would
turn every claimed floor tile in every level into rock on the first tick.

**Decision.** The conversion belongs to the work, not to the health level. Only
claiming, reinforcing and filling in convert a tile on reaching maximum;
repairing clamps and stops. Each of the three lands exactly where the data
points, which also answers Q18: there is no second meaning for the field.

**Consequences.** The rule stays uniform and data-driven, and no terrain is
special-cased. `TileWorkRules.CompletesUpward` is the single place the decision
lives. If Q18 is ever settled differently by disassembly, that one predicate is
what changes.

## ADR-0010 — Work orders are a queue on the world, filled by whoever is working

**Status:** accepted (2026-08-29, M3)

**Context.** Tile mechanics are ready before the creature that performs them.
Job selection (Q2) and pathfinding (Q5) are the two largest open questions in
the project and neither should block the mechanics they eventually drive.

**Decision.** `WorldState` carries a FIFO `TileWorkOrder` queue. `TileWorkSystem`
drains it every tick and applies the rules. Producing orders is somebody else's
job: eventually a working creature, for now a test or the scenario harness.

**Consequences.** The mechanics are testable and reviewable now, against real
game data, without inventing a worker. The cost is that a scenario can inject
work no creature would ever do, so the harness reports injected work as
unanswered in every run that uses it, and `ScenarioTests` asserts that it does.
The queue is a list, hashed and snapshotted like all other state: order is
behaviour, so it is never sorted or deduplicated.

## ADR-0011 — Ticks are related to seconds in exactly one place

**Status:** accepted (2026-08-29, M3)

**Context.** Q1 (DK2's tick rate) is unmeasured, and Q19 (what the health deltas
are *per*) depends on it. M3 needs both and can answer neither.

**Decision.** `SimulationTiming` holds every tick-to-second relationship,
including a placeholder `TicksPerSecond`. No tick count that means a duration
may be written anywhere else.

**Consequences.** Answering Q1 is a one-line change. The placeholder does not
affect the *order* of simulated events — per-second work is scheduled as one
application every `TicksPerSecond` ticks — so the determinism gate and the
pinned hash are independent of the guess. What it does affect is the mapping
from ticks to wall-clock seconds, which is precisely the claim the project is
not yet entitled to make.

## ADR-0012 — The pathfinder is an admitted placeholder behind an interface

**Status:** accepted (2026-08-29, M3)

**Context.** A worker cannot exist without routing, and DK2's routing is Q5 —
the project's largest open question (plan §9, R1). Flame names a 36-byte
`Follow_Path` struct with no named fields; nobody has reconstructed it. Waiting
for Q5 would block every mechanic that needs somebody to walk somewhere.

**Decision.** `DK2.Pathfinding.IPathfinder` defines the two questions the
simulation asks — route to a known tile, and route to the nearest tile
satisfying a predicate. `GridPathfinder` answers them with a four-connected,
uniform-cost, deterministically tie-broken search, and its own documentation
says it is not DK2. `ImpTaskSystem` takes the pathfinder by constructor, so a
reconstruction drops in without touching the systems.

**Consequences.** Three gameplay-visible choices are ours and are recorded as
such: four-connectivity (Q28), uniform terrain cost (Q30), and which of several
equally short routes comes back. Travel times in any scenario run are a
consequence of those choices, and the harness says so in every run that places a
worker. The conservative option was taken where there was one: a four-connected
route is walkable by an eight-connected mover, so replacing the search can only
widen the set of valid paths, never invalidate a path already relied upon.

## ADR-0013 — One declared system order, in one place

**Status:** accepted (2026-08-29, M3)

**Context.** System order is part of the simulation's definition: two systems
touching the same state in a different order produce a different game. With four
systems it was already being written out twice — once in the test fixture and
once in the scenario harness — and a divergence between them would mean the
regression corpus and the test suite were describing different games.

**Decision.** `DK2.Simulation.Systems.SimulationSystems.Default()` is the single
declaration of which systems run and in what order: tags, then movement, then
task selection, then work. Tests and the harness both use it.

**Consequences.** The order is stated once, with its rationale, rather than
implied in several places. DK2's own per-tick subsystem ordering remains
UNKNOWN; ours is explicit and explicable, which is the most that can honestly be
claimed. A system added later is added to one list, and the pinned determinism
hash moves in the same commit.

## ADR-0014 — Worker behaviour covers marked work only

**Status:** **SUPERSEDED by ADR-0018** (2026-08-29, same day) — the owner supplied
the priority order from the original, so the reason for the restriction went away.
Kept in full because the *reasoning* is the point: the restriction was correct
while the order was unknown, and it is what made the answer cheap to adopt when
it arrived. Originally accepted 2026-08-29, M3.

**Context.** A DK2 imp digs what is marked, but also claims floor, reinforces
walls, carries gold to a treasury, drags bodies and flees fights. Implementing
the others is not technically hard — claiming is one call into rules that
already exist — but each is a job the imp starts *by itself*, so each needs an
invented rule for when it starts and how it ranks against the rest.

**Decision.** `ImpTaskSystem` implements marked work and nothing else. Marked
work is the one job whose trigger is not in doubt: the keeper asked for it.

**Consequences.** A worker in this build does less than a DK2 imp, visibly and
by design, and every scenario run that places one says exactly which jobs are
missing and that their priority order is UNKNOWN (Q24). The alternative —
shipping a plausible priority order — would have put an invention at the centre
of creature behaviour, where later evidence would be hardest to reconcile
against it.

## ADR-0015 — Gold lives in tiles; a player has no gold counter

**Status:** accepted (2026-08-29, M3)

**Context.** `PlayerState` carried a `Gold` integer from M2, on the assumption
that gold is a player-level resource that treasuries merely cap. Extracting the
balance table for the treasury work contradicted that: DK2 has **no** variable
for a player's gold, a starting balance or a maximum. It has four limits, and
all four are per place — `MaxGoldPerTreasuryTile` (3000),
`MaxGoldPerDungeonHeartTile` (1000), `MaxGoldPileOutsideTreasury` (2000), and
the CREATURES record's own per-creature `maxGoldHeld`.

**Decision.** Gold is stored on tiles (`TileState.GoldHeld`) and nowhere else.
`PlayerState.Gold` is removed. `GoldStore` is the only way gold moves, and a
player's spendable total is the sum over the tiles they own. Mana stays on the
player, because its variables are ceilings and rates on one pool with no
per-place storage anywhere.

**Consequences.** Three behaviours stop being special cases and become
consequences: a keeper with no treasury cannot keep gold, destroying a treasury
takes the gold with it, and a keeper's first treasury is necessarily paid for out
of the dungeon heart. Gold that will not fit is refused and reported rather than
silently kept or silently dropped — `GoldStore.Deposit` returns what it accepted
and `TileWorkResult.GoldSpilled` reports what a destroyed room lost. The cost is
that `Total` and `TryWithdraw` scan the tile array; that is one scan per room
tile bought, and a renderer that wants the number every frame should cache it.

## ADR-0016 — Where a room may go is read from the room's own flags

**Status:** accepted (2026-08-29, M3)

**Context.** "Bridges go on water, everything else goes on land" is a rule that
could be written as a switch on room type. The ROOMS record already carries
`PlaceableOnLand`, `PlaceableOnWater` and `PlaceableOnLava`, and the TERRAIN
record already carries `Water`, `Lava`, `Solid` and `Room`.

**Decision.** `RoomRules.IsSuitableGround` asks the flags and never names a room.
The same discipline as "a gameplay rule keyed on a terrain id is a rule about the
reference install" (ADR-0009's neighbourhood), extended to rooms.

**Consequences.** No code knows what a bridge is, and the three bridges in the
shipped data fall out of the flags rather than out of a list.
`RoomsOnRealDataTests` asserts the partition against the install, so a data set
that broke it fails a test instead of quietly changing the rules. The one place
a room type *is* named is `GoldStore.Capacity`, where the two variables are
themselves named for the treasury and the dungeon heart — there the room type is
the data's own vocabulary for the question.

---

## ADR-0017 — A per-second rate from the balance table is applied once a game second

**Status:** accepted (2026-08-29, M3)

**Context.** Mana income is the first rate in the simulation that is paid to a
player rather than applied to a tile by a worker, and it is small: one mana per
claimed tile per second. Spread smoothly over `TicksPerSecond` ticks it is a
fraction, so it needs either fixed-point mana or a per-player accumulator — new
state, in the hash and the snapshot, to represent a sub-tick remainder.

**Decision.** Rates named `…PerSecond` in the data are applied in whole, once
every `SimulationTiming.TicksPerSecond` ticks, on the last tick of each game
second. `ManaSystem` does this and holds no state of its own.

**Consequences.** The arithmetic stays in integers with no remainder to carry, no
new field enters `PlayerState`, and — the reason this is a decision rather than a
convenience — **totals stay independent of `TicksPerSecond`**, which is still the
unmeasured Q1 placeholder. A run's mana after ten game seconds is the same number
whatever that constant is set to, so answering Q1 will not silently rewrite the
economy.

What it costs: the counter moves in steps rather than smoothly. Whether DK2
accrues smoothly is UNKNOWN (Q39), it is invisible in any total, and it cannot be
measured before Q1 is. If it ever matters, the change is local to `ManaSystem`
and needs one accumulator field.

This generalises the rule `SimulationTiming.WorkApplicationPeriodTicks` already
encoded for tile work; ADR-0011 (ticks meet seconds in one place) is unchanged.

---

## ADR-0018 — Imps take jobs in a strict, confirmed priority order

**Status:** accepted (2026-08-29, M3). Supersedes ADR-0014.

**Context.** ADR-0014 restricted workers to marked excavation because the order
an imp prefers its jobs in was UNKNOWN (Q24) and any order we picked would have
been an invention at the centre of creature behaviour. The owner then supplied
the order from the original: **marked excavation, then claiming, then
reinforcing**, and after that wandering the dungeon and interacting with scenery
and creatures.

**Decision.** `ImpTaskSystem` implements those three, in that order, as a strict
priority: the whole reachable map is searched for excavation before a single
claimable tile is considered. Three sequential nearest-searches, not one scored
search over a mixed candidate list — because what was described is an ordering,
not a weighting.

Carrying mined gold is deliberately **not** in the list. The same session
established that an imp mines into its own hands and delivers when full, so
carrying is the tail of a mining job rather than a job an imp chooses; putting it
in the priority list would have modelled it as the latter.

**Consequences.** An imp with nothing marked is no longer an imp with nothing to
do — it claims and reinforces outward from its keeper's territory, which is what
the original does and which changed five tests that had encoded the old
restriction. What is still not implemented is the *fourth* behaviour: an imp with
none of the three available stands still rather than wandering (Q41), because
wandering has no simulation consequence we model and inventing a walk would put
imps where DK2 would not.

Two smaller rules ride along, both flagged rather than hidden: claiming and
reinforcing are taken only from ground the keeper already holds, which is what
makes territory spread outwards rather than appear in islands (STRONGLY INFERRED,
Q42); and the original's cap of three imps a face and twelve a tile is
**not** implemented yet (Q43).

**What this decision is really about.** ADR-0014 was not wrong and is not being
corrected. Refusing to guess an order is precisely why adopting the real one cost
one afternoon and five test rewrites instead of an archaeology exercise against
an invented order that had spread into a dozen behaviours. That is the pattern to
repeat, not to regret.

---

## ADR-0019 — A creature's hands are a place gold can be

**Status:** Accepted (2026-08-30)

**Context.** ADR-0015 established that DK2 has no player gold counter — gold is
always in one of four places, each with its own limit, and a keeper's total is
the sum. Three of those four are tiles. The fourth is a creature's hands, bounded
by the CREATURES record's own `maxGoldHeld`, and it was left unimplemented at M3
because nothing carried anything.

Q31 then came back from the owner: an imp mining a seam **takes the gold into its
own hands** up to that capacity, runs to a treasury or dungeon heart, and puts it
down. A pile on the floor is the fallback for when there is nowhere to put it,
not the first step.

The tempting shortcut was to credit the keeper at the rock face and treat the
walk as decoration. It is not decoration: it is why gold arrives in lumps rather
than as a trickle, why destroying a treasury mid-run loses a specific amount, and
why a keeper watching their imps mine sees the number lag behind the seam.

**Decision.** `CreatureState.GoldHeld` exists, is bounded by the creature's own
`maxGoldHeld`, is hashed and snapshotted like every other piece of state, and
survives `ClearJob` — what is in an imp's hands is not part of its job.
`TileWorkOrder` carries the id of the creature that swung, so the resolver knows
whose hands the gold goes into; an order with no creature behind it (a test, the
harness) has its gold reported and dropped exactly as before.

**Consequences.**

- **A keeper's spendable total dips while imps are in transit.** That is not an
  artefact, it is where the gold is, and `GoldStore.Total` is right to exclude it.
- **Deposits land where the carrier walked.** `GoldStore.DepositInto` fills one
  tile — the one the imp is standing on — and the imp looks again if it is still
  carrying anything. Spillover from one treasury tile to the next is therefore a
  consequence rather than a rule, which is as close to Q35 as observation has
  got. `GoldStore.Deposit`'s tile-index order survives only where no carrier is
  involved: paying for a room, and refunds.
- **Where an imp stops mining is ours** (Q50, HYPOTHESIS): when one more swing
  would free more than it can hold. It is the only stopping point that neither
  wastes gold nor overfills a pair of hands.
- **An imp with nowhere to unload keeps mining**, which is what the original
  does, and loses what it cannot hold, which is not: DK2 leaves a lump. Standing
  still instead would stop a keeper with no treasury from mining at all, and that
  is a worse wrong answer than losing the overflow loudly. The lump needs the
  OBJECTS record (M6).

**Not decided here.** Whether `InitialGoldHeld` (500 on the Imp) means a creature
spawns holding gold. Nothing reads it and nothing should until somebody knows.

---

## ADR-0020 — Work lands five times a second, a fifth of the tabled delta at a time

**Status:** Accepted (2026-08-30). Supersedes the granularity half of ADR-0011's
placeholder status; ADR-0017's rule for `…PerSecond` variables is unaffected.

**Context.** The ten tile-work deltas in DK2's balance table carry no unit —
unlike their neighbours, which spell out `…PerSecond` and `…Seconds` in their
names. M3 shipped with the simplest reading that could be defended: one
application a second, applying the whole tabled delta. Against the original that
ran everything visibly too fast, and Q47 recorded it as a factor of about two
without implementing anything, because the fit rested on ranges ("2–3 hits",
"4–5 hits") rather than on a number.

The owner then measured one: **an imp mining a gold seam frees 273 gold a
swing.**

That is not a range, and it propagates. A seam is 3000 gold over 660 health, so
273 gold is 273 × 660 / 3000 = **60 health**; 660 / 60 is **11 swings**; and 60
is exactly a fifth of `MineGoldHealth`'s −300. Five applications a second.

**Two corroborations, neither of which was looked for.**

1. **Eight of the ten tabled deltas divide by five exactly** — 825, −140, 125,
   −300, −5000, −130, 5, 50. Chance would give two. No other small rate comes
   close: **825 is odd**, which on its own rules out the twice-a-second reading
   this replaces, and rules out four and ten as well.
2. **Claiming lands on the independently observed count.** `ClaimTileHealth` 825
   at a fifth is 165 a swing against a Dirt Path running 1→660: **four swings**,
   which is the top of Q46's "2-3-4 jumps MAX", measured a day earlier and from
   a different mechanic.

**Decision.** `SimulationTiming.WorkApplicationsPerSecond` is 5, and
`TileWorkRules.PerApplication` divides the tabled delta by it. The rate per
second is unchanged; only the granularity is. That is the whole point: **the
count of swings a tile takes is now the measured quantity and is independent of
Q1**, where the wall-clock time a tile takes still is not.

Two deltas do not divide — `DigEnemyWallHealth` (−8) and `RepairWallHealth` (22)
— so `TileState.WorkRemainder` carries the fraction between applications, the
same device `CreatureState.MoveRemainder` uses and for the same reason
(ADR-0001). Without it an enemy wall would take 900 swings instead of 563 and
run 37% slow forever. The remainder is on the *tile* rather than the worker,
because the fraction belongs to the wearing-down of the tile: two imps on one
wall should between them remove what the table says per second.

**Gold changed with it.** Gold released is now the difference of two running
totals — how much has come out by the time a seam is down to *this* health,
minus how much had at the last — rather than a proportion of the last swing.
Truncating each swing independently loses a coin a swing, and at eleven swings a
seam would be worth 2992 of its stated 3000. The running-total form makes a
fully mined seam yield **exactly** its `goldValue`, needs no state to do it, and
reproduces the measurement: 273 on eight of the eleven swings and 272 on the
other three.

**Consequences.**

- Every application count in the game changed. Claiming a tile is 4 swings where
  it was 1; reinforcing rock is 50 where it was 10; attacking floor is 24 where
  it was 5. Times in seconds are unchanged throughout.
- An imp fills its hands after **five** swings (1363 of 1400) and a seam takes
  **three trips** — 1363, 1364, 273. Q50, which asked where an imp stops mining,
  is answered by arithmetic rather than by the rule that was guessed for it.
- `DigRockHealth` is now the **only** number in the tile mechanics that does not
  fit. It was one anomaly among several suspects; it is now alone. See
  `tile-work.md` — it is still reproduced, not corrected.
- The pinned determinism hash and the snapshot format both moved.

**What this is not.** It is not a measurement of the tick rate. It fixes the
ratio between an application and a tabled per-second delta. Q1 is untouched.

---

## ADR-0021 — Imps come from a floor and a price, and mana finally has an outgoing

**Status:** Accepted (2026-08-30)

**Context.** Every scenario run since workers existed has carried the same
caveat: *"nothing generates imps: scenario files and tests place them"*. And
every run that earned mana carried another: *"mana here is income only, nothing
spends it"*. The two are the same gap seen from opposite ends.

The owner supplied the mechanic from the original: a keeper starts with four
imps, four is a **floor** rather than an allowance — drop them all into the
dungeon heart and they come back at roughly five-second intervals — and beyond
that an imp must be bought with the Create Imp spell at 1500 mana, rising by
1500 each time.

**Every number in that is in DK2's own data.** The balance table names the first
two in its own words:

```
   94  MinimumImpThreshold                  4
   95  TimeBeforeFreeImpGeneratedSeconds    5
```

It is rare for an observation and a variable name to meet this cleanly, which is
why the free supply is CONFIRMED rather than inferred.

The 1500 needed finding. `KeeperSpells.kwd` is real shipped data that this
project does not parse — keeper spells arrive at M7 — but it holds 13 records
over 5278 bytes, which is exactly 406 each, and offset **0x11C** of that record
turns out to be the mana cost: Possession 500, Create Imp **1500**, Sight of Evil
5000, Thunderbolt 6000, Call to Arms 10000, Create Gold 15000, Turncoat 20000,
Tremor 30000, Inferno 50000, Summon Horny 100000. Thirteen plausible prices in an
order that matches how the game plays, one of them independently measured by the
owner: that is a structural validation, not a lucky hit.

**Decision.**

- `ImpSupplySystem` hands a keeper below `MinimumImpThreshold` one imp every
  `TimeBeforeFreeImpGeneratedSeconds`, at their dungeon heart, and stops.
- `CreateImpCommand` buys one for
  `CreateImpBaseCost × (imps − minimum + 1)` — 1500 for the fifth, 3000 for the
  sixth, and so on. The owner settled the shape of that escalation in a second
  message: *"four free (starting) imps don't raise mana cost… if you drop down to
  4, cost also drops to base 1500"*. **The price falls back**, so the cost is a
  function of the imps a keeper has now and not a tally of what they have bought
  — which means it needs no counter and cannot drift from the roster.
- **One hardcoded balance constant is admitted**, `CreateImpBaseCost = 1500`, in
  express violation of this project's own rule. It earns the exception by being
  read back out of the real `KeeperSpells.kwd` by
  `KeeperSpellsOnRealDataTests`, which also re-validates the offset across all
  thirteen spells. It goes when the record is parsed (Q55).
- `WorldState.RemoveCreature` exists so that an imp count can fall. Nothing calls
  it: death is a process nobody has modelled. A floor that cannot be tested by
  dropping below it is not a tested floor.

**Consequences.**

- **Mana can go down.** The first outgoing in the project, and the caveat every
  scenario printed is now false and has been rewritten.
- **Any world with a dungeon heart grows imps.** That is correct and it surprised
  two existing tests, which had heart tiles for their gold capacity and quietly
  acquired a workforce. Both were re-timed rather than exempted, because the
  behaviour is right.
- **`ImpSupplySystem` runs last**, after the mana it spends, so a keeper who earns
  the price on tick N can cast on tick N.

**What is NOT decided here, and it is the big one.** Imps cost mana to *keep* —
about 7 a second each, which is the red figure under a keeper's mana orb and
which the owner's screenshot corroborates at 4 × 7 = 28. It is not implemented
because **the 7 is in no file we parse**: not the balance table, and not the
CREATURES record, where no offset holds the Imp's 7 against the Horned Reaper's
reported 2000. Writing it in would be a second hardcoded constant with no file to
check it against, which is exactly the thing the first exception was allowed to
stay narrow. Q53, and it is the top of the research list.

---

## ADR-0022 — An imp costs mana to keep, and the number was on the spell

**Date.** 2026-08-30. **Status.** Accepted. **Supersedes** the closing paragraph
of ADR-0021, which said this could not be built.

**Context.** ADR-0021 ended by naming the largest hole in the economy: DK2
charges a keeper about 7 mana a second for every imp they have, and this
simulation charged nothing, so a keeper here could buy imps until the map was
full. It was left unimplemented on the stated grounds that **the 7 was in no file
we parse** — writing it into simulation code would have been a balance constant
with nothing to check it against.

Both halves of that blocker have now gone, in that order:

1. **The owner measured it.** Two screenshots of the mana orb: four imps read
   **−28**, and after summoning a fifth, **−35**. Seven a second per imp, and two
   readings rather than one, so the *unit* is confirmed too.
2. **The field was found**, once the search could be anchored on a number that
   was known rather than quoted. It is not on the creature — a rescan of all 5449
   bytes of the CREATURES record found four offsets holding the Imp's 7 and every
   one of them is an ArtResource frame count, an array-length header or
   uninitialised padding. It is on the **spell**: `KeeperSpells.kwd` offset
   **0x00E2**.

**Decision.**

- **`KwdKeeperSpells` parses two fields of the 406-byte KEEPERSPELLS record** and
  no more: the cast cost at 0x11C and the per-second upkeep at 0x00E2. Parsing a
  record badly is worse than not parsing it; the other 400 bytes wait for M7.
- **The upkeep offset is accepted on structure, not on the anchor.** It reads
  non-zero for exactly the five spells whose result persists — Create Imp 7,
  Summon Horny 2000, Call To Arms 90, Possession 25, OneShotReaper 125 — and zero
  for all eight instantaneous ones. A spell that is over when it lands has
  nothing to keep. **And the column carries its own second anchor**: 2000 is the
  community's Horned Reaper figure, which had been the right number in the wrong
  record all along.
- **Income and upkeep stay separate through the whole calculation** rather than
  being netted first. The original shows them separately, and
  `MaximumManaGainPerSecond` (500) caps only the income — netting first would let
  a big enough workforce raise a keeper's income cap, which is nonsense.
- **Every imp is charged, including the free four.** The owner's starting
  complement drains 28. The floor is free to fill and not free to keep, and that
  is the pressure the imp economy is built on.
- **Mana floors at zero and nothing else happens.** What the original does when a
  keeper cannot pay is UNKNOWN (Q57); `InsufficientManaImpEvaluationPeriodSeconds`
  (10) says *something* does, and guessing it would put imps in graves DK2 might
  not.
- **Keeper spells are keyed by their data-file name**, via `KeeperSpellName`,
  because the record's id field has not been identified and inventing an ordinal
  from record order would break silently if the shipped order ever differed.

**Consequences.**

- **ADR-0021's admitted exception is gone.** `CreateImpBaseCost` is a lookup now.
  Chasing the upkeep into `KeeperSpells.kwd` forced the parser to exist, so the
  hardcoded 1500 lasted exactly one session — which is the right lifetime for an
  exception granted on the promise that it would.
- **A synthetic database must now supply keeper spells.** Three test fixtures
  gained a Create Imp definition. `GameDatabase.RequireKeeperSpell` throws with
  the missing name in the message, which is the same loud-at-the-seam convention
  the balance variables use, and it is how the third fixture was found.
- **`GameDatabase`'s constructor takes spells as an optional last parameter**, so
  data that has none still loads. `KeeperSpell` returns null; only
  `RequireKeeperSpell` throws.
- **The pinned determinism hash did not move**, correctly: `TestWorld` has no
  workers and no mana-generating terrain, so there is nothing for the upkeep to
  charge. No `HashVersion` bump — no hashed field changed shape.

**Still not decided.** What a trap draws (Q59 — confirmed to exist, unmeasured,
`Traps.kwd` unparsed); whether possession really drains 25 (Q58 — the data
disagrees with itself and with the owner); and what happens at zero mana (Q57).

**Amended the same day, from a third screenshot.** The first cut of
`ApplyOneSecond` clamped income to the ceiling and *then* charged upkeep. The
owner then photographed a keeper at full mana: the counter reads exactly
**200000** while the orb shows **+400** and **−297**. Under the first ordering
that keeper would sit permanently at 199703. So the ceiling sees the **net**,
and the order is: cap the income at `MaximumManaGainPerSecond`, subtract upkeep,
then clamp to the threshold. The separation of the two figures still matters —
it just matters for the income cap and not for the ceiling. A detail that would
have been chosen rather than known if a screenshot had not caught it, and the
kind of thing nobody would have noticed for a long time.
