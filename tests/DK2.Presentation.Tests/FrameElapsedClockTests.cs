// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class FrameElapsedClockTests
{
    [Fact]
    public void PreparationTimeIsCountedBeforeDrawingAndNotCountedAgainNextFrame()
    {
        var clock = new FrameElapsedClock();
        clock.Sample(0, fallbackDeltaSeconds: 0, maximumDeltaSeconds: .333, timeScale: 1);
        double input = clock.Sample(.01, fallbackDeltaSeconds: .01, maximumDeltaSeconds: .333, timeScale: 1);
        double prepared = clock.Sample(.05, fallbackDeltaSeconds: 0, maximumDeltaSeconds: .333 - input, timeScale: 1);
        double next = clock.Sample(.06, fallbackDeltaSeconds: .05, maximumDeltaSeconds: .333, timeScale: 1);
        Assert.Equal(.05, input + prepared, 6);
        Assert.Equal(.01, next, 6);
    }

    [Fact]
    public void WorkerWaitIsCountedOnTheFrameThatActuallyWaits()
    {
        var clock = new FrameElapsedClock();
        Assert.Equal(.01, clock.Sample(0, fallbackDeltaSeconds: .01, maximumDeltaSeconds: .333, timeScale: 1), 6);
        Assert.Equal(.01, clock.Sample(.01, fallbackDeltaSeconds: .01, maximumDeltaSeconds: .333, timeScale: 1), 6);
        Assert.Equal(.04, clock.Sample(.05, fallbackDeltaSeconds: .01, maximumDeltaSeconds: .333, timeScale: 1), 6);
        Assert.Equal(.01, clock.Sample(.06, fallbackDeltaSeconds: .04, maximumDeltaSeconds: .333, timeScale: 1), 6);
    }

    [Fact]
    public void PauseAndLongEditorStallsRetainTheCatchupLimit()
    {
        var clock = new FrameElapsedClock();
        clock.Sample(0, fallbackDeltaSeconds: 0, maximumDeltaSeconds: .333, timeScale: 1);
        Assert.Equal(0, clock.Sample(10, fallbackDeltaSeconds: 0, maximumDeltaSeconds: .333, timeScale: 0));
        Assert.Equal(.02, clock.Sample(10.01, fallbackDeltaSeconds: .02, maximumDeltaSeconds: .333, timeScale: 2), 6);
        Assert.Equal(.333, clock.Sample(100, fallbackDeltaSeconds: .333, maximumDeltaSeconds: .333, timeScale: 1));
    }
}
