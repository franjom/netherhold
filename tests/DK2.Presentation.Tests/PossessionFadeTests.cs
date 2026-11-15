// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>The possession cameras' darkening with distance (possession-warp.md §4).</summary>
public sealed class PossessionFadeTests
{
    [Fact]
    public void TheViewDistanceIsThePresetsOrTwiceTheZoomCappedInPossession_StronglyInferredFromBinary()
    {
        Assert.Equal(12f, PossessionFade.ViewDistance(0, 49152, possessionCamera: true));
        Assert.Equal(20f, PossessionFade.ViewDistance(40960, 61440, possessionCamera: false));
        Assert.Equal(PossessionFade.PossessionCap, PossessionFade.ViewDistance(0, 40 * 4096, possessionCamera: true));
        Assert.Equal(40f, PossessionFade.ViewDistance(0, 40 * 4096, possessionCamera: false));
    }

    [Fact]
    public void TheDistanceIsTheDepthPlusHalfTheScaledOffsets_StronglyInferredFromBinary()
    {
        // Straight ahead it is the depth; off the axis the scaled offsets add half their sum.
        Assert.Equal(6f, PossessionFade.Distance(0, 0, 6, 0.4f, 0.533f));
        Assert.Equal(6f + (0.5f * (2f + 1f)), PossessionFade.Distance(-4, 2, 6, 0.5f, 0.5f));
    }

    [Theory]
    [InlineData(0f, PossessionFade.Full)]
    [InlineData(6f, PossessionFade.Full)]      // half the view distance: the last unfaded point
    [InlineData(9f, 32768)]                    // three quarters: half brightness
    [InlineData(11.999f, 10)]                  // floor(65536 * 2 * 0.001 / 12)
    [InlineData(12f, 0)]
    [InlineData(20f, 0)]
    public void TheFadeRunsFromHalfTheViewDistanceToBlackAtIt_StronglyInferredFromBinary(float distance, int word)
    {
        Assert.Equal(word, PossessionFade.Word(distance, 12f));
        Assert.Equal(Math.Min(word, 65536) / 65536f, PossessionFade.Brightness(distance, 12f), 3);
    }

    [Fact]
    public void AChannelIsScaledByTheWordAndCutDown_StronglyInferredFromBinary()
    {
        Assert.Equal(200, PossessionFade.Shade(200, PossessionFade.Full));
        Assert.Equal(100, PossessionFade.Shade(200, 32768));
        Assert.Equal(99, PossessionFade.Shade(200, 32767));
        Assert.Equal(0, PossessionFade.Shade(255, 0));
    }

    [Fact]
    public void ATriangleWhollyPastTheViewDistanceIsNotDrawn_StronglyInferredFromBinary()
    {
        Assert.True(PossessionFade.Dropped(12.1f, 13f, 20f, 12f));
        Assert.False(PossessionFade.Dropped(11.9f, 13f, 20f, 12f));
    }
}
