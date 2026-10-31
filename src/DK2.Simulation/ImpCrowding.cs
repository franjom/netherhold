// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How many workers fit on one job at once, and what the ones who do not fit do.</summary>
/// <remarks>CONFIRMED by the owner (Q26/Q43/Q44, 2026-09-27): digging admits
/// three per face, reinforcing one per face, and claiming one on the tile.
/// A worker denied a slot finds other work. The binary's reinforce handler
/// uses one holder slot per side; the dig handler checks three.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imp-tasks.md")]
public static class ImpCrowding
{
    /// <summary>Digging and mining workers per face: three.</summary>
    public const int MaxWorkersPerFace = 3;

    /// <summary>One reinforcing worker per face (owner, 2026-09-27; original
    /// reinforce task uses one holder slot per side at 0x0046AEA0). Per kind: a reinforcer
    /// and a repairer each have their own slot (<see cref="SharesSlots"/>).</summary>
    public const int MaxReinforcersPerFace = 1;

    /// <summary>How many workers may work a tile they are standing on: one. CONFIRMED (owner,
    /// 2026-09-03) — "only one claiming imp jumping per unclaimed tile is allowed".</summary>
    public const int MaxWorkersOnTheTile = 1;

    /// <summary>The worker cap for a job and stance.</summary>
    public static int MaxWorkers(bool workedFromTheTile, TileWorkKind? kind = null) =>
        workedFromTheTile ? MaxWorkersOnTheTile
        : kind is TileWorkKind.Reinforce or TileWorkKind.RepairWall
            ? MaxReinforcersPerFace : MaxWorkersPerFace;

    /// <summary>Whether <paramref name="face"/> already has its full complement of workers on
    /// <paramref name="targetTileIndex"/>. A worker's face is where it will be standing when it
    /// swings — the end of its route, or the tile under it once arrived; counting the intended face
    /// is what stops four imps setting off for the same side of a block and finding the crowd on
    /// arrival. Counted per keeper: only the asker's keeper's workers share its task pool (ADR-0388,
    /// superseding ADR-0026's every-player count). <paramref name="askingCreatureId"/> is
    /// excluded so a worker re-evaluating the job it holds does not count itself out (cap: <see cref="MaxWorkers"/>).</summary>
    public static bool FaceIsFull(
        WorldState world,
        GameDatabase database,
        int targetTileIndex,
        TilePos face,
        int askingCreatureId,
        bool workedFromTheTile,
        TileWorkKind? kind = null)
    {
        int limit = MaxWorkers(workedFromTheTile, kind);
        int faceIndex = (face.Y * world.Width) + face.X;
        int count = 0;
        int pool = TaskStealing.PoolOf(world, askingCreatureId);

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (creature.Id == askingCreatureId) continue;
            if (creature.TargetTileIndex != targetTileIndex) continue;
            if (!TaskStealing.InPool(pool, creature)) continue;
            if (!SharesSlots(kind, creature.TargetWork)) continue;
            if (FaceOf(world, creature) != faceIndex) continue;
            if (!database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker))
                continue;

            if (++count >= limit) return true;
        }

        return false;
    }

    /// <summary>Whether a holder of <paramref name="held"/> takes a side slot that work of
    /// <paramref name="kind"/> would need. Reinforce and repair are two pool entries with their own
    /// slots (0x0046AEA0: <c>TaskPool_Find(6)</c>, <c>TaskPool_Find(0x10)</c>), so neither crowds the
    /// other; every other kind counts every worker on the face, as before.</summary>
    public static bool SharesSlots(TileWorkKind? kind, TileWorkKind held) =>
        kind is not (TileWorkKind.Reinforce or TileWorkKind.RepairWall)
        || held == kind;

    /// <summary>The tile a worker intends to swing from: the end of its route, or the tile it
    /// stands on when it has none. Derived rather than stored, so it cannot disagree with the route
    /// being walked — one less field to keep in step, and one less in the determinism hash.</summary>
    public static int FaceOf(WorldState world, CreatureState creature)
    {
        TilePos at = creature.IsRouted ? creature.Destination : creature.Tile;
        return (at.Y * world.Width) + at.X;
    }
}
