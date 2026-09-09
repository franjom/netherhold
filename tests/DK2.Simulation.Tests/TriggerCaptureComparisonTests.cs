// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Simulation.Tests;

public class TriggerCaptureComparisonTests
{
    [Fact]
    public void Compare_UsesStoredStampAndLeavesFirstSampleCensored()
    {
        using StringReader ours = Csv(
            Row(1, ("t15", 1), ("f83", 1)),
            Row(33, ("t15", 1), ("f1", 1), ("f83", 1)),
            Row(601, ("t15", 1), ("f1", 1), ("f34", 1), ("f83", 1)));
        using StringReader capture = Csv(
            Row(11, ("t15", 7), ("f83", 1)),
            Row(40, ("t15", 7), ("f1", 1), ("f83", 1)),
            Row(608, ("t15", 7), ("f1", 1), ("f34", 1), ("f83", 1)));

        TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
            ours, "ours", capture, "capture");

        Assert.Equal(["t15", "f1", "f34", "f83"], result.InBoth);
        Assert.Empty(result.CaptureOnly);
        Assert.Empty(result.OursOnly);
        Assert.Equal("t15", result.AnchorId);
        Assert.Equal(1, result.OursOrigin);
        Assert.Equal(7, result.CaptureOrigin);
        Assert.Equal([1L, 1L], result.AlignedEvents
            .Where(e => e.Residual.HasValue).Select(e => e.Residual!.Value));
        Assert.Null(result.AlignedEvents.Single(e => e.Id == "f83").Residual);
        Assert.Equal(1, result.LargestAbsoluteResidual);
    }

    [Fact]
    public void Compare_ReportsEventSetWithoutInventingAnAnchor()
    {
        using StringReader ours = Csv(Row(1, ("f1", 1)));
        using StringReader capture = Csv(Row(7, ("f2", 1)));

        TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
            ours, "ours", capture, "capture");

        Assert.Empty(result.InBoth);
        Assert.Equal(["f2"], result.CaptureOnly);
        Assert.Equal(["f1"], result.OursOnly);
        Assert.Null(result.AnchorId);
        Assert.Equal("no timer is stamped on both sides", result.AlignmentRefusal);
        Assert.Empty(result.AlignedEvents);
        Assert.Null(result.LargestAbsoluteResidual);
    }

    [Fact]
    public void Compare_RequestedAnchorOverridesTheLowestTimer()
    {
        using StringReader ours = Csv(
            Row(1, ("t2", 1), ("t5", 1)),
            Row(9, ("t2", 1), ("t5", 1), ("f1", 1)));
        using StringReader capture = Csv(
            Row(6, ("t2", 6), ("t5", 4)),
            Row(12, ("t2", 6), ("t5", 4), ("f1", 1)));

        TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
            ours, "ours", capture, "capture", "t5");

        Assert.Equal("t5", result.AnchorId);
        Assert.Equal(1, result.OursOrigin);
        Assert.Equal(4, result.CaptureOrigin);
    }

    [Fact]
    public void Compare_DoesNotFallBackWhenRequestedAnchorWasNotStamped()
    {
        using StringReader ours = Csv(Row(1, ("t2", 1), ("f1", 1)));
        using StringReader capture = Csv(Row(6, ("t2", 6), ("f1", 1)));

        TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
            ours, "ours", capture, "capture", "t5");

        Assert.Null(result.AnchorId);
        Assert.Equal("anchor t5 is not stamped on both sides", result.AlignmentRefusal);
        Assert.Empty(result.AlignedEvents);
    }

    private static CsvRow Row(int tick, params (string Id, int Value)[] events) =>
        new(tick, events);

    private static StringReader Csv(params CsvRow[] rows)
    {
        string[] columns = TriggerTurnCsvEmitter.Header.Split(',');
        var lines = new List<string> { TriggerTurnCsvEmitter.Header };
        foreach (CsvRow row in rows)
        {
            var cells = new string[columns.Length];
            Array.Fill(cells, "0");
            cells[0] = string.Empty;
            cells[1] = row.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach ((string id, int value) in row.Events)
                cells[Array.IndexOf(columns, id)] = value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            lines.Add(string.Join(',', cells));
        }
        return new StringReader(string.Join(Environment.NewLine, lines));
    }

    private sealed record CsvRow(int Tick, (string Id, int Value)[] Events);
}
