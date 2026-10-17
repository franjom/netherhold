// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later

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

    [Fact]
    public void SharedPhaseAdvancesOnlyOnAnAcceptedPresentationUpdate()
    {
        var animation = new ClassicTorchFlameAnimation();
        Assert.Equal(0, animation.Frame(20));
        for (int update = 0; update < 20; update++) animation.Advance();
        Assert.Equal(19, animation.Frame(20));
        for (int extraCamera = 0; extraCamera < 10; extraCamera++)
            Assert.Equal(19, animation.Frame(20));
        animation.Advance();
        Assert.Equal(0, animation.Frame(20));
    }

    [Fact]
    public void PerTorchOffsetsShareTheSameWrappedIntegerPhase()
    {
        var animation = new ClassicTorchFlameAnimation();
        Assert.Equal(10, animation.Frame(20, 2048));
        animation.Advance();
        Assert.Equal(0, animation.Frame(20, 4000));
        Assert.Equal(0, animation.Frame(20));
        for (int update = 1; update < 1024; update++) animation.Advance();
        Assert.Equal(10, animation.Frame(20, 2048));
    }
}
