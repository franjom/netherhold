// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

public sealed class StoreGoldSteeringTests
{
    [Fact]
    public void TheCapturedStorePilesProduceImp444sNativeStep()
    {
        var (db, world, nav) = Setup();
        Store(db, world, new TilePos(18, 24));
        Store(db, world, new TilePos(18, 25));
        world.Tile(18, 24).GoldHeld = 1000;
        world.Tile(18, 25).GoldHeld = 1000;
        nav.Refresh();
        var body = new SteeringBody
        {
            Pos = new Fixed16Vector(1201520, 1663392), Vel = new Fixed16Vector(-16288, -25584),
            Radius = 4576, MaxForce = 98304, MaxSpeed = 30144,
        };

        Fixed16Vector repel = RepelObstacleRules.Compute(body, nav);
        Fixed16Vector total = SteeringIntegrator.Combine(body, Fixed16Vector.Zero, repel,
            new Fixed16Vector(-203031, -319031), Fixed16Vector.Zero);
        SteeringIntegrator.Step(body, total,
            WorldSteering.ProfileOf(db.CreatureById(1), PlayerIds.FirstKeeper, MoveOptions.Plain), nav);

        Assert.Equal(new Fixed16Vector(-19750, -22894), body.Vel);
        Assert.Equal(new Fixed16Vector(1181770, 1640498), body.Pos);
        Assert.Empty(body.SolidContacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorePilesShareTheObjectsCreationOrder(bool goldFirst)
    {
        var (db, world, nav) = Setup();
        var at = new TilePos(2, 2);
        Store(db, world, at);
        if (goldFirst) Assert.Equal(1000, GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 1000));
        world.SpawnObject(db, 8, PlayerIds.FirstKeeper, world.TileIndex(at));
        if (!goldFirst) Assert.Equal(1000, GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 1000));
        nav.Refresh();

        List<SteeringObstacle> obstacles = Query(nav, at);

        Assert.Equal(2, obstacles.Count);
        int gold = (db.ObjectById(FloorGold.TreasuryPileObjectId)!.Width.Raw >> 1) << 4;
        int wheel = (db.ObjectById(8)!.Width.Raw >> 1) << 4;
        Assert.Equal(goldFirst ? wheel : gold, obstacles[0].Size);
        Assert.Equal(goldFirst ? gold : wheel, obstacles[1].Size);
        Assert.True(obstacles[0].Solid);
        Assert.False(obstacles[1].Solid);
    }

