// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Harness;

// DK2.Harness - headless scenario runner.
//
// Runs declarative scenario files against the simulation and writes an evidence
// trail (per-tick state hashes, event log, and a report of everything the
// scenario asked for that this build could not do).

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
        DK2.Harness

          run <scenario.yaml|directory> [--out <dir>]
              Runs one scenario, or every scenario under a directory.
              Default output: build/scenario-runs/<scenario-id>/

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

        int failures = 0;
        foreach (string file in files)
        {
            try
            {
                Scenario scenario = Scenario.Load(file);
                ScenarioResult result = runner.Run(scenario);
                string outDir = Path.Combine(outRoot, scenario.Id);
                ScenarioRunner.WriteReport(result, outDir);

                Console.WriteLine(
                    $"{scenario.Id,-36} {result.TicksRun,6} ticks  hash {result.FinalStateHash:x16}  -> {outDir}");
                foreach (string item in result.Unsupported)
                    Console.WriteLine($"    not applied: {item}");
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
            {
                Console.Error.WriteLine($"{file}: {ex.Message}");
                failures++;
            }
        }
        return failures == 0 ? 0 : 1;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'. Try --help.");
        return 2;
}

static List<string> FindScenarios(string dir) =>
    Directory.Exists(dir)
        ? [.. Directory.EnumerateFiles(dir, "*.yaml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)]
        : [];
