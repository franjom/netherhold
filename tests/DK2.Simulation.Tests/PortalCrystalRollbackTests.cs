// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Tests;

public class PortalCrystalRollbackTests
{
    [Theory]
    [InlineData(RoomTypeId.Portal)]
    [InlineData(RoomTypeId.HeroPortal)]
    public void CrystalPillarsAllowCrossingsAndDoNotProjectTargets(RoomTypeId type)
    {
        var navigation = Setup(type);
        Assert.True(navigation.CanMove(Point(4.2, 4.7), Point(5.6, 4.7)));
        Assert.True(navigation.CanMove(Point(4.2, 5.8), Point(5.6, 5.8)));
        Assert.True(navigation.CanMove(Point(5.5, 5.0), Point(6.6, 5.0)));
        Assert.True(navigation.CanMove(Point(5.5, 6.0), Point(6.6, 6.0)));
        var target = Point(5.0, 4.7);
        Assert.Equal(target, navigation.ConstrainTarget(Point(4.2, 4.7), target));
        Assert.Equal(NavBytes.PortalHole, navigation.NavByte(5, 5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SteeringCrossesPillarsWithoutContactsOrDeflection(bool escape)
    {
        var navigation = Setup(RoomTypeId.Portal);
        var body = new SteeringBody
        {
            Pos = Point(5.32, 4.5), Vel = Point(-.25, 0), Radius = 5200,
            EscapeStuckMode = escape, MaxSpeed = Fixed16.One,
        };
        var expected = body.Pos + body.Vel;
        SteeringIntegrator.Step(body, Fixed16Vector.Zero, MoverProfile.Walker, navigation);
        Assert.Empty(body.SolidContacts);
        Assert.Equal(expected, body.Pos);
    }

    private static Fixed16Vector Point(double x, double y) =>
        new((int)(x * Fixed16.One), (int)(y * Fixed16.One));

    private static WorldSteering Setup(RoomTypeId type)
    {
        var database = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 2; y <= 9; y++)
        for (int x = 2; x <= 9; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
            if (x >= 4 && x <= 6 && y >= 4 && y <= 6)
                RoomRules.Place(database, ref world.Tile(x, y), database.Room(type), PlayerIds.FirstKeeper);
        }
        var navigation = new WorldSteering(database, world);
        navigation.Refresh();
        return navigation;
    }
}
