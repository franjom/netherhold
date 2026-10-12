// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Simulation;
using Unity.Profiling;

namespace DK2.Unity
{
    /// <summary>Shows the simulation's <see cref="SimulationMarker"/> spans in the Unity Profiler,
    /// each as a <see cref="ProfilerMarker"/> of the same name under the <c>DK2.</c> prefix: the
    /// systems, navigation refreshes and builds, route queries and ImpTask's phases nest inside
    /// the tick on the CPU timeline, and the Hierarchy view counts their calls. A
    /// <see cref="ProfilerMarker"/> costs next to nothing while the Profiler is not recording.</summary>
    public sealed class UnitySimulationProfiler : ISimulationProfiler
    {
        private ProfilerMarker[] _markers = new ProfilerMarker[64];
        private bool[] _made = new bool[64];

        public void Begin(SimulationMarker marker)
        {
            int id = marker.Id;
            if (id >= _made.Length)
            {
                int size = Math.Max(id + 1, _made.Length * 2);
                Array.Resize(ref _markers, size);
                Array.Resize(ref _made, size);
            }
            if (!_made[id])
            {
                _markers[id] = new ProfilerMarker(ProfilerCategory.Scripts, "DK2." + marker.Name);
                _made[id] = true;
            }
            _markers[id].Begin();
        }

        public void End(SimulationMarker marker) => _markers[marker.Id].End();
    }
}
