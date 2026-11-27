// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class CreatureOwnershipTests
{
    private static (GameDatabase Db, WorldState World) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        return (db, WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)1, (byte)3, (byte)4]));
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte owner = 3)
    {
        return world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, owner, new TilePos(2, 2));
    }

    private static int[] Order(WorldState world, byte owner = 3)
    {
        return [.. world.OwnedCreatures(owner).Select(c => c.Id)];
    }

    [Fact]
    public void AcquisitionPrependsAndTransferDoesNotSortById()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        CreatureState first = Spawn(db, world);
        CreatureState second = Spawn(db, world);
        CreatureState rival = Spawn(db, world, 4);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { second.Id, first.Id }, Order(world));
        world.ChangeCreatureOwner(first, 4);
        Assert.Equal(new[] { first.Id, rival.Id }, Order(world, 4));
        Assert.Equal(new[] { second.Id }, Order(world));
        world.ChangeCreatureOwner(first, 3);
        Assert.Equal(new[] { first.Id, second.Id }, Order(world));
        Assert.Equal(new[] { rival.Id }, Order(world, 4));
#pragma warning restore IDE0300
    }

    [Fact]
    public void SameOwnerDoesNotReinsertAndRemovalPreservesOtherLinks()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        CreatureState first = Spawn(db, world);
        CreatureState second = Spawn(db, world);
        CreatureState third = Spawn(db, world);
        world.ChangeCreatureOwner(first, 3);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { third.Id, second.Id, first.Id }, Order(world));
        Assert.True(world.RemoveCreature(second.Id));
        Assert.Equal(new[] { third.Id, first.Id }, Order(world));
        Assert.True(world.RemoveCreature(third.Id));
        Assert.Equal(new[] { first.Id }, Order(world));
#pragma warning restore IDE0300
        Assert.True(world.RemoveCreature(first.Id));
        Assert.Empty(Order(world));
    }

    [Fact]
    public void ActualHandPickupKeepsMembershipAndOrder()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        CreatureState first = Spawn(db, world);
        CreatureState second = Spawn(db, world);
        var sim = new GameSimulation(db, world, 42, [new HandSystem()]);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(3, first.Id)));
        sim.Run(2); // hand state 1 takes the prepared creature two turns after the command
        Assert.True(first.InHand);
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { second.Id, first.Id }, Order(world));
#pragma warning restore IDE0300
    }

    [Fact]
    public void DyingCreaturesRemainButCorpseRepresentationIsExcluded()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        CreatureState creature = Spawn(db, world);
        creature.Health = 0;
#pragma warning disable IDE0300 // Preserve the original array construction or overload.
        Assert.Equal(new[] { creature.Id }, Order(world));
#pragma warning restore IDE0300
        creature.Dk2StateId = CreatureStateMachine.NoState;
        Assert.Empty(Order(world));
        Assert.Single(world.Creatures);
    }

    [Fact]
    public void SaveRestorePreservesTransferHistoryAndSubsequentAcquisition()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        CreatureState first = Spawn(db, world);
        Spawn(db, world);
        world.ChangeCreatureOwner(first, 4);
        world.ChangeCreatureOwner(first, 3);
        var sim = new GameSimulation(db, world, 42, []);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        Assert.Equal(Order(world), Order(restored.World));
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Spawn(db, world);
        Spawn(db, restored.World);
        world.ChangeCreatureOwner(first, 4);
        restored.World.ChangeCreatureOwner(restored.World.CreatureById(first.Id)!, 4);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(Order(world, 4), Order(restored.World, 4));
    }

    [Fact]
    public void HashDistinguishesOrderWithIdenticalCreatureValues()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        CreatureState first = Spawn(db, world);
        Spawn(db, world);
        var sim = new GameSimulation(db, world, 42, []);
        ulong before = sim.StateHash();
        world.ChangeCreatureOwner(first, 4);
        world.ChangeCreatureOwner(first, 3);
        Assert.NotEqual(before, sim.StateHash());
    }

    [Fact]
    public void TransferRejectsCreatureFromAnotherWorldEvenWithMatchingId()
    {
        (GameDatabase? db, WorldState? world) = Setup();
        (GameDatabase _, WorldState? other) = Setup();
        Spawn(db, world);
        CreatureState foreign = Spawn(db, other);
        Assert.Throws<ArgumentException>(() => world.ChangeCreatureOwner(foreign, 4));
        Assert.Equal((byte)3, foreign.OwnerPlayerId);
    }
}
