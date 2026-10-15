// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Linq;
using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>Holds <see cref="CreatureStateTable"/> to the checks that established it in
/// <c>docs/research/creature-state-table.md</c>. The table is generated from that note, so
/// these assertions are what stops a regenerated or hand-edited row drifting from the
/// research — an earlier reading reported 239 records from a scan window that ended before
/// the data did (ADR-0072), and every check here would have caught it.</summary>
public sealed class CreatureStateTableTests
{
    [Fact]
    public void TheTableHas281RecordsAndNotTheScanWindows239()
    {
        Assert.Equal(281, CreatureStateTable.Count);
        Assert.Equal(CreatureStateTable.Count, CreatureStateTable.All.Length);
    }

    [Fact]
    public void EveryStateIdEqualsItsRowIndex()
    {
        for (int i = 0; i < CreatureStateTable.Count; i++)
        {
            Assert.Equal(i, CreatureStateTable.Get(i).Id);
        }
    }

    [Fact]
    public void EveryCategoryIsOneOfTheThirtyTwo()
    {
        for (int i = 0; i < CreatureStateTable.Count; i++)
        {
            Assert.InRange((byte)CreatureStateTable.Get(i).Category, 0, CreatureStateTable.CategoryCount - 1);
        }
    }

    /// <summary>31 of the 32 categories carry states. The hook record DK2 ships for the
    /// remaining one is selected by nothing.</summary>
    [Fact]
    public void OnlyTheUnusedCategoryOwnsNoStates()
    {
        CreatureStateCategory[] empty = Enum.GetValues(typeof(CreatureStateCategory))
            .Cast<CreatureStateCategory>()
            .Where(c => CreatureStateTable.InCategory(c).Length == 0)
            .ToArray();

        Assert.Equal(new[] { CreatureStateCategory.Unused }, empty);
    }

    /// <summary>The utility states save the caller's state and resume it. Five of 281, and
    /// they are the movement states — which is why DK2 cannot move a creature without a
    /// resume slot.</summary>
    [Fact]
    public void TheMovementCategoryIsExactlyTheFiveUtilityStates()
    {
        Assert.Equal(new[] { 6, 7, 8, 9, 88 }, CreatureStateTable.InCategory(CreatureStateCategory.Movement));

        for (int i = 0; i < CreatureStateTable.Count; i++)
        {
            bool byCategory = CreatureStateTable.Get(i).Category == CreatureStateCategory.Movement;
            Assert.Equal(byCategory, CreatureStateTable.IsUtility(i));
        }
    }

    /// <summary>Six states carry label id 0, which is not a label: <c>Text.str</c> id 0 is
    /// an unrelated string, so these states have no display text at all.</summary>
    [Fact]
    public void SixStatesCarryNoLabelAndTheRestResolve()
    {
        int[] unlabelled = Enumerable.Range(0, CreatureStateTable.Count)
            .Where(i => CreatureStateTable.Get(i).LabelStringId == 0)
            .ToArray();

        Assert.Equal(new[] { 272, 273, 274, 275, 276, 277 }, unlabelled);

        foreach (int i in Enumerable.Range(0, CreatureStateTable.Count).Except(unlabelled))
        {
            Assert.InRange(CreatureStateTable.Get(i).LabelStringId, 1, 3087);
        }
    }

    /// <summary>Bits 5 and 7 are set by no shipped state, so their meaning can never be
    /// recovered from this table and <see cref="CreatureStateFlags"/> declares no member
    /// for them. Asserted rather than assumed.</summary>
    [Fact]
    public void TwoFlagBitsAreSetByNoState()
    {
        const byte unusedBits = 0b1010_0000;

        for (int i = 0; i < CreatureStateTable.Count; i++)
        {
            Assert.Equal(0, (byte)CreatureStateTable.Get(i).Flags & unusedBits);
        }
    }

