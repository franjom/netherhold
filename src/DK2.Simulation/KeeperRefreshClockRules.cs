// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>Player +0x92, +0x96 and +0x10FB. Ordinary startup at native turn1 initializes all three to zero.</summary>
public readonly record struct KeeperRefreshClock(GameTick LastRefreshTick, GameTick NextRefreshTick, bool Requested = false);

/// <summary>The keeper-frame tail's unsigned clock and delay draw (keeper-refresh-clock.md).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-refresh-clock.md")]
public static class KeeperRefreshClockRules
{
    public static KeeperRefreshClock Advance(KeeperRefreshClock clock, GameTick tick, SimRandom rng)
    {
        uint now = unchecked((uint)(tick.Value + 1));
        if (now < unchecked((uint)clock.NextRefreshTick.Value) && !clock.Requested) return clock;
        uint next = unchecked(now + 100u + (uint)rng.NextInt(50));
        return new KeeperRefreshClock(new GameTick(now), new GameTick(next));
    }
}
