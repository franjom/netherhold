# DK2.Harness

Each run writes `timings.txt` beside its deterministic report, with tick percentiles,
marker times, allocations and the worst tick after two warm-up ticks. The harness hashes
while computer keepers think (ADR-0578); `state hash wait` is the remaining join time.
`RunOptions.OverlapStateHash = false` measures hashing serially. Allocations count the run
thread and exclude pool-thread hash allocations when overlap is enabled.

The headless scenario runner (an `Exe` on `net10.0`, not loaded by Unity). It loads
YAML scenarios (`Scenario.cs`), builds a world, runs the simulation and any computer
keepers, checks `expect` claims, and writes a report per run.

    dotnet run --project src/DK2.Harness -- list [dir]
    dotnet run --project src/DK2.Harness -- run <file|dir> [--out <dir>] [--fuzz <player>[:<seed>[:<interval>]]] [--coverage] [--progress <ticks>] [--snapshot-check <ticks>]
    dotnet run --project src/DK2.Harness -- replay <scenario> <input.journal> [--out <dir>]
    dotnet run --project src/DK2.Harness -- watch <file|dir> [--out <dir>] [--timeout-seconds 600] [--budget-minutes 30] [--max-runs 20] [run options] [--fuzz-seeds <n>]

`run` always writes `input.journal`: every tick's merged input frame (scripted, computer
keeper and controller commands), hashes at least every 256 ticks, and an end record. `replay`
rebuilds the scenario's world and setup, skips its YAML `commands`, keepers and controllers,
feeds the journal's frames and compares hashes. It exits 0 on a match, 1 on a divergence, a
run that failed as recorded, or a recorded failure that did not recur, and 2 when it refuses
(another `HashVersion`, another scenario file by SHA-256, or a journal without a scenario
fingerprint). `watch` runs each scenario in a child process, kills its process tree at the
timeout, and writes a new `watch-<utc stamp>/` folder per watch (nothing is deleted) with
`watch-summary.tsv` and, for a run that did not pass, `failure.md` plus the child's stdout and
stderr. Verdicts are passed, failed, setup, crashed, timeout, cancelled and invalid. Exit 1
means a run did not pass, 4 means a limit left scenarios unrun, 2 is a usage error. The
journal format is `DK2.Simulation` (`CommandCodec`, `InputJournal`); Unity does not write one
yet. `watch` passes the run options through; `--fuzz` goes unchanged unless `--fuzz-seeds n`
runs each scenario with fuzz seeds 1 to n (refused without `--fuzz`); `watch-summary.tsv` has a
label column for the seed.

`--fuzz` makes a legal-action fuzzer play that keeper (player 3 or higher; any other player is a
per-scenario failure, exit 1): only commands `HumanCommandPolicy` allows, drawn from its own
`SimRandom(seed ^ player)`; the seed defaults to the scenario's, the interval to 4 ticks.
`--coverage` writes `coverage.tsv`. `--progress N` writes `progress.tsv` every N ticks (40 with
`--fuzz`) and lists stall candidates in `report.md`; they are diagnostics, not verdicts.
`--snapshot-check N` checks save/restore continuation every N ticks; a throw or divergence fails
the run. Option parsing is `HarnessCommandLine`.

A run can also take test controllers (`ScenarioRunner.Run(scenario, controllers)`): a
`ScenarioController` is a player, a decision interval and a function from `KeeperView` to
commands, and its commands join the next tick's input after the computer keepers'. From code
they are passed as above; on the command line `--fuzz` builds one. Their commands are journaled
like any others, so `replay` needs no controller. See ADR-0552 and
`docs/plans/2026-10-27-autonomous-playtesting.md`.

Exit code 1 means a scenario failed to load or run, or an expectation failed. The
file format, output files and corpus are in `scenarios/README.md`. Campaign maps need
`DK2_DIR`.

Depends on `DK2.Core`, `DK2.Data`, `DK2.Data.Model`, `DK2.Simulation`, `DK2.AI`,
`DK2.Assets`, `DK2.Presentation` and YamlDotNet.

Test: `dotnet test tests/DK2.Behavior.Tests` (references the harness).
