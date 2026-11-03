// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>ADR-0644: a committed step floors the body into the entity (<c>0x004D5AC0</c>,
/// <c>0x004B5560</c>) and leaves the body's 16.16 position; only the mover's own next call re-syncs
/// it (<c>0x004D59A0</c> at <c>0x00470CF5</c>/<c>0x0048A27F</c>), and a neighbour's push reads it
/// (<c>0x004D2B80</c>, creature <c>+0x1A8</c>). docs/research/steering-behaviours.md.</summary>
public class SteeringBodyRetentionTests
{
    [Fact]
    public void ACommittedStepLeavesTheBodysLowBits()
    {
        (GameSimulation sim, CreatureState walker, _) = Fixture();
        Prepare(walker, position: new(19770 << 4, 19770 << 4), velocity: new(21421, 21421),
            waypoint: new(393216, 393216), heading: 768);

        sim.Tick();

        Assert.NotEqual(walker.PosX << 4, walker.Body.Pos.X);
        Assert.Equal(walker.PosX, walker.Body.Pos.X >> 4);
        Assert.Equal(walker.PosY, walker.Body.Pos.Y >> 4);
    }

    [Fact]
    public void TheAdapterKeepsABodyStillOnItsEntityAtTheNextTurn()
    {
        (GameSimulation sim, _, CreatureState idle) = Fixture();
        var retained = new Fixed16Vector((idle.PosX << 4) + 5, (idle.PosY << 4) + 11);
        idle.Body.Pos = retained;

        sim.Tick();

        Assert.Equal(retained, idle.Body.Pos);
    }

    [Fact]
    public void TheAdapterResyncsABodyWhoseEntityMoved()
    {
        (GameSimulation sim, _, CreatureState idle) = Fixture();
        idle.Body.Pos = new Fixed16Vector((idle.PosX << 4) + 5, (idle.PosY << 4) + 11);
        idle.PosX += Fp32.One;

        sim.Tick();

        Assert.Equal(new Fixed16Vector(idle.PosX << 4, idle.PosY << 4), idle.Body.Pos);
    }

    [Fact]
    public void TheRetainedPositionIsSavedAndHashed()
    {
        (GameSimulation sim, _, CreatureState idle) = Fixture();
        ulong onEntity = sim.StateHash();
        idle.Body.Pos = new Fixed16Vector((idle.PosX << 4) + 5, (idle.PosY << 4) + 11);
        Assert.NotEqual(onEntity, sim.StateHash());

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new CreatureMovementSystem()]);

        Assert.Equal(idle.Body.Pos, copy.World.Creatures.Single(c => c.Id == idle.Id).Body.Pos);
        Assert.Equal(sim.StateHash(), copy.StateHash());
    }

    /// <summary>The captured Level 5 boundary (completed turn 1417, Imp 445 before Imp 454 in the
    /// native list): the mover is on its entity, the neighbour still carries its previous step's low
    /// bits. With them the step gives the native velocity; the second row, the neighbour quantized,
    /// is the old modern result, kept as a control. The wrapped size-share product makes the push's
    /// direction sensitive to the low bits. Follow-path force from the matched modern call.</summary>
    [Theory]
    [InlineData(1037061, 774047, -30207, 1088)]
    [InlineData(1037056, 774032, -30209, 417)]
    public void CapturedNeighbourLowBitsDecideTheStep(int neighbourX, int neighbourY, int expectedVx, int expectedVy)
    {
        var mover = new SteeringBody { Pos = new(1044544, 774000), Vel = new(-29280, 7968), Radius = 4576, MaxForce = 98304, MaxSpeed = 30144 };
        var neighbour = new SteeringBody { Pos = new(neighbourX, neighbourY), Vel = new(-28635, 10303), Radius = 4576, MaxForce = 98304, MaxSpeed = 30144 };

        Fixed16Vector separation = SeparationBehaviour.Push(mover, 1, neighbour, 1);
        Fixed16Vector total = SteeringIntegrator.Combine(mover, Fixed16Vector.Zero, Fixed16Vector.Zero, new(-19150, 12984), separation);
        Fixed16Vector velocity = Fixed16.Truncate(mover.Vel + Fixed16.Truncate(total, mover.MaxForce), mover.MaxSpeed);

        Assert.Equal(new Fixed16Vector(expectedVx, expectedVy), velocity);
    }

    private static (GameSimulation Sim, CreatureState Walker, CreatureState Idle) Fixture()
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
        CreatureState walker = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.FirstKeeper, new TilePos(4, 4));
        CreatureState idle = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId,
            PlayerIds.FirstKeeper, new TilePos(10, 10));
        CreatureStateMachine.BeginTravel(new StateHookContext(world, db), walker, 6, CreatureStateMachine.Idle);
        return (new GameSimulation(db, world, 1, [new CreatureMovementSystem()]), walker, idle);
    }

    private static void Prepare(CreatureState creature, Fixed16Vector position, Fixed16Vector velocity,
        Fixed16Vector waypoint, int heading)
    {
        creature.PosX = position.X >> 4;
        creature.PosY = position.Y >> 4;
        creature.Heading = heading;
        WorldSteering.SyncBody(creature);
        creature.Body.Vel = velocity;
        RouteWindow.Load(creature.Body, [waypoint], waypoint, 0);
    }
}
