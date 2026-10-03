// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Runs after every parallel test collection here, so <see cref="StateEntryLog"/>
/// holds what the scenario suite and the real-data tests entered.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AfterEveryOtherTest
{
    public const string Name = "state coverage, after every other test";
}

/// <summary><see cref="CreatureStateCoverage"/> against the scenario suite, the half of the
/// check <c>DK2.Simulation.Tests</c> cannot see.</summary>
[Collection(AfterEveryOtherTest.Name)]
public class StateCoverageOnScenariosTests
{
    [Fact]
    public void EveryStateTheScenarioSuiteEntersIsBuiltOrHasAnExplicitUnbuiltHandlerBoundary()
    {
        Assert.True(StateEntryLog.Enabled, $"{StateEntryLog.SwitchName} is off in this test project.");
        string[] offenders = StateEntryLog.Entered()
            .Where(id => CreatureStateCoverage.Get(id).Status != StateStatus.Built)
            .Where(id => ActivityRules.Of(new CreatureState { Dk2StateId = id }) != CreatureActivity.UnbuiltHandler)
            .Select(id => $"{id} ({CreatureStateCoverage.Get(id).Status}), first by {StateEntryLog.FirstEnteredBy(id)}")
            .ToArray();

        Assert.True(offenders.Length == 0, "Entered without Built coverage or an explicit unbuilt-handler boundary:\n" + string.Join("\n", offenders));
    }

    /// <summary>The evidence for 160–162, 166–168 and 170–172 (ADR-0223), checked on the shipped
    /// records rather than quoted: the job that enters each run is held by no creature.</summary>
    [InstallFact]
    public void TheTheftStatesCalledUnreachable_HaveTheirJobInNoShippedCreaturesLists()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var steals = new[] { JobType.StealSpells, JobType.StealManufactureCrates, JobType.StealEnemyGold };

        string[] holders = db.Creatures.Values
            .SelectMany(c => c.HappyJobs.Concat(c.UnhappyJobs).Concat(c.AngryJobs).Select(j => (c, j.Job))
                .Concat(c.AlternativeJobs.Select(j => (c, j.Job))))
            .Where(p => steals.Contains(p.Job))
            .Select(p => $"{p.c.CreatureId} holds {p.Job}")
            .ToArray();

        Assert.Empty(holders);
        foreach (int id in new[] { 160, 161, 162, 166, 167, 168, 170, 171, 172 })
            Assert.Equal(StateStatus.Unreachable, CreatureStateCoverage.Get(id).Status);
    }

    [InstallFact]
    public void EveryStateAllowListedAsScenarioOnlyIsEnteredByTheScenarioSuite()
    {
        var entered = StateEntryLog.Entered().ToHashSet();
        string[] missing = CreatureStateCoverage.All
            .Where(r => r.UnexercisedBecause == CreatureStateCoverage.ScenarioSuiteOnly && !entered.Contains(r.StateId))
            .Select(r => $"{r.StateId} ({r.Handler!.Name}.{r.Detail})")
            .ToArray();

        Assert.True(missing.Length == 0, "Allow-listed as scenario-only but not entered here:\n" + string.Join("\n", missing));
    }
}
