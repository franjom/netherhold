// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The travel states' handover (Q724, travel-states.md §5): 6 and 7 stop the body once the
/// route's progress is exactly 1.0, before Follow_Path's half-radius arrival for a small body.</summary>
public class TravelArrivalTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte SmallId = 99;
    private const int TravelToPoint = 6;

    /// <summary>A walker with the Jack's and the Mistress's footprint, 0x204: a radius of 0x1020, so
    /// Follow_Path's arrival needs 0x810, inside the 0x1000 the progress rounds away.</summary>
    private static CreatureDef Small() => Dk2TerrainFixture.Goblin() with { Name = "Small", CreatureId = SmallId, FootprintRadius = new Fp32(0x204) };

    private static (GameSimulation Sim, CreatureState Walker) Walk(bool withStep)
    {
        GameDatabase db = Dk2TerrainFixture.Database(Small());
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int x = 1; x <= 12; x++) world.Tile(x, 3).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState walker = world.SpawnCreature(db, SmallId, Keeper, new TilePos(2, 3));
        var send = new Send();
        ICreatureUpdateStep[] steps = withStep
            ? [send, new TravelArrivalSystem(), new CreatureMovementSystem()]
            : [send, new CreatureMovementSystem()];
        return (new GameSimulation(db, world, 1, [new CreatureUpdateSystem(steps)]), walker);
    }

    private sealed class Send : ICreatureUpdateStep
    {
        public string Name => "send";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

        public void Update(SimulationContext context, CreatureState creature)
        {
            if (context.Tick.Value != 0) return;
            CreatureStateMachine.BeginTravel(context.Hooks, creature, TravelToPoint, CreatureStateMachine.Idle);
            Assert.True(context.Navigation.TryRoute(creature, new TilePos(7, 3), MoveOptions.Plain));
        }
    }

    /// <summary>Standing on its route's last point: a walker in 6 is stopped; an imp, whose own handler
    /// steps it and tests the progress after the mover, and one approaching in 8, whose refresh comes
    /// first (0x004713B6), are left to their handlers.</summary>
    [Theory]
    [InlineData(SmallId, TravelToPoint, false)]
    [InlineData(Dk2TerrainFixture.ImpCreatureId, TravelToPoint, true)]
    [InlineData(SmallId, 8, true)]
    public void OnlyANonImpOutsideTheApproachIsStoppedByTheStep(byte typeId, int state, bool keepsItsRoute)
    {
        GameDatabase db = Dk2TerrainFixture.Database(Small());
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int x = 1; x <= 12; x++) world.Tile(x, 3).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState walker = world.SpawnCreature(db, typeId, Keeper, new TilePos(2, 3));
        var place = new Place(state);
        var sim = new GameSimulation(db, world, 1, [new CreatureUpdateSystem(place, new TravelArrivalSystem())]);

        sim.Tick();

        Assert.Equal(Fixed16.One, FollowPathRules.Progress(walker.Body));
        Assert.Equal(keepsItsRoute, walker.HasRoute);
    }

    /// <summary>Routes one tile east in <paramref name="state"/> and stands the body on the last point.</summary>
    private sealed class Place(int state) : ICreatureUpdateStep
    {
        public string Name => "place";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

        public void Update(SimulationContext context, CreatureState creature)
        {
            CreatureStateMachine.BeginTravel(context.Hooks, creature, state, CreatureStateMachine.Idle);
            Assert.True(context.Navigation.TryRoute(creature, new TilePos(3, 3), MoveOptions.Plain));
            Assert.Equal(1, creature.Body.PathTotalCount);
            creature.Body.Pos = creature.Body.PathTargetPos;
        }
    }

    [Fact]
    public void AWalkerStopsDeadOnceItsRouteProgressIsWhole_WhereFollowPathsBrakeLetItGlidePast()
    {
        (GameSimulation with, CreatureState stopped) = Walk(withStep: true);
        (GameSimulation without, CreatureState walking) = Walk(withStep: false);

        for (int tick = 0; tick < 120 && (tick == 0 || stopped.HasRoute); tick++)
        {
            with.Tick();
            without.Tick();
        }

        // Within 1/16 tile (0x100) of the target's centre, stopped with no velocity, as
        // Creature_StopMoving leaves it.
        int centre = CreatureState.CentreOf(7);
        Assert.False(stopped.HasRoute);
        Assert.Equal(Fixed16Vector.Zero, stopped.Body.Vel);
        Assert.InRange(Math.Abs(stopped.PosX - centre), 0, 0xFF);
        int at = stopped.PosX;

        // Without the step Follow_Path finishes the route, but its brake only takes the velocity
        // off a little at a time, and the body glides on past the centre.
        for (int k = 0; k < 6; k++)
        {
            with.Tick();
            without.Tick();
        }

        Assert.Equal(at, stopped.PosX);
        Assert.True(Math.Abs(walking.PosX - centre) > 0x300);
    }
}
