// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The crystal footprints are convex hulls (ADR-0527 amendment 2), not the full
/// bounding boxes they replaced: movement must block their interior, and only it.</summary>
public class PortalCrystalFootprintTests
{
    // Pieces 0, 3, 5 and 8 of a portal on tiles 4..6, in world tiles.
    private static readonly (double X, double Y)[][] Hulls = new[] { (0, 4, 4), (3, 4, 5), (5, 6, 5), (8, 6, 6) }
        .Select(p => PortalCrystalShape.ForPiece(p.Item1)!.Vertices
            .Select(v => (p.Item2 + 0.5 + (v.X / 65536.0), p.Item3 + 0.5 + (v.Y / 65536.0))).ToArray())
        .ToArray();

    [Fact]
    public void AZeroRadiusSegmentIsBlockedExactlyWhenItEntersAHull()
    {
        var (_, _, navigation) = Setup();
        int blocked = 0, open = 0;
        foreach (var hull in Hulls)
        {
            double cx = hull.Average(v => v.X), cy = hull.Average(v => v.Y);
            for (int k = 0; k < 16; k++)
            for (int o = -8; o <= 8; o++)
            {
                double dx = Math.Cos(k * Math.PI / 16), dy = Math.Sin(k * Math.PI / 16), off = o * 0.05;
                var (x0, y0) = (cx + (0.6 * dx) - (off * dy), cy + (0.6 * dy) + (off * dx));
                var (x1, y1) = (cx - (0.6 * dx) - (off * dy), cy - (0.6 * dy) + (off * dx));
                double depth = Enumerable.Range(0, 1201).Min(i => Nearest(x0 + ((x1 - x0) * i / 1200), y0 + ((y1 - y0) * i / 1200)));
                if (Math.Abs(depth) < 1e-3 || Nearest(x0, y0) < 1e-3 || Nearest(x1, y1) < 1e-3) continue;
                bool allowed = navigation.CanMove(Point(x0, y0), Point(x1, y1));
                Assert.True(allowed == depth > 0, $"{x0:F3},{y0:F3} -> {x1:F3},{y1:F3}: nearest {depth:F4}");
                if (allowed) open++; else blocked++;
            }
        }
        Assert.True(blocked > 100 && open > 100, $"{blocked} blocked, {open} open");
    }

    // The bound is the documented 1.2 radii (0.12 for 0.1); the sharpest shipped bevel overshoots ~1.1.
    [Fact]
    public void ARadiusPadsEachHullByItAndAtMostItsBevel()
    {
        var (_, _, navigation) = Setup();
        Fp32 radius = Fp32.FromDouble(.1);
        for (double y = 4.0; y <= 7.0; y += 0.02)
        for (double x = 4.0; x <= 7.0; x += 0.02)
        {
            double nearest = Nearest(x, y);
            bool moved = navigation.ConstrainTarget(Point(1.5, 1.5), Point(x, y), radius) != Point(x, y);
            if (nearest < 0.1 - 1e-3) Assert.True(moved, $"{x:F2},{y:F2} is {nearest:F3} from a crystal");
            if (nearest > 0.12) Assert.False(moved, $"{x:F2},{y:F2} is {nearest:F3} from a crystal");
        }
    }

    // The former bounding boxes bowed these departures 0.18-0.50 tile off the straight line.
    [Theory]
    [InlineData(1, 4.95, 5.5, 2.5, 8.5)]
    [InlineData(1, 6.05, 5.5, 8.5, 8.5)]
    [InlineData(1, 5.5, 4.95, 8.5, 2.5)]
    [InlineData(7, 4.95, 5.5, 2.5, 8.5)]
    [InlineData(7, 6.05, 5.5, 8.5, 8.5)]
    public void ACreatureLeavingTheHoleWalksStraightPastACrystalWithoutEnteringIt(byte type, double x0, double y0, double x1, double y1)
    {
        var (db, world, _) = Setup();
        var creature = world.SpawnCreature(db, type, PlayerIds.FirstKeeper, new TilePos((int)x0, (int)y0));
        creature.PosX = (int)(x0 * Fp32.One); creature.PosY = (int)(y0 * Fp32.One);
        var sim = new GameSimulation(db, world, 42, [new CreatureMovementSystem()]);
        Assert.True(sim.Navigation.TryRouteToStance(creature, (int)(x1 * Fp32.One), (int)(y1 * Fp32.One), MoveOptions.ExactSpot));
        double length = Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0))), deviation = 0;
        for (int tick = 0; tick < 300 && !creature.Body.PathFinished; tick++)
        {
            sim.Tick();
            double x = creature.PosX / (double)Fp32.One, y = creature.PosY / (double)Fp32.One;
            deviation = Math.Max(deviation, Math.Abs(((x - x0) * (y1 - y0)) - ((y - y0) * (x1 - x0))) / length);
            Assert.True(Nearest(x, y) >= (creature.Body.Radius / 65536.0) - (1 / 512.0), $"tick {tick} at {x:F3},{y:F3}");
        }
        Assert.True(creature.Body.PathFinished);
        Assert.True(deviation < 0.08, $"bowed {deviation:F3} tile off the line");
    }

    private static Fixed16Vector Point(double x, double y) => new((int)(x * Fixed16.One), (int)(y * Fixed16.One));

    /// <summary>Signed distance to the nearest hull: negative inside one.</summary>
    private static double Nearest(double x, double y) => Hulls.Min(h => Signed(h, x, y));

    private static double Signed((double X, double Y)[] hull, double x, double y)
    {
        double best = double.MaxValue;
        bool inside = true;
        int sign = 0;
        for (int i = 0; i < hull.Length; i++)
        {
            var (a, b) = (hull[i], hull[(i + 1) % hull.Length]);
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double t = Math.Clamp((((x - a.X) * ex) + ((y - a.Y) * ey)) / ((ex * ex) + (ey * ey)), 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(a.X + (t * ex) - x, 2) + Math.Pow(a.Y + (t * ey) - y, 2)));
            int s = Math.Sign((ex * (y - a.Y)) - (ey * (x - a.X)));
            if (s != 0 && sign != 0 && s != sign) inside = false;
            if (s != 0) sign = s;
        }
        return inside ? -best : best;
    }

    private static (GameDatabase, WorldState, WorldSteering) Setup()
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y <= 10; y++) for (int x = 1; x <= 10; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
        }
        for (int y = 4; y <= 6; y++) for (int x = 4; x <= 6; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Portal), PlayerIds.FirstKeeper);
        var navigation = new WorldSteering(db, world); navigation.Refresh();
        return (db, world, navigation);
    }
}
