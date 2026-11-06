// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The warm-up ticks a copy beside a live simulation and leave the live one as it was
/// (ADR-0689): the two share no state, so the live hash does not depend on the copy's ticks.</summary>
public sealed class SimulationWarmupTests
{
    [InstallFact]
    public void TheWarmUpTicksACopyAndLeavesTheLiveSimulationAsItWas()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        var map = GameDatabaseLoader.LoadCampaignMap(install, "Level8")!;
        var level = GameDatabaseLoader.LoadCampaignLevel(install, "Level8")!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "Level8")!;
        var things = GameDatabaseLoader.LoadCampaignThings(install, "Level8")!;
        var triggers = GameDatabaseLoader.LoadCampaignTriggers(install, "Level8")!;
        GameSimulation Create() => CampaignSimulationFactory.Create(database, map, level, players, things, triggers, 1);

        GameSimulation live = Create(), untouched = Create();
        Task warmup = SimulationWarmup.Start(Create, ticks: 8);
        for (int tick = 0; tick < 20; tick++)
        {
            live.Tick();
            untouched.Tick();
        }
        warmup.Wait();
        Assert.Equal(untouched.StateHash(), live.StateHash());
    }
}
