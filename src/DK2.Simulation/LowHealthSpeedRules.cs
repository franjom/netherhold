// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The low-health branch, the first of creature update's pre-handler modifiers
/// (<c>0x0048962C..0x00489705</c>, speed-set-turn.md): a non-worker whose health is under a fifth of
/// its level-scaled maximum moves at half speed, and on odd turns its three spell stamps and its
/// melee stamp move one turn later, so it recharges at about half rate. Level 5, turn 1851: Thief
/// 434 at 150 of 1500 approached at 4912 a tick, half its walk.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/speed-set-turn.md")]
public static class LowHealthSpeedRules
{
    /// <summary>The branch's test: a worker (definition flag bit 0) is skipped (<c>0x00489639</c>);
    /// otherwise <c>max × 20 / 100</c> above the health.</summary>
    public static bool IsWeak(GameDatabase database, CreatureDef definition, CreatureState creature) =>
        (definition.Flags & CreatureFlags.IsWorker) != CreatureFlags.IsWorker
        && CreatureLevelRules.MaxHealth(database, definition, creature.Level) * 20 / 100 > creature.Health;

    /// <summary>The cap halved in entity units (<c>speed × 50 / 100</c>) for a weak creature.</summary>
    public static int SpeedCap(GameDatabase database, CreatureDef definition, CreatureState creature, int cap) =>
        IsWeak(database, definition, creature) ? ((cap >> 4) * 50 / 100) << 4 : cap;

    /// <summary>The stamp half (<c>0x004896C8..0x00489705</c>): on an odd turn each of
    /// <c>+0x41B</c>, <c>+0x41F</c>, <c>+0x423</c> and <c>+0x427</c> goes up one. DK2's never-used
    /// zero becomes one, still long ready; ours keeps <see cref="CreatureState.NeverUsed"/>.</summary>
    public static void DelayRecharge(GameDatabase database, CreatureDef definition, CreatureState creature, long tick)
    {
        if ((tick & 1) == 0 || !IsWeak(database, definition, creature)) return;
        long[] stamps = creature.SpellStamps;
        for (int i = 0; i < stamps.Length; i++)
            if (stamps[i] != CreatureState.NeverUsed) stamps[i]++;
        if (creature.TimeLastUsedMelee == CreatureState.NeverUsed) return;
        creature.TimeLastUsedMelee++;
        creature.MeleeStampDelay++;
    }
}