    /// <summary>How many states carry each understood flag. <b>Bit 0's population is 195,
    /// not the 147 Q185 recorded</b> — 147 is the count of states whose flag byte is exactly
    /// 1, where six distinct values carry the bit. Bit 1's 43 was counted correctly.</summary>
    [Theory]
    [InlineData(CreatureStateFlags.Unread0, 195)]
    [InlineData(CreatureStateFlags.SuppressNeedAnger, 43)]
    [InlineData(CreatureStateFlags.Unread2, 25)]
    [InlineData(CreatureStateFlags.SuppressHungerClock, 15)]
    [InlineData(CreatureStateFlags.SuppressTirednessClock, 15)]
    [InlineData(CreatureStateFlags.NoFightLookup, 15)]
    public void EachFlagBitHasTheMeasuredPopulation(CreatureStateFlags flag, int expected)
    {
        int carrying = Enumerable.Range(0, CreatureStateTable.Count)
            .Count(i => CreatureStateTable.Get(i).Has(flag));

        Assert.Equal(expected, carrying);
    }

    /// <summary>The may-look gate's first test (<c>0x0045A71F</c>, ADR-0488): every stun refuses the
    /// fight lookup but 257, getting up.</summary>
    [Theory]
    [InlineData(209, true)]
    [InlineData(210, true)]
    [InlineData(255, true)]
    [InlineData(256, true)]
    [InlineData(263, true)]
    [InlineData(264, true)]
    [InlineData(257, false)]
    [InlineData(250, false)]
    public void TheStunsRefuseTheFightLookup_ButGettingUpDoesNot(int state, bool refuses) =>
        Assert.Equal(refuses, CreatureStateTable.Get(state).Has(CreatureStateFlags.NoFightLookup));

    /// <summary>The two clock-suppressing bits always travel together, and they mark a
    /// <b>contiguous run of ids</b> — 145 to 159, the Casino's states, which is why the room
    /// makes a creature neither hungry nor tired (ADR-0074).
    /// <para>They do <b>not</b> mark a category: the run spans three. Eight sit in
    /// <see cref="CreatureStateCategory.Casino"/>, six in the refused category and one in
    /// <see cref="CreatureStateCategory.Unnamed18"/> — so the category does not delimit the
    /// room, and a mechanic keyed on category 17 alone would miss seven of its states.</para></summary>
    [Fact]
    public void TheClockSuppressingBitsMarkAContiguousRunAcrossThreeCategories()
    {
        int[] suppressing = Enumerable.Range(0, CreatureStateTable.Count)
            .Where(i => CreatureStateTable.Get(i).Has(
                CreatureStateFlags.SuppressHungerClock | CreatureStateFlags.SuppressTirednessClock))
            .ToArray();

        Assert.Equal(Enumerable.Range(145, 15), suppressing);

        Assert.Equal(
            new[] { 145, 146, 147, 148, 149, 150, 151, 153 },
            CreatureStateTable.InCategory(CreatureStateCategory.Casino));

        int[] outside = suppressing
            .Where(i => CreatureStateTable.Get(i).Category != CreatureStateCategory.Casino)
            .ToArray();

        Assert.Equal(new[] { 152, 154, 155, 156, 157, 158, 159 }, outside);
    }

    /// <summary>Neither bit is ever set without the other.</summary>
    [Fact]
    public void TheTwoClockBitsNeverAppearAlone()
    {
        for (int i = 0; i < CreatureStateTable.Count; i++)
        {
            CreatureStateFlags f = CreatureStateTable.Get(i).Flags;
            Assert.Equal(
                f.HasFlag(CreatureStateFlags.SuppressHungerClock),
                f.HasFlag(CreatureStateFlags.SuppressTirednessClock));
        }
    }

    /// <summary>State 0 is the null state: DK2 gives it a null handler, and its category is
    /// the one the transition rule refuses as a target.</summary>
    [Fact]
    public void StateZeroIsTheNullState()
    {
        Assert.Equal(CreatureStateCategory.Refused, CreatureStateTable.Get(0).Category);
        Assert.Equal(100, CreatureStateTable.Get(0).Group);
    }

    /// <summary>The five categories named by what their hooks do (ADR-0083) must each own
    /// states, or a mechanic read against this table has nowhere to attach.</summary>
    [Theory]
    [InlineData(CreatureStateCategory.Research)]
    [InlineData(CreatureStateCategory.Manufacture)]
    [InlineData(CreatureStateCategory.Training)]
    [InlineData(CreatureStateCategory.Prison)]
    [InlineData(CreatureStateCategory.Combat)]
    public void EachHookNamedCategoryOwnsStates(CreatureStateCategory category)
    {
        Assert.NotEmpty(CreatureStateTable.InCategory(category));
    }
}
