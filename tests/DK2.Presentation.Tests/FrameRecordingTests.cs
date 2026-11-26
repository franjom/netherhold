// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace DK2.Presentation.Tests;

public sealed class FrameRecordingTests
{
    private static FrameRecording Recorded(params (double FrameMs, bool Moving)[] frames)
    {
        var recording = new FrameRecording(frames.Length);
        foreach ((double ms, bool moving) in frames)
        {
            Span<double> row = recording.Add(moving);
            row[(int)FrameMetric.FrameMs] = ms;
            row[(int)FrameMetric.DrawCalls] = moving ? 200 : 100;
        }
        return recording;
    }

    [Fact]
    public void PercentilesAreNearestRankOverThePhasesFrames()
    {
        var frames = new (double, bool)[100];
        for (int i = 0; i < 100; i++)
        {
            frames[i] = (i + 1, i % 2 == 1);
        }

        FrameRecording recording = Recorded(frames);
        Assert.Equal(new FrameStatistics(100, 50.5, 50, 95, 99, 100), recording.Statistics(FramePhase.All, FrameMetric.FrameMs));
        FrameStatistics moving = recording.Statistics(FramePhase.Moving, FrameMetric.FrameMs);
        Assert.Equal((50, 51d, 50d, 96d, 100d, 100d), (moving.Count, moving.Mean, moving.P50, moving.P95, moving.P99, moving.Max));
        Assert.Equal(100, recording.Statistics(FramePhase.Still, FrameMetric.DrawCalls).Max);
        Assert.Equal(default, new FrameRecording(4).Statistics(FramePhase.All, FrameMetric.FrameMs));
    }

    [Fact]
    public void AFullRecordingDropsAndCountsTheRest()
    {
        var recording = new FrameRecording(1);
        Assert.Equal(FrameRecording.MetricCount, recording.Add(false).Length);
        Assert.True(recording.Add(true).IsEmpty);
        Assert.Equal((1, 1), (recording.Count, recording.Dropped));
    }

    [Fact]
    public void SlowFramesAreThoseOverTheSixtyFpsBudget()
    {
        FrameRecording recording = Recorded((10, false), (17, true), (18, false), (5, false), (20, true));
        Assert.Equal(3, recording.SlowFrames(FramePhase.All));
        Assert.Equal(1, recording.SlowFrames(FramePhase.Still));
        Assert.Equal(2, recording.LongestSlowRun());
    }

    [Fact]
    public void TheJsonReportParsesAndNamesEveryMetricPerPhase()
    {
        FrameRecording recording = Recorded((10, false), (12, true));
        string json = recording.Json([new("gpu", "A \"quoted\" \\ name\n")], ["--level", "Level1"],
            [new("lighting.stillMeanMs", 0.25), new("bad", double.NaN)], warmUpFrames: 120);
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("dk2-godot-perf/1", root.GetProperty("schema").GetString());
        Assert.Equal("A \"quoted\" \\ name\n", root.GetProperty("info").GetProperty("gpu").GetString());
        Assert.Equal("Level1", root.GetProperty("arguments")[1].GetString());
        Assert.Equal(120, root.GetProperty("frames").GetProperty("warmUp").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("counters").GetProperty("bad").ValueKind);
        foreach (string phase in new[] { "all", "still", "moving" })
        {
            for (int m = 0; m < FrameRecording.MetricCount; m++)
            {
                Assert.True(root.GetProperty("phases").GetProperty(phase).TryGetProperty(FrameRecording.MetricName((FrameMetric)m), out _));
            }
        }

        Assert.Equal(12, root.GetProperty("phases").GetProperty("moving").GetProperty("frameMs").GetProperty("max").GetDouble());
        Assert.Contains("frameMs", recording.Table());
    }

