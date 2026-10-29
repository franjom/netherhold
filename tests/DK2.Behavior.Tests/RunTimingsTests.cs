// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Harness;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary><see cref="RunTimings.TickTimes"/>, which the speed budget's warm-up cut reads.</summary>
public class RunTimingsTests(ITestOutputHelper output)
{
    [InstallFact]
    public void TheTimingsCarryOneTimePerTick_InTickOrder()
    {
        RunTimings timings = ScenarioCorpus.Run(ScenarioCorpus.Load("tiles/dig_and_claim.yaml"), output).Timings!;

        Assert.Equal(timings.Ticks, timings.TickTimes.Count);
        Assert.Equal(timings.PerTick.Max, timings.TickTimes.Max());
        Assert.Equal(timings.Worst.Time, timings.TickTimes[timings.Worst.Tick]);
    }
}
