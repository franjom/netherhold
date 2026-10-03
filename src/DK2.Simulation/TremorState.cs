// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The simulation fields of one original world Tremor record; its display handle belongs to presentation.</summary>
public record struct TremorState
{
    public int WorldX;
    public int WorldY;
    public uint StartedAt;
    public ushort DurationTicks;
    public ushort Radius;

    internal readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(WorldX);
        hash.Add(WorldY);
        hash.Add(StartedAt);
        hash.Add(DurationTicks);
        hash.Add(Radius);
    }
}
