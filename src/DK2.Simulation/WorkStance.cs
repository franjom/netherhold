using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation;

/// <summary>
/// Where a worker stands to do a job: <i>on</i> the tile it is working, or on a
/// tile next to it.
///
/// <para><b>Why this exists.</b> Until now the project had one answer for
/// everything — an imp stood on an orthogonal neighbour and reached across —
/// which is necessarily right for a wall and is <b>wrong for floor</b>. The
/// owner's observation of the original (Q44, 2026-08-29) is unambiguous: <i>"imps
/// claim tiles by jumping on them"</i>, they stay on the tile roughly half a
/// second after it is claimed, and then move to an adjacent unclaimed one and do
/// it again. That is a different stance, and it changes more than animation —
/// it moves the ownership condition, and it is what makes claimed territory
/// creep tile by tile the way the original's does.</para>
///
/// <para><b>The rule is derived, not enumerated.</b> A worker works from the tile
/// itself when it can stand there <i>both while it works and when the job
/// finishes</i>; otherwise it works from a neighbour. Two clauses, and each one
/// is doing real work:</para>
/// <list type="bullet">
/// <item><b>Standable while working</b> rules out every wall job without naming
/// one. Rock, gold seams and reinforced walls are <see cref="TerrainFlags.Solid"/>,
/// nothing may stand on them, so digging, mining and reinforcing are worked from
/// a neighbour exactly as before. It also asks the grid rather than the terrain
/// table, so a creature that cannot cross water does not plan to stand in
/// it.</item>
/// <item><b>Standable when it finishes</b> rules out <b>filling in</b>, and that
/// is the clause that earns this being a rule rather than a list. Fill-in takes
/// a walkable floor tile and turns it into rock (<see cref="TileWorkRules.CompletesUpward"/>,
/// Q18), so a worker standing on it would be entombed by its own work. There is
/// exactly one stance that does not do that.</item>
/// </list>
///
/// <para><b>Confidence, honestly split.</b> <b>CONFIRMED</b> for claiming, which
/// is what the owner watched. <b>STRONGLY INFERRED</b> for repairing and
/// attacking floor: they are the same geometry as claiming — a walkable tile
/// that stays walkable — and no observation distinguishes them (Q49).
/// <b>STRONGLY INFERRED</b> for fill-in, from the entombment argument rather than
/// from anything anybody has seen. What is deliberately <b>not</b> modelled is
/// the ~500 ms an imp lingers on a tile it has just claimed: whether that is a
/// real cooldown or the animation finishing is UNKNOWN, and the two are
/// indistinguishable from the outside (Q49).</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-tasks.md")]
public static class WorkStance
{
    /// <summary>
    /// Whether <paramref name="kind"/> is done standing on
    /// <paramref name="target"/> itself, rather than from an orthogonal
    /// neighbour of it.
    ///
    /// <para><paramref name="grid"/> decides what "standable" means, so the
    /// answer is per worker: it is the same grid the mover will route over, which
    /// is what stops a stance being chosen that the pathfinder then cannot
    /// reach.</para>
    /// </summary>
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

    /// <summary>
    /// Whether a worker at <paramref name="standing"/> is in position to apply
    /// <paramref name="kind"/> to <paramref name="target"/>.
    ///
    /// The one place the two stances are turned back into a distance test, so
    /// that a worker cannot be judged "arrived" by one rule and routed by
    /// another.
    /// </summary>
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
