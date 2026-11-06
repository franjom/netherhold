// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Threading.Tasks;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>Runs a few ticks of a second copy of the level on a worker while the level loads, so
/// the code every tick runs is compiled before the live ticks run it (ADR-0689): under Mono the
/// second and third live ticks cost 40–75 ms of first-call compilation, which the join then
/// charged to a frame. The copy is built from the loaded level data on the worker, so it shares
/// nothing with the live simulation and costs the main thread nothing.</summary>
public static class SimulationWarmup
{
    public const int DefaultTicks = 8;

    /// <summary>Completes with how long the copy took to build and tick.</summary>
    public static Task<TimeSpan> Start(Func<GameSimulation> createCopy, int ticks = DefaultTicks)
    {
        if (createCopy is null) throw new ArgumentNullException(nameof(createCopy));
        return Task.Run(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            GameSimulation copy = createCopy();
            for (int i = 0; i < ticks; i++) copy.Tick();
            return clock.Elapsed;
        });
    }
}
