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

So the remaining Unity work is *one class implementing six methods*, and the
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

The clock runs and nothing can stop it, because eating needs chickens, chickens
are objects, and OBJECTS is unparsed (M6). **This costs no health on purpose:**
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

### What the binary could not say

`HungerState.cpp` and `LairState.cpp` are both named in the executable's own
assert strings, and four different approaches failed to reach their code: the
creature state machine is dispatched entirely through vtables, so Ghidra's
analysis never discovers those function bodies, and forcing them did not work
either. The method is written down in `creature-needs.md` §6 so the next attempt
starts further along, and the promising route is **reading the vtables**, which
would name all ~25 state handlers at once. **Nothing in this ADR rests on the
binary.**

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
A corpse has none of that: in DK2 it is an *object* with a `DecomposeValue` that
imps haul and a graveyard counts toward `DecomposeValueNeededForVampire`. OBJECTS
is unparsed (M6). Modelling it as a fake creature would put a thing with no needs
into the creature list, where every present and future system would have to
remember to skip it.

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
  rescued at all (Q107), which costs nothing while carrying does not exist. The
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

**Only two of DK2's anger sources are applied, and the rule is one sentence: a
source is applied when the player's own choices can turn it off.**

| Source | Applied? |
|---|---|
| `AngerNoLair` +100 | **Yes** — lairs are built, sold and filled |
| `ModifyAngerOfCreatureInLairPerSecond` −550 | **Yes** — sleep works, so recovery is real and 5.5× faster than the accrual |
| `AngerNoFood` +100 | No — chickens are objects (M6); *no* dungeon can feed anybody |
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
