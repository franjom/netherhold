// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>
/// <see cref="ThingProjection"/> against the fixture. The same assertions run
/// against the shipped campaign in
/// <c>DK2.Behavior.Tests.PlacedThingsOnRealDataTests</c>, which is what stops a
/// transcribed fixture from agreeing with a wrong reading.
/// </summary>
public class ThingProjectionTests
{
    private static GameDatabase Database() => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase database) =>
        WorldState.FromMap(
            database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);

    private static KeeperCreatureThing Keeper(
        int x, int y, byte creatureId, byte playerId, byte level = 1, int health = 100) =>
        new(new byte[28])
        {
            X = x, Y = y, CreatureId = creatureId, PlayerId = playerId,
            Level = level, InitialHealth = health,
        };

    [Fact]
    public void AThingsCoordinatesAreTileIndices_NotTheSimulationsSubtileUnits()
    {
        // The trap this file exists to keep shut. A THING's X and Y are 32-bit
        // ints and the simulation carries positions at Fp32.One (4096) units to
        // the tile, so reading them as subtile units type-checks, runs, and puts
        // every creature in the level inside tile (0,0).
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database, [Keeper(5, 7, Dk2TerrainFixture.ImpCreatureId, 3)]);

        CreatureState imp = Assert.Single(world.Creatures);
        Assert.Equal(new TilePos(5, 7), imp.Tile);
        Assert.Equal(CreatureState.CentreOf(5), imp.PosX);
        Assert.Equal(CreatureState.CentreOf(7), imp.PosY);
    }

    [Fact]
    public void InitialHealthIsAPercentageOfTheMaximum_NotAHitPointCount()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        CreatureDef imp = database.CreatureById(Dk2TerrainFixture.ImpCreatureId);

        ThingProjection.Place(world, database,
        [
            Keeper(4, 4, Dk2TerrainFixture.ImpCreatureId, 3, health: 100),
            Keeper(5, 4, Dk2TerrainFixture.ImpCreatureId, 3, health: 50),
            Keeper(6, 4, Dk2TerrainFixture.ImpCreatureId, 3, health: 5),
        ]);

        int max = CreatureLevelRules.MaxHealth(database, imp, 1);
        Assert.Equal([max, max / 2, max * 5 / 100],
                     world.Creatures.Select(c => c.Health).ToArray());

        // And the reading it is not: an absolute count would put all three at
        // 100, 50 and 5 hit points out of the Imp's several hundred.
        Assert.NotEqual(100, world.Creatures[0].Health);
    }

    [Fact]
    public void HealthIsScaledByTheCreaturesOwnLevel_NotByLevelOne()
    {
        // Ordering, not arithmetic. Maximum health is the record's Hp scaled by
        // the level curve, so a projection that writes health before it writes
        // the level leaves a level-10 creature standing at a level-1 maximum -
        // which looks like a wounded creature rather than like a bug.
        GameDatabase database = Database();
        WorldState world = World(database);
        CreatureDef imp = database.CreatureById(Dk2TerrainFixture.ImpCreatureId);

        ThingProjection.Place(world, database,
            [Keeper(4, 4, Dk2TerrainFixture.ImpCreatureId, 3, level: 10, health: 100)]);

        CreatureState placed = Assert.Single(world.Creatures);
        Assert.Equal(10, placed.Level);
        Assert.Equal(CreatureLevelRules.MaxHealth(database, imp, 10), placed.Health);
        Assert.True(placed.Health > CreatureLevelRules.MaxHealth(database, imp, 1));
    }

    [Fact]
    public void TheSideOfANeutralOrGoodCreatureComesFromWhichRecordItIs()
    {
        // Neither record carries a player field, because it does not need one.
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database,
        [
            new NeutralCreatureThing(new byte[24])
            { X = 4, Y = 4, CreatureId = 7, Level = 1, InitialHealth = 100 },
            new GoodCreatureThing(new byte[32])
            { X = 5, Y = 4, CreatureId = 7, Level = 1, InitialHealth = 100, Unknown_001D = new byte[2] },
        ]);

        Assert.Equal([PlayerIds.Neutral, PlayerIds.Good],
                     world.Creatures.Select(c => c.OwnerPlayerId).ToArray());
    }

    [Fact]
    public void AnObjectCarriesItsMoneyAmountAsGold()
    {
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database,
        [
            new ObjectThing(new byte[24])
            {
                X = 6, Y = 8, ObjectId = Dk2TerrainFixture.GoblinBedObjectId,
                PlayerId = PlayerIds.FirstKeeper, MoneyAmount = 750,
                Unknown_0008 = new byte[4],
            },
        ]);

        ObjectState placed = Assert.Single(world.Objects);
        Assert.Equal((8 * Dk2TerrainFixture.Map().Width) + 6, placed.TileIndex);
        Assert.Equal(PlayerIds.FirstKeeper, placed.OwnerPlayerId);
        Assert.Equal(750, placed.GoldHeld);
    }

    [Fact]
    public void ARoomRecordIsCountedAndNotPlaced_BecauseTheGridAlreadyHasIt()
    {
        GameDatabase database = Database();
        WorldState world = World(database);
        byte before = world.Tile(4, 4).TerrainId;

        ThingProjectionResult result = ThingProjection.Place(world, database,
        [
            new RoomThing(new byte[20])
            { X = 4, Y = 4, RoomType = Dk2TerrainFixture.TreasuryTerrain, PlayerId = 3, InitialHealth = 100 },
        ]);

        Assert.Equal(1, result.RoomsAlreadyInTheGrid);
        Assert.Equal(0, result.Placed);
        Assert.Equal(before, world.Tile(4, 4).TerrainId);
    }

    [Fact]
    public void WhatIsNotProjectedYetIsCounted_NotSilentlyDropped()
    {
        // The difference between "this level has no doors" and "this projection
        // cannot see doors" is a number, and without it both look like success.
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjectionResult result = ThingProjection.Place(world, database,
        [
            new DoorThing(new byte[20]) { X = 4, Y = 4, DoorId = 1, Unknown_0011 = new byte[3] },
            new TrapThing(new byte[16]) { X = 5, Y = 4, TrapId = 1 },
            new ActionPointThing(new byte[56]) { StartX = 6, StartY = 4, ActionPointId = 1, Name = "" },
        ]);

        Assert.Equal(3, result.NotYetProjected);
        Assert.Equal(0, result.Placed);
        Assert.Empty(world.Creatures);
        Assert.Empty(world.Objects);
    }

    [Fact]
    public void PlacementOrderIsTheFilesOrder_BecauseUpdateOrderIsBehaviour()
    {
        GameDatabase database = Database();
        WorldState world = World(database);

        ThingProjection.Place(world, database,
        [
            Keeper(7, 4, Dk2TerrainFixture.ImpCreatureId, 3),
            Keeper(4, 4, Dk2TerrainFixture.ImpCreatureId, 3),
            Keeper(6, 4, Dk2TerrainFixture.ImpCreatureId, 3),
        ]);

        Assert.Equal([7, 4, 6], world.Creatures.Select(c => c.Tile.X).ToArray());
    }
}
