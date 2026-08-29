# Architecture

Full rationale: `DK2_REIMPLEMENTATION_PLAN.md` §4. This file is the working summary.

## Layering (dependencies point downward only)

```
DK2.Tools / DK2.Harness            net10.0 executables
        │
DK2.AI ──────► DK2.Simulation ───► DK2.Pathfinding
        │              │                  │
        └──────► DK2.Data.Model ◄── DK2.Data (parsers)
                       │
                   DK2.Core                netstandard2.1 (Unity-loadable)
```

- `DK2.Core` … `DK2.Assets` target **netstandard2.1** (`DK2UnityBound` in
  Directory.Build.props) so Unity can load them directly.
- **No simulation assembly may reference Unity.** Enforced by
  `DK2.Simulation.Tests.ArchitectureTests` — the build fails if violated.
- Tools/harness/tests target net10.0.

## Simulation rules

1. Time advances only through `GameSimulation.Tick()`; `GameTick` is integer.
2. All randomness comes from `SimRandom` (seeded, serialisable). `System.Random`
   and `Guid.NewGuid()` are forbidden in simulation code.
3. All gameplay-affecting numbers come from the parsed KWD database
   (`DK2.Data.Model`). A literal balance constant in simulation code is a bug.
4. Gameplay math is integer/fixed-point. `float` is presentation-only (ADR-0001).
5. Every implemented DK2 mechanic carries a `[Dk2Behavior]` attribute with a
   confidence level and a pointer to its research note in `docs/research/`.

## Unity (arrives at milestone M3)

Unity project lives under `unity/`, consumes the netstandard2.1 assemblies,
renders `SimulationEvent`s and submits `InputFrame`s. It owns no game state.

## Determinism (M2)

The load-bearing contract: **same database + same initial world + same seed +
same input frames produce the same `GameSimulation.StateHash()` at every tick,
on any machine.** Enforced by `DK2.Simulation.Tests.DeterminismTests` and by the
CI gate, which runs it on Linux and Windows.

- `Tick(in InputFrame)` is the only way time moves; systems run in explicit
  registration order.
- The RNG's position is part of the state hash — two runs that look identical
  but have consumed different draws are about to diverge.
- `Hash64` is FNV-1a. `GetHashCode` is forbidden in the hash path (.NET
  randomises string hashing per process).
- One pinned hash literal guards cross-machine stability. Changing simulation
  state deliberately means bumping `GameSimulation.HashVersion` and updating the
  literal in the same commit.
- Events are write-only: attaching a recorder must not change behaviour.
- `WorldSnapshot` round-trips are verified by continuing both runs for hundreds
  of ticks, not by comparing bytes.

Full rationale: `docs/architecture/simulation-loop.md`.

## Scenarios (M2)

`scenarios/**.yaml` are declarative experiments, run headlessly by
`DK2.Harness`. The runner reports every part of a scenario it could not honour
— unknown keys, unapplied setup, observations nothing produces yet — so a run
can never look more complete than it is. See `scenarios/README.md`.

## Tile mechanics (M3)

The first real gameplay. Digging, mining, claiming, reinforcing, repairing,
attacking and filling in are one uniform, entirely data-driven rule in
`DK2.Simulation.TileWorkRules`; the numbers come from the terrain table and the
ten health-delta variables, and none of them appears in the code.

- `TileWorkRules` is pure and static: no world, no RNG, no time. It answers
  "does this kind of work apply to this tile" and "what does one application
  do", and nothing else.
- `TileWorkSystem` drains a FIFO queue of `TileWorkOrder`s on the world. Order
  is behaviour, so the queue is never sorted or deduplicated.
- `TileTagSystem` applies `TagTilesCommand` — the first real player command.
  Markers are per player, one bit each, and are cleared when a tile converts
  into terrain that cannot carry one (so a gem face, which collapses into
  itself, keeps its mark).
- The rules are written against terrain **flags**, never terrain ids. A rule
  keyed on id 2 would be a rule about the reference install rather than DK2.

Gold released by mining is reported and dropped rather than credited, because
the treasury it would go to does not exist yet.

Timing is the open wound: the health deltas carry no unit (Q19, tied to Q1), so
tick counts are not seconds. Everything that relates ticks to seconds lives in
`SimulationTiming` and nowhere else.

Evidence, confidence and the three anomalies the data throws up:
`docs/research/tile-work.md`.

## Workers (M3)

The producer the work queue was written to wait for. Anything flagged
`IsWorker` — the Imp and the Dwarf, never creature id 1 — looks for the nearest
tile its keeper has marked, walks to it, and works on it.

