// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

public class TriggerExecutorTests
{
    [Theory]
    [InlineData(0x0A, 40, 7, 7)]
    [InlineData(0x12, 40, 7, 47)]
    [InlineData(0x02, 40, 7, 33)]
    public void ModeByteBitsSelectAssignAddAndSubtract(byte mode, int initial, int operand, int expected)
    {
        var state = new TriggerState();
        state.Write(3, FlagWriteMode.Assign, initial);
        new TriggerExecutor(new TriggerTree([Action(1, 3, mode, operand)], 0xFFFF), state).Run(9);
        Assert.Equal(expected, state.Read(3));
    }

    [Fact]
    public void ClearLiteralBitReadsOperandIndirectly()
    {
        var state = new TriggerState();
        state.Write(9, FlagWriteMode.Assign, 77);
        new TriggerExecutor(new TriggerTree([Action(1, 3, 0x08, 9)], 0xFFFF), state).Run(1);
        Assert.Equal(77, state.Read(3));
    }

    [Fact]
    public void FlagOperandIsTruncatedToItsLowByte()
    {
        var state = new TriggerState();
        new TriggerExecutor(new TriggerTree([Action(1, 3, 0x0A, 0x1234)], 0xFFFF), state).Run(1);
        Assert.Equal(0x34, state.Read(3));
    }

    [Fact]
    public void SignedWriteAndUnsignedReadAsymmetryIsDeliberate()
    {
        var state = new TriggerState();
        TriggerRecord action = Action(1, 3, 0x02, 1);
        TriggerRecord condition = Condition(1, repeat: 1, child: 0, next: 0, new()
        {
            ["Comparison"] = 4, ["FlagId"] = 3, ["Flag"] = 1,
            ["IndirectFlagId"] = 0, ["Value"] = 0,
        });
        new TriggerExecutor(new TriggerTree([action], 0xFFFF), state).Run(1);

        Assert.Equal(255, state.Read(3));
        Assert.Equal(
            TriggerConditionResult.Satisfied,
            TriggerConditionEvaluator.Evaluate(condition, state, 1));
    }

    [Fact]
    public void InheritedRepeatOneRetiresSuccessfulActionBeforeVisitingItsChild()
    {
        var state = new TriggerState();
        TriggerRecord condition = Condition(1, repeat: 1, child: 0xFFFF, next: 0, new()
        {
            ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
            ["IndirectFlagId"] = 0, ["Value"] = 0,
        });
        TriggerRecord parent = Action(1, 3, 0x12, 1, child: 0xFFFE);
        TriggerRecord child = Action(2, 4, 0x12, 1);
        var executor = new TriggerExecutor(
            new TriggerTree([condition, parent, child], rootTriggerId: 1), state);

        executor.Run(5);
        executor.Run(6);

        Assert.Equal(1, state.Read(3));
        Assert.Equal(1, state.Read(4));
    }

    [Fact]
    public void InactiveConditionIsSkippedWithItsChild()
    {
        var state = new TriggerState();
        TriggerRecord inactive = Condition(1, repeat: 0, child: 0xFFFF, next: 0, new()
        {
            ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
            ["IndirectFlagId"] = 0, ["Value"] = 0,
        });
        TriggerRecord child = Action(1, 2, 0x12, 1);
        new TriggerExecutor(new TriggerTree([inactive, child], 1), state).Run(1);
        Assert.Equal(0, state.Read(2));
    }

    [Fact]
    public void ZeroRootIdEndsTheChain()
    {
        var state = new TriggerState();
        new TriggerExecutor(new TriggerTree([Action(1, 2, 0x12, 1)], 0), state).Run(1);
        Assert.Equal(0, state.Read(2));
    }

    [Fact]
    public void UnsupportedRecordIsCountedAndBlocksItsChildButNotItsSibling()
    {
        var state = new TriggerState();
        TriggerRecord blocked = Condition(1, 1, 0xFFFF, 2, new());
        TriggerRecord child = Action(1, 3, 0x12, 1);
        TriggerRecord sibling = Condition(2, 1, 0xFFFE, 0, new()
        {
            ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
            ["IndirectFlagId"] = 0, ["Value"] = 0,
        });
        TriggerRecord siblingChild = Action(2, 4, 0x12, 1);
        TriggerRunResult result = new TriggerExecutor(
            new TriggerTree([blocked, child, sibling, siblingChild], 1), state).Run(1);

        Assert.Equal(new BlockedTrigger(213, 72, 1, TriggerContext.World), Assert.Single(result.Blocked));
        Assert.Equal(0, state.Read(3));
        Assert.Equal(1, state.Read(4));
    }

    [Fact]
    public void TimerActionStampsTheCurrentTurn()
    {
        var state = new TriggerState();
        new TriggerExecutor(new TriggerTree([Timer(1, 5)], 0xFFFF), state).Run(27);
        Assert.Equal(27, state.ReadTimer(5));
    }

    [Fact]
    public void FirstSimulationTurnStartsTimerAtOneRatherThanTheUnstartedSentinel()
    {
        GameDatabase db = TestWorld.Database();
        var tree = new TriggerTree([Timer(1, 5)], 0xFFFF);
        var sim = new GameSimulation(db, TestWorld.World(db), 1, triggers: tree);

        sim.Tick();

        Assert.Equal(1, sim.World.Triggers.ReadTimer(5));
        Assert.True(sim.World.Triggers.IsStarted(5));
    }

    [Fact]
    public void RootRepeatComesFromResolvedRootIncludingFirstRecordFallback()
    {
        TriggerRecord first = Condition(3, repeat: 255, child: 0, next: 0, FlagEqualsZero());
        var tree = new TriggerTree([first], rootTriggerId: 99);
        Assert.Equal(255, tree.RootRepeat);
    }

    [Fact]
    public void ExternalDisableZerosTheRuntimeRepeatByte()
    {
        TriggerRecord repeating = Condition(1, repeat: 255, child: 0, next: 0, FlagEqualsZero());
        var disabledState = new TriggerState();
        var executor = new TriggerExecutor(new TriggerTree([repeating], 1), disabledState);
        executor.SetActive(1, active: false);

        TriggerRecord authoredInactive = Condition(1, repeat: 0, child: 0, next: 0, FlagEqualsZero());
        var zeroState = new TriggerState();
        _ = new TriggerExecutor(new TriggerTree([authoredInactive], 1), zeroState);

        Assert.Equal(StateHash(zeroState), StateHash(disabledState));
    }

    [Fact]
    public void TickRunsTriggersAndEmitsTheProbeHeaderAndOneRow()
    {
        GameDatabase db = TestWorld.Database();
        var tree = new TriggerTree([Action(1, 3, 0x0A, 7)], 0xFFFF);
        var sim = new GameSimulation(db, TestWorld.World(db), 1, triggers: tree);
        var output = new StringWriter();
        sim.EmitTriggerTurnsTo(output);

        sim.Tick();

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(TriggerTurnCsvEmitter.Header, lines[0]);
        Assert.Equal(150, lines[0].Split(',').Length);
        Assert.Equal(150, lines[1].Split(',').Length);
        Assert.Equal("", lines[1].Split(',')[0]);
        Assert.Equal("1", lines[1].Split(',')[1]);
        Assert.Equal(7, sim.World.Triggers.Read(3));
    }

    [Theory]
    [InlineData(5, 2, 7, false)]   // >= 2 seconds is >= 8 ticks, and turn 7 is not there yet
    [InlineData(5, 2, 8, true)]
    [InlineData(1, 2, 7, true)]    // < 2 seconds still holds at turn 7
    [InlineData(3, 0, 0, true)]    // == 0 seconds: level 1's own bootstrap, true from turn 1
    public void LevelTimeConditionScalesItsValueFromSecondsToTicks(
        byte comparison, int seconds, int turn, bool fires)
    {
        var state = new TriggerState();
        TriggerRecord condition = LevelTime(1, comparison, seconds, child: 0xFFFF);
        new TriggerExecutor(
            new TriggerTree([condition, Action(1, 3, 0x0A, 9)], 1), state).Run(turn);

        Assert.Equal(fires ? 9 : 0, state.Read(3));
    }

    /// <summary>The load sweep and action type 14 are two halves of one behaviour, so they
    /// are tested as one: the same tree runs a party root only after a type 14 has armed
    /// it.</summary>
    [Fact]
    public void PartySubtreeStaysDisabledUntilActionFourteenArmsIt()
    {
        var armed = new TriggerState();
        var unarmed = new TriggerState();
        TriggerRecord[] records = PartyRecords();
        HeroPartyThing[] parties = [Party(0, triggerId: 5, (6, 9))];

        // Root 0xFFFE is the type-14 action; root 0 is no world root at all, which leaves
        // the party subtree exactly as the level initialiser left it.
        new TriggerExecutor(new TriggerTree(records, 0xFFFE, parties), armed).Run(1);
        new TriggerExecutor(new TriggerTree(records, 0, parties), unarmed).Run(1);

        Assert.Equal(1, armed.Read(3));
        Assert.Equal(0, unarmed.Read(3));
    }

    /// <summary>The arming walk enters a condition's child chain and an action's next
    /// chain, and never an action's child chain — so what hangs under an action keeps
    /// whatever state it was authored with, through both the load sweep and the arm.
    /// </summary>
    [Fact]
    public void ArmingReachesAnActionsSiblingsButNotItsChildren()
    {
        var state = new TriggerState();
        // 5 -> child action 1 (flag 3), whose child is condition 6 (flag 4) and whose next
        // is condition 7 (flag 5). Both 6 and 7 are authored inactive, so only the one the
        // arming walk reaches can fire.
        TriggerRecord[] records =
        [
            Condition(5, repeat: 255, child: 0xFFFF, next: 0, FlagEqualsZero()),
            Condition(6, repeat: 0, child: 0xFFFE, next: 0, FlagEqualsZero()),
            Condition(7, repeat: 0, child: 0xFFFC, next: 0, FlagEqualsZero()),
            Action(1, 3, 0x0A, 1, child: 6, next: 7),
            Action(2, 4, 0x0A, 1),
            Action(4, 5, 0x0A, 1),
            PartyAction(3, partyId: 0),
        ];

        new TriggerExecutor(
            new TriggerTree(records, 0xFFFD, [Party(0, triggerId: 5)]), state).Run(1);

        Assert.Equal(1, state.Read(3));
        Assert.Equal(0, state.Read(4));
        Assert.Equal(1, state.Read(5));
    }

    [Fact]
    public void AMemberSlotWithNoCreatureIsNotArmed()
    {
        TriggerRecord[] records = PartyRecords();
        var occupied = new TriggerState();
        var empty = new TriggerState();

        // Same member trigger id either way; only the creature id byte differs, and that
        // is the byte DK2 tests at member +0x1C before arming. Record 7 is authored active
        // and the load sweep disables it in both runs, so any difference left is the arm's.
        new TriggerExecutor(
            new TriggerTree(records, 0xFFFE, [Party(0, 5, (7, 9))]), occupied).Run(1);
        new TriggerExecutor(
            new TriggerTree(records, 0xFFFE, [Party(0, 5, (7, 0))]), empty).Run(1);

        Assert.NotEqual(StateHash(occupied), StateHash(empty));
    }

    [Fact]
    public void ActionFourteenIsCountedAsPartialNotAsBlockedOrFinished()
    {
        TriggerRunResult result = new TriggerExecutor(
                new TriggerTree(PartyRecords(), 0xFFFE, [Party(0, 5, (7, 9))]), new TriggerState())
            .Run(1);

        Assert.Empty(result.Blocked);
        Assert.Equal([new BlockedTrigger(214, 14, 0xFFFE, TriggerContext.World)], result.Partial);
    }

