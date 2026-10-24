// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class PortalCrystalSteeringRegressionTests
{
    [Fact]
    public void PortalRoutesNearWallsFinishWithoutCrossingCrystalsOrSolidTiles()
    {
        var failures = new List<string>();
        foreach (int wall in Enumerable.Range(0, 5))
        foreach (bool reverse in new[] { false, true })
        foreach (double lane in new[] { 3.2, 3.5, 3.8, 4.0, 4.5, 5.0, 6.0, 6.5, 6.8, 7.0, 7.5 })
        {
            var (db, world) = Setup(wall);
            bool vertical = wall < 3;
            double x0 = vertical ? lane : 2.5, y0 = vertical ? 2.5 : lane;
            double x1 = vertical ? lane : 8.5, y1 = vertical ? 8.5 : lane;
            if (reverse) { (x0, x1) = (x1, x0); (y0, y1) = (y1, y0); }
            if (world.Tile((int)x0, (int)y0).TerrainId == Dk2TerrainFixture.ImpenetrableRock) continue;
            var creature = world.SpawnCreature(db, 7, PlayerIds.FirstKeeper, new TilePos((int)x0, (int)y0));
            creature.PosX = (int)(x0 * Fp32.One); creature.PosY = (int)(y0 * Fp32.One);
            var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem()]);
            Assert.True(sim.Navigation.TryRouteToStance(creature, (int)(x1 * Fp32.One), (int)(y1 * Fp32.One), MoveOptions.ExactSpot));
            for (int tick = 0; tick < 1000 && !creature.Body.PathFinished; tick++)
            {
                var before = new Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
                sim.Tick();
                Assert.True(sim.Navigation.CanMove(before, new(creature.PosX << 4, creature.PosY << 4), new Fp32(creature.Body.Radius >> 4)));
                Assert.NotEqual(Dk2TerrainFixture.ImpenetrableRock, world.Tile(creature.Tile).TerrainId);
            }
            if (!creature.Body.PathFinished || creature.Tile != new TilePos((int)x1, (int)y1))
                failures.Add($"wall={wall}, reverse={reverse}, lane={lane}: stopped at {creature.PosX / (double)Fp32.One:F3},{creature.PosY / (double)Fp32.One:F3}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void CrystalContactsPredictCrossingButLeaveClearSideMotionAlone()
    {
        var (db, world) = Setup(0);
        var nav = new WorldSteering(db, world); nav.Refresh();
        // 5.32 (was 5.52) by design: the hull footprint is smaller than the old box (ADR-0527 amendment 2).
        var body = new SteeringBody { Pos = Point(5.32, 4.5), Radius = 5200, EscapeStuckMode = true };
        nav.CollectMovementContacts(body, Point(-.25, 0));
        Assert.Single(body.SolidContacts);
        body.SolidContacts.Clear();
        body.Pos = Point(5.4, 4.74);
        nav.CollectMovementContacts(body, Point(0, -.02));
        Assert.Empty(body.SolidContacts);
        var ordinary = new List<SteeringObstacle>(); nav.CollectObstacles(body.Pos, Fixed16.One, ordinary);
        Assert.Empty(ordinary);
    }

    private static Fixed16Vector Point(double x, double y) => new((int)(x * Fixed16.One), (int)(y * Fixed16.One));

    [Fact]
    public void ArrivalBeforeACrystalDoesNotUseTheOvershootingVelocityForContacts()
    {
        var (db, world) = Setup(0);
        var nav = new WorldSteering(db, world); nav.Refresh();
        var body = new SteeringBody { Pos = Point(5.6, 4.6), Vel = Point(-.4, 0), Radius = 5200,
            PathFlags = 4, PathTotalCount = 1, MaxSpeed = Fixed16.One };
        body.Subpath[0] = Point(5.5, 4.6);
        SteeringIntegrator.Step(body, Fixed16Vector.Zero, MoverProfile.Walker, nav);
        Assert.Empty(body.SolidContacts);
        Assert.Equal(Point(5.5, 4.6), body.Pos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASweptContactSteersBeforeOverlapIncludingStuckEscape(bool escape)
    {
        var (db, world) = Setup(0);
        var nav = new WorldSteering(db, world); nav.Refresh();
        var body = new SteeringBody { Pos = Point(5.32, 4.5), Vel = Point(-.25, 0), Radius = 5200,
            EscapeStuckMode = escape, MaxSpeed = Fixed16.One };
        var from = body.Pos;
        SteeringIntegrator.Step(body, Fixed16Vector.Zero, MoverProfile.Walker, nav);
        Assert.Single(body.SolidContacts);
        Assert.NotEqual(from.Y, body.Pos.Y);
        Assert.True(nav.CanMove(from, body.Pos, new Fp32(body.Radius >> 4)));
    }

    private static (GameDatabase, WorldState) Setup(int wall)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y <= 10; y++) for (int x = 1; x <= 10; x++)
        { world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath; world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper; }
        for (int y = 4; y <= 6; y++) for (int x = 4; x <= 6; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), PlayerIds.FirstKeeper);
        for (int p = 2; p <= 9; p++)
        {
            if (wall == 1) world.Tile(3, p).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
            if (wall == 2) world.Tile(7, p).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
            if (wall == 3) world.Tile(p, 3).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
            if (wall == 4) world.Tile(p, 7).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        }
        return (db, world);
    }

}
