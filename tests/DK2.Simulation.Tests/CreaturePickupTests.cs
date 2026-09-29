// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class CreaturePickupTests
{
    [Theory]
    [InlineData(76, false, false)]
    [InlineData(193, false, false)]
    [InlineData(51, false, false)]
    [InlineData(52, false, false)]
    [InlineData(51, true, true)]
    [InlineData(52, true, true)]
    [InlineData(76, true, false)]
    [InlineData(193, true, false)]
    [InlineData(1, false, true)]
    public void CommonStateRefusalsPrecedeThePitException(int state, bool pit, bool expected)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3]);
        var creature = world.SpawnCreature(db, 7, 3, new TilePos(2, 2));
        creature.Dk2StateId = state;
        creature.CombatPitFighter = pit;
        Assert.Equal(expected, HandRules.HasPickupPermission(world, db, creature, 3));
    }

    [Fact]
    public void AnOwnCreatureInAnotherKeepersPrisonIsRefusedToItsOwnerAndPermittedToTheJailer()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3, (byte)4]);
        var captive = world.SpawnCreature(db, 7, 3, new TilePos(2, 2));
        Assert.True(HandRules.HasPickupPermission(world, db, captive, 3));
        // The Prison writes the room membership 0x0048E6A0 reads (0x0047B740 calls 0x004E5A70).
        Assert.True(PrisonRules.Imprison(world, db, captive, 4, (2 * world.Width) + 2));
        Assert.False(HandRules.HasPickupPermission(world, db, captive, 3));
        Assert.True(HandRules.HasPickupPermission(world, db, captive, 4));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(6, false)]
    public void WorkerPermissionTestsEitherBitRatherThanBoth(byte flags, bool expected)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3]);
        var imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(2, 2));
        Assert.Equal((byte)9, imp.Flags);
        imp.Flags = flags;
        Assert.Equal(expected, HandRules.CanPickUp(world, db, imp, 3));
        imp.CombatPitFighter = true;
        Assert.True(HandRules.HasPickupPermission(world, db, imp, 3));
        world.Tile(2, 2).OwnerPlayerId = 1;
        Assert.False(HandRules.HasPickupPermission(world, db, imp, 3));
        imp.CombatPitFighter = false;
        Assert.Equal(expected, HandRules.HasPickupPermission(world, db, imp, 3));
    }

    [Fact]
    public void ForeignToolBypassesOwnedTeleportGateButNotCommonGates()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)1, (byte)3]);
        var creature = world.SpawnCreature(db, 7, 1, new TilePos(2, 2));
        Assert.False(HandRules.CanPickUp(world, db, creature, 3));
        creature.IsTool = true;
        creature.Dk2StateId = 51;
        Assert.True(HandRules.CanPickUp(world, db, creature, 3));
        creature.Dk2StateId = 76;
        Assert.False(HandRules.CanPickUp(world, db, creature, 3));
        creature.Dk2StateId = 1;
        creature.InHand = true;
        Assert.False(HandRules.CanPickUp(world, db, creature, 3));
    }

    [Fact]
    public void FullHandRefusesCommandWithoutRevokingCreaturePermission()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3]);
        var creature = world.SpawnCreature(db, 7, 3, new TilePos(2, 2));
        for (int i = 0; i < HandRules.Capacity(db); i++)
        {
            var held = world.SpawnCreature(db, 7, 3, new TilePos(2, 2));
            held.InHand = true;
            world.Player(3).Hand.Add(HandItem.Of(held));
        }
        Assert.True(HandRules.HasPickupPermission(world, db, creature, 3));
        Assert.False(HandRules.CanPickUp(world, db, creature, 3));
        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(3, creature.Id)));
        Assert.False(creature.InHand);
    }

    [Fact]
    public void TravelResumeDoesNotActAsTheCurrentPickupState()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3]);
        var creature = world.SpawnCreature(db, 7, 3, new TilePos(2, 2));
        creature.Dk2StateId = 6;
        creature.UtilityNewState = 193;
        Assert.True(HandRules.CanPickUp(world, db, creature, 3));
    }
}
