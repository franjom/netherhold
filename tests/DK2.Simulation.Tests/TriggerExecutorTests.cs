// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
        new TriggerExecutor(new TriggerTree([Action(1, 3, mode, operand)], 0xFFFF, 2), state).Run(9);
        Assert.Equal(expected, state.Read(3));
    }

    [Fact]
    public void ClearLiteralBitReadsOperandIndirectly()
    {
        var state = new TriggerState();
        state.Write(9, FlagWriteMode.Assign, 77);
        new TriggerExecutor(new TriggerTree([Action(1, 3, 0x08, 9)], 0xFFFF, 2), state).Run(1);
        Assert.Equal(77, state.Read(3));
    }

    [Fact]
    public void FlagOperandIsTruncatedToItsLowByte()
    {
        var state = new TriggerState();
        new TriggerExecutor(new TriggerTree([Action(1, 3, 0x0A, 0x1234)], 0xFFFF, 2), state).Run(1);
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
        new TriggerExecutor(new TriggerTree([action], 0xFFFF, 2), state).Run(1);

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
            new TriggerTree([condition, parent, child], rootTriggerId: 1, rootRepeat: 2), state);

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
        new TriggerExecutor(new TriggerTree([inactive, child], 1, 2), state).Run(1);
        Assert.Equal(0, state.Read(2));
    }

    [Fact]
    public void ZeroRootIdEndsTheChain()
    {
        var state = new TriggerState();
        new TriggerExecutor(new TriggerTree([Action(1, 2, 0x12, 1)], 0, 1), state).Run(1);
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
            new TriggerTree([blocked, child, sibling, siblingChild], 1, 2), state).Run(1);

        Assert.Equal(new BlockedTrigger(213, 72, 1), Assert.Single(result.Blocked));
        Assert.Equal(0, state.Read(3));
        Assert.Equal(1, state.Read(4));
    }

    [Fact]
    public void TimerActionStampsTheCurrentTurn()
    {
        var state = new TriggerState();
        new TriggerExecutor(new TriggerTree([Timer(1, 5)], 0xFFFF, 2), state).Run(27);
        Assert.Equal(27, state.ReadTimer(5));
    }

    [Fact]
    public void TickRunsTriggersAndEmitsTheProbeHeaderAndOneRow()
    {
        GameDatabase db = TestWorld.Database();
        var tree = new TriggerTree([Action(1, 3, 0x0A, 7)], 0xFFFF, 2);
        var sim = new GameSimulation(db, TestWorld.World(db), 1, triggers: tree);
        var output = new StringWriter();
        sim.EmitTriggerTurnsTo(output);

        sim.Tick();

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(TriggerTurnCsvEmitter.Header, lines[0]);
        Assert.Equal(150, lines[0].Split(',').Length);
        Assert.Equal(150, lines[1].Split(',').Length);
        Assert.Equal("", lines[1].Split(',')[0]);
        Assert.Equal("0", lines[1].Split(',')[1]);
        Assert.Equal(7, sim.World.Triggers.Read(3));
    }

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
}
