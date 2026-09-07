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

> ### ⚠ Correction, 2026-09-04: half of this was not true for four months
>
> **The no-Unity-reference half held.** `ArchitectureTests` really did check it
> and it really did pass.
>
> **The netstandard2.1 half never happened.** `Directory.Build.props` selected
> the framework behind a `Condition` on a `DK2UnityBound` marker that each
> project set *in its own body* — which MSBuild evaluates **after** importing
> that props file. The condition was therefore always false, both branches fell
> through to `net10.0`, and **every assembly in this repository built as
> net10.0 from M1 until 2026-09-04.** `src/Shared/Polyfills.cs` sat behind
> `#if NETSTANDARD2_1` and was never once compiled.
>
> Nothing noticed because nothing had yet handed a DLL to Unity. **Unity cannot
> load a net10.0 assembly at all**, so the entire presentation plan would have
> failed at the first attempt to use it. It surfaced the moment the Unity
> adapter was compiled against Unity's own assemblies (ADR-0036) — an
> `NU1201: not compatible with netstandard2.1` from the projects that were
> supposed to *be* netstandard2.1.
>
> **Fixed**: `src/Shared/UnityBound.props`, imported explicitly from the body of
> each Unity-bound project, where the ordering works. The cost of four months of
> silence turned out to be small — one `Enum.IsDefined` overload, one
> `Stream.ReadExactly` polyfill, and the pinned determinism hash did not move —
> but that was luck, not design: every month of net10.0-only API use would have
> made it worse.
>
> **Now enforced from the built artefact**, not from configuration:
> `ArchitectureTests.UnityLoadableAssemblies_TargetNetStandard21` reads
> `TargetFrameworkAttribute` off the compiled assembly. **The lesson is the
> general one: a constraint expressed only in build configuration is a hope, and
> "enforced at compile time" in an ADR title is a claim that itself needs a
> test.**

## ADR-0003 — ~~MIT license;~~ clean-room use of reference projects
**Date:** 2026-08-29 · **Status:** **licence half SUPERSEDED by ADR-0041
(2026-09-04); clean-room half still in force**

*(Original text, kept for the record. **The first sentence is no longer true** —
see the amendment below.)*

This project is MIT-licensed. Consequences:
- **OpenKeeper (GPLv3):** no code may be copied or transliterated. Facts
  learned from it (format layouts, enum values, record ids) may be used.
- **Flame (no license):** same rule, stricter reason — unlicensed means no
  reuse rights at all. Its symbol maps are consulted as research material.
- Every research note in `docs/research/` records provenance so any borrowed
  *fact* can be traced to its source.

> ### ⚠ Amended 2026-09-04: the project is GPLv3, and one of these three rules
> ### rested on the licence rather than on the method
>
> `LICENSE` is now GNU GPL version 3 and every source file carries
> `SPDX-License-Identifier: GPL-3.0-or-later` (ADR-0041, owner decision). The
> premise of this ADR is therefore gone. What it decided does **not** all fall
> with it, and the difference matters:
>
> | rule | still holds? | on what grounds |
> |---|---|---|
> | **OpenKeeper: no code copied or transliterated** | **YES — but the reason changed** | The *licence* bar is gone: GPLv3 code may be combined with a GPLv3 work. What still forbids it is (a) the brief's request for *clean-room behavioural reimplementation based on observed behaviour and documented research*, and (b) the owner's explicit standing instruction — *"reference it and compare with our own findings, not copy it"*. Both are project choices, and neither was ever a licence consequence. |
> | **Flame: no reuse rights at all** | **YES, unchanged** | Unlicensed is unlicensed. A copyleft licence on *our* side grants nothing over *their* work. |
> | **Provenance recorded for every borrowed fact** | **YES, and it matters more now** | Under GPLv3 a borrowed *implementation* would need attribution and notice retention, so the line between "we learned a fact" and "we took an implementation" has to stay visible in the record. |
>
> **What genuinely changed is that a door is now open that used to be shut**, and
> whether to walk through it is the owner's call rather than a licence question.
> The most tempting candidate is the one place this project has repeatedly said
> "nobody has solved this": OpenKeeper's creature AI is an acknowledged stub, so
> the temptation is smaller than it looks. Its **file-format readers**, by
> contrast, are the most complete in existence — and they are also the part we
> have already reproduced independently and cross-validated to the byte
> (`external-resources.md`, worked examples 2, 3 and 5).
>
> **Recommendation on record, for whoever asks next:** keep the clean-room rule
> as a project standard, and keep taking facts with provenance. It has been
> working — three scan-derived offsets and every record size confirmed against an
> independent source, and in the one case where OpenKeeper had something we did
> not (`ALTER_TERRAIN_TYPE`, Q74) the *mechanism* came from them and the *answer*
> came from sweeping the install, which is exactly the division of labour to keep.
> If the owner does decide to adopt code, it needs its own ADR, an entry in
> `THIRD-PARTY-NOTICES.md`, and retained upstream notices — not a quiet copy.
>
> The full boundary table lives in `docs/research/external-resources.md` and has
> been rewritten for GPLv3.

> ### ⚠ Second amendment, 2026-09-04: the owner walked through the door
>
> The recommendation above was to keep the clean-room rule as a project
> standard. **The owner overrode it the same day, explicitly:** *"if any of your
> doubts can be answered from OpenKeeper, feel free to copy, reuse, whatever, we
> are now on the same license as them."* That is a decision, not an ambiguity,
> and this is the record of it.
>
> **What the rule is now.** OpenKeeper may be used as a source of
> *implementation*, not only of facts, wherever it answers a question the shipped
> data cannot. Two obligations come with that and are not optional:
>
> 1. **Attribution and notice retention.** GPLv3 requires it. Every place a
>    borrowed implementation lands gets an entry in `THIRD-PARTY-NOTICES.md`
>    naming the upstream class and what was taken.
> 2. **The provenance line stays visible.** A rule taken from OpenKeeper is
>    STRONGLY INFERRED, not CONFIRMED — because OpenKeeper is itself a
>    reimplementation and can be wrong. Its confidence never exceeds the
>    evidence behind it, whatever its source.
>
> **First use, the same day.** The portal (ADR-0042) took three composition rules
> from `CreatureSpawnSystem` — the interval scaling, the pool as a standing
> limit, and both-slots attraction — none of which is derivable from the files.
> It also inherited a fourth thing worth more than any of them: OpenKeeper's own
> note that a documented original *quirk* (two portals producing on the same
> tick) was replicated deliberately, which is a fidelity judgement we would
> otherwise have had to make blind.
>
> **And a caution the same use produced.** OpenKeeper's portals hand a creature
> to whoever claims an idle one — but only because it resets the clock to Java's
> `Double.MIN_VALUE`, the smallest *positive* double rather than the sentinel it
> reads as. **The bug is indistinguishable from a decision unless you read the
> code closely.** Reuse is now permitted; taking it on trust is not.

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

**Status:** Accepted (2026-08-30). **Amended 2026-09-05 — the fifth is
Efficiency; see the amendment at the end of this ADR.** Supersedes the
granularity half of ADR-0011's placeholder status; ADR-0017's rule for
`…PerSecond` variables is unaffected.

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

