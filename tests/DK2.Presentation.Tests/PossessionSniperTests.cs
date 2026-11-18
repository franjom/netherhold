// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;
using DK2.Data.Model;
namespace DK2.Presentation.Tests;
public sealed class PossessionSniperTests
{
    [Fact]
    public void LensMovesInRealTimeAndViewDistanceChangesAtOnce()
    {
        var preset = new CameraThing([])
        {
            Lens = 4096, ViewDistance = 12 * 4096, MinViewDistance = 4096, MaxViewDistance = 64 * 4096,
        };
        var sniper = new PossessionSniper(preset);
        sniper.Toggle(new Fp32(4 * 4096), currentlyOn: false, clockMilliseconds: 1000);
        Assert.Equal(22 * 4096, sniper.ViewDistance);
        sniper.Step(1150);
        Assert.Equal(10240, sniper.Lens);
        sniper.Step(1300);
        Assert.Equal(16384, sniper.Lens);
        sniper.Toggle(new Fp32(4 * 4096), currentlyOn: true, clockMilliseconds: 1300);
        Assert.Equal(12 * 4096, sniper.ViewDistance);
        sniper.Step(1600);
        Assert.Equal(4096, sniper.Lens);
    }
}
