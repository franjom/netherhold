// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The hand's trigger events (ADR-0770): a slap fires creature event 5 and player event 60 to
/// the slapped creature's owner (<c>0x0048B8A0</c>); a lift fires player event 58 to the lifting keeper
/// and creature event 65 (<c>0x004BFBD0</c>).</summary>
public sealed class HandEventTests
{
    private const byte Keeper = 3;
    private const ushort PlayerRoot = 5, CreatureRoot = 7;

    /// <summary>The main path counts the slap before its events (<c>0x0048BCAA</c>, <c>0x0048BCB2</c>);
    /// states 209 and 255 fire them before their count (<c>0x0048BD08</c>, <c>0x0048BD77</c>). A player
    /// tree on event 60 that tests NumberOfSlaps &gt;= 1 sees the difference.</summary>
    [Theory]
    [InlineData(CreatureStateMachine.Idle, 1)]
    [InlineData(255, 0)]
    public void ASlapFiresBothEvents_AroundTheCountAsItsPathDoes(int state, int countSeenByTheTree)
    {
        (GameSimulation sim, CreatureState goblin) = Simulation(
            playerEvent: (byte)PlayerTriggerEvent.CreatureSlapped, creatureEvent: (byte)CreatureTriggerEvent.Slapped, state);

        sim.Tick(InputFrame.Of(new SlapCreatureCommand(Keeper, goblin.Id)));

        Assert.Equal(1, sim.World.Player(Keeper).SlapCount);
        Assert.Equal(countSeenByTheTree, sim.World.Triggers.Read(1));
        Assert.Equal(1, sim.World.Triggers.Read(2));
    }

    /// <summary>The lift fires both events at the prepare step, before the hand's capacity test, once
    /// it takes state 75 afresh; a creature already in 75 skips them (<c>0x004BFBFB</c>) and gets only
    /// the lift sound.</summary>
    [Theory]
    [InlineData(CreatureStateMachine.Idle, 1)]
    [InlineData(HandRules.PreparationState, 0)]
    public void ALiftFiresBothEvents_UnlessTheCreatureIsAlreadyPrepared(int state, int fired)
    {
        (GameSimulation sim, CreatureState goblin) = Simulation(
            playerEvent: (byte)PlayerTriggerEvent.CreaturePickedUp, creatureEvent: (byte)CreatureTriggerEvent.PickedUp,
            state, slapCountTest: false);

        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, goblin.Id)));

        Assert.Equal(fired, sim.World.Triggers.Read(1));
        Assert.Equal(fired, sim.World.Triggers.Read(2));
    }

    private static (GameSimulation Sim, CreatureState Goblin) Simulation(
        byte playerEvent, byte creatureEvent, int state, bool slapCountTest = true)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, Keeper]);
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        StatePlacement.Place(goblin, state);
        goblin.TriggerId = CreatureRoot;
        Dk2TerrainFixture.SetKnownIdentities(world);

        var records = new List<TriggerRecord>
        {
            // Player tree: the event, then (for a slap) NumberOfSlaps >= 1, then flag 1 = 1.
            Condition(PlayerRoot, playerEvent, child: slapCountTest ? (ushort)6 : (ushort)0xFFFF),
            Condition(CreatureRoot, creatureEvent, child: 0xFFFE),
        };
        if (slapCountTest)
            records.Add(Condition(6, 71, child: 0xFFFF, new() { ["Comparison"] = 5, ["Value"] = 1 }));
        records.Add(SetFlag(0xFFFF, flag: 1));
        records.Add(SetFlag(0xFFFE, flag: 2));
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, PlayerRoot)]);
        return (new GameSimulation(db, world, seed: 7, [new HandSystem()], tree), goblin);
    }

    private static TriggerRecord Condition(ushort id, byte type, ushort child, Dictionary<string, long>? parameters = null) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 255,
            ChildTriggerId = child, NextTriggerId = 0, Parameters = parameters ?? [],
        };

    private static TriggerRecord SetFlag(ushort id, byte flag) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 7, RepeatCount = 0, ChildTriggerId = 0, NextTriggerId = 0,
            Parameters = new Dictionary<string, long> { ["FlagId"] = flag, ["Flag"] = 0x0A, ["Value"] = 1 },
        };
}
