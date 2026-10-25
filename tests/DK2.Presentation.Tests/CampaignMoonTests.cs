// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public class CampaignMoonTests
{
    [Fact]
    public void SecretFiveGateIncludesEndpointsAndRejectsMiddleOfLunation()
    {
        Assert.True(CampaignMoon.IsSecretFiveAvailable(-1));
        Assert.True(CampaignMoon.IsSecretFiveAvailable(-.95f));
        Assert.True(CampaignMoon.IsSecretFiveAvailable(.95f));
        Assert.False(CampaignMoon.IsSecretFiveAvailable(-.949f));
        Assert.False(CampaignMoon.IsSecretFiveAvailable(0));
        Assert.False(CampaignMoon.IsSecretFiveAvailable(.949f));
    }

    [Fact]
    public void CalendarPhaseVariesContinuouslyAcrossTheNativeGate()
    {
        var day = new DateTime(2026, 10, 1);
        float previous = CampaignMoon.PhaseAt(day);
        int openDays = 0, wraps = 0;
        for (int i = 1; i <= 60; i++)
        {
            float phase = CampaignMoon.PhaseAt(day.AddDays(i));
            Assert.InRange(phase, -1, 1);
            if (phase < previous) wraps++;
            if (CampaignMoon.IsSecretFiveAvailable(phase)) openDays++;
            previous = phase;
        }
        Assert.Equal(2, wraps);
        Assert.InRange(openDays, 2, 4);
        Assert.InRange(CampaignMoon.PhaseAt(day.AddHours(12)) - CampaignMoon.PhaseAt(day.AddHours(11)), .001f, .01f);
    }
    [Fact]
    public void NativeStartupClockIgnoresMilliseconds()
    {
        var clock = new DateTime(2026, 10, 1, 12, 34, 56);
        Assert.Equal(CampaignMoon.PhaseAt(clock), CampaignMoon.PhaseAt(clock.AddMilliseconds(999)));
    }

}
