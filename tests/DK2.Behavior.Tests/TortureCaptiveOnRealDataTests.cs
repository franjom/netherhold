// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class TortureCaptiveOnRealDataTests
{
    [InstallFact]
    public void LevelSixAsGuard48AlreadyTargetsTheKeeperAndHasNoAttackedTrigger()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level6a");
        GameSimulation sim = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignLevel(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignThings(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level6a")!, 1);

        CreatureState hero = sim.World.CreatureById(48)!;

        Assert.Equal("Guard", db.CreatureById(hero.CreatureTypeId).Name);
        Assert.Equal(new DK2.Core.TilePos(31, 15), hero.Tile);
        Assert.Equal((PlayerIds.Good, JobType.KillPlayer, (byte)3, 0),
            (hero.OwnerPlayerId, hero.GoodJob, hero.GoodJobPlayerId, (int)hero.TriggerId));
    }
}
