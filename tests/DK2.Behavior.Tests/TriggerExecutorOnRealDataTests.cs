// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class TriggerExecutorOnRealDataTests
{
    [InstallFact]
    public void LevelOneTreeWalksAndItsBlockedTypeCensusIsPinned()
    {
        (GameSimulation simulation, _) = LevelOne(seed: 7);
        simulation.Tick();

        string census = string.Join(",", simulation.LastTriggerRun!.Summary
            .Select(s => $"{s.Tag}:{s.Type}={s.Count}"));
        Assert.Equal("213:34=1", census);
    }

    [InstallFact]
    public void LevelOneTriggerRunIsDeterministicForTheSameSeed()
    {
        (GameSimulation a, _) = LevelOne(seed: 0x1234);
        (GameSimulation b, _) = LevelOne(seed: 0x1234);

        for (int i = 0; i < 200; i++)
        {
            a.Tick();
            b.Tick();
        }

        Assert.Equal(a.StateHash(), b.StateHash());
    }

    private static (GameSimulation Simulation, TriggerTree Tree) LevelOne(ulong seed)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "level1");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level1")!;
        LevelDef level = GameDatabaseLoader.LoadCampaignLevel(install, "level1")!;
        List<TriggerRecord> records = GameDatabaseLoader.LoadCampaignTriggers(install, "level1")!;
        byte[] players = GameDatabaseLoader.LoadCampaignPlayers(install, "level1")!
            .Select(p => p.PlayerId).ToArray();
        WorldState world = WorldState.FromMap(database, map, players);
        var tree = new TriggerTree(records, level.TriggerId, rootRepeat: 1);
        return (new GameSimulation(database, world, seed, triggers: tree), tree);
    }
}
