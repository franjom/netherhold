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
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), captive, 4, (2 * world.Width) + 2));
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

    /// <summary>DK2's stored pick-up (action 57: 0x00513480, 0x004C0BE0, 0x004BC500) never asks the
    /// permission 0x0048E6A0; only the cursor does. Level 5 seed 2's recorded pick-up of slot 439 at 1684
    /// lifted the hero Wizard there (states 75, 78, 76, then a stunned drop). ADR-0616.</summary>
    [Fact]
    public void AStoredPickUpLiftsAnotherOwnersCreatureThatPlayRefuses()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3, (byte)4]);
        var rivals = world.SpawnCreature(db, 7, 4, new TilePos(2, 2));
        Assert.False(HandRules.HasPickupPermission(world, db, rivals, 3));

        new GameSimulation(db, world, 42, [new HandSystem()]).Tick(InputFrame.Of(new PickUpCreatureCommand(3, rivals.Id)));
        Assert.False(rivals.InHand);

        var stored = new GameSimulation(db, world, 42, [new HandSystem { StoredCommandPickups = true }]);
        stored.Tick(InputFrame.Of(new PickUpCreatureCommand(3, rivals.Id)));
        stored.Run(2); // hand state 1 takes the prepared creature two turns after the command
        Assert.True(rivals.InHand);
        Assert.Contains(world.Player(3).Hand, item => item.Id == rivals.Id);
        Assert.Equal((byte)4, rivals.OwnerPlayerId);
    }

    /// <summary>What the stored path still refuses: a thing 0x004B5C30 calls dead (health below 1) and,
    /// in 0x004BC500, states 51 and 52.</summary>
    [Theory]
    [InlineData(1, 1000, true)]
    [InlineData(51, 1000, false)]
    [InlineData(52, 1000, false)]
    [InlineData(1, 0, false)]
    public void AStoredPickUpStillRefusesTheDeadAndStates51And52(int state, int health, bool expected)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)3, (byte)4]);
        var rivals = world.SpawnCreature(db, 7, 4, new TilePos(2, 2));
        rivals.Dk2StateId = state;
        rivals.Health = health;
        Assert.Equal(expected, HandRules.StoredPickupAccepts(rivals));
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
