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
    public void SharedNormalizedPhaseKeepsNativeIntegerStepAndWrap()
    {
        Assert.Equal(0, ClassicTorchFlame.Frame(new EffectTime(0), 20));
        Assert.Equal(0, ClassicTorchFlame.Frame(new EffectTime(50), 20));
        Assert.Equal(19, ClassicTorchFlame.Frame(new EffectTime(1000), 20));
        Assert.Equal(0, ClassicTorchFlame.Frame(new EffectTime(1050), 20));
        Assert.Equal(10, ClassicTorchFlame.Frame(new EffectTime(0), 20, 2048));
        Assert.Equal(0, ClassicTorchFlame.Frame(new EffectTime(50), 20, 4000));
    }
}
