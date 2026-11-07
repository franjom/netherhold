// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

public class HatcheryPillarSteeringTests
{
    private static (GameDatabase db, WorldState world, WorldSteering steering) Setup(
        bool obstacle = true, int width = 1634, int roomWidth = 3)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        ObjectDef pillar = basis.ObjectById(WorldSteering.ArchesObjectId)! with
        {
            ObjectId = HatcheryRules.PillarObjectId, Width = new Fp32(width),
            Flags = ObjectFlags.Pillar | (obstacle ? ObjectFlags.Obstacle : 0),
        };
        ObjectDef substitute = pillar with { ObjectId = 200, Flags = ObjectFlags.Pillar };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, objects: basis.Objects.Values.Append(pillar).Append(substitute));
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y <= 3; y++)
        for (int x = 1; x <= roomWidth; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Hatchery), PlayerIds.FirstKeeper);
        return (db, world, new WorldSteering(db, world));
    }

    private static List<SteeringObstacle> Query(WorldSteering steering, int x, int y)
    {
        var result = new List<SteeringObstacle>();
        steering.CollectObstacles(new Fixed16Vector(CreatureState.CentreOf(x) << 4,
            CreatureState.CentreOf(y) << 4), 4096, result);
        return result;
    }

    [Theory]
    [InlineData(1, 1, 7167, 7167)]
    [InlineData(3, 1, 13312, 7167)]
    [InlineData(1, 3, 7167, 13312)]
    [InlineData(3, 3, 13312, 13312)]
    public void CornerObstaclesUseTheNativeInwardPositionAndHalfWidth(int x, int y, int px, int py)
    {
        var (_, world, steering) = Setup();
        SteeringObstacle obstacle = Assert.Single(Query(steering, x, y));
        Assert.Equal(new Fixed16Vector(px << 4, py << 4), obstacle.Pos);
        Assert.Equal(13072, obstacle.Size);
        Assert.True(obstacle.Solid);
        Assert.False(obstacle.Flag2);
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == HatcheryRules.PillarObjectId);
    }

    [Theory]
    [InlineData(false, 1634, 3)]
    [InlineData(true, 0, 3)]
    [InlineData(true, 1634, 2)]
    public void AbsentObstacleWidthOrCornerSupportContributesNothing(bool obstacle, int width, int roomWidth)
    {
        var (_, _, steering) = Setup(obstacle, width, roomWidth);
        Assert.Empty(Query(steering, 1, 1));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void OnlyAnActivePhysicalPillarSuppressesTheVirtualOne(bool held, bool carried, bool expected)
    {
        var (db, world, steering) = Setup();
        ObjectState substitute = world.SpawnObject(db, 200, PlayerIds.FirstKeeper, world.Width + 1);
        substitute.InHand = held;
        if (carried) substitute.CarriedByCreatureId = 17;
        Assert.Equal(expected ? 1 : 0, Query(steering, 1, 1).Count);
    }

    [Fact]
    public void APhysicalHatcheryPillarContributesOnceAtItsStoredPosition()
    {
        var (db, world, steering) = Setup();
        ObjectState pillar = world.SpawnObject(db, HatcheryRules.PillarObjectId,
            PlayerIds.FirstKeeper, world.Width + 1);
        pillar.PosX = 7167; pillar.PosY = 7167;
        Assert.Equal(new Fixed16Vector(7167 << 4, 7167 << 4), Assert.Single(Query(steering, 1, 1)).Pos);
    }

    [Fact]
    public void InteriorTileHasNoVirtualPillar()
    {
        var (_, _, steering) = Setup();
        Assert.Empty(Query(steering, 2, 2));
    }
    [Fact]
    public void HypothesisVirtualPillarIsOldestAmongSameTileStoredObstacles()
    {
        var (basis, world, _) = Setup();
        ObjectDef obstacle = basis.ObjectById(200)! with { Flags = ObjectFlags.Obstacle };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, objects: basis.Objects.Values.Select(o => o.ObjectId == 200 ? obstacle : o));
        world.SpawnObject(db, 200, PlayerIds.FirstKeeper, world.Width + 1);
        List<SteeringObstacle> result = Query(new WorldSteering(db, world), 1, 1);
        Assert.Equal(2, result.Count);
        Assert.Equal(new Fixed16Vector(6144 << 4, 6144 << 4), result[0].Pos);
        Assert.True(result[0].Solid);
        Assert.Equal(new Fixed16Vector(7167 << 4, 7167 << 4), result[1].Pos);
        Assert.False(result[1].Solid);
    }

}
