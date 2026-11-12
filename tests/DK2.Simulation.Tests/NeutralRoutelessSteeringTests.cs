// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A neutral creature with the mover bit in its state steers with no route loaded; the
/// move gate of <see cref="CreatureMovementSystem.Update"/>.</summary>
public class NeutralRoutelessSteeringTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Goblin = Dk2TerrainFixture.GoblinCreatureId;

    // Level 5: neutral slot 449 stands in 87 with no route, is pushed on turn 1620 when another
    // neutral walks into it, then its velocity falls by the follow-path brake each turn.
    [Fact]
    public void ANeutralInTheWanderStateWithNoRouteIsPushedByAnOverlappingNeighbour()
    {
        (GameSimulation sim, CreatureState idler, CreatureState neighbour) = Pair(PlayerIds.Neutral, ImpStates.Wandering);
        Assert.Equal(0, idler.Body.PathTotalCount);
        int x = idler.PosX, y = idler.PosY;

        sim.Tick();

        Assert.NotEqual(Fixed16Vector.Zero, idler.Body.Vel);
        Assert.True(idler.PosX != x || idler.PosY != y);
        Assert.True(idler.PosX < neighbour.PosX, "pushed away from the neighbour on its right");
    }

    [Fact]
    public void ANeutralInTheWanderStateBrakesOnceTheNeighbourIsGone()
    {
        (GameSimulation sim, CreatureState idler, CreatureState neighbour) = Pair(PlayerIds.Neutral, ImpStates.Wandering);
        sim.Tick();
        long pushed = idler.Body.Vel.Length();
        Assert.True(pushed > 0);
        sim.World.RemoveCreature(neighbour.Id);

        sim.Tick();

        Assert.True(idler.Body.Vel.Length() < pushed, "no route and no neighbour: the brake slows it");
    }

    [Fact]
    public void AnOwnedCreatureInAnOrdinaryIdleStateWithNoRouteDoesNotMove()
    {
        (GameSimulation sim, CreatureState idler, _) = Pair(Keeper, CreatureStateMachine.Idle);
        int x = idler.PosX, y = idler.PosY;

        sim.Run(3);

        Assert.Equal((x, y), (idler.PosX, idler.PosY));
        Assert.Equal(Fixed16Vector.Zero, idler.Body.Vel);
    }

    [Fact]
    public void ANeutralImpInTheWanderStateWithNoRouteDoesNotMove()
    {
        (GameSimulation sim, CreatureState idler, _) = Pair(PlayerIds.Neutral, ImpStates.Wandering, Dk2TerrainFixture.ImpCreatureId);
        int x = idler.PosX, y = idler.PosY;

        sim.Run(3);

        Assert.Equal((x, y), (idler.PosX, idler.PosY));
    }

    /// <summary>A creature in <paramref name="state"/> with no route, and a keeper creature standing
    /// still a little to its right, well inside both radii.</summary>
    private static (GameSimulation, CreatureState, CreatureState) Pair(byte owner, int state, byte type = Goblin)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState idler = world.SpawnCreature(db, type, owner, new TilePos(5, 5));
        CreatureState neighbour = world.SpawnCreature(db, Goblin, Keeper, new TilePos(5, 5));
        idler.PosX = CreatureState.CentreOf(5) - 100;
        neighbour.PosX = CreatureState.CentreOf(5) + 100;
        StatePlacement.Place(idler, state);
        StatePlacement.Place(neighbour, CreatureStateMachine.Idle);
        foreach (CreatureState c in new[] { idler, neighbour })
        {
            WorldSteering.SyncBody(c);
        }
        return (new GameSimulation(db, world, 1, [new CreatureMovementSystem()]), idler, neighbour);
    }
}
