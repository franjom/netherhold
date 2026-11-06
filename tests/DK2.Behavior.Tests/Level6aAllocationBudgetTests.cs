// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

public sealed class Level6aAllocationBudgetTests(ITestOutputHelper output)
{
    [InstallFact]
    public void Level6a_TickGarbageStaysBelow64KiBAfterTick1500()
    {
        string install = Dk2InstallGate.Path;
        const string name = "Level6a";
        GameSimulation simulation = CampaignSimulationFactory.Create(
            GameDatabaseLoader.LoadForCampaignLevel(install, name),
            GameDatabaseLoader.LoadCampaignMap(install, name)!,
            GameDatabaseLoader.LoadCampaignLevel(install, name)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, name)!,
            GameDatabaseLoader.LoadCampaignThings(install, name)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, name)!, seed: 1);
        simulation.Run(1500);

        const int ticks = 200;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < ticks; i++) simulation.Tick();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"Level6a ticks 1501-1700: {allocated / ticks} bytes/tick (no harness hash)");
        Assert.True(allocated <= ticks * 65536L, $"{allocated / ticks} bytes/tick exceeds 64 KiB");
    }
}
