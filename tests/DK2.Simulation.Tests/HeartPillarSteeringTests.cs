// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class HeartPillarSteeringTests
{
    private const byte Owner = PlayerIds.FirstKeeper;
    private const int PillarWidth = 1000;

    // The terrain fixture ships no object 94; the test database gives it a width and the Obstacle flag.
    private static (GameDatabase db, WorldState world, WorldSteering steering) Setup(
        bool obstacle = true, int width = PillarWidth)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        ObjectDef pillar = basis.ObjectById(WorldSteering.ArchesObjectId)! with
        {
            ObjectId = HeartPillarRules.ArchPillarObjectId, Width = new Fp32(width),
            Flags = obstacle ? ObjectFlags.Obstacle : 0,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, objects: basis.Objects.Values.Append(pillar));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, Owner]);
        for (int y = 3; y <= 7; y++)
        for (int x = 10; x <= 14; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.DungeonHeart), Owner);
        return (db, world, new WorldSteering(db, world));
    }

    private static HeartPillarTarget[] Pillars(GameDatabase db, WorldState world)
    {
        int tile = (5 * world.Width) + 12;
        return HeartPillarRules.In(world, RoomIndex.RoomAt(world, db, tile)!);
    }

    private static List<SteeringObstacle> Query(WorldSteering steering, TilePos tile)
    {
        var result = new List<SteeringObstacle>();
        steering.CollectObstacles(new Fixed16Vector(CreatureState.CentreOf(tile.X) << 4,
            CreatureState.CentreOf(tile.Y) << 4), 4096, result);
        return result;
    }

    [Theory]
    [InlineData(HeartPillar.First)]
    [InlineData(HeartPillar.Second)]
    [InlineData(HeartPillar.Third)]
    public void ALiveHeartsPillarTileHasAnObstacleAtThePillarPositionWithHalfTheWidth(HeartPillar ordinal)
    {
        var (db, world, steering) = Setup();
        HeartPillarTarget pillar = Pillars(db, world)[(int)ordinal];

        SteeringObstacle obstacle = Assert.Single(Query(steering, pillar.Tile));

        Assert.Equal(new Fixed16Vector(pillar.X.Raw << 4, pillar.Y.Raw << 4), obstacle.Pos);
        Assert.Equal((PillarWidth >> 1) << 4, obstacle.Size);
        Assert.True(obstacle.Solid);
        Assert.False(obstacle.Flag2);
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == HeartPillarRules.ArchPillarObjectId);
    }

    [Fact]
    public void AHeartTileWithoutAPillarContributesNothing()
    {
        var (db, world, steering) = Setup();
        HeartPillarTarget[] pillars = Pillars(db, world);
        Assert.Equal(3, pillars.Length);
        int heartTiles = 0;
        for (int y = 3; y <= 7; y++)
        for (int x = 10; x <= 14; x++)
        {
            var tile = new TilePos(x, y);
            if (pillars.Any(p => p.Tile == tile)) continue;
            heartTiles++;
            Assert.Empty(Query(steering, tile));
        }
        Assert.Equal(22, heartTiles);
    }

    [Fact]
    public void ARuinedHeartContributesNoPillars()
    {
        var (db, world, steering) = Setup();
        HeartPillarTarget[] pillars = Pillars(db, world);
        Assert.All(pillars, p => Assert.Single(Query(steering, p.Tile)));

        HeartRuin.Set(world, Owner, true);

        Assert.All(pillars, p => Assert.Empty(Query(steering, p.Tile)));
    }

    [Theory]
    [InlineData(false, PillarWidth)]
    [InlineData(true, 0)]
    public void AnArchPillarWithoutTheObstacleFlagOrAWidthContributesNothing(bool obstacle, int width)
    {
        var (db, world, steering) = Setup(obstacle, width);
        Assert.All(Pillars(db, world), p => Assert.Empty(Query(steering, p.Tile)));
    }
}
