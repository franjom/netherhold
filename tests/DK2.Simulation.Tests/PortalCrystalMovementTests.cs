// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class PortalCrystalMovementTests
{
    [Theory]
    [InlineData(RoomTypeId.Portal)]
    [InlineData(RoomTypeId.HeroPortal)]
    public void CrystalFootprintsRejectCrossingsButLeaveTheGapsOpen(RoomTypeId type)
    {
        var (_, _, navigation) = Setup(type);
        Assert.False(navigation.CanMove(Point(4.2, 4.7), Point(5.6, 4.7)));
        Assert.False(navigation.CanMove(Point(4.2, 5.8), Point(5.6, 5.8)));
        Assert.False(navigation.CanMove(Point(5.5, 5.0), Point(6.6, 5.0)));
        Assert.False(navigation.CanMove(Point(5.5, 6.0), Point(6.6, 6.0)));
        Assert.True(navigation.CanMove(Point(4.2, 5.3), Point(5.6, 5.3)));
        Assert.True(navigation.CanMove(Point(3, 4), Point(7, 4)));
    }

    private static Fixed16Vector Point(double x, double y) => new((int)(x * Fixed16.One), (int)(y * Fixed16.One));

    [Fact]
    public void ATargetIsProjectedBeforeTheFirstRouteAndAfterRoomEdits()
    {
        var (db, world, _) = Setup();
        var navigation = new WorldSteering(db, world);
        var from = Point(4.2, 4.7); var target = Point(5, 4.7);
        Assert.NotEqual(target, navigation.ConstrainTarget(from, target));
        for (int y = 4; y <= 6; y++) for (int x = 4; x <= 6; x++)
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        Assert.Equal(target, navigation.ConstrainTarget(from, target));
        RoomRules.Place(db, ref world.Tile(4, 4), db.Room(RoomTypeId.Portal), PlayerIds.FirstKeeper);
        Assert.NotEqual(target, navigation.ConstrainTarget(from, target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleCrystalsOnNarrowOrSplitOwnershipFragmentsStillCollide(bool splitOwnership)
    {
        var (_, world, navigation) = Setup();
        for (int y = 4; y <= 6; y++) for (int x = 5; x <= 6; x++)
        {
            if (splitOwnership) world.Tile(x, y).OwnerPlayerId = 4;
            else world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        }
        navigation.Refresh();
        Assert.False(navigation.CanMove(Point(4.2, 4.7), Point(5.6, 4.7)));
        Assert.False(navigation.CanMove(Point(4.2, 5.8), Point(5.6, 5.8)));
    }

    [Theory]
    [InlineData(7.5, 4.7, 3.5, 4.7)]
    [InlineData(3.5, 5.9, 7.5, 5.9)]
    [InlineData(7.5, 5.9, 3.5, 5.9)]
    [InlineData(5.0, 3.5, 5.0, 7.5)]
    [InlineData(5.0, 7.5, 5.0, 3.5)]
    [InlineData(6.0, 3.5, 6.0, 7.5)]
    [InlineData(6.0, 7.5, 6.0, 3.5)]
    [InlineData(3.5, 3.5, 7.5, 7.5)]
    [InlineData(7.5, 7.5, 3.5, 3.5)]
    [InlineData(3.5, 7.5, 7.5, 3.5)]
    [InlineData(7.5, 3.5, 3.5, 7.5)]
    public void RoutesFromEverySideFinishWithoutCrossingCrystals(double x0, double y0, double x1, double y1)
    {
        var (db, world, navigation) = Setup();
        var creature = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, new TilePos((int)x0, (int)y0));
        creature.PosX = (int)(x0 * Fp32.One); creature.PosY = (int)(y0 * Fp32.One);
        Assert.True(navigation.TryRouteToStance(creature, (int)(x1 * Fp32.One), (int)(y1 * Fp32.One), MoveOptions.ExactSpot));
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem()]);
        for (int tick = 0; tick < 1000 && !creature.Body.PathFinished; tick++)
        {
            var before = new Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
            sim.Tick();
            Assert.True(navigation.CanMove(before, new(creature.PosX << 4, creature.PosY << 4), new Fp32(creature.Body.Radius >> 4)));
        }
        Assert.True(creature.Body.PathFinished, $"stopped at {creature.PosX / (double)Fp32.One},{creature.PosY / (double)Fp32.One}");
        Assert.Equal(new TilePos((int)x1, (int)y1), creature.Tile);
    }

    [Fact]
    public void BodyRadiusTargetsAndRebuildsRespectTheCrystals()
    {
        var (_, world, navigation) = Setup();
        Assert.True(navigation.CanMove(Point(4.2, 4.3), Point(5.6, 4.3)));
        Assert.False(navigation.CanMove(Point(4.2, 4.3), Point(5.6, 4.3), Fp32.FromDouble(.2)));
        var projected = navigation.ConstrainTarget(Point(4.2, 4.7), Point(5.0, 4.7), Fp32.FromDouble(.2));
        Assert.True(projected.X < Point(4.58, 4.7).X);
        Assert.True(navigation.CanMove(Point(4.2, 4.7), projected, Fp32.FromDouble(.2)));
        Assert.True(navigation.CanMove(Point(4.8, 4.7), Point(4.6, 4.7)));
        Assert.False(navigation.CanMove(Point(4.8, 4.7), Point(5.0, 4.7)));
        for (int y = 4; y <= 6; y++) for (int x = 4; x <= 6; x++)
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        navigation.Refresh();
        Assert.True(navigation.CanMove(Point(4.2, 4.7), Point(5.6, 4.7)));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(7, false)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    public void CreaturesWalkAroundThePillarsEvenInEscapeMode(byte type, bool escape)
    {
        var (db, world, navigation) = Setup();
        var creature = world.SpawnCreature(db, type, PlayerIds.FirstKeeper, new TilePos(3, 4));
        creature.PosY = (int)(4.7 * Fp32.One);
        Assert.True(navigation.TryRouteToStance(creature, (int)(7.5 * Fp32.One), creature.PosY, MoveOptions.ExactSpot));
        creature.Body.EscapeStuckMode = escape;
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem()]);
        for (int tick = 0; tick < 600 && !creature.Body.PathFinished; tick++)
        {
            var before = Point((double)creature.PosX / Fp32.One, (double)creature.PosY / Fp32.One);
            sim.Tick();
            var after = new Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
            Assert.True(navigation.CanMove(before, after, new Fp32(creature.Body.Radius >> 4)));
        }
        Assert.Equal(new TilePos(7, 4), creature.Tile);
        Assert.True(creature.Body.PathFinished);
    }

    private static (GameDatabase, WorldState, WorldSteering) Setup(RoomTypeId type = RoomTypeId.Portal)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 2; y <= 9; y++) for (int x = 2; x <= 9; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
            if (x >= 4 && x <= 6 && y >= 4 && y <= 6)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), PlayerIds.FirstKeeper);
        }
        var navigation = new WorldSteering(db, world); navigation.Refresh();
        return (db, world, navigation);
    }
}
