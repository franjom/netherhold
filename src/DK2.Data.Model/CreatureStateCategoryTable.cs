// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;

namespace DK2.Data.Model;

using Cat = CreatureStateCategory;

/// <summary>DK2's 32 state-category records: the half of the transition rule the state
/// table cannot answer, plus which of the three per-category hooks exist.
/// <para><b>Generated</b> by <c>tools/gen-creature-state-tables.py</c> from
/// <c>docs/research/state-categories.md</c> §7. Hook <i>addresses</i> are not carried:
/// nothing here calls them. An edit made here is lost on the next run.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-categories.md")]
public static class CreatureStateCategoryTable
{
    /// <summary>Records in the table, each <c>0x40</c> bytes.</summary>
    public const int Count = 32;

    //                              category        bit          entry mask   enter   update  leave  +3c
    private static readonly CreatureStateCategoryDef[] Rows =
    {
        new CreatureStateCategoryDef(Cat.Refused,       0x00000000u, 0x00000000u, false, false, true,    2),
        new CreatureStateCategoryDef(Cat.Idle,          0x00000002u, 0xFFFFFFFFu, false, false, false,   5),
        new CreatureStateCategoryDef(Cat.Movement,      0x00000004u, 0x00000000u, false, false, false,   5),
        new CreatureStateCategoryDef(Cat.WorkerJob,     0x00000008u, 0x00000003u, false, false, true,   29),
        new CreatureStateCategoryDef(Cat.Research,      0x00000010u, 0x8002383Bu, true,  true,  true,   20),
        new CreatureStateCategoryDef(Cat.Manufacture,   0x00000020u, 0x8002383Bu, true,  true,  true,   20),
        new CreatureStateCategoryDef(Cat.Lair,          0x00000040u, 0x800A3C3Bu, true,  true,  false,   6),
        new CreatureStateCategoryDef(Cat.Unnamed7,      0x00000080u, 0xFFFFFFFFu, false, false, false,   6),
        new CreatureStateCategoryDef(Cat.Eating,        0x00000100u, 0x800A3C3Bu, false, true,  false,   6),
        new CreatureStateCategoryDef(Cat.Sleeping,      0x00000200u, 0x800A3C3Bu, true,  true,  true,   12),
        new CreatureStateCategoryDef(Cat.Sulking,       0x00000400u, 0x8002383Bu, true,  true,  false,   5),
        new CreatureStateCategoryDef(Cat.GuardPost,     0x00000800u, 0x8002383Bu, true,  false, true,   22),
        new CreatureStateCategoryDef(Cat.Temple,        0x00001000u, 0x8002383Bu, true,  true,  true,   18),
        new CreatureStateCategoryDef(Cat.Training,      0x00002000u, 0x8002383Bu, true,  true,  true,   21),
        new CreatureStateCategoryDef(Cat.Prison,        0x00004000u, 0x00000000u, true,  true,  true,   16),
        new CreatureStateCategoryDef(Cat.Torture,       0x00008000u, 0x00000000u, true,  true,  true,   17),
        new CreatureStateCategoryDef(Cat.CombatPit,     0x00010000u, 0x00000000u, true,  true,  true,   21),
        new CreatureStateCategoryDef(Cat.Casino,        0x00020000u, 0x8002383Bu, true,  true,  true,   19),
        new CreatureStateCategoryDef(Cat.Unnamed18,     0x00040000u, 0x00000003u, false, false, false,   6),
        new CreatureStateCategoryDef(Cat.Unused,        0x00080000u, 0x8002383Bu, false, false, false,   5),
        new CreatureStateCategoryDef(Cat.Payday,        0x00100000u, 0x800A3C3Bu, false, false, false,   6),
        new CreatureStateCategoryDef(Cat.Unnamed21,     0x00200000u, 0x80123B7Bu, false, false, false,   6),
        new CreatureStateCategoryDef(Cat.Combat,        0x00400000u, 0xFFFFFFFFu, true,  true,  true,    4),
        new CreatureStateCategoryDef(Cat.Dying,         0x00800000u, 0xFFFFFFFFu, false, true,  false,   5),
        new CreatureStateCategoryDef(Cat.TrapDisarm,    0x01000000u, 0xFFFFFFFFu, false, false, true,    6),
        new CreatureStateCategoryDef(Cat.CallToArms,    0x02000000u, 0x801A3F7Bu, true,  false, true,    6),
        new CreatureStateCategoryDef(Cat.ReportingDanger, 0x04000000u, 0xFFFFFFFFu, false, false, true,    5),
        new CreatureStateCategoryDef(Cat.Unnamed27,     0x08000000u, 0xFFFFFFFFu, false, false, false,   5),
        new CreatureStateCategoryDef(Cat.Unnamed28,     0x10000000u, 0xFFFFFFFFu, false, true,  false,   5),
        new CreatureStateCategoryDef(Cat.Unnamed29,     0x20000000u, 0xFFFFFFFFu, false, false, false,   5),
        new CreatureStateCategoryDef(Cat.Incapacitated, 0x40000000u, 0xFFFFFFFFu, false, false, false,   5),
        new CreatureStateCategoryDef(Cat.Spectating,    0x80000000u, 0x8002383Bu, true,  true,  true,   21),
    };

    /// <summary>Every category record, in index order.</summary>
    public static ReadOnlySpan<CreatureStateCategoryDef> All => Rows;

    /// <summary>The record for one category.</summary>
    public static ref readonly CreatureStateCategoryDef Get(CreatureStateCategory category)
    {
        if ((uint)category >= (uint)Rows.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(category), category, $"DK2 has {Rows.Length} state categories.");
        }

        return ref Rows[(int)category];
    }
}
