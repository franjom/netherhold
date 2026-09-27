// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>The creature and player event filters (<c>0x0049AA30</c>, <c>0x004C7A50</c>):
/// a trigger tree's second path, where an event's occurrence is the condition.</summary>
public class TriggerEventTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void ACreatureDeathFiresKilledBeforeItsRootIsLost()
    {
        (WorldState world, GameDatabase database, CreatureState creature) = CreatureWorld(root: 5);
        creature.PosX = CreatureState.CentreOf(6);
        creature.PosY = CreatureState.CentreOf(6);
        creature.DieInstantly = true;
        creature.Health = 0;
        TriggerRecord[] records =
            [Event(5, CreatureTriggerEvent.Killed, child: 0xFFFF), Assign(1, flagId: 1)];

        new GameSimulation(database, world, 1,
            [new Systems.CreatureHealthSystem()], new TriggerTree(records, 0)).Run(4);

        Assert.Equal(9, world.Triggers.Read(1));
    }

    [Fact]
    public void AnEventRunsTheChildrenOfEveryActiveTopLevelRecordOfItsType_AndNothingElse()
    {
        (WorldState world, GameDatabase database, CreatureState creature) = CreatureWorld(root: 5);
        TriggerRecord[] records =
        [
            // Root 5 is a "killed" event whose child writes flag 1; its sibling 6 is an "attacked"
            // event writing flag 2; sibling 7 is another "attacked" writing flag 3; 8 is a plain
            // flag condition (never evaluated by an event) writing flag 4.
            Event(5, CreatureTriggerEvent.Killed, child: 0xFFFF, next: 6),
            Event(6, CreatureTriggerEvent.Attacked, child: 0xFFFE, next: 7),
            Event(7, CreatureTriggerEvent.Attacked, child: 0xFFFD, next: 8),
            FlagCondition(8, child: 0xFFFC),
            Assign(1, flagId: 1), Assign(2, flagId: 2), Assign(3, flagId: 3), Assign(4, flagId: 4),
        ];
        TriggerExecutor executor = Executor(records, world, database);

        executor.FireCreatureEvent(creature, CreatureTriggerEvent.Attacked, currentTick: 1);

        Assert.Equal(0, world.Triggers.Read(1));
        Assert.Equal(9, world.Triggers.Read(2));
        Assert.Equal(9, world.Triggers.Read(3));
        Assert.Equal(0, world.Triggers.Read(4));
    }

    [Fact]
    public void AnInactiveEventRecordIsSkipped_AndAnActionInTheSiblingChainEndsTheScan()
    {
        (WorldState world, GameDatabase database, CreatureState creature) = CreatureWorld(root: 5);
        TriggerRecord[] records =
        [
            Event(5, CreatureTriggerEvent.Attacked, child: 0xFFFF, next: 0xFFFE),
            // An action id in the top-level chain: DK2's filter exits on a non-positive id, so
            // the event record behind it is never reached.
            Assign(2, flagId: 2, next: 6),
            Event(6, CreatureTriggerEvent.Attacked, child: 0xFFFD),
            Assign(1, flagId: 1), Assign(3, flagId: 3),
        ];
        TriggerExecutor executor = Executor(records, world, database);
        executor.SetActive(5, active: false);

        executor.FireCreatureEvent(creature, CreatureTriggerEvent.Attacked, currentTick: 1);

        Assert.Equal(0, world.Triggers.Read(1));
        Assert.Equal(0, world.Triggers.Read(2));
        Assert.Equal(0, world.Triggers.Read(3));
    }

    [Fact]
    public void AnEventRecordsChildrenRunWithItsOwnRepeat_SoAOneShotActionFiresOnce()
    {
        (WorldState world, GameDatabase database, CreatureState creature) = CreatureWorld(root: 5);
        TriggerRecord[] records =
        [
            Event(5, CreatureTriggerEvent.Attacked, child: 0xFFFF, repeat: 1),
            Add(1, flagId: 1, value: 1),
        ];
        TriggerExecutor executor = Executor(records, world, database);

        executor.FireCreatureEvent(creature, CreatureTriggerEvent.Attacked, currentTick: 1);
        executor.FireCreatureEvent(creature, CreatureTriggerEvent.Attacked, currentTick: 2);
        executor.FireCreatureEvent(creature, CreatureTriggerEvent.Killed, currentTick: 3);

        Assert.Equal(1, world.Triggers.Read(1));
    }

    [Fact]
    public void ACreatureWithoutARootFiresNothing_AndTheExecutorWithoutATreeIgnoresEvents()
    {
        (WorldState world, GameDatabase database, CreatureState creature) = CreatureWorld(root: 0);
        TriggerRecord[] records = [Event(5, CreatureTriggerEvent.Attacked, child: 0xFFFF), Assign(1, flagId: 1)];
        Executor(records, world, database).FireCreatureEvent(creature, CreatureTriggerEvent.Attacked, 1);
        Assert.Equal(0, world.Triggers.Read(1));
    }

    [Fact]
    public void APlayerEventScansThePlayersOwnRoot_AndAGuiButtonNeedsItsTargetPair()
    {
        (WorldState world, GameDatabase database, _) = CreatureWorld(root: 0);
        TriggerRecord[] records =
        [
            Event(5, (byte)PlayerTriggerEvent.PayDay, child: 0xFFFF, next: 6),
            Gui(6, targetType: 2, targetId: 7, child: 0xFFFE, next: 7),
            Gui(7, targetType: 2, targetId: 8, child: 0xFFFD),
            Assign(1, flagId: 1), Assign(2, flagId: 2), Assign(3, flagId: 3),
        ];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        var executor = new TriggerExecutor(tree, world.Triggers, world, database);

        executor.FirePlayerEvent(PlayerIds.Neutral, PlayerTriggerEvent.PayDay, currentTick: 1);
        Assert.Equal(0, world.Triggers.Read(1));

        executor.FirePlayerEvent(Keeper, PlayerTriggerEvent.PayDay, currentTick: 1);
        executor.FirePlayerEvent(Keeper, PlayerTriggerEvent.GuiButtonPressed, currentTick: 1, targetType: 2, targetId: 8);

        Assert.Equal(9, world.Triggers.Read(1));
        Assert.Equal(0, world.Triggers.Read(2));
        Assert.Equal(9, world.Triggers.Read(3));
    }

    /// <summary>The level initialiser fires event 3 for every placed creature; the executor does
    /// it when it attaches the creature roots.</summary>
    [Fact]
    public void APlacedCreaturesCreatedEventFiresWhenTheExecutorAttachesItsRoot()
    {
        (WorldState world, GameDatabase database, _) = CreatureWorld(root: 5);
        TriggerRecord[] records = [Event(5, CreatureTriggerEvent.Created, child: 0xFFFF), Assign(1, flagId: 1)];

        Executor(records, world, database);

        Assert.Equal(9, world.Triggers.Read(1));
    }

    /// <summary>The hook end to end: a Blocker engages the Goblin, the fight-starter fires
    /// event 6 on it, and the Goblin's own tree writes the flag on the same tick.</summary>
    [Fact]
    public void ANewEngagementFiresAttackedOnTheTarget_ThroughTheSimulation()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, (byte)(Keeper + 1)]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                if (world.Tile(x, y).TerrainId != Dk2TerrainFixture.ClaimedPath)
                    world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(5, 5));
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, (byte)(Keeper + 1), new TilePos(7, 5));
        goblin.TriggerId = 5;
        TriggerRecord[] records = [Event(5, CreatureTriggerEvent.Attacked, child: 0xFFFF), Assign(1, flagId: 1)];

        new GameSimulation(db, world, 1, [new Systems.CombatSystem()], new TriggerTree(records, 0)).Run(1);

        Assert.Equal(CreatureActivity.Fighting, world.Creatures[0].Activity);
        Assert.Equal(9, world.Triggers.Read(1));
    }

    private static (WorldState World, GameDatabase Database, CreatureState Creature) CreatureWorld(ushort root)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        CreatureState creature = world.SpawnCreature(database, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(2, 2));
        creature.TriggerId = root;
        return (world, database, creature);
    }

    private static TriggerExecutor Executor(TriggerRecord[] records, WorldState world, GameDatabase database) =>
        new(new TriggerTree(records, 0), world.Triggers, world, database);

    private static TriggerRecord Event(ushort id, CreatureTriggerEvent type, ushort child, ushort next = 0, byte repeat = 255) =>
        Event(id, (byte)type, child, next, repeat);

    private static TriggerRecord Event(ushort id, byte type, ushort child, ushort next = 0, byte repeat = 255) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = repeat,
            ChildTriggerId = child, NextTriggerId = next,
            Parameters = new Dictionary<string, long>(),
        };

    private static TriggerRecord Gui(ushort id, byte targetType, byte targetId, ushort child, ushort next = 0) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 53, RepeatCount = 255,
            ChildTriggerId = child, NextTriggerId = next,
            Parameters = new Dictionary<string, long> { ["TargetType"] = targetType, ["TargetId"] = targetId },
        };

    private static TriggerRecord FlagCondition(ushort id, ushort child) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 1, RepeatCount = 255,
            ChildTriggerId = child, NextTriggerId = 0,
            Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = 3, ["TargetId"] = 0, ["Flag"] = 1, ["FlagId"] = 0, ["Value"] = 0,
            },
        };

    private static TriggerRecord Assign(ushort id, byte flagId, ushort next = 0) =>
        Action(id, flagId, mode: 0x0A, value: 9, next);

    private static TriggerRecord Add(ushort id, byte flagId, int value) =>
        Action(id, flagId, mode: 0x12, value, next: 0);

    private static TriggerRecord Action(ushort id, byte flagId, byte mode, int value, ushort next) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 7,
            RepeatCount = 0, ChildTriggerId = 0, NextTriggerId = next,
            Parameters = new Dictionary<string, long> { ["FlagId"] = flagId, ["Flag"] = mode, ["Value"] = value },
        };
}
