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