    [Theory]
    [InlineData(new[] { 7.0 }, 7, 7, 7, 7)]
    [InlineData(new[] { 2.0, 1.0 }, 1, 2, 2, 2)]
    [InlineData(new[] { 3.0, 1.0, 2.0 }, 2, 3, 3, 3)]
    public void NearestRankHoldsForTheSmallestCounts(double[] frames, double p50, double p95, double p99, double max)
    {
        var recording = new FrameRecording(frames.Length);
        foreach (double ms in frames)
        {
            recording.Add(false)[(int)FrameMetric.FrameMs] = ms;
        }

        FrameStatistics s = recording.Statistics(FramePhase.All, FrameMetric.FrameMs);
        Assert.Equal((p50, p95, p99, max), (s.P50, s.P95, s.P99, s.Max));
    }

    [Fact]
    public void AFrameExactlyOnTheBudgetIsNotSlow()
    {
        FrameRecording recording = Recorded((FrameRecording.BudgetMilliseconds, false),
            (Math.BitIncrement(FrameRecording.BudgetMilliseconds), false));
        Assert.Equal(1, recording.SlowFrames(FramePhase.All));
    }

    [Fact]
    public void EachMetricsNameIsItsEnumNameInCamelCase()
    {
        Assert.Equal(Enum.GetValues<FrameMetric>().Length, FrameRecording.MetricCount);
        foreach (FrameMetric metric in Enum.GetValues<FrameMetric>())
        {
            string name = metric.ToString();
            Assert.Equal(char.ToLowerInvariant(name[0]) + name[1..], FrameRecording.MetricName(metric));
        }
    }

    [Fact]
    public void TheReportIsInvariantUnderACommaDecimalCulture()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        try
        {
            // de-DE's separators on a clone: the tests run in globalization-invariant mode, with no named cultures.
            var german = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            german.NumberFormat.NumberDecimalSeparator = ",";
            german.NumberFormat.NumberGroupSeparator = ".";
            CultureInfo.CurrentCulture = german;
            Assert.Equal("10,25", 10.25.ToString(CultureInfo.CurrentCulture));
            FrameRecording recording = Recorded((10.25, false), (12.5, true), (11.75, true));
            string json = recording.Json([], [], [new("x", 0.5)], warmUpFrames: 1);
            using var document = JsonDocument.Parse(json);
            JsonElement frame = document.RootElement.GetProperty("phases").GetProperty("all").GetProperty("frameMs");
            foreach (string stat in new[] { "mean", "p50", "p95", "p99", "max" })
            {
                Assert.True(double.IsFinite(frame.GetProperty(stat).GetDouble()));
            }

            Assert.Equal(11.5, frame.GetProperty("mean").GetDouble(), 9);
            Assert.Equal(0.5, document.RootElement.GetProperty("counters").GetProperty("x").GetDouble());
            Assert.Contains("10.25", recording.Table());
            Assert.DoesNotContain("10,25", recording.Table());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void ACameraScriptMovesInStraightLinesBetweenKeysAndHoldsAtTheEnds()
    {
        CameraScript script = CameraScript.Parse("10:5,5; 20:15,5;30:-2.5,10")!;
        var origin = new Vector3(20, 2, -20);
        Assert.Equal(30, script.LastFrame);
        Assert.Equal(new Vector3(25, 2, -25), script.FocusAt(0, origin, 40, 40));
        Assert.Equal(new Vector3(30, 2, -25), script.FocusAt(15, origin, 40, 40));
        Assert.Equal(new Vector3(17.5f, 2, -30), script.FocusAt(99, origin, 40, 40));
        // Kept inside the map's pan bounds.
        Vector3 clamped = CameraScript.Parse("0:50,99")!.FocusAt(0, origin, 40, 40);
        Assert.True(clamped.X < 40 && clamped.X > 39.99f && clamped.Z > -40 && clamped.Z < -39.99f);
        Assert.Equal(0, CameraScript.Parse("0:-50,-99")!.FocusAt(0, origin, 40, 40).Z);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5:1,1;5:2,2")]
    [InlineData("-1:1,1")]
    [InlineData("1:1")]
    [InlineData("1:1,x")]
    [InlineData("1:1,1:L")]
    public void AMalformedCameraScriptIsNull(string text) => Assert.Null(CameraScript.Parse(text));
}
