// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// DK2's task stealing (Q155, ADR-0289): a handler that finds a task another worker holds takes
/// it when strictly nearer by the handler's measure, and sets the holder to 10 with its job
/// block kept. The holder must be a creature with a state, not in 51 or 52, and able to move
/// to 10. Distances are per axis <c>(d × d) &gt;&gt; 12</c> in 4096ths of a tile, summed.
/// Claim (<c>0x0046AB50</c>) and reinforce (<c>0x0046AEA0</c>) measure to the tile's centre;
/// the dig walk (<c>0x00469AF0</c>) is in <c>ImpTaskSystem.TryExcavationAt</c>. Not built: the
/// drop scan's dig handler (<c>0x0046A550</c>), which allows the holder <c>0x32</c> slack.
/// <para>A holder in its entry or work state is measured from its stance point, where it
/// settles; a walking one from where it is (imp-task-stealing.md §2).</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imp-task-stealing.md")]
public static class TaskStealing
{
    private const int TeleportingOut = 51;
    private const int TeleportingIn = 52;

    /// <summary>DK2's squared distance between two fixed-point positions.</summary>
    public static long DistanceSquared(long x, long y, long pointX, long pointY)
    {
        long dx = Math.Abs(pointX - x), dy = Math.Abs(pointY - y);
        return ((dx * dx) >> 12) + ((dy * dy) >> 12);
    }

    /// <summary>Whose task pool the worker <paramref name="askingCreatureId"/> searches: its
    /// keeper's, since each keeper has its own (<c>player+0x1105</c>, player-task-pool.md), so a
    /// rival's imp on the same tile neither fills a side nor can be robbed of its task (ADR-0388).
    /// <see cref="AnyPool"/> when the asker is not a live creature. Resolve once, outside a scan.</summary>
    public static int PoolOf(WorldState world, int askingCreatureId) =>
        world.CreatureById(askingCreatureId) is { } asking ? asking.OwnerPlayerId : AnyPool;

    /// <summary>No asker: every worker counts.</summary>
    public const int AnyPool = -1;

    /// <summary>Whether <paramref name="other"/>'s task is in <paramref name="pool"/> (<see cref="PoolOf"/>).</summary>
    public static bool InPool(int pool, CreatureState other) => pool == AnyPool || other.OwnerPlayerId == pool;

    /// <summary>Whether a holder can lose its task: the tests every handler makes before it
    /// measures. Nothing protects a task taken this tick: no handler reads the state stamp or
    /// the ticks in state (imp-task-stealing.md §6).</summary>
    public static bool CanLoseItsTask(CreatureState holder)
    {
        if (holder is null) throw new ArgumentNullException(nameof(holder));
        return CreatureStateMachine.HasState(holder)
            && holder.Dk2StateId is not (TeleportingOut or TeleportingIn)
            && CreatureStateMachine.Classify(holder, ImpStates.TaskSelection) != StateTransition.Refused;
    }

    /// <summary>Where the handler measures a holder from: its stance point once it is in the
    /// work's entry or work state, else its position.</summary>
    public static (long X, long Y) HolderPosition(CreatureState holder, (long X, long Y) stance)
    {
        if (holder is null) throw new ArgumentNullException(nameof(holder));
        (int entry, int work) = ImpStates.For(holder.TargetWork);
        return holder.Dk2StateId == entry || holder.Dk2StateId == work ? stance : (holder.PosX, holder.PosY);
    }

    /// <summary>The claim and reinforce rule: strictly nearer the tile's centre.</summary>
    public static bool NearerTheTile(CreatureState asking, (long X, long Y) holder, TilePos tile)
    {
        if (asking is null) throw new ArgumentNullException(nameof(asking));
        long cx = CreatureState.CentreOf(tile.X), cy = CreatureState.CentreOf(tile.Y);
        return DistanceSquared(asking.PosX, asking.PosY, cx, cy) < DistanceSquared(holder.X, holder.Y, cx, cy);
    }

    /// <summary>Where a reinforcer stands on a face (<c>0x00698AE0</c>): the middle of the edge that
    /// touches the wall, 682/4096 in. Faces as <see cref="MiningSlots.Face"/> numbers them.</summary>
    public static (long X, long Y) ReinforceStance(TilePos standing, int face)
    {
        (int ox, int oy) = face switch
        {
            0 => (2047, 682),
            1 => (682, 2047),
            2 => (3412, 2047),
            _ => (2047, 3412),
        };
        return ((standing.X * 4096L) + ox, (standing.Y * 4096L) + oy);
    }
}
