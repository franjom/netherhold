// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>The draw clock's lag behind the tick clock on the worker path (ADR-0687): the frame
/// that owes a tick draws before the tick exists, so without a lag it clamps at the boundary and
/// the next frame catches up. The tests run the runner's per-frame arithmetic (debt, ticks owed,
/// fraction, join next frame) and measure the drawn time's evenness, as the Level6a fly-by
/// measurement of 2026-11-06 did.</summary>
public class WorkerDrawLagTests
{
    private const double TicksPerSecond = 4;

    /// <summary>One frame of the worker path: the drawn time (completed ticks - 1 + fraction) and
    /// what the next frame's join will have.</summary>
    private static double[] DrawnTimes(WorkerDrawLag lag, IEnumerable<double> frameSeconds, int maxTicksPerFrame = 2)
    {
        double debt = 0; long completed = 0;
        var drawn = new List<double>();
        foreach (double seconds in frameSeconds)
        {
            double advance = seconds * TicksPerSecond;
            lag.Observe(advance);
            debt += advance;
            int ticks = (int)Math.Min(debt, maxTicksPerFrame);
            debt -= ticks;
            drawn.Add(completed - 1 + lag.Fraction(debt, ticks));
            completed += ticks; // the ticks started at the end of this frame are joined at the next
        }
        return [.. drawn];
    }

    [Fact]
    public void ASteadyFrameRateDrawsEvenlyAcrossEveryTickBoundary()
    {
        var lag = new WorkerDrawLag();
        double[] drawn = DrawnTimes(lag, Enumerable.Repeat(1 / 60.0, 240));
        double step = TicksPerSecond / 60.0;
        // After the lag has settled, every frame advances the drawn time by exactly one frame: no
        // clamp on the frame that owes a tick, no catch-up on the next.
        for (int i = 30; i < drawn.Length; i++)
            Assert.InRange(drawn[i] - drawn[i - 1], step - 1e-9, step + 1e-9);
    }

    [Fact]
    public void WithoutTheLagTheOwingFrameClampsAndTheNextCatchesUp()
    {
        // The pre-ADR-0687 formula, min(debt + ticks, 1), as the control for the test above.
        double debt = 0; long completed = 0; var drawn = new List<double>();
        for (int i = 0; i < 240; i++)
        {
            debt += TicksPerSecond / 60.0;
            int ticks = (int)Math.Min(debt, 2); debt -= ticks;
            drawn.Add(completed - 1 + Math.Min(debt + ticks, 1));
            completed += ticks;
        }
        double step = TicksPerSecond / 60.0;
        int uneven = Enumerable.Range(31, drawn.Count - 31).Count(i => Math.Abs(drawn[i] - drawn[i - 1] - step) > 1e-6);
        Assert.True(uneven >= 20, $"expected the clamp and catch-up once per tick, saw {uneven} uneven frames");
    }

    [Fact]
    public void TheLagIsOneFrameOfTheRateTheGameIsRunningAt()
    {
        var lag = new WorkerDrawLag();
        for (int i = 0; i < 100; i++) lag.Observe(TicksPerSecond / 120.0);
        Assert.InRange(lag.Lag, TicksPerSecond / 120.0 - 1e-9, TicksPerSecond / 120.0 + 1e-9);
        for (int i = 0; i < 100; i++) lag.Observe(TicksPerSecond / 30.0);
        Assert.InRange(lag.Lag, TicksPerSecond / 30.0 - 1e-6, TicksPerSecond / 30.0 + 1e-6);
    }

    [Fact]
    public void ALongFrameCostsAtMostItsOwnExcessAndNeverMoreThanOneTick()
    {
        var lag = new WorkerDrawLag();
        var frames = Enumerable.Repeat(1 / 60.0, 100).Concat([0.5]).Concat(Enumerable.Repeat(1 / 60.0, 100));
        double[] drawn = DrawnTimes(lag, frames);
        // The lag never exceeds a tick, so a stall cannot hide a whole tick of motion.
        Assert.True(lag.Lag <= 1);
        // The drawn time never runs backwards.
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
