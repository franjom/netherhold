// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Combat;

/// <summary>A creature's spells as the fight uses them — docs/research/combat-spells.md: the three
/// slots, the cooldown gate (<c>0x0048C800</c>), and the survey's choice of attack spell (§5).
/// The friendly survey and the applicability table are docs/research/spell-friendly-arm.md
/// (§1—§8). The thing-targeting spells are not built.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-spells.md")]
public static class SpellRules
{
    /// <summary>The spell definition's <c>Flags</c> bits the fight reads (§2).</summary>
    public const uint AttackFlag = 1u;
    public const uint ThingTargetFlag = 2u;
    public const uint FriendlyFlag = 4u;
    public const uint StatusFlag = 8u;

    /// <summary>Bit 7: the two Invisibles, cast on the caster alone (spell-friendly-arm.md §8.2).</summary>
    public const uint SelfOnlyFlag = 0x80u;

    /// <summary>The <c>AlternativeShot</c> indices of the applicability table at <c>0x006E4AC8</c> (§1).</summary>
    public const byte HasteBuff = 1;
    public const byte HealBuff = 2;
    public const byte ArmourBuff = 3;
    public const byte InvulnerableBuff = 4;
    public const byte InvisibleBuff = 5;

    public const int Slots = 3;

    /// <summary>What the current action is (<see cref="CreatureState.SwingKind"/>).</summary>
    public const byte MeleeSwing = 0;
    public const byte SpellCast = 1;
    public const byte FreeAimCast = 2;

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
    public static bool IsFriendly(CreatureSpellDef spell) => (spell.Flags & FriendlyFlag) != 0;
    public static bool IsSelfOnly(CreatureSpellDef spell) => (spell.Flags & SelfOnlyFlag) != 0;

    /// <summary>A status spell without damage: melee is taken against a thing instead (§2).</summary>
    public static bool IsStatus(CreatureSpellDef spell) => (spell.Flags & StatusFlag) != 0;

    /// <summary><c>0x0048C800</c>: castable when never cast, or when the last cast plus the recharge
    /// — over the creature's work rate, <see cref="CombatRules.Scaled"/> — is strictly before now.</summary>
    public static bool CanCast(GameDatabase database, CreatureState me, CreatureDef definition, int slot, long tick)
    {
        CreatureSpellDef? spell = SpellOf(database, definition, slot);
        if (spell is null) return false;
        long stamp = me.SpellStamps[slot];
        return stamp == CreatureState.NeverUsed || stamp + CombatRules.Scaled(
            CombatRules.RechargeTicks(database, me, RechargeTicks(spell)), me, definition) < tick;
    }

    /// <summary>The survey's attack choice (§5): over the castable attack slots the creature's level
    /// allows, the one whose <c>stamp + recharge</c> is latest — fresh, the longest recharge. The
    /// slot index, or −1 with <paramref name="chosen"/> null. With <paramref name="skipStatus"/>, as
    /// against a forced thing, a status spell (flag bit 3) is passed over inside the loop. With
    /// <paramref name="whileRecharging"/> — a Support creature that has a tile to back on to
    /// (<c>0x0045CB00</c>: <c>FightStyle == 4 &amp;&amp; !0x00460C50() || 0x0048C800()</c>) — a slot
    /// counts whether or not it has recharged, so the caster keeps its spell nibble, its range and
    /// the step-back while it waits; a cornered one needs the spell ready.</summary>
    public static int SurveyAttack(GameDatabase database, CreatureState me, CreatureDef definition, long tick, out CreatureSpellDef? chosen,
        bool skipStatus = false, bool whileRecharging = false)
    {
        chosen = null;
        int best = -1;
        long bestReady = long.MinValue;
        int count = Math.Min(definition.Spells.Count, Slots);
        for (int i = 0; i < count; i++)
        {
            if (definition.Spells[i].LevelAvailable > me.Level) continue;
            if (!whileRecharging && !CanCast(database, me, definition, i, tick)) continue;
            CreatureSpellDef? spell = SpellOf(database, definition, i);
            if (spell is null || !IsAttack(spell) || (skipStatus && IsStatus(spell))) continue;
            long ready = me.SpellStamps[i] + RechargeTicks(spell);
            if (best >= 0 && ready <= bestReady) continue;
            best = i;
            bestReady = ready;
            chosen = spell;
        }
        return best;
    }

    /// <summary>Survey F's spell (spell-friendly-arm.md §8.1): over the castable slots the level
    /// allows, the <b>last</b> friendly one wins — each overwrites the saved slot. −1 when none.</summary>
    public static int SurveyFriendly(GameDatabase database, CreatureState me, CreatureDef definition, long tick, out CreatureSpellDef? chosen)
    {
        chosen = null;
        int best = -1;
        int count = Math.Min(definition.Spells.Count, Slots);
        for (int i = 0; i < count; i++)
        {
            if (definition.Spells[i].LevelAvailable > me.Level) continue;
            if (!CanCast(database, me, definition, i, tick)) continue;
            CreatureSpellDef spell = SpellOf(database, definition, i)!;
            if (!IsFriendly(spell)) continue;
            best = i;
            chosen = spell;
        }
        return best;
    }

    /// <summary>The applicability table (<c>0x006E4AC8</c>, spell-friendly-arm.md §1—§7) by the spell's
    /// <c>AlternativeShot</c>: Haste wants no speed effect; Heal wants health under half the
    /// level-scaled maximum; Armour, Invulnerable and Invisible want the Combat state category
    /// (<see cref="CombatRules.InCombat"/>, a walk to a fight included) and their own word zero.
    /// Index 0 and anything past the six records is false.</summary>
    public static bool BuffApplies(GameDatabase database, CreatureDef targetDef, CreatureState target, CreatureSpellDef spell)
    {
        switch (spell.AlternativeShot)
        {
            case HasteBuff: return target.SpeedEffectTicks == 0;
            case HealBuff: return target.Health < (CreatureHealthRules.MaxHealth(database, targetDef, target) >> 1);
            case ArmourBuff: return CombatRules.InCombat(target) && target.ArmourTicks == 0;
            case InvulnerableBuff: return CombatRules.InCombat(target) && target.InvulnerableTicks == 0;
            case InvisibleBuff: return CombatRules.InCombat(target) && target.InvisibleTicks == 0;
            default: return false;
        }
    }
}
