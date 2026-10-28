// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

public class HeartPulseGateTests
{
    [Fact]
    public void FirstPendingBeatStartsTheClockAndRemainsPendingUntilStrictlyAfterOneSecond()
    {
        var gate = new HeartPulseGate();
        Assert.False(gate.ConsumePendingBeat(new EffectTime(5000)));
        Assert.False(gate.ConsumePendingBeat(new EffectTime(6000)));
        Assert.True(gate.ConsumePendingBeat(new EffectTime(6001)));
        Assert.False(gate.ConsumePendingBeat(new EffectTime(6001)));
        Assert.False(gate.ConsumePendingBeat(new EffectTime(7001)));
        Assert.True(gate.ConsumePendingBeat(new EffectTime(7002)));
    }

    [Fact]
    public void EarlyBeatsAreConsumedButEmissionRequiresMoreThanTheFullReveal()
    {
        Assert.False(HeartPulseGate.CanEmit(new GameTick(36)));
        Assert.True(HeartPulseGate.CanEmit(new GameTick(37)));
    }
}
