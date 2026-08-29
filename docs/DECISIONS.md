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
