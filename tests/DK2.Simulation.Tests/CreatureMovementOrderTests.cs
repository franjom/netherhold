// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class CreatureMovementOrderTests
{
    [Theory]
    [InlineData(true, 21478, 21112)]
    [InlineData(false, 21471, 21111)]
    public void CrowdQueryIncludesATileCrossingOnlyWhenTheOtherWalkerIsNewer(
        bool crossingWalkerIsNewer, int expectedVelocity, int expectedPosition)
    {
        var db = Dk2TerrainFixture.Database();
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
            }
        CreatureState first = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.FirstKeeper, new TilePos(4, 4));
        CreatureState second = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.FirstKeeper, new TilePos(6, 4));
        var hooks = new StateHookContext(world, db);
        CreatureStateMachine.BeginTravel(hooks, first, 6, CreatureStateMachine.Idle);
        CreatureStateMachine.BeginTravel(hooks, second, 6, CreatureStateMachine.Idle);
        CreatureState observed = crossingWalkerIsNewer ? first : second;
        CreatureState crossing = crossingWalkerIsNewer ? second : first;
        // Q613's captured boundary translated by (-20,-48) tiles; native arithmetic outcomes.
        Prepare(observed, position: new(19770 << 4, 19770 << 4), velocity: new(21421, 21421),
            waypoint: new(393216, 393216), target: new(393216, 393216), heading: 768);
        Prepare(crossing, position: new(26021 << 4, 20238 << 4), velocity: new(-9640, 28908),
            waypoint: new(316672, 622336), target: new(316736, 622576), heading: 1128);

        new GameSimulation(db, world, 1, [new CreatureMovementSystem()]).Tick();

        Assert.Equal(new TilePos(6, 5), crossing.Tile);
        Assert.Equal(new Fixed16Vector(expectedVelocity, expectedVelocity), observed.Body.Vel);
        Assert.Equal(expectedPosition, observed.PosX);
        Assert.Equal(expectedPosition, observed.PosY);
        Assert.Same(first, world.Creatures[0]);
        Assert.Same(second, world.Creatures[1]);
    }

    private static void Prepare(CreatureState creature, Fixed16Vector position, Fixed16Vector velocity,
        Fixed16Vector waypoint, Fixed16Vector target, int heading)
    {
        creature.PosX = position.X >> 4;
        creature.PosY = position.Y >> 4;
        creature.Heading = heading;
        WorldSteering.SyncBody(creature);
        creature.Body.Vel = velocity;
        RouteWindow.Load(creature.Body, [waypoint], target, 0);
    }
}
