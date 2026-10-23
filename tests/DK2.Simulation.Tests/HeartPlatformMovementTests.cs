// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class HeartPlatformMovementTests
{
    [Fact]
    public void NativeHeartCornerSegmentRemainsOpen()
    {
        var (_, _, steering, _) = Setup(new TilePos(2, 6));
        // Native integrations 3→4, translated from Heart (24.5,54.5) to (6.5,6.5).
        Assert.True(steering.CanMove(new Fixed16Vector(312349, 386071),
            new Fixed16Vector(339291, 372225)));
    }

    [Theory]
    [InlineData(-1.6, 0, -0.4, 0, false)]
    [InlineData(0, -1.6, 0, -0.4, false)]
    [InlineData(1.6, 0, 0.4, 0, false)]
    [InlineData(0, 1.6, 0, 0.4, false)]
    [InlineData(-1.6, 0, 1.6, 0, false)]
    [InlineData(-1.6, -1.4, 1.6, -1.4, true)]
    [InlineData(-1.6, -1.6, -1.4, -1.4, true)]
    [InlineData(-1.6, -1.6, 1.6, -1.6, true)]
    [InlineData(-1.6, 1.6, 1.6, 1.6, true)]
    [InlineData(-1.6, -1.6, -1.6, 1.6, true)]
    [InlineData(1.6, -1.6, 1.6, 1.6, true)]
    [InlineData(-1.6, -1.4, -1.4, -1.6, true)]
    [InlineData(-1.4, 0, -1.6, 0, true)]
    [InlineData(-0.4, 0, -0.6, 0, true)]
    [InlineData(-0.4, 0, -0.2, 0, false)]
    public void LiveCoreRejectsCrossingsAndLeavesTheOuterPlatformOpen(
        double x0, double y0, double x1, double y1, bool allowed)
    {
        var (db, world, steering, _) = Setup(new TilePos(2, 6));
        Assert.Equal(allowed, steering.CanMove(Point(x0, y0), Point(x1, y1)));
        world.Player(PlayerIds.FirstKeeper).HeartRuined = true;
        steering.Refresh();
        Assert.True(steering.CanMove(Point(x0, y0), Point(x1, y1)));
    }

    [Theory]
    [InlineData(0, 0, -0.625, 0)]
    [InlineData(-0.4, 0, -0.625, 0)]
    [InlineData(0, 0.4, -0.625, 0)]
    [InlineData(-1.4, -1.4, -1.4, -1.4)]
    [InlineData(1.4, 1.4, 1.4, 1.4)]
    public void OnlyCoreDestinationsMoveTowardTheApproach(double x, double y, double rimX, double rimY)
    {
        var (_, world, steering, _) = Setup(new TilePos(2, 6));
        Fixed16Vector target = steering.ConstrainTarget(Point(-4, 0), Point(x, y));
        Assert.Equal(Point(rimX, rimY), target);
        world.Player(PlayerIds.FirstKeeper).HeartRuined = true;
        Assert.Equal(Point(x, y), steering.ConstrainTarget(Point(-4, 0), Point(x, y)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoblinFinishesTheWalkAfterItsInitialEscapeModeClears(bool ruined)
    {
        var (db, world, steering, goblin) = Setup(new TilePos(2, 6));
        world.Player(PlayerIds.FirstKeeper).HeartRuined = ruined;
        Assert.True(steering.TryRoute(goblin, new TilePos(10, 6), MoveOptions.Loose));
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        bool crossedCore = false;
        goblin.Body.EscapeStuckMode = true;
        for (int tick = 0; tick < 800 && goblin.Tile != new TilePos(10, 6); tick++)
        {
            sim.Tick(InputFrame.Empty);
            long dx = goblin.PosX - CreatureState.CentreOf(6);
            long dy = goblin.PosY - CreatureState.CentreOf(6);
            crossedCore |= dx * dx + dy * dy < (Fp32.One / 2L) * (Fp32.One / 2L);
            if (!ruined) Assert.False(crossedCore);
        }
        Assert.Equal(new TilePos(10, 6), goblin.Tile);
        Assert.Equal(ruined, crossedCore);
    }

    [Fact]
    public void ForcedEscapeNearTheHeartStillCannotEnterItsCore()
    {
        var (db, world, steering, goblin) = Setup(new TilePos(5, 6));
        Assert.True(steering.TryRoute(goblin, new TilePos(10, 6), MoveOptions.Loose));
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        for (int tick = 0; tick < 60; tick++)
        {
            goblin.Body.EscapeStuckMode = true;
            goblin.Body.PathStuckBuffer = 0x7FFF;
            sim.Tick();
            Assert.True(goblin.Body.EscapeStuckMode);
            long dx = goblin.PosX - CreatureState.CentreOf(6);
            long dy = goblin.PosY - CreatureState.CentreOf(6);
            Assert.True(dx * dx + dy * dy >= (Fp32.One / 2L) * (Fp32.One / 2L));
        }
    }

    [Fact]
    public void PlatformCornerDestinationsRemainAtTheirNativePosition()
    {
        var (_, world, steering, _) = Setup(new TilePos(2, 6));
        Assert.Equal(Point(0, -1.2), steering.ConstrainTarget(Point(-4, 0), Point(0, -1.2)));
    }

    private static Fixed16Vector Point(double x, double y) =>
        new((int)((6.5 + x) * Fixed16.One), (int)((6.5 + y) * Fixed16.One));

    [Fact]
    public void ALoadedGoblinInsideThePlatformCanRouteOutwardOntoTheRim()
    {
        var (db, world, steering, goblin) = Setup(new TilePos(6, 6));
        Assert.True(steering.TryRoute(goblin, new TilePos(10, 6), MoveOptions.Loose));
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        for (int tick = 0; tick < 400 && goblin.Tile != new TilePos(10, 6); tick++) sim.Tick(InputFrame.Empty);
        Assert.Equal(new TilePos(10, 6), goblin.Tile);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(7, 7)]
    public void AnImpDroppedOnAPlatformTileWalksOffIt(int x, int y)
    {
        // The owner's photos, 2026-10-23: dropped on one of the eight, the Imp stays and walks away.
        var (db, world, steering, _) = Setup(new TilePos(2, 6));
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, PlayerIds.FirstKeeper, new TilePos(2, 2));
        var hand = new GameSimulation(db, world, 5, [new HandSystem()]);
        hand.Tick(InputFrame.Of(new Commands.PickUpCreatureCommand(PlayerIds.FirstKeeper, imp.Id)));
        hand.Tick(InputFrame.Of(new Commands.DropCommand(PlayerIds.FirstKeeper, new TilePos(x, y))));
        Assert.Equal(new TilePos(x, y), imp.Tile);

        Assert.True(steering.TryRoute(imp, new TilePos(10, 6), MoveOptions.Loose));
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        for (int tick = 0; tick < 400 && imp.Tile != new TilePos(10, 6); tick++) sim.Tick(InputFrame.Empty);
        Assert.Equal(new TilePos(10, 6), imp.Tile);
    }

    [Fact]
    public void RuiningAHeartRemovesCoreProtectionWithoutChangingTheRoutingBytes()
    {
        var (db, world, steering, goblin) = Setup(new TilePos(2, 6));
        byte[] nativeBytes = WorldSteering.CaptureNavigationBytes(db, world);
        Assert.True(steering.TryRoute(goblin, new TilePos(6, 6), MoveOptions.ExactSpot));
        Assert.Equal(Point(-0.625, 0), goblin.Body.PathTargetPos);
        int before = steering.Rebuilds;
        world.Player(PlayerIds.FirstKeeper).HeartRuined = true;
        steering.Refresh();
        Assert.Equal(before, steering.Rebuilds);
        Assert.Equal(nativeBytes, WorldSteering.CaptureNavigationBytes(db, world));
        Assert.Equal(DK2.Pathfinding.NavBytes.Floor, steering.NavByte(6, 6));
        Assert.True(steering.TryRoute(goblin, new TilePos(6, 6), MoveOptions.ExactSpot));
        Assert.Equal(Point(0, 0), goblin.Body.PathTargetPos);
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        for (int tick = 0; tick < 400 && !goblin.Body.PathFinished; tick++) sim.Tick(InputFrame.Empty);
        Assert.Equal(new TilePos(6, 6), goblin.Tile);
    }

    private static (GameDatabase, WorldState, WorldSteering, CreatureState) Setup(TilePos from)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 2; y <= 10; y++)
            for (int x = 2; x <= 10; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = PlayerIds.FirstKeeper;
                if (x >= 4 && x <= 8 && y >= 4 && y <= 8)
                    RoomRules.Place(db, ref tile, db.Room(RoomTypeId.DungeonHeart), PlayerIds.FirstKeeper);
            }
        var steering = new WorldSteering(db, world);
        steering.Refresh();
        CreatureState goblin = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, from);
        return (db, world, steering, goblin);
    }
}
