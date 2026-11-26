// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DK2.GodotAdapter;

public partial class WorldScene
{
    /// <summary>The step counters the exit lines print (the lighting step, the blended re-sort, Present's allocations and
    /// the collections since the level opened) and the world materials and shaders made, as named numbers for
    /// <see cref="PerformanceReport"/>'s JSON.</summary>
    internal void AddPerformanceCounters(List<KeyValuePair<string, double>> into)
    {
        int stillLightFrames = Math.Max(0, _lightFrames - DrawWarmUp - _lightMovedFrames);
        Add(into, "lighting.frames", _lightFrames);
        Add(into, "lighting.published", _lightPublishes);
        Add(into, "lighting.stillFrames", stillLightFrames);
        Add(into, "lighting.stillMeanMs", Ms(_lightTicks) / Math.Max(1, stillLightFrames));
        Add(into, "lighting.stillMaxMs", Ms(_lightMaxTicks));
        Add(into, "lighting.stillBytes", _lightBytes);
        Add(into, "lighting.movedFrames", _lightMovedFrames);
        Add(into, "lighting.movedBytes", _lightMovedBytes);
        Add(into, "blended.resorts", _blendedRefreshes);
        Add(into, "blended.firstMs", Ms(Math.Max(0, _blendedFirstTicks)));
        Add(into, "blended.meanMs", Ms(_blendedTicks) / Math.Max(1, _blendedRefreshes - 1));
        Add(into, "blended.maxMs", Ms(_blendedMaxTicks));
        Add(into, "blended.lateBytes", _blendedLateBytes);
        Add(into, "blended.maxRuns", _blendedMaxRuns);
        Add(into, "blended.maxTriangles", _blendedMaxTriangles);
        Add(into, "blended.instances", _runSlots.Count);
        int grows = 0;
        foreach (RunSlot slot in _runSlots)
        {
            grows += slot.Grows;
        }

        Add(into, "blended.surfaceBuilds", grows);
        Add(into, "blended.uploadBytes", _blendedUploadBytes);
        Add(into, "blended.uploadTriangles", _blendedUploadTriangles);
        Add(into, "world.materials", _worldMaterialsMade);
        int shaders = 0;
        for (int state = 0; state < WorldShaderPaths.Length; state++)
        {
            if (WorldShaders[WorldLightMode, state] != null)
            {
                shaders++;
            }
        }

        Add(into, "world.shaders", shaders);
        // The blended items (step 3k, AddItemCounters) and the flames' frames (one array).
        AddItemCounters(into);
        Add(into, "effects.models", PooledEffectModels());
        Add(into, "flames.drawn", _flames?.Count ?? 0);
        Add(into, "flames.frames", _flames?.FrameCount ?? 0);
        Add(into, "present.steadyFrames", _steadyFrames);
        Add(into, "present.steadyBytes", _steadyBytes);
        Add(into, "present.changeFrames", _changeFrames);
        Add(into, "present.changeBytes", _changeBytes);
        for (int generation = 0; generation < _collectionsAtOpen.Length; generation++)
        {
            Add(into, "gcSinceOpen.gen" + generation, GC.CollectionCount(generation) - _collectionsAtOpen[generation]);
        }
    }

    private static void Add(List<KeyValuePair<string, double>> into, string key, double value) => into.Add(new(key, value));

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
