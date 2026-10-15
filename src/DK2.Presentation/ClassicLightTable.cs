// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>Native tile-local selection; caller owns cache visibility, window order and refresh stamps.</summary>
public sealed class ClassicLightTable
{
    public const int MaximumStaticSources = 12, MaximumSources = 24;
    private readonly ClassicPointLight[] _lights = new ClassicPointLight[MaximumSources];
    private readonly float[] _distancesSquared = new float[MaximumSources];
    private readonly TilePos _tile;

    public ClassicLightTable(TilePos tile) => _tile = tile;
    public int StaticCount { get; private set; }
    public int Count { get; private set; }
    public ReadOnlySpan<ClassicPointLight> Lights => _lights.AsSpan(0, Count);

    /// <summary>Candidates are visible 7x7 cache sources in row, then physical-column, list order.</summary>
    public void RebuildStatic(ReadOnlySpan<ClassicPointLight> candidates)
    {
        Count = 0;
        foreach (ClassicPointLight candidate in candidates)
            Consider(candidate, firstSlot: 0, capacity: MaximumStaticSources);
        StaticCount = Count;
    }

    /// <summary>Candidates use physical-column, then row, list order; retains this tile's static slots.</summary>
    public void RebuildDynamic(ReadOnlySpan<ClassicPointLight> candidates)
    {
        Count = StaticCount;
        foreach (ClassicPointLight candidate in candidates)
            Consider(candidate, firstSlot: StaticCount, capacity: MaximumSources);
    }

    private void Consider(ClassicPointLight candidate, int firstSlot, int capacity)
    {
        float dx = candidate.Position.X - (_tile.X + .5f);
        float dz = candidate.Position.Z + _tile.Y + .5f;
        float distanceSquared = dx * dx + dz * dz;
        float reach = candidate.Radius + 1;
        if (!(distanceSquared < reach * reach)) return;

        int slot = Count;
        if (Count == capacity)
        {
            slot = firstSlot;
            for (int index = firstSlot + 1; index < Count; index++)
                if (_distancesSquared[index] > _distancesSquared[slot]) slot = index;
            if (!(distanceSquared < _distancesSquared[slot])) return;
        }
        else Count++;
        _lights[slot] = candidate;
        _distancesSquared[slot] = distanceSquared;
    }
}
