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

**Status:** accepted (2026-08-29, M3)

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
