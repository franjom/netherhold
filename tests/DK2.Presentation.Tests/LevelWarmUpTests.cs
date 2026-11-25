// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class LevelWarmUpTests
{
    private static ulong Hash(CampaignLevelData level, int ticks)
    {
        GameSimulation simulation = level.CreateSimulation(seed: 1, fogOfWarEnabled: true);
        for (int i = 0; i < ticks; i++) simulation.Tick(InputFrame.Empty);
        return simulation.StateHash();
    }

    [InstallTheory]
    [InlineData("Level1")]
    [InlineData("Level6a")]
    public void ARealSimulationAfterAWarmUpHasTheStateOfOneWithout(string name)
    {
        CampaignLevelData level = CampaignLevelData.Load(Dk2InstallGate.Path, name);
        ulong without = Hash(level, 120);

        ulong warmed = LevelWarmUp.Run(level, seed: 1, fogOfWarEnabled: true);
        ulong after = Hash(level, 120);

        Assert.Equal(without, after);
        Assert.Equal(Hash(level, LevelWarmUp.Ticks), warmed);
    }
}