    /// <summary>Condition type 30 is the player's gold. Its arm reads one dword,
    /// <c>[player+0x7E]</c>, and compares it (<c>0x004C841F</c>); the player frame keeps
    /// that dword by summing every Treasury and Dungeon Heart room it owns
    /// (<c>0x004B9F57</c>), which is the quantity <see cref="GoldStore.Total"/> already
    /// holds. ADR-0110.</summary>
    [Theory]
    [InlineData((byte)5, 400, true)]    // >= 400 holds on 500
    [InlineData((byte)5, 600, false)]
    [InlineData((byte)2, 600, true)]    // <= 600 holds on 500
    public void ConditionThirtyComparesThePlayersGold(byte comparison, int threshold, bool holds)
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        world.TileAtIndex(0).OwnerPlayerId = 3;
        world.TileAtIndex(0).GoldHeld = 500;

        TriggerRecord[] records = [Gold(5, comparison, threshold, child: 0xFFFF), Action(1, 3, 0x0A, 1)];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(3, 5)]);
        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(holds ? 1 : 0, world.Triggers.Read(3));
    }

    /// <summary>Condition type 31 is the gold this keeper has mined — the statistics
    /// struct's <c>GoldMined</c>, which lands on <c>[player+0x15A]</c> (ADR-0112). It is
    /// not the gold the keeper <i>has</i>: that is type 30, and the two diverge the moment
    /// any of it is spent.</summary>
    [Fact]
    public void ConditionThirtyOneComparesGoldMinedNotGoldHeld()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        world.Player(1).GoldMined = 500;   // mined it all and spent it all
        Assert.Equal(0, GoldStore.Total(world, 1));

        TriggerRecord[] records =
        [
            Counter(5, type: 31, comparison: 5, threshold: 400, child: 0xFFFF),
            Counter(6, type: 30, comparison: 5, threshold: 400, child: 0xFFFE),
            Action(1, 3, 0x0A, 1),
            Action(2, 4, 0x0A, 1),
        ];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(1, 5)]);
        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(1, world.Triggers.Read(3));
        Assert.Equal(0, world.Triggers.Read(4));
    }

    /// <summary>Condition type 32 is the player's mana. Same three-instruction arm as
    /// type 30, reading <c>[player+0x82]</c>, which DK2's own debug dumper prints as
    /// <c>CurrentMana</c> (`player-object.md`).</summary>
    [Theory]
    [InlineData((byte)5, 200, true)]
    [InlineData((byte)5, 400, false)]
    public void ConditionThirtyTwoComparesThePlayersMana(byte comparison, int threshold, bool holds)
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        world.Player(1).Mana = 300;

        TriggerRecord[] records =
        [
            Counter(5, type: 32, comparison, threshold, child: 0xFFFF),
            Action(1, 3, 0x0A, 1),
        ];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(1, 5)]);
        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(holds ? 1 : 0, world.Triggers.Read(3));
    }

    [Fact]
    public void Type25CountsRoomInstancesNotTiles()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState separate = RoomWorld(database);
        PlaceRoom(separate, database, 1, RoomTypeId.Treasury, new TilePos(3, 3), new TilePos(6, 3));
        WorldState joined = RoomWorld(database);
        PlaceRoom(joined, database, 1, RoomTypeId.Treasury, new TilePos(3, 3), new TilePos(4, 3));

        Assert.True(RoomConditionHolds(separate, database, 25, 1, RoomTypeId.Treasury, 3, 2));
        Assert.True(RoomConditionHolds(joined, database, 25, 1, RoomTypeId.Treasury, 3, 1));
    }

    [Fact]
    public void Type25RoomIdZeroCountsEveryOwnedRoomType()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = RoomWorld(database);
        PlaceRoom(world, database, 1, RoomTypeId.Treasury, new TilePos(3, 3));
        PlaceRoom(world, database, 1, RoomTypeId.Lair, new TilePos(6, 3));

        Assert.True(RoomConditionHolds(world, database, 25, 1, 0, 3, 2));
    }

    [Fact]
    public void Type25CountsOnlyTheAskingKeepersRooms()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = RoomWorld(database);
        PlaceRoom(world, database, 1, RoomTypeId.Treasury, new TilePos(3, 3));
        PlaceRoom(world, database, 2, RoomTypeId.Treasury, new TilePos(6, 3));

        Assert.True(RoomConditionHolds(world, database, 25, 1, RoomTypeId.Treasury, 3, 1));
    }

    [Fact]
    public void Type24AssumesRoomSizeIsTheSlabCount()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = RoomWorld(database);
        PlaceRoom(
            world, database, 1, RoomTypeId.Treasury,
            new TilePos(3, 3), new TilePos(4, 3), new TilePos(4, 4));

        Assert.True(RoomConditionHolds(world, database, 24, 1, 0, 3, 3));
    }

    [Fact]
    public void Type24WithRoomIdSumsOnlyThatRoomType()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = RoomWorld(database);
        PlaceRoom(
            world, database, 1, RoomTypeId.Treasury, new TilePos(3, 3), new TilePos(4, 3));
        PlaceRoom(
            world, database, 1, RoomTypeId.Lair,
            new TilePos(3, 5), new TilePos(4, 5), new TilePos(5, 5));

        Assert.True(RoomConditionHolds(world, database, 24, 1, RoomTypeId.Treasury, 3, 2));
    }

    [Theory]
    [InlineData((byte)1, false)]
    [InlineData((byte)2, true)]
    [InlineData((byte)3, true)]
    [InlineData((byte)4, false)]
    [InlineData((byte)5, true)]
    [InlineData((byte)6, false)]
    public void RoomCountersUseAllSixSharedComparisonCodes(byte comparison, bool holds)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = RoomWorld(database);
        PlaceRoom(
            world, database, 1, RoomTypeId.Treasury, new TilePos(3, 3), new TilePos(6, 3));

        Assert.Equal(
            holds,
            RoomConditionHolds(world, database, 25, 1, RoomTypeId.Treasury, comparison, 2));
    }

    [Fact]
    public void Type25ComparisonZeroAvailabilityModeIsDeclined()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = RoomWorld(database);
        PlaceRoom(world, database, 1, RoomTypeId.Treasury, new TilePos(3, 3));
        TriggerRecord[] records =
        [
            RoomCounter(5, 25, comparison: 0, RoomTypeId.Treasury, threshold: 0, child: 0xFFFF),
            Action(1, 3, 0x0A, 1),
        ];

        TriggerRunResult result = new TriggerExecutor(
            new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(1, 5)]),
            world.Triggers, world, database).Run(1);

        Assert.Equal(0, world.Triggers.Read(3));
        Assert.Equal([new BlockedTrigger(213, 25, 5, TriggerContext.Player)], result.Blocked);
    }

    [Theory]
    [InlineData((byte)27, (byte)0x08, 1)]
    [InlineData((byte)27, (byte)0x10, 2)]
    [InlineData((byte)27, (byte)0x18, 1)]
    [InlineData((byte)28, (byte)0x08, 1)]
    [InlineData((byte)28, (byte)0x10, 2)]
    [InlineData((byte)28, (byte)0x18, 1)]
    public void DoorAndTrapConstructionModesCountOnlyTheWalkedKeeper(
        byte type, byte flags, int expected)
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        ThingProjection.Place(world, database,
        [
            Door(1, Keeper, underConstruction: true),
            Door(1, Keeper, underConstruction: false),
            Door(2, Keeper, underConstruction: false),
            Door(1, PlayerIds.Neutral, underConstruction: true),
            Trap(1, Keeper, underConstruction: true),
            Trap(1, Keeper, underConstruction: false),
            Trap(2, Keeper, underConstruction: false),
            Trap(1, PlayerIds.Neutral, underConstruction: true),
        ]);

        RunDoorOrTrapCondition(world, database, type, targetId: 1, flags, expected);

        Assert.Equal(1, world.Triggers.Read(3));
    }

    [Theory]
    [InlineData((byte)27, (byte)0, 3)]
    [InlineData((byte)27, (byte)1, 2)]
    [InlineData((byte)28, (byte)0, 3)]
    [InlineData((byte)28, (byte)1, 2)]
    public void DoorAndTrapOrdinaryModeCountsAllOrOneType(byte type, byte targetId, int expected)
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        ThingProjection.Place(world, database,
        [
            Door(1, Keeper, true), Door(1, Keeper, false), Door(2, Keeper, false),
            Trap(1, Keeper, true), Trap(1, Keeper, false), Trap(2, Keeper, false),
        ]);

        RunDoorOrTrapCondition(world, database, type, targetId, flags: 1, expected);
        Assert.Equal(1, world.Triggers.Read(3));
    }

    [Theory]
    [InlineData((byte)27)]
    [InlineData((byte)28)]
    public void DoorAndTrapAvailabilityModesRemainDeclinedAfterConstructionStateExists(byte type)
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        ThingProjection.Place(world, database, [Door(1, Keeper, false), Trap(1, Keeper, false)]);

        TriggerRunResult result = RunDoorOrTrapCondition(
            world, database, type, targetId: 1, flags: 1, expected: 0, comparison: 0);

        Assert.Equal([new BlockedTrigger(213, type, 5, TriggerContext.Player)], result.Blocked);
    }

    [Fact]
    public void NamedBoulderTrapCountIncludesOwnedLiveBoulderObjects()
    {
        GameDatabase basis = TestWorld.Database();
        var database = new GameDatabase(
            basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values, basis.Variables,
            objects:
            [
                new ObjectDef
                {
                    Name = "fixture boulder", ObjectId = 74, Flags = ObjectFlags.None,
                    Hp = 1, ManaValue = 0, RoomCapacity = 0, StartState = 0,
                    PickUpPriority = 0, Width = new Fp32(0), Height = new Fp32(0),
                    Mass = new Fp32(0), Speed = new Fp32(0), NameStringId = 0,
                    TooltipStringId = 0,
                },
            ],
            traps:
            [
                new TrapDef
                {
                    Name = "fixture", TrapId = 4, Health = 1, GoldCost = 1,
                    ManufactureRequired = 1, ShotTypeId = 26, ManaUpkeepPerSecond = 0,
                },
            ]);
        WorldState world = TestWorld.World(database);
        ThingProjection.Place(world, database, [Trap(4, Keeper, false)]);
        world.SpawnObject(database, 74, Keeper, tileIndex: 0);
        world.SpawnObject(database, 74, PlayerIds.Neutral, tileIndex: 1);

        RunDoorOrTrapCondition(world, database, 28, targetId: 4, flags: 1, expected: 2);

        Assert.Equal(1, world.Triggers.Read(3));
    }

    /// <summary>And it is the walked player's gold, not any player's. Two roots, two
    /// keepers, one holding gold: only that one's subtree runs.</summary>
    [Fact]
    public void ConditionThirtyReadsTheGoldOfThePlayerWhoseTreeIsBeingWalked()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        world.TileAtIndex(0).OwnerPlayerId = 3;
        world.TileAtIndex(0).GoldHeld = 500;

        TriggerRecord[] records =
        [
            Gold(5, comparison: 5, threshold: 400, child: 0xFFFF),
            Gold(6, comparison: 5, threshold: 400, child: 0xFFFE),
            Action(1, 3, 0x0A, 1),
            Action(2, 4, 0x0A, 1),
        ];
        var tree = new TriggerTree(
            records, 0,
            playerRoots: [new TriggerPlayerRoot(3, 5), new TriggerPlayerRoot(1, 6)]);
        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(1, world.Triggers.Read(3));
        Assert.Equal(0, world.Triggers.Read(4));
    }

    /// <summary>Condition type 19 counts the keeper's creatures (<c>0x004C7B9D</c>). Naming
    /// a creature id reads DK2's per-type cache through
    /// <c>CPlayer::GetNumberOfCreaturesOfType</c> (<c>0x004BA7D0</c>), and that count is
    /// of the type asked for, whatever it is.</summary>
    [Theory]
    [InlineData(GoblinId, (byte)5, 2, true)]     // two goblins, >= 2
    [InlineData(GoblinId, (byte)5, 3, false)]
    [InlineData(ImpId, (byte)3, 3, true)]        // three imps, == 3
    [InlineData(ImpId, (byte)4, 3, false)]       // 4 is >, not >=
    public void ConditionNineteenCountsTheKeepersCreaturesOfOneType(
        byte creatureId, byte comparison, int threshold, bool holds)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);

        RunCreatureCondition(world, database, creatureId, comparison, threshold);

        Assert.Equal(holds ? 1 : 0, world.Triggers.Read(3));
    }

    /// <summary>Creature id 0 is not "no filter" — it is the total, and the total
    /// <b>excludes workers</b>. Its arm takes <c>TotalNumberOfOwnedThings[0]</c> and walks
    /// the owned list decrementing once per definition with bit 0 of <c>+0x686</c> set
    /// (<c>0x004C7BD9</c>). Across the shipped roster that bit is exactly Imp and Dwarf.
    /// So this keeper's three imps and two goblins count as two.</summary>
    [Fact]
    public void ConditionNineteenWithCreatureIdZeroCountsEveryCreatureExceptWorkers()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);

        RunCreatureCondition(world, database, creatureId: 0, comparison: 3, threshold: 2);

        Assert.Equal(1, world.Triggers.Read(3));
    }

    /// <summary>And asking for imps by name still counts them, which is the whole
    /// asymmetry: the same three imps are 0 of the total and 3 of their own type.</summary>
    [Fact]
    public void ConditionNineteenCountsWorkersWhenAskedForThemByName()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);

        RunCreatureCondition(world, database, ImpId, comparison: 3, threshold: 3);

        Assert.Equal(1, world.Triggers.Read(3));
    }

    /// <summary>It counts the walked keeper's creatures, not everyone's. The rival owns
    /// four goblins; the keeper's own two are what the arm sees.</summary>
    [Fact]
    public void ConditionNineteenCountsOnlyTheWalkedPlayersCreatures()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);
        for (int i = 0; i < 4; i++)
            world.SpawnCreature(database, GoblinId, PlayerIds.Neutral, new TilePos(6, 6));

        RunCreatureCondition(world, database, GoblinId, comparison: 3, threshold: 2);

        Assert.Equal(1, world.Triggers.Read(3));
    }

    /// <summary>Q269: types 20 and 21 count the player's non-workers, of the named type, whose mood
    /// band (<c>+0x28A</c>, written by <c>Anger_Add</c>) is happy or angry (<c>0x004C7B20</c> arms
    /// 0x14 and 0x15). Ours declined both.</summary>
    [Theory]
    [InlineData(20, false, 1)]
    [InlineData(20, true, 0)]
    [InlineData(21, true, 1)]
    [InlineData(21, false, 0)]
    public void ConditionsTwentyAndTwentyOneCountHappyAndAngryCreatures(byte type, bool angry, int flag)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);
        CreatureDef goblin = database.CreatureById(GoblinId);
        foreach (CreatureState c in world.Creatures.Where(c => c.CreatureTypeId == GoblinId))
            c.Anger.Lair = angry ? CreatureAngerRules.AngryAt(goblin) + 1 : 0;

        TriggerRecord[] records =
        [
            CreatureCondition(5, type: type, creatureId: GoblinId, comparison: 5, threshold: 1),
            Action(1, 3, 0x0A, 1),
        ];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        TriggerRunResult result = new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(flag, world.Triggers.Read(3));
        Assert.Empty(result.Blocked);
    }

    /// <summary>Condition type <b>52</b> counts creatures at or above a level, and the
    /// level is byte 03 — the byte the previous reader called a player id. The arm
    /// (<c>0x004C7E4E</c>) compares it against the creature's own <c>+0x403</c>.</summary>
    [Theory]
    [InlineData(1, 2)]   // both goblins qualify at level 1
    [InlineData(4, 1)]   // only the trained one
    [InlineData(9, 0)]   // nothing is that high
    public void ConditionFiftyTwoCountsCreaturesAtOrAboveTheAuthoredLevel(
        byte level, int expected)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);
        world.Creatures.First(c => c.CreatureTypeId == GoblinId).Level = 4;

        AssertLeveledCount(world, database, level, expected);
    }

    /// <summary>And it excludes workers, by the same <c>+0x686</c> bit 0 that keeps them
    /// out of type 19's total. <see cref="CreatureWorld"/> holds three imps, so a
    /// threshold of 1 that counted workers would answer five rather than two.</summary>
    [Fact]
    public void ConditionFiftyTwoNeverCountsAWorkerHoweverHighItsLevel()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);
        foreach (CreatureState imp in world.Creatures.Where(c => c.CreatureTypeId == ImpId))
            imp.Level = 10;

        AssertLeveledCount(world, database, level: 1, expected: 2);
    }

    /// <summary>Another keeper's veterans are not this keeper's count.</summary>
    [Fact]
    public void ConditionFiftyTwoCountsOnlyTheKeeperWhoseTreeAsked()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);
        world.SpawnCreature(database, GoblinId, PlayerIds.Neutral, new TilePos(4, 4));
        world.Creatures[^1].Level = 9;

        AssertLeveledCount(world, database, level: 9, expected: 0);
    }

    /// <summary>Condition type <b>71</b> reads the keeper's slap tally
    /// (<c>player+0xEE6</c>, <c>0x004C8491</c>). Nothing slaps yet, so it is zero — which
    /// is an answer, not a decline.</summary>
    [Fact]
    public void ConditionSeventyOneReadsTheSlapTallyAndTodayThatIsZero()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);

        TriggerRunResult result = RunPlayerCondition(
            world, database, type: 71, comparison: 5, value: 1, extra: null);

        Assert.Empty(result.Blocked);
        Assert.Equal(0, world.Triggers.Read(3));
    }

    /// <summary>The same condition fires the moment the tally is set, which is what makes
    /// the zero above a reading of state rather than a constant: when slapping arrives it
    /// has one field to write and this condition is already right.</summary>
    [Fact]
    public void ConditionSeventyOneFiresOnceTheTallyIsSet()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);
        world.Player(Keeper).SlapCount = 11;

        RunPlayerCondition(world, database, type: 71, comparison: 5, value: 11, extra: null);

        Assert.Equal(1, world.Triggers.Read(3));
    }

    /// <summary>Condition type <b>73</b> returns false outright when the keeper is not
    /// possessing (<c>0x004C8500</c>), without reading its comparison or value. Nothing
    /// possesses here, so it is answered false — answered, not declined.</summary>
    [Fact]
    public void ConditionSeventyThreeIsFalseWhileNoKeeperPossessesAnything()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);

        // Value 0 under ">=" is true of every number, so a comparison reached at all would
        // fire. It does not: the arm returns before it.
        TriggerRunResult result = RunPlayerCondition(
            world, database, type: 73, comparison: 5, value: 0, extra: null);

        Assert.Empty(result.Blocked);
        Assert.Equal(0, world.Triggers.Read(3));
    }

    /// <summary>But the moment a keeper <i>is</i> possessing, it declines. What the arm
    /// compares then is the possessed creature's <c>+0x488</c>, which is unread (Q279).
    /// This test exists so that adding possession fails here rather than leaving type 73
    /// silently answering false forever.</summary>
    [Fact]
    public void ConditionSeventyThreeDeclinesRatherThanGuessOncePossessionExists()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);
        world.Player(Keeper).PossessedCreatureId = world.Creatures[0].Id;

        TriggerRunResult result = RunPlayerCondition(
            world, database, type: 73, comparison: 5, value: 0, extra: null);

        Assert.Equal([new BlockedTrigger(213, 73, 5, TriggerContext.Player)], result.Blocked);
    }

    /// <summary>Condition type <b>46</b> is DK2's own silent false. Its slot in the world
    /// dispatcher's byte table points at <c>0x00518941</c> — the unhandled path's tail,
    /// past the <c>printf</c> — so the engine returns zero for it deliberately and says
    /// nothing, unlike every type it genuinely does not implement.</summary>
    [Theory]
    [InlineData(true)]    // reached through a player tree, by delegation
    [InlineData(false)]   // and in the world tree, where it is dispatched directly
    public void ConditionFortySixIsAnsweredFalseInEitherTree(bool inPlayerTree)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = CreatureWorld(database);
        TriggerRecord[] records =
        [
            PlayerCondition(5, type: 46, comparison: 5, value: 0, extra: null),
            Action(1, 3, 0x0A, 1),
        ];
        TriggerTree tree = inPlayerTree
            ? new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)])
            : new TriggerTree(records, 5);

        TriggerRunResult result =
            new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Empty(result.Blocked);
        Assert.Equal(0, world.Triggers.Read(3));
    }

    /// <summary>Asks type 52 twice — "at least <paramref name="expected"/>", which must
    /// fire, and one more, which must not. Two inequalities pin the count itself; one
    /// would pass for any number on the right side of it.</summary>
    private static void AssertLeveledCount(
        WorldState world, GameDatabase database, byte level, int expected)
    {
        RunPlayerCondition(world, database, 52, 5, expected, ("Level", level));
        Assert.Equal(1, world.Triggers.Read(3));
        world.Triggers.Write(3, FlagWriteMode.Assign, 0);

        RunPlayerCondition(world, database, 52, 5, expected + 1, ("Level", level));
        Assert.Equal(0, world.Triggers.Read(3));
    }

    private static TriggerRunResult RunPlayerCondition(
        WorldState world,
        GameDatabase database,
        byte type,
        byte comparison,
        int value,
        (string Name, long Value)? extra)
    {
        TriggerRecord[] records =
        [
            PlayerCondition(5, type, comparison, value, extra),
            Action(1, 3, 0x0A, 1),
        ];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        return new TriggerExecutor(tree, world.Triggers, world, database).Run(1);
    }

    private static TriggerRunResult RunDoorOrTrapCondition(
        WorldState world,
        GameDatabase database,
        byte type,
        byte targetId,
        byte flags,
        int expected,
        byte comparison = 3)
    {
        TriggerRecord condition = new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 5, Type = type, RepeatCount = 1,
            ChildTriggerId = 0xFFFF, NextTriggerId = 0,
            Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = comparison, ["Value"] = expected,
                ["TargetId"] = targetId, ["Flag"] = flags,
            },
        };
        var tree = new TriggerTree(
            [condition, Action(1, 3, 0x0A, 1)], 0,
            playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        return new TriggerExecutor(tree, world.Triggers, world, database).Run(1);
    }

    private static DoorThing Door(byte type, byte owner, bool underConstruction) =>
        new(new byte[20])
        {
            DoorId = type, PlayerId = owner, Flags = underConstruction ? (byte)0x02 : (byte)0,
            Unknown_0011 = new byte[3],
        };

    private static TrapThing Trap(byte type, byte owner, bool underConstruction) =>
        new(new byte[16])
        {
            TrapId = type, PlayerId = owner, NumberOfShots = underConstruction ? (byte)0 : (byte)1,
        };

    private static TriggerRecord PlayerCondition(
        ushort id, byte type, byte comparison, int value, (string Name, long Value)? extra)
    {
        var parameters = new Dictionary<string, long>
        {
            ["Comparison"] = comparison, ["Value"] = value,
        };
        if (extra is { } e) parameters[e.Name] = e.Value;
        return new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 1,
            ChildTriggerId = 0xFFFF, NextTriggerId = 0, Parameters = parameters,
        };
    }

    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private const byte GoblinId = 7;

    /// <summary>Three imps and two goblins, all the keeper's.</summary>
    private static WorldState CreatureWorld(GameDatabase database)
    {
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int i = 0; i < 3; i++)
            world.SpawnCreature(database, ImpId, Keeper, new TilePos(2, 2));
        for (int i = 0; i < 2; i++)
            world.SpawnCreature(database, GoblinId, Keeper, new TilePos(3, 3));
        return world;
    }

    private static void RunCreatureCondition(
        WorldState world, GameDatabase database, byte creatureId, byte comparison, int threshold)
    {
        TriggerRecord[] records =
        [
            CreatureCondition(5, type: 19, creatureId, comparison, threshold),
            Action(1, 3, 0x0A, 1),
        ];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);
    }

    private static TriggerRecord CreatureCondition(
        ushort id, byte type, byte creatureId, byte comparison, int threshold) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 1,
            ChildTriggerId = 0xFFFF, NextTriggerId = 0,
            Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = comparison, ["CreatureId"] = creatureId,
                ["Value"] = threshold,
            },
        };

    /// <summary>The room list types 24 and 25 read is built once per <see cref="Run"/> and
    /// held for the whole walk (ADR-0114). That is only sound while <b>no action this
    /// executor performs can alter terrain</b> — a flag write, a timer start and party
    /// arming cannot. This pins the set so that adding another is a failure here rather
    /// than a room condition silently reading the map as it was before the write.</summary>
    [Fact]
    public void OnlyActionsThatCannotAlterTerrainArePerformed()
    {
        // 22 is the one that can, and it clears TriggerRun.Rooms after the write. 32 ruins a
        // gate's tiles without touching their terrain, which no room condition reads; 18 writes
        // only alliance bytes.
        // Anything added here without doing the same brings back the bug this test
        // was written for: a room condition later in the turn reading the old map.
        byte[] performed = Enumerable.Range(0, 256)
            .Select(t => (byte)t).Where(TriggerExecutor.Performs).ToArray();

        Assert.Equal(
            [(byte)1, (byte)6, (byte)7, (byte)8, (byte)9, (byte)10, (byte)11, (byte)14, (byte)17, (byte)18, (byte)22, (byte)23, (byte)28, (byte)32, (byte)37, (byte)44, (byte)45, (byte)47],
            performed);
    }

    /// <summary>Action type 22's coordinates are <b>one-based</b>: the arm at
    /// <c>0x00519425</c> decrements each before the lookup, so an authored (4,3)
    /// writes tile (3,2). Getting this wrong moves every scripted terrain change one
    /// tile up and left, which no test of the write alone would catch.</summary>
    [Fact]
    public void AlterTerrainCoordinatesAreOneBased()
    {
        (WorldState world, GameDatabase database) = AlterWorld();
        RunAlter(world, database, Dk2TerrainFixture.Rock, Keeper, x: 4, y: 3);

        Assert.Equal(Dk2TerrainFixture.Rock, world.Tile(3, 2).TerrainId);
    }

    /// <summary>Ownable terrain takes the record's player; terrain nobody can own is
    /// cleared instead of keeping one (Q95, ADR-0045). A Claimed Path with no owner is
    /// a tile DK2 cannot produce, and a fixture that builds one measures nothing.
    /// </summary>
    [Theory]
    [InlineData(Dk2TerrainFixture.ClaimedPath, Keeper)]
    [InlineData(Dk2TerrainFixture.DirtPath, PlayerIds.Neutral)] // Tile_SetTerrain's neutral owner (Q514)
    public void AlterTerrainOwnsOnlyWhatCanBeOwned(byte terrainId, byte expectedOwner)
    {
        (WorldState world, GameDatabase database) = AlterWorld();
        RunAlter(world, database, terrainId, Keeper, x: 4, y: 4);

        ref TileState tile = ref world.Tile(3, 3);
        Assert.Equal(terrainId, tile.TerrainId);
        Assert.Equal(expectedOwner, tile.OwnerPlayerId);
    }

    /// <summary>Q301: a room terrain is built free for the record's player. A Wooden Bridge,
    /// placeable on water and lava, gets water laid first even over lava (the water test comes
    /// first in the room arm, <c>0x00519519</c>), so it spans water. Ours declined every room terrain,
    /// which left the Siege and Secret3 bridges unbuilt.</summary>
    [Theory]
    [InlineData(Dk2TerrainFixture.Water)]
    [InlineData(Dk2TerrainFixture.Lava)]
    public void AlterTerrainBuildsABridgeOnWaterLaidFirst(byte hazard)
    {
        (WorldState world, GameDatabase database) = AlterWorld();
        ref TileState tile = ref world.Tile(3, 3);
        tile.TerrainId = hazard;
        tile.OwnerPlayerId = PlayerIds.Neutral;

        RunAlter(world, database, Dk2TerrainFixture.WoodenBridgeTerrain, Keeper, x: 4, y: 4);

        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, world.Tile(3, 3).TerrainId);
        Assert.Equal(BridgeRules.SpansWater, world.Tile(3, 3).Flags);
        Assert.NotNull(RoomIndex.RoomAt(world, database, (3 * world.Width) + 3));
    }

    /// <summary>Q301: a room on the tile is sold first, with no refund, then the terrain written
    /// with the record's player: a Lair under a Claimed Path write becomes the player's path.</summary>
    [Fact]
    public void AlterTerrainSellsARoomTileFirst()
    {
        (WorldState world, GameDatabase database) = AlterWorld();
        RoomRules.Place(database, ref world.Tile(2, 2), database.Room(RoomTypeId.Lair), PlayerIds.FirstKeeper);
        const byte other = PlayerIds.FirstKeeper + 1;

        RunAlter(world, database, Dk2TerrainFixture.ClaimedPath, other, x: 3, y: 3);

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, world.Tile(2, 2).TerrainId);
        Assert.Equal(other, world.Tile(2, 2).OwnerPlayerId);
        Assert.Null(RoomIndex.RoomAt(world, database, (2 * world.Width) + 2));
    }

    /// <summary>Two cases the arm answers by succeeding and changing nothing: a
    /// coordinate off the map, and a tile that already carries the terrain asked for
    /// (<c>0x00519484</c>). Neither is an error and neither writes.</summary>
    [Fact]
    public void AlterTerrainSucceedsWithoutWritingOffTheMapOrOnItsOwnTerrain()
    {
        (WorldState world, GameDatabase database) = AlterWorld();
        byte before = world.Tile(3, 3).TerrainId;

        RunAlter(world, database, Dk2TerrainFixture.Rock, Keeper, x: 30_000, y: 30_000);
        Assert.Equal(before, world.Tile(3, 3).TerrainId);

        world.Tile(3, 3).OwnerPlayerId = PlayerIds.Unowned;
        RunAlter(world, database, before, Keeper, x: 4, y: 4);
        Assert.Equal(PlayerIds.Unowned, world.Tile(3, 3).OwnerPlayerId);
    }

    /// <summary>Action type 1's coordinates are <b>zero-based</b> — the arm at
    /// <c>0x00518B12</c> makes a tile centre out of each without decrementing, where type
    /// 22's arm decrements first. Two neighbouring arms reading the same two body words
    /// under different conventions is exactly the kind of quirk a test has to hold down.
    /// </summary>
    [Fact]
    public void CreateCreatureCoordinatesAreZeroBasedUnlikeAlterTerrain()
    {
        (WorldState world, GameDatabase database) = CreatureWorld();
        RunCreate(world, database, x: 4, y: 3);

        CreatureState made = Assert.Single(world.Creatures);
        Assert.Equal(new TilePos(4, 3), made.Tile);
    }

    /// <summary>Type, owner and level come off the record. <c>Level</c> is one-based and
    /// the arm reaches it by repetition — <c>Level - 1</c> calls to the level-up routine
    /// — so 0 and 1 are the same request and both leave a level-1 creature.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(200, CreatureLevelRules.MaxLevel)]
    public void CreateCreatureAppliesTheRecordsLevelOneBased(int asked, int expected)
    {
        (WorldState world, GameDatabase database) = CreatureWorld();
        RunCreate(world, database, x: 4, y: 3, level: asked);

        CreatureState made = Assert.Single(world.Creatures);
        Assert.Equal(Dk2TerrainFixture.GoblinCreatureId, made.CreatureTypeId);
        Assert.Equal(Keeper, made.OwnerPlayerId);
        Assert.Equal(expected, made.Level);
        Assert.Equal(
            CreatureLevelRules.MaxHealth(
                database, database.CreatureById(made.CreatureTypeId), expected),
            made.Health);
    }

    /// <summary>Creating a creature is reported <b>partial for every record</b>, and the
    /// reason is not that the creature is missing. The same arm copies the body's
    /// <c>Flag</c> byte onto the creature's <c>Flags</c> at <c>+0x46F</c>, and its bit 0
    /// — set by all 268 shipped records — is <c>DestroysRooms</c>, which nothing here
    /// reads (Q302). A census that called this performed would overstate the fidelity.
    /// </summary>
    [Fact]
    public void CreateCreatureReportsPartialBecauseTheFlagByteIsDropped()
    {
        (WorldState world, GameDatabase database) = CreatureWorld();
        TriggerRunResult result = RunCreate(world, database, x: 4, y: 3);

        Assert.Single(world.Creatures);
        BlockedTrigger partial = Assert.Single(result.Partial);
        Assert.Equal(1, partial.Type);
        Assert.Equal(TriggerContext.World, partial.Context);
        Assert.Empty(result.Unhandled);
    }

    /// <summary>Off the map nothing is created, and the walk still succeeds. Our
    /// <c>SpawnCreature</c> throws outside the map, so without this guard a record with a
    /// coordinate the level cannot hold would take the whole turn down.</summary>
    [Fact]
    public void CreateCreatureOffTheMapMakesNothingAndDoesNotThrow()
    {
        (WorldState world, GameDatabase database) = CreatureWorld();
        RunCreate(world, database, x: 30_000, y: 30_000);

        Assert.Empty(world.Creatures);
    }

    private static (WorldState World, GameDatabase Database) CreatureWorld()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        return (WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]),
            database);
    }

    private static TriggerRunResult RunCreate(
        WorldState world, GameDatabase database, int x, int y, int level = 1)
    {
        TriggerRecord[] records =
        [
            LevelTime(5, comparison: 5, seconds: 0, child: 0xFFFF),
            new ActionTriggerRecord(new byte[16])
            {
                Body = new byte[8], TriggerId = unchecked((ushort)-1), Type = 1,
                Parameters = new Dictionary<string, long>
                {
                    ["CreatureId"] = Dk2TerrainFixture.GoblinCreatureId,
                    ["PlayerId"] = Keeper, ["Level"] = level, ["Flag"] = 0x29,
                    ["PosX"] = x, ["PosY"] = y,
                },
            },
        ];
        var tree = new TriggerTree(records, 5, playerRoots: []);
        return new TriggerExecutor(tree, world.Triggers, world, database).Run(1);
    }

    /// <summary>Action 32 (<c>0x005196BB</c>): the one-based tile's room is ruined when it is a Hero
    /// Gate, so an imp may then convert it (<c>0x00454040</c>), its terrain unchanged; off a room it
    /// does nothing and succeeds.</summary>
    [Fact]
    public void CollapseHeroGateRuinsTheGateUnderItsOneBasedTile_AndAKeeperMayThenConvertIt()
    {
        (WorldState world, GameDatabase database) = AlterWorld();
        foreach ((int x, int y) in new[] { (3, 2), (4, 2), (3, 3), (4, 3) })
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.HeroGate2By2Terrain;
            tile.OwnerPlayerId = PlayerIds.Good;
        }
        Assert.False(TileWorkRules.Applies(world, database, world.Tile(3, 2), TileWorkKind.ConvertRoom, Keeper));

        TriggerRecord[] records = [LevelTime(5, comparison: 5, seconds: 0, child: 0xFFFF), CollapseRecord(1, x: 5, y: 4)];
        new TriggerExecutor(new TriggerTree(records, 5, playerRoots: []), world.Triggers, world, database).Run(1);

        foreach ((int x, int y) in new[] { (3, 2), (4, 2), (3, 3), (4, 3) })
        {
            Assert.True(world.Tile(x, y).RoomRuined);
            Assert.Equal(Dk2TerrainFixture.HeroGate2By2Terrain, world.Tile(x, y).TerrainId);
            Assert.True(TileWorkRules.Applies(world, database, world.Tile(x, y), TileWorkKind.ConvertRoom, Keeper));
        }
        Assert.False(world.Tile(2, 2).RoomRuined);
    }

    [Fact]
    public void CollapseHeroGateOnATileWithNoRoomDoesNothing()
    {
        (WorldState world, GameDatabase database) = AlterWorld();
        TriggerRecord[] records = [LevelTime(5, comparison: 5, seconds: 0, child: 0xFFFF), CollapseRecord(1, x: 3, y: 3)];
        TriggerRunResult result = new TriggerExecutor(new TriggerTree(records, 5, playerRoots: []), world.Triggers, world, database).Run(1);
        Assert.DoesNotContain(Enumerable.Range(0, world.TileCount), i => world.TileAtIndex(i).RoomRuined);
        Assert.Empty(result.Partial);
    }

    /// <summary>The ruin has no arm for the Tile gate, so action 32 there changes nothing; a heart
    /// is ruined in DK2 too, which ours declines and reports.</summary>
    [Theory]
    [InlineData(Dk2TerrainFixture.HeroGateTileTerrain, false)]
    [InlineData(Dk2TerrainFixture.DungeonHeartTerrain, true)]
    public void CollapseHeroGateLeavesATileGateAlone_AndDeclinesAHeart(byte terrain, bool declined)
    {
        (WorldState world, GameDatabase database) = AlterWorld();
        ref TileState tile = ref world.Tile(3, 3);
        tile.TerrainId = terrain;
        tile.OwnerPlayerId = Keeper;
        TriggerRecord[] records = [LevelTime(5, comparison: 5, seconds: 0, child: 0xFFFF), CollapseRecord(1, x: 4, y: 4)];
        TriggerRunResult result = new TriggerExecutor(new TriggerTree(records, 5, playerRoots: []), world.Triggers, world, database).Run(1);
        Assert.False(world.Tile(3, 3).RoomRuined);
        Assert.Equal(declined, result.Partial.Count > 0);
    }

    /// <summary>Action 18 (<c>0x00519376</c>): body byte 2 zero makes the two players allies, anything
    /// else breaks it, both ways. Level 11A's first turn allies Raksha with the other keepers, which
    /// the capture shows at player <c>+0x1AE</c> on turn 2.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void AllianceActionWritesBothPlayers_ZeroAllies(byte mode, bool allied)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, 4]);
        world.SetAllied(Keeper, 4, !allied);
        TriggerRecord[] records = [LevelTime(5, comparison: 5, seconds: 0, child: 0xFFFF), AllianceRecord(1, Keeper, 4, mode)];
        TriggerRunResult result = new TriggerExecutor(new TriggerTree(records, 5, playerRoots: []), world.Triggers, world, database).Run(1);
        Assert.Equal(allied, world.AreAllied(Keeper, 4));
        Assert.Equal(allied, world.AreAllied(new PlayerId(4), Keeper));
        Assert.Empty(result.Partial);
    }

    private static TriggerRecord AllianceRecord(ushort id, byte one, byte two, byte mode) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 18,
            Parameters = new Dictionary<string, long> { ["PlayerOneId"] = one, ["PlayerTwoId"] = two, ["Available"] = mode },
        };

    private static TriggerRecord CollapseRecord(ushort id, int x, int y) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 32,
            Parameters = new Dictionary<string, long> { ["Unreached_0000"] = 0, ["PosX"] = x, ["PosY"] = y },
        };

    private static (WorldState World, GameDatabase Database) AlterWorld()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        return (WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]),
            database);
    }

    private static TriggerRunResult RunAlter(
        WorldState world, GameDatabase database, byte terrainId, byte playerId, int x, int y)
    {
        // An action hangs off a condition, so the root is a level-time test that is
        // true on the first turn and the alter-terrain record is its child.
        TriggerRecord[] records =
        [
            LevelTime(5, comparison: 5, seconds: 0, child: 0xFFFF),
            AlterTerrainRecord(1, terrainId, playerId, x, y),
        ];
        var tree = new TriggerTree(records, 5, playerRoots: []);
        return new TriggerExecutor(tree, world.Triggers, world, database).Run(1);
    }

    private static TriggerRecord AlterTerrainRecord(
        ushort id, byte terrainId, byte playerId, int x, int y) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 22,
            Parameters = new Dictionary<string, long>
            {
                ["TerrainId"] = terrainId, ["PlayerId"] = playerId,
                ["PosX"] = x, ["PosY"] = y,
            },
        };

    /// <summary>And the cache lives for one turn, not for the executor: each
    /// <see cref="TriggerExecutor.Run"/> makes its own <c>TriggerRun</c>. A room dug between
    /// turns must therefore be visible on the next one, which this digs and re-asks.</summary>
    [Fact]
    public void TheRoomCacheDoesNotSurviveIntoTheNextTurn()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);

        // Zero treasuries now, so ">= 1" is false on turn 1.
        TriggerRecord[] records =
        [
            RoomCounter(5, 25, comparison: 5, RoomTypeId.Treasury, threshold: 1, child: 0xFFFF),
            Action(1, 3, 0x0A, 1),
        ];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        var executor = new TriggerExecutor(tree, world.Triggers, world, database);

        executor.Run(1);
        Assert.Equal(0, world.Triggers.Read(3));

        BuildTreasury(world, database);
        executor.Run(2);

        Assert.Equal(1, world.Triggers.Read(3));
    }

    /// <summary>A one-tile Treasury owned by the keeper, laid straight onto the map the way
    /// <see cref="RoomIndex"/> derives rooms — from the tiles, not from a room object.
    /// </summary>
    private static void BuildTreasury(WorldState world, GameDatabase database)
    {
        RoomDef treasury = database.Rooms.Values.First(r => r.RoomId == (byte)RoomTypeId.Treasury);
        ref TileState tile = ref world.Tile(3, 3);
        tile.TerrainId = treasury.TerrainId;
        tile.OwnerPlayerId = Keeper;
    }

    /// <summary>Condition type 47 asks whether this keeper has been killed, and by whom
    /// (<c>0x004C7B4B</c>). It is answered whole rather than compared: the arm returns 0 on
    /// a living keeper, 1 when the record names no player, and otherwise tests equality —
    /// it reads no <c>Comparison</c> and no <c>Value</c>, and the record carries neither.
    /// </summary>
    [Theory]
    [InlineData(0, 0, false)]     // alive, any killer named: false
    [InlineData(0, 4, false)]     // alive, killer 4 named: still false
    [InlineData(4, 0, true)]      // killed, no killer named: true
    [InlineData(4, 4, true)]      // killed by 4, asking for 4
    [InlineData(4, 5, false)]     // killed by 4, asking for 5
    public void ConditionFortySevenAsksWhoKilledThisKeeper(
        int killer, int asked, bool holds)
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        world.Player(1).KilledByPlayerId = (byte)killer;

        TriggerRecord[] records = [KilledBy(5, asked), Action(1, 3, 0x0A, 1)];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(1, 5)]);
        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(holds ? 1 : 0, world.Triggers.Read(3));
    }

    /// <summary>And it is never blocked: a living keeper is DK2's own answer, not a gap.
    /// All 72 shipped records read false today because nothing can kill a keeper yet, which
    /// is why this retires them from the census without advancing any level (ADR-0116).
    /// </summary>
    [Fact]
    public void ConditionFortySevenIsAnsweredRatherThanDeclined()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);

        TriggerRunResult result = new TriggerExecutor(
                new TriggerTree([KilledBy(5, 3), Action(1, 3, 0x0A, 1)], 0,
                    playerRoots: [new TriggerPlayerRoot(1, 5)]),
                world.Triggers, world, database)
            .Run(1);

        Assert.DoesNotContain(result.Blocked, b => b.Type == 47);
    }

    /// <summary>Condition type 29 asks whether a keeper's spell is available
    /// (<c>0x004C83C8</c>). Like type 47 it is answered whole: the arm reads one byte and
    /// returns a bare boolean, reading no <c>Comparison</c> and no <c>Value</c>.
    /// <para><b>The state test is strictly AVAILABLE (3).</b> RESEARCHABLE (2) does not
    /// answer it true, which is where a spell differs from a creature — see
    /// <see cref="PortalRules.IsUnlocked"/>, which accepts either.</para></summary>
    [Theory]
    [InlineData(3, 7, 7, true)]      // spell 7 available, asked for 7
    [InlineData(3, 7, 8, false)]     // spell 7 available, asked for 8
    [InlineData(2, 7, 7, false)]     // RESEARCHABLE is not available
    [InlineData(1, 7, 7, false)]     // UNAVAILABLE
    [InlineData(4, 7, 7, false)]     // the fourth state is not available either
    [InlineData(3, 7, 0, true)]      // id 0 asks whether ANY spell is available
    [InlineData(2, 7, 0, false)]     // ...and only state 3 counts there too
    public void ConditionTwentyNineAsksWhetherAKeeperSpellIsAvailable(
        int state, int spellId, int asked, bool holds)
    {
        GameDatabase database = SpellDatabase(spellId, state, Keeper);
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper]);

        TriggerRecord[] records = [SpellAvailable(5, asked), Action(1, 3, 0x0A, 1)];
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(holds ? 1 : 0, world.Triggers.Read(3));
    }

    /// <summary>The table is per keeper and per kind: another player's available spell, and
    /// an available <i>creature</i> of the same id, both answer false.</summary>
    [Theory]
    [InlineData(5, 2, false)]   // kind 5 (spells) but the rival's row
    [InlineData(2, 3, false)]   // this keeper, state 3, but kind 2 (creature)
    public void ConditionTwentyNineReadsOnlyThisKeepersSpells(
        int kind, int owner, bool holds)
    {
        GameDatabase database = Dk2TerrainFixture.Database().ForLevel(Level(
            new AvailabilityEntry((ushort)kind, (ushort)owner, 7, 3)));
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper, (byte)owner]);

        var tree = new TriggerTree(
            [SpellAvailable(5, 7), Action(1, 3, 0x0A, 1)], 0,
            playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(holds ? 1 : 0, world.Triggers.Read(3));
    }

    /// <summary>And it is answered rather than declined — 80 shipped records over 14
    /// levels, the largest single block in the stall census before this (ADR-0120).
    /// </summary>
    [Fact]
    public void ConditionTwentyNineIsAnsweredRatherThanDeclined()
    {
        GameDatabase database = SpellDatabase(7, 1, Keeper);
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper]);

        TriggerRunResult result = new TriggerExecutor(
                new TriggerTree([SpellAvailable(5, 7), Action(1, 3, 0x0A, 1)], 0,
                    playerRoots: [new TriggerPlayerRoot(Keeper, 5)]),
                world.Triggers, world, database)
            .Run(1);

        Assert.DoesNotContain(result.Blocked, b => b.Type == 29);
    }

    private static GameDatabase SpellDatabase(int spellId, int state, byte owner) =>
        Dk2TerrainFixture.Database().ForLevel(Level(
            new AvailabilityEntry(5, owner, spellId, state)));

    private static GlobalVariables Level(params AvailabilityEntry[] entries)
    {
        var level = new GlobalVariables();
        level.Availabilities.AddRange(entries);
        return level;
    }

    /// <summary>A type-29 record as the reader builds one: <c>TargetId</c> at byte 01 and
    /// nothing else the arm reads.</summary>
    // ---- WIN_GAME and LOSE_GAME (ADR-0162) -------------------------------

    [Fact]
    public void WinGameInAPlayerTreeEndsThatPlayersGameAsWon()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper]);
        var tree = new TriggerTree(
            [Condition(5, repeat: 255, child: 0xFFFF, next: 0, FlagEqualsZero()), EndGameAction(1, 10)],
            0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);

        TriggerRunResult run = new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(PlayerStatus.Won, world.Player(Keeper).Status);
        Assert.Empty(run.Partial);
        Assert.Empty(run.Unhandled);
    }

    [Fact]
    public void LoseGameEndsTheGameAsLost_AndALaterWinDoesNotOverturnIt()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper]);
        var lose = new TriggerTree(
            [Condition(5, repeat: 255, child: 0xFFFF, next: 0, FlagEqualsZero()), EndGameAction(1, 11)],
            0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);
        var win = new TriggerTree(
            [Condition(5, repeat: 255, child: 0xFFFF, next: 0, FlagEqualsZero()), EndGameAction(1, 10)],
            0, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);

        new TriggerExecutor(lose, world.Triggers, world, database).Run(1);
        new TriggerExecutor(win, world.Triggers, world, database).Run(2);

        Assert.Equal(PlayerStatus.Lost, world.Player(Keeper).Status);
    }

    /// <summary>The world action dispatcher's jump table sends types 10 and 11 to its
    /// failure exit, so a WIN_GAME authored in a world tree ends nothing — DK2's answer,
    /// not a decline (trigger-semantics.md, "What an arm returns").</summary>
    [Fact]
    public void WinGameInAWorldTreeIsRefusedAsDk2Refuses()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper]);
        var tree = new TriggerTree(
            [Condition(5, repeat: 255, child: 0xFFFF, next: 0, FlagEqualsZero()), EndGameAction(1, 10)], 5);

        TriggerRunResult run = new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(PlayerStatus.Playing, world.Player(Keeper).Status);
        Assert.Contains(run.Unhandled, b => b.Type == 10);
    }

    private static TriggerRecord EndGameAction(ushort id, byte type) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = type,
            Parameters = new Dictionary<string, long>(),
        };

    private static TriggerRecord SpellAvailable(ushort id, int targetId, ushort child = 0xFFFF) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 29, RepeatCount = 1,
            ChildTriggerId = child, NextTriggerId = 0,
            Parameters = new Dictionary<string, long> { ["TargetId"] = targetId },
        };

    /// <summary>A type-47 record as the reader builds one: byte 00 only, no comparison and
    /// no value, so a helper that supplied either would not be testing this arm.</summary>
    private static TriggerRecord KilledBy(ushort id, int playerId) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 47, RepeatCount = 1,
            ChildTriggerId = 0xFFFF, NextTriggerId = 0,
            Parameters = new Dictionary<string, long> { ["PlayerId"] = playerId },
        };

    private static TriggerRecord Gold(ushort id, byte comparison, int threshold, ushort child) =>
        Counter(id, 30, comparison, threshold, child);

    private static TriggerRecord Counter(
        ushort id, byte type, byte comparison, int threshold, ushort child) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 1,
            ChildTriggerId = child, NextTriggerId = 0,
            Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = comparison, ["Value"] = threshold,
            },
        };

    private static TriggerRecord RoomCounter(
        ushort id,
        byte type,
        byte comparison,
        RoomTypeId roomType,
        int threshold,
        ushort child) => new GenericTriggerRecord(new byte[16])
    {
        Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 1,
        ChildTriggerId = child, NextTriggerId = 0,
        Parameters = new Dictionary<string, long>
        {
            ["Comparison"] = comparison, ["RoomId"] = (byte)roomType, ["Value"] = threshold,
        },
    };

    private static WorldState RoomWorld(GameDatabase database) =>
        WorldState.FromMap(database, TestWorld.Map(10, 8), [(byte)0, (byte)1, (byte)2]);

    private static void PlaceRoom(
        WorldState world,
        GameDatabase database,
        byte owner,
        RoomTypeId type,
        params TilePos[] tiles)
    {
        byte terrainId = database.Room(type).TerrainId;
        foreach (TilePos at in tiles)
        {
            ref TileState tile = ref world.Tile(at.X, at.Y);
            tile.TerrainId = terrainId;
            tile.OwnerPlayerId = owner;
            tile.Health = database.TerrainById(terrainId).StartingHealth;
        }
    }

    private static bool RoomConditionHolds(
        WorldState world,
        GameDatabase database,
        byte type,
        byte playerId,
        RoomTypeId roomType,
        byte comparison,
        int threshold)
    {
        TriggerRecord[] records =
        [
            RoomCounter(5, type, comparison, roomType, threshold, child: 0xFFFF),
            Action(1, 3, 0x0A, 1),
        ];
        new TriggerExecutor(
            new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(playerId, 5)]),
            world.Triggers, world, database).Run(1);
        return world.Triggers.Read(3) == 1;
    }

    /// <summary>Eight player condition types are answered <b>false</b>, not declined:
    /// their jump-table entry is the dispatcher's own zero-return epilogue, so DK2 reads
    /// nothing and returns 0. The walk is unchanged either way — what changes is that 163
    /// shipped records stop being reported as work nobody has done (ADR-0109).</summary>
    [Theory]
    [InlineData((byte)48)]
    [InlineData((byte)53)]
    [InlineData((byte)56)]
    [InlineData((byte)61)]
    public void AConstantFalsePlayerConditionIsAnsweredNotDeclined(byte type)
    {
        TriggerRecord[] records = [Typed(5, type, child: 0xFFFF), Action(1, 3, 0x0A, 1)];

        var state = new TriggerState();
        TriggerRunResult run = new TriggerExecutor(
            new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(3, 5)]), state).Run(1);

        Assert.Equal(0, state.Read(3));
        Assert.Empty(run.Blocked);

        // And only in a player tree: the world dispatcher has no arm for these at all.
        TriggerRunResult world =
            new TriggerExecutor(new TriggerTree(records, 5), new TriggerState()).Run(1);
        Assert.Equal([new BlockedTrigger(213, type, 5, TriggerContext.World)], world.Blocked);
    }

    /// <summary>An action nobody has implemented must descend anyway when DK2's dispatcher
    /// succeeds at it: the return value is decided by the jump table, not by the arm. Type
    /// 5 is the degenerate case — its arm <i>is</i> the success exit.</summary>
    /// <remarks>Type 17 is here for a different reason than the rest, and the distinction is
    /// worth keeping: its arm (<c>0x00519257</c>) has been <b>read</b>. It walks the action
    /// point's rectangle and sets or clears one bit of each slab's flag word, and every
    /// reader found for that bit paints the overhead map while its notifications enter a
    /// sink the engine runs correctly without. So there is no simulation effect to miss, and
    /// 291 records across 40 levels stay Partial by decision rather than by backlog
    /// (ADR-0144). What the bit means is still Q309.</remarks>
    [Theory]
    [InlineData((byte)5)]
    [InlineData((byte)17)]
    [InlineData((byte)51)]
    public void AnActionDk2SucceedsAtDescendsAndCountsAsPartial(byte type)
    {
        var state = new TriggerState();
        TriggerRunResult result = new TriggerExecutor(
                new TriggerTree([Typed214(1, type, child: 0xFFFE), Action(2, 3, 0x0A, 1)], 0xFFFF),
                state)
            .Run(1);

        Assert.Equal(1, state.Read(3));
        Assert.Equal(new BlockedTrigger(214, type, 0xFFFF, TriggerContext.World), Assert.Single(result.Partial));
        Assert.Empty(result.Unhandled);
    }

    /// <summary>Action 23 writes the level time limit (<c>0x0051916F</c>), not a timer slot; action
    /// 47 writes <c>SpecialInfoNextRoomTypeId</c> (<c>0x005097B1</c>). Neither is partial.</summary>
    [Fact]
    public void ActionTwentyThreeSetsTheLevelTimeLimit_AndFortySevenTheNextRoomInfo()
    {
        var state = new TriggerState();
        TriggerRecord limit = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = 23,
            Parameters = new Dictionary<string, long> { ["TimerId"] = TriggerState.SpecialTimerId, ["Value"] = 300 },
        };
        TriggerRecord info = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFE, Type = 47,
            Parameters = new Dictionary<string, long> { ["TargetId"] = 9 },
        };

        TriggerRunResult first = new TriggerExecutor(new TriggerTree([limit], 0xFFFF), state).Run(1);
        TriggerRunResult second = new TriggerExecutor(new TriggerTree([info], 0xFFFE), state).Run(2);

        Assert.Equal(300, state.LevelTimeLimit);
        Assert.Equal(9, state.SpecialInfoNextRoomTypeId);
        Assert.Empty(first.Partial);
        Assert.Empty(second.Partial);
    }

    /// <summary>The per-turn update counts the limit down by one and sets the time-up bit at zero
    /// (<c>0x0050A64E</c>); a limit of zero is left alone.</summary>
    [Fact]
    public void TheLevelTimeLimitCountsDownOncePerTurn_AndIsUpAtZero()
    {
        GameSimulation sim = TestWorld.Simulation();
        sim.World.Triggers.LevelTimeLimit = 3;

        sim.Run(2);
        Assert.Equal(1, sim.World.Triggers.LevelTimeLimit);
        Assert.False(sim.World.Triggers.LevelTimeUp);

        sim.Run(1);
        Assert.Equal(0, sim.World.Triggers.LevelTimeLimit);
        Assert.True(sim.World.Triggers.LevelTimeUp);

        sim.Run(1);
        Assert.Equal(0, sim.World.Triggers.LevelTimeLimit);
    }

    [Theory]
    [InlineData((byte)44)]
    [InlineData((byte)45)]
    public void WorldOptionActionsUseOnlyLiteralOneAsEnabled(byte type)
    {
        var state = new TriggerState();

        TriggerRunResult disabled = new TriggerExecutor(
            new TriggerTree([WorldOptionAction(1, type, 2)], 0xFFFF), state).Run(1);

        Assert.False(type == 44 ? state.CreatureAngerEnabled : state.NotificationsEnabled);
        Assert.Empty(disabled.Partial);

        TriggerRunResult enabled = new TriggerExecutor(
            new TriggerTree([WorldOptionAction(2, type, 1)], 0xFFFE), state).Run(2);

        Assert.True(type == 44 ? state.CreatureAngerEnabled : state.NotificationsEnabled);
        Assert.Empty(enabled.Partial);
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    [InlineData((byte)5)]
    public void ActionSixChangesAnExistingAvailabilityRow(byte kind)
    {
        GameDatabase database = TestWorld.Database().ForLevel(Level(
            new AvailabilityEntry(kind, Keeper, 7, 1)));
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper]);

        TriggerRunResult enabled = new TriggerExecutor(
            new TriggerTree([AvailabilityAction(1, Keeper, kind, 7, available: 2)], 0xFFFF),
            world.Triggers, world, database).Run(1);

        Assert.Equal(3, world.Availability(Keeper, (AvailabilityKind)kind, 7));
        Assert.Empty(enabled.Partial);

        new TriggerExecutor(
            new TriggerTree([AvailabilityAction(2, Keeper, kind, 7, available: 0)], 0xFFFE),
            world.Triggers, world, database).Run(2);

        Assert.Equal(1, world.Availability(Keeper, (AvailabilityKind)kind, 7));
    }

    [Fact]
    public void ActionSixDoesNotCreateAMissingAvailabilityRow()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper]);

        new TriggerExecutor(
            new TriggerTree([AvailabilityAction(1, Keeper, 2, 7, available: 1)], 0xFFFF),
            world.Triggers, world, database).Run(1);

        Assert.Empty(world.Player(Keeper).Availabilities);
        Assert.False(PortalRules.IsUnlocked(world, Keeper, 7));
    }

    [Fact]
    public void ActionSixFeedsSpellAvailabilityConditionsLaterInTheRun()
    {
        GameDatabase database = TestWorld.Database().ForLevel(Level(
            new AvailabilityEntry(5, Keeper, 7, 1)));
        WorldState world = WorldState.FromMap(database, TestWorld.Map(), [Keeper]);
        TriggerRecord[] records =
        [
            AvailabilityAction(1, Keeper, 5, 7, available: 1),
            SpellAvailable(5, 7, child: 0xFFFE),
            Action(2, 3, 0x0A, 1),
        ];
        var tree = new TriggerTree(
            records, 0xFFFF, playerRoots: [new TriggerPlayerRoot(Keeper, 5)]);

        new TriggerExecutor(tree, world.Triggers, world, database).Run(1);

        Assert.Equal(1, world.Triggers.Read(3));
    }

    [Fact]
    public void DisabledWorldAngerOptionRefusesEveryAngerDelta()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        CreatureDef definition = database.CreatureById(Dk2TerrainFixture.GoblinCreatureId);
        CreatureState creature = world.SpawnCreature(
            database, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));

        new TriggerExecutor(
            new TriggerTree([WorldOptionAction(1, 44, 0)], 0xFFFF),
            world.Triggers, world, database).Run(1);

        Assert.Equal(0, CreatureAngerRules.Apply(
            world, definition, creature, AngerBucket.Treatment, 3000, AngerComplaint.Slapped));
        Assert.Equal(0, creature.Anger.Treatment);
    }

    /// <summary>And an action DK2 itself refuses must not: its dispatcher prints
    /// <c>Unhandled Trigger When</c> and returns zero, so the subtree is dead script in the
    /// retail game too. Counted apart from <c>Blocked</c>, which is our own gaps.</summary>
    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)26)]
    [InlineData((byte)43)]
    [InlineData((byte)52)]
    public void AnActionDk2RefusesStopsTheWalkAndCountsAsUnhandled(byte type)
    {
        var state = new TriggerState();
        TriggerRunResult result = new TriggerExecutor(
                new TriggerTree([Typed214(1, type, child: 0xFFFE), Action(2, 3, 0x0A, 1)], 0xFFFF),
                state)
            .Run(1);

        Assert.Equal(0, state.Read(3));
        Assert.Equal(new BlockedTrigger(214, type, 0xFFFF, TriggerContext.World), Assert.Single(result.Unhandled));
        Assert.Empty(result.Partial);
        Assert.Empty(result.Blocked);
    }

    /// <summary>The thirteen action types the player dispatcher owns reach its own
    /// <c>MOV EAX,1</c>; the world dispatcher has no arm for any of them. So the same
    /// record descends in a player tree and stops in the world tree — the mirror image of
    /// condition type 72.</summary>
    [Fact]
    public void ActionType24DescendsInAPlayerTreeAndIsRefusedInTheWorldTree()
    {
        TriggerRecord[] records = [Typed214(1, 24, child: 0xFFFE), Action(2, 3, 0x0A, 1)];

        var player = new TriggerState();
        TriggerRunResult playerRun = new TriggerExecutor(
            new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(3, 0xFFFF)]), player).Run(1);

        var world = new TriggerState();
        TriggerRunResult worldRun =
            new TriggerExecutor(new TriggerTree(records, 0xFFFF), world).Run(1);

        Assert.Equal(1, player.Read(3));
        Assert.Equal([new BlockedTrigger(214, 24, 0xFFFF, TriggerContext.Player)], playerRun.Partial);
        Assert.Empty(playerRun.Unhandled);

        Assert.Equal(0, world.Read(3));
        Assert.Equal([new BlockedTrigger(214, 24, 0xFFFF, TriggerContext.World)], worldRun.Unhandled);
        Assert.Empty(worldRun.Partial);
    }

    /// <summary>Disabling a condition zeros its repeat byte; disabling an action must not
    /// touch one. Both setters are one instruction apart in intent and the action's simply
    /// does not write <c>+0x0F</c> — a difference no walk reads, and the determinism hash
    /// does.</summary>
    [Fact]
    public void DisablingAnActionLeavesItsRepeatByteAlone()
    {
        ulong Disabled(byte repeat)
        {
            var state = new TriggerState();
            TriggerRecord action = new ActionTriggerRecord(new byte[16])
            {
                Body = new byte[8], TriggerId = 0xFFFF, Type = 7, RepeatCount = repeat,
                Parameters = new Dictionary<string, long>
                {
                    ["FlagId"] = 3, ["Flag"] = 0x0A, ["Value"] = 1,
                },
            };
            var executor = new TriggerExecutor(new TriggerTree([action], 0xFFFF), state);
            executor.SetActive(unchecked((short)0xFFFF), active: false);
            return StateHash(state);
        }

        Assert.NotEqual(Disabled(7), Disabled(0));
    }

    [Fact]
    public void PlayerRootsAreWalkedAndTheirSubtreesRun()
    {
        TriggerRecord[] records =
        [
            Condition(5, repeat: 255, child: 0xFFFF, next: 0, FlagEqualsZero()),
            Action(1, 3, 0x0A, 1),
        ];
        var withPlayer = new TriggerState();
        var without = new TriggerState();

        new TriggerExecutor(new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(3, 5)]), withPlayer).Run(1);
        new TriggerExecutor(new TriggerTree(records, 0), without).Run(1);

        Assert.Equal(1, withPlayer.Read(3));
        Assert.Equal(0, without.Read(3));
    }

    [Fact]
    public void CreatureRootUsesWorldConditionsAndDelegatesPlayerActionsBeforeWorldActions()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        CreatureState creature = world.SpawnCreature(
            database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(4, 4));
        creature.TriggerId = 5;

        TriggerRecord[] records =
        [
            Condition(5, repeat: 1, child: 0xFFFF, next: 0, FlagEqualsZero()),
            Typed214(1, 24, child: 0xFFFE),
            Action(2, 3, 0x0A, 1),
        ];
        TriggerRunResult run = new TriggerExecutor(
            new TriggerTree(records, 0), world.Triggers, world, database).Run(1);

        Assert.Equal(1, creature.TriggerRepeat);
        Assert.Equal(1, world.Triggers.Read(3));
        Assert.Equal(
            new BlockedTrigger(214, 24, 0xFFFF, TriggerContext.Creature),
            Assert.Single(run.Partial));
        Assert.Empty(run.Unhandled);
        Assert.Empty(run.Blocked);
    }

    /// <summary>Creature action 15 (<c>0x0049ACC1</c>) gives a Good creature its standing job and
    /// state 1; objective 22 keeps the creature's own player; a keeper's creature is left alone.
    /// None of it is partial.</summary>
    [Fact]
    public void CreatureActionFifteenSetsAHerosStandingJob()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        CreatureState hero = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, PlayerIds.Good, new TilePos(4, 4));
        CreatureState keepers = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(5, 4));
        CreatureState kept = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, PlayerIds.Good, new TilePos(6, 4));
        hero.TriggerId = 5;
        keepers.TriggerId = 9;
        kept.TriggerId = 7;
        kept.GoodJobPlayerId = 4;
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, database), hero, 90));

        TriggerRecord[] records =
        [
            Condition(5, repeat: 1, child: 0xFFFF, next: 0, FlagEqualsZero()),
            Condition(7, repeat: 1, child: 0xFFFE, next: 0, FlagEqualsZero()),
            Condition(9, repeat: 1, child: 0xFFFD, next: 0, FlagEqualsZero()),
            Objective(0xFFFF, (byte)JobType.KillPlayer, player: 3, actionPoint: 2),
            Objective(0xFFFE, 22, player: 3, actionPoint: 9),
            Objective(0xFFFD, (byte)JobType.KillPlayer, player: 3, actionPoint: 2),
        ];
        TriggerRunResult run = new TriggerExecutor(
            new TriggerTree(records, 0), world.Triggers, world, database).Run(1);

        Assert.Equal((JobType.KillPlayer, (byte)3, 2), (hero.GoodJob, hero.GoodJobPlayerId, hero.GoodJobActionPointId));
        Assert.Equal(CreatureStateMachine.Idle, hero.Dk2StateId);
        Assert.Equal((byte)0, keepers.GoodJobPlayerId);
        Assert.Equal(((JobType)22, (byte)4, 9), (kept.GoodJob, kept.GoodJobPlayerId, kept.GoodJobActionPointId));
        Assert.Empty(run.Partial);
        Assert.Equal(3, run.CreatureActions.Count);
    }

    /// <summary>Action 15 leaves a Good creature alone while it is a Combat Pit fighter (DK2's
    /// <c>+0x3CB</c>) or dying (body bit 21), and still succeeds (<c>0x0049ACE7</c>).</summary>
    [Fact]
    public void CreatureActionFifteenLeavesAPitFighterAndADyingHeroAlone()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        CreatureState fighter = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, PlayerIds.Good, new TilePos(4, 4));
        CreatureState dying = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, PlayerIds.Good, new TilePos(6, 4));
        fighter.TriggerId = 5;
        dying.TriggerId = 7;
        fighter.CombatPitFighter = true;
        dying.Dk2StateId = CreatureDeathRules.DyingState;
        Assert.True(CreatureDeathRules.IsDying(dying));

        TriggerRecord[] records =
        [
            Condition(5, repeat: 1, child: 0xFFFF, next: 0, FlagEqualsZero()),
            Condition(7, repeat: 1, child: 0xFFFE, next: 0, FlagEqualsZero()),
            Objective(0xFFFF, (byte)JobType.KillPlayer, player: 3, actionPoint: 2),
            Objective(0xFFFE, (byte)JobType.KillPlayer, player: 3, actionPoint: 2),
        ];
        TriggerRunResult run = new TriggerExecutor(
            new TriggerTree(records, 0), world.Triggers, world, database).Run(1);

        Assert.Equal(2, run.CreatureActions.Count);
        Assert.Equal((JobType.None, (byte)0), (fighter.GoodJob, fighter.GoodJobPlayerId));
        Assert.Equal((JobType.None, (byte)0), (dying.GoodJob, dying.GoodJobPlayerId));
        Assert.Equal(CreatureDeathRules.DyingState, dying.Dk2StateId);
    }

    /// <summary>Q330: the frame's entity pass (<c>0x004B7830</c>) walks list 0, the creatures, to
    /// its end before list 5, the action points, and each list newest first, so the points run in
    /// reverse THINGS order. Each root here writes flag 5 only if the one before it in DK2's order
    /// has: the creature, then the second point, then the first. Ours walked the points first,
    /// oldest first, then the creatures.</summary>
    [Fact]
    public void CreatureRootsRunBeforeActionPointRoots_AndEachClassNewestFirst()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        ThingProjection.Place(world, database,
        [
            new ActionPointThing(new byte[56]) { StartX = 2, StartY = 2, EndX = 2, EndY = 2, ActionPointId = 1, TriggerId = 20, Name = "first" },
            new ActionPointThing(new byte[56]) { StartX = 3, StartY = 3, EndX = 3, EndY = 3, ActionPointId = 2, TriggerId = 30, Name = "second" },
        ]);
        CreatureState creature = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(5, 5));
        creature.TriggerId = 10;

        TriggerRecord[] records =
        [
            Condition(10, repeat: 1, child: 0xFFFF, next: 0, FlagEquals(5, 0)), Action(1, 5, 0x0A, 1),
            Condition(30, repeat: 1, child: 0xFFFE, next: 0, FlagEquals(5, 1)), Action(2, 5, 0x0A, 3),
            Condition(20, repeat: 1, child: 0xFFFD, next: 0, FlagEquals(5, 3)), Action(3, 5, 0x0A, 2),
        ];
        new TriggerExecutor(new TriggerTree(records, 0), world.Triggers, world, database).Run(1);

        Assert.Equal(2, world.Triggers.Read(5));
    }

    private static Dictionary<string, long> FlagEquals(byte flag, int value) => new()
    {
        ["Comparison"] = 3, ["FlagId"] = flag, ["Flag"] = 1,
        ["IndirectFlagId"] = 0, ["Value"] = value,
    };

    private static TriggerRecord Objective(ushort id, byte objective, byte player, int actionPoint) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 15, RepeatCount = 0, ChildTriggerId = 0,
            Parameters = new Dictionary<string, long>
            {
                ["PlayerId"] = player, ["ObjectiveType"] = objective, ["ActionPointId"] = actionPoint,
            },
        };

    // Action 20 (give the Portal Gem) is performed since ADR-0495: PortalGemOnRealDataTests.
    [Theory]
    [InlineData((byte)43)]
    public void CreatureOwnedActionsSucceedAndRemainExplicitlyPartial(byte type)
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        CreatureState creature = world.SpawnCreature(
            database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(4, 4));
        creature.TriggerId = 5;

        TriggerRecord[] records =
        [
            Condition(5, repeat: 1, child: 0xFFFF, next: 0, FlagEqualsZero()),
            Typed214(1, type, child: 0xFFFE),
            Action(2, 3, 0x0A, 1),
        ];
        TriggerRunResult run = new TriggerExecutor(
            new TriggerTree(records, 0), world.Triggers, world, database).Run(1);

        Assert.Equal(1, world.Triggers.Read(3));
        Assert.Equal(
            new BlockedTrigger(214, type, 0xFFFF, TriggerContext.Creature),
            Assert.Single(run.Partial));
        Assert.Empty(run.Unhandled);
    }

    [Fact]
    public void CreatureStatusConditionsAreAnsweredByTheCreatureDispatcher()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        CreatureState creature = world.SpawnCreature(
            database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(4, 4));
        creature.TriggerId = 5;
        creature.Health = 0;

        TriggerRecord[] records =
        [
            Typed(5, 36, child: 0xFFFF),
            Action(1, 3, 0x0A, 1),
        ];
        TriggerRunResult run = new TriggerExecutor(
            new TriggerTree(records, 0), world.Triggers, world, database).Run(1);

        Assert.Equal(1, world.Triggers.Read(3));
        Assert.Empty(run.Blocked);
    }

    [Fact]
    public void CreatureEventConditionIsFalseRatherThanDeclinedOnThePerTurnPath()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        CreatureState creature = world.SpawnCreature(
            database, Dk2TerrainFixture.ImpCreatureId, 3, new TilePos(4, 4));
        creature.TriggerId = 5;

        TriggerRecord[] records = [Typed(5, 6, child: 0xFFFF), Action(1, 3, 0x0A, 1)];
        TriggerRunResult run = new TriggerExecutor(
            new TriggerTree(records, 0), world.Triggers, world, database).Run(1);

        Assert.Equal(0, world.Triggers.Read(3));
        Assert.Empty(run.Blocked);
    }

    /// <summary>The only condition type whose meaning this executor changes with context.
    /// The player dispatcher owns 72 and computes it; the world dispatcher does not have
    /// it at all, so the same record holds in one tree and is declined in the other.
    /// </summary>
    [Fact]
    public void ConditionType72HoldsInAPlayerTreeAndIsDeclinedInTheWorldTree()
    {
        TriggerRecord[] records = [Type72(5, child: 0xFFFF), Action(1, 3, 0x0A, 1)];

        var player = new TriggerState();
        TriggerRunResult playerRun = new TriggerExecutor(
            new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(3, 5)]), player).Run(1);

        var world = new TriggerState();
        TriggerRunResult worldRun =
            new TriggerExecutor(new TriggerTree(records, 5), world).Run(1);

        Assert.Equal(1, player.Read(3));
        Assert.Equal([new BlockedTrigger(213, 72, 5, TriggerContext.Player)], playerRun.Assumed);
        Assert.Empty(playerRun.Blocked);

        Assert.Equal(0, world.Read(3));
        Assert.Equal([new BlockedTrigger(213, 72, 5, TriggerContext.World)], worldRun.Blocked);
        Assert.Empty(worldRun.Assumed);
    }

    /// <summary>Reusing the world evaluator inside a player tree is only correct because
    /// the player dispatcher delegates these types. If someone implements a type the
    /// player context owns, this fails and says so.</summary>
    [Theory]
    [InlineData(213, (byte)1)]
    [InlineData(213, (byte)2)]
    [InlineData(213, (byte)34)]
    [InlineData(214, (byte)7)]
    [InlineData(214, (byte)6)]
    [InlineData(214, (byte)8)]
    [InlineData(214, (byte)14)]
    [InlineData(214, (byte)44)]
    [InlineData(214, (byte)45)]
    public void EveryTypeWeImplementIsOneThePlayerDispatcherDelegates(int tag, byte type)
    {
        // A record of that type in a player tree must reach the same code as in a world
        // tree: it must not be diverted, and it must not be declined for being diverted.
        TriggerRecord[] records = tag == 213
            ? [Typed(5, type, child: 0xFFFF), Action(1, 3, 0x0A, 1)]
            : [Condition(5, 255, child: 0xFFFF, next: 0, FlagEqualsZero()), Typed214(1, type)];

        TriggerRunResult run = new TriggerExecutor(
            new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(3, 5)]), new TriggerState()).Run(1);

        Assert.DoesNotContain(run.Assumed, b => b.Tag == tag && b.Type == type);
        Assert.DoesNotContain(run.Blocked, b => b.Tag == tag && b.Type == type);
    }

    private static TriggerRecord Type72(ushort id, ushort child) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 72, RepeatCount = 1,
            ChildTriggerId = child, NextTriggerId = 0,
            Parameters = new Dictionary<string, long>(),
        };

    private static TriggerRecord Typed(ushort id, byte type, ushort child) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 1,
            ChildTriggerId = child, NextTriggerId = 0,
            Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
                ["IndirectFlagId"] = 0, ["TimerId"] = 0, ["IndirectTimerId"] = 0, ["Value"] = 0,
            },
        };

    private static TriggerRecord Typed214(ushort id, byte type, ushort child = 0) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = type,
            RepeatCount = 0, ChildTriggerId = child,
            Parameters = new Dictionary<string, long>
            {
                ["FlagId"] = 3, ["Flag"] = 0x0A, ["Value"] = 1, ["TimerId"] = 0,
                ["PartyId"] = 0, ["PartyType"] = 0, ["ActionPointId"] = 0,
                ["Available"] = 1,
            },
        };

    private static TriggerRecord WorldOptionAction(ushort id, byte type, byte available) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = type,
            Parameters = new Dictionary<string, long> { ["Available"] = available },
        };

    private static TriggerRecord AvailabilityAction(
        ushort id, byte playerId, byte kind, byte typeId, byte available) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 6,
            Parameters = new Dictionary<string, long>
            {
                ["PlayerId"] = playerId, ["AvailabilityKind"] = kind,
                ["TypeId"] = typeId, ["Available"] = available,
            },
        };

    // A party root (5) whose subtree writes flag 3, a spare member subtree (7) linked from
    // nowhere else, and the type-14 action that arms them both.
    private static TriggerRecord[] PartyRecords() =>
    [
        Condition(5, repeat: 255, child: 0xFFFF, next: 0, FlagEqualsZero()),
        Condition(7, repeat: 255, child: 0, next: 0, FlagEqualsZero()),
        Action(1, 3, 0x0A, 1),
        PartyAction(2, partyId: 0),
    ];

    private static HeroPartyThing Party(
        byte partyId, ushort triggerId, params (ushort Trigger, byte Creature)[] members) =>
        new(new byte[555])
        {
            Name = string.Empty, TriggerId = triggerId, PartyId = partyId,
            Members = members.Select(m => new GoodCreatureThing(new byte[32])
            {
                TriggerId = m.Trigger, CreatureId = m.Creature,
            }).ToArray(),
        };

    private static TriggerRecord PartyAction(ushort id, byte partyId) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 14,
            RepeatCount = 0,
            Parameters = new Dictionary<string, long>
            {
                ["PartyId"] = partyId, ["PartyType"] = 0, ["ActionPointId"] = 0,
            },
        };

    private static TriggerRecord LevelTime(ushort id, byte comparison, int seconds, ushort child) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 34, RepeatCount = 1,
            ChildTriggerId = child, NextTriggerId = 0,
            Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = comparison, ["Value"] = seconds,
            },
        };

    private static TriggerRecord Action(
        ushort id, byte flagId, byte mode, int value, ushort child = 0, ushort next = 0) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 7,
            RepeatCount = 0, ChildTriggerId = child, NextTriggerId = next,
            Parameters = new Dictionary<string, long>
            {
                ["FlagId"] = flagId, ["Flag"] = mode, ["Value"] = value,
            },
        };

    private static TriggerRecord Timer(ushort id, byte timerId) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = unchecked((ushort)-(short)id), Type = 8,
            Parameters = new Dictionary<string, long> { ["TimerId"] = timerId },
        };

    private static TriggerRecord Condition(
        ushort id, byte repeat, ushort child, ushort next, Dictionary<string, long> parameters) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = parameters.Count == 0 ? (byte)72 : (byte)1,
            RepeatCount = repeat, ChildTriggerId = child, NextTriggerId = next, Parameters = parameters,
        };

    private static Dictionary<string, long> FlagEqualsZero() => new()
    {
        ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
        ["IndirectFlagId"] = 0, ["Value"] = 0,
    };

    private static ulong StateHash(TriggerState state)
    {
        DK2.Core.Hash64 hash = DK2.Core.Hash64.Start();
        state.HashInto(ref hash);
        return hash.Value;
    }
}
