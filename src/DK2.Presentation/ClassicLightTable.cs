// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Presentation;

/// <summary>Native tile-local selection; ClassicLightWindow supplies cache order and refresh stamps.</summary>
public sealed class ClassicLightTable
{
    public const int MaximumStaticSources = 12, MaximumSources = 24;
    private readonly ClassicPointLight[] _lights = new ClassicPointLight[MaximumSources];
    private readonly float[] _distancesSquared = new float[MaximumSources];
    private readonly TilePos _tile;

    public ClassicLightTable(TilePos tile) => _tile = tile;
    public TilePos Tile => _tile;
    public int StaticCount { get; private set; }
    public int Count { get; private set; }
    public ClassicLightFrame StaticFrame { get; private set; }
    public ReadOnlySpan<ClassicPointLight> Lights => _lights.AsSpan(0, Count);

    /// <summary>Replaces static candidates in encounter order and invalidates the gathered cache stamp.</summary>
    public void RebuildStatic(ReadOnlySpan<ClassicPointLight> candidates)
    {
        BeginStatic();
        AppendStatic(candidates);
    }

    internal void BeginStatic()
    {
        Count = StaticCount = 0;
        StaticFrame = default;
    }

    internal void CompleteStatic(ClassicLightFrame frame) => StaticFrame = frame;

    /// <summary>A freed neighbour cell's sources are gone: the static selection empties but keeps
    /// its stamp (<c>0x00576010</c>). The binary zeroes only the static count; zeroing Count too is
    /// safe because every Gather rebuilds the dynamic part (BeginDynamic) before the table is read.</summary>
    internal void DropStatic() => Count = StaticCount = 0;

    internal void AppendStatic(ReadOnlySpan<ClassicPointLight> candidates)
    {
        foreach (ClassicPointLight candidate in candidates)
            Consider(candidate, firstSlot: 0, capacity: MaximumStaticSources);
        StaticCount = Count;
    }

    /// <summary>Candidates use physical-column, then row, list order; retains this tile's static slots.</summary>
    public void RebuildDynamic(ReadOnlySpan<ClassicPointLight> candidates)
    {
        BeginDynamic();
        AppendDynamic(candidates);
    }

    internal void BeginDynamic() => Count = StaticCount;

    internal void AppendDynamic(ReadOnlySpan<ClassicPointLight> candidates)
    {
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
