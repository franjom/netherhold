// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Reflection;
using DK2.Harness;
using DK2.Simulation;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>Runs after this assembly's parallel collections, one test at a time. Other test
/// assemblies are not held back, so run the budget on its own (see <see cref="SpeedBudgetTests"/>).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimedAlone
{
    public const string Name = "speed budget, timed alone";
}

/// <summary>
/// Goal 9's speed budget (GOAL_ALIGNMENT.md gap 4). A DK2 turn is 250 ms. Throughput: a campaign
/// level runs at least <see cref="RealTimeMultiple"/> times faster than that, setup included.
/// Latency: after <see cref="WarmUpTicks"/> ticks, no tick of any scenario takes more than
/// <see cref="TickBudget"/>, under half a turn. A tick's time includes the harness's state hash,
/// monitor and keeper AI, so it errs on the slow side. Opt-in, on an idle machine and a Release
/// build: <c>DK2_SPEED_BUDGET=1 dotnet test tests/DK2.Behavior.Tests -c Release --filter
/// FullyQualifiedName~SpeedBudgetTests</c> (ADR-0575).
/// </summary>
[Collection(TimedAlone.Name)]
public class SpeedBudgetTests(ITestOutputHelper output)
{
    public const int RealTimeMultiple = 50;
    public const int WarmUpTicks = 2;
    public static readonly TimeSpan Turn = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan TickBudget = TimeSpan.FromMilliseconds(100);

    /// <summary>Levels 1-4 for as many turns as their packet recordings run.</summary>
    public static TheoryData<int, int> CampaignLevels() => new()
    {
        { 1, 2044 },
        { 2, 3657 },
        { 3, 3704 },
        { 4, 6804 },
    };

    [SpeedBudgetTheory]
    [MemberData(nameof(CampaignLevels))]
    public void ACampaignLevelRunsFiftyTimesFasterThanRealTime(int level, int turns)
    {
        ScenarioResult result = Run(new Scenario
        {
            Id = $"speed-budget-level{level}",
            Map = $"campaign:level{level}",
            Seed = 1,
            DurationTicks = turns,
            Setup = new ScenarioSetup { PlacedThings = true },
        });

        RunTimings timings = result.Timings!;
        Assert.Equal(turns, timings.Ticks);
        TimeSpan budget = turns * Turn / RealTimeMultiple;
        Assert.True(timings.Total <= budget,
            $"level {level}: {timings.Total.TotalSeconds:F2} s for {turns} turns, budget {budget.TotalSeconds:F2} s " +
            $"({turns * Turn / timings.Total:F0}x real time)");
    }

    public static TheoryData<string> EveryScenario()
    {
        var data = new TheoryData<string>();
        foreach (string file in ScenarioCorpus.Files())
            if (ScenarioCorpus.Load(file).DurationTicks > WarmUpTicks) data.Add(file);
        return data;
    }

    [SpeedBudgetTheory]
    [MemberData(nameof(EveryScenario))]
    public void NoTickAfterWarmUpTakesMoreThanTheBudget(string file)
    {
        RunTimings timings = Run(ScenarioCorpus.Load(file)).Timings!;

        (TimeSpan worst, int tick) = WorstAfterWarmUp(timings);
        output.WriteLine($"{file}: worst after warm-up {worst.TotalMilliseconds:F1} ms (tick {tick})");
        Assert.True(worst <= TickBudget,
            $"{file}: tick {tick} took {worst.TotalMilliseconds:F1} ms, budget {TickBudget.TotalMilliseconds:F0} ms");
    }

    private static (TimeSpan Time, int Tick) WorstAfterWarmUp(RunTimings timings)
    {
        (TimeSpan Time, int Tick) worst = (TimeSpan.Zero, -1);
        for (int t = WarmUpTicks; t < timings.TickTimes.Count; t++)
            if (timings.TickTimes[t] > worst.Time) worst = (timings.TickTimes[t], t);
        return worst;
    }

    /// <summary>Runs <paramref name="scenario"/> after refusing an unoptimised build, which would
    /// time the JIT's debug code against a budget measured on Release.</summary>
    private ScenarioResult Run(Scenario scenario)
    {
        Assert.False(typeof(GameSimulation).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled ?? false,
            "The speed budget is measured on a Release build: run with -c Release.");
        return ScenarioCorpus.Run(scenario, output);
    }
}
