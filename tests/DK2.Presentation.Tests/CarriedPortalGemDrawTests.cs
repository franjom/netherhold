// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>A Portal Gem in its carrier's arms is off the tile chain (<c>0x0048B350</c> calls
/// <c>0x004B4F60</c>), so DK2's per-tile pass never draws it. Ours used to draw it where it was
/// made: Level 5's Lord of the Land walked off and left the gem hovering on his start tile.</summary>
public class CarriedPortalGemDrawTests
{
    [Fact]
    public void OnlyACarriedPortalGemIsInItsCarriersArms()
    {
        var gem = new ObjectState { ObjectTypeId = TriggerExecutor.PortalGemObjectId };
        Assert.False(LevelGemIdle.IsInCarrierArms(gem));
        gem.CarriedByCreatureId = 4;
        Assert.True(LevelGemIdle.IsInCarrierArms(gem));
        Assert.False(LevelGemIdle.IsInCarrierArms(new ObjectState { ObjectTypeId = 9, CarriedByCreatureId = 4 }));
    }

    [InstallFact]
    public void LevelFivesLordCarriesTheGemOffItsStartTileAndItIsNotDrawnUntilHeDropsIt()
    {
        string install = Dk2InstallGate.Path;
        const string level = "level5";
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        GameSimulation simulation = CampaignSimulationFactory.Create(database,
            GameDatabaseLoader.LoadCampaignMap(install, level)!, GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!, GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, 1);
        WorldState world = simulation.World;
        var view = new WorldView(database);

        simulation.Run(40);

        CreatureState lord = Assert.Single(world.Creatures,
            c => database.CreatureById(c.CreatureTypeId).Name == "Lord Of The Land");
        ObjectState gem = Assert.Single(world.Objects, o => o.ObjectTypeId == TriggerExecutor.PortalGemObjectId);
        Assert.Equal(gem.Id, lord.CarriedObjectId);
        Assert.Equal(lord.Id, gem.CarriedByCreatureId);
        Assert.DoesNotContain(view.Objects(world), o => o.Id == gem.Id);

        lord.Health = 0;
        simulation.Run(SimulationTiming.TicksPerSecond * 2);

        Assert.Equal(-1, gem.CarriedByCreatureId);
        Assert.Contains(view.Objects(world), o => o.Id == gem.Id);
    }
}
