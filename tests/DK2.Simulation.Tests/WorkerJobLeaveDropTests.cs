// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A worker that leaves the WorkerJob category puts down what it holds: <c>WorkerJob_Leave</c>
/// (<c>0x004647D0</c>) calls <c>Creature_DropCarriedThing</c> and <c>Creature_DropDraggedThing</c> before its
/// release switch (docs/research/functions/004647D0.md), so a threat flight, a fight, a slap or a stun
/// leaves the body or the object where the imp stands.</summary>
public class WorkerJobLeaveDropTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;
    private const int TravelToPoint = 6;
    private const int Second = SimulationTiming.TicksPerSecond;

    private static GameDatabase Db
    {
        get
        {
            return Dk2TerrainFixture.Database();
        }
    }

    private static WorldState World(GameDatabase db)
    {
        return Dk2TerrainFixture.Sellable(WorldState.FromMap(
        db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]));
    }

    private static void BigPrison(GameDatabase db, WorldState world)
    {
        for (int y = 1; y <= 8; y++)
        {
            for (int x = 1; x <= 8; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = Keeper;
            }
        }

        for (int y = 2; y <= 6; y++)
        {
            for (int x = 3; x <= 7; x++)
            {
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
            }
        }
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new CarriedThingAttachSystem(),
            new CreatureMovementSystem(),
            new ImpTaskSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
            new PrisonSystem(),
        ]);
        sim.ObserveWith(sink);
        return sim;
    }

    private sealed class Probe(Action<SimulationContext> act) : ISimulationSystem
    {
        public string Name
        {
            get
            {
                return "Probe";
            }
        }

        public void Execute(SimulationContext context)
        {
            act(context);
        }
    }

    private static CreatureState Collapsed(GameDatabase db, WorldState world, TilePos at)
    {
        CreatureState body = world.SpawnCreature(db, GoblinId, Rival, at);
        body.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, db), body));
        return body;
    }

    [Fact]
    public void AnImpThatLeavesTheCategoryMidCarryPutsTheBodyDownWhereItStands_AndTheBodyStaysCollapsed()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        BigPrison(db, world);
        CreatureState body = Collapsed(db, world, new TilePos(1, 1));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 4));
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world, sink);

        for (int i = 0; i < 20 * Second && imp.CarriedCreatureId < 0; i++)
        {
            sim.Tick();
        }

        Assert.Equal(body.Id, imp.CarriedCreatureId);
        sim.Run(Second);
        Assert.Equal(body.Id, imp.CarriedCreatureId);

        // What ThreatFleeRules.Turn does to a worker: SetState(1) out of the worker category.
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), imp, CreatureStateMachine.Idle));

        Assert.Equal(-1, imp.CarriedCreatureId);
        Assert.Equal(-1, body.CarriedByCreatureId);
        Assert.Equal(-1, imp.TargetTileIndex);
        Assert.Equal(imp.PosX, body.PosX);
        Assert.Equal(imp.PosY, body.PosY);
        Assert.True(CreatureDeathRules.IsDying(body));
        Assert.True(CarryRules.IsCarryable(body));
    }

    [Fact]
    public void ADroppedBodyIsCapturedAgainWithoutTheImpKeepingItInItsArms()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        BigPrison(db, world);
        CreatureState body = Collapsed(db, world, new TilePos(1, 1));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 4));
        var sink = new ListEventSink();
        bool leave = false;
        var sim = new GameSimulation(db, world, seed: 42,
        [
            new HandSystem(),
            new CarriedThingAttachSystem(),
            new CreatureMovementSystem(),
            new ImpTaskSystem(),
            new CreatureSleepSystem(),
            new CreatureHealthSystem(),
            new PrisonSystem(),
            new Probe(c =>
            {
                if (!leave) { return; } leave = false;
                Assert.True(CreatureStateMachine.TrySetState(c.Hooks, imp, CreatureStateMachine.Idle));
            }),
        ]);
        sim.ObserveWith(sink);

        for (int i = 0; i < 20 * Second && imp.CarriedCreatureId < 0; i++)
        {
            sim.Tick();
        }

        Assert.Equal(body.Id, imp.CarriedCreatureId);
        leave = true;
        sim.Tick();
        Assert.Equal(-1, imp.CarriedCreatureId);
        Assert.Contains(sink.Events, e => e.Kind == "body_dropped" && e.Detail.Contains("reason=left_worker_job"));

        for (int i = 0; i < 60 * Second && !sink.Events.Any(e => e.Kind == "creature_imprisoned"); i++)
        {
            sim.Tick();
        }

        Assert.Contains(sink.Events, e => e.Kind == "creature_imprisoned" && e.Detail.Contains($"body={body.Id}"));
        Assert.Equal(CreatureActivity.Imprisoned, body.Activity);
        Assert.Equal(-1, imp.CarriedCreatureId);
    }

    [Fact]
    public void AnImpThatLeavesTheCategoryMidCarryPutsTheObjectDownWhereItStands()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        ObjectState cargo = world.SpawnObject(db, Dk2TerrainFixture.AlarmCrateObjectId, Keeper, 0);
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), imp, ImpStates.CarryingToPrison));
        ObjectCarryRules.PickUp(imp, cargo);

        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), imp, CreatureStateMachine.Idle));

        Assert.Equal(-1, imp.CarriedObjectId);
        Assert.Equal(-1, cargo.CarriedByCreatureId);
        Assert.Equal((3 * world.Width) + 2, cargo.TileIndex);
    }

    [Fact]
    public void AnImpFleeingMidWalkWithTheBodyAlsoPutsItDown()
    {
        // The walk to the Prison is state 6 with resume 43; the leave hook is judged by the saved caller.
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState body = Collapsed(db, world, new TilePos(1, 1));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        var hooks = new StateHookContext(world, db);
        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, ImpStates.CarryingToPrison));
        CarryRules.PickUp(imp, body);
        imp.TargetTileIndex = 5;
        CreatureStateMachine.BeginTravel(hooks, imp, TravelToPoint, ImpStates.CarryingToPrison);
        Assert.Equal(TravelToPoint, imp.Dk2StateId);

        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, CreatureStateMachine.Idle));

        Assert.Equal(-1, imp.CarriedCreatureId);
        Assert.Equal(-1, body.CarriedByCreatureId);
        Assert.Equal(-1, imp.TargetTileIndex);
        Assert.True(CreatureDeathRules.IsDying(body));
    }

    [Fact]
    public void AWalkBetweenWorkerStatesHoldsOnToTheBody()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState body = Collapsed(db, world, new TilePos(1, 1));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        var hooks = new StateHookContext(world, db);
        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, ImpStates.CarryingToPrison));
        CarryRules.PickUp(imp, body);
        CreatureStateMachine.BeginTravel(hooks, imp, TravelToPoint, ImpStates.AtThePrisonDoor);

        Assert.True(CreatureStateMachine.Arrive(hooks, imp));

        Assert.Equal(ImpStates.AtThePrisonDoor, imp.Dk2StateId);
        Assert.Equal(body.Id, imp.CarriedCreatureId);
    }

    [Fact]
    public void AMoveWithinTheWorkerCategoryHoldsOnToTheBody()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState body = Collapsed(db, world, new TilePos(1, 1));
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(2, 3));
        var hooks = new StateHookContext(world, db);
        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, ImpStates.CarryingToPrison));
        CarryRules.PickUp(imp, body);

        Assert.True(CreatureStateMachine.TrySetState(hooks, imp, ImpStates.AtThePrisonDoor));

        Assert.Equal(body.Id, imp.CarriedCreatureId);
        Assert.Equal(imp.Id, body.CarriedByCreatureId);
    }
}
