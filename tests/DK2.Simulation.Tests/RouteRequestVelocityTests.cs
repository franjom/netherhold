// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

/// <summary>Route requests and stops zero the body's velocity: <c>Creature_RequestRoute</c>
/// (<c>0x004D0F60</c>) and <c>Creature_StopMoving</c> (<c>0x0048ECE0</c>), ADR-0513.</summary>
public class RouteRequestVelocityTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly Fixed16Vector Moving = new(2134, -7286);

    [Fact]
    public void AFoundRouteStartsFromZeroVelocityAtTheEntityPosition()
    {
        var (db, world, imp) = MovingImp();
        Assert.True(new WorldSteering(db, world).TryRouteTo(
            imp, CreatureState.CentreOf(10), CreatureState.CentreOf(6), MoveOptions.Plain));
        Assert.Equal(Fixed16Vector.Zero, imp.Body.Vel);
        Assert.Equal(new Fixed16Vector(imp.PosX << 4, imp.PosY << 4), imp.Body.Pos);
    }

    [Fact]
    public void ARequestOnTheSameTileAndAStanceRequestAlsoZeroIt()
    {
        var (db, world, imp) = MovingImp();
        var navigation = new WorldSteering(db, world);
        Assert.True(navigation.TryRouteTo(imp, imp.PosX, imp.PosY, MoveOptions.Plain));
        Assert.Equal(Fixed16Vector.Zero, imp.Body.Vel);

        imp.Body.Vel = Moving;
        Assert.True(navigation.TryRouteToStance(imp, imp.PosX + 512, imp.PosY, MoveOptions.Loose));
        Assert.Equal(Fixed16Vector.Zero, imp.Body.Vel);
    }

    [Fact]
    public void StopMovingDropsTheRouteAndStopsTheBodyWhereTheCreatureStands()
    {
        var (_, _, imp) = MovingImp();
        imp.Body.Pos = new Fixed16Vector((imp.PosX << 4) + 7, (imp.PosY << 4) + 15);
        var target = imp.Body.Pos + new Fixed16Vector(1024, 0);
        RouteWindow.Load(imp.Body, [target], target, 0);
        imp.RouteOptions = MoveOptions.Loose;

        imp.StopMoving();

        Assert.False(imp.HasRoute);
        Assert.Equal(MoveOptions.Plain, imp.RouteOptions);
        Assert.Equal(Fixed16Vector.Zero, imp.Body.Vel);
        Assert.Equal(new Fixed16Vector(imp.PosX << 4, imp.PosY << 4), imp.Body.Pos);
    }

    private static (GameDatabase, WorldState, CreatureState) MovingImp()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }

        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(7, 6));
        imp.Body.Vel = Moving;
        return (db, world, imp);
    }
}
