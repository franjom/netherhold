// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.AI.Tests;

public class KeeperResponseQueueTests
{
    [Fact]
    public void FullQueueDefersTheMessageWithoutEvaluatingOrRerollingImprisonment()
    {
        var queue = new KeeperResponseQueue();
        for (short i = 1; i <= 3; i++)
            Assert.True(queue.TryReceive(() => new KeeperThreatResponse(i, new TilePos(i, 10))));

        Assert.False(queue.TryReceive(() => throw new InvalidOperationException("Must not reroll")));
        Assert.Equal(3, queue.Count);
        Assert.Equal(new KeeperThreatResponse(1, new TilePos(1, 10)), queue.Current);

        Assert.Equal(0, queue.Dispatch(4, _ => new KeeperResponseAttempt(false, null)));
        Assert.True(queue.TryReceive(() => new KeeperThreatResponse(4, new TilePos(4, 10))));
        for (short i = 2; i <= 4; i++)
        {
            Assert.Equal(new KeeperThreatResponse(i, new TilePos(i, 10)), queue.Current);
            queue.Dispatch(1, _ => new KeeperResponseAttempt(false, null));
        }
        Assert.Null(queue.Current);
    }

    [Fact]
    public void CancelledPositionResponseIsHandledWithoutOccupyingAQueueSlot()
    {
        var queue = new KeeperResponseQueue();
        var target = new TilePos(10, 10);
        Assert.True(queue.TryReceive(() => ThreatEvaluator.Response(target, target, 10, 11, 100, false, false)));
        Assert.Equal(0, queue.Count);
        Assert.True(queue.TryReceive(() => ThreatEvaluator.Response(target, target, 10, 11, 100, false, true)));
        Assert.Equal(new KeeperThreatResponse(0, target), queue.Current);
    }

    [Theory]
    [InlineData(10, 11)]
    [InlineData(-10, 11)]
    [InlineData(0, 1)]
    public void CompletingAnEventConsumesTheRestOfTheTickAndLeavesTheNextEvent(short residual, int threat)
    {
        var queue = new KeeperResponseQueue();
        queue.TryReceive(() => new KeeperThreatResponse(residual, new TilePos(10, 10)));
        queue.TryReceive(() => new KeeperThreatResponse(50, new TilePos(20, 20)));
        int attempts = 0;
        byte remaining = queue.Dispatch(4, response =>
        {
            attempts++;
            Assert.Equal(residual, response.RemainingThreat);
            return new KeeperResponseAttempt(true, threat);
        });
        Assert.Equal(0, remaining);
        Assert.Equal(1, attempts);
        Assert.Equal(1, queue.Count);
        Assert.Equal(new KeeperThreatResponse(50, new TilePos(20, 20)), queue.Current);
    }

    [Fact]
    public void RefusedInteractionRetriesSelectionWithoutDebitingThreat()
    {
        var queue = new KeeperResponseQueue();
        var response = new KeeperThreatResponse(25, new TilePos(10, 10));
        queue.TryReceive(() => response);
        int attempts = 0;
        Assert.Equal(0, queue.Dispatch(4, current =>
        {
            Assert.Equal(response, current);
            attempts++;
            return new KeeperResponseAttempt(true, null);
        }));
        Assert.Equal(4, attempts);
        Assert.Equal(response, queue.Current);
    }

    [Fact]
    public void OwnedTargetNoMoveCanDebitTheSameCreatureRepeatedly()
    {
        var queue = new KeeperResponseQueue();
        var target = new TilePos(10, 10);
        queue.TryReceive(() => new KeeperThreatResponse(25, target));
        var residuals = new List<short>();
        queue.Dispatch(2, response =>
        {
            residuals.Add(response.RemainingThreat);
            return new KeeperResponseAttempt(true, 10);
        });
        Assert.Equal(new short[] { 25, 15 }, residuals);
        Assert.Equal(new KeeperThreatResponse(5, target), queue.Current);
        queue.Dispatch(1, _ => new KeeperResponseAttempt(true, 10));
        Assert.Equal(0, queue.Count);
    }

    [Theory]
    [InlineData(10, 65536, 10)]
    [InlineData(-10, 65536, -10)]
    [InlineData(10, 32779, 32767)]
    [InlineData(-10, 32779, -32767)]
    public void ThreatAccountingNarrowsToASignedWordBeforeTestingCompletion(short residual, int threat, short expected)
    {
        var queue = new KeeperResponseQueue();
        queue.TryReceive(() => new KeeperThreatResponse(residual, new TilePos(10, 10)));
        queue.Dispatch(1, _ => new KeeperResponseAttempt(true, threat));
        Assert.Equal(expected, queue.Current!.Value.RemainingThreat);
    }

    [Fact]
    public void ZeroResidualStillAttemptsSelectionBeforeRetiringAfterRefusedInteraction()
    {
        var queue = new KeeperResponseQueue();
        queue.TryReceive(() => new KeeperThreatResponse(0, new TilePos(10, 10)));
        int attempts = 0;
        Assert.Equal(0, queue.Dispatch(4, _ =>
        {
            attempts++;
            return new KeeperResponseAttempt(true, null);
        }));
        Assert.Equal(1, attempts);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void ReplacementSitePersistsWhenTheAllowancePausesAnEvent()
    {
        var queue = new KeeperResponseQueue();
        queue.TryReceive(() => new KeeperThreatResponse(25, new TilePos(10, 10)));
        var replacement = new TilePos(11, 12);
        queue.Dispatch(1, _ => new KeeperResponseAttempt(true, 10, replacement));
        Assert.Equal(new KeeperThreatResponse(15, replacement), queue.Current);
    }

    [Fact]
    public void ZeroAllowanceLeavesTheHeadUntouchedAndEmptyQueuePreservesAllowance()
    {
        var queue = new KeeperResponseQueue();
        Assert.Equal(4, queue.Dispatch(4, _ => throw new InvalidOperationException()));
        var response = new KeeperThreatResponse(-25, new TilePos(10, 10));
        queue.TryReceive(() => response);
        Assert.Equal(0, queue.Dispatch(0, _ => throw new InvalidOperationException()));
        Assert.Equal(response, queue.Current);
    }
}
