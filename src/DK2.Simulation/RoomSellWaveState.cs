// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>One queued rectangle sale (player <c>+0xE97</c>, <c>0x004C70A0</c>): its normalised corners and the
/// turn it was queued. The wave starts at <see cref="Max"/> and ends at <see cref="Min"/>.</summary>
public readonly record struct RoomSellWaveState(TilePos Min, TilePos Max, GameTick QueuedTick)
{
    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Min.X);
        hash.Add(Min.Y);
        hash.Add(Max.X);
        hash.Add(Max.Y);
        hash.Add(QueuedTick);
    }
}

public static class RoomSellWaveRules
{
    public const int SlotsPerPlayer = 3;

    /// <summary>This turn's diagonal of <paramref name="wave"/> (<c>0x004C7160</c>), from the maximum corner.</summary>
    public static IEnumerable<TilePos> Diagonal(RoomSellWaveState wave, GameTick now, WorldState world) =>
        RoomBuildWaveRules.Diagonal(wave.Min, wave.Max, wave.QueuedTick, now, world);
}
