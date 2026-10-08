# DK2.Harness

The headless scenario runner (an `Exe` on `net10.0`, not loaded by Unity). It loads
YAML scenarios (`Scenario.cs`), builds a world, runs the simulation and any computer
keepers, checks `expect` claims, and writes a report per run.

    dotnet run --project src/DK2.Harness -- list [dir]
    dotnet run --project src/DK2.Harness -- run <file|dir> [--out <dir>]

Exit code 1 means a scenario failed to load or run, or an expectation failed. The
file format, output files and corpus are in `scenarios/README.md`. Campaign maps need
`DK2_DIR`.

Depends on `DK2.Core`, `DK2.Data`, `DK2.Data.Model`, `DK2.Simulation`, `DK2.AI`,
`DK2.Assets`, `DK2.Presentation` and YamlDotNet.

Test: `dotnet test tests/DK2.Behavior.Tests` (references the harness).
