// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Presentation;

namespace DK2.Presentation.Tests;

public sealed class ClassicTorchFlameTests
{
    [Theory]
    [InlineData(0, 4096, 8192)]
    [InlineData(512, 4096, 8192)]
    [InlineData(1024, 4095, 8192)]
    [InlineData(1536, 4096, 8191)]
    public void SpriteCancelsTheMountOffsetAndRisesAboveTheLight(int yaw, int x, int y)
    {
        var torch = new TorchView(0, new TilePos(0, 0), 4096, 8192, 6144, yaw,
            255, 255, 255, 128, 6144, default);
        var position = ClassicTorchFlame.Position(torch);
        Assert.Equal(x, position.X.Raw);
        Assert.Equal(y, position.Y.Raw);
        Assert.Equal(7344, position.Z.Raw);
        Assert.Equal(1024, ClassicTorchFlame.SizeRaw);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void PlaybackSpeedIsIndependentOfUnityFrameRate(int framesPerSecond)
    {
        var animation = new ClassicTorchFlameAnimation();
        for (int frame = 1; frame <= framesPerSecond; frame++)
            animation.AdvanceTo(new EffectTime(frame * 1000 / framesPerSecond));
        Assert.Equal(4080, animation.Frame(4096));
        Assert.Equal(19, animation.Frame(20));
    }

    [Fact]
    public void RepeatedUpdatesAndCameraDrawsDoNotConsumePlaybackTime()
    {
        var animation = new ClassicTorchFlameAnimation();
        animation.AdvanceTo(new EffectTime(49));
        Assert.Equal(0, animation.Frame(4096));
        animation.AdvanceTo(new EffectTime(50));
        for (int extraUpdate = 0; extraUpdate < 10; extraUpdate++)
        {
            animation.AdvanceTo(new EffectTime(50));
            Assert.Equal(204, animation.Frame(4096));
        }
        animation.AdvanceTo(new EffectTime(1050));
        Assert.Equal(0, animation.Frame(20));
    }

    [Fact]
    public void PerTorchOffsetsShareTheSameWrappedIntegerPhase()
    {
        var animation = new ClassicTorchFlameAnimation();
        Assert.Equal(10, animation.Frame(20, 2048));
        animation.AdvanceTo(new EffectTime(50));
        Assert.Equal(0, animation.Frame(20, 4000));
        animation.AdvanceTo(new EffectTime(51200));
        Assert.Equal(10, animation.Frame(20, 2048));
    }
}
