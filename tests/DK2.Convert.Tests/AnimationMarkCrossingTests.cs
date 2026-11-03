// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Convert.Tests;

/// <summary>The animation-mark crossing test <c>0x004B4630</c> (footsteps, docs/research/sound-effects-continuous.md section 1): marks
/// strictly inside the phase interval between two drawn frames, 1 before a wrap and 2 after it.</summary>
public sealed class AnimationMarkCrossingTests
{
    [Fact]
    public void NoMarksNeverCross()
    {
        Assert.Equal(0, AnimationMarkCrossing.Test(0, 0, 0, 4000, 0, 256));
    }

    [Fact]
    public void ALoneFirstMarkStandsInForTheSecond()
    {
        Assert.Equal(1, AnimationMarkCrossing.Test(1000, 0, 900, 1100, 0, 256));
        Assert.Equal(1, AnimationMarkCrossing.Test(0, 1000, 900, 1100, 0, 256));
        // A zero first mark with a second mark is not substituted: the second is tested as it is.
        Assert.Equal(0, AnimationMarkCrossing.Test(0, 3000, 900, 1100, 0, 256));
    }

    [Fact]
    public void AMarkOnAnEndOfTheIntervalDoesNotCross()
    {
        Assert.Equal(0, AnimationMarkCrossing.Test(1000, 0, 1000, 1100, 0, 256));
        Assert.Equal(0, AnimationMarkCrossing.Test(1100, 0, 1000, 1100, 0, 256));
        Assert.Equal(1, AnimationMarkCrossing.Test(1001, 0, 1000, 1100, 0, 256));
        Assert.Equal(1, AnimationMarkCrossing.Test(1099, 0, 1000, 1100, 0, 256));
    }

    [Fact]
    public void EitherMarkOfThePairCrosses()
    {
        Assert.Equal(1, AnimationMarkCrossing.Test(500, 1500, 1400, 1600, 0, 256));
        Assert.Equal(1, AnimationMarkCrossing.Test(500, 1500, 400, 600, 0, 256));
        Assert.Equal(0, AnimationMarkCrossing.Test(500, 1500, 600, 1400, 0, 256));
    }

    [Fact]
    public void TheIntervalIsTheFractionOfTheTurnsPhaseStepBetweenTheTwoDrawnFrames()
    {
        // Phase 0 to 1000 over the turn; the frame fractions are 1/256ths of the turn.
        Assert.Equal(0, AnimationMarkCrossing.Test(400, 0, 0, 1000, 128, 256));
        Assert.Equal(1, AnimationMarkCrossing.Test(600, 0, 0, 1000, 128, 256));
        Assert.Equal(1, AnimationMarkCrossing.Test(400, 0, 0, 1000, 0, 128));
        // A frame before the turn began extends the interval back past the previous phase.
        Assert.Equal(1, AnimationMarkCrossing.Test(900, 0, 1000, 1200, -256, 0));
        Assert.Equal(0, AnimationMarkCrossing.Test(1100, 0, 1000, 1200, -256, 0));
    }

    [Fact]
    public void ACrossingAfterTheWrapReturnsTwoAndOneBeforeItReturnsOne()
    {
        // Previous phase 4000, current 100: the step is 196 and the interval 4000..4196.
        Assert.Equal(1, AnimationMarkCrossing.Test(4050, 0, 4000, 100, 0, 256));
        Assert.Equal(2, AnimationMarkCrossing.Test(50, 0, 4000, 100, 0, 256));
        Assert.Equal(0, AnimationMarkCrossing.Test(150, 0, 4000, 100, 0, 256));
        // The second mark after the wrap, the first nowhere near.
        Assert.Equal(2, AnimationMarkCrossing.Test(2000, 99, 4000, 100, 0, 256));
    }

    [Fact]
    public void AScaledProductRoundsTowardZeroAsTheNativeShiftDoes()
    {
        // step 3, from -100: -300/256 truncates to -1, so the start is 1000 - 1.
        Assert.Equal(0, AnimationMarkCrossing.Test(999, 0, 1000, 1003, -100, 256));
        Assert.Equal(1, AnimationMarkCrossing.Test(999, 0, 1000, 1003, -300, 256));
    }

    [Fact]
    public void AMarkBeyondTheClipNeverCrosses()
    {
        // An unauthored mark word (0xCDCD in the unique definitions) normalizes far past 4096.
        Assert.Equal(0, AnimationMarkCrossing.Test(5_000_000, 5_000_000, 3000, 200, 0, 256));
    }

    [Fact]
    public void TheClockExposesItsPreviousAndCurrentPhaseForTheTest()
    {
        var clock = new RenderAnimationClock(new CreatureAnimationTiming { ResourceType = 6, FrameCount = 8, FramesPerSecond = 8 }, loop: true);
        clock.Advance();
        Assert.Equal(0, clock.PreviousPhase);
        Assert.Equal(1024, clock.Phase);
        clock.Advance();
        Assert.Equal(1024, clock.PreviousPhase);
        Assert.Equal(2048, clock.Phase);
    }

    [Fact]
    public void TheWatcherNeverCrossesOnItsFirstFrameAndThenAdvancesFromTheLastFraction()
    {
        var watcher = new AnimationMarkWatcher();
        // First frame: no earlier frame, so an empty interval.
        Assert.Equal(0, watcher.Observe(500, 0, 0, 1000, ticksAdvanced: 1, fraction: 0.75f));
        // Same turn, fraction .75 to 1.0 of a step that covers 0..1000 at the turn: 750..1000 holds no 500.
        Assert.Equal(0, watcher.Observe(500, 0, 0, 1000, ticksAdvanced: 0, fraction: 0.999f));
        var other = new AnimationMarkWatcher();
        other.Observe(500, 0, 0, 1000, 1, 0.25f);
        Assert.Equal(1, other.Observe(500, 0, 0, 1000, 0, 0.75f));
        // A new turn: the last frame was at .75 of the earlier turn, which is -0.25 of this one.
        var across = new AnimationMarkWatcher();
        Assert.Equal(0, across.Observe(800, 0, 0, 1000, 1, 0.75f));
        Assert.Equal(1, across.Observe(800, 0, 1000, 2000, 1, 0.5f));
        Assert.Equal(0, across.Observe(800, 0, 1000, 2000, 0, 0.9f));
    }
}
