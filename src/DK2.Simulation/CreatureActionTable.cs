// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The sound word of each record of DK2's creature action table (81 records of 29 bytes at <c>0x006E4198</c>, word at
/// <c>+25</c>), which <c>CreatureAction_Begin</c> (<c>0x00496700</c>, <c>0x004968BC</c>) asks of the creature's category when
/// the action begins and its animation is not already part way through. The filler <c>0x004950C0</c> stores them, so they are
/// read from its stores and not from a file: 44 records carry one. Index 21 is excluded by the runner and has one
/// (<c>0xDB</c>) the hover test skips. Ours has no action layer: each system that begins an action asks for the word of the
/// index it mirrors (<see cref="CreatureActionSounds"/>). docs/research/sound-effects-continuous.md section 2.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-continuous.md#2-the-action-table")]
public static class CreatureActionTable
{
    public const int Count = 81;

    /// <summary>Action 1, the imp's dig (<c>0x00496E00</c>).</summary>
    public const int Dig = 1;
    /// <summary>Action 2, the imp's mine (<c>0x004971D0</c>).</summary>
    public const int Mine = 2;
    /// <summary>Action 8, the imp's wall repair, begun by the repair state <c>0x00466710</c> (<c>0x004667C0</c>).</summary>
    public const int RepairWall = 8;
    /// <summary>Action 15, the free-aim cast of a possessed creature (<c>0x00496980(15, ...)</c>).</summary>
    public const int FreeAimCast = 15;
    /// <summary>The eating action: art 13 for a cycle, a null handler (eating-action.md).</summary>
    public const int Eat = 26;
    /// <summary>Action 16, the spell cast.</summary>
    public const int SpellCast = 16;
    /// <summary>Action 17, the melee swing.</summary>
    public const int MeleeSwing = 17;
    /// <summary>Action 30, a trainee's swing at the dummy (<c>0x00485692</c>, <c>0x00485794</c>).</summary>
    public const int TrainMelee = 30;
    /// <summary>Action 31, a trainee's spell at the target (<c>0x0048571E</c>).</summary>
    public const int TrainSpell = 31;

    private static readonly ushort[] Words =
    [
        0x00, 0x334, 0x109, 0x00, 0x00, 0xE5, 0xE5, 0x00, 0x10C,   // 0-8
        0x109, 0x00, 0xD7, 0x00, 0x00, 0xD8, 0xD8, 0xD8, 0xD7,   // 9-17
        0x00, 0xC5, 0xDB, 0xDB, 0x00, 0xE6, 0xCE, 0xDE, 0xCD,   // 18-26
        0xD4, 0xE0, 0xD1, 0xD3, 0xD3, 0xD2, 0xCA, 0xCC, 0xCA,   // 27-35
        0x00, 0x00, 0x00, 0xE1, 0xD7, 0xD0, 0xD0, 0x00, 0x00,   // 36-44
        0xE6, 0xE6, 0xE6, 0xE6, 0x00, 0xD4, 0xD1, 0x00, 0x00,   // 45-53
        0x00, 0x00, 0x00, 0x00, 0x12D, 0x00, 0x00, 0x00, 0x00,   // 54-62
        0xCF, 0x00, 0xD1, 0xCA, 0xDE, 0x00, 0xCA, 0x00, 0x00,   // 63-71
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xD7,   // 72-80
    ];

    /// <summary>The word of <paramref name="action"/>'s record, 0 for none or an index outside the table.</summary>
    public static ushort SoundWord(int action) => (uint)action < Count ? Words[action] : (ushort)0;
}

/// <summary>The sound <c>CreatureAction_Begin</c> asks when an action begins (<c>0x004968BC</c>): the record's word, in the creature's
/// category, with its handle, at its position. DK2 asks it only when the creature's animation is not part way through a clip
/// that was kept (a clip installed fresh, or one that has wrapped, qualifies); each caller marks which of those it mirrors.</summary>
public static class CreatureActionSounds
{
    public static void Begin(SimulationContext context, string system, CreatureState creature, int action)
    {
        ushort word = CreatureActionTable.SoundWord(action);
        if (word == 0) return;
        ThingSoundRules.Request(context, system, creature, (ThingSoundEvent)word);
    }
}
