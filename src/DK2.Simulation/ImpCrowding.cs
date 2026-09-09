// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How many workers fit on one job at once, and what the ones who do not fit do.</summary>
/// <remarks><para>CONFIRMED by observation (owner, 2026-08-29 and 2026-09-03, Q26/Q43/Q44): three
/// imps to a face and twelve to a block for a wall, exactly one for a tile a worker stands on to
/// claim, and an imp that cannot get onto a face finds other work rather than queueing. The twelve
/// is not a rule but the three four times — a tile has four orthogonal neighbours and work is
/// orthogonal (Q27); nothing here counts to twelve.</para>
/// <para>Both numbers are observed with no source in the data: a different category from
/// <see cref="ObservedCorrections"/>, where the file carries a value that cannot produce the
/// behaviour. The balance table has no worker crowding limit — the nearest,
/// <c>OvercrowdingJailBreakPercentagePerExtraCreature</c>, is about prisons — and the CREATURES
/// record gives a <see cref="CreatureDef.Height"/> and no width or radius. The ~500 ms pause after a claim (Q49) is the other case of this shape.</para></remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imp-tasks.md")]
public static class ImpCrowding
{
    /// <summary>How many workers may work one tile from the same neighbouring tile: three.
    /// Observed in the original; in no file.</summary>
    public const int MaxWorkersPerFace = 3;

    /// <summary>How many workers may work a tile they are standing on: one. CONFIRMED (owner,
    /// 2026-09-03) — "only one claiming imp jumping per unclaimed tile is allowed".</summary>
    public const int MaxWorkersOnTheTile = 1;

    /// <summary>The cap for a job in the given <see cref="WorkStance"/>: one worker if the job is
    /// done standing on the target, three if from a neighbour. Both numbers observed, not derived.</summary>
    public static int MaxWorkers(bool workedFromTheTile) =>
        workedFromTheTile ? MaxWorkersOnTheTile : MaxWorkersPerFace;

    /// <summary>Whether <paramref name="face"/> already has its full complement of workers on
    /// <paramref name="targetTileIndex"/>. A worker's face is where it will be standing when it
    /// swings — the end of its route, or the tile under it once arrived; counting the intended face
    /// is what stops four imps setting off for the same side of a block and finding the crowd on
    /// arrival. Counted across every player, not per keeper. <paramref name="askingCreatureId"/> is
    /// excluded so a worker re-evaluating the job it holds does not count itself out (cap: <see cref="MaxWorkers"/>).</summary>
    public static bool FaceIsFull(
        WorldState world,
        GameDatabase database,
        int targetTileIndex,
        TilePos face,
        int askingCreatureId,
        bool workedFromTheTile)
    {
        int limit = MaxWorkers(workedFromTheTile);
        int faceIndex = (face.Y * world.Width) + face.X;
        int count = 0;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (creature.Id == askingCreatureId) continue;
            if (creature.TargetTileIndex != targetTileIndex) continue;
            if (FaceOf(world, creature) != faceIndex) continue;
            if (!database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker))
                continue;

            if (++count >= limit) return true;
        }

        return false;
    }

    /// <summary>The tile a worker intends to swing from: the end of its route, or the tile it
    /// stands on when it has none. Derived rather than stored, so it cannot disagree with the route
    /// being walked — one less field to keep in step, and one less in the determinism hash.</summary>
    public static int FaceOf(WorldState world, CreatureState creature)
    {
        TilePos at = creature.Path.Count > 0 ? creature.Path[^1] : creature.Tile;
        return (at.Y * world.Width) + at.X;
    }
}
