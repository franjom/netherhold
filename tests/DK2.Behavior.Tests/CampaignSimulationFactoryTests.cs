// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class CampaignSimulationFactoryTests
{
    [InstallFact]
    public void SnapdragonNeutralPrisonGeneratesRatsOnlyAfterKeeperOwnership()
    {
        GameSimulation simulation = LevelEight();
        WorldState world = simulation.World;
        RoomInstance prison = Assert.Single(RoomIndex.Build(world, simulation.Database),
            room => room.Definition.TypeId == RoomTypeId.Prison);
        Assert.Equal(PlayerIds.Neutral, prison.OwnerPlayerId);
        Assert.Equal(19, prison.TileCount);
        simulation.Run(1000);
        Assert.DoesNotContain(world.Objects, thing => thing.ObjectTypeId == RatRules.RatObjectId);
        foreach (int tile in prison.TileIndices)
            world.TileAtIndex(tile).OwnerPlayerId = PlayerIds.FirstKeeper;
        Assert.Equal(2, RatRules.PopulationLimit(RoomIndex.Build(world, simulation.Database), PlayerIds.FirstKeeper));
        simulation.Run(100);
        ObjectState[] rats = world.Objects.Where(thing => thing.ObjectTypeId == RatRules.RatObjectId).ToArray();
        Assert.Equal(2, rats.Length);
        Assert.All(rats, rat =>
        {
            Assert.Equal(PlayerIds.FirstKeeper, rat.OwnerPlayerId);
            Assert.InRange(rat.Health, 1, 299);
            // Moving or choosing its next point: which one at this turn depends on the stream's draws.
            Assert.Contains(rat.ProcessState, new[] { ObjectMovementRules.MovingProcess.Value, RatRules.WanderingProcess.Value });
        });
        var positions = rats.Select(rat => (rat.PosX, rat.PosY)).ToArray();
        simulation.Run(20);
        Assert.All(rats.Select((rat, index) => (rat, index)), item =>
            Assert.NotEqual(positions[item.index], (item.rat.PosX, item.rat.PosY)));
    }

    [InstallFact]
    public void SnapdragonRaisesTheNorthernLordAndEntourageOnItsFirstTick()
    {
        GameSimulation simulation = LevelEight();
        WorldState world = simulation.World;
        Assert.DoesNotContain(world.Creatures, c => c.PartyId == 5);
        simulation.Tick();
        CreatureState[] party = world.Creatures.Where(c => c.PartyId == 5).ToArray();
        Assert.Equal(6, party.Length);
        Assert.Equal(new[] { "Lord Of The Land", "Dwarf", "Guard", "Giant", "Dwarf", "Dwarf" },
            party.Select(c => simulation.Database.CreatureById(c.CreatureTypeId).Name));
        Assert.Equal(new int[] { 5, 2, 5, 3, 4, 4 }, party.Select(c => c.Level));
        Assert.All(party, c =>
        {
            Assert.Equal(PlayerIds.Good, c.OwnerPlayerId);
            Assert.Equal(new TilePos(33, 4), c.Tile);
        });
        Assert.Equal((byte)1, world.HeroParties.Single(p => p.PartyId == 5).SpawnCount);
        simulation.Run(99);
        Assert.All(party, c =>
        {
            Assert.True(c.Health > 0);
            Assert.InRange(c.Tile.X, 30, 36);
            Assert.InRange(c.Tile.Y, 3, 7);
        });
        Assert.Equal((byte)1, world.HeroParties.Single(p => p.PartyId == 5).SpawnCount);
    }

    private static GameSimulation LevelEight()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level8")!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "Level8")!;
        var things = GameDatabaseLoader.LoadCampaignThings(install, "Level8")!;
        var level = GameDatabaseLoader.LoadCampaignLevel(install, "Level8")!;
        var triggers = GameDatabaseLoader.LoadCampaignTriggers(install, "Level8")!;
        return CampaignSimulationFactory.Create(db, map, level, players, things, triggers, 1);
    }
}
