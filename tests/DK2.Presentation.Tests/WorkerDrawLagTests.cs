// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>The draw clock's lag behind the tick clock on the worker path (ADR-0687). The tests run
/// the runner's per-frame arithmetic: the debt grows by the frame, whole ticks are taken from it and
/// started at the frame's end, the frame draws at the fraction, and the next frame's join has the
/// ticks. The drawn time is the completed ticks less one, plus the fraction.</summary>
public class WorkerDrawLagTests
{
    private const double TicksPerSecond = 4;
    private const int MaxTicksPerFrame = 2;

    /// <summary>The drawn time of each frame. An unobserved lag is zero, which is the old
    /// <c>min(debt + ticks, 1)</c> and serves as the control.</summary>
    private static double[] DrawnTimes(IEnumerable<double> frameSeconds, bool observe)
    {
        var lag = new WorkerDrawLag();
        double debt = 0;
        long completed = 0;
        var drawn = new List<double>();
        foreach (double seconds in frameSeconds)
        {
            double advance = seconds * TicksPerSecond;
            if (observe) lag.Observe(advance);
            debt += advance;
            int ticks = (int)Math.Min(debt, MaxTicksPerFrame);
            debt -= ticks;
            drawn.Add(completed - 1 + lag.Fraction(debt, ticks));
            completed += ticks;
        }
        return [.. drawn];
    }

    private static IEnumerable<double> Steady(int frames, double fps) => Enumerable.Repeat(1 / fps, frames);

    [Fact]
    public void ASteadyFrameRateDrawsEvenlyAcrossEveryTickBoundary()
    {
        double[] drawn = DrawnTimes(Steady(240, 60), observe: true);
        double step = TicksPerSecond / 60;
        for (int i = 30; i < drawn.Length; i++) Assert.Equal(step, drawn[i] - drawn[i - 1], 9);
    }

    [Fact]
    public void WithoutTheLagTheOwingFrameClampsAndTheNextCatchesUp()
    {
        double[] drawn = DrawnTimes(Steady(240, 60), observe: false);
        double step = TicksPerSecond / 60;
        int uneven = Enumerable.Range(31, drawn.Length - 31).Count(i => Math.Abs(drawn[i] - drawn[i - 1] - step) > 1e-6);
        Assert.True(uneven >= 20, $"expected a clamp and a catch-up once per tick, saw {uneven} uneven frames");
    }

    [Fact]
    public void TheLagIsOneFrameOfTheRateTheGameIsRunningAt()
    {
        var lag = new WorkerDrawLag();
        for (int i = 0; i < 100; i++) lag.Observe(TicksPerSecond / 120);
        Assert.Equal(TicksPerSecond / 120, lag.Lag, 9);
        for (int i = 0; i < 100; i++) lag.Observe(TicksPerSecond / 30);
        Assert.Equal(TicksPerSecond / 30, lag.Lag, 6);
    }

    [Fact]
    public void AStallNeverDrawsBackwardsAndNeverHidesAWholeTick()
    {
        var lag = new WorkerDrawLag();
        for (int i = 0; i < 10; i++) lag.Observe(2);
        Assert.Equal(1, lag.Lag);
        double[] drawn = DrawnTimes(Steady(100, 60).Append(0.5).Concat(Steady(100, 60)), observe: true);
        for (int i = 1; i < drawn.Length; i++) Assert.True(drawn[i] >= drawn[i - 1] - 1e-9, $"frame {i} drew backwards");
    }

    [Fact]
    public void TheFractionStaysWithinTheKnownMovementInterval()
    {
        var lag = new WorkerDrawLag();
        for (int i = 0; i < 20; i++) lag.Observe(0.25);
        Assert.Equal(0, lag.Fraction(0, 0));
        Assert.Equal(1, lag.Fraction(0.9, 1));
        Assert.Equal(0.5, lag.Fraction(0.75, 0), 9);
    }
}
