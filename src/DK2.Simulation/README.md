# DK2.Simulation

The game rules, independent of Unity. `WorldState` holds everything that changes
(`WorldState.FromMap` builds one from a loaded `LevelMap`); `GameSimulation` owns a
world and advances it: `Tick(InputFrame)` applies the player's commands, then runs
each `ISimulationSystem` in order. `SimulationSystems.Default()` is the standard
system list (under `Systems/`). Rule logic lives in `*Rules.cs` and `*Step.cs`;
`ThingProjection` places a level's decoded THINGS; `SimEvent` and its sink record
what happened. State is hashed (`Hash64`) every tick.

Targets `netstandard2.1`. Depends on `DK2.Core`, `DK2.Data.Model` and
`DK2.Pathfinding`. It reads no files; the data it needs arrives as a `GameDatabase`.

Test: `dotnet test tests/DK2.Simulation.Tests`. Behaviour against the original
install is covered by `tests/DK2.Behavior.Tests`, and the scenarios in `scenarios/`
run through `DK2.Harness`.