- `CreatureState` is the first mutable creature state: position in fixed point,
  activity, target, timer and route. Only fields a mechanic actually reads are
  there; health, mood and hunger arrive with the mechanics that read them.
- `ImpTaskSystem` decides. One outward search from the worker answers "which job
  is nearest", "how do I get there" and "is anything reachable at all" at once.
  It queues `TileWorkOrder`s; it never applies them.
- `CreatureMovementSystem` walks. Speed is read as tiles per second
  (`docs/research/imp-tasks.md`), spent per tick, with the remainder carried so
  nothing drifts slow.
- `DK2.Pathfinding` holds the search behind `IPathfinder`. It knows nothing of
  terrain or creatures: `WorldPathGrid` decides what "walkable" means for one
  particular mover, from terrain flags and that creature's own flags.
- `SimulationSystems.Default()` is the one declaration of system order (ADR-0013).

Two things are deliberately absent, and both are reported by every scenario run
that places a worker. **Workers only do marked work** — claiming, reinforcing,
carrying gold and fleeing are real imp jobs whose trigger and priority order are
UNKNOWN (Q24, ADR-0014). And **routing is ours, not DK2's** (Q5, ADR-0012):
four-connected, uniform cost, deterministically tie-broken, and an admitted
placeholder behind an interface.

Nothing generates workers either. DK2 makes imps from a dungeon heart, from a
spell at a mana cost, and from specials; `WorldState.SpawnCreature` is what tests
and scenario files use in the meantime, and it says so.

Evidence and confidence: `docs/research/imp-tasks.md`.

## Rooms and gold (M3)

The second player command, and the first resource that is somewhere rather than
just a number.

- `RoomRules` says where a room may go, what it costs and what it does to the
  tile. Where it may go is read from the room's own placeable-on flags, so no
  code knows what a bridge is (ADR-0016).
- `BuildRoomCommand` + `RoomBuildSystem` are the command path, alongside
  `TagTilesCommand` + `TileTagSystem`. A tile at a time, in the order the command
  gives, with every refusal counted by reason in the event log.
- `GoldStore` is the only way gold moves. **A player has no gold counter**:
  `TileState.GoldHeld` is where gold is, and a keeper's total is the sum over the
  tiles they own (ADR-0015). Every limit DK2 sets on gold is per tile, which is
  what forced the shape.

Three behaviours fall out rather than being written: a keeper with no treasury
cannot keep gold, destroying a treasury takes the gold with it, and a keeper's
first treasury is necessarily paid for out of the dungeon heart.

What is deliberately absent: **a room does nothing.** A treasury and a heart hold
gold because a variable says how much per tile; every other room is inert terrain
its owner holds. Rooms have no size or shape rules (Q36), cannot be sold (Q34),
and are built instantly — building over time is another worker job with the same
unknown trigger as the rest (Q24). And gold released by mining still reaches
nobody, because nothing carries it (Q31).

Evidence and confidence: `docs/research/rooms-and-gold.md`.

## Mana (M3)

The first thing in the simulation that changes because time passed, rather than
because a player or a worker did something.

- `ManaRules` is the whole model: a keeper's income is the sum of
  `TerrainDef.ManaGain` over the tiles they own, plus a flat 30 if they have a
  dungeon heart, capped by `MaximumManaGainPerSecond`. `MaximumManaThreshold` is
  a flat 200000 ceiling for every keeper (CONFIRMED by observation), soft rather
  than a clamp: mana above it drains back.
- The heart's 30 is counted **once per keeper, not once per heart tile**, and
  that is exact rather than a simplification — a keeper has at most one dungeon
  heart, ever, and losing it is defeat. So no room-instance concept is needed to
  count hearts, which was the thing blocking it.
- `ManaSystem` pays it once a game second (ADR-0017) and runs last, after
  everything that could change what a keeper owns.
- Mana stays a player-level number, unlike gold. The asymmetry is the data's:
  every limit DK2 sets on gold is per place, and every variable it sets on mana
  is per player.

**Mana income is territory**, and no code says so. Three terrain types out of 41
pay anything, and they are exactly the ownable non-room ones — so claiming ground
raises income, losing it lowers it, and building a room on your own floor takes
that floor off the payroll, all without a rule about claiming, losing or
building. `scenarios/mana/land_pays_a_keeper.yaml` shows the last of those: the
rate falls from 38/s to 35/s when three of eight claimed tiles become a treasury.

What is deliberately absent: **nothing spends mana** (every spender is a spell,
M7), and **nothing happens when a heart falls** — losing it is defeat in DK2, and
defeat is a win-conditions mechanic that belongs with objectives at M6 (Q40).

Evidence and confidence: `docs/research/mana.md`.
