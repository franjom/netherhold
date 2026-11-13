// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Tests;

/// <summary>For a test whose needs timings read from the spawn tick.</summary>
internal static class NeedClocks
{
    /// <summary>Puts a creature's needs stamps on <paramref name="tick"/>, as if its thing tag's
    /// stagger were zero; <c>SpawnStampsAreStaggeredByTheThingTag</c> covers the stagger itself.</summary>
    internal static CreatureState Unstaggered(this CreatureState creature, long tick = 0)
    {
        creature.LastAteTick = tick;
        creature.LastFoodAttemptTick = tick;
        creature.LastSleptTick = new GameTick(tick);
        creature.NextSleepTryTick = new GameTick(tick + NeedRules.RetryTurns);
        creature.NextPayTryTick = tick;
        return creature;
    }

    /// <summary>Hungry, and with its last try to eat long past (<c>+0x50</c>), as a creature that
    /// has been on the map a while: it may try at once.</summary>
    internal static CreatureState Hungry(this CreatureState creature, int need)
    {
        creature.HungerNeed = need;
        creature.LastFoodAttemptTick = -EatingRules.RetryTicks;
        return creature;
    }
}
