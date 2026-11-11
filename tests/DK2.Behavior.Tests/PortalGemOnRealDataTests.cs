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

    /// <summary>Trigger 85 also runs creature action 42 with byte 1 (<c>0x0049AC80</c> arm
    /// <c>0x2A</c>): the Lord takes the first world slot, value 1, which the renderer reads for his
    /// Objective ring (Q607). The slot is saved, and his removal frees it, as the death bookkeeping
    /// does (<c>0x00488396</c>).</summary>
    [InstallFact]
    public void LevelOnesLordIsMarkedInAWorldSlotSavedAndFreedWithHim()
    {
        GameSimulation simulation = Level("level1");
        WorldState world = simulation.World;
        simulation.Run(3);

        CreatureState lord = Assert.Single(world.Creatures,
            c => simulation.Database.CreatureById(c.CreatureTypeId).Name == "Lord Of The Land");
        Assert.Equal(new HeroSlot { CreatureId = lord.Id, Value = 1 }, world.HeroSlots[0]);
        Assert.All(world.HeroSlots.Skip(1), slot => Assert.True(slot.IsFree));

        WorldState restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(simulation), simulation.Database).World;
        Assert.Equal(world.HeroSlots, restored.HeroSlots);

        Assert.True(world.RemoveCreature(lord.Id));
        Assert.All(world.HeroSlots, slot => Assert.True(slot.IsFree));
    }

    /// <summary>The Things loader (<c>0x00520BB0</c>) skips a level-placed Portal Gem
    /// (<c>0x00520BCA</c>), so Level 19 starts without one though its map places one at (61, 41).
    /// The gem comes from world action 2 under trigger 18 once flag 5 is 3, at that tile's centre
    /// (arm <c>0x00518E92</c>), for player 1.</summary>
    [InstallFact]
    public void LevelNineteensGemIsNotPlacedButMadeByItsScriptOnTheMarkedTile()
    {
        GameSimulation simulation = Level("level19");
        WorldState world = simulation.World;
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == TriggerExecutor.PortalGemObjectId);

        world.Triggers.Write(5, FlagWriteMode.Assign, 3);
        simulation.Run(2);

        ObjectState gem = Assert.Single(world.Objects, o => o.ObjectTypeId == TriggerExecutor.PortalGemObjectId);
        Assert.Equal(PlayerIds.Good, gem.OwnerPlayerId);
        Assert.Equal((41 * world.Width) + 61, gem.TileIndex);
        Assert.Equal((61 << 12) + 0x800, gem.PosX);
        Assert.Equal((41 << 12) + 0x800, gem.PosY);
    }

    [InstallTheory]
    [InlineData("level18")]
    [InlineData("level20")]
    public void ALevelPlacedGemIsDroppedAtLoad(string name) =>
        Assert.DoesNotContain(Level(name).World.Objects, o => o.ObjectTypeId == TriggerExecutor.PortalGemObjectId);

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
