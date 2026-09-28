// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Tests;

public class ManaPressureTests
{
    private static ManaPressureResult Step(ManaPressureState state, uint now,
        int mana = 0, int net = -7, uint count = 5) =>
        ManaPressureRules.Advance(state, now, mana, net, count, 4, 100, 10, 4);

    [Fact]
    public void FourthWarningEnablesRemovalOnTheSamePayment()
    {
        var result = Step(default, 4);
        Assert.Equal(0, result.Warning);
        Assert.Equal(44u, result.State.WarningDeadline);
        for (int warning = 1; warning <= 4; warning++)
        {
            result = Step(result.State, (uint)(4 + 40 * warning));
            Assert.Equal(warning, result.Warning);
            Assert.Equal(warning == 4, result.ScanForRemoval);
        }
        Assert.True(result.State.WarningsSuppressed);
        Assert.Equal(204u, result.State.ScanDeadline);
        Assert.False(Step(result.State, 204).ScanForRemoval);
        Assert.True(Step(result.State, 208).ScanForRemoval);
    }

    [Fact]
    public void FloorRepeatsWarningThreeAndHeldWorkersStillCount()
    {
        var ready = new ManaPressureState(40, 4);
        var result = Step(ready, 40, count: 4);
        Assert.Equal(3, result.Warning);
        Assert.False(result.ScanForRemoval);
        Assert.Equal(4, result.State.WarningStage);
        Assert.Equal(3, Step(result.State, 80, count: 3).Warning);
        Assert.Equal(4, Step(result.State, 80, count: 5).Warning);
    }

    [Fact]
    public void LateWarningsAdvanceOldDeadlineOnlyOnce()
    {
        var result = Step(new(40, 1), 200);
        Assert.Equal(1, result.Warning);
        Assert.Equal(80u, result.State.WarningDeadline);
        Assert.False(result.ScanForRemoval);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(99, 0, 44)]
    [InlineData(100, -7, 0)]
    public void WarningGateUsesManaThresholdAndUnclampedNet(int mana, int net, int deadline)
    {
        Assert.Equal((uint)deadline, Step(default, 4, mana, net).State.WarningDeadline);
    }

    [Fact]
    public void RecoveryKeepsStageAndScanDeadlineButResetsWarningCycle()
    {
        var result = Step(new(164, 5, true, true, 204), 180, mana: 100);
        Assert.False(result.State.RemovalEnabled);
        Assert.False(result.State.WarningsSuppressed);
        Assert.Equal(5, result.State.WarningStage);
        Assert.Equal(204u, result.State.ScanDeadline);
        result = Step(result.State, 184);
        Assert.Equal(1, result.State.WarningStage);
        Assert.Equal(224u, result.State.WarningDeadline);
    }

    [Fact]
    public void PositiveNetDoesNotStopAnAlreadyEnabledScanBelowThreshold()
    {
        var result = Step(new(44, 5, true, true, 100), 104, mana: 7, net: 1);
        Assert.True(result.ScanForRemoval);
        Assert.Equal(0, result.Warning);
        Assert.Equal(144u, result.State.ScanDeadline);
    }

    private static ManaRemovalCandidate Imp(int id, byte level = 1, bool held = false,
        bool idle = false, int x = 2048, int y = 2048) =>
        new(id, 1, level, true, held, idle, x, y);

    [Fact]
    public void UniqueLowestLevelCanSelectANonImp()
    {
        ManaRemovalCandidate[] creatures = [Imp(1, 2), Imp(2, 3), new(3, 5, 1, false, false, false, 0, 0)];
        Assert.Equal(3, ManaPressureRules.SelectForRemoval(creatures, 1, 0, 0));
        Assert.Equal(ManaRemovalAction.None, ManaPressureRules.Countdown(5, false, 4, 100, 0, 7, 4));
    }

    [Fact]
    public void HeldEvilWorkersCountForFloorButCannotBeSelected()
    {
        ManaRemovalCandidate[] creatures = [Imp(1, held: true), Imp(2, 2)];
        Assert.Equal(2, ManaPressureRules.SelectForRemoval(creatures, 1, 0, 0));
        Assert.Null(ManaPressureRules.SelectForRemoval(creatures, 2, 0, 0));
        creatures[0] = creatures[0] with { EvilWorker = false };
        Assert.Null(ManaPressureRules.SelectForRemoval(creatures, 1, 0, 0));
    }

    [Fact]
    public void LevelTieChoosesFirstIdleImpEvenAtHigherLevel()
    {
        ManaRemovalCandidate[] creatures = [Imp(1), Imp(2), Imp(3, 8, idle: true), Imp(4, 2, idle: true)];
        Assert.Equal(3, ManaPressureRules.SelectForRemoval(creatures, 0, 0, 0));
        creatures[2] = creatures[2] with { InHand = true };
        Assert.Equal(4, ManaPressureRules.SelectForRemoval(creatures, 0, 0, 0));
    }

    [Fact]
    public void FarthestUsesSquaredDistanceAndRetainsFirstEqualCandidate()
    {
        ManaRemovalCandidate[] creatures =
            [Imp(1, x: 2048 + 5 * 4096), Imp(2, x: 2048 + 4 * 4096, y: 2048 + 4 * 4096),
             Imp(3, x: 2048 - 4 * 4096, y: 2048 - 4 * 4096)];
        Assert.Equal(2, ManaPressureRules.SelectForRemoval(creatures, 0, 0, 0));
        Assert.Equal(3, ManaPressureRules.SelectForRemoval(creatures.Reverse().ToArray(), 0, 0, 0));
    }

    [Fact]
    public void DistanceTruncatesEachSquaredAxisBeforeAdding()
    {
        ManaRemovalCandidate[] creatures = [Imp(1, x: 2112), Imp(2, x: 2108, y: 2108)];
        Assert.Equal(1, ManaPressureRules.SelectForRemoval(creatures, 0, 0, 0));
    }

    [Fact]
    public void TiedImpsAtHeartCenterProduceNoCandidate()
    {
        Assert.Null(ManaPressureRules.SelectForRemoval([Imp(1), Imp(2)], 0, 0, 0));
        Assert.Null(ManaPressureRules.SelectForRemoval([Imp(1, held: true)], 0, 0, 0));
    }

    [Theory]
    [InlineData(1, false, 4, 23, 0, 7, ManaRemovalAction.None)]
    [InlineData(1, false, 4, 24, 0, 7, ManaRemovalAction.Remove)]
    [InlineData(1, false, 4, 24, 7, 7, ManaRemovalAction.Cancel)]
    [InlineData(1, false, 4, 24, -1, 0, ManaRemovalAction.Cancel)]
    [InlineData(1, true, 4, 100, 0, 7, ManaRemovalAction.None)]
    [InlineData(1, true, 4, 100, 7, 7, ManaRemovalAction.None)]
    [InlineData(1, false, 0, 100, 0, 7, ManaRemovalAction.None)]
    public void CountdownUsesFiveSecondsAndRecoveryWinsAtDeadline(
        byte type, bool possessed, uint start, uint now, int mana, ushort upkeep, ManaRemovalAction expected)
    {
        Assert.Equal(expected, ManaPressureRules.Countdown(type, possessed, start, now, mana, upkeep, 4));
    }
}
