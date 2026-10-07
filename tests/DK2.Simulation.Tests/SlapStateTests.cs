// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The slap's state moves 67, 77 and 120 and their reaction (slap-states.md, ADR-0401).</summary>
public class SlapStateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Goblin = Dk2TerrainFixture.GoblinCreatureId;
    private const byte Imp = Dk2TerrainFixture.ImpCreatureId;

    /// <summary>A clip of 20 frames at 20 a second in art slot 4, which action 19 plays, for the
    /// Goblin and the Imp.</summary>
    private static GameDatabase Db()
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        slots[SlapRules.ReactionArtSlot] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 20, FramesPerSecond = 20 };
        return new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values.Select(c => c.CreatureId is Goblin or Imp ? c with { ArtSlots = slots } : c),
            basis.Variables, objects: basis.Objects.Values, shots: basis.Shots.Values);
    }

    private static WorldState Open(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < 19; y++)
        for (int x = 1; x < 19; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        return world;
    }

    private static void Room(GameDatabase db, WorldState world, RoomTypeId type, int x0, int y0)
    {
        for (int y = y0; y < y0 + 3; y++)
        for (int x = x0; x < x0 + 3; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, ListEventSink sink)
    {
        var sim = new GameSimulation(db, world, seed: 7, [new HandSystem(), new CreatureStatusSystem()]);
        sim.ObserveWith(sink);
        return sim;
    }

    /// <summary>Runs until the reaction's action is over and one more turn has acted on it.</summary>
    private static void SitOut(GameSimulation sim, CreatureState creature)
    {
        sim.Tick();   // Age 1: the action starts
        while (sim.CurrentTick.Value <= creature.ActionUntilTick) sim.Tick();
    }

    [Fact]
    public void AnIdleCreatureSlappedGoesTo77_HoldsForAction19_ThenIdles()
    {
        GameDatabase db = Db();
        WorldState world = Open(db);
        CreatureState goblin = world.SpawnCreature(db, Goblin, Keeper, new TilePos(9, 9));
        StatePlacement.Place(goblin, CreatureStateMachine.Idle);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, goblin.Id)));
        Assert.Equal(SlapRules.ReactionState, goblin.Dk2StateId);
        sim.Tick();
        long hold = AnimationTimingRules.CountedHoldTicks(db.CreatureById(Goblin), SlapRules.ReactionArtSlot, 0);
        Assert.True(hold > 1);
        Assert.Equal(sim.CurrentTick.Value - 1 + hold, goblin.ActionUntilTick);
        while (sim.CurrentTick.Value <= goblin.ActionUntilTick)
        {
            Assert.Equal(SlapRules.ReactionState, goblin.Dk2StateId);
            sim.Tick();
        }

        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
        Assert.Equal(1, world.Player(Keeper).SlapCount);
        Assert.Contains(sink.Events, e => e.Kind == "slap_reaction");
    }

    [Fact]
    public void AWorkerSlappedIdlesAfterTheReaction_TheTableRefusing10From77()
    {
        GameDatabase db = Db();
        WorldState world = Open(db);
        CreatureState imp = world.SpawnCreature(db, Imp, Keeper, new TilePos(9, 9));
        StatePlacement.Place(imp, ImpStates.TaskSelection);
        GameSimulation sim = Sim(db, world, new ListEventSink());

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, imp.Id)));
        Assert.Equal(SlapRules.ReactionState, imp.Dk2StateId);
        SitOut(sim, imp);

        // 0x0046F1F0 asks 10 for a worker, which the transition table refuses from category 7, so
        // its 0 drops the worker to 1, and 1 sends it on to 10.
        Assert.Equal(CreatureStateMachine.Idle, imp.Dk2StateId);
    }

    [Fact]
    public void AManufacturerSlappedInItsKeepersWorkshopGoesTo67_EndingItsWork_ThenBackTo63()
    {
        GameDatabase db = Db();
        WorldState world = Open(db);
        Room(db, world, RoomTypeId.Workshop, 3, 3);
        CreatureState goblin = world.SpawnCreature(db, Goblin, Keeper, new TilePos(4, 4));
        goblin.CurrentJob = JobType.Manufacture;
        StatePlacement.Place(goblin, WorkshopStep.ChoosingState + 2);   // 65, at work
        goblin.HeldPlace = new RoomPlace((4 * world.Width) + 4, RoomCapacityRules.Units);
        goblin.ActionUntilTick = long.MaxValue;
        GameSimulation sim = Sim(db, world, new ListEventSink());

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, goblin.Id)));
        Assert.Equal(SlapRules.WorkshopState, goblin.Dk2StateId);
        Assert.Equal(0, goblin.ActionUntilTick);   // its work ended (0x00496D20)
        SitOut(sim, goblin);

        Assert.Equal(WorkshopStep.ChoosingState, goblin.Dk2StateId);
        Assert.True(goblin.HeldPlace.IsHeld);
    }

    [Fact]
    public void ACaptiveSlappedInItsJailersPrisonGoesTo120_ThenBackTo115()
    {
        GameDatabase db = Db();
        WorldState world = Open(db);
        Room(db, world, RoomTypeId.Prison, 3, 3);
        CreatureState captive = world.SpawnCreature(db, Goblin, PlayerIds.Good, new TilePos(4, 4));
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), captive, Keeper, (4 * world.Width) + 4));
        GameSimulation sim = Sim(db, world, new ListEventSink());

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, captive.Id)));
        Assert.Equal(SlapRules.PrisonState, captive.Dk2StateId);
        SitOut(sim, captive);

        Assert.Equal(PrisonRules.DecidingState, captive.Dk2StateId);
        Assert.True(PrisonRules.IsImprisoned(captive));
    }

    [Fact]
    public void ASlapWhoseMoveTheTableRefusesDoesNothingMore_NoDamageNoCount()
    {
        GameDatabase db = Db();
        WorldState world = Open(db);
        CreatureState goblin = world.SpawnCreature(db, Goblin, Keeper, new TilePos(9, 9));
        StatePlacement.Place(goblin, 218);   // frozen: the table refuses 77 from it
        goblin.FrozenTicks = 1000;
        int health = goblin.Health;
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, sink);

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, goblin.Id)));

        Assert.Equal(218, goblin.Dk2StateId);
        Assert.Equal(health, goblin.Health);
        Assert.Equal(0, world.Player(Keeper).SlapCount);
        Assert.Contains(sink.Events, e => e.Kind == "slap_ignored");
    }

    [Fact]
    public void ACreatureSlappedMidWalkStopsWhereItIs()
    {
        GameDatabase db = Db();
        WorldState world = Open(db);
        CreatureState goblin = world.SpawnCreature(db, Goblin, Keeper, new TilePos(3, 9));
        bool routed = false;
        var walk = new Probe(context =>
        {
            if (routed) return;
            routed = context.Navigation.TryRoute(goblin, new TilePos(15, 9), Navigation.MoveOptions.Loose);
        });
        var sim = new GameSimulation(db, world, seed: 7, [walk, new HandSystem(), new CreatureStatusSystem(), new CreatureMovementSystem()]);
        sim.Tick();
        Assert.True(routed && goblin.HasRoute);

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, goblin.Id)));
        (int x, int y) = (goblin.PosX, goblin.PosY);
        for (int i = 0; i < 5; i++) sim.Tick();

        Assert.False(goblin.HasRoute);
        Assert.Equal((x, y), (goblin.PosX, goblin.PosY));
    }

    private sealed class Probe(Action<SimulationContext> body) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context) => body(context);
    }
}
