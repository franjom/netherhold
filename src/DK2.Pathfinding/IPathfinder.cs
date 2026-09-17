// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Pathfinding;

/// <summary>
/// The grid a search runs over, as the search sees it.
///
/// Deliberately tiny, and deliberately not <c>WorldState</c>: this assembly
/// knows nothing about terrain, creatures or ownership. Whoever asks the
/// question decides what "walkable" means for the thing that is walking — an
/// imp may cross water, a dwarf may not — and answers here.
/// </summary>
public interface IPathGrid
{
    int Width { get; }
    int Height { get; }

    /// <summary>
    /// Whether a mover may stand on this tile. Must be a pure function of the
    /// world for the duration of one search; a grid that changes its answers
    /// mid-search makes the result unreproducible.
    /// </summary>
    bool IsWalkable(int x, int y);
}

/// <summary>Grid search over a dungeon. Since ADR-0157 no creature walks a route from here: DK2
/// routes over a navigation mesh (<see cref="MeshRouter"/>) and the simulation requests its routes
/// there. What remains in service is <see cref="TryFindNearest"/>, a Dijkstra that chooses the
/// nearest goal by walking distance, a labelled stand-in for DK2's kind lists and ring scans
/// (ADR-0058). Nothing outside this assembly may assume which of several equally short routes
/// comes back.</summary>
public interface IPathfinder
{
    /// <summary>
    /// Shortest route from <paramref name="from"/> to <paramref name="to"/>.
    ///
    /// On success <paramref name="path"/> holds the tiles to walk through,
    /// excluding the starting tile and including the destination — so a mover
    /// already standing on the destination gets an empty path and <c>true</c>.
    /// </summary>
    bool TryFindPath(IPathGrid grid, TilePos from, TilePos to, List<TilePos> path);

    /// <summary>
    /// Shortest route to the closest tile that satisfies
    /// <paramref name="isGoal"/>, measured in steps rather than in straight-line
    /// distance. Answers "where is the nearest thing I can get to", which is a
    /// different question from "how do I get to that thing" and the one a
    /// creature looking for work actually asks.
    ///
    /// <paramref name="isGoal"/> is also evaluated on <paramref name="from"/>,
    /// and must be pure: it is called an unspecified number of times in an
    /// unspecified order.
    /// </summary>
    bool TryFindNearest(IPathGrid grid, TilePos from, Func<TilePos, bool> isGoal, List<TilePos> path);
}
