// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Navigation;

namespace DK2.AI;

/// <summary>Shares one whole-built routing mesh across keepers' staging probes, restoring
/// the build's locate cache before each view. Unchanged routing bytes reuse the mesh across
/// ticks (ADR-0577). Views must be constructed serially; this is not thread-safe.</summary>
public sealed class KeeperStaging
{
    private WorldSteering? _steering;
    private WorldState? _world;
    private GameDatabase? _database;

    public int Builds => _steering?.Rebuilds ?? 0;

    internal WorldSteering SteeringFor(GameDatabase database, WorldState world)
    {
        if (_steering is null || !ReferenceEquals(world, _world) || !ReferenceEquals(database, _database))
        {
            _steering = new WorldSteering(database, world, wholeBuilds: true);
            _world = world;
            _database = database;
        }
        _steering.Refresh();
        _steering.RestoreBuiltLocate();
        return _steering;
    }
}
