// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation;

/// <summary>Where a worker stands to do a job: on the tile it works, or on an orthogonal neighbour.</summary>
/// <remarks>
/// <para>Rule: from the tile itself when it can stand there both while it works and when the job
/// finishes, else from a neighbour. That excludes wall jobs (rock, seams and reinforced walls are
/// <see cref="TerrainFlags.Solid"/>) and fill-in, which turns walkable floor into rock
/// (<see cref="TileWorkRules.CompletesUpward"/>, Q18) and would entomb the worker. Standability is
/// asked of the grid, not the terrain table, so a creature that cannot cross water never plans to
/// stand in it.</para>
/// <para>CONFIRMED for claiming — imps jump onto the tile, stay ~0.5 s, then step to an adjacent
/// unclaimed one (owner, Q44); this is why claimed territory creeps tile by tile. STRONGLY
/// INFERRED for repair, floor attack and fill-in. Not implemented: the ~500 ms linger after a
/// claim — cooldown or animation is UNKNOWN (Q49).</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-tasks.md")]
public static class WorkStance
{
    /// <summary>Whether <paramref name="kind"/> is done standing on <paramref name="target"/>
    /// itself rather than from an orthogonal neighbour. <paramref name="grid"/> decides what
    /// "standable" means, so the answer is per worker and uses the same grid the mover routes
    /// over — a stance the pathfinder cannot reach is never chosen.</summary>
    public static bool IsOnTheTile(
        GameDatabase database,
        WorldState world,
        IPathGrid grid,
        TilePos target,
        TileWorkKind kind)
    {
        if (!world.Contains(target)) return false;
        if (!grid.IsWalkable(target.X, target.Y)) return false;

        ref TileState tile = ref world.Tile(target);
        byte after = TileWorkRules.OutcomeTerrainId(database, tile, kind);
        return !database.TerrainById(after).IsSolid;
    }

    /// <summary>Whether a worker at <paramref name="standing"/> is in position to apply
    /// <paramref name="kind"/> to <paramref name="target"/>. The one place the two stances become
    /// a distance test, so a worker cannot be judged "arrived" by one rule and routed by
    /// another.</summary>
    public static bool IsInPosition(
        GameDatabase database,
        WorldState world,
        IPathGrid grid,
        TilePos standing,
        TilePos target,
        TileWorkKind kind)
    {
        int distance = standing.ManhattanDistance(target);
        return IsOnTheTile(database, world, grid, target, kind) ? distance == 0 : distance == 1;
    }
}
