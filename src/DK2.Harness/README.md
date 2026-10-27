# DK2.Harness

The headless scenario runner (an `Exe` on `net10.0`, not loaded by Unity). It loads
YAML scenarios (`Scenario.cs`), builds a world, runs the simulation and any computer
keepers, checks `expect` claims, and writes a report per run.

    dotnet run --project src/DK2.Harness -- list [dir]
    dotnet run --project src/DK2.Harness -- run <file|dir> [--out <dir>]

A run can also take test controllers (`ScenarioRunner.Run(scenario, controllers)`): a
`ScenarioController` is a player, a decision interval and a function from `KeeperView` to
commands, and its commands join the next tick's input after the computer keepers'. Controllers
are usable from code and tests only; there is no CLI verb. See ADR-0552 and
`docs/plans/2026-10-27-autonomous-playtesting.md`.

Exit code 1 means a scenario failed to load or run, or an expectation failed. The
file format, output files and corpus are in `scenarios/README.md`. Campaign maps need
`DK2_DIR`.

Depends on `DK2.Core`, `DK2.Data`, `DK2.Data.Model`, `DK2.Simulation`, `DK2.AI`,
`DK2.Assets`, `DK2.Presentation` and YamlDotNet.

Test: `dotnet test tests/DK2.Behavior.Tests` (references the harness).
