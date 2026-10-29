// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Harness;

namespace DK2.Behavior.Tests;

public class ScenarioReportWriterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ReportsIncludeTimingsAndTheWorstTickAfterWarmUp(int ticks)
    {
        string root = Path.Combine(Path.GetTempPath(), $"dk2-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string scenario = Path.Combine(root, "probe.yaml");
        File.WriteAllText(scenario, $"id: report-probe\nmap: flat_8x8\nseed: 1\nduration_ticks: {ticks}\n");
        ScenarioResult result = new ScenarioRunner(ScenarioTests.SyntheticDatabase(), null).Run(Scenario.Load(scenario));

        ScenarioRunner.WriteReport(result, root);

        string path = Path.Combine(root, "timings.txt");
        Assert.True(File.Exists(path));
        string timings = File.ReadAllText(path);
        Assert.Contains("ticks: tick p50", timings, StringComparison.Ordinal);
        Assert.Contains("MB allocated", timings, StringComparison.Ordinal);
        double worst = result.Timings!.TickTimes.Skip(2).Select(t => t.TotalMilliseconds).DefaultIfEmpty(0).Max();
        Assert.Contains(FormattableString.Invariant($"worst tick after warm-up: {worst:F3} ms"), timings, StringComparison.Ordinal);
    }
}
