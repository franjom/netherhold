// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Combat;

/// <summary>A creature's spells as the fight uses them — docs/research/combat-spells.md: the three
/// slots, the cooldown gate (<c>0x0048C800</c>), and the survey's choice of attack spell (§5).
/// The haste and slow words that scale the cooldown are not modelled (Q393); the friendly and
/// thing-targeting spells are not built (Q394).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-spells.md")]
public static class SpellRules
{
    /// <summary>The spell definition's <c>Flags</c> bits the fight reads (§2).</summary>
    public const uint AttackFlag = 1u;
    public const uint ThingTargetFlag = 2u;
    public const uint FriendlyFlag = 4u;
    public const uint StatusFlag = 8u;

    public const int Slots = 3;

    /// <summary>What the current action is (<see cref="CreatureState.SwingKind"/>).</summary>
    public const byte MeleeSwing = 0;
    public const byte SpellCast = 1;

    /// <summary>The spell in a slot, or null when the slot is empty or names no definition.</summary>
    public static CreatureSpellDef? SpellOf(GameDatabase database, CreatureDef definition, int slot)
    {
        if (slot < 0 || slot >= definition.Spells.Count || slot >= Slots) return null;
        byte id = definition.Spells[slot].CreatureSpellId;
        return id != 0 && database.CreatureSpells.TryGetValue(id, out CreatureSpellDef? spell) ? spell : null;
    }

    /// <summary>The file's fixed-point seconds as the loader's ticks (§1).</summary>
    public static int RechargeTicks(CreatureSpellDef spell) => CombatRules.TicksOf(new Fp32(spell.RechargeTime));

    /// <summary>The slot's wind-up in ticks — the same multiply the loader gives it (§3).</summary>
    public static int DelayTicks(CreatureDef definition, int slot) => CombatRules.TicksOf(definition.Spells[slot].ShotDelay);

    /// <summary>The engagement's reach for a spell: <c>(range >> 12) × range</c> (§5).</summary>
    public static int RangeSquared(CreatureSpellDef spell) => (spell.Range >> 12) * spell.Range;

    public static bool IsAttack(CreatureSpellDef spell) => (spell.Flags & AttackFlag) != 0;

    /// <summary>A status spell without damage: melee is taken against a thing instead (§2).</summary>
    public static bool IsStatus(CreatureSpellDef spell) => (spell.Flags & StatusFlag) != 0;

    /// <summary><c>0x0048C800</c>: castable when never cast, or when the last cast plus the recharge
    /// is strictly before now. The divisor by the haste and slow words is 4096 here (Q393).</summary>
    public static bool CanCast(GameDatabase database, CreatureState me, CreatureDef definition, int slot, long tick)
    {
        CreatureSpellDef? spell = SpellOf(database, definition, slot);
        if (spell is null) return false;
        long stamp = me.SpellStamps[slot];
        return stamp == 0 || stamp + RechargeTicks(spell) < tick;
    }

    /// <summary>The survey's attack choice (§5): over the castable attack slots the creature's level
    /// allows, the one whose <c>stamp + recharge</c> is latest — fresh, the longest recharge. The
    /// slot index, or −1 with <paramref name="chosen"/> null.</summary>
    public static int SurveyAttack(GameDatabase database, CreatureState me, CreatureDef definition, long tick, out CreatureSpellDef? chosen)
    {
        chosen = null;
        int best = -1;
        long bestReady = long.MinValue;
        int count = Math.Min(definition.Spells.Count, Slots);
        for (int i = 0; i < count; i++)
        {
            if (definition.Spells[i].LevelAvailable > me.Level) continue;
            if (!CanCast(database, me, definition, i, tick)) continue;
            CreatureSpellDef spell = SpellOf(database, definition, i)!;
            if (!IsAttack(spell)) continue;
            long ready = me.SpellStamps[i] + RechargeTicks(spell);
            if (best >= 0 && ready <= bestReady) continue;
            best = i;
            bestReady = ready;
            chosen = spell;
        }
        return best;
    }
}
