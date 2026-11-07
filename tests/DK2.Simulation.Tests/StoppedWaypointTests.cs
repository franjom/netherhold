// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class StoppedWaypointTests
{
    [Theory]
    [InlineData(PlayerIds.Neutral, 87)]
    [InlineData(PlayerIds.FirstKeeper, ResearchStep.ChoosingState)]
    [InlineData(PlayerIds.FirstKeeper, 199)]
    public void StoppedWaypointUsesNavigationPrecisionWhileBodyAndTargetStayExact(byte owner, int state)
    {
        var (_, _, creature) = Setup((PlayerId)owner, state);
        creature.Body.Vel = new Fixed16Vector(777, -888);

        creature.StopMoving();

        Assert.Equal(new Fixed16Vector(428192, 1972704), creature.Body.Pos);
        Assert.Equal(creature.Body.Pos, creature.Body.PathTargetPos);
        Assert.Equal(new Fixed16Vector(428032, 1972480), creature.Body.CurrentPoint);
        Assert.Equal(Fixed16Vector.Zero, creature.Body.Vel);
        Assert.Equal(1, creature.Body.PathTotalCount);
        Assert.False(creature.Body.PathFinished);
    }

    [Fact]
    public void TheStoppedWaypointDirectsTheExistingEscapeNudge()
    {
        var (db, world, creature) = Setup(PlayerIds.FirstKeeper, ResearchStep.ChoosingState);
        creature.StopMoving();
        StatePlacement.Place(creature, ResearchStep.ReturningState);
        creature.Body.EscapeStuckMode = true;
        creature.Heading = 1050;

        new GameSimulation(db, world, 1, [new CreatureMovementSystem()]).Tick();

        Assert.Equal(new Fixed16Vector(-2, 0), creature.Body.Vel);
        Assert.Equal(new Fixed16Vector(428190, 1972704), creature.Body.Pos);
    }

    [Fact]
    public void AnOwnedIdleStopKeepsItsExistingEmptyRouteAdapter()
    {
        var (_, _, creature) = Setup(PlayerIds.FirstKeeper, CreatureStateMachine.Idle);

        creature.StopMoving();

        Assert.Equal(0, creature.Body.PathTotalCount);
        Assert.Equal(new Fixed16Vector(428192, 1972704), creature.Body.Pos);
    }

    private static (GameDatabase, WorldState, CreatureState) Setup(PlayerId owner, int state)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 40),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
        for (int x = 1; x < world.Width - 1; x++)
        {
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
            world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
        }
        var creature = world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId, owner, new TilePos(6, 30));
        creature.PosX = 26762;
        creature.PosY = 123294;
        StatePlacement.Place(creature, state);
        return (db, world, creature);
    }
}