**Amendment, 2026-09-05 (CONFIRMED by the owner, off DK2's own status bar).**
**The fifth is not a cadence. It is the worker's Efficiency divided by the four
ticks of a game second.** DK2 prints the stat next to a creature's name —
*"Imp. Name: Snipe. Job: idle. Efficiency: 80%. Mood: Happy."* — and 80% is the
`0xccc` = 0.8 fixed-point multiplier the work handlers read from
`creature[0x3af]`. So an application is `delta x 0.8 / 4`, which is a fifth of
the delta, and there are **four applications a game second, one a tick**, not
five.

**Nothing this ADR implemented changes.** The per-application amount and the
number of applications a job takes are exactly as measured; only the reason is
different, and the wall-clock rate still rides on Q1 as it always did.

**What it adds** is that Efficiency is a *variable*, which this ADR assumed away:
a slap raises it to 90% for a while - 60 seconds when this was written, 25 in
ADR-0062 once the slap handler was read, and Q159 either way - dropping an imp
next to a tile it has
marked raises it to 90% as well, and both at once give 100% — measured at 273,
309-310 and 341 gold a swing respectively. `SimulationTiming` keeps
`WorkApplicationsPerSecond = 5` because that reproduces the default-efficiency
case exactly; it becomes a per-creature Efficiency when the hand and the slap
exist. See `docs/research/tile-work.md`, "Efficiency".

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
  **⚠ AMENDED 2026-09-05 by ADR-0057:** "every
  `TimeBeforeFreeImpGeneratedSeconds`" is right for the second replacement
  onwards and wrong for the first. The original keeps a **deadline** that only
  moves when an imp is made, so a keeper who has been at full strength replaces
  their next loss **immediately**. The owner filmed it; the binary said why.
- `CreateImpCommand` buys one for
  `CreateImpBaseCost × (imps − minimum + 1)` — 1500 for the fifth, 3000 for the
  sixth, and so on. The owner settled the shape of that escalation in a second
  message: *"four free (starting) imps don't raise mana cost… if you drop down to
  4, cost also drops to base 1500"*. **The price falls back**, so the cost is a
  function of the imps a keeper has now and not a tally of what they have bought
  — which means it needs no counter and cannot drift from the roster.
  **CONFIRMED A SECOND TIME on 2026-09-05, from the binary** (ADR-0057): the
  original computes the same expression, clamped to at least one step, on a
  creature type read off the power's own record.
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

---

## ADR-0023 — One measured behaviour overrides one shipped number

**Date.** 2026-08-30. **Status.** Accepted, owner-approved.

**Context.** Two of this project's rules pointed opposite ways for exactly one
variable, and no amount of research was going to reconcile them.

`DigRockHealth` ships as **−5000**. Rock has 165 health. Under the CONFIRMED
five-applications-a-second model (ADR-0020) that is 1000 a swing, so a plain wall
falls in **one**. The owner dug one out in the original and counted **two**,
drawing the distinction that made the count usable: two swings do the damage, and
a third animation plays over the block already crumbling.

Two applications against 165 health bounds the per-application damage at
**[83, 164]** — under 165 or one swing would finish it, at least 83 or two would
not — so the per-second figure has to be in **[415, 820]**. The shipped value is
six to twelve times too big.

Everything that could make this a misreading was checked and came back negative:

- **No alternative variable.** The balance table was scanned for anything holding
  a value in [415, 820]. Six hits: two payday intervals, a boulder fear amount, a
  tremor damage, a music threshold and the mana gain cap. None is a candidate.
- **The model around it is confirmed.** The same five-a-second reading gives a
  gold seam 273 gold a swing and claiming four swings, both measured in the
  original by the owner, independently, on different days.
- **The value is an outlier in its own table.** Every other tile delta is in the
  tens or low hundreds — 825, 125, −300, −130, −8, 5, 50, 22. −5000 is the sole
  four-digit one.

**Decision.** Correct it to **−500**, in one named place.

- **`ObservedCorrections` is the only place in the project that may contain a
  number the data does not.** It carries the measurement, the arithmetic, the
  negative search and the rules for adding to it, and it is the single seam
  through which a corrected value can enter the simulation —
  `TileWorkRules.PerApplication` reads through it rather than off the database.
- **−500 rather than any other value in the window.** It is the only round number
  in [415, 820] and exactly a tenth of the shipped figure; an extra zero is the
  most ordinary way for a number to be wrong. It gives 100 a swing: 165 → 65 →
  destroyed, two applications, no coincidence required.
- **The correction fires only on the exact shipped −5000.** A mod, a patch or any
  data set carrying a workable number is left alone, so this is a correction to
  one known-bad value and not a policy about digging.
- **The raw value stays readable.** `db[VariableTypeId.DigRockHealth]` still
  returns −5000, and tests assert both it and the corrected behaviour.
- **Four rules govern adding a second entry**, written on the type: the behaviour
  must be measured rather than inferred; the shipped value must be shown
  incapable of producing it, with the arithmetic; the table must have been
  searched for an alternative variable and come back empty; and the correction
  must be guarded on the exact shipped value.

**Consequences.**

- **Five tests changed**, all of which had encoded the one-swing behaviour,
  including two that were only incidentally digging. Each now takes two swings.
- **This corrects the behaviour, not the explanation.** Whether the engine reads
  a different field, applies a divisor nobody has found, or ships a typo is still
  UNKNOWN, and finding out would let the override be deleted.
- **It is a precedent, and it is meant to be a narrow one.** The bar in the four
  rules is deliberately hard to clear: in three sittings of owner measurements
  this is the only number that has cleared it, and two others that looked like
  candidates (the imp mana upkeep, the Create Imp cost) turned out to be findable
  in the data instead. **Look for the field before reaching for this file.**

---

## ADR-0024 — An upward job costs the terrain's whole MaxHealth

**Date.** 2026-08-30. **Status.** Accepted. **Amends** ADR-0020 and the rule
recorded against Q18.

**Context.** Claiming, reinforcing and filling in convert a tile to its
`MaxHealthTypeTerrainId`. Until now they did it by *healing* the tile: work added
to `Health`, and the conversion fired when `Health` reached `MaxHealth`. So the
work required was the **gap** — `MaxHealth − Health`.

The owner stopwatched one imp reinforcing one wall: **13.38 s and 13.77 s**, both
carrying their own reaction time on the start and the stop, so the truth sits at
or below 13.38. At the confirmed five applications a second (ADR-0020) that is
66–67 swings, and **13.2 s is 66 exactly**. `ReinforceWallHealth` is 50 a second,
so 10 a swing, so the work required is **660 — precisely Rock's `MaxHealth`**, and
not the 495 the gap from its 165 starting health gives.

The gap rule predicts 50 swings and **10.0 seconds**, three and a half seconds
short of anything observed.

**The same rule reproduces claiming**, which was already measured: Dirt Path takes
4 swings at 165, and **4 × 165 = 660 = Dirt Path's `MaxHealth`**. So one rule fits
two independently measured jobs.

**Why this survived so long.** Dirt Path ships at **1** health, so its gap (659)
and its maximum (660) are within a single swing of each other. Claiming is
precisely where the two rules agree. Reinforcing is the only job where they
differ enough to see, and it needed a stopwatch to see it.

**A third confirmation of ADR-0020 falls out.** 66 swings over 13.38 s is 4.93 a
second. Under the gap rule it would be 50 swings over the same 13.38 s — 3.74 a
second — which contradicts the five-a-second reading that the 273-gold
measurement established. Two measurements taken on different days on unrelated
mechanics agree on the swing rate, **and only under the new reading**.

**Decision.**

- **An upward conversion needs `terrain.MaxHealth` worth of work**, counted from
  zero and independent of the tile's current health.
- **Progress lives in `TileState.UpwardWork`, not in `Health`.** Counting it in
  health would mean letting a tile climb past its own maximum while a job ran — a
  wall at 825 of 660 — leaking an impossible number into every snapshot, event
  and observer, and it would make a *damaged* tile take longer to convert, which
  nothing has observed. One integer is cheaper than either.
- **Reinforcing does not heal.** A wall's health is untouched while it is being
  reinforced; the conversion is what changes it. How DK2 stores this internally
  is UNKNOWN and is explicitly not a fidelity requirement (goal 5).
- **Repair jobs are unchanged** — they raise `Health` and stop at `MaxHealth`.
  The two used to share a code path, and sharing it was the bug.
- `UpwardWork` resets on any terrain change, along with `WorkRemainder`.

**Consequences.**

- **`HashVersion` 7 → 8 and snapshot `FormatVersion` 7 → 8**, and the pinned
  determinism hash moves with them. The fixture behind it does no upward work at
  all; the hash changed purely because the composition did.
- **Reinforcing a wall goes from 50 swings to 66**, 10.0 s to 13.2 s.
- **Claiming is unchanged at 4 swings**, which is the corroboration.
- **Filling in changes beyond recognition, and this is the one to watch.** Under
  the old rule it was *instantaneous*: Claimed Path ships at 660/660, so the gap
  was zero and one application converted it — the whole of Q22's puzzle. Under the
  measured rule it needs 660 of work at `FillInHealth`'s 1 a swing, so **660
  swings, about 132 seconds**. Neither figure has been observed. Instant was
  obviously wrong; **132 seconds is a prediction**, and it is now the cheapest
  unanswered thing on the observation checklist.
- The rule for **which** terrain a tile becomes is untouched — that is still
  `MaxHealthTypeTerrainId` selected by the work, which is Q18 and stands.

---

## ADR-0025 — Movement is eight-connected, and a diagonal costs √2

**Date.** 2026-08-30. **Status.** Accepted. **Supersedes** the four-connected
choice recorded on `GridPathfinder` since M2.

**Context.** `GridPathfinder` was four-connected on an explicit and reasonable
argument: an eight-connected search can return a route that squeezes diagonally
between two solid tiles, whether DK2 permits that was UNKNOWN, and a
four-connected route is always walkable by an eight-connected mover — so the
conservative choice could only ever be widened later.

**The conservative choice was the wrong one.** The owner watched the original
(Q28): a DK2 creature **will** pass diagonally between two solid tiles. Every
route this project produced was therefore an overestimate, in the one system
every other system depends on.

Implementing it needed one number nobody had: what a diagonal step costs. That
was Q48, and it was on the observation checklist as a stopwatch task for a long
time. **It should not have been.** A creature's speed is in *tiles per second*
(Q29 — three variables name that unit in their own words), so a creature moves
continuously through world space at a constant rate, so a step to a diagonal
neighbour covers **√2 tile-widths** and takes **1.41× as long**. There is no
mechanism by which it could be free short of the creature teleporting or
accelerating on the bias. Uniform cost was never a live option; it would have
been the invention.

**Decision.**

- **Eight-connected, and corners may be cut.** No corner-cutting check: a
  diagonal neighbour is reachable whatever its two orthogonal neighbours are,
  which is precisely what the owner observed.
- **Octile step cost: `StraightCost` 10, `DiagonalCost` 14**, the usual integer
  stand-in for 10√2 = 14.142. Integers throughout, so no square root, so no
  floating point, so no cross-machine drift.
- **The heuristic is octile distance**, which is the exact cost of the cheapest
  unobstructed route — admissible and consistent, so A* still returns shortest
  routes.
- **`TryFindNearest` becomes Dijkstra**, not breadth-first. This is the
  non-obvious consequence and it is the one that matters: once steps stop costing
  the same, a breadth-first sweep returns the goal reachable in the fewest
  *steps* rather than the nearest one. The owner confirmed an imp takes the
  marked tile nearest **by walking distance** (Q25), and three diagonal steps
  (42) are further than four straight ones (40).
- **The mover charges the same 14/10.** `CreatureMovementSystem` measured a leg
  with `|dx| + |dy|`, which is right for a four-connected path and charges 2× for
  a diagonal. It now uses the octile length. **A router and a mover that disagree
  produce creatures that arrive early**, which is a bug nobody would find by
  reading either one.

**Consequences.**

- **Every route in the game is shorter**, and journeys that were overestimated
  are now right. Scenario hashes move; the pinned determinism hash does not,
  because the fixture behind it has nothing that walks.
- **Three tests changed**, and each had a four-connected assumption written into
  it — a path-step adjacency assertion using Manhattan distance, a detour length
  of 8 that is now 4 diagonals, and a gold-carrying test pinned to a fixed tick
  count that the faster imp overran. The last was rewritten to run to the event
  it is about rather than to a clock.
- **What the *pathfinder* charges is still not evidence about DK2** (Q5). The
  mover's √2 is forced; the router's 14 is the only choice consistent with it,
  but whether the original's own search weights diagonals is unobserved and
  probably unobservable — in open uniform terrain both cost models produce paths
  of the same shape.

---

## ADR-0026 — Three workers to a face, and the fourth goes elsewhere

**Date.** 2026-08-30. **Status.** Accepted.

**Context.** The owner watched a block being dug in the original (Q26/Q43):
**three imps to a face, twelve to a block**, and an imp that cannot get onto a
face **goes and finds other work — it does not queue**. Ours converged without
limit, so a marked tile could soak up every imp a keeper had while the rest of
the dungeon stood still.

**Decision.**

- **The cap is on the face, not on the block.** `ImpCrowding.FaceIsFull` counts
  workers whose *target tile* is this one and whose *face* is this neighbour, and
  `TryJobBeside` skips a full face. **The twelve is not a rule and nothing counts
  to it** — it is the three, four times, because a tile has four orthogonal
  neighbours and work is orthogonal (Q27). Indeed the twelve is how the owner
  established that work is orthogonal in the first place: twelve at three a face
  is four faces, and eight-connected work would have given eight.
- **Rejecting the face rather than the tile is what makes the fourth imp leave.**
  The search simply carries on to the next standing tile, which may be another
  side of the same block or another block entirely, and if nothing is left it
  falls through to the next job priority. Nothing waits, which is what was
  observed.
- **A worker's face is derived, not stored**: the end of its route if it is still
  walking, the tile under it if it has arrived. Counting the *intended* face is
  what stops four imps setting off for the same side and only discovering the
  crowd on arrival. Deriving it keeps a field out of the determinism hash and
  makes it impossible for the face to disagree with the route.
- **The asking creature is excluded from the count**, or the third imp on a face
  would drop its own job the instant it re-evaluated it.
- **Counted across every player.** The limit is physical space on a tile, and two
  keepers' imps on the same side of the same block are in each other's way
  exactly as much as one keeper's would be.

**The three is an observed number with no source in the data, and that is a new
category.** `ObservedCorrections` exists for a shipped value that cannot produce
the observed behaviour; this is different — the files carry **no** value at all.
The balance table was searched and has no per-face, per-tile or crowding limit
for workers; the nearest thing to one, `OvercrowdingJailBreakPercentagePerExtraCreature`,
is about prisons. It is not derivable from geometry either: CREATURES gives a
creature a `Height` and no width or radius, so "how many fit along an edge"
cannot be computed from anything we parse. It lives in `ImpCrowding`, named,
documented, with the search recorded. **Q34's 50% sell refund is the other known
member of this category** and will join it when selling is implemented.

**Consequences.**

- **Imps now spread.** A crowd on one marked tile pushes the surplus onto other
  faces and then into other jobs, which is what a DK2 dungeon looks like.
- **No test broke**, which is worth noting rather than celebrating: every
  existing imp test uses one or two imps, so none of them ever reached the cap.
  Three new tests cover it.
- **The count is O(creatures) inside a search predicate.** Acceptable at the
  tens-of-creatures scale this runs at, and worth revisiting if a level ever
  carries hundreds.

---

## ADR-0027 — A room is an instance, and it is derived rather than stored

**Date.** 2026-08-30. **Status.** Accepted.

**Context.** A room here was terrain, tile by tile. Nothing could ask the
question most room rules turn out to need: *how big is the room this tile
belongs to, and where is this tile within it?* Five separate findings — all of
them measured in the original, all of them recorded — sat unimplementable on
that one gap:

| | |
|---|---|
| **Q52** | a dungeon heart holds **16000**, because 9 of its 25 tiles are the platform |
| **Q60** | a temple's capacity is its **rim** — 8, 12, 16 for 3x3, 4x4, 5x5 |
| **Q60** | a graveyard's graves are its **interior ring** |
| **Q45** | `RecommendedSizeX/Y` is the size below which a room **does nothing** |
| **Q61** | creature attraction wants a room's **tile count** (Bile Demon: hatchery ≥ 25) |

**Decision.** `RoomInstance` — a connected group of tiles with the same room
terrain and the same owner — found by `RoomIndex`.

- **Derived, never stored.** Membership is recomputed from the tiles rather than
  kept in `TileState`. Three reasons, and the second is the one that decided it:
  - stored membership must be maintained on **every** path that changes a tile —
    built, sold, dug out, destroyed, claimed by a rival — and the first one
    anybody forgets leaves a room disagreeing with the map;
  - **splitting and merging come free.** Cut a treasury in half and it is two
    treasuries on the next query, with no split logic to write and no chance to
    write it wrong. A tile joining two rooms merges them the same way. Both are
    tested;
  - it stays **out of the determinism hash**, being a pure function of state that
    is already hashed. **No new field, no snapshot format change, no
    `HashVersion` bump** — which for a change this size is worth a great deal.
- **Connectivity is orthogonal**, and **owner counts**: two keepers' treasuries
  meeting along a line are two treasuries, or a rival's tiles would inflate your
  capacity.
- **Two entry points, agreeing by construction** because they share one flood
  fill: `RoomAt` walks a single room, `Build` sweeps the map once. A caller
  looping over tiles uses `Build`; the test suite asserts they agree.
- **`GoldStore.Capacity` gained a room-aware overload.** The old tile-only one
  survives for the single caller that has a tile and no map — the spill
  calculation when a tile stops being a store — where the difference cannot bite,
  because a platform tile can never have had gold to spill.

**What this fixed, and what it merely enabled.**

- **Q52 is fixed.** A keeper's heart on the real campaign map now holds exactly
  **16000**, asserted against `level1`'s actual heart. The platform is found from
  the bounding box's centre, so it needs nothing from `TileConstruction`
  (`FiveByFiveRotated`, which we do not read) and a half-destroyed heart degrades
  sensibly instead of by a hardcoded 16.
- **Q45 is implemented** as `IsFunctional`, and pinned against the real record in
  both directions — Graveyard 3x3 and Temple 5x5 as the owner observed, and
  Treasury/Lair/Guard Room at 1x1 so that "every room needs to be big" could not
  fit the same evidence.
- **Q60's temple half is expressed** as `EdgeTileCount`, counted rather than
  computed from `2(w+h) − 4`, so an L-shaped room answers sensibly. Nothing reads
  it yet — no creature worships — but the geometry is tested against the owner's
  three measurements.
- **Q60's graveyard half is not implemented.** Two data points and an ad-hoc rule
  (interior tiles, less one for the centrepiece) is not enough; it needs one more
  graveyard size.
- **Q61 is not implemented.** Attraction needs portals, which are M6.

**Consequences.**

- `RoomIndex.Build` is O(tiles) and `RoomAt` is O(room). `GoldStore.Deposit` and
  `SpaceFor` build the index once rather than filling per tile.
- The fixture now transcribes `RecommendedSizeX/Y`, and the real-data drift guard
  checks them, because a rule reads them now.
- **No behaviour changed except the heart's capacity**, and no scenario hash
  moved.

---

## ADR-0028 — Floor is worked from on top of it, walls from beside

**Date.** 2026-09-01. **Status.** Accepted.

**Context.** Everything in this project was worked from an orthogonal
neighbour. That model came from Q27, which is CONFIRMED and about **walls** — a
gold block takes three imps a side and twelve in all, and twelve is four sides
of three — and it was then applied to floor as well, because at the time there
was nothing to say otherwise.

There is now. The owner's observation of the original (Q44, CONFIRMED) is
unambiguous: *"imps claim tiles by jumping on them"*, they stay on the tile
roughly half a second afterwards, and then move to an adjacent unclaimed one and
do it again. The last known defect on the M3 list.

It is not only where the imp stands. The **ownership condition moves with it.**
Claiming and reinforcing spread from ground the keeper already holds (Q42), and
that was implemented as *the tile the imp stands on must be owned*. An imp
standing on the tile it is claiming can never satisfy that, because the tile
being claimed is by definition not owned yet.

**Decision.** A worker works from the target tile itself when it can stand
there **both while it works and when the job finishes**; otherwise from an
orthogonal neighbour. `WorkStance` is the whole rule, and it is *derived* rather
than a list of work kinds:

| Clause | What it rules out | Why it is not a guess |
|---|---|---|
| standable **now** | dig, mine, reinforce, repair-wall | those tiles are `Solid`; nothing may stand on them |
| standable **when it finishes** | fill in | fill-in turns floor into rock (Q18) — a worker on it would be **entombed by its own work** |

Standability is asked of the same `IPathGrid` the mover will route over, so the
stance is per worker and cannot pick a tile the pathfinder then refuses.

**And the ownership condition follows the stance:**

- **worked from beside** → the **standing** tile must belong to the keeper
  (unchanged);
- **worked from on top** → the **target** must touch a tile that belongs to the
  keeper.

Both say the same thing about the map — territory grows outwards, never in
islands — and they differ only in where the imp's feet are.

**Confidence, split honestly.**

- **CONFIRMED** for claiming. This is what the owner watched.
- **STRONGLY INFERRED** for repairing and attacking floor: identical geometry — a
  walkable tile that stays walkable — and no observation distinguishes them (Q49).
- **STRONGLY INFERRED** for fill-in, from the entombment argument rather than
  from anything anybody has seen.

Deriving the rule is what makes those last two something other than three
guesses made alongside one confirmation, which was the reason this sat waiting
on Q49. Two physical clauses answer all ten work kinds; a hand-written list
would have been the invention.

**What is deliberately still absent.** The **~500 ms an imp lingers** on a tile
it has just claimed. Whether that is a real cooldown or the claim animation
finishing is UNKNOWN and the two are indistinguishable from outside (Q49). An
imp here goes straight on to the next job.

**Consequences.**

- `ImpTaskSystem.TryJobBeside` became `TryJobFrom`, and now considers the tile
  underfoot before the four around it — a job that can be done where the worker
  already is beats one it must reach for.
- The crowding cap (ADR-0026) applies unchanged: for a job done on the tile, the
  face *is* the tile, which is the same rule the cap already states — three
  workers is physical room to stand.
- One `WorldPathGrid` is now built per worker per tick and threaded through, so
  that routing, stance and arrival are all answered against the same world.
- **Visible in the scenarios**: a claim in `imps-carry-gold-home` now reads
  `steps=2` where it read `steps=1` — the imp walks onto the tile.
- `TileWorkRules` gained `PerSecond` and `OutcomeTerrainId`: "what will this tile
  be when the job is done" is now askable without simulating it.
- No new state, so **no `HashVersion` bump** — but imp *positions* differ, so
  scenario hashes moved.

---

## ADR-0029 — Selling a room returns half its cost, and the data said so all along

**Date.** 2026-09-03. **Status.** Accepted.

**Context.** Q34 asked what selling a room refunds. The owner observed **50% of
the cost, per tile** on 2026-08-29, and this project recorded the answer with a
warning attached: that the number appeared in *neither* candidate variable, since
`RoomDef.ReturnPercentage` and `RoomSellValuePercentageOfCost` "both say 100". It
was written up as the next member of the `ImpCrowding` category — a behaviour
observed in the original with no value anywhere in the files — and the pick-up
list carried it for four sessions on that footing.

**That was wrong, and it was our own error.** `RoomSellValuePercentageOfCost` is
**50**. `docs/research/game-database-dump.md` — a file this project generated
from the install — has said 50 since it was written. The 100 came from a
transcription that nobody checked against the dump, and having believed a number
absent, the plan was to invent an override for it.

**Decision.** Selling is implemented, entirely from the data.

**The refund.** `RoomRules.RefundPerTile` = `Cost` × `ReturnPercentage`% ×
`RoomSellValuePercentageOfCost`%. With the shipped numbers that is half the cost
per tile for every buildable room: a treasury 100 of its 200, a temple 1500 of
its 3000. Exactly what the owner measured.

**`ReturnPercentage` is a per-room gate, not a rival refund rate.** This is the
reading that makes the two variables agree rather than compete:

| | |
|---|---|
| **100** | every room a keeper can build — 17 of them |
| **0** | exactly the hero gates, which a keeper cannot sell |

Nothing in between. That the zero rows coincide precisely with the unsellable
rooms is the internal check, and it is asserted in both directions so that "the
field is always 100" could not fit the same evidence. Whether the two numbers
multiply or the per-room value overrides the global is **indistinguishable on
shipped data** — no room carries a middle value — so the behaviour is settled and
the composition is not. They are multiplied, which is the reading that still
behaves sensibly on data we do not have.

**Sellable means buildable.** The same `Buildable` flag that decides what a
keeper may put down decides what they may take up, so the two rules can never
disagree and nothing in the code names a portal or a dungeon heart.

**The tile reverts to its terrain's own `DestroyedTypeTerrainId`**, which for
every room terrain in the shipped data is Claimed Path — and **CONFIRMED by the
owner the same day**: a sold treasury tile becomes claimed floor. Ownership is
not cleared, for the same reason `TileWorkRules` never clears it: selling your
own room does not hand the ground to nobody.

**What happens to gold on a sold treasury tile is CONFIRMED and half
implemented.** The owner: the gold **drops onto the claimed floor tile that
replaces the store** and comes off the keeper's spendable total until somebody
fetches it. Two of those three things are reproduced — the tile reverts, and the
total falls by exactly that much, so the *economy* of a sale is right. The pile
is missing, because a pile is an OBJECT and objects arrive at M6 (Q31). So a
keeper here loses permanently what a keeper there can pick back up, and the run
says so as `gold_displaced` rather than hiding it. Same for a refund with nowhere
to go (`refund_lost`).

**Ordering.** `RoomSellSystem` runs **before** `RoomBuildSystem`, so a keeper can
sell one room to afford another on the same tick — the friendlier of the two
orderings, and the same reasoning as `ImpSupplySystem` running after the mana it
spends. Within a command, tiles are sold one at a time and each refund is banked
before the next tile goes, so a keeper selling their whole treasury runs out of
somewhere to put the money at a point the run can name.

**Consequences.**

- Doors and traps come free: `DoorSellValuePercentageOfCost` and
  `TrapSellValuePercentageOfCost` are **50** as well, which answers the owner's
  next question before they tested it. Neither is implemented — a door and a trap
  are objects (M6).
- **Selling a bridge is known wrong, and the data is why** (Q64). A wooden
  bridge's destroyed type is Claimed Path like every other room terrain, so
  selling a bridge over water leaves walkable floor where there should be water.
  The tile does not record what was underneath — the MAP record's byte 3 is
  bridge-related and unparsed (Q16). Reproduced as the data has it rather than
  special-cased, and the event names it.
- Selling is **instant**; in DK2 an imp walks over and dismantles the tile, which
  is the same missing worker job as building (Q24).
- The harness gained a `sell:` command, and
  `scenarios/rooms/sell_a_room_back.yaml` runs the whole thing — including a
  dungeon heart being refused in the middle of a selection.
- **No new state, so no `HashVersion` bump.** The economy now has its first sink
  that runs backwards.

---

## ADR-0030 — The crowding cap follows the stance: three to a wall, one to a floor tile

**Date.** 2026-09-03. **Status.** Accepted. **Refines** ADR-0026.

**Context.** ADR-0026 implemented the owner's observation of three imps to a face
and twelve to a block, and justified it as *"the limit is physical space on a
tile"*. ADR-0028 then gave floor jobs a different stance — an imp claims a tile
by standing **on** it — and reused the same cap on the grounds that standing on
the target *is* standing on a tile, so the same rule applied.

**That reasoning was wrong, and the owner's next observation says so**
(2026-09-03): *"only one claiming imp jumping per unclaimed tile is allowed"*.

Three imps really do share a single tile to get at a wall. Only one may stand on
a floor tile to claim it. So the cap is **not** about how many bodies fit on a
tile — if it were, the two numbers would agree. It is per job.

**A test was passing for the wrong reason because of this.**
`AnImpDoesNotCountItselfOutOfItsOwnFace` ticked four times past its crowding
assertions and checked three imps still held the block. Three imps swinging
together put 300 damage into a plain wall's 165, so the block had already fallen
and the tile had become claimable floor — what the assertion actually read was
**three imps claiming one tile**, which the original does not do. The wrong cap
made a wrong test green.

**Decision.** `ImpCrowding.MaxWorkers(workedFromTheTile)` — **1** on the tile,
**3** from a neighbour — and `FaceIsFull` takes the stance. `ImpTaskSystem`
already knows which stance a job uses (ADR-0028), so nothing new is computed and
no work kind is named.

**Confidence.** CONFIRMED for both numbers, and **neither is derived**. That is
worth stating plainly, because the pull towards one unifying number was what
produced the error: the tidier story ("a tile holds three workers") is refuted by
the very observation that gave us the second number. Both are here because both
were watched.

**Also CONFIRMED the same day:** *"imps claim tiles adjacent to claimed tiles"* —
which is Q42 exactly as implemented since ADR-0028, the ownership condition
sitting on the target's neighbour rather than on the imp's feet. **Whether
"adjacent" includes diagonals is UNKNOWN** and now matters, because movement is
eight-connected (ADR-0025) so a corner-touching tile is reachable. Orthogonal is
what is implemented, matching the way work spreads (Q27). New as Q66.

**Consequences.**

- Visible in the scenarios: `imps-carry-gold-home` previously had both imps claim
  the tile they had just mined out; now one claims it and the other goes to
  reinforce, so its hash moved.
- The claim of ADR-0026 that the cap counts across every player still holds — two
  keepers' imps are in each other's way — but its stated *reason* is replaced by
  this one.
- `ImpCrowding`'s note about the balance table having no crowding limit for
  workers now covers two numbers rather than one; neither is in any file we read.
- No new state, so **no `HashVersion` bump**.

---

## ADR-0031 — A bridge reverts to the hazard it spanned, and the MAP record says which

**Date.** 2026-09-03. **Status.** Accepted. **Answers** Q64, and the bridge half
of Q16.

**Context.** ADR-0029 took a sold room's tile back to its terrain's own
`DestroyedTypeTerrainId`. That is Claimed Path for **every** room terrain in the
shipped data, the two bridges included — so selling a bridge left walkable floor
where there had been water or lava. It was recorded as Q64: known wrong,
reproduced deliberately, with the note that the tile "does not record what was
underneath it" and that unblocking it needed Q16.

The owner then confirmed the behaviour (2026-09-03): **a sold bridge reverts to
the water or lava it spanned.** So the hazard *is* recorded somewhere.

**Decision.** It is recorded in a byte the project had been carrying since M2
without a meaning. `TileState.Flags` — the MAP record's third byte per tile,
commented "bridge-related; exact meaning UNKNOWN, preserved verbatim" — **marks a
bridge over lava.**

The evidence, swept over all **66** shipped level maps:

| | |
|---|---|
| the byte takes **no value but 1 or 2** anywhere in the corpus | so there is nothing else left for it to mean |
| **2 occurs only on bridge terrain** | never on any of the other 38 terrain types |
| every `2` bridge tile **borders lava** | across all three bridge types |
| every `1` bridge tile that touches a hazard **borders water** | the two sets never cross |

`BridgeRules` holds the reading, and `BridgesOnRealDataTests` pins all four
claims so that a data set breaking any of them fails loudly rather than quietly
reverting a bridge to the wrong thing.

**Confidence, split.** The **behaviour** is CONFIRMED — the owner watched it. That
**this byte** is where the original keeps it is **STRONGLY INFERRED** from a clean
partition over 66 maps; it could be a rendering hint that happens to correlate
perfectly. Nothing depends on being right about the original's storage, only on
the revert being correct, and the project's brief puts internal representation
explicitly outside what must match.

**A bridge cannot be built on land**, so for a bridge tile the byte is a straight
binary — and that is the ROOMS record rather than an assumption, since neither
bridge carries `PlaceableOnLand`.

**One rule, one place.** `BridgeRules.RevertsTo` is now the only thing that
decides what a removed tile leaves, and both callers go through it: selling
(`RoomRules.Unmake`) and being beaten down (`TileWorkRules.Apply`'s destroyed
path). Those two used to consult the destroyed-type field separately and **both
were wrong in the same way** — a rival could already attack a bridge tile, since
`AttackTile` applies to any non-solid ownable terrain somebody else owns.

**A player-built bridge writes its own marker.** The level file speaks only for
bridges it shipped with, so `RoomRules.Place` records the hazard it is covering,
and `Unmake` clears the marker again so a tile bridged twice cannot inherit the
first hazard.

**The near-miss worth keeping.** The first attempt at this compared each level's
*remaining* water and lava counts against its bridges, and appeared to refute the
reading: Furnace has bridges of both markers and, apparently, no water at all.
That is because **a fully bridged pond leaves no water to count** — the bridge
tiles *are* the pond. Counting hazards nearly killed a correct reading; asking
what each tile **borders** is what made the partition visible.

**Consequences.**

- **No new state and no `HashVersion` bump.** The byte was already in
  `TileState`, already hashed, already snapshotted. It went from carried to
  understood.
- `GameDatabase` gained `WaterTerrain` and `LavaTerrain`, found **by flag** and
  not by id, like every other gameplay lookup here. The install has exactly one
  of each, which is asserted.
- The owner byte is **still not cleared** when a tile reverts to water, matching
  `TileWorkRules`: which owner id means "nobody" is UNKNOWN (Q17), every rule that
  cares asks `IsOwnable` first, and inventing a neutral id would be the riskier
  move.
- **Q67 became implementable.** `WoodBridgeLifeOnLavaSeconds` is 30 — a wooden
  bridge over lava burns away — and that rule needs to know which bridges are over
  lava. Nothing here could tell before; `BridgeRules.SpannedTerrain` now can. Not
  implemented: the *shape* of the countdown is UNKNOWN.
- The test fixture gained a Lava terrain, because a fixture with one hazard
  cannot tell a correct revert from a lucky one.

---

## ADR-0032 — A wooden bridge over lava burns, and the clock starts when it is built

**Date.** 2026-09-03. **Status.** Accepted. **Answers** the tile half of Q67.

**Context.** `WoodBridgeLifeOnLavaSeconds` is 30 in the balance table — a
variable that names its own mechanic and that nothing in this project read.
ADR-0031 made it implementable for the first time by establishing which bridges
are over lava; before that, a bridge tile's terrain said "Wooden Bridge" and its
destroyed type said "Claimed Path", neither of which mentions the hazard.

What was still missing was the **shape**. The variable's name fits three readings
equally well: the clock runs from the build, from the first footstep, or as
damage to the tile's health. The owner settled it (2026-09-03): **the clock
starts when the bridge is built.**

**Decision.** A countdown of its own — `TileState.BurnTicksRemaining`, set by
`RoomRules.Place`, spent by `BridgeBurnSystem`, and cleared by every path that
takes the tile away.

**Not health decay**, and the reason is not tidiness. `TileState.Health` is
already what a rival attacking the bridge reduces, and a wooden bridge over
**water** carries exactly the same 2000 health and never burns. Modelling fire as
health would make combat damage and fire the same quantity, which they are not.

**Which bridge burns is read from the room type**, and this is the one rule in
the bridge code that names one. The justification is that **DK2's own variable
names it** — `WoodBridge…` — and the terrain table cannot help: Wooden Bridge and
Stone Bridge carry *identical* flags (0x30000034) and identical health, and
neither carries `TerrainFlags.Decay`. Asserted against the install, so a data set
where they diverged would say so. Reading the room's identity here is following
the data rather than working around it.

**What is CONFIRMED and deliberately NOT implemented — the larger half.** The
owner described the whole mechanic:

| | |
|---|---|
| everything on the tile | **falls into the lava** |
| a dead body | **disappears** |
| a living creature | **burns until it gets out of the lava or its health reaches zero** |
| some creatures | **immune** |
| gold | **UNKNOWN** |

None of it is built, because **creature health does not exist**:
`CreatureState` carries only the fields a built mechanic reads, and nothing
damages a creature. Lava's harm is not a bridge rule in any case — it is
`TerrainDef.Damage`, 100 a second, the same field that makes a Hero Lair heal at
−67 — so it belongs with creature health and terrain damage generally, not here.
The tile half stands on its own and the event log says what is missing.

**The immunity is already in the data, and it extends the owner's list.**
`CanWalkOnLava` falls on exactly seven creatures: the **Salamander** and
**Giant** they named, the two unique variants, plus **Firefly**, **Fairy** and
**Horny**. Two fliers and the Horned Reaper is precisely the set one would guess,
so this corroborates an admittedly partial list ("some creatures, like…") rather
than contradicting it. Pinned in both directions — the **Imp is not immune**,
which is what makes a cheap wooden bridge a real trap.

**Gold is not worth an observation yet.** The owner offered to play a game to find
out what happens to gold on a burning bridge. It is blocked behind objects (M6)
either way, so we could not model the answer if we had it; better to ask when it
matters than to spend their time now.

**A creature caught on a burning bridge is unharmed here but not stranded.** The
pathfinder enters a mover's starting tile without testing walkability, so a
creature standing in lava can still walk out — a rule written long before
anything existed that could turn the ground under a creature impassable, and this
is its first real cause. Tested.

**Consequences.**

- `HashVersion` 8 → 9 and snapshot `FormatVersion` 8 → 9, with the pinned literal
  updated in the same commit. First state addition since ADR-0024.
- `BridgeBurnSystem` runs **after** `TileWorkSystem`: a bridge somebody was
  repairing on the tick it ran out gets the repair and burns anyway, so the fire
  is final rather than something a keeper can outrun by one tick.
- A stone bridge over lava is permanent, which is what makes the wooden one a
  real decision in the original — 200 a tile and temporary against 500 and
  forever.
- A level-authored wooden bridge over lava does not burn here, because the clock
  is set at build and a shipped bridge has no build event. Moot in practice: the
  campaign contains **no** wooden bridge over lava anywhere, which is presumably
  the designers avoiding exactly this.

---

## ADR-0033 — A rival claims your ground directly, and hostile expansion falls out

**Date.** 2026-09-03. **Status.** Accepted. **Answers** Q68. **Refutes** the
two-step reading this project had carried since M3.

**Context.** `TileWorkKind.Claim` applied only to terrain that was **not
`Ownable`** — unclaimed Dirt Path. A rival's claimed floor was therefore not
claimable at all, and the implied model was two steps: damage the tile to neutral
with `AttackTileHealth` (−140), then claim the neutral tile with
`ClaimTileHealth` (825). Nothing had tested it, because no imp ever chooses
`AttackTile` and `JobPriority` has no entry for taking enemy ground.

The owner settled it in two messages (2026-09-03). First: a bridge can only be
"attacked" by a **rival keeper's imps, who jump on it and claim it if it is
adjacent to their keeper's territory**. Then, asked directly whether the tile
turns neutral on the way: **"no it doesn't go to neutral first, it is immediately
claimed by an imp."**

**The two-step model is refuted.**

**Two derived rules predicted the rest of it, which is the part worth
remembering.** Both halves of the owner's first sentence were already implemented
here, from evidence that had nothing to do with rival keepers:

| the owner said | this project already had |
|---|---|
| *"jump on it"* | `WorkStance`'s on-the-tile stance — derived from "standable while working and standable when the job finishes", and written without a thought for bridges (ADR-0028) |
| *"adjacent to their keeper's territory"* | the ownership condition on the **claimed tile's neighbour** (Q42, same ADR) |

Two derived rules predicting an observation neither was built from is the
strongest evidence either has had, and both are now pinned by tests that say so.

**Decision.**

- **`Claim` applies to any non-solid tile that is not already yours** — neutral or
  a rival's. The old `!IsOwnable` test is replaced by
  `tile.OwnerPlayerId != playerId`, plus a "nowhere to go" guard: either the
  terrain is already the claimable kind, or it converts into something.
- **Claiming ground that is already `Ownable` changes only the owner**
  (`UpwardDestination`). This is not a nicety: a claim normally converts to
  `MaxHealthTypeTerrainId`, and for Claimed Path that field is **Rock** — its
  fill-in destination (Q18) — so following it would have turned a rival's floor
  into a wall. Decided by the terrain's own flag, so the rule reads identically
  for a rival's floor, bridge and room.
- **`Convert` no longer resets `BurnTicksRemaining` when the terrain is
  unchanged.** A rival claiming a burning wooden bridge takes the bridge, fire and
  all; putting the fire out by capturing the tile would be a reward for capturing
  it (Q67).

**Hostile expansion came free, and that is the point.** Claiming is already
second in the CONFIRMED priority order (ADR-0018), already requires the tile to
touch the keeper's territory, and is already done standing on the tile. So imps
now take a rival's border tiles **unprompted, with no new decision logic and no
new job kind** — exactly the behaviour the owner described. A mechanic that would
have been a milestone's work arrived as a two-line change to one predicate,
because the rules around it were right.

**Confidence.** CONFIRMED for a rival's **claimed floor** (measured) and for a
**bridge** (measured). **STRONGLY INFERRED** for a rival's **rooms** — the same
rule, applied uniformly, because a bridge *is* room terrain and singling other
rooms out would need evidence nobody has.

**Rate.** `ClaimTileHealth` (825) for neutral and enemy alike, because the table
offers nothing else. A mana vault has *dedicated* neutral and enemy claim rates
(4 and 20) which nothing reads — so DK2 does differentiate for at least one
thing, and our generic rate is certainly wrong for a vault. Q69.

**Consequences.**

- **No scenario changed.** Verified by running the corpus with and without the
  change: identical hashes. No shipped scenario has two keepers with adjacent
  claimed floor, so the new behaviour is unit-tested rather than scenario-tested,
  and that is worth knowing rather than assuming.
- **`AttackTileHealth` now has no user at all.** It must have one in the original;
  the obvious candidate is creatures other than imps smashing enemy floor during
  an invasion, which would make attacking and claiming two actors' jobs rather
  than two steps. HYPOTHESIS, recorded as Q69.
- No new state, so **no `HashVersion` bump**.
- A tile of `Ownable` terrain owned by id 0 is now claimable, which matters
  because **which owner id means "nobody" is still UNKNOWN** (Q17). The rule is
  right either way — id 0 is either nobody, in which case claiming neutral floor
  is correct, or a keeper, in which case it is hostile claiming — but it is the
  first rule whose reach depends on that question.

---

### ADR-0031 amendment — independent corroboration of the MAP flags byte

**Date.** 2026-09-03.

ADR-0031 read `TileState.Flags` (the MAP record's third byte) as marking a bridge
over lava, on the strength of a clean partition across 66 shipped maps, and rated
that **STRONGLY INFERRED** because the byte could have been a rendering hint that
happened to correlate.

A 2013 KeeperKlan reverse-engineering thread — reached through web.archive.org,
since the forum now returns 403 — documents the on-disk element as
`{BYTE idTerrain; BYTE idPlayer; BYTE flags; BYTE unused;}`, notes that **"only a
'2' bit here is interpreted to do anything special at load, 1 may indicate
'valid'"**, and adds that the 2 bit *"assigns an as yet unknown flag in the
runtime map element"*. The author says they had been "upside down inside of this
executable for the past year and a half".

That independently confirms three things we had inferred: the byte **is** a flags
field, **2 is the only value that means anything**, and the fourth byte is
**unused** (which closes the second half of Q16 rather than leaving it merely
uninteresting).

**What it does not supply is the meaning** — that was explicitly open there. So
the two approaches turn out to be complementary rather than redundant: static
reversal of the executable finds the fields, and sweeping the shipped maps for
what each bridge tile *borders* finds what they mean. Worth remembering as the
expected shape of value from these resources.

The confidence rating stays **STRONGLY INFERRED**: the field is now confirmed,
our reading of its purpose is still ours. Facts quoted with provenance and no
code taken — see `docs/research/external-resources.md` for the licence boundary
this sits inside.

---

## ADR-0034 — An asset is named by a logical key, and a provider chain resolves it

**Status.** Accepted.
**Date.** 2026-09-04.

Goal 4 (`AGENTS.md`) draws the pipeline as
`Game Definition → Asset Reference → Asset Provider → Actual Asset`, and
`GOAL_ALIGNMENT.md` gap 3 attaches a deadline to it: **the asset layer must land
in the same milestone as the first Unity view, not after it.** Cheap today,
because nothing depends on an asset; expensive the moment a renderer exists.

**The decision.** `DK2.Assets` holds four things and no more:

- **`AssetRef`** — a validated logical key, `category.subject.role[.variant]`,
  built from a record's **name** and never its id. Same reasoning that keeps
  gameplay rules off terrain ids: an id is a fact about the reference install, a
  name survives a mod and a re-extract.
- **`IAssetProvider`** — given a key, answer or say you cannot. **Never throw for
  a key you do not have**, because "no asset" is a normal condition in a project
  that must run with no assets at all.
- **`AssetProviderChain`** — first answer wins, in the declared order
  `replacement → converted → placeholder`. Order *is* the policy; a
  "best answer" rule would need a judgement no code here can make and would make
  the result depend on what happens to be installed. Same reasoning as ADR-0013.
- **`PlaceholderProvider`** — **total**. Answers every valid key with a coloured
  box whose colour is derived from the key's hash, so a type is the same colour
  in every run, on every machine and in every renderer.

**Why the placeholder is load-bearing rather than a fallback.** Plan §7.2 asks
that the game *run* with no DK2 install and no art pack, so that "shippable
without proprietary assets" is continuously tested instead of aspirational. That
is only true if the provider is total — and `AssetCatalogue` makes it checkable
by enumerating every key the shipped database implies, which
`AssetCatalogueTests` then resolves one by one with nothing installed.

**Consequences.**

- **No filename literal exists outside `DK2.Assets`** (plan §4.8). One type turns
  a key into a path, mechanically: `creature.imp.mesh` →
  `creature/imp/mesh.glb`. An art pack's layout is knowable from the key alone,
  with no manifest to fall out of date.
- **`AssetRef`'s validation is a security property, not tidiness.** A key segment
  is `[a-z0-9_]` only, so it cannot be `..`, absolute, or contain a separator —
  which is what makes the dot-to-separator mapping safe. Refusing an unmappable
  character also catches the real bug: a name carrying `.` or `/` did not come
  from a record, and sanitising it would mint a believable key for a wrong thing.
- **The separator set is measured, not assumed.** The first version allowed only
  space, dash and apostrophe; the shipped terrain table carries
  `Hero Gate [ Final ]` and refuted it on the first run against real data.
- **The catalogue claims only what the game asks for.** DK2's creature record
  names about thirty animations; ours lists the four activities the simulation
  can distinguish, checked equal to `CreatureActivity` by a test. Enumerating
  DK2's full set would put keys in a converter's work list that nothing requests.
- **`DK2.Assets` must not reference Unity or the simulation**, asserted by
  `AssetArchitectureTests`. It is the abstraction; the engine-specific half lives
  in the Unity project.

**What this does not decide.** It says nothing about how many assets a terrain
needs. `AssetCatalogue` currently asks for one mesh per solid terrain, and that
is **known to be a simplification** — DK2 picks wall pieces by the size and shape
of the room behind them (`wall-rendering.md`, Q71). `AssetRef` already carries a
variant segment, so the shape is right; the variant list waits for evidence.

---

## ADR-0035 — Rendering goes through one view, and the harness draws it as text

**Status.** Accepted.
**Date.** 2026-09-04.

Plan §4.7 puts the presentation boundary at `SimulationEvent` out and
`InputFrame` in, with Unity holding no authoritative state. That says what may
cross; it does not say who decides *which asset a tile needs*.

**The decision.** `DK2.Presentation` owns that translation exactly once.
`WorldView` turns `WorldState` into `TileView` / `CreatureView` — readonly
structs carrying asset keys, fixed-point positions and the few state bits a
player can see — and every renderer consumes those.

**Why not let each renderer read `WorldState`.** Three decisions would otherwise
be repeated per renderer: which asset key a terrain implies, which animation an
activity implies, and how a room's floor differs from its furniture. Two
renderers disagreeing about those looks exactly like a simulation bug, which is
the most expensive kind of confusion this project can manufacture. The view also
has to agree with `AssetCatalogue`, because **a key the catalogue never
enumerates is a key no converter ever produces** — a real failure mode with no
error anywhere, and now a test.

**And the harness draws it as text.** A second renderer is the only thing that
actually proves the boundary is one: if the view types were secretly
Unity-shaped, `TextRenderer` would not compile. It also **runs in CI**, which the
Unity view will not, so "the presentation layer still works" is a test result
rather than something somebody notices on opening the editor. Every scenario run
now writes `world.txt` and a picture into its report, with a line saying where
the art came from — normally "all placeholders", and saying so is the point.

**Consequences.**

- Positions cross the boundary in `Fp32` tile units, untouched. A view that
  helpfully divided would throw away the sub-tile position a renderer needs to
  interpolate, and ADR-0001 puts that conversion on the renderer.
- A tile reports `HealthPermille` rather than health, so a renderer needs no
  terrain table. DK2 shows wall damage by swapping textures, which corroborates
  both the fraction and the eventual asset kind (`wall-rendering.md`).
- A scenario report now shows *where* two runs differ, not only that they do —
  the gap the hash series has always had.
- **Observation is a convention here, not a compiler guarantee**, and this is the
  honest caveat: `WorldState.Tile` hands out a `ref`, so a determined renderer
  could still mutate. The view copies by value and a test asserts that building
  and rendering a whole view leaves the state hash untouched. The guarantee the
  plan actually wants — deleting every `GameObject` changes nothing — holds by
  construction, since the simulation references none of this.

**What is NOT decided, and is the reason this ADR is not "the Unity view".**
There is no Unity project. Unity is not installed on the development machine, so
any Unity code written here could not be compiled, let alone run, and this
project does not commit code it has not built. What is done is everything the
Unity view needs to be thin: the boundary, the asset layer, and a working second
renderer proving both.

### Amendment, 2026-09-05: objects cross the boundary too

`ObjectView` joins `TileView` and `CreatureView`, `AssetRef` gains an
**`object`** category with one mesh per definition, `IRenderTarget` gains
create/update/remove for objects, and both renderers draw them.

**This was a hole rather than an omission.** Objects have been in the world
since ADR-0054 — beds, coops, eggs, and since ADR-0055 piles of gold — and every
renderer drew bare floor where one stood. A scenario picture of a sold treasury
showed the gold vanishing; the Unity view of a claimed lair showed no beds. The
tools this project uses to check its own work were silently lying about a whole
category of state.

Three decisions inside it:

- **an object is a category of its own, not part of `room`.** An object outlives
  the room it was placed for: a bed belongs to the creature that dropped it, and
  a pile of gold on bare floor belongs to no room at all;
- **a mesh and no variants.** DK2 gives an object a state machine and a
  `MoveTo` block, so a pecking chicken and a rolling boulder will want
  animations — but nothing here can distinguish one object state from another
  yet, and a variant list invented now would put keys in a converter's work list
  that correspond to nothing. The same restraint the wall pieces get (Q71);
- **objects are pushed before creatures, and drawn under them.** A creature
  stands on an object. The order lives in `ScenePresenter` so the Unity target
  and the text renderer cannot disagree about what occludes what.

An object still has no sub-tile position, because the simulation has none —
`ObjectView` carries a tile. DK2 places a pile at one of eight fixed points
around a tile centre and a dying creature's gold anywhere in its tile
(`gold-on-the-floor.md` §7); reproducing that needs the field, and the field
arrives with the first mechanic that moves an object.

---

## ADR-0036 — Unity 6 LTS, and the adapter is six methods

**Status.** Accepted (version choice) / Pending an install (the adapter).
**Date.** 2026-09-04.

`DK2_REIMPLEMENTATION_PLAN.md` §11 says *"Unity version: latest LTS; decide at
M3, not now — nothing before M3 needs Unity"*. It is M3 and the presentation
boundary is built, so the decision is due.

**The version.** **Unity 6 LTS**, `6000.6.0f1`, which is what
`winget install --source winget --id Unity.Unity.6000` installs. The previous LTS
(2022.3.62f3) is also available and is the fallback if Unity 6 turns out to
misbehave; nothing in this project depends on a Unity 6 feature, because nothing
in this project depends on Unity at all.

**Why the version barely matters, which is the point of ADR-0002.** Every
assembly Unity will load targets `netstandard2.1`, enforced by
`Directory.Build.props` since M1 and by `ArchitectureTests`. Both LTS lines
support that profile, so the choice is about tooling comfort rather than
compatibility, and switching it later is a Hub download rather than a port.

**What is NOT decided here, and cannot be.** The Unity project itself does not
exist. Unity is not installed on the development machine, and **this project does
not commit code it has not compiled** — a rule worth more here than anywhere
else, since a plausible-looking `MonoBehaviour` that does not build is exactly
the kind of thing that sits in a repository for months looking finished.

**What was done instead, and it is most of the work** (ADR-0035, extended):
`ScenePresenter` and `IRenderTarget`. The presenter owns every decision a
renderer could get wrong — what counts as a visible change, what order things
are pushed in, which asset key each thing needs, create versus update versus
remove — and is tested against a real campaign level. `IRenderTarget` is six
methods with no logic in them: make a tile, update a tile, make a creature,
update a creature, forget a creature, flush.

So the remaining Unity work is *one class implementing the target interface*
— six methods when this was written, nine since objects joined the boundary on
2026-09-05 (ADR-0035's amendment) — and the
things it must not do are already impossible: it never sees a `WorldState`, it
never resolves an asset, it never decides an order, and it gets positions in
`Fp32` tile units so the conversion has to happen on its side (ADR-0001).

**Two properties that shrink the risk further, both now tested:**

- **Presenting an unchanged world pushes nothing.** A target that rebuilt 3,721
  tile objects a frame would be unusable; over 60 ticks of a running campaign
  level the presenter pushes well under a tenth of that.
- **Presenting never changes the world.** Asserted per tick over a run, using
  the same state hash the determinism gate uses.

**Consequence for whoever writes the adapter:** read `TextRenderer` first. It is
the same job against a character grid, it runs in CI, and a disagreement between
the two is a bug in one of them rather than a matter of taste.

### ADR-0036 amendment, 2026-09-04 — the adapter exists and compiles

Written when Unity was not installed. It is now installed (`6000.6.0f1`, via
winget) and the adapter is written:

| | |
|---|---|
| `unity/DK2.Unity/` | the Unity project — `ProjectVersion.txt`, `manifest.json`, an asmdef |
| `Assets/DK2/DungeonScene.cs` | `IRenderTarget` in GameObjects. Arithmetic and `Instantiate`, no judgement |
| `Assets/DK2/DungeonRunner.cs` | the `MonoBehaviour`: own a simulation, tick it, present it |
| `unity/DK2.Unity.Compile/` | **compiles the adapter against Unity's real assemblies** |
| `unity/sync-assemblies.ps1` | builds the 7 netstandard2.1 DLLs into `Assets/Plugins/DK2` |

**`DK2.Unity.Compile` is the point worth keeping.** This project's rule is that
it does not commit code it has not built, and Unity code has no other way to
satisfy it. The project is **deliberately outside `DK2.slnx`** — a machine
without Unity cannot build it, and CI has none, so including it would break the
build for everyone who has not installed the editor. It is run by hand:

```
dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj
```

It earned its place immediately, catching three things reading could not have:
the seed is `ulong` and not `int`, the map tile type is `MapTile` and not
`LevelTile`, and referencing `UnityEngine.dll` *alongside* the module assemblies
makes every Unity type ambiguous (Unity's own asmdef compilation references only
the modules, which is why the project now does too).

**What is still not verified, and it is the honest remainder.** Unity is
installed but **not activated** — a headless run exits with code 198 and
`Found 0 entitlement groups and 0 free entitlements`. So the C# is confirmed to
compile against Unity's API surface, and *nothing has been seen to run*. Opening
the project and pressing Play needs an activated editor and a person looking at
the result, and activation needs the owner's account: it is not an assistant's to
do. Until then, `TextRenderer` remains the only renderer this project has
actually watched work.

### ADR-0036 second amendment, 2026-09-04 — Unity runs it

The editor is activated, and both headless checks pass:

| command | proves |
|---|---|
| `Unity.exe -batchmode -quit -nographics -projectPath unity/DK2.Unity` | Unity's own compiler builds `DK2.Unity.dll` against the seven netstandard2.1 plugin assemblies. Stronger than `DK2.Unity.Compile`, which is our compiler against Unity's DLLs |
| `... -executeMethod DK2.Unity.Editor.DungeonSetup.SmokeTest` | **the whole pipeline runs inside Unity** |

The smoke test is the one that matters, and `DungeonSetup` was written for it.
It loads campaign level 1 from a real install, builds the simulation, presents
into a `DungeonScene`, ticks 20 and presents again — failing unless there is one
GameObject per tile. Result: **30×38, 1140 tile objects, 1178 assets all
placeholders, hash `aae0f0e1…` → `8e38409c…`**.

**Everything it exercises was never going to fail at compile time and was always
going to fail on first Play**: whether `GameDatabaseLoader` can read a DK2
install through Unity's Mono runtime, whether netstandard2.1 assemblies load
there at all, and whether the presenter produces objects. That is the shape of
check worth writing for any engine boundary — compiling proves the least
interesting half.

**Zero creatures is correct.** A level's creatures come from the THINGS record,
unparsed until M6; the harness scenarios place their own.

**What remains unverified is appearance.** Nobody has pressed Play. The smoke
test proves the objects exist, not that the camera points at them or that the
scale and colours read as a dungeon. That needs a person, and it is the one
remaining step.

### ADR-0036 third amendment, 2026-09-04 — it renders, and the screenshot found a bug

The owner pressed Play and got campaign level 1 — recognisably the starting
dungeon — on the first attempt. The diagnostics line in that screenshot
cross-checked three claims that had until then only been asserted:

- **`1140 tile objects`** — 30 × 38, matching the headless smoke test exactly.
- **`pushed 0 tiles / 0 creatures this frame`** — the diffing works. A settled
  dungeon costs nothing to keep on screen, which is the property `ScenePresenter`
  exists for and which no amount of unit testing makes as convincing as watching
  the number sit at zero.
- **`2 creatures` at tick 203** — and this is the pleasing one. The map places
  nobody, because creatures live in the unparsed THINGS record. Those two imps
  were produced by `ImpSupplySystem` from the dungeon heart (ADR-0021), at
  roughly the five-second cadence the data specifies. **A mechanic built from the
  owner's own measurements, running in an engine, visible as two boxes.**
- The asset count closes too: 1178 at tick 20, 1182 with two creatures at four
  assets between them.

**And it found a bug nothing else could have.** `LookAt` framed the level using
`world.Width` for the camera height. Level 1 is 30 wide and **38 deep**, so the
near end of the dungeon fell off the bottom of the screen. Now derived by fitting
the level's bounding sphere to the vertical field of view, so the framing cannot
depend on which dimension happens to be larger; `cameraPitch` became a slider.

**The general point, and it is the counterweight to everything else in this
document.** This project has pushed as much as possible behind tests, and that
was right — the smoke test proves 1140 objects exist. *It cannot prove a camera
points at them.* Framing, scale, colour and legibility need a person and a
screenshot. That is not a gap in the test suite; it is the boundary of what a
test suite is, and the honest thing is to know which side of it a claim sits on.

---

## ADR-0037 — The 1999 manual is a corroborating source, never an overriding one

**Date:** 2026-09-04
**Status:** Accepted
**Context:** the owner supplied the shipped game manual
(`original-reference/Dungeon_Keeper_2_Manual.pdf`, gitignored). Research write-up:
`docs/research/manual-1999.md`.

### The decision

The manual is admitted as a research source with a **fixed rank**: below the
shipped v1.70 data and below the owner's observation of the running game, above
every community source. Concretely:

- Where the manual and the shipped data agree, the claim is **corroborated** and
  its confidence rises.
- Where they disagree, **the data is implemented** and the disagreement is
  recorded as a question rather than resolved by preference.
- Where the data is silent, the manual may supply *what a mechanic is for* — but
  never a number the data could have carried and does not.

### Why it needs a rule at all

Because it is unusually tempting. It is EA's own document about EA's own game,
written by people who could ask the programmers, and it is far more readable than
a hex dump. Nothing else in this project's sources has that combination.

But it shipped with **1.0** and this project targets **v1.70**, and the gap is
real rather than theoretical: it disagrees with the shipped `Creatures.kwd` five
times, all in creature attraction, and in one of those it describes a **third**
attraction slot the record has no bytes for (the eight bytes after the second slot
decode as two fixed-point values in 0.1–1.0). A source that describes a record
layout the shipped file does not have is describing a different build.

The precedent this follows is ADR-0023 and the Q53 lesson: a plausible external
number is a *hypothesis to check against the install*, never an anchor. The manual
is the most plausible external source yet encountered, which is exactly why it
gets the rule written down rather than assumed.

### Establishing its reliability first

The manual was tested before it was used, on ground where we already had an
answer. Its room pages print a **Cost** and a **Minimum Recommended Size** for
each of the seventeen rooms; both are fields parsed out of `Rooms.kwd` by offsets
found without reference to it.

**17 of 17 costs matched. 16 of 17 sizes matched.** The exception is the Workshop,
where the manual's room page says 5x5 and the record says 3x3 — and the manual's
*own* Bile Demon page says 3x3, so it contradicts itself and the record is right.

That is a strong pass, and it bought two things at once: an independent
cross-validation of our `Rooms.kwd` offsets, and a third source for Q45 — the
manual states the semantics of `RecommendedSizeX/Y` outright (*"At this size the
room is most likely to function effectively"*, and *"you can create smaller
rooms"*), which is `RoomInstance.IsFunctional` and Q36 together.

### What it delivered

Corroborated, independently: the gold overflow chain treasury → heart → floor
(Q31, ADR-0018/0019), the 50% sell rate (Q34), `TrainingRoomMaxExperienceLevel` =
4, `CombatPitMaxExperienceLevel` = 8, `RoomDef.CreatedCreatureId` (Graveyard →
Vampire, Prison → Skeleton), `MinimumImpThreshold` = 4 (ADR-0021), room tiles
generating the same mana as plain claimed tiles, wooden bridges burning over lava
(Q67), payday every 600 seconds (Q9), and the meaning of `Fear` and `Threat`.

Settled, where nothing else had it: research is stored **in the library** and lost
with it; `ModifyPlayerColdWhileTrainingPerSecond` is **Gold**, not "Cold"; the
casino's payout lever is the `Smiles`/`Money` variable pairs; the creature cap is
**per portal**; a gold treat is a **prepayment** of wages; a lair holds **beds**,
one per creature, not tiles.

Opened: Q74 through Q81, including two that push on existing decisions —
whether there is a player-level starting-gold stockpile after all (against
ADR-0015), and whether the mana ceiling is flat or per-heart.

### Consequences

- Room research for M8 now starts from prose plus a rate rather than from a
  blank sheet. That is the single biggest effect.
- `manual-1999.md` carries the page-referenced extract; nothing else should quote
  the PDF directly.
- The manual is **not** an oracle for timing. It says "each turn" throughout and
  never defines a turn, so it says nothing about Q1.
- The PDF stays out of the repository. It is EA's copyright and this repository
  is public, and it lives under `original-reference/` with the rest of the
  original material. (Written when the project was MIT; the licence changed to
  GPLv3 the same day, ADR-0041, and it makes no difference here — our licence
  grants nothing over EA's work either way.)

---

## ADR-0038 — A creature never enters a tile it could not have walked into

**Date:** 2026-09-04
**Status:** Accepted
**Supersedes nothing.** Prompted by the owner, unprompted, 2026-09-04:
*"creatures CAN NOT pass through walls, they can only move through dug out tiles,
not through solid rocks"*, after watching imps walk through rock in an OpenKeeper
gameplay video.

### The decision

`CreatureMovementSystem` re-checks each waypoint against `WorldPathGrid` **at the
moment it steps onto it**, and a creature that finds one blocked stops and drops
the route. The tile it is *standing on* is deliberately not re-checked.

### Why a second check, when the router already refuses walls

`WorldPathGrid.IsWalkable` has refused `TerrainFlags.Solid` since the first worker,
so no route has ever been *planned* through rock. But **a route is planned once and
the world keeps moving**, and there is a shipped mechanic that makes it move in the
wrong direction: a wooden bridge over lava burns out thirty seconds after it is
built and the tile reverts to lava (Q67, ADR-0032). A creature already walking a
route across that bridge would have strolled over the lava.

`BridgeBurnSystem`'s own notes had anticipated half of this — a creature *caught*
on ground that turned impassable must be able to leave, which is why the
pathfinder does not test a mover's starting tile. Nothing had stopped one
*entering* such ground.

### Dropping the route rather than re-planning

The mover clears the path and stops. It does not re-route, because it is not the
mover's job to decide what a creature wants; `ImpTaskSystem` already handles an
empty path in both states, and handles it differently and correctly in each:

- a worker walking to a job **abandons the job and looks for another** — the same
  "the world changed under the route" branch, with the same reasoning that an imp
  should not chase a moving answer;
- a carrier delivering gold **re-routes to somewhere else that will take it**,
  keeping what is in its hands.

So the change is four lines in the mover and no change anywhere else.

### The invariant, and the thing that was missing

`PathfindingTests` proved the *search* will not cross a wall on a hand-drawn grid.
Nothing proved that the *simulation*, running the real systems on real terrain
over many ticks, never puts a creature inside one. `WallsBlockMovementTests` now
asserts, across a 4000-tick dig with four imps: no creature ever stands on a solid
tile, no route in flight ever crosses one, and a staged case where lava appears
ahead of a walk stops it.

**Two existing tests had to be fixed to make this pass**, and they are the reason
the invariant was worth writing. `WalkingOnTheBias_TakesRootTwoTimesAsLong` and
`SpeedDoesNotDriftFromRoundingItAwayEveryTick` hand-fed routes that ran straight
across the fixture's rock. They were measuring movement arithmetic and the
arithmetic was right, so they passed — while walking imps through walls. Nobody
noticed for four months because nothing looked.

### Confidence

The rule is CONFIRMED (owner). What the original does *at the instant* the ground
fails — stop dead, or re-route — is UNKNOWN and deliberately not invented. Only
the observable rule is reproduced.

---

## ADR-0039 — A dug-out tile is never closed again, and two terrains are never opened

**Date:** 2026-09-04
**Status:** Accepted
**Context:** two more rules the owner made explicit, 2026-09-04.

### The decisions

1. **Nothing in a running simulation ever turns an open tile solid.** Asserted,
   not merely true.
2. **Impenetrable Rock and Gems can never be dug through**, by the two different
   mechanisms the shipped data already gives them. Asserted, and it was already
   correct.

### The second one first, because it is easy

> *"there are 2 categories (that I know of) of indestructable tiles, that can't be
> dug through by imps, solid rock and a gem tile. gem tile is an infinite gold
> source and solid rock is the boundary to limit expansion on the map."*

The terrain table says exactly this. **Impenetrable Rock** is `Solid, Impenetrable`
and **not taggable**, so a keeper cannot mark it and no rule ever runs.
**Gems** are `Solid, Impenetrable, Taggable` with a gold value of 3000 and a
`DestroyedTypeTerrainId` of *themselves*, so mining applies forever and the tile
never goes. `TileWork`'s dig rule requires `IsSolid && IsTaggable &&
!IsImpenetrable`, which excludes both without a special case; mining requires
`IsSolid && IsTaggable && GoldValue > 0`, which includes gems. Nothing changed —
tests were added, on the fixture and on the shipped table.

### The first one is more interesting, because the data argues back

> *"once a tile is dug out, and maybe claimed, there is no possible way to fill in
> that tile to rock again. DK2 doesn't have a game mechanic for filling up dug out
> tiles."*

Three shipped fields describe filling in: `FillInHealth` = 5 in the balance table,
`TerrainFlags.FillInable` on exactly the five floor terrains, and Claimed Path's
`MaxHealthTypeTerrainId` pointing at Rock. Q18 and Q22 read those together and
produced `TileWorkKind.FillIn`. The owner has played the game and says no keeper
can do it.

**Both are accepted as true.** The engine carries the concept; the shipped game
exposes no way to reach it. The evidence for that reading is that three
*coordinated* pieces of data are not an accident, while the manual — which
documents every keeper affordance down to the casino payout lever — never mentions
filling anything in.

#### The investigation the owner asked for, same day

Two of the three candidate explanations were then closed.

**OpenKeeper is in exactly our position.** The most complete DK2 reimplementation
in existence carries `FILL_INABLE(0x00008000)` (`Terrain.java:55`) and
`FILL_IN_HEALTH(11), // value=5` (`Variable.java:333`) in its file-format layer,
**both declared once and referenced nowhere else**, and its worker task list has
no fill-in task. Corroboration rather than proof, but it rules out the possibility
that somebody else had already solved this. *(GPLv3: cited facts about its source,
per ADR-0003. No code copied.)*

**It did point at the real mechanism, though, and the install settled it.**
Their trigger-action enum has **`ALTER_TERRAIN_TYPE(22)`**, implemented as a
direct terrain write with no health and no rate. That is a way a tile could become
rock again, so it was checked against the shipped maps rather than taken on trust:
new command `dk2tools kwd alterterrain` decodes every such action in all 66
trigger files (5408 action records, 160 of type 22) and reports what each target
tile *starts* as. The layout hypothesis self-validates — all 5408 decoded action
types fall inside the enumerated 1..60 range.

The finding is the opposite of what was being looked for. **Ninety-four of the 160
writes are `Impenetrable Rock → Dirt Path`** — scripts *opening* passages sealed
against digging. **Level 1's three Rock writes are also opening**: (15,18),
(16,18) and (17,18) all start as Impenetrable Rock and are softened into diggable
Rock, which is the tutorial handing the player a route. **Exactly two writes in
the whole campaign turn an open tile solid** — `Secret3` at (13,20) and (5,25),
both Claimed Path → **Reinforced Wall**, never Rock.

So: a level script can wall a tile up, as a set-piece, twice in sixty-six maps.
**Nothing anywhere uses the fill-in *health* mechanic** — not the keeper, not the
level script, not OpenKeeper. Q74 keeps only that narrow half open, and the level
editor is the remaining place to look.

**One consequence for M6, recorded now so it is not discovered later:**
`ALTER_TERRAIN_TYPE` must be able to write solid terrain onto an open tile. The
invariant below is a statement about **workers**, not about the engine, and level
1's three writes are a ready-made first test for the action on a level already
rendering.

### What was done, and what was deliberately not done

**Not done: deleting anything.** `TileWorkKind.FillIn` and the terrain transition
stay. They are in the shipped file, and removing them would put our terrain graph
out of step with the data it was read from — which is a worse failure than
carrying a rule nothing invokes. This project's discipline is that the data model
mirrors the data; behaviour is where fidelity is judged.

**Done: making the behavioural claim executable.** `ImpTaskSystem` only ever
chooses excavation, claiming and reinforcing, so no imp filled anything in — but
that was an absence, and absences rot. `DugOutTilesStayDugOutTests` now pins both
halves over a 4000-tick dig: **no tile ever goes from open back to solid**, and
**no work order that reaches the queue is a fill-in**. Both are checked
non-vacuously (the run is asserted to have queued digging and claiming). Adding a
fill-in job priority later would fail loudly.

### Consequences

- The walkable set only ever grows, with the single exception of a wooden bridge
  burning. Anything that caches reachability may rely on that.
- Q22 — "how long does filling a tile in take?" — is withdrawn from the
  observation checklist: the experiment does not exist. The prediction it carried
  (660 swings, ~132 seconds) is a fact about the terrain table, not about play.
- The one thing still worth a look, if the owner ever opens the level editor: is
  there a fill-in tool, or a fill-in action among the 51 trigger types?

---

## ADR-0040 — A level plays on its own VARIABLES, layered over the global table

**Date:** 2026-09-04
**Status:** Accepted
**Context:** found while working out what "what rooms do" would need.
Research: `docs/research/kwd-variables.md` §*Update 2026-09-04*. Questions: Q82,
and it corrects Q1.

### The defect

`GameDatabaseLoader.Load` read `GlobalVariables.kwd` and nothing else. Every
shipped level also ships `<name>Variables.kld`, in the identical record format,
and **26 distinct variable ids are overridden by at least one level**.

So every campaign run used the wrong numbers wherever a level disagreed — and one
disagreement was already live: **`MinimumImpThreshold` is 0 on four of the five
secret levels and on the front end**, against a global 4. A keeper there gets no
free imps at all, which is exactly right for a fixed-roster puzzle level, and
`ImpSupplyRules` (ADR-0021) would have handed out four. Level 1 doubles both
payday variables, 600 to 1200.

**This project's own notes had the fact and no code read it.**
`kwd-variables.md` has said *"Per-level `*Variables.kld` files override it (same
record format)"* since 2026-08-29. That is the third instance in a week of a
claim written down and enforced nowhere — the netstandard target, the walls rule,
and now this — and it is recorded in `GOAL_ALIGNMENT.md` as a standing review
question rather than as three separate bugs.

### The decision

`GlobalVariables.MergedWith(level)` produces a new table; `GameDatabase.ForLevel`
produces a new database around it; `GameDatabaseLoader.LoadForCampaignLevel` does
both in one call and is the shape to prefer whenever a level is named.

Two merge rules, because the file holds two kinds of thing.

**1. Plain values replace by id, and ids only the level has are added.** That is
what an override file is for, and it is the only reading under which the secret
levels' 0 means anything. Levels also introduce ids the global table lacks
entirely — `GameTicks`, `LevelRating`, `AverageTime`, and two unnamed ones — so
"replace" has to include "add".

**2. The four lists replace wholesale when the level provides any, and are kept
when it does not.** STRONGLY INFERRED, and the shipped data argues for it rather
than merely permitting it:

- The **global availability list is a template keyed to player 0** — every room
  AVAILABLE, every trap, door and spell RESEARCHABLE. A level's list names *its
  own* player ids and its own states. A union would leave a level-1 keeper holding
  "every room available" from the template beside its own "treasury unavailable",
  which is not a state the game can be in.
- Conversely **no level ships sacrifice recipes or creature-stat rows**, and the
  temple's 44 recipes and the 320 stat rows are plainly game-wide. So silence has
  to mean "keep".

The one case this cannot distinguish is a level that meant to *add* a single
availability entry. No shipped level looks like that — every level carrying any
availability carries a full set across the same five kinds — so the question does
not arise on this data (Q82).

### Immutability, deliberately

A merge returns a **new** `GlobalVariables` and `ForLevel` a **new**
`GameDatabase`, sharing the parsed definitions. A running simulation holds a
database, and one level's numbers must not be able to reach another level's run;
`TheMergeNeverMutatesEitherSide` pins that in both directions. `ForLevel(null)`
returns `this`, so a caller can hand it whatever the loader found without
checking.

### The harness

`ScenarioRunner.Run` resolves the level's variables first and delegates to a
runner built around the level's database, rather than threading a second database
through every helper. Every rule below then reads the one `_database` that is
correct for that run.

The run's balance table is reported as **provenance, not as a shortfall**: it is a
`ScenarioResult.BalanceTable` property and a line in the report header, not an
entry in `Unsupported`. A reader of a campaign report should not have to guess
which numbers produced it. Level 1's line reads
`GlobalVariables.kwd + level1Variables.kld (202 values, 31 availability entries,
1 creature-pool entry)`.

**No hash moved.** All ten scenarios produce the same state hashes as before,
which is the expected result and worth stating: level 1 overrides nothing that any
currently-implemented system reads. The fix is for the levels and mechanics that
come next, and it lands before them rather than after.

### What it unblocks, and why it came before the rooms work

The per-level file carries the two lists the global table leaves empty, and they
are precisely what a **portal** needs: `CreaturePool` for who may come (level 1:
fifteen Goblins), `Availability` kind 2 for whether they are unlocked yet,
`EntranceGenerationSpeedSeconds` for how often, against
`CreaturesSupportedByFirstPortal` = 15 for the cap.

Decoding `Availability` was a by-product and a large one. Its `kind` and `value`
enums were derived **from the shipped data before any external source was
consulted** — kind by how many type ids appear under each (eleven traps, six
doors, thirteen spells), value by how the states differ between the global
template and levels 1, 5, 13 and 20 — and OpenKeeper names both fields exactly the
same way. Details in `kwd-variables.md`. It explains the shape of a DK2 campaign:
level 1 starts its keeper with almost everything UNAVAILABLE and one creature
available, level 20 with nearly everything AVAILABLE, and `Secret1` with no
availability entries and no pool at all.

It also names a gap nobody had written down: **which rooms a keeper may build is
per level, and `RoomRules` does not model it** — a keeper here can build any
buildable room on any level.

### Corrects Q1

The id-74 `GameTicks` variable was recorded on 2026-08-29 as carried by "no
shipped file — not GlobalVariables.kwd, not any of the 71 map KWDs". It is
carried by **all 66 per-level files, at 340 in every one**. The sweep looked at
the `*.kwd` map files; the value is in the `*Variables.kld` siblings, which
nothing was loading.

**What 340 means is UNKNOWN and is not being guessed** (Q83). 340 a second is
implausible; the id shares its "no trailing fields" shape with the two
front-end metadata ids; and OpenKeeper's comment on the same id says `value=4`,
matching nothing here, so even the name is suspect. What has changed is that Q1
has a datum that is invariant across the entire campaign instead of a blank.

## ADR-0041 — The project is GPLv3, and the notice is enforced by a test

**Date:** 2026-09-04
**Status:** Accepted
**Context:** the repository was MIT (ADR none — it was never decided, it was the
default). Changed to GNU GPL version 3 at the owner's request, to match
OpenKeeper.

### The decision

`LICENSE` is the GNU General Public License version 3, verbatim, 674 lines.

`license.txt` is the per-file notice as a FreeMarker header template — the same
form and the same purpose as OpenKeeper's `license.txt`, which is where this
file came from and why a lower-case `license.txt` sits next to an upper-case
`LICENSE`. IDEs that stamp a notice into new files read it.

**The notice is three lines, not fourteen.**

```
// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
```

Under the GPL the per-file notice is what makes `LICENSE` apply to a file
rather than merely sit beside it in the repository, so this is not a purely
cosmetic choice. It was first written as the GPL's own *How to Apply These
Terms to Your New Programs* boilerplate — fourteen lines, copied verbatim out
of `LICENSE` — and shortened the same day, on the owner's call, to the SPDX
form. Fourteen lines across 134 files is 1,876 lines of prose that no reader
ever reads twice, and it opened every diff.

**What matters is that the short form does not narrow the grant.** The prose
said "either version 3 of the License, or (at your option) any later version";
`GPL-3.0-or-later` says exactly that, in a token that tooling can also read
(the SPDX/REUSE convention). But the whole grant now rests on one token with
no prose to fall back on, so two tests hold it in place:
`TheNotice_DeclaresGpl3OrLater` pins the identifier — `GPL-3.0-only` or a bare
`GPL-3.0` is a one-word edit and a narrower licence — and
`TheLicenceFile_IsTheVersionTheNoticeClaims` parses the version out of the
identifier and asserts `LICENSE` really is that version. Under a long notice
each file quoted the licence; under a short one each file only names it, and
naming can go stale silently.

**This was a free choice, not an obligation.** `THIRD-PARTY-NOTICES.md` and
ADR-0003 record that OpenKeeper was consulted as documentation only, with no
code copied or transliterated, so nothing in this repository was under a
copyleft obligation. The MIT-licensed material that *is* here — the GLKeeper WAD
decompression adaptation in `WadDecompressor.cs`, and YamlDotNet — is
GPL-compatible and keeps its own attribution.

### The defect this uncovered

The natural way to enforce a file header in .NET is Roslyn's IDE0073 with
`file_header_template` in `.editorconfig`, and with
`EnforceCodeStyleInBuild` + `TreatWarningsAsErrors` already set in
`Directory.Build.props` it becomes a build error for free. That was built, and
it works — for MIT.

**It could not express the GPL's prose notice. An `.editorconfig` value ends at
the first `;`, and that notice has one here:**

```
but WITHOUT ANY WARRANTY; without even the implied warranty of
```

The template silently truncated at the semicolon. Roslyn then enforced, and
`dotnet format` wrote into seven files, a notice whose warranty disclaimer had
been cut in half — and the build went green, because the files matched the
truncated template exactly. There is no escape for `;` in the format.

**The MIT text contains no semicolon.** That is the only reason IDE0073 looked
like the answer, and it is why this was found by changing licence rather than by
review: the mechanism was never correct, only untested against text that
happened to exercise it.

### The enforcement

`tests/DK2.Core.Tests/LicenseHeaderTests.cs` renders `license.txt` and asserts
that all 134 source files start with it. Three reasons a test rather than an
analyser:

1. **It covers `unity/DK2.Unity/Assets`.** No project in `DK2.slnx` compiles
   those three files — only `unity/DK2.Unity.Compile` does, and that needs an
   installed editor (ADR-0036) — so a build-time analyser is structurally blind
   to exactly the files most likely to be forgotten.
2. It has no `;` problem, and no dependence on a template format's parsing.
   **Note that the three-line notice contains no semicolon, so IDE0073 would
   work today.** It is still not used: "correct as long as nobody lengthens the
   text" is the same accident as before, and it fails silently both times.
3. CI already runs `dotnet test DK2.slnx` on Linux and Windows, so it is gated
   with no new workflow step.

The sweep asserts its own file count is above 100 before checking anything. A
search that finds nothing passes every content assertion, which is how a check
becomes a decoration; this project has enough of those in its history
(ADR-0036, ADR-0039, ADR-0040) to write the guard in up front.

`.editorconfig` keeps the whole account of the IDE0073 failure, next to the
`end_of_line = lf` that stops `dotnet format` rewriting whole files to CRLF on
Windows — which it did, twice, while this was being built.

### Open, not decided

**GPLv3 and Unity.** OpenKeeper builds on jMonkeyEngine, which is BSD;
this project builds on Unity, which is proprietary. Distributing GPLv3 game code
that links a proprietary engine runtime is a known friction point, and it is not
resolved here because nothing is distributed yet. It needs an answer before the
first public build, not before the next commit.

---

## ADR-0042 — The portal produces creatures, and three of its rules come from OpenKeeper

**Date:** 2026-09-04 · **Status:** Accepted · **Supersedes:** nothing ·
**Implements:** `PortalRules`, `PortalSystem`, `TileState.EntranceIdleTicks`

**Context.** The portal is the first room whose behaviour is implemented, and it
was chosen first for a structural reason: **nothing else could have occupants.**
THINGS is unparsed so no level's creature roster loads, and `ImpTaskSystem` skips
anything without `IsWorker`, so a non-worker dropped into the world stood still
forever. Every other room's behaviour needs creatures in it; the portal's
behaviour *is* producing them, and it needs no creature health, hunger, mood or
experience — none of which exists.

**Decision.** A portal produces a creature when four gates all pass: the creature
is unlocked for that keeper (`Availability` kind 2), the pool has room for
another of its type (`CreaturePool`), something in the dungeon attracts it
(`CreatureAttraction`), and the keeper is under their portals' capacity
(`CreaturesSupportedByFirstPortal` + 5 each after). Every number is CONFIRMED
from the shipped v1.70 data and the 1999 manual states the capacity rule
independently.

**How they compose is STRONGLY INFERRED, and three of those inferences are
OpenKeeper's rather than the data's.** They are itemised in
docs/research/portals.md and listed in THIRD-PARTY-NOTICES.md, because that is a
GPLv3 attribution obligation rather than a courtesy:

1. **The interval grows with the dungeon** — `max(base, base × creatures ÷ 2)`.
   Nothing in the balance table expresses this and it is the highest-risk
   inference in the mechanic: it is the difference between fifteen goblins in
   four and a half minutes and fifteen in fifteen. **Unfalsifiable from the
   files; only a stopwatch on the original settles it (Q86).**
2. **The pool is a standing per-type limit, not a draining quota**, so sacking a
   creature lets another in. Corroborated by the manual's "replaced by a new
   creature", and indistinguishable on level 1 where the pool and the capacity
   are both 15 (Q87).
3. **A two-slot creature needs both rooms, and the size means one room's tile
   count** rather than the keeper's total.

**One quirk is reproduced rather than fixed** (fidelity priority 4): the keeper's
creature count is read once per tick, so two portals ripe on the same tick both
produce and a multi-portal keeper can end a tick over their stated maximum.
OpenKeeper documents this as original behaviour and replicates it deliberately.

**The spawn clock lives on the portal's centre tile**, counts *up*, and runs only
while a keeper owns the portal. It cannot live on `PlayerState` because the clock
is per portal; it cannot live on `RoomInstance` because a room instance is
derived fresh from the tiles and remembers nothing (ADR-0027). Counting up rather
than down is what lets the requirement change mid-wait — a creature dying
shortens the wait, and a portal blocked for want of a lair banks its wait and
produces the moment one appears. `HashVersion` 9 → 10 and snapshot
`FormatVersion` 9 → 10 in the same commit.

**Consequences.**
- Uniform random selection among eligible creatures is a placeholder. The data
  carries a per-type percentage that shifts with sacking
  (`DecreaseEntrancePersentageOfLastSackedCreatureType` = 33) and neither
  sacking nor the weighting exists (Q90).
- **This is the first consumer of `SimulationContext.Rng`**, which has been
  carried, hashed and snapshotted since M1 with nothing drawing from it. The
  draw happens even when only one creature qualifies, so the RNG position cannot
  become a function of the keeper's rooms.
- Level 1 produces nothing until its keeper builds a lair, because the Goblin's
  only attraction is a Lair. The tutorial's order is a mechanic, not a script.
- A portal still cannot be **claimed** by an imp: room conversion is a real job
  with its own balance value (`ConvertRoomHealth` = −4000) and has no
  `TileWorkKind`. A scenario says "claimed portal" by re-owning the tiles, and
  the harness reports that as a shortfall (Q92).

---

## ADR-0043 — Not every player is a keeper: DK2's player ids are reserved

**Date:** 2026-09-04 · **Status:** Accepted · **Implements:** `PlayerIds`

**Context.** Found while asking whose portal an unclaimed portal is. Level 1's
portal ships owned by **player 2**, not player 0 — and the assumption that every
id in `WorldState.PlayerIds` is a keeper had been made silently everywhere.

**Decision.** Four reserved ids: **0** unowned terrain, **1** the hero side,
**2 neutral — which is what an unclaimed room belongs to**, and **3 to 7**
keepers. `PlayerIds.IsKeeper(id)` is `id >= 3`.

**CONFIRMED from three independent directions**, only one of which is
OpenKeeper's `Player` constants. The campaign's own data says it twice: across
all 66 levels every dungeon-heart tile belongs to a player between 3 and 7, and
every creature pool and creature-availability entry names a player between 3 and
7. Level 1 shows all four at once. Asserted in
`PortalOnRealDataTests.TheReservedPlayerIdsAreWhatTheCampaignSays`.

**The obvious argument for it is wrong, and that is the part worth keeping.**
"Unclaimed things belong to player 0" fails twice over: solid rock carries a
non-zero owner all over the campaign, because DK2 colours the walls bordering a
keeper's territory in that keeper's colour (`TerrainFlags.PlayerColouredWall`),
so ownership of rock is a rendering fact rather than a claim about who holds the
tile — and an unclaimed room is player 2. The argument that does hold is the
narrower one about hearts and pools.

**Consequences, including one not acted on.** `PortalSystem` runs only a keeper's
portals, so an unclaimed portal's clock stays at zero and claiming one does not
hand over a banked wait. **`ImpSupplySystem` still loops every player** and would
give free imps to 0, 1 and 2; it changes nothing on shipped data because none of
them has a dungeon heart, but the rule is unstated where it should be explicit.
The test suite also uses player 1 as "a keeper" and 2 as "a rival" throughout.
Both are **Q91**, with the fix named — gate on `PlayerIds.IsKeeper`, re-key the
fixtures — and left as their own change rather than bundled into the portal's,
because they touch five hundred tests written on the old convention.

---

## ADR-0044 — A room is claimed whole, never tile by tile

**Date:** 2026-09-04 · **Status:** Accepted · **Implements:**
`TileWorkKind.ConvertRoom`, `TileWorkRules.CompletesAsTakeover`,
`TileWorkSystem.SpreadTakeover`

**Context.** **CONFIRMED by the owner**, unprompted, the day the portal landed:
*"once an imp claims a portal then the entire portal gets claimed, all tiles
that are assigned to that portal are immediately claimed … that is a game
mechanic that works on claiming enemy/neutral rooms too, rooms are instantly
claimed by imps, not tile by tile."*

**It arrived as an answer to Q92 — "how does a keeper claim a portal?" — and it
turned out to be a defect report about something much older.** The project did
not lack room claiming; it had it and it was wrong.
`TileWorkKind.Claim` applied to room terrain, because room terrain is `Ownable`
and not `Solid` and that is all the claim rule asked for. So a rival's treasury
came over one tile at a time, at `ClaimTileHealth`'s rate, and a half-taken
portal was a reachable state. Nothing checked it, and nothing checked it because
nobody had asked: it took an owner sentence about portals to notice the general
case.

**Decision.**
1. **`Claim` excludes room terrain** (`!terrain.IsRoom`).
2. **`ConvertRoom` takes rooms**, reading `ConvertRoomHealth` — **−4000**, which
   until now was an unused entry in `VariableTypeId`. At five applications a
   second against a room tile's 10000 health that is **thirteen swings, about
   2.6 seconds**: the propagation is instant, the work is not.
3. **Completion is room-scoped.** `TileWorkRules.Apply` converts the worked
   tile; `TileWorkSystem` reads the `RoomInstance` *before* the work lands and
   hands the rest of it over. It has to be before — a room instance is tiles of
   one terrain with **one owner** (ADR-0027), so the moment the worked tile
   changes hands the rest of the room is unreachable from it.
4. **Only the owner is written on the other tiles.** They were never worked, so
   resetting their health would repair a room by taking it. A rival who has been
   chipping at your treasury gets the damage with it — the same reasoning that
   keeps a claimed burning bridge burning (Q68).
5. **Gold changes hands with the room.** Nothing spills: a tile's capacity is a
   property of its terrain and the terrain did not change. Exactly what Q40
   observed on defeat — rivals claim the rooms *and gold*.

**The mixed direction is the data's, not a choice.** `ClaimTileHealth` is
**+825** and builds a floor tile *up* to its maximum; `ConvertRoomHealth` is
**−4000** and beats a room *down* to zero. Two jobs that both end in "this is
mine now", from opposite directions, which is why `CompletesAsTakeover` exists:
without it the downward path would send a converted room to its
`DestroyedTypeTerrainId` — Claimed Path for every room in the data — and
claiming a treasury would demolish it. Demolishing is `AttackRoomHealth`'s job
(−1500) and has no work kind yet.

**Two things that fell out rather than being written.** A worker takes a room by
standing **in** it, because the stance rule asks what the tile will be when the
job finishes and a takeover leaves it alone (ADR-0028). And an imp holding gold
with a rival's treasury next door now **takes it and then uses it** — which
prompted the owner to confirm the rule it might have broken: *"imps fill only
their own keeper's treasury … for SP my imps NEVER fill anyone's treasure but my
own."* That holds, structurally: `GoldStore`'s store lookup skips every room
whose owner is not the depositor, and it is the only path into a tile's gold.
Asserted as an ordering — the takeover precedes the first delivery — and as an
invariant over every tick.

**Consequences.**
- Two rules are ours rather than the owner's, both flagged: **a dungeon heart
  cannot be taken this way** (a heart is beaten down, and Q40's "rivals claim the
  rooms" happens *after* it falls — Q93), and **a multi-tile bridge converts
  whole** (a bridge is a room in every way the data can tell, but the owner said
  "rooms" and a ten-tile bridge changing hands from one tile is a large
  consequence to draw from that — Q94).
- Q92's *rate* is still unobserved. Only the propagation is confirmed.

---

## ADR-0045 — A tile that cannot be owned does not carry an owner

**Date:** 2026-09-04 · **Status:** Accepted · **Supersedes:** the
never-clear-ownership rule stated in `TileWorkRules.Convert` and
`RoomRules.Unmake`

**Context.** Found by a scenario, not by review. A run that dug from level 1's
dungeon heart toward its portal did **nothing at all for 130 game-seconds**: it
dug the three tiles it was told to and then sat there.

**The cause is four months old and it walled a level-1 keeper in permanently.**
The owner byte survived every terrain conversion, on a justification recorded in
two places: *"which owner id means 'nobody' is UNKNOWN (Q17), and every rule that
cares asks `TerrainDef.IsOwnable` first, so a stale byte on unownable terrain is
inert."*

**Both halves were false by the time it mattered.**

| the claim | why it failed |
|---|---|
| "which id means nobody is UNKNOWN (Q17)" | **Answered the same day, in ADR-0043**: 0 is unowned, confirmed three ways across all 66 shipped levels. The reason for not writing a neutral id had expired, and nothing connected the two facts. |
| "every rule that cares asks `IsOwnable` first, so a stale byte is inert" | **`TileWorkKind.Claim` does not.** It tests `OwnerPlayerId != playerId` and nothing else about ownership. |

So a keeper who dug through **their own reinforced wall** got Dirt Path still
stamped with their id — neither claimable by them (the owner already matched) nor
their own land (`StandsOnOwnLand` requires `IsOwnable`). Their territory could
not grow through it, ever. On level 1 that is fatal rather than untidy: the only
route out of that dungeon heart is through the keeper's own wall, so nothing
beyond it was reachable — not the portal, not a lair, not a treasury.

**Decision.** `Convert` and `RoomRules.Unmake` clear the owner to
`PlayerIds.Unowned` when the terrain they leave cannot be owned. The original
reason for keeping it still holds where it applied: a treasury's destroyed type
is Claimed Path, which *is* ownable, so **selling your own room still keeps the
floor it stood on**. What changes is the cases nobody had in view — a dug wall, a
destroyed claimed path, and a sold bridge over water, which no longer leaves you
owning the water.

**Consequences.**
- Eight tests in `TileOwnershipTests` and one on the shipped level-1 map, and the
  point of them is that **every existing claiming test opened *unowned* rock**,
  which never carried the stale byte. The gap was specifically a wall the keeper
  already owned, and nothing dug one and then tried to grow past it.
- One scenario hash moved (`tiles-mine-gold-001`): a mined seam's tile no longer
  carries the owner the rock happened to have.
- **The lesson generalises and is new.** The project's standing review question
  has been *what would fail if this stopped being true?* and *does anything read
  this fact at all?* This adds a third: **is this justification still true?** A
  reason resting on an open question rots silently the day that question is
  answered somewhere else, and neither the answer nor the reason knows about the
  other. `GOAL_ALIGNMENT.md` carries it.

---

## ADR-0046 — DKII.EXE may be decompiled; its output may not enter the repository

**Date:** 2026-09-04
**Status:** Accepted
**Answers:** Q96
**Supersedes nothing. Extends:** ADR-0003 (what may be used as a source)

### The decision the owner took

*"about ghidra, you are free to use it and decompile it, put all decompiled
resources under reverse-engineering folder."*

So: **decompile freely, quarantine the output.** `reverse-engineering/` is
gitignored except for its README. This ADR is the rule for what may leave it.

### Why a rule was needed at all rather than just permission

Q96 was put to the owner rather than decided here, because it is the one
research decision that changes what the project can *say about its own method*.
The brief is explicit twice over: prefer *"clean-room behavioural
reimplementation based on observed behaviour and documented research rather than
copying proprietary implementation wholesale"*, and *"do not copy original
source code into the new project merely because it is available through reverse
engineering."*

Decompiler output is not a description of the binary. **It is source code
derived from it** — a C rendering of EA's own statements, function by function,
with EA's control flow and EA's operation order. The second sentence of the
brief is about exactly this, and it does not stop applying because the route is
a decompiler rather than a leak.

But the *first* sentence says "documented research", and a number is not an
expression. `EntranceGenerationSpeedSeconds` is 25 whether we read it from a
KWD, watch it with a stopwatch, or find the comparison in `.text`. Facts about
behaviour are the whole point of the project; they are not ownable and never
were.

So the boundary is not decompile / do-not-decompile. It is **fact / expression.**

### The rule

1. **Facts, formulas, thresholds, comparisons, orders of operation and
   descriptions of control flow may cross** into `docs/research/`, written in
   our own words, with provenance naming the binary and the address.
2. **Decompiler output itself never crosses.** Not into `src/`, not into
   `docs/`, not into a commit message, not into a test fixture, not renamed or
   reformatted or tidied. It stays in `reverse-engineering/`, which is
   gitignored, and a `git status` that shows a file in there is a bug.
3. **Binary-derived facts rank below owner observation** of the game running.
   The decompiler is an inference engine — it invents names, guesses types, and
   quietly mismodels optimised fixed-point and float arithmetic. Where the two
   disagree the running game wins, and the disagreement gets written down rather
   than reconciled silently.
4. **Every binary-derived fact is tagged as such** in its research doc, so that
   a reader can always tell which of the four evidence classes a claim rests on:
   shipped data, owner observation, community research, and now the binary.
5. **`[Dk2Behavior]` confidence may rise on binary evidence, but not to
   CONFIRMED.** CONFIRMED is reserved for the running game. A fact read cleanly
   out of the binary and consistent with the data is STRONGLY INFERRED — which
   is an honest promotion from HYPOTHESIS, and most of what decompiling is for.

### Amendment, 2026-09-04 — the binary is the *primary* source

The owner, after watching a question get answered from the shipped data and a
community enum: *"I want you to make using ghidra and decompiled sources as the
primary source and go to when in doubt."*

**This reorders the sources; it does not loosen rule 2.** Nothing about what may
cross out of `reverse-engineering/` changes — the fact / expression boundary is
the whole ADR and it stands. What changes is where to look *first*:

| Before | After |
|---|---|
| OpenKeeper, then Flame, then Ember, then RE docs, then experiments (`AGENTS.md`) | **The binary first**, then the shipped data, then the running game, then the community sources |

The reasoning is the owner's own and it is hard to argue with: `DKII.EXE` **is**
DK2. Every other written source — OpenKeeper's field names, Flame's tables, the
wiki's prose, this project's own inferences — is somebody's *reading* of that
binary or of the game it runs. Reading the binary is reading the thing itself.

**Rule 3 survives, narrowed.** The running game still outranks the binary *for
behaviour the owner can observe*, because the decompiler is an inference engine
and a misread disassembly is still a misread. What rule 3 must no longer be used
for is deferring a question the owner cannot easily observe — "put it to the
owner" is not a substitute for opening Ghidra. Where the binary and an
observation genuinely conflict, the observation wins **and the conflict is
written down**, as before.

**Rule 5 survives unchanged.** CONFIRMED still means the running game. Being the
primary source to *consult* is not the same as being the highest court on
behaviour, and conflating those would let a plausible-looking decompilation
freeze a wrong answer as CONFIRMED.

**What it costs in practice:** an open question now owes a Ghidra pass before it
is recorded as UNKNOWN with a "please play the game" note attached. The pass that
prompted this amendment took twenty minutes and returned, from one debug-dump
function, EA's own names for about 220 creature-instance fields — including
`InWater`, `IsSlowed`, `Leaving`, `TurncoatPlayer`, `TurncoatTimer`,
`HasBeenKnockedOut`, `DieInstantly`, `PortalGeneratedFromId` and
`ContributesToPortalLimit`, four of which land directly on open questions.

### The licence question, flagged rather than answered

The brief says to flag legal questions rather than assume. Two, both noted and
neither resolved here:

- **The GOG EULA is a contract question distinct from copyright.** Reverse
  engineering for interoperability has statutory protection in the EU
  (Directive 2009/24/EC Art. 6, and Art. 5(3) for observation and testing,
  which is what the observation checklist has been doing all along) and a
  fair-use history in the US. Contractual prohibitions on it are of *doubtful
  enforceability* in the EU and are void in some member states — but "doubtful"
  is not "settled", and this project does not need to bet on it.
- **What protects us is rule 2, not the statute.** Nothing derived from the
  binary is distributed. The repository ships facts and our own code; the
  decompilation stays on one machine. That is the same posture the project
  already has toward the game's assets, which are equally undistributable and
  equally used every day as an oracle.

### What this unblocks, in order

**Q1/Q83, the tick rate** — the project's largest single blocker,
`SimulationTiming.TicksPerSecond = 20` is an admitted placeholder, and it is on
the *playing cannot settle* list. The route is the four imported timer APIs
(`QueryPerformanceCounter`, `QueryPerformanceFrequency`, `timeGetTime`,
`GetTickCount`), all four of which the strings pass already found.

Then **Q86's interval slope** — the owner has now CONFIRMED the interval grows
with the dungeon (see Q86), which leaves only *by how much*; `Entrance.cpp`'s
assert strings give the locality. Then the per-tick subsystem ordering, which
`SimulationSystems` says outright is ours. Then Q90's entrance percentages, and
ADR-0023's `DigRockHealth` override — the one place shipped data is deliberately
disbelieved, and worth knowing whether the binary disbelieves it too.

---

## ADR-0047 — A portal produces only creatures whose own flags allow it

**Date:** 2026-09-04
**Status:** Accepted
**Evidence:** the binary (ADR-0046), `docs/research/binary-entrance.md` §1
**Amends:** ADR-0042 (the portal), which had four gates and needed five

### The decision

`PortalRules.CanComeFromPortal` is now the first gate in
`PortalSystem.Pick`: a creature may come out of a portal only if it carries
`CreatureFlags.AvailableViaPortal` (bit 10) and does **not** carry
`CreatureFlags.IsUnique` (bit 27). Both flags were already parsed and named;
neither was read by anything.

### Why, and it is not a tidy-up

The four existing gates let through nine creatures DK2 will not produce.
**Frosty's Castle**, **Frosty's Lair** and **Frosty's Realm** mark the hero
roster — Knight, Giant, Guard, Royal Guard, Monk, Wizard, Thief, Fairy, Elven
Archer — both AVAILABLE and pooled for every keeper from player 3 to player 6.
Availability passes, the pool passes, and a keeper's portal on those levels
produced Knights.

The entrance in the binary tests the creature's flags **before** consulting the
level at all, which is also the natural order: whether a Knight can come out of
a portal is a fact about Knights, not about the level.

### The two things worth remembering about it

**A flag nothing reads is a rule nothing enforces.** `AvailableViaPortal` had
been sitting in `CreatureFlags` with the right name since creatures were
parsed. The gap was not knowledge, it was that no rule asked. This is the same
shape as ADR-0045 and it is now the third instance — see `GOAL_ALIGNMENT.md`,
whose review clauses this feeds: *does anything read this fact at all?*

**Availability is not the same question as producibility**, and the campaign
proves it rather than merely permitting it. A creature can be in your roster and
your pool and still not be something a portal hands you — because DK2 has other
ways to acquire creatures, and the flag is how it says which.

### The half that cannot be checked from the files

No shipped level pools or unlocks a unique creature, so the `IsUnique` test
never fires on shipped data. It rests on the binary alone, and it is implemented
because all thirteen uniques *do* carry `AvailableViaPortal` — the second test
is exactly what stops the first from handing over a Unique Dark Angel. Both
premises are now tests (`NoShippedLevelEverPoolsOrUnlocksAUniqueCreature`,
`TheThirteenUniqueCreaturesAllCarryThePortalFlag`) rather than sentences.

---

## ADR-0048 — A creature has health and a hunger clock

**Date:** 2026-09-04
**Status:** Accepted
**Evidence:** `docs/research/creature-needs.md`
**Milestone:** M4, second item after the portal

### The decision

`CreatureState` gains `Health`, `HungerTicks` and `ChickensNeeded`.
`CreatureHealthRules` holds the rules and `CreatureHealthSystem` applies them
once a game second, registered last in the tick. HashVersion 10 → 11, snapshot
FormatVersion 10 → 11.

Health first because **everything unbuilt reads it**: the balance table has 21
health variables and they are the lair, the prison, torture, praying, fire,
possession and the heart. `CreatureSleepsWhenBelowPercentHealth` makes sleep
itself a function of health. Each of those is about one line once health exists
and impossible before.

### `SpawnCreature` now takes the database, and that is the point

A creature seeded from a zero maximum dies on the tick it arrives. So there must
be **no way to make one** — no overload that leaves health out, no lazy
initialisation, no "0 means unset". The database is a parameter for exactly one
line of that method, and it cost 25 call-site changes across 16 files.

That is the right trade. The alternative — seeding health somewhere later —
makes a silent corpse the default for anyone who adds a spawn site and does not
know the convention. `EveryCreatureHasHealth` checks the other half of the
invariant, that no shipped record has zero `Hp`.

### Per second, not per tick

Every number involved is written *per second* in the data. Applying a twentieth
of it twenty times a tick would invent precision the files do not have and would
make the result depend on `SimulationTiming.TicksPerSecond`, which is still an
admitted placeholder (Q1). Same cadence and same phase as `ManaSystem`, which
already had to make this choice.

### Three things the data said that no list had

**32767 means "never", and it is per need rather than per creature.** Eight
creatures never get hungry, six never sleep, and **neither set contains the
other** — the Vampire sleeps and never eats, the Dwarf eats and never sleeps. No
single "undead" flag can produce that. This closes a question that had been open
since creatures were parsed.

**Own-land regeneration is real and applies to two creatures out of 46** — Imp
67 a second, King Reginald 400, everyone else 0. The same shape as mana's three
terrain types out of 41 (ADR-0017). Implemented as data so it is a no-op for the
other 44 with no special case, and tested with a creature that has it *and* one
that does not, because a rule written from the Imp alone would read as general.

**A chicken can over-heal, so the clamp is load-bearing.** Jack has 240 health
and a chicken worth 250. The test asserting that *no* creature could be
over-healed was written first, and the install refuted it.

### Hunger is half-built, deliberately

The clock runs and nothing can stop it: eating needs a chicken, the Chicken is
object 9, and nothing lays one — a Hatchery is a room this simulation does not
have. (Until 2026-09-04 the reason given here was that OBJECTS was unparsed. It
is parsed; the room is what is missing.) **This costs no health on purpose:**
searched 21 health variables and nine anger variables, and there is no
starvation anywhere in the balance table. What going unfed costs is *anger*,
which is per creature in the record (`angerNoFood`). Tested as a negative so
nobody later turns it into damage.

### The reading that is weakest, and it is flagged rather than buried

Lair healing is implemented as **standing on a lair tile the creature's keeper
owns**. DK2 gives each creature a personal lair with its own bed and heals it
while it sleeps there — and there is no lair ownership, no sleeping and no bed
yet, all three of which are the next mechanic. The proxy is deliberately a
single expression so replacing it is one edit, and it is knowingly wrong twice
over: a creature heals while awake, and in any of its keeper's lairs.

Put to the owner as **Q101**, with Q99 (is zero health death, or a 60-second
unconscious window?), Q100 (does regeneration care what the creature is doing?)
and Q102 (when does the hunger clock start?).

### What the binary could not say — **SOLVED 2026-09-05**

**As written on 2026-09-04:** `HungerState.cpp` and `LairState.cpp` are both
named in the executable's own assert strings, and four different approaches
failed to reach their code — the creature state machine is dispatched entirely
through vtables, so Ghidra's analysis never discovers those function bodies, and
forcing them did not work either. **Nothing in this ADR rested on the binary.**

**⚠ That negative result expired.** The route that worked is neither of the two
that were tried: **scan initialised memory for the 32-bit value of the assert
string's own address**, rather than asking Ghidra for references to it. Ghidra
resolves a reference only when it can reach the instruction; an address loaded as
a raw immediate inside an undiscovered function body has none. `LairState.cpp`
shows **zero** referring functions by the reference database and **one** by the
raw scan — and that one hit landed in the middle of the lair-acquisition code.

From there the neighbourhood decompiles normally, and `lair-acquisition.md` is
what came out. The vtable route was never needed. `ScanForAddr.py` is the tool
and `source-file-map.md` is the index of which name to scan for.

---

## ADR-0049 — A creature has its own bed, and heals only while asleep in it

**Date:** 2026-09-04
**Status:** Accepted
**Evidence:** owner observation — **CONFIRMED**
**Amends:** ADR-0048, whose lair healing was a proxy and is now replaced

### What the owner said

> *"A lair is a room with tiles, on each tile only one bed/chair/whatever per
> creature can be present. When a creature enters a dungeon through a portal it
> goes to a lair, finds a free tile and occupies it with its bed. When a
> creature is injured it heals while it sleeps in its own bed."*

Plus two corrections in the same breath: **a skeleton does not have a lair**, and
**Horny is a summon** — not in a regular roster, and only on particular maps
when conditions are met. Both are consistent with `LairObjectId` being 0 for
them, and the second is independently why Horny carries no
`AvailableViaPortal` (ADR-0047).

### The decision

`CreatureState` gains `LairTileIndex`, `AwakeTicks` and `SleepTicks`.
`LairRules` holds the rules; `CreatureSleepSystem` claims a bed, walks the
creature to it, puts it to sleep and wakes it. HashVersion 11 → 12, snapshot
FormatVersion 11 → 12.

`CreatureHealthRules.LairHealPerSecond` is now one expression:
`AsleepInOwnBed ? 100 : 0`.

### What this replaced, and it was wrong twice over

ADR-0048 healed **anything standing on any of its keeper's lair tiles**. Both
halves were wrong:

- **Asleep, not present.** A wounded creature standing awake in its own lair
  heals nothing.
- **Its own bed, not its keeper's lair.** One bed per tile, one tile per
  creature. A creature with no bed never heals, whatever it stands on.

Both were flagged as the weakest reading in the mechanic and written as a single
expression precisely so that replacing them would be one edit. It was.

### The third thing, which nobody asked

**A lair's capacity is its tile count** — one bed per tile. Worth an ADR line of
its own because it is **not** the general rule for rooms: a temple's capacity is
its *rim*, `2(w+h) − 4` (Q60), and a dungeon heart stores gold in an outer ring.
So capacity is per room type, and the lair's happens to be the obvious one.
Anyone tempted to write a general `Capacity(room)` should read Q60 first.

### The first driver a non-worker has ever had

Until now the only creature that moved under its own steam was an imp, driven by
`ImpTaskSystem`; a goblin out of a portal stood on the hole forever because
nothing looked at it. `CreatureSleepSystem` is the other half of that, and it
**reuses the imps' pathfinder and the imps' movement system** rather than
growing a second way to walk. Registered after `ImpTaskSystem` and *before*
`CreatureHealthSystem`, so a creature that falls asleep this tick is healed on
the same game second rather than a second late.

Workers are skipped by an **explicit** check rather than incidentally. An imp
has no bed and never sleeps, so the two systems could not fight over one — but
"the imp is excluded because its record happens to say 0" is the kind of
reasoning that stops being true when somebody edits a record.

### Two invariants that needed their own code

**A bed is not a promise.** The lair can be sold, dug out or claimed by a rival
while its owner is asleep in it, so `BedStillValid` is checked every tick and
the bed is given up when it fails. Without it a creature goes on healing on a
patch of claimed floor.

**A dead creature gives up its bed.** Otherwise a lair silently loses a tile
every time something dies in it. Both are tested.

### Occupancy is derived, never stored

A tile is taken if some creature's `LairTileIndex` points at it — the same
discipline `RoomInstance` follows, and for the same reason: nothing can strand a
bed that no creature owns. The cost is a scan of the creature list per query,
and creatures are tens where tiles are tens of thousands.

Which free tile gets claimed is **lowest tile index first**, across lairs in
`RoomIndex` order. DK2 almost certainly picks nearest-to-the-door; this is
deterministic, which is the property that cannot be given up (ADR-0001).

### What is still a reading

The **cycle**: `TimeAwake` 240 and `TimeSleep` 9 read as seconds, so four
minutes up and nine seconds down. That looks wrong until you multiply — nine
seconds at 100 a second is 900 of a Goblin's 1500 — but it is a reading, and it
is Q103. The wounded shortcut is the data's own:
`CreatureSleepsWhenBelowPercentHealth` is 50, which is what makes a losing fight
end with creatures streaming home rather than dying in place.

Also Q104: the **Dwarf** and the **Stone Knight** carry a bed object *and* a
`TimeAwake` of 32767, so by the record they occupy a lair tile and never sleep in
it. `NeedsBed` gates on the object, so here they take a bed and never use it.

---

## ADR-0050 — The six anger fields are signed, and anger waits for its unit

**Date:** 2026-09-04
**Status:** Accepted
**Evidence:** shipped data; the ladder CONFIRMED by the owner (Q9, Q105)
**See:** `docs/research/creature-anger.md`

### Two decisions, and the second is a decision not to build

**One: the anger fields are `short`, not `ushort`.** `AngerSlap` and
`AngerInHand` were parsed as unsigned, and `kwd-creatures.md` documented the
whole run of six that way for four months. Read unsigned, a Goblin's
`AngerInHand` is 65286 and Jack's `AngerSlap` is 60536 — **−250 and −5000**.

Signed, the table means something:

- **slapping angers** (+3000 Goblin, +7000 Warlock, +8000 Horny)
- **being picked up calms** (−250 Goblin, −500 Warlock)
- **Jack is the inverse** — slapping calms it, being held annoys it

All six are now signed. The first four hold 0..150 on every shipped creature, so
signed reads them identically and is strictly safer. **Had anger been built
first, picking a creature up would have added 65286 to its anger** and maxed it
on the spot.

**Two: anger is not implemented, and will not be until Q106 is answered.**

### The ladder is known; the unit is not

The owner has CONFIRMED the ladder twice from two causes — *"unpaid → unhappy →
angry → leaves"* (Q9) and *"if a creature can't get a lair it becomes unhappy,
it can't go to sleep, then angry, then it leaves the dungeon"* (Q105). Every
rate is in the data: `AngerNoLair` 100 a second for a Goblin, a lair calming at
−550, a slap at +3000, nine more variables besides.

What is missing is what those numbers are **measured in**.
`UnhappyThreshold` is 50 on all 46 creatures, which makes it a percentage — and
**there is no ceiling anywhere**: not in the 214 balance variables, not in the
creature record, and **not in OpenKeeper, whose `CreatureMoodSystem` is an empty
stub with a TODO.** This is the first mechanic the project has reached where
OpenKeeper offers nothing at all.

A ceiling of 65535 makes a lairless Goblin leave after eleven minutes; a ceiling
of 10000 makes it two. Both are self-consistent. Both are plausible.

### Why waiting is the decision rather than the absence of one

**This is Q47 again**, and that lesson cost something worth not repeating. Q47
had a plausible answer — work applied twice a second at half the tabled delta —
that fitted every observed range and was wrong. It was left unbuilt because
ranges are not counts; the measurement came back at five a second, and *not
having built it made the correction free*.

Anger is the same shape, and worse placed: mood feeds job selection, desertion
and the rest of M4, so a wrong ceiling propagates into everything built on top
of it rather than sitting in one rule.

**The measurement is one stopwatch and needs no precision.** Give a creature
nowhere to sleep and time how long until it leaves; or count how many slaps make
a content creature angry. Either pins the ceiling, because the rates are known.

### What the absence costs, stated rather than hidden

- A creature with no bed stays awake for ever and never recovers from a wound.
- A hungry creature that cannot eat is unharmed for ever — which ADR-0048
  established is right about *health* and is now known to be wrong about
  *anger*.
- Neither matters much yet, because **nothing can slap or pick up a creature**,
  so the two largest anger sources have no trigger. That is the sequencing
  point: anger has no player input to respond to until the hand exists.

---

## ADR-0051 — Zero health is a sixty-second window, and the dying creature is a creature while the corpse is not

**Status:** Accepted (2026-09-04)
**Confidence:** Confirmed (owner, Q99)
**Supersedes:** the death branch of ADR-0048

### Context

`CreatureHealthSystem` removed a creature the moment its health reached zero and
emitted `creature_died`. That was recorded as a placeholder with Q99 against it,
because `CreatureDyingStateDurationSeconds` is 60 and 60 seconds is a long time
to specify for nothing.

The owner answered:

> *"When a creature drops, its petals on the flower above its head are exhausted,
> it remains lying on the ground for a period of time, that gives imps an
> opportunity to return a creature to its bed in the lair for healing, or to
> prison if it is an enemy creature. Once that timer runs out a creature dies and
> its corpse remains there lying, that gives imps an opportunity to carry it to
> graveyard. If that timer runs out and no one carries the corpse it just
> disappears."*

**Two timers, three states, and both timers were already in the shipped data:**

| Variable | Id | Value | What it times |
|---|---|---|---|
| `CreatureDyingStateDurationSeconds` | 26 | **60** | collapsed, rescuable |
| `DeadBodyDiesAfterSeconds` | 23 | **60** | corpse, carryable |

Both had been parsed for months with nothing reading either. Two independently
named variables holding the same value looked like redundancy; the answer says
they are two clocks on two different things.

### Decision

**1. Zero health collapses; it does not kill.** A creature with no health enters
`CreatureActivity.Dying`, keeps its owner, its tile, its gold and its bed, and
dies only when `CreatureDyingStateDurationSeconds` elapses.

**2. A dying creature does nothing at all** — no movement, no work, no sleep, no
hunger, no regeneration.

**3. The only route back is a bed.** `CreatureDeathRules.Rescue` puts a rescued
creature into its own bed as `Sleeping` at *unchanged* health, and the lair heals
it at `ModifyHealthOfCreatureInLairPerSecond`. Nothing calls it: carrying does
not exist. It is written and tested for the same reason `Eat` is.

**4. The dying creature stays in `WorldState.Creatures`; the corpse is not
modelled.** A collapsed creature has health, an owner, a tile and a bed, and
recovers by being healed — everything that reads a creature keeps working on it.
A corpse has none of that: it has a `DecomposeValue` that imps haul and a
graveyard counts toward `DecomposeValueNeededForVampire`. Modelling it as a fake
creature would put a thing with no needs into the creature list, where every
present and future system would have to remember to skip it.

> **CORRECTED 2026-09-04 — the reason given here was wrong.** This paragraph said
> a corpse "in DK2 is an *object*" and that OBJECTS was unparsed (M6), and that
> pairing became the standing explanation for three gaps: no corpse on the floor,
> no graveyard, no vampire.
>
> **OBJECTS is parsed now, and none of its 145 objects is a corpse.** The nearest
> are the untyped props Skeleton (48), Skull (49) and Bone (50). So corpses were
> never blocked on that record, and the evidence had been pointing the other way
> the whole time: both timers are **creature** variables
> (`CreatureDyingStateDurationSeconds`, `DeadBodyDiesAfterSeconds`),
> `DecomposeValue` is a **creature** field, and the binary's per-creature debug
> dump carries `HasBeenKnockedOut` and `DieInstantly`.
>
> **A corpse is a creature in a state.** The *decision* above still stands on its
> own merits — a corpse should not be a fake creature in `WorldState.Creatures`
> with needs it cannot have — but what it is waiting for is a **carry task and a
> graveyard**, not a record. That is a much smaller job than this ADR implied for
> five sittings. See `docs/research/objects.md` §4.

**5. `GenerateDeadBody` governs, not `DecomposeValue`.**

### Consequences

**What it costs, stated rather than hidden:** a creature that dies vanishes
instead of leaving a corpse for 60 further seconds, so nothing accumulates toward
a graveyard and **no Vampire can ever be made**. Known gap, named cause.

`HashVersion` 12 → 13 and snapshot `FormatVersion` 12 → 13: `CreatureState`
gained `DyingTicks`.

### The bug this found, which is the reason the guard is on health and not on state

Guarding the other systems on "is in the Dying state" was wrong, because the
collapse is *booked* once a game second. Between health reaching zero and the
booking there is a window in which a creature has no health and is still `Idle`,
and two things exploited it:

- **A creature at zero health near its bed** decided it was wounded enough to
  sleep (`CreatureSleepsWhenBelowPercentHealth`), walked there, and healed — so
  it **never collapsed at all** whenever a bed was in reach.
- **An Imp at zero health on its own land** regenerated its 67 a second before
  the collapse was booked, climbed back to 500, and **could not be killed at
  home**.

So every system guards on `CreatureDeathRules.IsDown` — *has no health, or is
already dying* — rather than on the state flag. This matters beyond the current
cadence: combat will subtract health per tick, not per second.

Two consequences of the same reasoning:

- **Own-land regeneration is suppressed at zero health; lair healing is not.**
  The owner's answer gives exactly one route off the floor, so passive
  regeneration must not be a second one. Lair healing must still work at zero,
  because that *is* the rescue.
- **The order inside `CreatureHealthSystem` is load-bearing**: healing applies
  *before* the collapse test, so a creature rescued at zero health is above zero
  by the time anything asks. Reversed, a rescue would collapse the creature it
  just saved.

### What the data gave up in passing

**Eight of 46 creatures leave no body:** Imp, Vampire, Skeleton, Horny, Stone
Knight, Unique Skeleton, Unique Vampire, **Jack**. That is the **third near-miss
grouping** in this record and the third refutation of "these are the undead, one
flag would do":

| Set | Members not shared |
|---|---|
| never eats | **Unique Firefly** (leaves a body) |
| no body | **Jack** (eats) |

Seven in common, one each way. Never-sleeps is a third, distinct set — the
**Dwarf** sleeps never and leaves a body. There is no undead flag; each need is
its own column, and all three sets are asserted by name because every one was
guessed wrong before it was read.

**And two records carry unreachable data:** `Horny` and `Jack` both have
`DecomposeValue = 200` while generating no body, so those 200s can never be read.
Both are records where a template value would plausibly survive unedited — Horny
is a summon rather than a roster creature (CONFIRMED). A graveyard counting
`DecomposeValue` would be fed by two corpses that never exist.

### Rejected

- **Modelling the corpse as a creature with a second timer.** Rejected under
  point 4: it puts a non-creature in the creature list.
- **Granting a rescued creature 1 health** so it is not collapsed. Invents a
  number; the ordering achieves it with none.
- **Letting a collapsed creature claim a replacement bed** when its lair is sold
  from under it. The owner describes claiming as *"it goes to a lair, finds a
  free tile and occupies it with its bed"* — a walk and a placement, which
  something unconscious cannot do. Cost: such a creature can no longer be
  rescued at all (Q107) — which cost nothing until ADR-0063 and now decides
  whether an imp sets off for it at all. The
  alternative costs something now: a dying creature would take the last free bed
  from a goblin that walked in on its own feet.

### Amendment (2026-09-04, same day) — not every creature gets the window

The owner corrected the rule within hours of it landing:

> *"Imps are dead right away, they don't have a lying state. Vampires also don't
> drop a body, they reincarnate at their sarcophagus and sleep to recover, but
> they lose a level once they reincarnate. If they were killed at level 1 they
> are permanently dead. Exception to that rule for vampires are monks: if a monk
> kills a vampire it is permanently dead, regardless of its level."*

**A creature that leaves no body has no lying state — it dies on the spot.**
`CreatureDeathRules.HasDyingState` is `LeavesBody`, and that is **CONFIRMED from
two directions that never met**: the owner for the Imp, and OpenKeeper's
`HealthSystem.processHealthDepleted`, which opens with

```java
// No body, just vanish from the world
if (!isLeaveDeadBody(entityId)) { ...remove(); return; }
```

before any unconscious handling, reading `GENERATE_DEAD_BODY` and nothing else.
So **one flag decides both halves**, which is coherent: the dying state *is* a
body on the floor, a live one. Nothing left behind means there was nothing to
lie there.

This also retires the puzzle in the parent decision about the bodyless set being
an odd mixture. It is not a mixture: a summon, three undead, a construct, a
worker and Jack are exactly the things that puff out rather than fall over.

**The vampire is data-driven and deliberately unimplemented.**
`CanBeResurrected` (bit 23) is set on exactly the two Vampires. Two facts snap
into place — the **sarcophagus is the vampire's lair bed** (its `LairObjectId`
is a coffin, pinned months ago for an unrelated reason), so reincarnation is
`Rescue` with no imp involved; and the vampire leaving no body stops being an
oddity, because there is no corpse when the creature is not finished.

**Corrected 2026-09-04, by the owner retracting their own claim.** *"sarchopagus
at the graveyard, I made a wrong claim earlier."* So reincarnation happens **at
the Graveyard**, not at the vampire's own bed, and the "two facts snapping into
place" above was one fact and one mistake — built on the owner's first wording
(*"they reincarnate at their sarcophagus"*) and on an assumption that its
`LairObjectId` was that sarcophagus.

**The shipped data agrees with the correction, in two places neither of which
was consulted when the wrong claim was made:**

- **`Rooms.kwd` gives the Graveyard a `CreatedCreatureId` of 8 — the Vampire.**
  Exactly two rooms in the whole table create a creature: the Graveyard (8,
  Vampire) and the Prison (9, Skeleton). The room record says outright which
  room makes a vampire.
- **The Graveyard's object ids are 31, 32, 33, 34, and the Vampire's
  `LairObjectId` is 26.** Different objects. The bed is not the sarcophagus, and
  nothing ever said it was except us.

**And the binary uses neither word.** `Object.cpp`'s union dump names its object
kinds, and among them are `Lair` and `TombStone` — there is no Sarcophagus and no
Coffin anywhere in the executable's strings (binary, ADR-0046 — facts only). So
in DK2's own vocabulary a bed is a `Lair` object whatever art it wears, and the
graveyard's furniture is a `TombStone`. The vampire's coffin-looking bed and the
graveyard's sarcophagus are two different objects that happen to look alike, and
the wrong claim was a pun.

**What still stands:** the vampire leaving no body stops being an oddity, because
there is no corpse when the creature is not finished. And the mechanic remains
deliberately unimplemented for the level reason below — the correction changes
*where* reincarnation would put the creature (a Graveyard tile, not its bed),
which matters the day it is built and not before. Q113 is closed.


It stays unimplemented because **the cost of reincarnation is a level and levels
do not exist** — a vampire that reincarnated without paying would come back for
ever, which is the immortal Imp again in a cape. Both exits are equally out of
reach: level 1 needs levels, and "a monk killed it" needs an attributed killer,
which needs combat (M7).

**Instant death is therefore correct for vampires rather than a placeholder:**
with no levels every creature is level 1, and a level-1 vampire is permanently
dead by the owner's own rule. The flag is read, tested, and branched on by
nothing (Q111).

OpenKeeper neither implements nor reads `CanBeResurrected`. This is the second
mechanic where the owner's testimony is the only source there is, after the
anger ceiling.

**One guard changed subject rather than becoming redundant.** Suppressing
own-land regeneration at zero health was written to stop a collapsed Imp healing
off the floor, and the Imp no longer collapses. The subject is now **King
Reginald**: 400 health a second on his own land *and* a dead body, so he
collapses and would heal every second he lay there. He is the only creature in
the shipped data with both properties, so the test asserts he is the only one.

**And a fixture bug this exposed:** `Dk2TerrainFixture`'s Goblin had
`GenerateDeadBody` missing, so every collapse test would silently have been
testing instant death. That is the **third** transcription error the drift guard
has caught in this one record, after the Imp's hunger rate (wrong by 4096) and
its own-land healing (transcribed as 0).

---

## ADR-0052 — Only a keeper is staffed, and the test suite stops calling the hero side "Keeper"

**Status:** Accepted (2026-09-04)
**Confidence:** Confirmed (structural — `PlayerIds`, ADR-0043)
**Closes:** the implementation half of Q91

### Context

`PlayerIds` (ADR-0043) established that **0 is unowned, 1 is the hero side, 2 is
neutral and 3–7 are keepers**. `PortalSystem` was written afterwards and gates on
`PlayerIds.IsKeeper`. `ImpSupplySystem` was written before, and did not: it
looped every player in the world and would hand free imps to 0, 1 and 2, and
would let any of them cast Create Imp.

This had been recorded as "harmless on shipped data, because none of those three
has a dungeon heart" — `Spawn` returned null and the clock simply ran again.

**That reasoning was wrong in a way worth naming**, and it is the exact shape
this project has a memory entry about: *a claim nothing checks may already be
false*. The rule held because of a fact about the shipped levels, not because
anything checked it. Two consequences:

1. **A level that gave the hero side a dungeon heart would have quietly staffed
   it with imps.** Nothing in the code said otherwise.
2. **It was not even harmless.** `PlayerState.FreeImpCountdownTicks` is hashed
   state, and it was counting down and resetting every interval for players 0, 1
   and 2 on every level. So a non-keeper's imp clock was **inside the
   determinism hash** — which is why seven scenario hashes moved when the gate
   went in, on scenarios where no imp was ever produced.

### Decision

**1. `ImpSupplySystem` gates on `PlayerIds.IsKeeper`** in both paths: the free
top-up skips non-keepers (and holds their countdown at zero rather than running
it), and `Buy` refuses with `reason=not_a_keeper` **before the cost is looked
up**, so no mana moves.

The refusal is distinct from `reason=no_such_player` deliberately: Neutral
*exists* and owns real terrain — 167 of the campaign's portals — and still may
not have imps.

**2. The test suite stops using non-keeper ids as keepers.** Fifteen files
declared `Keeper = 1` and `Rival = 2`. They are now `PlayerIds.FirstKeeper` and
4, and the worlds they build carry `PlayerIds.Neutral` where they previously
carried a bare `(byte)2` standing in for a rival.

**3. `Dk2TerrainFixture.Map()`'s starting room now belongs to the first
keeper.** It belonged to **player 1** — so every test treating it as "my
dungeon" was giving a keeper's home ground to the hero side.

**4. Seven scenarios are re-keyed**, including one that used **`id: 0`** — the
*unowned* player — as its keeper.

### Consequences

Seven scenario hashes moved, and every one is the `FreeImpCountdownTicks`
pollution above rather than a behaviour change. Verified by re-running one
scenario with the ids stashed: the event log is identical byte-for-byte apart
from the player number.

Four new tests, and the shape of them is the point: **a non-keeper is given a
dungeon heart and the whole starting room, and still gets nothing** — because
testing the gate on a player who could not have produced imps anyway would
prove nothing. `AKeeperWithTheSameHeartIsStaffed_WhichIsWhatMakesTheGateAGate`
is the control, since a guard that refuses everybody is not a guard.

### Why the fixture bug hid for so long

Two errors that agreed with each other. The suite called player 1 "Keeper", and
the fixture map gave player 1 the starting room. Each made the other look
correct, and the tests passed. Re-keying the constant alone produced **38
failures** — territory that would not spread, claim jobs that were never
queued, a rival's treasury that could not be taken — every one of them the map
still handing the room to id 1.

That is the second time this pairing has bitten: `PlayerIds` itself (ADR-0043)
was written because ownership ids were being guessed from context rather than
read.

### Not done

`ManaSystem` still runs for every player. Unlike imps this is not obviously
wrong — mana comes from owned terrain, and non-keepers do own terrain — and
"only keepers accrue mana" is a claim with no source behind it. Left alone
rather than changed on aesthetics; noted here so it is a decision and not an
oversight.

---

## ADR-0053 — Anger's ceiling is 32767, measured twice by two methods

**Status:** Accepted (2026-09-04)
**Confidence:** Confirmed (owner measurement, twice, by independent methods)
**Closes:** Q106 and Q114. **Supersedes** ADR-0050's decision to defer anger.

### Context

ADR-0050 deliberately left anger unbuilt. Every rate was in the shipped data —
`AngerNoLair` 100 a second for a Goblin, nine more variables besides — and
`UnhappyThreshold` was **50** on all 46 creatures, which makes it a *percentage*
of a ceiling that appears **nowhere**: not in the 214 balance variables, not in
the creature record, and not in OpenKeeper, whose `CreatureMoodSystem` is an
empty stub and whose `CreaturesController` carries the comment
`// I guess the initial mood is 10000`.

That was Q47's lesson applied: a mechanic whose every rate is known and whose
unit is not. Building it on a guessed ceiling would have propagated the error
into job selection, desertion and the rest of M4.

### The measurements

**One — the stopwatch.** A Goblin in a dungeon with no lair, everything else
provided, so `AngerNoLair` = 100/s was the only source running:

| Observed | Anger accrued | Against |
|---|---|---|
| unhappy at ~2:45 (165 s) | 16 500 | **16 383** = 32767 ÷ 2 |
| angry at ~5:30 (330 s) | 33 000 | **32 767** = `short.MaxValue` |

Both inside 1%, which is inside the resolution of "ish" on a wristwatch.

**The ratio is the stronger half.** 330/165 is exactly 2, and that pins three
things *without* the ceiling: anger accrues **linearly**; the **angry** threshold
is **100%** of the ceiling, a number that exists in no variable; and
`UnhappyThreshold` = 50 really is a percentage. It also refutes 10000 outright,
which predicts 50 s and 100 s.

**Two — the slap count, which closed the gap timing could not.** Duration cannot
separate *(100/s into 32767)* from *(200/s into 65535)*: doubling both preserves
every time, and 65535 is equally round. **This is Q47's trap exactly**, so it was
named rather than buried, and the discriminator is a *discrete* delta, which
scales with the ceiling where a rate does not.

The owner slapped a **Salamander** — whose `AngerSlap` is also 3000 — and got
**exactly 6 slaps to unhappy, then 5 more to angry.** The two counts *bracket*
the ceiling rather than merely agreeing with it:

| Observation | Implies |
|---|---|
| 5 not enough, 6 enough | unhappy ∈ (15000, 18000] ⇒ ceiling ∈ (30000, 36000] |
| 10 not enough, 11 enough | angry ∈ (30000, 33000] ⇒ **ceiling ∈ (30000, 33000]** |

32767 is the only meaningful number in the intersection. **65535 and 10000 are
excluded outright** — 65535 would have needed 11 slaps to *unhappy* and 22 to
angry.

**And that fixes the cadence, which is the part worth having.** With the ceiling
pinned, the 165 seconds gives the rate as 16383/165 = **99.3, i.e. 100 once a
second**, exactly as the field name says. No hidden sub-second cadence. Two
independent methods, one number — which is what closes both questions rather
than trading one unknown for another.

### Decision

`CreatureAngerRules.Ceiling` = `short.MaxValue`. **Not a balance variable**,
because it is not in the balance table — it is a property of the field width,
and the six per-creature anger fields are signed 16-bit (ADR-0050). 32767 is
*already* DK2's sentinel in the same record, where it means "never" for
`HungerRate` and `TimeAwake`: one house style, two meanings.

**The ladder** is `Content → Unhappy → Angry`, at 0, 50% and 100%. Confirmed
three times from three causes before it was ever built — Q9 (pay), Q105 (lairs),
and the bed-loss answer.

**What an angry creature does comes from its own record**, not from a rule here:
a Goblin's `AngryJobs` is `Leave(50%)` / `Rebel(50%)`, drawn from the
simulation's RNG so it is part of the deterministic stream, and drawn **once and
kept**. Kept because re-rolling every second would eventually draw `Leave` by
luck and desert — reporting the unimplemented outcome as the implemented one.

**Amendment, 2026-09-05 (Q122, from the binary).** The draw-once-and-keep rule
is wrong in shape. DK2's angry chooser rolls afresh on every allowed selection
and falls through the list in order when the chosen job fails; a new selection
is allowed only after five game seconds (5 × variable 19 = 20 ticks), and
`LastGameTickSuccessfullyChoseAngryJob` is the timestamp that gate reads. The
concern above — that re-rolling would make a Goblin leave by luck — describes
DK2: with `Leave(50%)` it does. The correction is to re-roll on the five-second
gate and keep reporting `Leave`/`Rebel` as unbuilt. See `lair-acquisition.md` §8
and Q122. **Built the same day as ADR-0061**, which supersedes the rule above
and keeps its principle.

**Only two of DK2's anger sources are applied, and the rule is one sentence: a
source is applied when the player's own choices can turn it off.**

| Source | Applied? |
|---|---|
| `AngerNoLair` +100 | **Yes** — lairs are built, sold and filled |
| `ModifyAngerOfCreatureInLairPerSecond` −550 | **Yes** — sleep works, so recovery is real and 5.5× faster than the accrual |
| `AngerNoFood` +100 | No — the Chicken is object 9 but no Hatchery lays one; *no* dungeon can feed anybody |
| `AngerNoWork` +50 | No — non-workers have no job system; "no work" is a fact about us |
| `AngerNoPay` +60 | No — there is no payday |
| hated company +150 | No — the relation is parsed and unused |

Otherwise anger stops being feedback and becomes a countdown no dungeon
survives, which would say nothing about DK2 and would end every long scenario in
mass desertion.

### Consequences

`HashVersion` 13 → 14, snapshot `FormatVersion` 13 → 14: `CreatureState` gained
`Anger` and `AngryChoice`.

**A creature that is down is skipped.** Something lying on the floor waiting to
be carried to bed is not accruing a grievance about not having got there — and a
dying creature that deserted would leave the rescue window by a second route
(ADR-0051).

**Imps need no special case.** The DK2 wiki says flatly *"Imps never get
angry"*, and the shipped data says the same arithmetically: `AngerNoLair` = 1,
which against 32767 is **nine hours**. It is moot anyway, since an imp is owed no
bed. The **Skeleton** is the only creature whose four `angerNo*` fields are all
zero.

### Not done

**Rebelling**, which is half of a Goblin's table. It needs a creature to change
sides and become a hero; `RebelLeavingPercentage` (70),
`RebelBecomeGoodPercentage` (15) and `RebelDefectPercentage` (15) have nothing to
act on. A creature that draws it stays angry and says so, once.

**Where a deserter goes.** DK2 sends it to a portal, so a keeper with no portal
keeps a creature that wanted to leave. The wiki's DK2 section describes rebellion
as the *fallback* when the exit is unreachable, where the record plainly holds a
weighted table. Both cannot be right; the record is the stronger source and
reachability is untestable here anyway (Q116).

**The zero floor is ours** (Q115). Nothing in the data states it, and without it
a creature that had just won a combat pit (−16400) would be immune to every
grievance for nearly three minutes.

## ADR-0054 — An object is one type with per-kind state, and a bed knows whose it is

**Status:** Accepted (2026-09-04)
**Confidence:** Strongly inferred (the shape is read out of `DKII.EXE`; the
storage is ours and is explicitly not a fidelity requirement)
**Closes:** Q130. **Opens:** Q131.

### Context

`OBJECTS` was parsed the sitting before this one: 145 definitions, twelve of them
beds, the Chicken at 9, gold on the floor at three of them. That unblocked
eating, gold piles and beds *in data terms* and immediately exposed that there
was nowhere to put an **instance**. `WorldState` had tiles and creatures and no
third list.

Two shapes were available and the choice was not obvious:

- **one type with per-kind state**, so a bed and a chicken are the same kind of
  thing in different states;
- **a list per kind**, easier to iterate and easier to hash, and it makes "the
  gold pile that is also a bed" unrepresentable.

There was also a conflict to resolve first. `LairRules` already modelled a bed —
as `CreatureState.LairTileIndex`, a tile index on the *creature*. If an object
carried the same relation from the other end, the project would hold **two
copies of one fact**, which is the shape behind three of its longest-lived
defects.

### Decision

**One type, and the object owns the relation.**

`ObjectState` is a single type with a definition id, an owner, a tile and
per-kind fields, added as mechanics need them. `WorldState.Objects` is one list
in placement order. `CreatureState.LairTileIndex` is **removed**; the bed carries
`CreatureId`, and `LairRules` derives everything else from that.

A bed now exists only while it is owned: claiming a lair tile **creates** an
object of the creature's own `LairObjectId`, and releasing the bed **destroys**
it. So occupancy needs no bookkeeping at all — a lair tile is taken exactly when
a bed object stands on it.

### Why

**The binary settled the shape, and it did not have to be asked twice.** DK2's
object dump prints a literal `Union Data:` header and then one of twenty-three
blocks, every one reading from offset **0x14A**; the field after the union sits
at 0x15C, so the union is exactly **18 bytes** of per-kind state. That is one
type with a tag, not a family of types — the original's own words for it.

**The tag is the object's definition id**, branched on with hardcoded literals,
and **twenty-one of those literals were checked against our parse of
`Objects.kwd`.** All twenty-one agree: 9 is the Chicken, 35 the Chicken Coop, 74
the Boulder, 31–34 the four Tomb Stones. This is the strongest evidence the
OBJECTS layout has, and the only piece that comes from outside the file — the
other four checks are internal consistency, and internal consistency cannot tell
you that the bytes mean what you think they mean.

**Five type flags are confirmed from the other side too.** The rest of that
dispatch tests bits rather than ids — `TypeGold` 0x40, `TypeSpecial` 0x04,
`TypeLair` 0x20, `TypeCrate` 0x10, `TypeSpellBook` 0x08 — so five of the seven
bits §3 took on OpenKeeper's authority are now confirmed by the code that acts on
them. Note the precedence, because it is behaviour: **flags beat ids.** The
`Lair` branch is reached by flag alone, which is how one branch serves all twelve
beds, and it is why `ObjectDef.IsLair` is the right question to ask.

**Ownership went to the object for three reasons beyond fidelity.** A bed is a
thing that can be destroyed, and `FlagDestroyWhenDropped` sits next to
`CreatureId` saying so — a creature outliving its bed is real and representable
either way, but a *bed* outliving its creature is a leak the creature-side model
cannot even express, because there is nothing to leak. The bed is the creature's
own: twelve objects serve 46 creatures and each names which is its, so the thing
on the tile is not "a bed" but "a Goblin's bed belonging to Goblin 7". And the
presentation layer needs the object anyway, to draw the right one.

**Ours is flat rather than a union**, and that is not a compromise. The fidelity
requirement is on behaviour, not layout (brief item 5), and the behavioural
content of a union is that at most one kind's state is live — which a flat record
with a documented owner per field gives exactly. What the union's real
consequence costs us is a rule instead of a type: every field says which kind may
write it, every other kind leaves it zero, and **the hash covers all of them
unconditionally**, because a kind-dependent hash would let a stray write hide.

### Consequences

`HashVersion` 16 → 17 and snapshot `FormatVersion` 16 → 17. The pinned hash moved
without any behaviour moving: the fixture's creatures are working imps and an imp
has no bed, so what changed is the composition — one field out of every creature,
a count and an id counter in.

**The migration found a real defect in `GameDatabase.ForLevel`.** It rebuilds the
database from parts and had been silently dropping `Objects` ever since the
parameter was added — the constructor gained it, the call did not. Nothing
noticed for two sittings, because objects were only definitions; the moment a bed
became a thing, a campaign scenario threw for want of object 22 while all 719
tests passed. Tests build their database directly and none of them called it.
Two guards now exist, and the real one sweeps every record collection by
reflection so the next record is covered the day it is added.

**Q131 was what this decision assumed and nobody had watched** — that the bed
object is *created* when a creature claims a tile rather than shipped with the
room. **CONFIRMED by the owner the next day** (2026-09-05): *"yes a lair is bare,
green lair texture on the floor tile, only when a creature enters a lair and
claims a tile by dropping its bed does that tile get furniture on top of it."*

Nothing had to change, which is the useful part: the code already did this, on
the strength of a per-creature bed type being unplaceable before the creature is
known. The confidence moves from STRONGLY INFERRED to CONFIRMED and **this ADR
has no open assumptions left.** The wording adds one detail — the creature
*drops* its bed, so placement is an act the creature performs on arrival — which
is presentation rather than simulation, and is also why
`FlagDestroyWhenDropped` sits beside `CreatureId` in the object's union.

What this unblocks, in order: the **Hatchery** (a chicken is an object with a
pecking timer and a scatter countdown, and the union says so), **gold on the
floor**, and the **graveyard** — which wants a creature-carrying imp task rather
than a record, per ADR-0051's correction.

---

## ADR-0055 — Gold on the floor is an object, and the limit named after it is not a limit

**Status:** Accepted (2026-09-05)
**Confidence:** Strongly inferred (every rule below is read out of `DKII.EXE`;
what is ours is which tile two of the call sites choose, and the owner of a pile)
**Closes:** Q65. **Opens:** Q136, Q137, Q138, Q139, Q140.

### Context

`GoldStore` names four places a keeper's gold can be — a treasury tile, a
dungeon-heart tile, a creature's hands, and a pile on the floor — and has
implemented three of them since it was written. The fourth carried a standing
reason: *"a pile is an OBJECT and the OBJECTS record is not parsed until M6."*

Both halves of that reason expired. OBJECTS was parsed, and ADR-0054 gave
objects somewhere to live. What was left was the rule — and three places in the
simulation were throwing gold away for want of it, each of them reporting the
loss in its own event log rather than hiding it: a sold treasury tile, a
destroyed one, and a creature that died carrying.

### Decision

**A pile of gold is an object of type 1, "Gold", carrying its amount in
`ObjectState.GoldHeld` — the object union's first field.** `FloorGold` has the
rules; `docs/research/gold-on-the-floor.md` has the evidence.

Four choices inside that, each with its reason:

1. **Nothing merges.** Every path implemented here creates a *new* object rather
   than adding to whatever stands on the tile, because that is what the original
   does on every one of them. The single place it merges is an imp choosing a
   slot to put a load down, which is a carrying task and does not exist yet.
2. **Nothing is clamped**, and this is the load-bearing one — see below.
3. **A pile is on nobody's books.** `GoldStore.Total` counts tiles, so a pile is
   outside it. That is the original's own arithmetic: making a floor pile out of
   a treasury tile *subtracts* the amount from the player in the same breath.
4. **Treasury gold stays in the tile.** The original stores it as an object too
   — type 3, one per tile — and ADR-0015's decision is unchanged, because no
   behaviour has been found that separates the two representations. The
   difference is written down rather than acted on.

### Why the limit is not a limit

`MaxGoldPileOutsideTreasury` is **2000**, and this project had assumed for
months that it capped a pile. It does not. Its global has **exactly three
readers in the whole binary**, all three imp code choosing where to put a load
down, and all three make one test: a candidate spot holding a pile **strictly
greater** than the variable is not chosen. Nothing anywhere clamps.

Two of the three paths that make piles routinely exceed it. A treasury tile
holds 3000, and selling it makes a pile of 3000.

**This is the second time in one week that a plausibly named cap turned out to
govern something else.** `MaxFreeRangeChickensPerPlayer` was the first, three
days earlier, and the hedge that saved it — expose the variable, do not enforce
it, say why in the code — is applied here for the same reason: enforcing a
plausible cap is unfalsifiable once it is in, because every test then agrees
with it.

### The creature's drop, reproduced with its bug

A creature destroyed while carrying gold leaves:

    piles = 1 if gold < 1000, 2 if gold < 3000, else 3
    each pile gets gold / piles      <- integer division

**The remainder is destroyed.** 2999 carried leaves two piles of 1499 and one
gold that ceases to exist. Reproduced rather than corrected, per the brief:
a quirk is behaviour, and this one is trivially observable.

### Consequences

- `HashVersion` 17 → 18 and snapshot `FormatVersion` 17 → 18: an object has one
  more field. Nothing in the pinned run drops any gold, so what moved is the
  composition alone — which is the useful shape for a pin, because it proves the
  hash covers the field rather than the value.
- Three event messages stopped claiming that piles are unimplemented, and two
  tests that asserted the gap now assert that it is closed.
- **Q65 is answered**, and its premise was wrong rather than its question hard.
- Five questions open, and every one of them is about a *caller* rather than
  about a pile: who owns one (Q136), whether a portal drops one (Q137), whether
  mining leaves one (Q138), which tile an overflowing refund uses (Q139), and
  whether a blocked imp puts its load down (Q140).
- **What this does not do is give the gold back.** No imp fetches a pile, so a
  keeper who sells a full treasury is still poorer — the loss is now a delay
  with no end rather than a hole in the world. Carrying is the next piece, and
  this is the second mechanic waiting on it.

---

## ADR-0056 — Gold on the floor is the first thing an imp does

**Status:** Accepted (2026-09-05)
**Confidence:** Confirmed (the priority is the owner watching the original; the
mechanism is the mining tail that was already built)
**Closes:** Q141. **Opens:** Q142, Q143, Q144, Q145.

### Context

ADR-0055 made gold on the floor real and stopped there, deliberately: nothing
fetched it, because **where fetching sits in an imp's job priority could not be
guessed**. The order this project has — marked excavation, then claiming, then
reinforcing — is CONFIRMED from the owner watching the original (Q24, ADR-0018),
and fetching was not in it. The binary was searched the same day and does not
offer the answer cheaply: an imp's job selection is part of the
runtime-registered state machine (Q125), not a visible list.

So it went on the observation checklist as the cheapest item on it, and came
back the same day.

### What the owner watched

> *"If there is gold on the ground an imp will first take from that gold pile
> and take it to a treasury, until all of it is gone."*

Reported from a dungeon where imps had mined a **gem** tile, gold was lying all
over the floor, and **nobody was mining it** — the tile still there, still
inexhaustible, still marked.

**That is the strong form of the answer.** Not "an imp will get round to it":
fetching **displaces** the job that had been the top of the order.

### Decision

**Gold on the floor is searched for before any of the three tile-work
priorities.** `CreatureActivity.FetchingGold`; `ImpTaskSystem.TryFetchGold` runs
ahead of the `JobPriority` loop.

- **Above claiming and reinforcing by transitivity**, not by a second
  observation: excavation already outranks both, and this outranks excavation.
- **It is not a `JobPriority` member**, because it is not tile work — there is no
  `TileWorkKind` for "pick that up", nothing is damaged and nothing converts.
  The enum stays what it is and the search order carries the ordering.
- **The pick-up is instant on arrival.** Picking gold up is not work: no health
  delta applies and the balance table has no number for it, so charging it a
  work period would be inventing a duration (Q143).
- **One imp to a pile tile**, which is not a new rule — an imp stands *on* gold
  to pick it up, and `ImpCrowding` already gives a tile a worker stands on a cap
  of exactly one (Q44, CONFIRMED). The stance decides the number.
- **Carrying is unchanged.** `DeliveringGold` is the same mechanic whatever put
  the gold in the hands, so the fetch ends by handing over to it.
- **Oldest pile first** where a tile holds several — a creature's death leaves up
  to three (ADR-0055). Spawn order, reproducible, and marked as ours.

### The correction, and why it is the more valuable half

The owner also corrected **their own earlier report**:

> *"When I marked that gem tile for excavating and it wasn't connected to my
> territory via bridge and tiles around it weren't claimed, imps mined it, but
> they didn't carry any gold to treasury/dungeon heart."*

**Nothing in this project codes that, and it reproduces it anyway.** Both
searches walk — an imp cannot reach a pile it cannot walk to, and
`TryRouteToSomewhereItFits` cannot find a store across water — so a stranded imp
picks the gold up, finds nowhere to put it, and stands there while the pile
grows. That is the same shape as Q35's treasury spillover: arrived at rather
than coded, which is the strongest kind of agreement this project gets.

**But it is not evidence for the rule, only for the outcome.** The tile was
neither connected **nor** claimed, so two conditions fit equally well:
reachability, or an ownership test on the ground the gold is lying on. **Q144**,
and one bridge to an unclaimed tile separates them. Recorded rather than
resolved, because assuming reachability because it is what we happen to
implement is exactly the error this project keeps finding in its own history.

### Consequences

- **Hashes unmoved.** No imp in the pinned run or in any prior scenario ever
  meets floor gold, so nothing in them changes — and the new scenario is not on
  the pinned map.
- Nine new tests, and the load-bearing one asserts an *ordering*: `work_applied`
  must not appear before `gold_picked_up`.
- `imps-fetch-dropped-gold-001` shows the whole loop on a synthetic map — an imp
  digging marked walls at t35, gold on the floor at t40, the wall in hand
  finished at t52, and at t53 the imp **walking past two still-marked walls** to
  reach the pile, all of it home by t69.
- **One nuance is ours and is flagged:** the imp finishes the tile it is already
  working on. Fetching outranks excavation at the moment a job is *chosen*, and
  whether the original interrupts a swing in progress is **Q145** — which the
  owner's observation cannot settle, because a gem tile is inexhaustible and an
  imp re-chooses after every completion either way.
- **Carrying is no longer the largest gap.** What is left of it is the
  creature-carrying task: a corpse to a graveyard, an unconscious body to a
  prison or a bed.

### Amendment, 2026-09-05, the same day: the priority was wrong, and the owner caught it

**ADR-0056 as first written put fetching above marked excavation. That is
wrong.** It stood for a few hours, and what ended it was the owner reading their
own report back and saying they were not sure — then asking for a Ghidra pass
rather than for the change to be kept.

**What the binary says** (`docs/research/imp-task-priority.md`): DK2 keeps a
keeper-wide **task list** whose entries carry a kind, and one function tries the
kinds in a fixed order, returning on the first that takes. **Digging and mining
a marked tile are kinds 1 and 2, the first two tried. Gold on the floor is kind
7, the sixth of nine.** Kinds 1 and 2 are pinned hard: they are raised by the
tile-marking code, which chooses between them on whether the terrain's
`GoldValue` is zero — our own parsed field selecting the task type.

So **Q24 stands exactly as it was**, and the August observation that put marked
excavation at the top of the order was right all along.

**What changed here:**

- fetching moved to **second**, below marked excavation. Only "below excavation"
  is read; above claiming and reinforcing is **ours** (Q146), chosen because
  putting it last would make the mechanic *dead* — an imp can nearly always find
  a wall to reinforce — which is flatly contrary to what the owner watched;
- **an imp will not take gold that is not on its keeper's own ground** (or on
  water). Read out of the same function, and it **answers Q144**;
- the ADR's claim that our reachability accident reproduced the correction is
  withdrawn. It reproduced the *outcome*. The rule was ownership.

### Two things worth keeping from being wrong

**The outcome matched while the rule was wrong.** That is the exact shape of the
three multi-month defects in this project's history, and it happened again inside
a single day — with a test suite that passed, a scenario that looked right, and
an ADR that said CONFIRMED.

**And the deeper reading may make the priority question moot — Q147.** The
original's kind chain is a **callback**, invoked from a scan, and every handler
takes a position and looks around it. So DK2's primary key is *where* and the
kind order breaks ties *within* a position, where this project runs one
whole-map search per priority. Under that shape, an imp standing in spilled gold
takes the gold **because it is underfoot**, not because gold outranks mining —
which explains the owner's observation with no priority claim at all. Not acted
on: it changes how every job is chosen, and it wants the owner's eyes first.

---

## ADR-0057 — The free-imp clock is a deadline, and it was a countdown for three weeks

**Date:** 2026-09-05 · **Status:** Accepted · **Supersedes the Q56 half of
ADR-0021**

### The decision

`PlayerState` carries **`NextFreeImpTick`** — the earliest tick at which this
keeper may be handed a free imp — and `ImpSupplySystem` writes it in **exactly
one place**: immediately after it makes one, as `now + interval`. Nothing else
touches it. A keeper at or above `MinimumImpThreshold` is skipped without their
deadline being cleared, reset, paused or banked.

The consequence, and the whole point: **a keeper who has been at full strength
carries a deadline that is already in the past, so their next loss is replaced on
the very next tick.** Only the second and later replacements are an interval
apart.

### What it replaces

`FreeImpCountdownTicks`, held at zero while a keeper was up to strength and
restarted from a full interval when they fell short. Its doc-comment said the
alternative "is indistinguishable in the steady state and differs only in how
fast the first replacement arrives after a long spell at full strength". Both
clauses were wrong in the same sentence: it is distinguishable, and the case it
differs on is the common one.

### How it was caught, which matters more than what it was

**The owner filmed it.** Thirty seconds sitting at exactly four imps, one imp
picked up and dropped into the dungeon heart, and the replacement walking around
about **1.4 seconds** later rather than five — then five-second gaps for the
rest. A second assistant analysed the recording and produced a numbered list of
questions for the decompiler, of which this was named the most important. It was.

**The binary then said why**, in eight instructions in the keeper's per-frame
update, and gave three more facts for free:

- the deadline is advanced **whether or not the imp is actually placed** — the
  write follows the call with no test of its result, so a keeper with nowhere to
  put one burns the interval rather than retrying every frame;
- a free imp is **genuinely free**: the free flag makes the original skip the
  spell-availability check, the mana check and the deduction;
- **`TimeBeforeFreeImpGeneratedSeconds` is multiplied by variable 19**, the
  223-reader global that no shipped file supplies. Seconds in, engine time out.
  The clearest single use of it found so far, and further weight behind Q132's
  reading that id 19 is the seconds-to-ticks constant. It still does not give the
  value.

### What else the same read settled

**Q54 is now confirmed twice.** Create Imp's price is
`max(1, imps − MinimumImpThreshold + 1) × base`, computed in the original with
the clamp included — which the owner's play could not have shown, because it only
bites below the floor. The creature type counted is read off the power's own
record rather than hardcoded.

### Consequences, including the awkward ones

- `HashVersion` 18 → 19 and snapshot `FormatVersion` 18 → 19.
- **Three tests changed meaning rather than being repaired.** There is no longer
  any such thing as "the window before a keeper's first free imp": a keeper with
  a dungeon heart and no imps is staffed on tick zero. `ManaTests` now runs
  without `ImpSupplySystem` so that heart-income assertions measure one mechanic
  rather than two, and two `ManaUpkeepTests` were re-based on the land a keeper
  *starts* with.
- Two questions opened and neither is a decompilation job: whether an imp in the
  Hand of Evil counts towards the threshold (**Q148**), and whether the ~1.4
  seconds is simulation or the creation animation (**Q149**).

### The rule this is the fourth instance of

A claim nothing checks may already be false. Q56 was recorded as UNKNOWN with
"nothing depends on it" in the consequences column — and the thing depending on it
was shipped behaviour that had been wrong since the day it was written. **What
made it findable was the owner watching the game**, which is the only oracle this
project has for a question the data cannot answer.

## ADR-0058 — An imp chooses in two tiers, and gold is near rather than last

**Date:** 2026-09-05 · **Status:** Accepted · **Supersedes** the gold placement
in ADR-0056; **amends** ADR-0018 (the order stands; what it is *part of* changed).

### The decision

`ImpTaskSystem` chooses a job in two tiers, because that is what the original
does (`docs/research/imp-task-priority.md`):

1. **Tier A — kind first, whole map:** marked excavation, then claiming. One
   search per kind; any marked tile anywhere beats any claimable tile anywhere.
2. **Tier B — position first, only when Tier A is empty:** one nearest-first
   search that asks, at every standing tile, *is there a wall to reinforce from
   here?* and then *is there gold under my feet?* A wall beats gold on the same
   tile; a nearer pile beats a farther wall.

Gold on the floor therefore sits **below claiming** and is **not ordered
against reinforcing at all** — it is decided by distance.

### What it replaces, and why the replaced argument was wrong

ADR-0056 placed gold second, above claiming and reinforcing, as an explicit
choice (Q146) with this argument: *putting gold last would make the mechanic
dead — an imp can nearly always find a wall to reinforce, so gold would never
be collected.* The argument assumed the order was a ladder. It is not. In the
original, reinforcing and gold are both in the positional tier, so a pile is
collected the moment it is nearer than the nearest bare wall — which in a
settled dungeon is nearly always. **The argument's premise ("an imp can nearly
always find a wall") is true and its conclusion is false, because distance is
the key.** That is the kind of reasoning error worth naming: a correct fact
about the world fed into the wrong model of the rule.

### How it was settled

The first binary read (morning) found a nine-step chain and called it the
priority. It also noticed the chain was a *callback taking a position* and
raised Q147 — global or positional? — with a two-minute experiment. **The owner
ran it:** a far wall beats gold underfoot, and a near wall beats gold a few
tiles off. Walls, both times.

That single observation was consistent with "global" and inconsistent with
"positional", and the second read explained why both had been half right: the
nine-step chain is the per-tile callback of a **two-tile scan that runs only
after an imp has been dropped**; the real routine walks a **byte table of
kinds** (`0e 08 09 01 03 0d 0c`) whole-map first, and only then ring-scans with
a **four-step** per-tile chain. Reinforce and gold are in the four, not the
table.

### Consequences

- `ImpTaskSystem.TryTakeJob` is Tier A; `TrySweep` is Tier B and is a single
  pathfinder search with a two-part predicate. `JobPriority.Reinforce` is no
  longer in any ordered list.
- **Three tests changed meaning rather than being repaired**: with bare rock
  beside an imp, the wall now correctly wins over gold two tiles away, so the
  gold-fetching tests pre-reinforce the room's walls to measure fetching alone.
  Four new tests pin the competition itself.
- The floor-gold scenario's timeline changed and its header was rewritten;
  its duration doubled so the second trip is in the log.
- The determinism pin **did not move** — no pinned scenario reaches Tier B with
  gold on the floor.
- **The distance metric is ours** (steps, Q25) where the original uses rings.
  It is the one respect in which Tier B is not the original's search, and it
  is written at the top of `TrySweep`.

### What the same read found and did NOT build

- **Kind 14 — claim the tile you have just dug, before anything else**, once no
  neighbour of it still carries a dig task. It is *first* in the original's
  table. **Built later the same day** (Q154, the commit after this one):
  `TileState.FreshlyDug` stands in for the original's task — set when excavation
  destroys a tile, cleared by the next change of terrain — and
  `JobPriority.ClaimFreshlyDug` is Tier A's first step, with the eight-neighbour
  test on every tagged kind but mining. HashVersion and snapshot FormatVersion
  19 → 20. The rank and the neighbour rule are read; that the task is raised at
  the moment the dig completes is STRONGLY INFERRED from where the raise sits.
  Building it exposed a latent mover defect, which is ADR-0059.
- **Task stealing by distance** — a nearer imp takes a task from a farther one
  in every handler. **Q155**, not built.
- Kinds 8, 9/10, 12, 13 (objects, traps and doors, corpses, carrying creatures)
  are identified and wait on the mechanics they carry.

### The rule this is an instance of

"Its observations are oracle, its inferences are reasoning about our repo"
(Codex Brief 4) cut the other way here: the first *read* was an inference too.
A fixed-order chain is not a priority list until you know who calls it and how
often — and the same day's hedge audit clause applies: the first read's
hypothesis named its own experiment, and the experiment took two minutes.

## ADR-0059 — A diagonal leg is walked surplus-first, and the corner is the only point it touches

**Date:** 2026-09-05 · **Status:** Accepted · **Amends** ADR-0025 (the mover's
half of it).

### The defect

`WallsBlockMovementTests.NoCreatureIsEverStandingOnASolidTile` went red the
moment imps started claiming the tiles they had just dug (Q154): on tick 440 of
a four-imp dig-everything run, imp 4 stood inside the impenetrable gem tile at
(6,5) for five ticks.

Its route was legal — (6,4) to (5,5), a corner-cut ADR-0025 explicitly allows.
What put it in the rock was the **mover**, not the router. A creature stops
wherever it *crossed into* its tile, not at the centre, so this imp began the leg
at x ≈ 6.96. The mover walked a diagonal leg **45° first, then straight**, and
from that start the 45° phase swept through (6,5) for nearly half a tile-width.
The invariant had held for a week by luck of routes.

### The decision

A leg is walked in two phases in the **other order**: the *surplus* of the longer
axis first, straight; then both axes together at 14/10. From any point inside
the starting tile that path runs **exactly through the corner the two tiles
share** and touches no other tile — which is what "a creature passes diagonally
between two solid tiles" (Q28, owner-confirmed) looks like once the creature has
a width and slides along the wall. `LegLength` became octile for the same
reason: a leg from an off-centre start is not square, and charging
`max × 14/10` for it made creatures arrive late for no visible reason.

One guard remains, `KeepOffTheCorner`: if a tick's budget runs out with the
position *exactly* on the shared corner, integer division hands that point to the
tile with the larger coordinates, which between two solid tiles is a solid one.
One position unit — 1/4096 of a tile — toward the next waypoint is inside the
destination. It is the only place the mover ever adjusts a position for a reason
other than speed, and it says so.

### Consequences

- Both wall invariants hold again on the four-imp run, with corner-cutting still
  allowed.
- **The determinism pin did not move for this change** — the fixture has no
  off-centre diagonal leg — and no other test moved either. Where a creature
  *does* start a diagonal leg off-centre, its position over the next few ticks
  differs from before by up to half a tile-width in one axis; arrival times are
  unchanged to within the octile rounding.
- Routing remains ours (Q5). What this fixes is a property of *our* mover that
  no observation of the original bears on, other than the one ADR-0025 already
  cites.

## ADR-0060 — A bed is walked to: the room by distance, the tile on arrival

**Date:** 2026-09-05 · **Status:** Accepted · **Extends** ADR-0049 (a creature
has its own bed) and ADR-0054 (the bed is an object that knows whose it is).

### The defect

`CreatureSleepSystem` gave a creature a bed on the first tick one was free,
from wherever it stood, with no walk — the lowest free tile index across the
keeper's lairs. The owner filmed the original doing something else entirely
(`docs/research/lair-acquisition.md` §1): creatures walked into a lair, up to
an occupied tile, rejected it and carried on; in a 1×5 lair the tiles went
1, 3, 5, 4. The binary was then read to the tile (§4, §5) and a third party's
implementation plan checked against it (§7). This ADR is the build.

### What the original does (STRONGLY INFERRED from the binary; outcome CONFIRMED by recordings)

1. **The trigger is idleness without a bed**, whatever the mood (Q150), and the
   sleep need every `TimeAwake` is a second way in. The attempt is gated on
   `LairObjectId ≠ 0` **and `TimeSleep ≠ 0`** (Q104).
2. **The room is chosen first**, by distance from the creature to the room's
   centre: the nearest reachable lair with a free slot, preferring one that
   holds no bed whose owner is of the other alignment (Q151). Capacity is the
   tile count (Q156). Nothing about size or "function".
3. **The tile is chosen on arrival**: the tile underfoot, then eight neighbours
   in the order (−1,−1), (−1,0), (−1,+1), (0,+1), (+1,+1), (+1,0), (+1,−1),
   (0,−1), then a walk of the room from a random start; each candidate must be
   the keeper's lair tile with no bed of any kind on it. Nothing is reserved.
4. **The bed is created at the tile's centre**, and the creature then sleeps at
   once only if its health is below `CreatureSleepsWhenBelowPercentHealth`
   (variable 88, 50); otherwise the state ends and it is idle.
5. A room found full on arrival sends the creature back through the finder;
   a failed attempt falls through and is retried at the next idle decision.

### The decision

- A new activity, `CreatureActivity.SeekingLair` (9), distinct from
  `GoingToSleep`: the latter walks to a bed the creature owns, the former walks
  to a room and decides the tile on arrival.
- `LairRules.NeedsBed` tests both fields. The Dwarf and the Stone Knight, who
  carry the hero bed with `TimeSleep` 0, are never sent for a bed.
- The finder, the on-arrival search (`LairRules.TryFindFreeTile`, with the
  neighbour table read from the binary) and the predicate
  (`LairRules.IsFreeLairTile`) are built as read. The room preference reads the
  `IsGood` / `IsEvil` pair both ways (`LairRules.HoldsBedOfOtherAlignment`).
- Reach is a real route over the creature's own `WorldPathGrid`, planned in the
  attempt and then walked by `CreatureMovementSystem`. A route that fails
  mid-walk, a destination sold or captured mid-walk, and a room found full all
  end the state cleanly in idle, with an event.
- The remote claim is gone. `LairRules.TryClaimBed` is renamed
  `TryPlaceBedInAnyLair` and documented as placement without a journey, for
  tests and level setup; no system calls it.
- Moving house (a creature finding a tile while owning a bed in another room
  destroys the old one) is kept as a guarded branch with an event; no path in
  this simulation reaches it, because a bed here is either valid or already
  released.

### What is ours, marked as such in code

- **The idle retry cadence**, `LairRules.IdleRedecisionTicks` = 12: the
  original's idle state wanders 12 of its ticks and decides again; whether its
  tick is ours is not established. A failure event is emitted at every
  attempt, so a bedless creature in a full dungeon is loud in the log — 530
  lines in the 6700-tick anger scenario. That is the truth of what it does.
- **The arrival tile** (Q157): the room tile nearest the bounding-box centre,
  lowest index among equals (`LairRules.ArrivalTile`). The original's
  travel-to-room routine is unread.
- **The order of the room walk**: ascending tile index rather than the room's
  own join order, which is not observable without the room's history. The
  random start is kept.
- **No sub-tile jitter** on the walk target. The original displaces it by
  about a third of a tile; our mover walks to tile centres and the bed sits at
  the centre either way.
- **The unhappy and angry job choosers' lair-first line** needs no code: with
  no job system for non-workers, every bedless non-worker is idle, and idle is
  where the attempt already is.

### Consequences

- **No new persistent field**, so `HashVersion` and the snapshot
  `FormatVersion` stay at 20 and the determinism pin did not move (the fixture
  run is imps only). The new enum value is hashed as an int like the others.
- **A random draw is consumed by each on-arrival room walk** that gets past the
  neighbour ring. The anger scenario's third goblin does exactly that once, so
  its angry draw moved from `Rebel` to `Leave` on the same seed — and with no
  portal on that map it is blocked. Both halves of the coin end the same way
  there; the scenario's notes say so.
- Tests that painted a lair behind rock and expected a bed now paint it a walk
  away and run the mover; the anger arithmetic test measures one sleep's worth
  from the anger at the moment the bed went down, since the walk costs the
  seconds it takes. `LairAcquisitionTests` (18) covers the checklist the
  verified plan set out: no remote claim; unreachable rejected; nearest
  non-full chosen; alignment preference and its fallback; occupancy on
  arrival; the ring order and the random-start walk; two creatures one room;
  full on arrival then retry; destination sold mid-walk; bed loss by the same
  journey; sleep only if hurt; the two-field gate; snapshot mid-journey; same
  seed same beds.
- Suite: 829 tests; 16 scenarios run.

### Still open

- Q157 (arrival tile) and the cadence are readable and unread.
- The owner can break the prediction in §5 of the note: with a large empty
  reachable lair before the portal fires, every creature beds down before its
  first job.

## ADR-0061 — The angry job is rolled again, and a lair is tried before it

**Date:** 2026-09-05
**Status:** Accepted
**Corrects:** ADR-0053 (whose 2026-09-05 amendment scheduled this)
**Extends:** ADR-0060 (the lair attempt is now shared code)

### The defect

ADR-0053 gave an angry creature **one** draw from its `AngryJobs` table and kept
it for the creature's life. The reasoning was honest and is quoted in the file:
half a Goblin's table is `Rebel`, which is unbuilt, and a creature re-rolling
every second would eventually draw `Leave` by luck — reporting an unimplemented
outcome as the implemented one.

Q122 read the original's chooser (`FUN_004784c0`) on 2026-09-05 and the shape is
different. **Nothing is kept but a timestamp.** The creature rolls afresh on
every allowed selection; a selection is allowed five game seconds after the last
one that came to something; and the field that gates it is the one EA's own
debug dump calls `LastGameTickSuccessfullyChoseAngryJob`. The concern ADR-0053
raised turns out to describe DK2 rather than argue against it: with `Leave(50%)`
re-rolled, an angry Goblin *does* walk out, after a round or two.

### What the original does

Read from the binary, STRONGLY INFERRED, and written out step by step in
`docs/research/creature-anger.md` ("The chooser, read from the binary"):

1. a five-game-second gate against that timestamp, checked **first**;
2. **a lair attempt before any roll** — the same `FUN_0046f390` the idle state
   makes (`lair-acquisition.md` §5, §8). A creature that sets off for a lair has
   spent its selection and does not roll;
3. cumulative ranges over the table in **record order** — the sort the original
   applies has a comparator that never returns negative and so cannot reorder an
   already-ascending array;
4. `rand(100)`, which is `% 100` and therefore **0..99**;
5. the first entry whose range holds the roll, tested **inclusively at both
   ends**, dispatched;
6. **fall-through in record order** when that started nothing;
7. the timestamp stamped **only on success**.

### The decision

Build 1-7, with one deliberate stop. `CreatureState.AngryChoice` is gone and
`CreatureState.LastAngryChoiceTick` replaces it; the finder that chooses a lair
and sets off for it moves out of `CreatureSleepSystem` into `LairSeeker`, so the
idle path and the angry path make one attempt rather than two similar ones —
which is what the original does with one function.

**The stop: an unbuilt job ends the selection.** The original's fall-through
exists for jobs whose own start routine *fails* (no training room, no shrine).
Ours cannot fail that way, because every angry job either starts (`Leave`) or
does not exist (everything else). Falling through an unbuilt `Rebel` to a built
`Leave` would turn half of every angry Goblin into a deserter DK2 would not have
produced, so **a job this simulation has not built ends the selection where DK2
would have succeeded**: the creature says so, stands there, and rolls again when
its gate opens. ADR-0053's principle survives its rule.

The fall-through itself is built and reachable — a table whose chances sum to
less than 100 leaves rolls that match nothing, and 99 of 100 rolls miss a 1%
table.

### What is ours rather than the original's

- **Five game seconds is five game seconds.** The original computes
  `5 x` variable 19 and that variable is 4; ours is
  `5 x SimulationTiming.TicksPerSecond` and that is 20. The conflict is Q1's,
  recorded there, and not resolved by fiat here.
- **The selection is offered once a game second**, because that is when the
  anger ladder runs. The original offers it at every "think", whose cadence is
  not established.
- **`Leave` still releases the bed at the moment it starts** (Q121), and a
  creature already leaving is carried rather than re-choosing — the original
  gets the second of those from its state machine, which we do not have.

### Consequences

- An angry Goblin now leaves. Half of them used to stand in the dungeon for
  ever, on a draw that stuck.
- A Vampire never leaves, because its table is `Rebel(100%)` — the unbuilt
  outcome is its only outcome, and it must stay that way.
- `HashVersion` and the snapshot `FormatVersion` go 20 -> 21: the persistent
  field changed shape.
- The lair attempt now appears in the log under `[CreatureAnger]` as well as
  `[CreatureSleep]`. The goblin scenario has exactly one of them, at the second
  the goblin turns angry.
- 23 tests in `CreatureAngerTests`, including the two arithmetic quirks (the
  overlapping ranges, the fall-through) and the gate's cadence.

### Still open

- **What the two flags at the top of the chooser are** (a per-creature bit, and
  the definition flag `0x4000` that diverts a creature into a state instead of a
  choice). Neither is applied. **`0x4000` was identified the next day as
  `IsHorny`** (Q158, ADR-0062): the slap handler tests the same bit first and
  diverts the same way, the enum had carried the name at bit 14 since
  `creatures_struct.bt` was transcribed, and across the 46 shipped creatures it
  is set on Horny alone. So both halves had been in the repository all along and
  nothing had put them together. The per-creature bit is still unidentified.
- **The unhappy chooser**, which has no jobs to run here yet, and carries a quirk
  worth keeping when it does: on a successful lair attempt it stamps the *angry*
  chooser's timestamp rather than its own.

## ADR-0062 — The Hand of Evil, and Efficiency becomes a number a keeper can move

**Status:** Accepted (2026-09-05). Builds the mechanic ADR-0020's 2026-09-05
amendment said Efficiency was waiting on. Research:
`docs/research/hand-of-evil.md`.

### Context

Efficiency was named the day before this and left as a constant, because the two
things that move it are both the keeper's hand: a slap, and dropping a worker
onto work. Neither existed. The hand was also the largest thing four other
half-built mechanics were waiting on, and the balance table has been carrying
`AngerSlap`, `AngerInHand`, `SlapDamage` and `MaxNumberOfThingsInHand` parsed
and unread since the day they were parsed.

### Decision

Three player commands — `PickUpCreatureCommand`, `DropCommand`,
`SlapCreatureCommand` — one `HandSystem` that runs **before every system that
makes a creature act**, and `EfficiencyRules` feeding every work application.

**What is read from the original**, in its own words (§ numbers are the research
document):

- **The hand is an array on the keeper**, capacity `MaxNumberOfThingsInHand`
  = 64, and **dropping is last in, first out** — the drop handler reads back the
  slot it is about to free (§2).
- **The drop takes a position and no id.** Naming a creature would have hidden
  the ordering, which is behaviour.
- **`PickupTick` is written on the drop as well as the pick-up** (§2), which is
  what makes the mood grace period work.
- **`AngerInHand` is a rate**: once a game second, from **20 game seconds** after
  the pick-up (§7). So moving a creature costs nothing, and parking one in the
  hand *calms* it — a Goblin's is −250.
- **The slap sets `WorkFasterTimer` unconditionally**, so slaps do not stack (§4)
  — which is exactly what the owner measured.
- **Efficiency is two independent ten-point bonuses on an 80% base** (§6), which
  is why 100% is reachable and 110% is not.

**What was corrected mid-build, and it is the entry worth keeping.** The first
draft read the original's `now < LastSlapTick + var19` test as a one-second
cooldown on the whole slap. Its test failed, the handler was re-read, and that
window guards **only the knock-back impulse**: the anger, the damage and the
raised Efficiency all land regardless. **There is no slap cooldown.** The six
countable slaps that pinned the anger ceiling (Q114) were six because a human
hand is slow, not because the game rationed them.

### Efficiency in percents, not in 4096ths

DK2 multiplies in 4096 fixed point and its constants are approximations:
`0xccc` is 0.7998 and `0xccc + 0x199` is 0.8997. Run the owner's seam through
them and 90% gives **67.47** health a swing, which rounds to 67 by every rule
there is — and 67 would have shown **304** gold on the counter. It showed
**309**, which is 68.

So the **displayed percentages are the exact quantity** and the fixed-point
constants are the game's approximation of them. `EfficiencyRules` works in whole
percents with halves rounded away from zero, reproducing all three measured
numbers exactly. ADR-0002: behaviour, not implementation.

`EfficiencyRules.Scale` returns its input unchanged at 80%, which is the whole
reason 529 existing tests and every scenario hash survived: **the 80% case is
what this project already had right**, and `delta / 5` is `delta x 0.8 / 4`.

### What is ours rather than DK2's, stated

- **`HandSystem` runs first.** The original's system order is UNKNOWN (the
  standing caveat on `ISimulationSystem`). This is the order that makes a hand
  feel like a hand: a creature picked up this tick does not also take a step,
  and one dropped onto work starts on it at once.
- **Only the keeper's own creatures may be handled.** DK2 refuses an enemy
  creature under one condition and this refuses it under all — a narrowing.
- **A collapsed creature cannot be picked up** (Q161). DK2 allows it, and it is
  half of what the dying window is for; ours cannot, because carrying one to a
  bed or a prison is the next mechanic.
- **The aiming test is the tile underfoot and four neighbours**, where the
  original scans a two-tile radius (Q162).
- **A held creature keeps climbing the anger ladder but decides nothing.** It
  rolls when it lands.

### Consequences

- `HashVersion` and the snapshot `FormatVersion` go 21 -> 22: `CreatureState`
  gained five fields and `PlayerState` gained the hand.
- **`Dk2TerrainFixture` had the Imp's `SlapDamage` transcribed as 0.** It is
  **100** against 500 health, so five slaps kill an imp — a piece of DK2 anyone
  who has played it has done by accident, and the fixture had been quietly
  denying it since the field was parsed. Corrected, and now guarded by
  `CreatureNeedsOnRealDataTests` against the install.
- **The fixture's Goblin was missing `CanBePickedUp` and `CanBeSlapped`.** Both
  are on the real record. Without them every hand test written on a Goblin
  silently tested the refusal path instead, which is what two of them did until
  they were fixed.
- The scenario harness gained `pick_up`, `slap` and `drop` commands, and
  `the_keeper_picks_up_an_imp_and_slaps_it` uses `imps_dig_what_is_marked` as
  its control: same map, same seed, same imps, same marks, and the corridor
  falls at t62 instead of t67.
- 27 tests in `HandTests`, two of which assert numbers the owner read off the
  running game.

### Still open

- **How long the slap's bonus really lasts** — 25 seconds from the binary
  against the owner's *"after a minute"* and an
  `IncreasedWorkRateDurationFromSlappingSeconds` of 60 (Q159). Now a cheap test:
  the status bar prints the percentage.
- **What `TimeInHandBeforeCreaturesBecomeAngrySeconds` = 1000 is for** (Q160),
  since the grace period is hardcoded at 20.
- **The third Efficiency term**, 0.05 per unit of a counter on the keeper —
  probably the torture tally, and unverifiable without a Torture Chamber.
- **Everything else the hand holds**: gold, chickens, and an unconscious body on
  its way to a lair. That is carrying, and it is next.

## ADR-0063 — Carrying: an imp picks a body off the floor

**Status:** Accepted (2026-09-05). Completes the dying pipeline ADR-0051 could
only build the first half of, and pays the debt `objects.md` §4 identified.
Research: `docs/research/creature-death.md`, `docs/research/imp-task-priority.md`
§4.

### Context

The owner described dying in one breath (Q99) and it is three states with two
sixty-second timers:

```
  health -> 0
     |
  COLLAPSED   CreatureDyingStateDurationSeconds = 60   an imp -> its own bed
     |
  CORPSE      DeadBodyDiesAfterSeconds          = 60   an imp -> a graveyard
     |
  gone
```

Only the first state existed. Both numbers had been parsed for months with
nothing able to read either, and the named reason — repeated in ADR-0051, in
`creature-death.md` and in several code comments — was **"a corpse is an
object"**. When somebody finally read all 145 OBJECTS records, none of them was
a corpse. **A corpse is a creature in a state**, and what was missing was never
a record: it was an imp task that carries a creature.

That is the fourth time a claim nothing checked has cost this project a
mechanic.

### Decision

`CarryRules`, two creature activities (`FetchingBody`, `CarryingBody`), one
corpse state (`Dead`), and the last two entries of DK2's own Tier A table.

**Read from the original** (`imp-task-priority.md` §4): the imp job table's Tier
A is `14, 8, 9, 1, 3, 13, 12` — whole-map, first that takes — and the last two
are **kind 13**, carry an unconscious creature (own to a Lair, an enemy's to a
Prison), and **kind 12**, carry a corpse to the Graveyard. So carrying sits
**below claiming** and above everything found by distance. An imp buries nobody
while there is unclaimed ground, which is not an obvious ordering and is visible
in `an_imp_buries_a_corpse`: the corpses are claimable at t1219 and the imp does
not come for one until t1311.

**What happens at each end:**

- **A collapsed creature goes to its own bed**, not to any free lair tile — the
  bed it kept through the whole dying window precisely so this walk would have a
  destination. Arrival calls `CreatureDeathRules.Rescue`, which was written and
  tested five sittings ago with no caller.
- **A corpse goes to the nearest Graveyard tile**, and is consumed.
- **The destination is settled before the walk starts.** A body with nowhere to
  go is a body nobody sets off for, which is what stops an imp crossing the
  dungeon to stand over one and shrug.

**Two clocks in one field.** `DyingTicks` counts the rescue window while
`Dying` and the corpse window while `Dead`, because they never run at once, and
it is reset at the seam so a corpse gets a fresh sixty seconds rather than a
spent clock.

**Both ends of the relation are stored** — `CarriedCreatureId` on the carrier
and `CarriedByCreatureId` on the body — because both are asked: the carrier
needs to know what it is holding on arrival, and the body needs to know it is
held so that a second imp does not walk across the dungeon for it. A third
field, `TargetBodyId`, is the body a worker is walking *towards*, which is a
different thing that can fail in different ways.

### What is ours rather than DK2's, stated

- **No prison** (Q163). DK2's kind 13 sends an enemy's unconscious creature to
  room 11 and there is no prison here, so an enemy on the floor is left there
  rather than quietly rescued into the wrong dungeon.
- **The corpse clock does not pause in an imp's arms** (Q164), so a long walk to
  a distant graveyard can outlast the body. UNKNOWN in the original; the
  alternative is a corpse that keeps for ever as long as somebody is walking
  with it.
- **A graveyard keeps no tally** (Q165). It consumes the corpse and the event
  reports `DecomposeValue`. Its `CreatedCreatureId` is 8 — the Vampire — and
  building that now would be premature for the reasons `creature-death.md` sets
  out.
- **Nearest by walking**, as everywhere else here (Q25), where the original
  scans in straight-line rings.

### Consequences

- **A creature that leaves a body no longer vanishes at death.** Four tests
  asserted that it did, and every one of them was encoding the truncated model
  rather than the game; they now assert the corpse.
- `HashVersion` and the snapshot `FormatVersion` go 22 -> 23.
- `CreatureDeathRules.BodyWindowTicks` has a caller for the first time. It was
  put there five sittings ago specifically so the missing half would be missing
  *visibly*, and that worked.
- The fixture gained the Graveyard — terrain 23, room 14, 2000 to build — which
  it had never needed.
- Three new animation keys, because `WorldView.ActivityKeySegment` is a switch
  and adding an activity without deciding its animation does not compile.
- 26 tests in `CarryingTests`; `an_imp_buries_a_corpse` runs the second half end
  to end, and `a_goblin_collapses_and_dies` now runs to t2419 and shows the
  corpse rotting where this project used to stop.

### Still open

- **The prison, and capture** — the other branch of kind 13.
- **Kind 8**, carry a loose object to its room, which is the third carrying task
  and needs object instances that go somewhere.
- **What a graveyard does when it fills**, which is where vampires come from.

## ADR-0064 — The Prison: capture, and the half of carrying that was missing

**Status:** Accepted (2026-09-05). Completes DK2's imp task kind 13, which
ADR-0063 could only build one branch of. Research: `docs/research/prison.md`,
`docs/research/imp-task-priority.md` §4, `manual-1999.md` §Prison.

### Context

ADR-0063 built carrying and stopped at a deliberate line: an imp carried its
keeper's own unconscious creatures to a bed and corpses to a graveyard, and left
an enemy lying where it fell, because DK2's kind 13 sends that one to a **Prison
(room 11)** and there was none. Q163 recorded it as a stop rather than a gap.

The owner then corrected the whole verb (2026-09-05):

> *"a corpse can not be picked up by hand of evil and dropped to a graveyard,
> only an imp can carry it to its grave, also an injured creature, lying on the
> floor bleeding out, can't be picked up by hand of evil and dropped to its lair,
> only an imp can carry it to its bed to recover, likewise only an imp can drag
> an enemy creature to prison, a hand of evil can then pick up that creature from
> prison and drop it around the dungeon, back in prison, or onto a torture rack
> in the torture chamber."*

**Two rules, and the first of them contradicted ADR-0062.** That decision had
recorded *"DK2 lets the hand take a collapsed creature"* and refused one anyway,
calling the refusal a narrowing invented to excuse a missing mechanic. It was
DK2's rule all along: the pick-up handler refuses bit `0x200000` of the first
state-flag word, and DK2's own creature dump names that bit **`CreatureDying`**.
The bit had been read and not named. Fourth instance of a claim nothing checked.

### Decision

`PrisonRules`, `PrisonSystem`, `CreatureActivity.Imprisoned`,
`CreatureState.PrisonOwnerPlayerId`, and the enemy branch of
`CarryRules.TryDestinationFor`.

**Capture is an imp's job, and only an imp's.** `HandRules.CanPickUp` refuses
anything that is down, full stop — no corpse, no dying creature, its keeper's own
or anybody's. The hand's entire role in this mechanic is moving a prisoner that
an imp has already taken.

**`PrisonOwner` is a second owner, not a change of owner.** DK2 keeps it as its
own creature field at offset `0x3cb` and this keeps it as
`PrisonOwnerPlayerId`. A prisoner is still its keeper's creature; it is merely
somebody else's problem to feed. That is what makes the hand's one ownership
exception a field test rather than a search of every prison, and it is what
survives a creature being lifted out of a cell — so dropping it back in a prison
re-imprisons it and dropping it anywhere else frees it.

**The cell is chosen before the walk starts**, as a bed and a graveyard tile
already are. Full prison, or no prison, means no imp sets off — which is the
manual's *"imps stop collecting prisoners"* falling out of a rule that was
already there rather than being written a second time.

**One system settles a prisoner's second.** `CreatureHealthSystem` skips
prisoners entirely and `PrisonSystem` runs immediately after it, on the same
cadence and phase, doing three things in order: free anybody whose cell is no
longer a cell, roll an overcrowded prison's jail break, then wither everybody
left by `PrisonModifyCreatureHealthPerSecond` (-3). A prisoner that runs out is
replaced where it stands by the Prison's `CreatedCreatureId` — **9, the
Skeleton** — belonging to the **jailer**. No collapse, no rescue window, no
corpse: dying in a prison is not the ordinary death, and that is what the room is
for.

**Selling a prison and losing one are the same rule here.** `PrisonSystem` asks
each second whether a prisoner still stands on a tile of a prison belonging to
the keeper who imprisoned it. That covers the sale, the tile being dug away, a
rival claiming it, and the room being destroyed, without hooking any of those
paths — the same choice `RoomInstance` makes, because derived state cannot
drift.

### What is ours rather than DK2's, stated

- **One prisoner per tile** (Q166), and the weakest inference here. DK2 *stores*
  `TotalCapacity`/`UsedCapacity` on the room rather than deriving them, and how
  it derives them has not been read. The owner can settle it by counting.
- ~~**A captured creature wakes at full health** (Q167).~~ **Amended the same
  evening, by the owner, unprompted:** *"its flower gets two petals, and then
  that creature starts loosing health at some standard rate"*. A prisoner wakes
  at `CreatureCriticalHealthPercentageOfMax` — **20%**, two petals of ten — so a
  Goblin arrives at 300 of 1500 and becomes a Skeleton in **100** game seconds
  rather than 500. `CreatureHealthRules.CriticalHealth` is the new caller of a
  variable that had been read by nothing.

  **This is what the guess cost, stated plainly:** the ADR named the right
  experiment, the scenario shipped the wrong number for an afternoon, and the
  mechanic was five times too slow — a prison you could forget about rather than
  one you have to answer. The sentence also settled **Q108** (the flower is a
  health readout, not the anger meter) and opened **Q172** (a prisoner walks
  about its cell and races the other prisoners to a dropped chicken, none of
  which is built), neither of which had been asked.
- **The jail break is rolled once a game second** (Q168). The variable names a
  percentage per extra creature and says nothing about how often.
- **Freed where they stand.** DK2's `prison.cpp` releases at the room's
  `DoorTileX`/`DoorTileY`; there is no door here.
- **No keeper-set door bar**, though `SafeCreatureCapacity` is a real field. So
  overcrowding is only ever a keeper dropping a prisoner into a full prison.
- **A prisoner is not slappable**, and nothing observed says either way.

### Consequences

- **The hand's refusal of a body is now a rule with a reason**, and two tests
  assert it directly. Q161 is answered against what this project believed.
- `HashVersion` and the snapshot `FormatVersion` go 23 -> 24.
- The fixture gained the **Prison** (terrain 20, room 11, 750 to build) and the
  **Skeleton** (creature 9) — the creature with no needs at all, which makes it
  the control against the Goblin, which has every need there is. `RoomDef.R()`
  now transcribes `CreatedCreatureId`, so the Graveyard's Vampire is in the
  fixture too even though nothing makes one.
- `PrisonSystem` reads its balance variable inside the prisoner loop rather than
  once per tick, so a level with no prison does not demand a variable only a
  prison needs. The synthetic databases the harness runs on carry exactly what a
  registered system reads, and the first draft broke every one of them.
- 20 tests in `PrisonTests`, one real-data test pinning the two variables and
  the Skeleton against the shipped files, and
  `an_imp_drags_an_enemy_to_prison` runs the whole thing: captured at t1209 with
  ten ticks of the rescue window left, a Skeleton at t11199.

### Still open

- **Torture and conversion**, which the prison is the door to and which have had
  per-creature fields parsed and unread for months.
- **Chickens**, which are how a keeper keeps a prisoner alive. A chicken is an
  object and nothing can pick one up — kind 8.
- **Combat.** Everything here begins after a fight, and there is no fight.

## ADR-0065 — Kind 8: an imp puts a book back on the shelf

**Status:** Accepted (2026-09-05). The third and last of DK2's carrying tasks,
after ADR-0063 (kinds 13 and 12) and ADR-0064 (kind 13's other branch).
Research: `docs/research/objects.md`, `docs/research/imp-task-priority.md` §4.

### Context

DK2's imp Tier A job table is `14, 8, 9, 1, 3, 13, 12`. Five of the seven were
built; the two that were not are **kind 8**, carry a loose object to its room,
and **kind 9/10**, fetch a crate for a placed trap or door. Kind 8 was recorded
as *"nothing to act on"* — which was true and was also, on this project's own
record, the exact shape of a claim worth checking. The check: an object needs a
room to go to, a room needs a list of its objects, and `Rooms.kwd` has carried
that list, parsed, since Q120. Nothing was missing but the task.

### Decision

`ObjectCarryRules`, two activities (`FetchingObject`, `CarryingObject`), and one
new entry in the Tier A order.

**It goes second, above marked excavation and above claiming.** That is the
original's ordering and it is not the obvious one: a keeper who marks a wall and
knocks a book onto the floor gets the book put away first. There is a test whose
only job is to fail if somebody quietly moves it down the list.

**The carry slot is not the drag slot.** DK2's creature dump names `CarryingId`
at offset `0x3c3` and `DraggingId` at `0x3c5` — two fields side by side, an
object *carried* and a creature *dragged* — so this has two fields too, and an
imp cannot hold a book and a body at once.

**The room→object mapping is read backwards.** Each room record lists its
objects (Library 64 and 58, Workshop 16/15/46, Prison 49/50/51, Graveyard
31–34), and `HomeRoomFor` walks the rooms to find the one that lists a given
object. The original goes the other way, from the object's own definition, so
there is presumably a room field in OBJECTS this project has not identified —
the second time it has had to read one of DK2's pairs from the wrong end (the
first was a bed's owner, Q130).

**Delivery makes the object the keeper's.** An object owned by nobody is what
"loose" means, so giving it an owner on arrival is what takes it out of the
search — rather than a "shelved" flag, which would be a second copy of a fact
the ownership field already holds.

**A worker that stops being a worker puts its load down where it stands.** Picked
up by the keeper, or killed: the object goes on the floor, still loose, and the
next imp takes it to the same room. **That fixed a latent bug from ADR-0063 as
well** — an imp lifted while dragging a body kept hold of it, and because a held
creature is skipped by every system the body stopped following and stopped being
anybody's problem.

### What is ours rather than DK2's, stated

- **One object per tile** (Q166's cousin). DK2 stores `UsedObjectCapacity` on
  the room and every object definition carries a `RoomCapacity` of its own, so
  the original's accounting is finer than this and has not been read.
- **The first room in table order wins an ambiguous object** (Q169). Objects 49
  and 50 are listed by both the Prison and the Combat Pit.
- **Own floor only, not "own floor or water"** (Q170). There is no reason here
  for a worker to wade for a book, and the narrower half is the safe one.

### Consequences

- `HashVersion` and the snapshot `FormatVersion` go 24 -> 25.
- The fixture gained the **Library** (terrain 15, room 6) and object **64, the
  Book Case** — and `R()` now transcribes `ObjectIds`, which nothing had needed
  before.
- 12 tests in `ObjectCarryingTests`.
- Kind 8 has something to act on the moment anything drops a loose object,
  which is what the Hatchery's chickens will be.

### Still open

- **Kind 9/10**, the last unbuilt Tier A entry: fetch a crate from the Workshop
  for a placed trap or door. Needs traps, doors and manufacturing.
- **Tier B's loose-object question.** The original's ring scan asks about loose
  objects too, as its third question after reinforce-or-repair and floor gold.
  This builds the whole-map Tier A kind only.

## ADR-0066 — The Hatchery lays, and the blocker had already been removed

**Status:** Accepted (2026-09-06). Research: `docs/research/hatchery.md` §4,
Q132, Q134. Scenario: `a_hatchery_lays_and_hatches`.

### Context

`HatcheryRules` was written four sittings ago and could run none of itself. It
knew how many coops a room of a given size gets (`floor(tiles / 6)`, capped at
36), where they may stand (never two within a squared distance of 3), that the
room lays an **Egg** (object 47) at a randomly chosen coop, that the egg becomes
a **Chicken** (object 9) after two seconds, and that a room holds one bird per
tile. What it did not have was the clock, and the reason was written into the
class, into `hatchery.md`, and into the harness's own caveat list:

> DK2 multiplies its laying interval by a balance variable **that no shipped
> file supplies** (Q132).

**Q132 was answered on 2026-09-05** — the multiplier is variable 19, ticks per
game second, the **level header** supplies it rather than any variables file,
and all 66 shipped levels carry 4. The blocker had been gone for a day while
three documents still named it, and the answer was written in this project's own
question list.

**Fifth instance of a claim nothing checks**, and a new variant: not a stated
rule, not a stated blocker, but a **stated blocker that had already been
removed**. Nothing re-reads a "blocked on Qnnn" note when Qnnn is answered.

### Decision

`HatcheryRules.LayIntervalSeconds`, `HatcherySystem`, and two fields of state.

**The interval, read from the original:**

```
  interval = (int)( ChickenGenerationTimePerHatchery / pow(tiles, 0.315477776) ) * V19
```

Returned in **whole game seconds**, with the caller converting to ticks — the
truncation happens before the multiply, and returning ticks would bake the
still-placeholder `TicksPerSecond` into the arithmetic (Q1).

**A fixed-point table rather than `Math.Pow`.** This number decides when an egg
appears and eggs are hashed; IEEE-754 `pow` is not guaranteed bit-identical
across runtimes, and the truncation sits on a knife edge. `HatcheryClockTests`
regenerates all 256 entries from `Math.Pow` and compares, so the table is
provably the formula while the simulation never touches a float.

**The 3x3 quirk is reproduced.** `pow(9, e)` is **2.0000566**, a hair above two,
so `16 / 2.0000566` is 7.99977 and the truncation takes **7**. A designer
deciding *"nine tiles should be exactly half of one"* would have got 8 — the
exponent is typed rather than derived and was typed just high enough to miss.
`hatchery.md`'s own table said 8 while its own header said "truncated"; it says 7
now. One second off every 3x3 hatchery in the game, from the sixth decimal place.

**Two fields of state, because a room here cannot hold any.** Rooms are derived
(`RoomInstance`), and DK2 keeps `GameTickLastGenerated` on the room object. So
the laying clock goes on the room's **lowest tile index**
(`TileState.HatchTicks`, following `EntranceIdleTicks`), and each egg's own
countdown goes on the object (`ObjectState.TimerTicks`, where DK2 keeps it in
the object's union). **Stated consequence:** a hatchery that grows or is cut so
its lowest tile changes restarts its clock, costing at most one interval.

**Hatching runs before laying**, so an egg is not aged on the second it is laid.
The other order gives a two-second egg a one-second life, which is what the first
draft did and what a test caught.

### Consequences

- **Kind 8 has something to carry.** ADR-0065 shipped the loose-object task with
  nothing in the world to act on; a chicken is an object, and this is the first
  thing in the project that puts one on the floor without a keeper or a creature
  doing it.
- `HashVersion` and the snapshot `FormatVersion` go 25 -> 26.
- 8 tests in `HatcheryClockTests`; the scenario runs a 3x3 from empty to full and
  then shows it refusing, twice.

### Still open

- **Nothing eats.** A hungry creature does not walk to a hatchery, choose a bird
  or race another creature for one, and a prisoner cannot be fed — which is the
  counterplay the prison's withering exists for (Q172). The eating half is a
  creature-side mechanic and is the next one.
- **Free-range chickens.** `MaxFreeRangeChickensPerPlayer` (12) caps birds
  outside a room in the original; nothing here takes a chicken out of one, so the
  cap has nothing to count.

## ADR-0067 — A creature eats, and the hunger clock stops saturating

**Status:** Accepted (2026-09-06). Research: `docs/research/eating.md`. Scenario:
`a_goblin_eats_a_chicken`. Questions opened: Q174, Q175, Q176.

### Context

The oldest "cannot" in the project. A creature has had a hunger clock since
ADR-0048 and there has never been anything it could do about it: the scenario
`a_goblin_gets_hungry` ends with *"and then nothing"*, three times over.

The reason moved twice. It was *"chickens are objects and OBJECTS is
unparsed"*, then *"the Hatchery has no clock"* (Q132, removed by ADR-0066), and
after that it was simply the creature side: nothing walked anywhere for food.

The binary was read on 2026-09-06 — `HungerState.cpp`, whose name DK2 ships, and
the creature state table that had never been recovered before. (That sitting
recorded the table as 239 records; it has 281. See ADR-0072 — the correction
does not touch anything this ADR decided.)

### Decision

`EatingRules` + `CreatureEatSystem`, following the original's states one for one:
walk to the room, choose the bird on arrival, walk to the bird, eat it.

**The hunger clock accumulates.** This is the part that changed shape rather
than grew. `CreatureState.ChickensNeeded` was *set* to `HungerFill` when the
period elapsed; DK2 keeps an accumulator its own dump calls **`HungerNeed`** and
does `HungerNeed += HungerFill` every period, capping nothing. So a creature with
nowhere to eat owes every helping it missed, and eats them all when a hatchery
finally appears. The field is renamed and a test states the difference.

**Two tests, not one.** Setting out needs `HungerNeed >= HungerFill`; carrying on
eating needs only `HungerNeed > 0`. That hysteresis is the original's — it
computes `HungerNeed != 0` the instant a bird goes down and branches on it — and
it is what makes `HungerFill` mean *chickens needed to be full* even though each
bird pays off exactly one.

**One bird fills a prisoner.** The hatchery path passes a literal 1 to the
subtract; the prison path passes the level-scaled `HungerFill`. So a caged Bile
Demon is filled by one chicken where a free one eats five.
`EatingRules.EatInPrison` carries the number and nothing calls it yet, because a
prisoner still cannot move (Q172) — written down where the mechanic will need it
rather than guessed at then.

**Nothing is reserved.** The chosen bird's id lives on the creature and nowhere
else. Two hungry creatures may pick the same one and the loser searches again —
which is exactly what the owner watched prisoners do (Q172): *"they rush toward
that chicken, the first one to it gets it"*.

**Hunger is asked before sleep**, because the original's needs routine asks it
first and returns early on success. `CreatureEatSystem` is registered above
`CreatureSleepSystem` for that reason, and the determinism fixture — all imps —
cannot see the difference, which the pin's comment says out loud.

### Consequences

- `HashVersion` and snapshot `FormatVersion` 26 → 27.
- `CreatureState` gains `HungerNeed` (renamed) and `FoodAttemptTicks`; two
  activities, `SeekingFood` and `FetchingChicken`.
- `CreatureStatId` gains `HungerFill = 9`, which is 100% at every level in the
  shipped table — applied anyway, with a real-data test asserting the row is
  flat so that the day it stops being flat is a data change and not a surprise.
- The fixture gains a **Bile Demon**, for one number: `HungerFill` 5 is the only
  value in the game that tells the two ways of paying off hunger apart.
- 20 tests in `EatingTests`, 4 in `CreatureNeedsOnRealDataTests`.
- **A hatchery that had stopped laying starts again**, because eating takes birds
  out of a room with a one-per-tile ceiling. Nobody wrote that; it is the first
  place two mechanics in this project feed each other without being told to, and
  the scenario shows it at t3919.

### Two documents in one repository that disagreed

`HatcheryRules` said *"only the Chicken carries `ObjectFlags.TypeFood`"*.
`objects.md` had said the opposite since the OBJECTS record was first read —
*"`TypeFood` falls on the Torture Pendulum (65) as well as the Chicken (Q128) …
nothing depends on it: the rule that will matter asks whether a creature can eat
the thing, and a pendulum is not on a hatchery tile."*

Both halves of that turn out to matter. The research note was right about the
count and right about the hatchery — the hatchery search matches the object
**id** 9, so a pendulum in a hatchery is not lunch. It was wrong about "nothing
depends on it": **the prison search matches the flag**, and a pendulum standing
in a prison is food to a prisoner by the original's own predicate.

Which is the interesting failure mode rather than the ordinary one. Nobody
asserted anything false out of carelessness; a code comment generalised a true
statement about three objects into a false one about 145, and a research note
predicted the consequence would never arise from the one search it had thought
about. A real-data test counts the flag now, and `eating.md` §6 states both
predicates side by side.

### Still open

- **How the original walks a hungry creature to the hatchery** (Q174). Its
  in-room search state fails outright when the creature is not already in one,
  and the state that does the walk has one caller, a fallback for a particular
  player id. This composes the two.
- **The post-meal wander** (Q175) and **whether a sleeper wakes to eat** (Q176).
- **Prisoners still cannot eat**, because they cannot move.

## ADR-0068 — The prison drain is hunger, and a Skeleton never withers

**Status:** Accepted (2026-09-06). Research: `docs/research/prison.md` §4b,
`docs/research/eating.md` §10. Closes Q173.

### Context

ADR-0064 built the prison's withering from `PrisonModifyCreatureHealthPerSecond`
and recorded an honest gap: the routine that applies it, `FUN_0047b7c0`, had
**no callers anywhere** — its only reference was a `MOV` immediate inside a long
run of stores filling a table. So *"this routine is the prison's"* was an
inference from the variable it happened to use, and Q173 named the probe.

The probe was run for a different reason. Reading `HungerState.cpp` for eating
turned up **the creature state table** and, behind it, a table of **32 state
categories at `0x00699688`**, each with an enter hook, an update hook and a
leave hook. That is the table the run of stores was filling.

### Decision

**`FUN_0047b7c0` is category 14's update hook, and category 14 is exactly the
six states of `PrisonState.cpp`.** It needs no room-type test because it only
runs on a creature that is in a prison state. Q173 answered.

**And its gate is `HungerFill`.** The "per-creature byte scaled by a per-level
percentage" the earlier reading could not name is the same field eating uses,
through the same stat row. The drain fires only when it is non-zero.

So: **the prison drain is hunger**, and the four creatures that never eat never
wither. `PrisonRules.WitherPerSecond` now takes the creature and returns 0 for
one that cannot get hungry.

**The Skeleton is one of them**, which is the consequence worth stating: a
prison turns a starved prisoner into a Skeleton, and the Skeleton is then immune
to the room that made it. A cell full of skeletons is stable for ever.

**The owner guessed this and said he did not know.** 2026-09-05: *"then that
creature starts loosing health at some standard rate, probably from hunger"*,
followed the next day by *"that was my speculation, I don't know which mechanic
causes a prisoner to start loosing health, decompiled source is the top
authority"*. The decompiled source says the speculation was right.

### Consequences

- One new test in `PrisonTests` with both halves on one map and one clock: a
  Skeleton that sits at its arrival health for sixty seconds, and a Goblin that
  does not.
- `CreatureNeedsOnRealDataTests` counts the never-withering creatures against
  the install's own `HungerFill == 0` set, so the two cannot drift apart.
- **No hash change.** Every prisoner in every existing test and scenario is a
  Goblin, which does get hungry — so this moves nothing that was already
  measured, and saying so is the point rather than the relief.

### Still not built, and now precisely

- **The four-second lump.** The original reloads a counter with
  `ticksPerSecond << 2` and applies `PrisonModifyCreatureHealthPerSecond << 2`:
  -12 every four game seconds against our -3 every one. Same average, four times
  finer here.
- **The anger.** The same routine applies
  `ModifyAngerInCompanyOfHatedCreaturesPerSecond` (150) under reason 9 in the
  same breath. Not built, and the reason channel is unmodelled.
- **A prisoner cannot move**, so it cannot eat and cannot be saved from the
  drain — which is now known to be the counterplay the drain exists for, since
  the drain *is* hunger (Q172).


---

## ADR-0069 — The aimed bonus is a task kind, and it lasts as long as that kind of work

**Date:** 2026-09-06
**Status:** Accepted
**Closes:** Q159, Q162. **Opens:** Q177.

### What the owner watched

Three sentences, off DK2's own status bar, and each one contradicts something
this project had built:

> *"seems an imp gets that 10% boost up to 3 tiles from marked gold tile, and
> funny thing is it stays at 90% until it gets to that marked tile and starts
> excavating, it doesn't revert to 80%, however if I drop an imp, it gets to
> 90%, goes to marked tile, I unmark it before an imp gets to it, it gets to
> that tile, "sees" it is unmarked and it drops back to 80%"*

> *"timeout for efficiency to return back to 80% after slap is 25s, not 60s"*

The second one closes **Q159** with no code change: 25 is what ADR-0062 built,
from a hardcoded 100 ticks in the slap handler, against the owner's own earlier
hand-timed *"after a minute"* and an
`IncreasedWorkRateDurationFromSlappingSeconds` of 60 that nothing in the binary
reads. **A stopwatch is an instrument with error bars and the binary is not.**

The first one costs three changes.

### What the binary says

Read out of `ImpState.cpp` and `ImpUtils.cpp`, with `docs/research/hand-of-evil.md`
section 6 carrying the detail.

**The Efficiency arithmetic reads the wrong field.** DK2's worker branch adds its
ten points on `ImpJobData.DroppedTask` — a **task kind**, zero when there is
none. `ImpJobData.JustDropped`, which this project had been reading, is a
different field **four bytes earlier**, and is a one-shot ticket that the next
job evaluation spends unconditionally. Both are in the creature's state object,
which is pinned at **creature + 0x24E** — the creature debug dump hands that
address to the state dump, and two independent reads in the creature update land
where that base puts them. So this was not a guess that went wrong; it was a
plausible name four bytes from the right one, with nothing checking.

**The look is a bounded scan with its own priority order.** A drop sets the
ticket; the next evaluation clears it, skips its own idle throttle, skips the
ordinary priority chain, and runs a ring scan around where the worker landed. The
predicate is a composite of nine task kinds in an order that is **not** the
priority chain's — excavation, claim, crate, unconscious body, reinforce, gold,
object, corpse — so a worker dropped beside a marked wall digs it rather than
putting a book away, even though the book is second in the ordinary chain
(ADR-0065). Kind 14, claim-the-just-dug-tile, is not in the list at all.
`DroppedTask` is set only if that scan takes something.

**Three tiles, and it is DK2's own off-by-one.** The scan is handed `0x2000`,
which against a `0x1000` tile is a radius of **two**. Its ring walk tests the
bound *after* scanning a ring, with a strict greater-than, from a counter that
starts at 1 — so rings 1, 2 and **3** are all scanned. Which is exactly what the
owner counted: three tiles pay, the fourth does not. Reproduced rather than
fixed, per ADR-0002. The bound is measured to the tile the **work** is on, not to
the tile the worker would stand on, which the owner's fourth tile settles: a wall
four away is reachable from a floor tile three away and still does not pay.

**And there is exactly one clear, in the whole binary.** At the top of a later
evaluation:

```
if DroppedTask == LastTaskType:
    if TakeJob(LastTaskType, radius = unlimited): return   # bonus survives
    DroppedTask = 0                                        # the only clear
```

So the bonus survives the walk, the digging, and finishing the tile. It is lost
only when a worker that still calls itself an excavator can find nothing left to
excavate anywhere on the map — which is the owner's unmark case, arrived at from
the other side. It is *not* worth one job, which is what this project built on
2026-09-05.

### The decision

Build all of it.

- `CreatureState.DroppedTask` (an `ImpTaskKind`, DK2's own numbering) is the
  Efficiency bonus; `JustDropped` stays, demoted to the one-shot ticket it is;
  `LastTaskKind` is added because the clear is guarded on it.
- `HandSystem` no longer decides anything about the bonus. It sets the ticket for
  a worker and zeroes the idle timer, and that is all. The *"is there marked work
  beside this tile"* test it used to run is gone — it was wrong about the
  distance and wrong about which kinds of work count.
- `ImpTaskSystem` gains `TryTakeDroppedJob` (the drop scan, in DK2's drop order,
  bounded to three tiles of the work), `TryTakeJobOfKind` (the same-kind retry at
  unlimited range), and the clear.
- `ClearJob` no longer clears the bonus. Giving a job up is not the clear.

### What this does not settle

**Q177**, and it is one glance: mark two blocks far apart, drop an imp on one,
and watch the status bar after the first falls. The binary says 90%; the older
owner report (*"mines its first seam at 90% and the next at 80%"*) is consistent
with that only if the seam was the last marked work on the map.

**Whether reinforcing really is in the drop scan.** It is one of the nine and it
was read, but it is also the one kind that is available almost everywhere a
keeper's dungeon meets rock — so if it counted freely, *"drop it on a random tile
and it remains at 80%"* would be hard to arrange. The reconciliation this ADR
assumes is that a drop in the middle of a finished dungeon has no rock within
three tiles, which is true and is what the test fixture now models. If the owner
ever sees 90% from a drop next to a plain unmarked wall, this is the line that
was wrong.

### The cost

`HashVersion` 27 → 28 and snapshot `FormatVersion` 27 → 28. **Unlike every bump
since 22 this one is not just defaults**: the determinism fixture is full of imps
taking jobs, and every job taken now records its kind. No *behaviour* moved —
nothing in that fixture is ever picked up, so the retry branch is never entered
and the imps dig, claim and reinforce in the order they always did.

---

## ADR-0070 — A prison holds its inner tiles plus one, so a 3×3 holds two

**Date:** 2026-09-06
**Status:** Accepted
**Closes:** Q166. **Opens:** Q179.

### What the owner watched

> *"about the prison, minimum usable size is 3x3 tiles, that gives capacity of 2
> prisoners that can be held, and 2 skeletons that can be spawned. Any smaller
> size and it doesn't render like a prison, it only occupies those tiles."*

With a screenshot of DK2's own status bar: **`Captives held: 0 / 2 … Maximum
Skeletons: 2`**.

This project held **one prisoner per tile**, which on that room is **nine**. The
error is more than four times, and everything that counts prisoners was
downstream of it: whether an imp will deliver, whether a keeper has overcrowded
the room, the jail-break roll, and how many Skeletons a keeper can raise.

### What the binary says

DK2's room-capacity routine switches on room type and the **Prison is case 8**
(type 11 − 3). Its case is unlike the others: where a lair or a treasury walks
the room's *object* list and counts the pieces of furniture it recognises, the
prison walks its own **tiles** and counts the ones a general
*"nothing is standing here"* predicate accepts — then **adds one if that count is
non-zero** — and stores the result in the field the room debug dump calls
`SafeCreatureCapacity`.

Two consequences fall straight out, and they are the owner's two sentences:

- **A prison's bars and gate take the rim.** What is left on a 3×3 is one tile,
  so the count is 1 and the capacity is **2**.
- **Below 3×3 there is no such tile at all.** The count is zero, the *"+1"* never
  fires, and the room has no capacity — *"it only occupies those tiles"*.

The second sentence is not a separate rule that had to be added. It is the same
arithmetic reaching zero, which is the sort of agreement worth more than either
half on its own.

**And `TotalCapacity` is left at −1 for a prison** — the case `break`s without
touching it, and the sentinel the function was primed with survives. So the
generic room-occupancy check does not apply to a prison, which is *why* a keeper
can overcrowd one by hand at all. This project already had manual overcrowding
(ADR-0064) as a rule of its own; it turns out to be a consequence.

### The decision

`PrisonRules.Capacity(prison)` becomes `InteriorTileCount + 1`, and `0` when
there is no interior. `RoomInstance` already counted the rim for the Temple
(Q60), so this needed no new geometry.

**Capacity stops being a per-tile rule and that is deliberate.** Two prisoners on
a room with one inner tile is DK2's own arithmetic, and smoothing the *"+1"* away
to make it tile-shaped again would be fitting our model to our habits.
`FreeTileIn` still puts each arrival on a tile of its own, because that is where
a body is laid down — it is not what limits the room.

**The fixture had to change with it.** `PrisonTests` used a 1×3 column of cells,
which under the corrected rule is not a prison; it is now a real 3×3 with paved
floor around it, and the scenario's two-cell prison is a 3×3 too. That is the
right kind of churn: the old fixture was testing a room DK2 would not have built.

### What this opens

**Q179.** The Prison's `RecommendedSizeX/Y` is **5×5**, and the owner has just
built a working 3×3 one. ADR's Q45 reads that field as *"the size below which the
room does nothing"*, on two observations that fit it exactly (a graveyard needs
3×3 and says 3×3; a temple needs 5×5 and says 5×5). A third room now disagrees.
Nothing depends on it — `IsFunctional` is read only by `PortalRules`, and a
prison's capacity now comes from its own geometry — so this is recorded rather
than resolved, per the conflict protocol.

### What is still ours

**Whether an L-shaped prison's rim is furnished the same way.** The rim is
counted rather than computed from the bounding box, so an L-shape answers
sensibly; whether it answers *correctly* is unread. For the rectangles anybody
builds, the two agree.

**Maximum Skeletons.** The status bar shows the same number, and here a Skeleton
replaces the prisoner that died, so the two are equal without a second rule. That
they are equal *in DK2 for the same reason* is inference.

---

## ADR-0071 — A knocked-out creature has exactly one sixth of its health, and DK2 says so

**Date:** 2026-09-06
**Status:** Accepted
**Closes:** Q167 (the health half). **Supersedes:** the 20% in ADR-0064.

### What this replaces, and how it went wrong

A prisoner's arrival health has been guessed three times.

1. **Full health** — the first draft, on nothing but the shape of the mechanic.
   A Goblin took 500 game seconds to wither.
2. **20% of maximum** — after the owner said *"its flower gets two petals"*.
   `CreatureCriticalHealthPercentageOfMax` ships at 20, and two petals of ten is
   20%, so the observation and the shipped number appeared to meet. 100 seconds.
3. **A sixth of maximum**, read out of the binary. **83 seconds.**

**Both legs of the second reading failed on the same day.** The flower has
**six** petals, not ten (Q171, owner screenshot). And a raw scan of `.text` for
that variable's cached global finds **exactly one reference — the loader writing
it**. Nothing in DK2 reads it, so the project had built a prisoner's whole clock
on a number the engine ignores, justified by a petal count that was wrong.

That the two errors cancelled to something plausible is the uncomfortable part.
20% sits inside the 2-petals-of-six band, so the guess looked confirmed from
both sides while resting on nothing.

### What the binary says

DK2's `ModifyHealth` applies the delta, then:

```
if flags & CreatureDying:  return health          # already down: this can kill
if health < 0:
    health = maxHealth / 6                         # level-scaled maximum
    KnockOut()                                     # sets the CreatureDying flag
```

The sixth is computed the long way round — `100 * max / 6 / 100`, as two
magic-number divides — which is the arithmetic of *"a sixth expressed as a
percentage"*, and is the clearest sign that the sixths and the six petals are
one decision. Some creature types are destroyed outright instead, which is the
`DieInstantly` flag this project already reads.

**And imprisonment does not touch health at all.** The prison's category-enter
hook sets the drain countdown and nothing else. A prisoner arrives carrying
whatever the knockout left it.

### The decision

`CreatureHealthRules.CriticalHealth` becomes `KnockoutHealth` = `MaxHealth / 6`,
with `KnockoutFraction = 6` named next to it. `PrisonRules.Imprison` is the only
caller and keeps calling it — DK2 puts the value there at the moment of the
collapse rather than at the prison door, and for the only path DK2 allows into a
cell the two are the same.

`IsCriticallyHurt` keeps reading the shipped variable and keeps having no
callers, now with a note saying that DK2 does not read it either.

### The loose end, and it is a display question

**A sixth is exactly one petal of six, and the owner counts two** on a creature
an imp has just dragged in. So either the flower is not a plain proportion of
six, or something between the knockout and the cell adds a little. Neither moves
the health number, which is read; and the residue belongs to whatever eventually
draws a flower, not to `PrisonRules`.

### The cost

No hash change: the determinism fixture imprisons nobody. Two prison tests moved
from 300/100 s to 250/83 s, and the scenario's prisoner now perishes at t2619
instead of t2939.

## ADR-0072 — The state table has 281 records, and the 239 was a scan window

Date: 2026-09-06 · Status: accepted · Research only, no code change

### The question, asked deliberately

The creature state table's record count was left explicitly unsettled at the end
of the previous sitting: *"239 was counted under the shifted frame; Flame says
281. Don't quote 239 without re-deriving it."* This is that re-derivation.

**It is 281.** Flame was right, again.

### Why it was 239, and it was not the frame

The frame shift — base `0x006DA8C0` instead of `0x006DA8B0`, which put every
handler one state late — was found and fixed last sitting, and it did **not**
move the count. The count was wrong for a second, unrelated reason.

`eating.md` recorded the recovery method as *"list every write into
`[0x006DA000, 0x006DC000)`"*. The table runs to `0x006DC420`. Under that
sitting's frame record 238 starts at `0x006DBFFE`, inside the window, and record
239 starts at `0x006DC017`, outside it. **The window ended `0x420` bytes before
the table did**, and the walk stopped at a boundary belonging to the scan rather
than to the data.

What it then recorded was *"all 239 resolve, none unknown"* — true, and
worthless. Every record the scan **saw** resolved, because resolving is what the
scan did. A count cannot be checked by the instrument that produced it. That is
the eleventh instance of a claim nothing checks turning out to be false, and the
first where the check and the claim were the same act.

The published code range carried the same truncation: the fill was recorded as
ending at `0x0047FAEB`; it ends at `0x004805E4`.

### What settles it

Six checks, no two sharing a failure mode. The first five are internal to this
reading; the sixth arrived later and is not:

1. **Offset histogram.** Every reference into the table's address range falls on
   exactly nine offsets modulo 25, with no strays. This recovers the stride and
   the frame without assuming either.
2. **Exact accounting.** 2,529 writes = **281 × 9**. Every field of every record
   written exactly once — none twice, none missing.
3. **Contiguity.** All nine fields span records `0`–`280` with no gaps.
4. **Self-consistency.** 277 records write their id as a literal and each equals
   its index; the four that use a register are records 0, 1, 3 and 8, and the
   smallest literal in the table is 2 — exactly what remains once those are
   removed.
5. **The labels resolve.** All 281 `+0x14` values are valid `Text.str` ids.
6. **An outside reading agrees on the numbering, 58 times out of 58** (added
   2026-09-06). Flame's map names 58 of the 264 handlers in a form that carries
   the state's decimal id, and every one equals our record index; none
   disagrees. §5.2 above records that Flame's base address was right where ours
   was wrong, and the rule taken from that was to re-derive on disagreement —
   this is the converse being run, per record rather than per base address, with
   nothing to re-derive. It raises confidence rather than adding a new kind of
   evidence: Flame is a reading of the binary, not the binary. Detail, coverage
   and the ADR-0046 boundary are in
   `docs/research/creature-state-table.md` §5.4.

### What came with it

**Every state is now named.** The `u16` at `+0x14` is a `Text.str` string id, and
it resolves to the activity label the game itself prints on the flower above a
creature's head — the line the owner read as *"reinforcing walls"* (Q171). All
281 resolve; 70 distinct labels. This was posed to Codex in Brief 5 as a long
shot and it paid.

**The two bytes are different things.** `eating.md` called `+0x16` and `+0x17`
*"two category bytes"*. Reading `SetState`, `+0x16` is a **group tag**, only ever
compared for equality — which is why it holds `50` and `100` without being out of
range — and `+0x17` is the **category**, the one that indexes the 32-entry table
at `0x00699688`. Its range across all 281 records is `0`–`31`, which is the
32 entries exactly.

**Q174 is answered, and it was the same off-by-one.** The original *does* walk a
hungry creature to the hatchery: `FUN_004633d0` calls `SetState(0x5B)`, and state
`0x5B`'s handler finds the room and calls `MoveTo(target, resume = 0x5C)`. Under
the old frame that handler was filed under `0x5A`, which really is
near-unreferenced — so a working walk looked orphaned and `0x5B` looked like a
state that fails unless the creature is already standing in the room.
`CreatureEatSystem` turns out to match the original's shape rather than compose
around a gap, and ADR-0067's stated-composition caveat is withdrawn.

### The decision

`docs/research/creature-state-table.md` is the state table's home and carries all
281 records. `eating.md` §2.1 keeps a superseded marker and a corrected summary,
because that is where the reader who followed ADR-0067 will look first.

No simulation code changes and no hash moves: nothing in the project ever read a
record count.

### The rule this leaves

Both errors were invisible for the same reason — **the thing that would have
caught them was the thing that produced them.** A fixed-stride table read at the
wrong offset yields plausible values at every field; a table counted with a scan
window yields a plausible count at whatever boundary the window has. Pin a
table's *frame* from the code that indexes it, and its *extent* from something
other than the scan: the next referenced global, an exact field-count identity,
or a value that has to be valid somewhere else. All three were available here and
none had been used.

### A legal question, flagged not assumed

The new document reproduces 70 short UI strings from the install's `Text.str` as
state labels. They are research documentation — the labels are how a state is
identified, and the code loads them at run time from the user's own install like
every other asset, so nothing is redistributed by shipping this repo. If that is
judged too far, the `Text.str` id column alone keeps the table usable and the
labels can be dropped without re-deriving anything.

## ADR-0073 — A prisoner shuffles about its cell, one orthogonal tile at a time

Date: 2026-09-06 · Status: accepted · Q172, half of it

### The question, and how long it had been open

The owner watched a prison on 2026-09-05 and reported three things: a prisoner
*moves about its cell*, prisoners *race* each other to a chicken dropped among
them, and one of them *eats it*. That became Q172, and for a day the project's
answer was to do none of it. `CreatureMovementSystem`, `CreatureSleepSystem` and
`ImpTaskSystem` each carried the same paragraph saying so, and the harness
declared it unsupported in two places.

This ADR builds the first of the three. It is the one everything else was
waiting on.

### What the original does, read state by state

Under the state-table frame corrected by ADR-0072 — without which none of this
could have been read, because every handler was filed one state early — the
prison is **states 114–126**, all of category **14**. Three of them are the loop:

- **114** (`0x72`) — arrival. Finds the room of type `0xb`, checks it with
  `FUN_00454110` (the same tile predicate the capacity routine counts with),
  records the jailer, and enters 115.
- **115** (`0x73`) — the decision. If hunger clears a threshold *and* a cooldown
  has elapsed it goes to state **117**. ~~and eats where it stands~~ —
  **corrected by ADR-0074**: 117 is a *search and a walk*, and the eating is
  118. The error came from reading only as far as the transition. Otherwise it
  asks `Room.cpp:1717` for a neighbouring tile, issues a move to it, and enters
  116.
- **116** (`0x74`) — the walk, and the pause after it. Each tick it rolls
  `random(100)` and returns to 115 when the roll is under **20**.

### The step, read from instructions rather than decompilation

Ghidra garbles `Room.cpp:1717` badly — it types the random result as a struct
pointer — so this one was read from the disassembly. It takes a neighbour
**count**, draws `random(count)` as a **starting index**, and then scans
`(start + i) % count` forwards through a table of offset pairs: the **four
orthogonal** offsets at `0x006BC350` when the count is 4, the eight at
`0x006BC370` otherwise. The prison passes 4. **The first candidate that passes
wins**, and a candidate must have a non-zero room id, belong to the **same room
instance** the creature is standing in, and have a terrain flag clear.

Two things follow that shaped the implementation:

**The room-instance test is the containment.** Nothing anywhere says *"a
prisoner may not leave the prison"* — a tile outside the cell is simply never a
candidate. That is a better guarantee than a guard in the mover, and it is why
`CreatureMovementSystem`'s refusal is **deleted** rather than narrowed.

**The table order is behaviour.** North, east, south, west. It decides which way
a prisoner goes when its drawn direction is a wall, and getting it backwards
would be invisible except in the walk it produces — so a theory pins it from the
cell's north-west corner, where two of the four directions are outside the room
and the scan has to wrap.

### `MotionlessInPrisonSeconds` is dead, and the guess built on it is gone

`CreatureState` used to say, of the shipped `MotionlessInPrisonSeconds` = 8,
that it was *"presumably how long one stands still between moves"*. A raw
`.text` scan for its global `0x006F25E9` returns **one hit**, and disassembling
it gives `MOV [0x006f25e9],EAX` — the setter stub. **Nothing in DK2 reads it.**

That is the **third** shipped balance variable found dead here, and the second
to have had a mechanic built on it: `CreatureCriticalHealthPercentageOfMax` gave
this project a wrong prisoner clock for a day (ADR-0071), and
`CriticalHealthPercentageForHeroLair` is the third. **A named variable in the
table is not evidence that the engine consults it**, and the check costs one
scan.

### The decision

`PrisonRules.TryWanderStep` carries the rule — the four offsets in DK2's order,
the wrap, and the same-room test — and `PrisonerWanderSystem` runs it every
tick: a prisoner that is standing still rolls
`WanderPercentPerTick` = 20, and on a hit takes one step. The two draws happen
in the original's order, the hundred before the direction, so the random stream
is consumed the same way round.

Registered **after** `PrisonSystem`, so a prisoner freed or turned to bone this
second is not handed a step it will never take.

### What this cost, and what it did not

**No hash change.** The determinism fixture imprisons nobody, and the system
draws no randomness when there is no eligible prisoner.

**Two tests changed, and both were asserting the gap rather than a rule.**
`APrisonerDoesNotWalkWorkSleepOrGrowHungry` asserted that a prisoner never left
its arrival tile and never held a path; those two lines are gone and the name
lost its *Walk*. `APrisonerThatWithersAwayRisesAsASkeletonForItsJailer` asserted
the skeleton rises on the tile the prisoner went in on — it now tracks where the
prisoner actually stood, which is what *"replaced where it stands"* always
meant and what the test had been getting for free.

The scenario shows it: between imprisonment at t959 and perishing at t2619 the
Goblin takes **50 steps** and stands on **all nine** tiles of the 3×3, never
leaving, and rises as a Skeleton at (7,5) rather than the (5,3) it arrived on.
The clock is untouched.

### What is left of Q172

**The bird**, tracked as ~~Q187~~ **Q195** — the number collided with the
save-format session's, which was allocating from the committed file while this
one was not. It used to wait on movement; it now waits
only on a keeper being able to drop a chicken into a cell. Every other piece
exists, including the prison's own eating number
(`EatingRules.EatInPrison` — one bird pays off the whole of `HungerFill`) and
the original's search, which is state 117's `FUN_00473550`.

Q188 records the one thing in the loop that is not read: a second path in state
116 that halves the step roll to 10 in 100 when a per-creature flag is set.

## ADR-0074 — A prisoner gets hungry, hunts the cell, and races the others to the bird

Date: 2026-09-06 · Status: accepted · Q195, and the rest of Q172

### The question

The owner watched a prison on 2026-09-05 and reported three things. ADR-0073
built the first — a prisoner moves about its cell. This is the other two:
*"they rush toward that chicken, the first one to reach it eats it, and its
health replenishes."*

### What the original does, and the first sentence of it is a correction

**ADR-0073 said state 117 was where a prisoner "eats where it stands". It is
not.** That sentence was written from the transition alone; reading the handler
gives something else entirely, and something better:

| State | What it does |
|---|---|
| **115** | Hungry **and** past `TimeBetweenReattemptingHungerSeconds` → stamp the clock, go to 117. Otherwise wander |
| **117** | Walk the prison's own tile chain, then each tile's object list. Keep every object of kind 2 whose definition carries flag `0x80`. Rank by **straight-line squared distance**, take the smallest, and **issue a move to it** resuming in 118. Found none → apply `AngerNoFood`, tell the interface, drop back to 116 |
| **118** | A food-flagged object underfoot: destroy it, pay off **the whole of `HungerFill`**. Still owing → 117. **Nothing there → also 117** |

**The race is the absence of a reservation.** Nothing marks the chosen bird —
the chooser writes it on itself and on nothing else — so two hungry prisoners
pick the same chicken and the loser arrives at an empty tile, falls through
118's loop into 117, and looks again. The owner's sentence needs no mechanism
beyond leaving one out, which is why this project had already written the same
absence into the hatchery path (ADR-0067).

`prison.md` §7 carries the full read.

### Three numbers, none of them chosen

**One bird fills a prisoner.** 118 passes the level-scaled `HungerFill` where
the hatchery's state passes a literal 1, so a caged Bile Demon that owes five
helpings is filled by one chicken. `EatingRules.EatInPrison` had this written
down two sittings before anything could call it, and it was right.

**The anger is a quarter of the tabled figure.** The creature loader divides
every `angerNo*` field by the level's ticks per second as it loads it — pinned
by the identical register multiplying `TimeAwake` and `TimeSleep` into ticks two
dozen instructions earlier — so the engine holds **anger per DK2 tick**. A
routine adding one per tick delivers the tabled figure a second, which is the
lair's and is what the owner measured. **117 adds one per attempt**, and
attempts are gated to one a game second. A hungry prisoner in an unstocked cell
therefore angers at **25 a second where a bedless creature angers at 100**.

This is why `SimulationTiming.OriginalTicksPerGameSecond` now exists. Four is a
constant of DK2, in the same class as a balance variable; using our own
placeholder `TicksPerSecond` there would have made the number an artefact of a
guess we have not settled (Q1).

**A prisoner does get hungry**, and the bit that says so answers part of Q185.
The needs routine suppresses the hunger clock on `flags & 8` of the state
record's byte at `+0x18`, and bit `0x10` beside it suppresses tiredness. **The
fifteen states that set them are exactly drinking, gambling and dancing** — the
Casino, not the Prison. Every prison state has both clear.

### The gap this leaves, and it is not a gap in the prisoner

A prisoner never gets hungry *in* a cell: a Goblin's `HungerRate` is 180 game
seconds and the prison's drain turns it to bone in 83. The mechanic is
reachable anyway, because `HungerNeed` accumulates over a creature's **whole
life** and is paid off only by eating — so anything dragged in after a few
minutes on the map is hungry when it lands. The tests set that up rather than
waiting for it, and say why in the test.

### What this corrects besides ADR-0073

**`eating.md` §2.2 was still on the pre-ADR-0072 frame** and every state id in
it read one too low; the handler addresses were right all along. The five
hatchery states are `eating` and the six prison ones are `being held captive`,
which is now the cheapest check that the frame is straight.

**The needs routine's first gate is the group tag, not the category.** Group 7
is exactly the fourteen states that *are* a need — lair-seeking, sleeping,
eating, pay — so the rule reads *a creature already attending to a need does not
reconsider its needs*. **Q176's conclusion survives and its explanation does
not**: a hungry creature still does not get out of bed, because sleeping is one
of the fourteen.

**`FUN_004633d0` routes on the room, not on imprisonment.** The general "go and
eat" attempt asks which room the creature is standing in and jumps to the prison
search when it is type `0xb`, without asking whether the creature is a prisoner.
So in DK2 *any* hungry creature standing in a prison eats off the floor rather
than walking to a hatchery. Ours is prisoner-specific. Nothing observable turns
on it — only prisoners loiter in prisons — and it is named rather than smoothed
over.

### The decision

`PrisonerEatSystem` runs the three states, registered **before**
`PrisonerWanderSystem` because the original's single handler asks about hunger
first. `EatingRules.NearestFoodIn` is the flag-based search, kept separate from
the hatchery's id-9 one because the two are genuinely different sets — a Torture
Pendulum is food in a prison and not in a hatchery.

A prisoner **stays in `CreatureActivity.Imprisoned` throughout**, marked only by
`TargetObjectId`, because 117, 118 and 119 carry the same label, group tag and
category as 114, 115 and 116. That is not tidiness: it is why `PrisonSystem`
keeps draining a prisoner walking to its dinner, and why a keeper can still lift
one out mid-stride.

`CreatureHealthSystem`'s blanket skip of prisoners is narrowed to health. It
used to take their hunger clock with it, which was a side effect of ADR-0064
rather than a rule.

### What is ours

**The route to the bird is restricted to the cell's own tiles.** DK2 issues an
ordinary move and relies on the bird being in the room the prisoner stands in;
ours would happily route an L-shaped prison's corner through the corridor
outside, which would look exactly like an escape. The containment ADR-0073 found
in the wander, applied to the walk.

### What it cost

**No hash change.** The determinism fixture imprisons nobody, and the system
draws no randomness at all.

**One test changed its answer and one changed its name.**
`APrisonerDoesNotWorkSleepOrGrowHungry` asserted that a prisoner's hunger clock
never moved; it now asserts that it does, as `APrisonerDoesGrowHungry`, and
carries the arithmetic explaining why no prisoner ever reaches a full helping
inside a cell.

## ADR-0075 — The Hand of Evil holds a thing, not only a creature

Date: 2026-09-06 · Status: accepted · Q196

### Why now

ADR-0074 gave a prisoner the hunger, the hunt and the meal, and left the
mechanic **complete and unreachable in play**: a chicken only gets into a cell
because a keeper puts it there, and `PlayerState.Hand` held creature ids.

The owner settled it the same day, unprompted: *"I can pick up a chicken and
drop it in the prison, or anywhere in the dungeon, on my owned tiles"* — with a
screenshot of **four chickens held at once**, as four separate slots, and the
drop offered on a Claimed Path whose tooltip reads *"Your territory"*.

### What DK2 does

Its hand is one array of **thing ids** on the player record, with a count and a
capacity beside it, and both handlers branch on the thing's kind. Creatures and
objects share the table, so four chickens and a goblin go into the same slots.
Ours are separate id spaces, so a slot carries which kind it is — `HandItem`.

**Last in, first out**, and this is now an observation rather than an inference:
the owner confirmed it, and the drop handler reads back the slot it is about to
free. It holds **across kinds**, because there is one array.

**What may be lifted is `CanBePickedUp`** — 26 of the 145 OBJECTS records: the
three golds, the Chicken, Call To Arms, all fifteen specials and six odds and
ends. Note what is absent: **spell books, crates and creature beds**. A keeper
does not hand-carry a finished trap to a workshop; an imp does.

That flag has **exactly one reader in the executable**, and it is not the
pick-up handler — it is the cursor's target search, which is worth a paragraph
because it is a rule rather than a ray-cast. It sweeps the 3x3 tiles around the
point, measures with an **octagonal approximation** (`max + min/2`) rather than a
true distance, allows an object within a **quarter tile** where a creature gets
**half**, admits the **Dungeon Heart** by id regardless of the flag, and
**returns a creature in preference to an object** whenever both are in reach.
None of it is here, because none of it is simulation.

### The near miss, and it is the fourth of its shape

`CanBeDroppedOnAnyLand` is set on **exactly one** record — Call To Arms — which
is precisely the object you would expect to be exempt from an owned-ground rule,
and its name is a perfect fit for the behaviour the owner described. It was one
edit away from being the drop rule.

**It is tested nowhere in the executable.** A raw scan for the mask against the
definition's flag dword returns nothing at all. The name is OpenKeeper's reading
of a bit, which is their guess and not evidence; the fit is a coincidence of
naming.

That makes it the **fourth dead data field** found here, after
`CreatureCriticalHealthPercentageOfMax` (ADR-0071, which cost a wrong prisoner
clock for a day), `CriticalHealthPercentageForHeroLair` and
`MotionlessInPrisonSeconds` (ADR-0073). The rule that catches all four is one
line long — **scan the field and count its readers before building on it** — and
it cost thirty seconds here.

So the owned-ground rule is reproduced **as an observation**, and where it lives
in the original is stated as unread: the drop handler contains no ownership test
at all, which puts the gate in the input layer that draws the marker the
screenshot shows.

**And it is the object drop's rule, not the hand's.** A creature can certainly
be put down on somebody else's ground — the drop handler has a branch that
*changes the creature's owner* when the tile belongs to neither the creature's
keeper nor the dropping one, which would be unreachable if the drop were
refused.

### Two branches read and deliberately not built

Both need a target this project's drop has no way to name, and both are recorded
rather than guessed at:

- **A chicken dropped into somebody else's Hatchery becomes theirs.** The
  handler tests object id 9 against a tile owned by another keeper, in a room of
  type 4, and reassigns the bird.
- **A chicken or a pile of gold dropped ONTO a creature is earmarked for it** —
  the handler writes that creature's id into a field on the object when the
  creature's level-scaled `HungerFill` (or ~~`MaxGoldHeld`~~ **`Pay`** — corrected 2026-09-06: the field is `def + 0x69a`, which the loader fills from record `0x0F1C`, and `kwd-creatures.md` calls that `u16 pay`; the same field and the same level-percentage table drive the wage in `payday.md` §3) is non-zero. **That is
  a reservation**, and it is the one case where a bird is not up for grabs. The
  prison search of ADR-0074 does not consult the field, so a race for an
  earmarked bird would still be a race; whether anything else does is unread.

### The decision

`PlayerState.Hand` becomes `List<HandItem>`; `ObjectState` gains `InHand`, the
object half of `CreatureState.InHand`, and a held object is out of the dungeon —
nothing may find, eat or decay it. `DropCreatureCommand` becomes
**`DropCommand`**, because DK2 has one drop that branches on what it read back
and a name that says "creature" would be a lie. `PickUpObjectCommand` joins it.

The scenario harness gains `setup.objects` and `setup.creatures.hunger`, both
named as shortcuts in the run report. The second is not a convenience: a
prisoner never gets hungry *in* a cell — a Goblin's `HungerRate` is 180 game
seconds and the drain turns it to bone in 83 — so stating the hunger a creature
accumulated *before* capture is the only way a scenario can reach the mechanic
at all.

### What it cost

**`HashVersion` 28 → 30, skipping 29, and `FormatVersion` with it.** Both are
composition rather than behaviour: the determinism fixture's keeper never picks
anything up, so no slot's kind byte is ever written, and the world holds no
objects, so `InHand` is never hashed for one. A field added to an empty list
still has to change the hash, which is the useful shape for a pin.

**29 is skipped on purpose.** Codex's fog-of-war branch claimed it in its own
worktree while this was being written, and two branches holding one version
number is exactly the failure a version number exists to catch — a save written
by either would be readable by the other and wrong. Numbers are cheap.

**One test found a real gap rather than confirming one.**
`AMixedHandSurvivesASnapshot` failed because `ObjectState.InHand` was hashed and
not snapshotted, which is precisely the restore-and-continue invariant the
snapshot tests exist for.

### The scenario shows the clock, not the race

`a_keeper_drops_a_chicken_in_a_prison` runs the whole loop: two goblins dragged
in by imps, angering at 25 a second in an empty cell, then a keeper lifting a
bird off its own floor and putting it in the cell, and one prisoner eating for
+375.

**The second prisoner does not lose a race there. It loses a clock.** The two
were captured 50 ticks apart, so their once-a-second looks are ten ticks out of
phase; the bird lands at 1401, the first looks at 1409 and eats it, and the
second's next look is 1419. That is a real consequence of
`TimeBetweenReattemptingHungerSeconds` and is narrated rather than engineered
away. The contested case — both choosing the same bird, one arriving at an empty
tile — is pinned by unit test instead, because staging it in a scenario would
mean choreographing the wander, and the choreography would be the part worth
doubting.

## ADR-0076 — Fog of war is simulation state, and it cost a hash bump

Date: 2026-09-06 · Status: accepted · Q190–Q194 · Built by Codex on
`codex/fog-of-war`, merged after independent re-derivation

### The stop rule, and why it was overridden

Brief 8 was written with a Phase 0 stop rule: **find out whether fog is
per-keeper simulation state or per-client renderer state, and if it is the
former, report and stop.** The reasoning was that in `WorldState` fog enters
`HashInto`, `WorldSnapshot` and the pinned cross-machine literal, and beside
`WorldView` it costs nothing — so the answer decides the shape of the whole
mechanic and is not a decision to take by accident.

Phase 0 answered **simulation**, by the exact route Q190 predicted from another
mechanic: DK2's world-save coordinator invokes the runtime map's save operation,
which walks every tile and writes ten contiguous bytes of the live 16-byte tile
record — including the byte whose bit 6 gates fog rendering. **A save persists
the simulation.** The saved-game session had already noticed three lanes of that
10-byte tile moving between any two saves and recorded them as *"consistent with
visibility or fog state, and still UNKNOWN"*; this is what they were.

The owner reviewed that result and authorised continuing. **The stop commit is
retained in the branch's history**, which is the right way to keep an overridden
gate honest: the decision is visible rather than smoothed out of the record.

### What was verified on merge rather than taken on trust

Two counted claims, re-derived from the shipped data with a tool written for the
purpose rather than by reading the branch's tests back to themselves:

- **`PerceptionRange` is `0x6000` for all 46 creatures** — exactly six tiles at
  4096 to the tile. Confirmed, and the useful half is the contrast: the same
  records carry `DistanceCanSee` and `DistanceCanHear` at `0x4000`, four tiles,
  for all 46. So there really are two different ranges and the branch took the
  right one. This turns the owner's *"about 6 tiles"* estimate into a number.
- **Six terrains carry `RevealThroughFogOfWar`** — Impenetrable Rock, Water,
  Lava, Gold, Gems and Edge of Map. Exactly the six the branch named. Which is
  why an unexplored map still shows a keeper where the gems are.

`kwd creatures -s` was added to the tools for the first of those, because a
*"they are all the same"* claim is only worth anything counted.

### What is accepted

Per-keeper discovery on every tile, hashed and snapshotted; a monotonic circular
reveal read from creature data rather than frozen as a constant; a keeper-
specific `WorldView` that leaks nothing about a hidden tile; and the
`RevealThroughFogOfWar` exception applied through the flag rather than an id
list.

**Fog defaults to off**, as a world and scenario setting, so nothing that existed
before this behaves differently. That is what makes a hash bump on new
composition rather than on changed behaviour.

The branch's own UNKNOWN list is kept intact in `fog-of-war.md` §8.3, and the
most important entry is that **persistence proves the state belongs to the
simulation but does not prove any AI reads it** — Q190's second half is open.

### What the merge changed

**The branch report was moved out of the repository root.** It arrived as
`README.md`, which master did not have, so a branch report would have become the
project's front page. Its content is now `fog-of-war.md` §8 and this ADR; the
file is deleted.

**`HashVersion` and `FormatVersion` resolve to 30, not 29.** The branch took 29
in its worktree while ADR-0075 was taking it in the main tree — two branches
numbering the same save format differently, which is the collision a version
number exists to catch, and either one would have read the other's file and been
wrong. The hand went to 30 rather than fighting over 29, so master goes 28 → 30
across two commits with 29 spent on a branch. Numbers are cheap.

### What this leaves for whoever picks fog up next

Q192 is the interesting one. `All Owned Reveal`, `Re-shroud` and `Re-fog` are
real strings, and the configuration value behind fog is **boolean** — so they
are not one enum behind that value, and where they *are* selected is unread. The
campaign's *Stay Revealed* is the one mode the owner has confirmed and the one
mode that is built.


## ADR-0077 — A creature goes down on the keeper's own ground, and only a worker may go anywhere else

**Status:** accepted, 2026-09-06
**Context:** Q198, `docs/research/hand-of-evil.md` §8, ADR-0075
**Supersedes in part:** a paragraph of ADR-0075's reasoning, not its decision

### The observation

The owner, 2026-09-06:

> *"imps are the only creature that can be dropped onto dirt path tiles, those
> are tiles that are unclaimed by any keeper"*

CONFIRMED, watched in the original.

### What the simulation did instead

`HandSystem.Drop` refused exactly one thing: a tile the creature could not have
stood on. So a keeper could put a Goblin down on unclaimed dirt, on a rival's
claimed floor, in the middle of somebody else's dungeon — anywhere it could
stand. That is not a missing feature but a **wrong rule**, and it had been wrong
since the hand was built (ADR-0062).

### The inference that let it stand, and why it was wrong

ADR-0075 and `hand-of-evil.md` §8 both argued the drop was *not* restricted to
owned ground, and gave a reason that reads well:

> DK2's drop handler has a branch that changes the creature's owner when the
> tile belongs to neither keeper, which would be unreachable if the drop were
> refused.

**It proves only that the handler does not refuse.** That was already known and
already written down for the *object* rule three paragraphs away: `FUN_004bc710`
contains no ownership test at all, so the gate is in the input layer that draws
the drop marker. **A handler that does not refuse is not a UI that offers.**
Whatever reaches the owner-changing branch is not the ordinary drop.

This is the project's standing failure pattern in a new costume — a claim
nothing checked, resting on a reading of one function — and it is the **14th**
recorded instance. It differs from the usual shape in one way worth noting: the
inference was not lazy. It was a real argument from the binary that happened to
answer a different question than the one asked.

### Decision

`HandRules.CanDropCreatureOn(world, database, tile, playerId, mover)`:

1. the tile is on the map, and
2. **its owner is the dropping keeper**, or
3. the creature carries `IsWorker` **and** the terrain is *unclaimed floor*.

`HandRules.IsUnclaimedFloor` is structural: not `Solid`, not `Ownable`, not
`Room`, and neither `Water` nor `Lava`. In the shipped table of 41 terrains that
is **Dirt Path and the Mana Vault**, which carry the identical flag word
`0x10008000`, and whose claimed counterparts — Claimed Path and Claimed Vault —
are `Ownable`. That pair is what makes "unclaimed" mean something.

The walkability gate stays and runs first, so the two refusals stay
distinguishable in the event log: `not_walkable` and `not_owned`.

### Three things this decides deliberately, and says so

**Keyed on `IsWorker`, which is wider than what was watched.** The owner said
*imps*; the flag falls on exactly two creatures, the Imp and the **Dwarf**. This
project has twice decided to key worker behaviour on the flag rather than the id
(`imp-tasks.md`, `portals.md`), and a hero keeper dropping a Dwarf is not
something anything here can do yet — so the widening costs nothing today. It is
recorded rather than hidden.

**Water and lava are excluded by name, and that is a stop rather than a
reading.** Both are unownable non-solid floor and would qualify structurally,
and **44 of the 46 creatures can wade** — so without the clause a keeper here
could drop an imp into the middle of a lake. Whether the original offers that is
**UNKNOWN**; it is item 5 on the observation checklist, with a bridge and a
rival's *unclaimed* room floor. Refusing invents least, and the test that pins it
says in as many words that it is the narrower reading and what would change it.

**A prisoner is not a way round the rule.** `CanPickUp` makes one ownership
exception, for lifting a captive out of a cell (ADR-0064). It does not extend to
putting one down: a keeper cannot use a prisoner to plant somebody on unclaimed
ground, and a refused drop does not free the captive.

### Consequences

- Six tests in `HandTests`, one of which exists only to prove the imp's water
  refusal comes from *this* rule and not from the walkability gate above it.
- `scenarios/creatures/a_keeper_drops_a_warlock_and_an_imp_on_open_ground.yaml`
  — a Goblin refused on unclaimed dirt, the same Goblin landing on its own lair,
  and an Imp landing on the very tile the Goblin was turned away from. The middle
  drop is what makes the first mean anything.
- The run turned up the reason DK2 has the exception at all: the dropped imp
  takes a **Claim** job on the tile it lands on, in the same tick.
- No hash change. Nothing about the state layout moved; a rule got stricter.
- `hand-of-evil.md` §8 and `HandRules.CanDropObjectOn`'s doc comment are
  corrected in place, with the old argument struck through rather than deleted.

## ADR-0078 — Payday is a journey, and the money comes off the tile underfoot

**Status:** accepted, 2026-09-06
**Context:** Q9, `docs/research/payday.md`, ADR-0015, ADR-0053
**Hash:** `HashVersion` and `FormatVersion` 30 → **31**, pin `0xC026542547419606`

### What was built

The last of M4's original seven items. A keeper's wage bill falls due every
`PayDayFrequencySeconds`; every creature it owns is credited `Pay` scaled by its
level; and each one then **walks to a treasury and lifts its own wages off the
floor**.

- `PaydayRules` — the interval, the first payday, the wage, the paying-room test,
  and the single-tile withdrawal.
- `PaydaySystem` — the clock and the collecting, registered after the needs
  systems and before `CreatureAngerSystem`.
- `CreatureState.WageOwed` / `PayAttemptsFailed` / `SkipNextPayment`,
  `PlayerState.NextPaydayTick`, `CreatureActivity.SeekingWages`.

### The three things worth deciding, and why they went the way they did

**1. Payday is a journey, not a transaction — and that is the original's shape,
not a flourish of ours.** DK2 gives collecting *three creature states*: one finds
the room and issues the walk, one notices the arrival, one takes the money. It
would have been far easier to credit the treasury and debit it in the same tick.
The reason not to is that the journey is where the behaviour lives: a creature
can fail to arrive, arrive to find the tile empty, or be interrupted, and each of
those is something a player sees.

**2. The gold comes off the one tile the creature is standing on.** The
original's collecting state passes the creature's own coordinates to the
withdrawal — not the room, not the player. **This is the strongest independent
confirmation this project has of ADR-0015's finding that DK2 has no player gold
counter.** Payday is precisely the moment where a global balance would be most
convenient, and the engine still reaches for a tile. A rule that was inferred
from four capacity limits a year of work ago now has a second, unrelated witness.

**3. Wages accumulate.** The accrual *adds* to the field rather than assigning
it, so a creature that misses a payday is owed two and a keeper who cannot pay
builds a debt that compounds. This is what makes payday a mechanic rather than a
tick: it is a keeper's running failure, and it is visible in the numbers before
it is visible in the creatures.

### What falls out of the data rather than out of a rule

**An imp is unpaid because its record says `pay=0`.** Nothing in `PaydayRules`
names the Imp. Its wage computes to zero, so it is never owed anything and never
goes to collect — the same shape as the Casino's fifteen states falling out of a
flag byte rather than out of a list. A special case avoided is a special case
that cannot drift.

**`SkipNextPayment` is a one-shot ticket, spent whether or not it was needed.**
Q9 had the name from Flame's symbol map and guessed it was *"a mood input rather
than an accounting flag"*. It is neither. A creature holding it is **not credited
a wage at all** and the flag is cleared in the same breath, which is exactly the
manual's account of a treat (§7.6.2.2) buying off *"wages on the next Payday"*.
Nothing sets it yet — keeper spells do not exist — so it is carried, hashed, and
written by tests only.

**The level scale is the shipped table's `Pay` row**, to **250%** at level 10.
That row has been parsed since variables were first read and looked at by nothing
(Q119); this is its first consumer.

### Divergences, each stated

- **Three states folded into one activity** (`SeekingWages`), the way
  `SeekingFood` folds the hunger walk. Same observable behaviour, shortest honest
  route.
- **The arrival tile is the room's gold-bearing tile nearest the creature.** The
  original walks to a tile and then searches that tile's *object list* for a gold
  pile, which needs an object model for gold this project does not have — gold is
  a field on the tile (ADR-0015). Ours makes the empty-tile case rarer than the
  original's; it does not remove it, because somebody else can empty the tile
  during the walk, and that is the case worth having.
- **The Dungeon Heart is admitted unconditionally.** The original tests one
  undecoded flag bit beside the type test. The wider reading, and the heart
  matters: a keeper's first treasury is bought out of the heart, so a keeper who
  has not dug a vault still has somewhere for wages to come from.
- **`NextPaydayTick == 0` means "not scheduled yet"**, and the system seeds it.
  The original seeds it in an initialiser and reads zero as *never*. Ours has
  nowhere to seed it — `WorldState.FromMap` has no `GameDatabase` — and nothing
  that needs *never*: a player slot with no creatures bills nobody. If a hero or
  front-end player ever needs suppressing it wants a flag, not this sentinel.
- **A prisoner is not on the payroll.** The original's loop walks the player's
  own creature chain and a captive has been moved off it; this project has no
  equivalent, so the skip is explicit.

### What is NOT built, and it is the important one

**Nothing charges `AngerNoPay`.** Both ends exist — the owner has described the
consequence (Q9: unhappy, then angry, then leaves the dungeon, or rebels if it
was converted) and the anger ladder is built (ADR-0053) — and the routine that
connects them **was not found in the payday path**. It is not invented here.

So an unpaid creature stays owed and stays perfectly cheerful, which is wrong.
It is wrong **visibly**: `wages_unreachable` fires twice per creature per payday
and the scenario's own header says nobody gets angry about it. The standing
hypothesis is the needs routine, in `AngerNoFood`'s shape. That is the next thing
to read, and until it is read this mechanic is half a mechanic.

**Also not read: `PayDayCutOffTimeSeconds`.** Q9 guessed it was the window a
creature has to reach the treasury. It is live — three references — but **neither
reader is in the payday path**, which is evidence against that reading. Left
UNKNOWN rather than built on.

### Consequences

- 13 tests in `PaydayTests`, and
  `scenarios/creatures/a_keeper_meets_its_wage_bill.yaml` — two keepers, one with
  gold and one without.
- **The scenario's first draft taught two things and its header keeps them.** It
  had an imp per keeper; on an open map two imps spend twelve thousand ticks
  re-claiming every tile including each other's treasuries, and since gold
  belongs to whoever owns the *tile*, the broke keeper ended the run holding the
  other's money. Correct behaviour, useless scenario. It also had no lairs, so
  every Goblin spent the run in the lair search and was never idle when payday
  fell — **a creature has to have somewhere to live before it can be paid**,
  because the collecting decision only fires on an idle creature, here and in the
  original.
- Hash 30 → 31 on the composition: three creature fields and one player field.

## ADR-0079 — Being owed is the whole trigger, and a captive is charged nothing

**Status:** accepted, 2026-09-06
**Context:** the hole ADR-0078 left open; Q9, Q185
**Hash:** unchanged. No field moved; behaviour did.

### What was found

`FUN_00478d00`, reached from the creature update immediately **before** the needs
routine, is DK2's need-anger routine. It charges all three of the standing
sources one after another and **independently**:

| Source | Condition |
|---|---|
| `AngerNoLair` (`def+0x6c1`) | no bed **and** the room type is available |
| `AngerNoFood` (`def+0x6c3`) | `HungerNeed` has reached the level-scaled `HungerFill` **and** a hatchery is available |
| **`AngerNoPay` (`def+0x6c5`)** | **`WageOwed != 0`, and the creature's own level-scaled `Pay` is non-zero** |

`def+0x6c5` was pinned the same way `Pay` was: the loader's anger block at
`0x00493964` stores five fields in a row from consecutive frame offsets, and at
the established `0x3F0` frame-to-record delta they land exactly on
`0x0F28..0x0F30` — `angerNoLair`, `angerNoFood`, `angerNoPay`, `angerNoWork`,
`angerSlap`. **A side finding worth keeping: the first four are divided by
ticks-per-second and `angerSlap` is not** — which is the per-tick/per-event
distinction ADR-0074 inferred for the prison, visible directly in the loader.

### The decision that matters, and it is a subtraction

**`AngerNoPay` has no treasury clause.** Its two siblings each test that the
*room* exists — a creature is not angered for having no bed on a map with no
lairs, nor for hunger on a map with no hatcheries. The pay charge tests
**nothing but the debt**. A keeper with no vault at all, and a keeper whose
creature simply has not walked over yet, are charged identically, from the moment
the clock runs until the money is in hand.

That asymmetry is the finding. It would have been very natural to write the
sibling rule here — *only charge if there is a treasury to have been paid from* —
and it would have been wrong. **You cannot excuse yourself by having no
treasury.** And it is why the wage accumulating (ADR-0078) matters twice over:
the debt is the anger source, and it persists.

### Two corrections to what was already built

**`CreatureAngerRules.PerSecond` is now additive.** It was an early-return chain,
which was right while there was only ever one source to return; the original
charges each independently, so a bedless unpaid creature now takes both.

**A prisoner takes no need-anger at all**, and this is a behaviour change on
work that predates payday. Until now a captive accrued `AngerNoLair` every second
like anybody else, on the reasoning that it had no bed — true, and not what DK2
does with it. **The need-anger routine refuses outright when the current state
carries bit 1 of the state record's flag byte, and all ELEVEN "being held
captive" states carry it.** A captive's anger is the prison's business alone: it
charges `AngerNoFood` per failed search for a bird and nothing else (ADR-0064,
ADR-0074), which is now consistent rather than accidentally so.

**This advances Q185.** Bit 1 of the state flag byte is *"do not charge
need-anger in this state"*, and its 43 states read as one idea: **being held
captive** (11), **stunned** (6), **joining your dungeon** (4), **fleeing** (4),
**idle** (3), **torturing** (3), **freeing comrades**, **being tortured**, **in
battle**, **responding to a threat**, **reporting danger**, **celebrating** (2
each). A creature busy with something urgent, or not properly yours yet, does not
stop to be resentful. Bits 3 and 4 were the Casino (ADR-0074); this is the third
bit named.

### What the scenario turned up, and it is now arithmetic rather than prose

`a_keeper_meets_its_wage_bill.yaml` shows **no mood change on either side**, and
the reason is worth more than the demonstration would have been: **a bed outpaces
an unpaid wage nine to one.** `ModifyAngerOfCreatureInLairPerSecond` is −550 and
a Goblin's `AngerNoPay` is 60, so a creature asleep in its own bed sleeps off
resentment far faster than being owed accrues it. **Wages alone will never drive
a well-housed creature out of a dungeon** — failing to pay has to be one of
several things going wrong, which is what the ladder is for.

The lairs that scenario needs to make its Goblins idle enough to collect are the
same lairs that keep them calm about not being paid. Pinned by
`PaydayTests.ABedOutpacesAnUnpaidWageNineToOne` rather than left in a comment,
on the standing rule that when the same fact is stated in two places one of them
should be a test.

### What is still not built

- **`AngerNoWork`** — non-workers have no job system, so "no work available" is
  a fact about this simulation rather than about the dungeon.
- **`AngerNoFood` in the general case** — still charged only by the prison. The
  original's clause needs a hatchery-availability test this has not been given.
- The routine's remaining guards: `creature[0x482]`, `creature[0x3cb]`,
  three bits of `creature[0x3e6]`, `def[0x686] & 0x4000`, and state category 22
  (fleeing, battle, threat-response, celebrating). None is modelled; each is a
  narrowing that would only ever charge *less*.

### Consequences

- Four tests in `PaydayTests`; 1083 green.
- **The tests caught their own first draft.** All four ran the full ten minutes
  to a real payday, and every one failed the same way: a Goblin with no bed takes
  `AngerNoLair` at 100 a second, so after 600 seconds it is pinned at the 32767
  ceiling and has already drawn Leave from its angry job table. They were
  measuring a creature that had deserted. **Anger is measured over seconds, not
  over paydays.**

## ADR-0080 — A Flame name may be cited in a document, and never appears in code

**Status:** accepted, 2026-09-06 (owner decision)
**Context:** the boundary §2.2.1 of the plan left *"flagged rather than decided"*;
the pre-existing occurrences the keeper-AI handoff found; ADR-0003, ADR-0041
**Hash:** unchanged. No code moved; two doc comments were reworded.

### What was found

The keeper-AI merge swept the tree for Flame identifiers and reported the source
tree clean and the planning document not: `DK2_REIMPLEMENTATION_PLAN.md` names
Flame's class for the computer player at three places. That report was
correct as far as it looked, and it did not look far enough. Widening the same
sweep to the whole repository found:

| Where | What | Count |
|---|---|---|
| `DK2_REIMPLEMENTATION_PLAN.md` | Flame class and global names, each cited as Flame's | about a dozen, in eighteen lines |
| `docs/DECISIONS.md` ADR-0001 | a Flame header file name | 1 |
| `docs/research/open-questions.md` Q4 | a Flame class name | 1 |
| `docs/research/external-resources.md` | a Flame global, quoted *as* the example of what not to adopt | 1 |
| `src/DK2.Core/GameTick.cs`, `TilePos.cs` | **two Flame field names, prefix and all, in `<summary>` comments** | 2 |
| `THIRD-PARTY-NOTICES.md` | the sentence *"no Flame identifier appears anywhere in this repository"* | false since revision 1 of the plan |

Three of these are the same fact: the project had a rule it stated in absolute
terms, in three documents, and nothing checked it. **A claim nothing checks may
already be false**, and this one was, in a source file, for the whole life of the
repository.

### Why the rule as written was stricter than the law and looser than the practice

Copyright protects expression. It does not protect names, titles or short
phrases, and in the EU the Court of Justice
held in *SAS Institute v World Programming* (C-406/10, 2012) that a program's
functionality, its language and its data formats are not protected expression
either. `MyComputerPlayer` is a name. Writing *"Flame calls this class
`MyComputerPlayer`"* in a planning document is a citation with attribution, the
same act as naming the chapter of a book one did not license. Flame publishing
no licence means all rights reserved on its **code**; it does not give its author
a claim over people saying what the code is called.

Where risk begins is where a reference becomes a copy: lifting Flame's headers,
its comments, or its **set** of field names and layouts into C#, which could be
argued as taking the selection and arrangement of the work. That argument is
weak — the offsets are dictated by the DK2 binary, not by DiaLight — but it is
the only argument there is, and the one place it could be made is the source
tree.

So the two halves of the old rule had different strengths. *"Never in a
document"* forbade something lawful and useful, and the documents ignored it a
dozen times. *"Never in code"* is the half that actually removes the exposure,
and it was the half nobody was checking.

### The decision

1. **In documents, a Flame identifier may be named as a citation.** The plan,
   the ADRs, the research notes and the handoff may write *"Flame calls this X"*
   provided the sentence makes Flame the source. This is what those documents
   already do, and it is now permitted rather than tolerated.
2. **In the source tree and its tests, never — not as a member, not as a type,
   not in a comment, not in a string.** Every name in `src/`, `tests/` and the
   Unity adapter is this project's own choice of words. Where a comment needs to
   say what Flame established, it says so in this project's words: *"Flame's
   room record stamps the game tick the disco started on"*, not the field.
3. **Offsets, sizes, vtable slots and decoded values are facts about DK2** and
   are used freely, with provenance, as before (ADR-0003, `external-resources.md`).
4. **No Flame code, comment, header layout or file is ever copied**, in whole or
   in part, anywhere. Unchanged.
5. **The commit-message clause is dropped.** A commit message is a document.

`THIRD-PARTY-NOTICES.md` now says what is true: Flame is consulted and cited, and
none of its names are adopted into code.

### What enforces it

`FlameIdentifierTests` in `DK2.Core.Tests` sweeps the same files
`LicenseHeaderTests` does and fails on Flame's field-naming style — a lower-case
`f`, two to four hex digits, an underscore — and on its `sData_` serialisation
prefix. The test deliberately matches the **shape** of Flame's names rather
than a list of them, because a denylist of Flame identifiers would itself be
Flame identifiers pasted into the repository. It is a floor, not a fence: a
Flame class name typed into a comment would slip through it. The rule still
rests on review; the test is there so that the class of mistake
found today cannot recur silently.

### What this does not decide

Whether Flame's `mapping/*.map` files may be vendored as research data (§10.1
decision 3) is still open and still recommended against — the clone at
`D:\Flame` is outside the repository on purpose, and nothing here changes that.

**And it does not move Flame up the order of sources.** The owner restated the
hierarchy the same day, in so many words: *the primary source of truth is Ghidra
and its decompiled sources; Flame is there as a reference, a secondary source of
truth, to help determine whether the findings hold. That's it.* Citing a Flame
name in a document is permitted because a name is not a copyrightable work, not
because Flame is authoritative. A finding is made from the binary and checked
against Flame, never the other way round (AGENTS.md "Existing research",
ADR-0046 amendment).

## ADR-0081 — Anger is six counters and the mood is the largest, not the sum

**Status:** accepted, 2026-09-06
**Context:** found while reading the job choosers for M4's last item; ADR-0050,
ADR-0053, ADR-0079; Q106, Q114, Q185, Q202 (new)
**Hash:** 31 → **32**. One `int` on the creature became six.

### What was found

`FUN_00487030` is DK2's anger-add, reached through the wrapper `FUN_00478b80`
that every one of the sixty anger sites in the executable calls. Its arguments
are **(bucket index, amount, message id)** and its receiver is an `int[8]` living
at `creature+0x26e`:

- `[0..5]` are **six independent anger counters**.
- `[6]` is the creature's unhappy threshold.
- `[7]` is the mood band it writes back.

The routine adds the amount to **one** counter, clamps that counter to
`[0, 0xFFFD]`, then walks all six to find the **maximum** and bands *that one
value*: below `[6]` is content, above `0x7FFF` is angry, between is unhappy.
**Nothing anywhere sums them.**

### The decision, and it is a correction to three years of this file's own work

This simulation kept one pooled `CreatureState.Anger`. That is wrong in both
directions at once, and neither error is small:

- **Two half-grievances do not add up to desertion.** A creature at 20 000
  hungry and 20 000 unpaid is *unhappy* in DK2 and was *angry* here.
- **A kindness in one bucket does not pay off a grievance in another.** This is
  the one that matters, because it retires a finding from the day before.
  **ADR-0079 recorded that a bed outpaces an unpaid wage nine to one** — 550 a
  second of `ModifyAngerOfCreatureInLairPerSecond` against a Goblin's 60 of
  `AngerNoPay` — and concluded that wages alone could never drive a well-housed
  creature out of a dungeon. Both numbers were right. **The conclusion was
  wrong**: the bed drains bucket 5 and the wage fills bucket 4, and the maximum
  never sees the trade. A keeper who does not pay loses its creatures however
  comfortable it has made them.

That second correction is the more interesting one, because the old model made
the game *easier* in a way no player would recognise: it let a keeper buy off
every grievance with the cheapest one to fix.

### Four of the six are certain, and two names are ours (Q202)

Exactly one creature-record field feeds each of four buckets, and the field names
them: `angerNoFood` → **1**, `angerNoLair` → **2**, `angerNoWork` → **3**,
`angerNoPay` → **4**. Those four are the needs, and they are precisely the four
the need-anger routine of ADR-0079 charges — so the taxonomy and the routine
agree without being told to.

The other two are catch-alls and the division between them is **UNKNOWN**. What
is read is which sources reach which:

| Bucket | Reached by |
|---|---|
| **5**, called `Treatment` here | `angerSlap`, `angerInHand`, `ModifyAngerOfCreatureInLairPerSecond`, `ModifyAngerOfCombatPitVictor`, both `Gambling…Anger…PerSecond`, and the torture sites |
| **0**, called `Company` here | `ModifyAngerInCompanyOfHatedCreaturesPerSecond`, a share of the casino and of pit spectating, and — not fitting the name — `angerNoWork` at one site inside the combat pit |

Bucket 5 is coherent as *what is done to you, and resting it off*: slapping,
carrying, torture and the pit fill it, and a bed drains it. That makes the
"sleep it off" mechanic specifically the answer to **slapping**, which is the
loop a DK2 player actually plays. Bucket 0 is less coherent and the name is
flagged as ours, on the same footing `Unk8` → `IsGood` was.

**The Temple reaches all six.** `ModifyCreatureAngerWhilePrayingPerSecond` (−275)
is applied in six consecutive calls, one per bucket. Praying is the only thing in
the game that takes back a grievance it did not cause — which is what a Temple is
*for*, and is now a mechanical fact rather than a flavour one.

### Two more numbers, one new and one unchanged

**The counters saturate at 65 533, not at the angry band.** Each clamps to
`0xFFFD` and only the maximum is compared against `0x7FFF`, so a grievance keeps
climbing for a second full ceiling's worth after the creature has already turned
angry. A long-neglected creature therefore does not calm the moment a keeper does
one thing right, and `CreatureAngerRules.Saturation` is now distinct from
`Ceiling`.

**Nothing about the owner's stopwatch moves.** Q106 measured the *band* a
creature crosses — 16 383 unhappy, 32 767 angry — and that is still exactly what
`Ceiling` and `UnhappyAt` express. The saturation above it is invisible to any
stopwatch, which is why five sittings of measurement could not have found it and
the binary could.

The one hair-splitting change: angry is `> 32767` where this was `>= 32767`, from
the routine's own `CMP`/`JLE`. Below the resolution of any observation, and free.

### Consequences

- `CreatureAnger` (new), `AngerBucket` (new); `CreatureAngerRules.Apply` takes a
  bucket; `PerSecond` returns per-bucket deltas; `WorldSnapshot` writes six.
- **Three tests changed their claim rather than their spelling**, and each is
  worth naming because each had been asserting something DK2 does not do:
  `ABedOutpacesAnUnpaidWageNineToOne` is replaced by
  `ABedDoesNotPayOffAWageBecauseTheyAreDifferentCounters`;
  `ABedFixesItInInstalmentsAndNotAtOnce` no longer ends Content, because the
  Lair bucket is frozen where the bed found it and not one point of it is
  repaid; and the Vampire that draws Rebel is now *crosser* after thirty
  seconds than it was, where before it sat pinned at the band.
- **The hand tests got simpler, which is a good sign.** Two of them existed to
  cancel out the lairless ladder that a held creature was climbing at the same
  time as the hand's own delta — one of them by spawning a twin Goblin to
  subtract. The two effects are different counters now, so the arithmetic is
  gone and the twin stays only to demonstrate the separation.
- 1092 green, 25 scenarios, zero warnings. Pin `0x30722F6054924CC5`.
- **This is the fourth time a claim nothing checked turned out already false**,
  and it had the standing shape: a single number that every mechanic wrote into,
  never questioned because it had been right for the one mechanic that existed
  when it was written.

## ADR-0082 — A creature that is not a worker now has a day, and M4 is closed

**Status:** accepted, 2026-09-06
**Context:** M4's seventh and last item; Q2 (closed), ADR-0061, ADR-0081
**Hash:** 32 → **33**. `CurrentJob`, `JobTileIndex`, two choice timestamps and
the stored mood band.

### What was found

`docs/research/job-selection.md` has the read in full. The shape:

**Three choosers, one per mood** — happy `FUN_004780e0`, unhappy `FUN_004782c0`,
angry `FUN_004784c0` (built since ADR-0061). Each gates on **five game seconds**
against **its own timestamp** — the creature carries three side by side — builds
cumulative ranges from that mood's table, rolls `rand(100)`, tries the job whose
range holds the roll, and on failure tries every job in the table in record
order. A failed selection stamps nothing.

**One dispatcher, `FUN_00478800`** — a 28-way switch to that job's handler, and
every handler is *(a cheap test on the keeper or creature) → (find the nearest
room) → (enter the job's state, which can still fail)*. **That boolean is the
whole of job availability**, which closes the clause Q2 had open since 2026-09-05:
what makes Research or Train *fail* so the list falls through.

### Three findings worth the sitting on their own

**Only the happy chooser charges `AngerNoWork`.** Its tail, when every job has
failed, charges `def+0x6c7`; the unhappy chooser's identical tail is a bare
return, and so is the angry one's. **A creature that is already unhappy is never
made crosser by idleness** — the grievance belongs to the creature that was fine
until its keeper ran out of work for it. The same shape as ADR-0079's finding
that `AngerNoPay` has no treasury clause where both its siblings test for the
room. Twice now: a plausible symmetry the game does not have, and both times the
symmetric version would have been the natural thing to write.

**Sulking is going to your lair and moping in it.** `FUN_00482ef0` is the Sleep
handler with one digit changed — same three preconditions, same room, state
`0x55` where sleep uses `0x51`. **A creature with no bed cannot sulk at all**,
and every unhappy table in the shipped game is Sulk and Drink.

**Seven more dead balance variables, and this time a whole family.**
`MotionlessWhileResearching/Manufacturing/Praying/TrainingSeconds`,
`StateCounterWhileGuarding`, `CasinoModifyCreatureGoldPerSecond` and
`MinimumJobDurationSeconds` are each written by the loader stub and read by
nothing — taking the count from five to twelve. The last is a cross-check
rather than a curiosity: all three choosers gate on a hardcoded `5 × variable
19` while the shipped variable that looks like it should govern job length sits
unread, which is **evidence that there is no job timer**, not merely an absence
of one.

### The one job with an effect, and why it is that one

Praying. Since ADR-0081 the Temple is **the only source in DK2 that drains an
anger bucket it did not fill** — every other calm in the game reaches bucket 5
alone, so a month of unpaid wages in bucket 4 has exactly one cure. `-275` from
all six buckets and `+33` health, once a game second, both numbers verified live
against the seven dead ones beside them.

Research, Train, Guard and Drink are chosen, walked to and stood in, and logged
`effect=unbuilt`. That is not a stub: **every second a creature spends at a job
is a second it is not idle and not taking `AngerNoWork`**, which is the half of
each job the choosers actually depend on. What is missing is production, and
training's rate carries the same per-second/per-minute ambiguity that made
`ExpPerSecond` turn out to be per minute — so it is left for its own read (Q205)
rather than guessed.

### A correction that fell out of testing it

**The mood band is now stored on the creature**, as DK2 stores it in slot 7 of
its anger object and rewrites it on every add. Anger reaches a creature from
four places and three of them run before `CreatureAngerSystem` in the same tick
— a slap, the prison, and now having no work — so a "mood before" worked out
from the anger visible at the top of that system's own pass **missed every
crossing it did not cause itself**. The scenario found it: two Goblins went from
content to unhappy on pure `AngerNoWork` and nothing said so. A pre-existing gap
that slapping shared, and it is closed.

### And the determinism pin cannot see any of this (Q206)

Discovered while checking that the new fields moved the hash: **they do not**.
`TestWorld.World` spawns no creatures at all, so every creature-state change
this project has made has moved the pinned value through `HashVersion` alone and
never through the field itself. The pin still catches what it was built for —
struct layout drift, dictionary order, string hashing — but for a simulation
whose state is mostly creatures it is guarding a world that has none. Several of
the hash-history notes in `DeterminismTests` describe creatures the fixture does
not have. Recorded rather than fixed here: widening it is a change to the guard
itself and deserves its own sitting.

### Consequences

- **M4 is seven of seven.** Portal spawn, lair claim, hunger→hatchery, sleep,
  payday, job selection with logged reasons — and training, which is the one
  item that is *selected* but not yet *worth* anything.
- 19 tests in `CreatureJobTests`, 1111 green, 26 scenarios, zero warnings. Pin
  `0x21A7E4D24F37DF48`.
- `two_keepers_find_work_for_a_goblin.yaml`, whose log is the mechanic in five
  lines: one keeper's Goblins take Train and go; the other's are refused, take
  50 a second, cross to Unhappy at 5m28s with `worst=Work` naming the bucket,
  and are then **never charged again** because the unhappy chooser does not
  charge it.
- **Its first draft inverted its own demonstration.** The Vampire was placed to
  be the third creature for two beds, and it was nearer the lairs than the
  Goblins were — so it got a bed and one of them did not. Correct behaviour
  (nothing is reserved, ADR-0060) and a useless run. The Goblins now start
  standing on the beds. Third time a scenario's setup has hidden the thing it
  was built to show.

---

## ADR-0083 — Training pays, and the experience model underneath it was wrong in three places

**Date:** 2026-09-07
**Status:** accepted
**Questions:** Q205 (answered), Q79 (answered), Q127 (both readings corrected),
Q124 (one clause promoted from choice to fact), Q186 (advanced), Q203 (two more)
**Evidence:** `docs/research/training.md`

### Context

ADR-0082 gave a non-worker a day: a Goblin rolls Train, walks to the training
room and stands in it. **What that was worth was unread**, and it was the one
loose thread left in M4.

The rate had been sitting in the creature record under the name
`ExpPerSecondTraining` and was not used because the name could not be trusted.
The neighbouring `ExpPerSecond` reads **1200** for an Imp against a balance-table
`ImpExperienceGainPerSecond` of **20**; 20 × 60 = 1200; and this project had
written that down as **STRONGLY INFERRED evidence that the record's rates are per
minute** — two independent fields agreeing on one creature's rate.

### Decision

Build training, and correct the experience model it landed on.

**1. The rate is per second and the loader is what says so.** DK2's creature
loader stores `ExpPerSecondTraining` as `file value / variable 19`, flooring at 1
when the source is non-zero. Three siblings go through the same divide —
`ExpPerSecond`, `ResearchPerSecond`, `ManufacturePerSecond` — and their immediate
neighbour `ExpForNextLevel` is stored **raw**, which is the tell: a threshold is
not a rate. `SimulationTiming.LoaderRatePerTick` is now the one definition of
that conversion, and `PrisonRules` uses it instead of its own copy. The known
membership of that loader idiom goes from six fields to ten.

**2. The 1200 was a coincidence between two unrelated mechanics.**
`ExpPerSecond` belongs to state **category 22** — battle, fleeing,
threat-response — so it is **combat** experience, which is the owner's own *"to
get to level 10 they need to fight actual fights"* (Q124). The per-minute reading
is withdrawn. Third instance in this project of two correct numbers that never
meet.

**3. The payment hangs off the state's category, not the state.** None of the six
training states awards anything. The 32 category records at `0x00699688` each
carry an enter hook at `+0x04`, an update hook at `+0x14` and a leave hook at
`+0x24`, and the per-tick dispatcher calls the current category's update hook
before the state's own handler. Category 13 is training; its update hook is the
payment. All 32 records are now decoded (Q186).

**4. Training bills the keeper five gold a game second, and poverty ends the
job.** `ModifyPlayerColdWhileTrainingPerSecond` is −5 and "Cold" is the shipped
spelling of gold. The original tests the keeper's gold before entering the state
and before every charge, and returning failure from a category update hook makes
the dispatcher put the creature back to idle. **Training is the only job in DK2 a
keeper can be evicted from by being poor.** `JobRules.Refused` now makes that
test too, and *before* the level test, because that is the order in the handler.

**5. `CreatureLevelRules.TryLevelUp` is replaced by `Grant`, and Q127's two
readings were both wrong.** DK2 promotes **once** and **zeroes** the balance —
the overshoot is discarded, not carried, and a large award cannot buy two levels.
A negative award larger than the balance is a **no-op**, not a clamp. A
`ExpForNextLevel` of zero refuses everything, losses included.

**6. Q79: imp experience is per tick of work, not per job.** The owner leaned
*"per job done"* and named exactly the right activities.
`ImpExperienceGainPerSecond` is read at nine sites, all in the imp's task unit,
all awarding the **raw 20** with no divide, each guarded by a predicate that
decompiles to *"is this tile still a valid target"* rather than *"the job
finished"*. It is **the one rate in the family that is not divided by variable
19**, so its name is wrong by a factor of four in the opposite direction to
everything around it: **80 a game second**, and an imp reaches level 2 in about
**37 seconds** of work rather than the 150 this project had asserted in a test
name for three weeks.

**7. Both awards are paid on the original's tick, not once a game second.**
`SimulationTiming.OnOriginalTick`. Not a flourish — because the promotion
**zeroes** the balance, four awards of ten and one award of forty leave different
numbers behind on any second that crosses a threshold. The cadence is behaviour.

### Consequences

- **`TrainingRules`** and the `Train` branch of `CreatureJobSystem.Working`.
  `JobRules.EffectIsBuilt` now names two jobs.
- **The truncation is reproduced, not rounded away.** 30 / 4 = 7 gives 28 a
  second, and **26 of the 46 shipped creatures lose something** — the heroes'
  lords and Horny lose a fifth. So does the **floor**, which is reachable: the
  Skeleton's `ResearchPerSecond` of 1 and the Dark Angel's 3 both become 4 a
  second.
- **A fixture drift, caught by needing the number.** The fixture Goblin had been
  inheriting the Imp's 30 and 1200; its own are 40 and 200.
- **One more inert balance variable (Q203), and it is a new KIND of inert.**
  `EPLossSecondWhileNotTraining` **has a real reader and ships as zero**: a
  per-second hook that drains experience from any creature whose state category
  is not 13. DK2 was built to make experience decay unless you kept your
  creatures training, and the feature was **turned off rather than cut** — which
  is worth distinguishing from the twelve that have no reader at all, because
  the mechanism is intact and a mod that sets the rate would work.
  (`MotionlessWhileTrainingSeconds` is dead too and this dig re-confirmed it,
  but ADR-0082 had already found it; it is not a new one.)
- **41 of 46 creatures have `ExpPerSecond` exactly 5 × `ExpPerSecondTraining`**,
  which argues the two are the same kind of quantity without reading any code.
  The exceptions are three uniques whose combat rate was raised without the
  training column following, the Maiden Of The Nest, and **the Imp at forty
  times** — the one creature that neither trains nor fights, and the source of
  the number that misled us.
- The scenario now shows the whole arc: two Goblins train, both reach level 2,
  the treasury empties at about t2500 and both lose the job on the spot, and
  2500 ticks later they are the same unhappy Goblin the keeper who built no room
  has. **Two keepers arrive at the same creature by different routes.** Player 4
  had to be *given* gold for its refusal to still say `no_room`.
- **1130 tests**, 26 scenarios. `HashVersion` unchanged at 33 — no hashed field
  changed shape — but hashed *values* move wherever experience is earned.

### What is ours, and flagged

- A capped creature keeps the job for up to one five-second gate instead of being
  ejected by a state handler, because we have one working state where DK2 has
  six.
- The gold comes out of the keeper's tiles rather than the tile underfoot.
- No floating "−20", no message `0x37`, no combat-pit fallback for a creature
  that has outgrown the training room.
- **Q79's factor of four rests on `tile-work.md`'s per-tick accrual** rather than
  on a caller read. The probe is named in the research doc: find what iterates
  the imp task table at `0x006E4198`.
