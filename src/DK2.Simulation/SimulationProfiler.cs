// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>A named span of simulation work that a profiler can time. One instance per name for
/// the life of the process (<see cref="Named"/>), so a profiler can index its own arrays by
/// <see cref="Id"/> and an engine can keep one native marker per id.</summary>
public sealed class SimulationMarker
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SimulationMarker> ByName = new(StringComparer.Ordinal);
    private static readonly List<SimulationMarker> ById = [];

    private SimulationMarker(string name, int id)
    {
        Name = name;
        Id = id;
    }

    public string Name { get; }

    /// <summary>Dense, from 0, in order of first use.</summary>
    public int Id { get; }

    /// <summary>How many markers exist so far; every <see cref="Id"/> is below it.</summary>
    public static int Count
    {
        get { lock (Gate) return ById.Count; }
    }

    /// <summary>The marker for <paramref name="name"/>, made on first use. Call it once and keep
    /// the result in a field: it takes a lock.</summary>
    public static SimulationMarker Named(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        lock (Gate)
        {
            if (ByName.TryGetValue(name, out SimulationMarker? marker)) return marker;
            marker = new SimulationMarker(name, ById.Count);
            ByName.Add(name, marker);
            ById.Add(marker);
            return marker;
        }
    }

    /// <summary>The marker with <paramref name="id"/>.</summary>
    public static SimulationMarker ForId(int id)
    {
        lock (Gate) return ById[id];
    }

    public override string ToString() => Name;
}

/// <summary>The markers the simulation itself opens below system level. A system's own marker is
/// its <see cref="ISimulationSystem.Name"/>, opened by <see cref="GameSimulation.Tick(in InputFrame)"/>.</summary>
public static class SimulationMarkers
{
    public static readonly SimulationMarker Tick = SimulationMarker.Named("tick");
    public static readonly SimulationMarker Messages = SimulationMarker.Named("messages");
    public static readonly SimulationMarker Triggers = SimulationMarker.Named("triggers");

    /// <summary>One call per <see cref="Navigation.WorldSteering.Refresh"/>: re-reading every
    /// tile's navigation byte. Its call count is how many times a tick asks.</summary>
    public static readonly SimulationMarker NavRefresh = SimulationMarker.Named("nav.refresh");

    /// <summary>A full navigation-mesh build, inside <see cref="NavRefresh"/> when a byte changed.</summary>
    public static readonly SimulationMarker NavBuild = SimulationMarker.Named("nav.build");

    /// <summary>One route query against the mesh.</summary>
    public static readonly SimulationMarker NavRoute = SimulationMarker.Named("nav.route");

    /// <summary>One <see cref="Pathfinding.IPathfinder.TryFindPath"/> grid search (<see cref="Navigation.ProfiledPathfinder"/>).</summary>
    public static readonly SimulationMarker GridPath = SimulationMarker.Named("grid.path");

    /// <summary>One <see cref="Pathfinding.IPathfinder.TryFindNearest"/> grid search.</summary>
    public static readonly SimulationMarker GridNearest = SimulationMarker.Named("grid.nearest");

    /// <summary>One <see cref="Pathfinding.ReachableTiles.Flood"/>: every tile a worker can reach, found once
    /// per worker update to answer its candidate tests.</summary>
    public static readonly SimulationMarker GridFlood = SimulationMarker.Named("grid.flood");

    public static readonly SimulationMarker ImpClaimOnTheWay = SimulationMarker.Named("ImpTask.claim on the way");
    public static readonly SimulationMarker ImpWander = SimulationMarker.Named("ImpTask.wander");
    public static readonly SimulationMarker ImpCarry = SimulationMarker.Named("ImpTask.carry");
    public static readonly SimulationMarker ImpJob = SimulationMarker.Named("ImpTask.job");
}

/// <summary>
/// Times <see cref="SimulationMarker"/> spans. Observation only, under the same contract as
/// <see cref="ISimulationEventSink"/>: assigning one must not change behaviour, and
/// <c>DeterminismTests.Profiling_DoesNotAffectState</c> pins that. Spans nest and every
/// <see cref="Begin"/> is closed by an <see cref="End"/> of the same marker, innermost first.
/// </summary>
public interface ISimulationProfiler
{
    void Begin(SimulationMarker marker);

    void End(SimulationMarker marker);
}

/// <summary>The default: two empty interface calls per span.</summary>
public sealed class NullSimulationProfiler : ISimulationProfiler
{
    public static readonly NullSimulationProfiler Instance = new();

    private NullSimulationProfiler() { }

    public void Begin(SimulationMarker marker) { }

    public void End(SimulationMarker marker) { }
}

/// <summary>A span closed by <c>using</c>, so an early return or an exception still ends it.</summary>
public readonly ref struct ProfileScope
{
    private readonly ISimulationProfiler _profiler;
    private readonly SimulationMarker _marker;

    public ProfileScope(ISimulationProfiler profiler, SimulationMarker marker)
    {
        _profiler = profiler;
        _marker = marker;
        profiler.Begin(marker);
    }

    public void Dispose() => _profiler.End(_marker);
}

public static class SimulationProfilerExtensions
{
    /// <summary><c>using var _ = profiler.Scope(marker);</c></summary>
    public static ProfileScope Scope(this ISimulationProfiler profiler, SimulationMarker marker) =>
        new(profiler, marker);
}
