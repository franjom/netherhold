// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Creature trigger action 20 hands a creature a Portal Gem and marks it as the gem's
/// carrier (<c>0x0049AC80</c> arm <c>0x14</c>). Level 1's trigger 85 does it to the Lord of the Land
/// on creation; DK2's own capture of that level has him carrying the gem (his <c>+0x3C3</c> names
/// it) from tick 2 until he dies at tick 1776, and the gem on the floor after.</summary>
public class PortalGemOnRealDataTests
{
    [InstallFact]
    public void LevelOnesLordIsHandedThePortalGemWhenHisPartyArrives()
    {
        GameSimulation simulation = Level("level1");
        WorldState world = simulation.World;

        simulation.Run(3);

        CreatureState lord = Assert.Single(world.Creatures,
            c => simulation.Database.CreatureById(c.CreatureTypeId).Name == "Lord Of The Land");
        Assert.True(lord.GemCarrier);
        ObjectState gem = Assert.Single(world.Objects, o => o.ObjectTypeId == TriggerExecutor.PortalGemObjectId);
        Assert.Equal(gem.Id, lord.CarriedObjectId);
        Assert.Equal(lord.Id, gem.CarriedByCreatureId);
        Assert.Equal(lord.OwnerPlayerId, gem.OwnerPlayerId);
        Assert.DoesNotContain(world.Creatures, c => c.Id != lord.Id && c.GemCarrier);
    }

    [InstallFact]
    public void TheGemCarrierFlagSurvivesASnapshot()
    {
        GameSimulation simulation = Level("level1");
        simulation.Run(3);

        WorldState restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(simulation), simulation.Database).World;

        Assert.Equal(simulation.World.Creatures.Select(c => c.GemCarrier),
            restored.Creatures.Select(c => c.GemCarrier));
        Assert.Contains(restored.Creatures, c => c.GemCarrier);
    }

    private static GameSimulation Level(string name)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, name);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, name)!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, name)!;
        var things = GameDatabaseLoader.LoadCampaignThings(install, name)!;
        var level = GameDatabaseLoader.LoadCampaignLevel(install, name)!;
        var triggers = GameDatabaseLoader.LoadCampaignTriggers(install, name)!;
        return CampaignSimulationFactory.Create(db, map, level, players, things, triggers, 1);
    }
}
