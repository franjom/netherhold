// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

public class DungeonHeartVisualTests
{
    [Theory]
    [InlineData(100, 438, 4096, 0)]
    [InlineData(50, 877, 6144, 2048)]
    [InlineData(10, 175, 2211, 3686)]
    [InlineData(0, 14, 1228, 4095)]
    public void HealthSelectsOriginalRateScaleAndDecay(int percent, int phaseStep, int scale, int decay)
    {
        DungeonHeartVisual visual = DungeonHeartVisual.FromHealth(currentHealth: percent, maximumHealth: 100);
        Assert.Equal(new Fp32(phaseStep), visual.PhaseStep);
        Assert.Equal(new Fp32(scale), visual.VerticalScale);
        Assert.Equal(new Fp32(decay), visual.DecayPhase);
    }

    [Fact]
    public void HealthPercentTruncatesBeforeSelectingFivePercentStage()
    {
        Assert.Equal(DungeonHeartVisual.FromHealth(95, 100), DungeonHeartVisual.FromHealth(959, 1000));
        Assert.Equal(DungeonHeartVisual.FromHealth(100, 100), DungeonHeartVisual.FromHealth(120, 100));
        Assert.Equal(DungeonHeartVisual.FromHealth(0, 100), DungeonHeartVisual.FromHealth(-1, 100));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2048, 2)]
    [InlineData(4095, 4)]
    public void DecaySelectsAnAuthoredTextureAndClampsAtLastFrame(int phase, int index)
    {
        Assert.Equal(index, DungeonHeartVisual.TextureIndex(new Fp32(phase), textureCount: 5));
    }
}
