// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Creature trigger event 7, imprisoned: fired by the Prison's enter hook
/// <c>0x0047B740</c> once its claim takes, and by nothing else (ADR-0311). Each test gives the
/// captive a tree whose event-7 record adds 1 to flag 1, so the flag counts the firings.</summary>
public class PrisonImprisonedEventTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 1;
    private const byte GoblinId = 7;
    private const ushort Root = 5;
    private const int Second = SimulationTiming.TicksPerSecond;
    private const int TravelToPoint = 6;

    // A 5x5 prison at (3..7, 2..6): the interior (4..6, 3..5) is the cells, the ring the rim.
    private static readonly TilePos Cell = new(5, 4);
    private static readonly TilePos Rim = new(3, 4);
    private static readonly TilePos Floor = new(2, 3);

    [Fact]
    public void AnImpsCaptiveFiresImprisonedOnce_AndItsWalksBetweenPrisonStatesFireNoMore()
    {
        (GameDatabase db, WorldState world) = PrisonWorld();
        CreatureState enemy = world.SpawnCreature(db, GoblinId, Rival, new TilePos(1, 1));
        enemy.TriggerId = Root;
        enemy.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, db), enemy));
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, Floor);
        GameSimulation sim = Simulation(db, world);

        for (int i = 0; i < 30 * Second && !PrisonRules.IsImprisoned(enemy); i++) sim.Tick();
        Assert.True(PrisonRules.IsImprisoned(enemy));
        Assert.Equal(1, world.Triggers.Read(1));

        // 116 -> 115 -> 6 (resume 116) -> 116, walking in 116 with saved 115 (Q422): every move
        // stays in the category or is judged by a Prison caller, so no enter hook runs.
        bool walked = false;
        for (int i = 0; i < 60 * Second; i++)
        {
            sim.Tick();
            walked |= enemy.HasRoute && enemy.StateBeforeUtilityState == PrisonRules.DecidingState;
        }

        Assert.True(walked);
        Assert.True(PrisonRules.IsImprisoned(enemy));
        Assert.Equal(1, world.Triggers.Read(1));
    }

    [Fact]
    public void AWalkFrom115To116RunsNoEnterHook()
    {
        (GameDatabase db, WorldState world) = PrisonWorld();
        CreatureState captive = Captive(db, world);
        var sim = new GameSimulation(db, world, 1, [new Act(context =>
        {
            CreatureStateMachine.BeginTravel(context.Hooks, captive, TravelToPoint, PrisonRules.InTheCellState);
            Assert.True(CreatureStateMachine.Arrive(context.Hooks, captive));
        })], Tree());

        sim.Tick();

        Assert.Equal(PrisonRules.InTheCellState, captive.Dk2StateId);
        Assert.Equal(0, world.Triggers.Read(1));
    }

    [Fact]
    public void AHandDropIntoACellFiresImprisoned_OncePerDrop()
    {
        (GameDatabase db, WorldState world) = PrisonWorld();
        CreatureState captive = Captive(db, world);
        GameSimulation sim = Simulation(db, world);

        PickUpAndDrop(sim, captive, Cell);
        Assert.Equal(PrisonRules.DecidingState, captive.Dk2StateId);
        Assert.Equal(1, world.Triggers.Read(1));

        PickUpAndDrop(sim, captive, Cell);
        Assert.Equal(2, world.Triggers.Read(1));
    }

    /// <summary>A known divergence, not DK2's behaviour: DK2's claim refuses a rim slab
    /// (<c>0x00454110</c>) and its hook returns 0 before event 7. Ours lacks that test (Q504).</summary>
    [Fact]
    public void AHandDropOnThePrisonsRimFiresImprisoned_WhereDk2sClaimWouldRefuse_Q504()
    {
        (GameDatabase db, WorldState world) = PrisonWorld();
        CreatureState captive = Captive(db, world);
        GameSimulation sim = Simulation(db, world);

        PickUpAndDrop(sim, captive, Rim);

        Assert.Equal(Rim, captive.Tile);
        Assert.Equal(PrisonRules.DecidingState, captive.Dk2StateId);
        Assert.Equal(1, world.Triggers.Read(1));
    }

    /// <summary>A refused claim returns 0 before event 7 (<c>0x0047B78C</c>), leaving the creature
    /// in state 1. Refused here because no room is under it, the hook's first test; ours reads no
    /// Prison capacity, so a full prison does not refuse (Q504).</summary>
    [Fact]
    public void AnEntryOffAnyRoomIsRefusedByTheClaim_AndFiresNothing()
    {
        (GameDatabase db, WorldState world) = PrisonWorld();
        CreatureState newcomer = world.SpawnCreature(db, GoblinId, Rival, Floor);
        newcomer.TriggerId = Root;
        bool took = true;
        var sim = new GameSimulation(db, world, 1,
            [new Act(context => took = CreatureStateMachine.TrySetState(context.Hooks, newcomer, PrisonRules.AdmittedState))],
            Tree());

        sim.Tick();

        Assert.False(took);
        Assert.Equal(CreatureStateMachine.Idle, newcomer.Dk2StateId);
        Assert.False(newcomer.HeldPlace.IsHeld);
        Assert.Equal(0, world.Triggers.Read(1));
    }

    [Fact]
    public void AMoveTheTransitionRuleRefusesFiresNothing()
    {
        (GameDatabase db, WorldState world) = PrisonWorld();
        CreatureState captive = Captive(db, world);
        int refusing = Enumerable.Range(0, CreatureStateTable.Count).First(s =>
            !CreatureStateTable.Get(s).IsUtility
            && CreatureStateTable.Get(s).Category != CreatureStateCategory.Prison
            && CreatureStateTransitions.Classify(s, PrisonRules.AdmittedState, CreatureStateMachine.Idle)
                == StateTransition.Refused);
        captive.Dk2StateId = refusing;
        bool took = true;
        var sim = new GameSimulation(db, world, 1,
            [new Act(context => took = CreatureStateMachine.TrySetState(context.Hooks, captive, PrisonRules.AdmittedState))],
            Tree());

        sim.Tick();

        Assert.False(took);
        Assert.Equal(refusing, captive.Dk2StateId);
        Assert.Equal(0, world.Triggers.Read(1));
    }

    private static (GameDatabase Db, WorldState World) PrisonWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]);
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
            for (int x = 3; x <= 7; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
        return (db, world);
    }

    /// <summary>A rival already in a cell. Imprisoned through a context with no trigger executor,
    /// so the setup fires nothing.</summary>
    private static CreatureState Captive(GameDatabase db, WorldState world)
    {
        CreatureState captive = world.SpawnCreature(db, GoblinId, Rival, Cell);
        captive.TriggerId = Root;
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), captive, Keeper, (Cell.Y * world.Width) + Cell.X));
        return captive;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world) =>
        new(db, world, seed: 42,
        [
            new HandSystem(),
            new CreatureMovementSystem(),
            new ImpTaskSystem(),
            new CreatureHealthSystem(),
            new PrisonSystem(),
            new PrisonerEatSystem(),
            new PrisonerWanderSystem(),
        ], Tree());

    private static void PickUpAndDrop(GameSimulation sim, CreatureState creature, TilePos onto)
    {
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, onto)));
    }

    /// <summary>Root 5: an event-7 record whose child adds 1 to flag 1.</summary>
    private static TriggerTree Tree() => new(
    [
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = Root, Type = (byte)CreatureTriggerEvent.Imprisoned, RepeatCount = 255,
            ChildTriggerId = 0xFFFF, NextTriggerId = 0, Parameters = new Dictionary<string, long>(),
        },
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = 7, RepeatCount = 0, ChildTriggerId = 0, NextTriggerId = 0,
            Parameters = new Dictionary<string, long> { ["FlagId"] = 1, ["Flag"] = 0x12, ["Value"] = 1 },
        },
    ], 0);

    private sealed class Act(Action<SimulationContext> body) : ISimulationSystem
    {
        public string Name => "imprisoned event test";
        public void Execute(SimulationContext context) => body(context);
    }
}
