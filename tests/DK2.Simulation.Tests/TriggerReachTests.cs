// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// What a declined condition forecloses, which is <b>not</b> how many conditions were
/// declined. Campaign-wide the two differ by a factor of three and rank differently:
/// type 64 declines twelve records and forecloses twelve, type 28 declines five and
/// forecloses eighteen.
/// </summary>
public class TriggerReachTests
{
    /// <summary>The whole subtree under a declined condition is foreclosed, not just its
    /// immediate child: the walk would have descended all of it.</summary>
    [Fact]
    public void ADeclineForeclosesEveryDescendantOfItsChild()
    {
        // 5 (declined) -> child 6 -> child 7, sibling 8. Record 9 is a sibling of 5 and
        // runs regardless, so it is not part of the cost.
        TriggerRecord[] records =
        [
            Declined(5, child: 6, next: 9),
            Gate(6, child: 7, next: 8),
            Gate(7), Gate(8), Gate(9),
        ];

        Assert.Equal(3, GatedBy(records, 5));
    }

    /// <summary>A decline with no children costs nothing, which is the case that made the
    /// stall count misleading: twelve such declines look like twelve times the work and
    /// are worth twelve records between them.</summary>
    [Fact]
    public void ADeclineWithNoChildForeclosesNothing()
    {
        TriggerRecord[] records = [Declined(5, child: 0, next: 9), Gate(9)];

        Assert.Equal(0, GatedBy(records, 5));
    }

    /// <summary>A record the walk reached by another route is not something the decline
    /// cost. Subtracting the run's own reach is what makes this a difference rather than
    /// an upper bound.</summary>
    [Fact]
    public void RecordsReachedByAnotherRouteAreNotCounted()
    {
        // Both 5 (declined) and 9 (which runs) have child 6, and 6 descends, so the walk
        // reaches 6 and 7 through 9 — declining 5 foreclosed nothing at all.
        TriggerRecord[] records =
        [
            Declined(5, child: 6, next: 9),
            AlwaysTrue(6, child: 7), Gate(7),
            AlwaysTrue(9, child: 6),
        ];

        Assert.Equal(0, GatedBy(records, 5));
    }

    /// <summary>A shipped tree is a graph: ids repeat across chains, and a subtree walk
    /// that does not remember where it has been runs forever rather than answering.
    /// </summary>
    [Fact]
    public void ASubtreeThatLoopsBackIsCountedOnceAndTerminates()
    {
        TriggerRecord[] records =
        [
            Declined(5, child: 6),
            Gate(6, child: 7), Gate(7, child: 6),
        ];

        Assert.Equal(2, GatedBy(records, 5));
    }

    /// <summary>Reach tracking is off by default, and asking for the gated count without
    /// it is an error rather than a zero — a silent zero would read as "this decline is
    /// free".</summary>
    [Fact]
    public void WithoutReachTrackingTheAnswerIsRefusedRatherThanGuessed()
    {
        TriggerRecord[] records = [Declined(5, child: 6), Gate(6)];
        var tree = new TriggerTree(records, 5);
        TriggerRunResult result = new TriggerExecutor(tree, new TriggerState()).Run(1);

        Assert.Null(result.Reached);
        Assert.Throws<ArgumentException>(() => TriggerReach.Gated(tree, result));
    }

    /// <summary>And turning it on must not change what the simulation does. It records
    /// what the walk touched; it must not alter the walk.</summary>
    [Fact]
    public void TrackingReachDoesNotChangeTheStateHash()
    {
        GameSimulation quiet = TestWorld.Simulation();
        GameSimulation tracked = TestWorld.Simulation();
        tracked.TrackTriggerReach = true;

        quiet.Run(100);
        tracked.Run(100);

        Assert.Equal(quiet.StateHash(), tracked.StateHash());
    }

    /// <summary>Static reachability answers what a tree <i>contains</i> below a root, so
    /// that records a run never walked can be attributed to the root class that owns
    /// them. It descends past conditions the walk would have stopped at — that is the
    /// difference from a run.</summary>
    [Fact]
    public void ReachableDescendsPastConditionsARunWouldHaveStoppedAt()
    {
        // 6 is constant-false, so a run reaches 6 and stops. The tree still contains 7.
        TriggerRecord[] records = [Gate(5, child: 6), Gate(6, child: 7), Gate(7)];
        var tree = new TriggerTree(records, 5);

        Assert.Equal([(ushort)5, (ushort)6, (ushort)7], TriggerReach.Reachable(tree, [(ushort)5]).Order());
    }

    /// <summary>And a root of zero contributes nothing rather than resolving to the
    /// first record, which is what <see cref="TriggerTree.Resolve"/> does for an unknown
    /// non-zero id.</summary>
    [Fact]
    public void AZeroRootContributesNothing()
    {
        TriggerRecord[] records = [Gate(5, child: 6), Gate(6)];
        var tree = new TriggerTree(records, 5);

        Assert.Empty(TriggerReach.Reachable(tree, [(ushort)0]));
    }

    private static int GatedBy(TriggerRecord[] records, ushort declined)
    {
        var tree = new TriggerTree(records, records[0].TriggerId);
        var executor = new TriggerExecutor(tree, new TriggerState()) { TrackReach = true };
        TriggerRunResult result = executor.Run(1);

        return TriggerReach.Gated(tree, result).Single(g => g.Blocked.TriggerId == declined)
            .Records;
    }

    /// <summary>Type 21 ships no records and has no arm here, so the executor declines
    /// it — which is exactly what this needs.</summary>
    private static TriggerRecord Declined(ushort id, ushort child = 0, ushort next = 0) =>
        Condition(id, type: 21, child, next);

    /// <summary>Type 48 is one of DK2's own constant-false arms, so it is answered and
    /// never descended. Its children exist to be counted, not run.</summary>
    private static TriggerRecord Gate(ushort id, ushort child = 0, ushort next = 0) =>
        Condition(id, type: 48, child, next);

    /// <summary>Level time at least zero — true on every turn, so the walk descends.
    /// </summary>
    private static TriggerRecord AlwaysTrue(ushort id, ushort child = 0, ushort next = 0) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = 34, RepeatCount = 1,
            ChildTriggerId = child, NextTriggerId = next,
            Parameters = new Dictionary<string, long> { ["Comparison"] = 5, ["Value"] = 0 },
        };

    private static TriggerRecord Condition(ushort id, byte type, ushort child, ushort next) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = 1,
            ChildTriggerId = child, NextTriggerId = next,
            Parameters = new Dictionary<string, long> { ["Comparison"] = 3, ["Value"] = 0 },
        };
}
