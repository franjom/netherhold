// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The impact lookup the melee swing runs on each class arm (<c>Attack_LookupImpactPair</c>, <c>0x004998A0</c>, called
/// with the attacker as <c>this</c> at <c>0x00499072</c>, <c>0x00499168</c>, <c>0x00499194</c> and <c>0x004991C0</c>): the attacker's
/// attack type picks one of four records of the table at <c>0x0069DA38</c> and the struck thing's surface byte (1 to 7) one of its
/// seven pairs; the pair's sound is asked in the record's weapon category with the attacker's handle at the attacker's position
/// (<c>0x00499A15</c>), before the blow-of-zero test. The pair's effects (<c>0xED</c>, <c>0x139</c>) are presentation and not asked here.
/// A type outside the table or a surface outside 1 to 7 asks nothing. Docs: sound-effects-combat.md section 5.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-combat.md#5-the-swing-death-and-corpses")]
public static class WeaponImpactRules
{
    /// <summary>The record's category: type 1 <c>WEAPON_METAL</c>, 2 <c>WEAPON_WOOD</c>, 3 <c>WEAPON_FLESH</c>, <c>0xF</c> <c>WEAPON_SCYTHE</c>.</summary>
    public static string? CategoryOf(AttackType type) => type switch
    {
        AttackType.MeleeBlade => "WEAPON_METAL",
        AttackType.MeleeBlunt => "WEAPON_WOOD",
        AttackType.MeleeBody => "WEAPON_FLESH",
        AttackType.MeleeScythe => "WEAPON_SCYTHE",
        _ => null,
    };

    /// <summary>The sound of surface 1 to 7 in every record: <c>0x114</c>, <c>0x1BC</c>, <c>0x113</c>, <c>0x1BB</c>, <c>0x112</c>, <c>0x112</c>, <c>0x1BD</c>. The
    /// jump table <c>0x00499A24</c> sends surface 6 to the pair at record <c>+0x21</c> and surface 7 to the one at <c>+0x1D</c>.</summary>
    private static readonly ushort[] BySurface = [0x114, 0x1BC, 0x113, 0x1BB, 0x112, 0x112, 0x1BD];

    /// <summary>The impact on a creature (<c>0x00499072</c>, definition <c>+0x685</c>).</summary>
    public static void OnCreature(SimulationContext context, string system, CreatureState attacker, CreatureDef mine, CreatureState target)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        Request(context, system, attacker, mine.MeleeAttackType, context.Database.CreatureById(target.CreatureTypeId).ImpactKind);
    }

    /// <summary>The impact on a door (<c>0x00499168</c>, definition <c>+0xB3</c>).</summary>
    public static void OnDoor(SimulationContext context, string system, CreatureState attacker, CreatureDef mine, DoorState door)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        Request(context, system, attacker, mine.MeleeAttackType, context.Database.Doors.TryGetValue(door.DoorId, out DoorDef? def) ? def.ImpactKind : (byte)0);
    }

    /// <summary>The impact on a trap (<c>0x00499194</c>, definition <c>+0xBF</c>).</summary>
    public static void OnTrap(SimulationContext context, string system, CreatureState attacker, CreatureDef mine, TrapState trap)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        Request(context, system, attacker, mine.MeleeAttackType, context.Database.Traps.TryGetValue(trap.TrapId, out TrapDef? def) ? def.ImpactKind : (byte)0);
    }

    /// <summary>The impact on an object (<c>0x004991C0</c>, definition <c>+0x11C</c>); also the possessed act's (<c>0x00514C8B</c>).</summary>
    public static void OnObject(SimulationContext context, string system, CreatureState attacker, CreatureDef mine, byte objectTypeId)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        Request(context, system, attacker, mine.MeleeAttackType, context.Database.ObjectById(objectTypeId)?.ImpactKind ?? (byte)0);
    }

    private static void Request(SimulationContext context, string system, CreatureState attacker, AttackType type, byte surface)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        if (surface is < 1 or > 7 || CategoryOf(type) is not { } category) return;
        ThingSoundRules.Request(context, system, SoundSourceId.Of(new CreatureId(attacker.Id)), category,
            (ThingSoundEvent)BySurface[surface - 1],
            new FpPosition3(new Fp32(attacker.PosX), new Fp32(attacker.PosY), new Fp32(attacker.PosZ)));
    }
}
