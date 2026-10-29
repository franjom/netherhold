// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text;
using DK2.Core;
using DK2.Presentation;

namespace DK2.Harness;

/// <summary>Writes a scenario run's evidence trail.</summary>
internal static class ScenarioReportWriter
{
    public static void Write(ScenarioResult result, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        File.WriteAllLines(
            Path.Combine(outputDir, "hashes.tsv"),
            result.HashPerTick.Select((h, i) => i.ToString(CultureInfo.InvariantCulture) + "\t" + h.ToString("x16", CultureInfo.InvariantCulture)));

        File.WriteAllLines(
            Path.Combine(outputDir, "events.log"),
            result.Events.Select(e => e.ToString()));

        if (result.Scenario.ComputerKeepers.Count > 0)
            File.WriteAllLines(Path.Combine(outputDir, "keeper-commands.tsv"),
                result.KeeperCommandLog.Select(e =>
                {
                    var hash = Hash64.Start();
                    e.Command.HashInto(ref hash);
                    return $"{e.InputTick}\t{e.Command.PlayerId}\t{e.Command.GetType().Name}\t{hash.Value:x16}";
                }));

        // Type and hash only, like keeper-commands.tsv: a readable summary; input.journal replays.
        if (result.ControllerCommandLog.Count > 0)
            File.WriteAllLines(Path.Combine(outputDir, "controller-commands.tsv"),
                result.ControllerCommandLog.Select(e =>
                {
                    var hash = Hash64.Start();
                    e.Command.HashInto(ref hash);
                    return $"{e.InputTick}\t{e.Controller}\t{e.Command.PlayerId}\t{e.Command.GetType().Name}\t{hash.Value:x16}";
                }));

        if (result.Coverage is RunCoverage coverage)
            File.WriteAllLines(Path.Combine(outputDir, "coverage.tsv"),
                [$"triggers\treached\t{coverage.TriggerRecordsReached}",
                 .. coverage.CreatureTicksByState.OrderBy(kv => kv.Key).Select(kv => $"state\t{kv.Key}\t{kv.Value}"),
                 .. coverage.EventsByKind.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"event\t{kv.Key}\t{kv.Value}")]);

        if (result.Progress.Count > 0)
            File.WriteAllLines(Path.Combine(outputDir, "progress.tsv"),
                ["tick\tplayer\tstatus\towned_tiles\troom_tiles\tgold\tmana\tcreatures",
                 .. result.Progress.Select(p => $"{p.Tick}\t{p.Player}\t{p.Status}\t{p.OwnedTiles}\t{p.RoomTiles}\t{p.Gold}\t{p.Mana}\t{p.Creatures}")]);

        var report = new StringBuilder();
        report.AppendLine($"# Scenario run: {result.Scenario.Id}");
        report.AppendLine();
        report.AppendLine($"- source: {result.Scenario.SourcePath}");
        report.AppendLine($"- map: {result.Scenario.Map}");
        report.AppendLine($"- seed: {result.Scenario.Seed}");
        if (result.Scenario.Random != "modern")
            report.AppendLine($"- random: {result.Scenario.Random}");
        report.AppendLine($"- balance table: {result.BalanceTable}");
        report.AppendLine($"- ticks run: {result.TicksRun}");
        report.AppendLine($"- final state hash: {result.FinalStateHash:x16}");
        report.AppendLine($"- events recorded: {result.Events.Count}");
        if (result.Scenario.ComputerKeepers.Count > 0)
            report.AppendLine($"- keeper commands emitted: {result.KeeperCommandLog.Count} (keeper-commands.tsv)");
        if (result.ControllerCommandLog.Count > 0)
            report.AppendLine($"- test controller commands emitted: {result.ControllerCommandLog.Count} (controller-commands.tsv)");
        report.AppendLine();

        if (result.SnapshotChecks > 0)
            report.AppendLine($"Snapshot continuation checks: {result.SnapshotChecks}, failed: {result.InvariantFailures.Count}.{Environment.NewLine}");

        if (result.InvariantFailures.Count > 0)
        {
            report.AppendLine("## Invariant failures");
            report.AppendLine();
            foreach (string failure in result.InvariantFailures) report.AppendLine($"- **FAILED**: {failure}");
            report.AppendLine();
        }

        if (result.StallCandidates.Count > 0)
        {
            report.AppendLine("## Stall candidates (diagnostic, not verdicts)");
            report.AppendLine();
            foreach (string stall in result.StallCandidates) report.AppendLine($"- {stall}");
            report.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(result.Scenario.Question))
        {
            report.AppendLine("## Question");
            report.AppendLine();
            report.AppendLine(result.Scenario.Question.Trim());
            report.AppendLine();
        }

        if (result.Scenario.Expect.Count > 0)
        {
            report.AppendLine("## What this run claims");
            report.AppendLine();
            report.AppendLine(result.ExpectationFailures.Count == 0
                ? $"All {result.Scenario.Expect.Count} expectation(s) held."
                : $"{result.ExpectationFailures.Count} of {result.Scenario.Expect.Count} expectation(s) FAILED.");
            report.AppendLine();
            foreach (ScenarioExpectation expectation in result.Scenario.Expect)
                report.AppendLine($"- {expectation}{Environment.NewLine}  because {expectation.Because.Trim()}");

            foreach (string failure in result.ExpectationFailures)
                report.AppendLine($"- **FAILED**: {failure}");

            report.AppendLine();
        }

        report.AppendLine("## Not answered by this run");
        report.AppendLine();
        if (result.Unsupported.Count == 0)
        {
            report.AppendLine("Nothing - every part of the scenario was applied.");
        }
        else
        {
            report.AppendLine("This build could not act on the following. Any conclusion drawn");
            report.AppendLine("from this run covers the remainder only.");
            report.AppendLine();
            foreach (string item in result.Unsupported)
                report.AppendLine($"- {item}");
        }

        if (!string.IsNullOrEmpty(result.Picture))
        {
            report.AppendLine();
            report.AppendLine("## The world at the end of the run");
            report.AppendLine();
            report.AppendLine($"Art: {result.AssetSummary}.");
            report.AppendLine();
            report.AppendLine(TextRenderer.Legend());
            report.AppendLine();
            report.AppendLine("```");
            report.Append(result.Picture);
            report.AppendLine("```");

            File.WriteAllText(Path.Combine(outputDir, "world.txt"), result.Picture);
        }

        File.WriteAllText(Path.Combine(outputDir, "report.md"), report.ToString());
        // Wall-clock measurements stay separate from the deterministic report.
        if (result.Timings is RunTimings timings)
        {
            double worst = timings.TickTimes.Skip(2).Select(t => t.TotalMilliseconds).DefaultIfEmpty(0).Max();
            File.WriteAllText(Path.Combine(outputDir, "timings.txt"), timings.Summarise(top: 30) +
                Environment.NewLine + FormattableString.Invariant($"worst tick after warm-up: {worst:F3} ms") +
                Environment.NewLine);
        }
    }
}
