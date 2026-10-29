// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Simulation;

// DK2.Harness - headless scenario runner.
//
// Runs declarative scenario files against the simulation and writes an evidence
// trail (per-tick state hashes, event log, and a report of everything the
// scenario asked for that this build could not do).

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
        DK2.Harness

          run <scenario.yaml|directory> [--out <dir>] [run options]
              Runs one scenario, or every scenario under a directory.
              Default output: build/scenario-runs/<scenario-id>/, with input.journal.

              run options:
                --fuzz <player>[:<seed>[:<interval>]]  a legal-action fuzzer plays that
                    player (seed defaults to the scenario's, interval to 4 ticks)
                --coverage            coverage.tsv: triggers reached, states, events
                --progress <ticks>    progress.tsv and stall candidates (40 with --fuzz)
                --snapshot-check <ticks>  save, restore and compare every that many ticks

          replay <scenario.yaml> <input.journal> [--out <dir>]
              Re-runs a journal without its keepers or controllers and compares
              every recorded hash. Exit 0 when all match, 1 on a divergence or
              a repeated failure.

          watch <scenario.yaml|directory> [--out <dir>] [--timeout-seconds 600]
                [--budget-minutes 30] [--max-runs 20] [--jobs 1] [run options] [--fuzz-seeds <n>]
              Runs each scenario in its own process, at most --jobs at a time, and kills a
              run that outlives its limit. Each watch writes a new folder with
              watch-summary.tsv and, for a run that did not pass, failure.md
              beside its journal. Exit 1 when a run did not pass, 4 when a limit
              left scenarios unrun. --fuzz passes through as given; with
              --fuzz-seeds n each scenario runs n times, with fuzz seeds 1 to n.

          list [directory]
              Lists the scenarios found (default: scenarios/).

        The DK2 installation is located via the DK2_DIR environment variable.
        """);
    return 0;
}

switch (args[0])
{
    case "list":
    {
        string dir = args.Length > 1 ? args[1] : "scenarios";
        List<string> files = FindScenarios(dir);
        if (files.Count == 0)
        {
            Console.WriteLine($"No scenario files under '{dir}'.");
            return 0;
        }

        foreach (string file in files)
        {
            try
            {
                Scenario s = Scenario.Load(file);
                Console.WriteLine($"{s.Id,-36} {s.Map,-22} {s.DurationTicks,6} ticks  {file}");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                Console.WriteLine($"{"(invalid)",-36} {ex.Message}");
            }
        }
        return 0;
    }

    case "run":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("run needs a scenario file or directory.");
            return 2;
        }

        string outRoot = "build/scenario-runs";
        int outIndex = Array.IndexOf(args, "--out");
        if (outIndex >= 0 && outIndex + 1 < args.Length) outRoot = args[outIndex + 1];

        string? install = Dk2Install.Find();
        if (install is null)
        {
            Console.Error.WriteLine(
                "No Dungeon Keeper 2 installation found. Set DK2_DIR to the folder containing DKII.EXE.");
            return 3;
        }

        GameDatabase database = GameDatabaseLoader.Load(install);
        var runner = new ScenarioRunner(database, install);

        List<string> files = Directory.Exists(args[1]) ? FindScenarios(args[1]) : [args[1]];
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No scenario files under '{args[1]}'.");
            return 2;
        }

        if (HarnessCommandLine.RunOptions(args) is not { } template)
        {
            Console.Error.WriteLine("--fuzz takes <player>[:<seed>[:<interval>]]; --progress and --snapshot-check a positive whole number.");
            return 2;
        }

        int failures = 0;
        foreach (string file in files)
        {
            try
            {
                Scenario scenario = Scenario.Load(file);
                string outDir = Path.Combine(outRoot, scenario.Id);
                Directory.CreateDirectory(outDir);
                ScenarioResult result;
                using (FileStream journal = File.Create(Path.Combine(outDir, "input.journal")))
                    result = runner.Run(scenario, template.For(scenario, journal));
                ScenarioRunner.WriteReport(result, outDir);

                Console.WriteLine(
                    $"{scenario.Id,-36} {result.TicksRun,6} ticks  hash {result.FinalStateHash:x16}  -> {outDir}");
                foreach (string item in result.Unsupported)
                    Console.WriteLine($"    not applied: {item}");

                // A scenario that carries claims and does not meet them is a
                // failure of the run, not a note on it: the corpus exists to
                // catch exactly this, so it must reach the exit code.
                foreach (string failure in result.ExpectationFailures)
                    Console.Error.WriteLine($"    EXPECTATION FAILED: {failure}");
                foreach (string failure in result.InvariantFailures)
                    Console.Error.WriteLine($"    INVARIANT FAILED: {failure}");
                foreach (string stall in result.StallCandidates)
                    Console.WriteLine($"    stall candidate: {stall}");

                if (result.ExpectationFailures.Count > 0 || result.InvariantFailures.Count > 0) failures++;
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or ArgumentException)
            {
                // ArgumentException: a --fuzz player that is no keeper, or not in this world.
                Console.Error.WriteLine($"{file}: {ex.Message}");
                failures++;
            }
        }
        return failures == 0 ? 0 : 1;
    }

    case "replay":
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("replay needs a scenario file and its input.journal.");
            return 2;
        }

        string? install = Dk2Install.Find();
        if (install is null)
        {
            Console.Error.WriteLine(
                "No Dungeon Keeper 2 installation found. Set DK2_DIR to the folder containing DKII.EXE.");
            return 3;
        }

        Scenario scenario = Scenario.Load(args[1]);
        InputJournal journal;
        try
        {
            using FileStream stream = File.OpenRead(args[2]);
            journal = InputJournal.Read(stream);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"not replayed: {ex.Message}");
            return 2;
        }
        Console.WriteLine($"journal: {journal.Hashes.Count} hashes, ends {journal.EndReason ?? "cut short"}");

        try
        {
            JournalHeader.CheckReplayable(scenario, journal);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"not replayed: {ex.Message}");
            return 2;
        }

        var runner = new ScenarioRunner(GameDatabaseLoader.Load(install), install);
        string? recordedFailure = journal.EndReason is "completed" ? null : journal.EndReason;
        ScenarioResult result;
        try
        {
            result = runner.Replay(scenario, journal);
        }
        catch (Exception ex) when (recordedFailure is not null && recordedFailure.Contains(ex.GetType().FullName!, StringComparison.Ordinal))
        {
            // The recorded run failed with this exception type; failing the same way is the reproduction.
            Console.Error.WriteLine($"replay failed as recorded ({recordedFailure}): {ex.GetType().FullName}: {ex.Message}");
            return 1;
        }

        string? replayOut = HarnessCommandLine.Value(args, "--out");
        if (replayOut is not null) ScenarioRunner.WriteReport(result, Path.Combine(replayOut, scenario.Id));
        ReplayReport report = result.Replay!;
        if (report.JournalTruncated)
            Console.WriteLine($"the journal was cut short after tick {journal.LastHashedTick}; later ticks were run but not compared");
        if (report.Matches && recordedFailure is not null)
        {
            // A controller's or keeper's failure is not in the frames, so it cannot recur here.
            Console.Error.WriteLine(
                $"hashes match ({report.ComparedTicks} compared) but the recorded failure did not recur: {recordedFailure}");
            return 1;
        }
        if (report.Matches)
        {
            Console.WriteLine($"replay matches: {report.ComparedTicks} recorded hashes compared");
            return 0;
        }
        Console.Error.WriteLine(
            $"replay diverges at tick {report.FirstDivergentTick}: recorded {report.Recorded:x16}, " +
            $"replayed {report.Replayed:x16} ({report.ComparedTicks} hashes compared)");
        return 1;
    }

    case "watch":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("watch needs a scenario file or directory.");
            return 2;
        }

        List<string> files = Directory.Exists(args[1]) ? FindScenarios(args[1]) : [args[1]];
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No scenario files under '{args[1]}'.");
            return 2;
        }

        if (HarnessCommandLine.WatchJobs(files, args) is not { } jobs)
        {
            Console.Error.WriteLine("A run option is malformed, or --fuzz-seeds was given without --fuzz; see --help.");
            return 2;
        }

        int? perRun = HarnessCommandLine.Int(args, "--timeout-seconds", 600);
        int? budget = HarnessCommandLine.Int(args, "--budget-minutes", 30);
        int? maxRuns = HarnessCommandLine.Int(args, "--max-runs", 20);
        int? parallelJobs = HarnessCommandLine.Int(args, "--jobs", 1);
        if (perRun is null || budget is null || maxRuns is null || parallelJobs is null)
        {
            Console.Error.WriteLine("--timeout-seconds, --budget-minutes, --max-runs and --jobs take a positive whole number.");
            return 2;
        }
        if (parallelJobs > Environment.ProcessorCount)
        {
            Console.Error.WriteLine($"--jobs cannot exceed the processor count ({Environment.ProcessorCount}).");
            return 2;
        }

        var watchdog = new Watchdog(HarnessCommandLine.Value(args, "--out") ?? "build/watch-runs",
            new WatchLimits(TimeSpan.FromSeconds(perRun.Value), TimeSpan.FromMinutes(budget.Value), maxRuns.Value, parallelJobs.Value));
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            watchdog.Cancel();
        };
        WatchSummary summary = watchdog.Watch(jobs, Console.Out);
        Console.WriteLine($"{summary.Ran} run, {summary.NotPassed} did not pass, {summary.Unrun} not run");
        // 1: something failed. 4: all that ran passed, but a limit or Ctrl+C left scenarios unrun.
        return summary.NotPassed > 0 ? 1 : summary.Unrun > 0 ? 4 : 0;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'. Try --help.");
        return 2;
}

static List<string> FindScenarios(string dir) =>
    Directory.Exists(dir)
        ? [.. Directory.EnumerateFiles(dir, "*.yaml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)]
        : [];
