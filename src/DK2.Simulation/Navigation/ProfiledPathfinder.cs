// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Pathfinding;

namespace DK2.Simulation.Navigation;

/// <summary>Times each grid search of the pathfinder it wraps under
/// <see cref="SimulationMarkers.GridPath"/> and <see cref="SimulationMarkers.GridNearest"/>, so a
/// profile shows how many searches a tick makes. The answers are the wrapped pathfinder's.</summary>
public sealed class ProfiledPathfinder(IPathfinder inner) : IPathfinder
{
    private readonly IPathfinder _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>Set by the owning system at the start of each tick from
    /// <see cref="SimulationContext.Profiler"/>.</summary>
    public ISimulationProfiler Profiler { get; set; } = NullSimulationProfiler.Instance;

    public bool TryFindPath(IPathGrid grid, TilePos from, TilePos to, List<TilePos> path)
    {
        using ProfileScope _ = Profiler.Scope(SimulationMarkers.GridPath);
        return _inner.TryFindPath(grid, from, to, path);
    }

    public bool TryFindNearest(IPathGrid grid, TilePos from, Func<TilePos, bool> isGoal, List<TilePos> path)
    {
        using ProfileScope _ = Profiler.Scope(SimulationMarkers.GridNearest);
        return _inner.TryFindNearest(grid, from, isGoal, path);
    }
}
