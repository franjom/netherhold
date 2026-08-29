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
  Markers are per player, one bit each, and are cleared when a tile converts.
- The rules are written against terrain **flags**, never terrain ids. A rule
  keyed on id 2 would be a rule about the reference install rather than DK2.

Two things are deliberately absent. Nothing decides *who* works — that needs job
selection (Q2) and pathfinding (Q5) — so the work queue is filled by tests and
by the scenario harness, which says so in every report. And gold released by
mining is reported and dropped rather than credited, because the imp that would
carry it and the treasury it would carry it to do not exist yet.

Timing is the open wound: the health deltas carry no unit (Q19, tied to Q1), so
tick counts are not seconds. Everything that relates ticks to seconds lives in
`SimulationTiming` and nowhere else.

Evidence, confidence and the three anomalies the data throws up:
`docs/research/tile-work.md`.
