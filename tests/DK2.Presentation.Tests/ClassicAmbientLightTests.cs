// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class ClassicAmbientLightTests
{
    [Fact]
    public void DefaultSettingDoesNotIlluminateBeforeACommandAndTheFirstIncreaseStartsAtOneEighth()
    {
        var ambient = new ClassicAmbientLight();
        Assert.Equal(new ClassicAmbientSetting(512), ambient.StoredSetting);
        Assert.Equal(0, ambient.Normalized);
        ambient.Increase();
        Assert.Equal(.125f, ambient.Normalized);
        Assert.Equal(new ClassicAmbientSetting(512 << 12), ambient.StoredSetting);
    }

    [Theory]
    [InlineData(1024 << 12, .375f, 1536 << 12)]
    [InlineData((1024 << 12) + 4095, .375f, 1536 << 12)]
    [InlineData(-1, 511f / 4096, 511 << 12)]
    [InlineData(int.MinValue, 0, 0)]
    [InlineData(int.MaxValue, .5f, 2048 << 12)]
    public void IncreaseShiftsTheLoadedSettingBeforeAddingAndQuantizesThePersistedValue(
        int raw, float expected, int stored)
    {
        var ambient = new ClassicAmbientLight(new ClassicAmbientSetting(raw));
        Assert.Equal(0, ambient.Normalized);
        ambient.Increase();
        Assert.Equal(expected, ambient.Normalized);
        Assert.Equal(new ClassicAmbientSetting(stored), ambient.StoredSetting);
    }

    [Fact]
    public void DecreaseUsesTheLoadedSettingWithoutFirstApplyingItToTheRenderer()
    {
        var ambient = new ClassicAmbientLight(new ClassicAmbientSetting(1536 << 12));
        Assert.Equal(0, ambient.Normalized);
        ambient.Decrease();
        Assert.Equal(.25f, ambient.Normalized);
        Assert.Equal(new ClassicAmbientSetting(1024 << 12), ambient.StoredSetting);
    }

    [Fact]
    public void RepeatedCommandsClampBothEndsAndReverseFromThePersistedSetting()
    {
        var ambient = new ClassicAmbientLight();
        ambient.Decrease();
        Assert.Equal(0, ambient.Normalized);
        Assert.Equal(new ClassicAmbientSetting(0), ambient.StoredSetting);
        for (int i = 0; i < 6; i++) ambient.Increase();
        Assert.Equal(.5f, ambient.Normalized);
        Assert.Equal(new ClassicAmbientSetting(2048 << 12), ambient.StoredSetting);
        ambient.Decrease();
        Assert.Equal(.375f, ambient.Normalized);
        for (int i = 0; i < 6; i++) ambient.Decrease();
        Assert.Equal(0, ambient.Normalized);
        Assert.Equal(new ClassicAmbientSetting(0), ambient.StoredSetting);
    }
}
