// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public enum RoomBuildSlot : byte { First, Second, Third }

public readonly record struct RoomBuildWaveState(RoomTypeId RoomId, TilePos Press, TilePos Release,
    GameTick QueuedTick, ushort AccumulatedCost)
{
    public void HashInto(ref Hash64 hash)
    {
        hash.Add((byte)RoomId);
        hash.Add(Press.X);
        hash.Add(Press.Y);
        hash.Add(Release.X);
        hash.Add(Release.Y);
        hash.Add(QueuedTick);
        hash.Add(AccumulatedCost);
    }
}

public static class RoomBuildWaveRules
{
    public const int SlotsPerPlayer = 3;

    public static IEnumerable<TilePos> Diagonal(RoomBuildWaveState wave, GameTick now, WorldState world)
    {
        TilePos press = ClampPress(wave.Press);
        TilePos release = ClampRelease(wave.Release, world);
        long elapsedTicks = now.Value - wave.QueuedTick.Value;
        int dx = Math.Abs(press.X - release.X), dy = Math.Abs(press.Y - release.Y);
        if (elapsedTicks < 0 || elapsedTicks > dx + dy) yield break;
        int elapsed = (int)elapsedTicks;
        int sx = release.X <= press.X ? 1 : -1, sy = release.Y <= press.Y ? 1 : -1;
        for (int xOffset = Math.Max(0, elapsed - dy); xOffset <= Math.Min(elapsed, dx); xOffset++)
            yield return new TilePos(release.X + xOffset * sx, release.Y + (elapsed - xOffset) * sy);
    }

    internal static TilePos ClampPress(TilePos press) => new(Math.Max(0, press.X), Math.Max(0, press.Y));
    internal static TilePos ClampRelease(TilePos release, WorldState world) =>
        new(Math.Min(world.Width - 1, release.X), Math.Min(world.Height - 1, release.Y));
}
