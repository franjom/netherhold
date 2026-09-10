// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// What a declined condition <b>forecloses</b>: the records behind it the walk never
/// reached because of it. Not the same quantity as how many conditions were declined,
/// and it does not rank the same way — campaign-wide, 59 declines foreclose 153 records,
/// type 28 forecloses 18 from five declines and type 64 twelve from twelve (ADR-0123).
/// </summary>
/// <remarks>
/// <para>Records already reached by another route are subtracted, so this is a
/// difference and not an upper bound. It <i>is</i> an upper bound in one direction: a
/// condition inside the foreclosed subtree might have answered false anyway.</para>
/// <para>This measures our executor, not DK2, so it carries no <c>Dk2Behavior</c>.</para>
/// </remarks>
public static class TriggerReach
{
    /// <summary>Records foreclosed by each declined condition, largest first.</summary>
    /// <param name="tree">The tree the run walked.</param>
    /// <param name="result">A run whose <see cref="TriggerRunResult.Reached"/> is
    /// populated — the executor needs <see cref="TriggerExecutor.TrackReach"/>.</param>
    public static IReadOnlyList<TriggerGate> Gated(TriggerTree tree, TriggerRunResult result)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (result.Reached is null)
            throw new ArgumentException(
                "The run did not track reach; set TriggerExecutor.TrackReach.", nameof(result));

        var gates = new List<TriggerGate>();
        foreach (BlockedTrigger blocked in result.Blocked)
        {
            TriggerRecord? record = tree.Resolve(unchecked((short)blocked.TriggerId));
            if (record is null) continue;

            var subtree = new HashSet<ushort>();
            Collect(tree, unchecked((short)record.ChildTriggerId), subtree);
            subtree.ExceptWith(result.Reached);
            gates.Add(new TriggerGate(blocked, subtree));
        }

        return [.. gates.OrderByDescending(g => g.Records).ThenBy(g => g.Blocked.TriggerId)];
    }

    /// <summary>Every record reachable from <paramref name="id"/> by following child and
    /// sibling links, which is exactly what the walk would have visited.</summary>
    /// <remarks>The visited set is not an optimisation. A shipped tree is a graph, not a
    /// tree — ids repeat across chains — and following one without it does not
    /// terminate.</remarks>
    private static void Collect(TriggerTree tree, short id, HashSet<ushort> seen)
    {
        while (id != 0)
        {
            TriggerRecord? record = tree.Resolve(id);
            if (record is null || !seen.Add(record.TriggerId)) return;
            Collect(tree, unchecked((short)record.ChildTriggerId), seen);
            id = unchecked((short)record.NextTriggerId);
        }
    }
}

/// <summary>One declined condition and the records its decline foreclosed.</summary>
/// <remarks>The set, not just its size, because a caller summing over turns has to union
/// them: the same subtree foreclosed on three hundred turns is one cost, not three
/// hundred.</remarks>
public readonly record struct TriggerGate(
    BlockedTrigger Blocked, IReadOnlyCollection<ushort> Foreclosed)
{
    public int Records => Foreclosed.Count;
}
