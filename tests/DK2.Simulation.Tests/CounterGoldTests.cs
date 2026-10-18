// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class CounterGoldTests
{
    [Theory]
    [InlineData(100, 100)]
    [InlineData(1000, 350)]
    public void CounterWithdrawalTransfersOnlyAvailableStoredGoldIntoANeutralTreasuryPile(int requested, int expected)
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper, PlayerIds.Neutral]);
        world.Tile(2, 2).TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        world.Tile(2, 2).OwnerPlayerId = PlayerIds.FirstKeeper;
        world.Tile(2, 2).GoldHeld = 350;
        int before = GoldStore.Total(world, PlayerIds.FirstKeeper);
        var simulation = new GameSimulation(database, world, seed: 1, [new HandSystem()]);
        simulation.Tick(InputFrame.Of(new WithdrawCounterGoldCommand(PlayerIds.FirstKeeper, new(requested))));
        HandItem held = Assert.Single(world.Player(PlayerIds.FirstKeeper).Hand);
        ObjectState pile = world.ObjectById(held.Id)!;
        Assert.Equal(FloorGold.TreasuryPileObjectId, pile.ObjectTypeId);
        Assert.Equal(PlayerIds.Neutral, pile.OwnerPlayerId);
        Assert.True(pile.InHand);
        Assert.Equal(expected, pile.GoldHeld);
        Assert.Equal(before - expected, GoldStore.Total(world, PlayerIds.FirstKeeper));
    }

    [Fact]
    public void FullHandRefusesWithdrawalBeforeAnyGoldLeavesTheStore()
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [PlayerIds.FirstKeeper, PlayerIds.Neutral]);
        world.Tile(2, 2).TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        world.Tile(2, 2).OwnerPlayerId = PlayerIds.FirstKeeper;
        world.Tile(2, 2).GoldHeld = 350;
        var pile = world.SpawnObject(database, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, 0);
        for (int i = 0; i < HandRules.Capacity(database); i++)
            world.Player(PlayerIds.FirstKeeper).Hand.Add(HandItem.Of(pile));
        int before = GoldStore.Total(world, PlayerIds.FirstKeeper), objects = world.Objects.Count;
        var simulation = new GameSimulation(database, world, seed: 1, [new HandSystem()]);
        simulation.Tick(InputFrame.Of(new WithdrawCounterGoldCommand(PlayerIds.FirstKeeper, new(100))));
        Assert.Equal(before, GoldStore.Total(world, PlayerIds.FirstKeeper));
        Assert.Equal(objects, world.Objects.Count);
    }
}
