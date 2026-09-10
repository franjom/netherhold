// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Linq;
using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>Holds the recovered category records to the checks that established them. The
/// table is filled at run time by a run of stores no function contains, so it was recovered
/// by simulating registers — these assertions are what stops a re-walk drifting.</summary>
public sealed class CreatureStateCategoryTableTests
{
    [Fact]
    public void ThereAreThirtyTwoRecords()
    {
        Assert.Equal(32, CreatureStateCategoryTable.Count);
        Assert.Equal(32, CreatureStateCategoryTable.All.Length);
    }

    [Fact]
    public void EachRecordSitsAtItsOwnCategoryIndex()
    {
        for (int i = 0; i < CreatureStateCategoryTable.Count; i++)
        {
            Assert.Equal((CreatureStateCategory)i, CreatureStateCategoryTable.All[i].Category);
        }
    }

    /// <summary>Every category's bit is <c>1 &lt;&lt; index</c> — except the refused one,
    /// whose bit the program never writes. A whole-program scan finds 511 of the 512 dwords
    /// stored and that one absent, while fourteen instructions read it.</summary>
    [Fact]
    public void EveryBitIsOneShiftedExceptTheRefusedCategoryWhichHasNone()
    {
        Assert.Equal(0u, CreatureStateCategoryTable.Get(CreatureStateCategory.Refused).Bit);

        for (int i = 1; i < CreatureStateCategoryTable.Count; i++)
        {
            Assert.Equal(1u << i, CreatureStateCategoryTable.All[i].Bit);
        }
    }

    /// <summary>The consequence of that missing bit: several entry masks name the refused
    /// category, and not one of those bits can ever match, because the bit tested against
    /// them is zero. A mask bit that can never fire.</summary>
    [Fact]
    public void MasksThatNameTheRefusedCategoryCanNeverAdmitIt()
    {
        ref readonly CreatureStateCategoryDef refused =
            ref CreatureStateCategoryTable.Get(CreatureStateCategory.Refused);

        CreatureStateCategoryDef[] naming = CreatureStateCategoryTable.All.ToArray()
            .Where(d => (d.EntryMask & 1u) != 0)
            .ToArray();

        Assert.NotEmpty(naming);

        foreach (CreatureStateCategoryDef d in naming)
        {
            // The mask names it, yet the mask test cannot admit it.
            Assert.Equal(d.EntryMask == 0, d.AdmitsFrom(refused));
        }
    }

    /// <summary>A zero entry mask means unrestricted, not closed — the original admits the
    /// move outright. Five categories ship one.</summary>
    [Fact]
    public void FiveCategoriesHaveAnUnrestrictedEntryMask()
    {
        CreatureStateCategory[] unrestricted = CreatureStateCategoryTable.All.ToArray()
            .Where(d => d.EntryMask == 0)
            .Select(d => d.Category)
            .ToArray();

        Assert.Equal(
            new[]
            {
                CreatureStateCategory.Refused, CreatureStateCategory.Movement,
                CreatureStateCategory.Prison, CreatureStateCategory.Torture,
                CreatureStateCategory.CombatPit,
            },
            unrestricted);
    }

    /// <summary>Ten of the 32 carry no hook of any kind: nothing runs on entering, leaving,
    /// or each tick. A mechanic looked for in one of these categories is not there.</summary>
    [Fact]
    public void TenCategoriesHaveNoHookAtAll()
    {
        CreatureStateCategory[] hookless = CreatureStateCategoryTable.All.ToArray()
            .Where(d => !d.HasEnterHook && !d.HasUpdateHook && !d.HasLeaveHook)
            .Select(d => d.Category)
            .ToArray();

        Assert.Equal(
            new[]
            {
                CreatureStateCategory.Idle, CreatureStateCategory.Movement,
                CreatureStateCategory.Unnamed7, CreatureStateCategory.Unnamed18,
                CreatureStateCategory.Unused, CreatureStateCategory.Payday,
                CreatureStateCategory.Unnamed21, CreatureStateCategory.Unnamed27,
                CreatureStateCategory.Unnamed29, CreatureStateCategory.Incapacitated,
            },
            hookless);
    }

    /// <summary>The four categories ADR-0083 named by their hooks must still have the hook
    /// it named them by: an update hook run every tick.</summary>
    [Theory]
    [InlineData(CreatureStateCategory.Research)]
    [InlineData(CreatureStateCategory.Manufacture)]
    [InlineData(CreatureStateCategory.Training)]
    [InlineData(CreatureStateCategory.Prison)]
    [InlineData(CreatureStateCategory.Combat)]
    public void EachHookNamedCategoryStillHasItsUpdateHook(CreatureStateCategory category)
    {
        Assert.True(CreatureStateCategoryTable.Get(category).HasUpdateHook);
    }

    /// <summary>The category the state table never selects carries a record like any other,
    /// and that record has no hooks — so nothing was lost by nothing pointing at it.</summary>
    [Fact]
    public void TheUnusedCategoryHasARecordAndNoHooks()
    {
        ref readonly CreatureStateCategoryDef d =
            ref CreatureStateCategoryTable.Get(CreatureStateCategory.Unused);

        Assert.Equal(1u << 19, d.Bit);
        Assert.False(d.HasEnterHook);
        Assert.False(d.HasUpdateHook);
        Assert.False(d.HasLeaveHook);
        Assert.Empty(CreatureStateTable.InCategory(CreatureStateCategory.Unused));
    }
}
