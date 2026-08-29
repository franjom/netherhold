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