    [Fact]
    public void ToppingUpKeepsTheLinkButAnEmptiedPileIsRecreatedAtTheHead()
    {
        var (db, world, nav) = Setup();
        var at = new TilePos(2, 2);
        Store(db, world, at);
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 1000);
        world.SpawnObject(db, 8, PlayerIds.FirstKeeper, world.TileIndex(at));
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 100);
        nav.Refresh();
        Assert.Equal(2, Query(nav, at).Count);
        int wheel = (db.ObjectById(8)!.Width.Raw >> 1) << 4;
        Assert.Equal(wheel, Query(nav, at)[0].Size);

        Assert.Equal(1100, GoldStore.Spend(world, db, PlayerIds.FirstKeeper, 1100));
        Assert.Single(Query(nav, at));
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 100);

        Assert.Equal((db.ObjectById(3)!.Width.Raw >> 1) << 4, Query(nav, at)[0].Size);
    }

    [Fact]
    public void AnEmptyStoreDoesNotContributeAPile()
    {
        var (db, world, nav) = Setup();
        Store(db, world, new TilePos(2, 2));
        nav.Refresh();
        Assert.Empty(Query(nav, new TilePos(2, 2)));
    }

    [Fact]
    public void APhysicalStorePileIsNotCountedTwice()
    {
        var (db, world, nav) = Setup();
        var at = new TilePos(2, 2);
        Store(db, world, at);
        world.Tile(at).GoldHeld = 1000;
        world.SpawnObject(db, FloorGold.TreasuryPileObjectId, PlayerIds.FirstKeeper, world.TileIndex(at));
        nav.Refresh();
        Assert.Single(Query(nav, at));
    }

    [Fact]
    public void ThePileLinkChangesTheHashAndSurvivesSaveAndCopy()
    {
        var (db, world, nav) = Setup();
        var at = new TilePos(2, 2);
        Store(db, world, at);
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 1000);
        Assert.True(world.Tile(at).StoreGoldLinkOrder > 0);
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        int order = world.Tile(at).StoreGoldLinkOrder;
        world.Tile(at).StoreGoldLinkOrder = 0;
        Assert.NotEqual(before, sim.StateHash());
        world.Tile(at).StoreGoldLinkOrder = order;

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db);
        Assert.Equal(world.Tile(at).StoreGoldLinkOrder, restored.World.Tile(at).StoreGoldLinkOrder);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var (_, copy, _) = Setup();
        copy.CopyFrom(world);
        Assert.Equal(world.Tile(at).StoreGoldLinkOrder, copy.Tile(at).StoreGoldLinkOrder);
    }

    [Fact]
    public void ADirectTileDepositCreatesTheNewestPileLink()
    {
        var (db, world, nav) = Setup();
        var at = new TilePos(2, 2);
        Store(db, world, at);
        world.SpawnObject(db, 8, PlayerIds.FirstKeeper, world.TileIndex(at));
        Assert.Equal(1000, GoldStore.DepositInto(world, db, tileIndex: world.TileIndex(at), amount: 1000));
        nav.Refresh();
        Assert.Equal((db.ObjectById(3)!.Width.Raw >> 1) << 4, Query(nav, at)[0].Size);
    }

    [Fact]
    public void ARefusedDirectDepositDoesNotCreateAPileLink()
    {
        var (db, world, _) = Setup();
        var at = new TilePos(2, 2);
        Assert.Equal(0, GoldStore.DepositInto(world, db, tileIndex: world.TileIndex(at), amount: 1000));
        Assert.Equal(0, world.Tile(at).StoreGoldLinkOrder);
    }

    [Fact]
    public void PaydayClearsTheLinkOfAnEmptiedPile()
    {
        var (db, world, _) = Setup();
        var at = new TilePos(2, 2);
        Store(db, world, at);
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 1000);
        var room = RoomIndex.RoomAt(world, db, world.TileIndex(at))!;
        Assert.Equal(1000, PaydayRules.TakeFromRoom(world, room, start: world.TileIndex(at), wanted: 1000));
        Assert.Equal(0, world.Tile(at).StoreGoldLinkOrder);
    }

    [Fact]
    public void RemovingTheRoomClearsTheSpilledPilesLink()
    {
        var (db, world, _) = Setup();
        var at = new TilePos(2, 2);
        Store(db, world, at);
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 1000);
        Assert.Equal(1000, RoomRules.Unmake(db, ref world.Tile(at)));
        Assert.Equal(0, world.Tile(at).StoreGoldLinkOrder);
    }

    [Fact]
    public void TerrainDamageClearsTheSpilledPilesLink()
    {
        var (db, world, _) = Setup();
        var at = new TilePos(2, 2);
        Store(db, world, at);
        GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 1000);
        TileWorkRules.ApplyDamage(db, ref world.Tile(at), new TileHealthDamage(world.Tile(at).Health));
        Assert.Equal(0, world.Tile(at).GoldHeld);
        Assert.Equal(0, world.Tile(at).StoreGoldLinkOrder);
    }

    private static List<SteeringObstacle> Query(WorldSteering nav, TilePos at)
    {
        var result = new List<SteeringObstacle>();
        nav.CollectObstacles(new Fixed16Vector(CreatureState.CentreOf(at.X) << 4,
            CreatureState.CentreOf(at.Y) << 4), Fixed16.One, result);
        return result;
    }

    private static void Store(GameDatabase db, WorldState world, TilePos at) =>
        RoomRules.Place(db, ref world.Tile(at), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);

    private static (GameDatabase, WorldState, WorldSteering) Setup()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(24, 32),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
        for (int x = 1; x < world.Width - 1; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
        }
        return (db, world, new WorldSteering(db, world));
    }
}
