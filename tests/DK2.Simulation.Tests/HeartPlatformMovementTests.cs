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
    [Theory]
    [InlineData(-1.6, 0, -0.6, 0, false)]
    [InlineData(0, -1.6, 0, -0.6, false)]
    [InlineData(1.6, 0, 0.6, 0, false)]
    [InlineData(0, 1.6, 0, 0.6, false)]
    [InlineData(-1.6, -1.4, 1.6, -1.4, false)]
    [InlineData(-1.6, -1.6, -1.4, -1.4, false)]
    [InlineData(-1.6, -1.6, 1.6, -1.6, true)]
    [InlineData(-1.6, 1.6, 1.6, 1.6, true)]
    [InlineData(-1.6, -1.6, -1.6, 1.6, true)]
    [InlineData(1.6, -1.6, 1.6, 1.6, true)]
    [InlineData(-1.6, -1.4, -1.4, -1.6, true)]
    [InlineData(-1.4, 0, -1.6, 0, true)]
    [InlineData(-1.4, 0, -1.2, 0, false)]
    public void LivePlatformRejectsCrossingsAndKeepsEveryRimOpen(
        double x0, double y0, double x1, double y1, bool allowed)
    {
        var (db, world, steering, _) = Setup(new TilePos(2, 6));
        Assert.Equal(allowed, steering.CanMove(Point(x0, y0), Point(x1, y1)));
        world.Player(PlayerIds.FirstKeeper).HeartRuined = true;
        steering.Refresh();
        Assert.True(steering.CanMove(Point(x0, y0), Point(x1, y1)));
    }

    [Theory]
    [InlineData(0, 0, -1.625, 0)]
    [InlineData(-1.4, -1.4, -1.625, -1.4)]
    [InlineData(1.4, 1.4, 1.625, 1.4)]
    [InlineData(0, -1.2, 0, -1.625)]
    public void PlatformDestinationsMoveOntoTheirNearestRimEdge(double x, double y, double rimX, double rimY)
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
    public void GoblinEscapingAStuckRouteCannotCrossTheLivePlatform(bool ruined)
    {
        var (db, world, steering, goblin) = Setup(new TilePos(2, 6));
        world.Player(PlayerIds.FirstKeeper).HeartRuined = ruined;
        Assert.True(steering.TryRoute(goblin, new TilePos(10, 6), MoveOptions.Loose));
        var sim = new GameSimulation(db, world, 5, [new CreatureMovementSystem()]);
        bool crossedPlatform = false;
        for (int tick = 0; tick < 800 && goblin.Tile != new TilePos(10, 6); tick++)
        {
            goblin.Body.EscapeStuckMode = true;
            sim.Tick(InputFrame.Empty);
            crossedPlatform |= goblin.PosX > 5 * Fp32.One && goblin.PosX < 8 * Fp32.One
                && goblin.PosY > 5 * Fp32.One && goblin.PosY < 8 * Fp32.One;
            if (!ruined) Assert.False(crossedPlatform);
        }
        Assert.Equal(new TilePos(10, 6), goblin.Tile);
        Assert.Equal(ruined, crossedPlatform);
    }

    [Fact]
    public void ABlockedNearestRimEdgeFallsBackToTheExistingApproachFloor()
    {
        var (_, world, steering, _) = Setup(new TilePos(2, 6));
        world.Tile(6, 4).TerrainId = Dk2TerrainFixture.Rock;
        Assert.Equal(Point(-1.625, 0), steering.ConstrainTarget(Point(-4, 0), Point(0, -1.2)));
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

    [Fact]
    public void RuiningAHeartRebuildsOnlyTheRoutingMaskAndAllowsItsCentreAgain()
    {
        var (db, world, steering, goblin) = Setup(new TilePos(2, 6));
        byte[] nativeBytes = WorldSteering.CaptureNavigationBytes(db, world);
        Assert.True(steering.TryRoute(goblin, new TilePos(6, 6), MoveOptions.ExactSpot));
        Assert.True(goblin.Body.PathTargetPos.X < 5 << 16);
        int before = steering.Rebuilds;
        world.Player(PlayerIds.FirstKeeper).HeartRuined = true;
        steering.Refresh();
        Assert.Equal(before + 1, steering.Rebuilds);
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
