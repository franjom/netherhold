// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>The half of DK2's transition rule that the two state bytes decide, plus proof
/// that the other half is reported rather than answered.</summary>
public sealed class CreatureStateTransitionsTests
{
    /// <summary>Sleeping states 82 and 84 share group 7 and category 9.</summary>
    [Fact]
    public void SameTagAndSameCategoryIsAllowed()
    {
        Assert.Equal(StateTransition.Allowed, CreatureStateTransitions.Classify(82, 84));
    }

    /// <summary>State 79 (group 7, category 6) to state 82 (group 7, category 9): the tags
    /// match and the categories do not, so the original refuses it.</summary>
    [Fact]
    public void SameTagAndDifferentCategoryIsRefused()
    {
        Assert.Equal(StateTransition.Refused, CreatureStateTransitions.Classify(79, 82));
    }

    /// <summary>States 2 and 3 both sit in group 2, category 0 — the category the rule
    /// refuses as a target even when the categories are equal, because the refusal is
    /// tested before the equality.</summary>
    [Fact]
    public void CategoryZeroIsRefusedEvenWhenBothSidesAreCategoryZero()
    {
        Assert.Equal(CreatureStateCategory.Refused, CreatureStateTable.Get(2).Category);
        Assert.Equal(CreatureStateCategory.Refused, CreatureStateTable.Get(3).Category);
        Assert.Equal(StateTransition.Refused, CreatureStateTransitions.Classify(2, 3));
    }

    /// <summary>Differing group tags hand the decision to the category table's own fields,
    /// which have not been read. The answer must not collapse to <c>Refused</c>.</summary>
    [Fact]
    public void DifferingTagsAreReportedNotRefused()
    {
        // State 13 is group 8; state 82 is group 7.
        Assert.NotEqual(CreatureStateTable.Get(13).Group, CreatureStateTable.Get(82).Group);
        Assert.Equal(StateTransition.NeedsCategoryTable, CreatureStateTransitions.Classify(13, 82));
    }

    /// <summary>The passthrough pair — group 3 with the dying category — is three states. A creature in group 3 can always be moved into them whatever its
    /// category, which is the rule that lets anything die: the pair is tested before the
    /// category refusal and before the equality.</summary>
    [Fact]
    public void ThePassthroughPairIsDyingAndFrozen()
    {
        foreach (int id in new[] { 216, 217, 218 })
        {
            Assert.Equal(CreatureStateTransitions.PassthroughGroup, CreatureStateTable.Get(id).Group);
            Assert.Equal(CreatureStateTransitions.PassthroughCategory, CreatureStateTable.Get(id).Category);
        }

        // State 207 is group 3, category 0 — a category the rule would otherwise refuse.
        Assert.Equal(CreatureStateTransitions.PassthroughGroup, CreatureStateTable.Get(207).Group);
        Assert.Equal(StateTransition.Allowed, CreatureStateTransitions.Classify(207, 216));
    }

    /// <summary>A creature in a utility state is compared by the state it saved. Utility
    /// states are the five movement states.</summary>
    [Fact]
    public void AUtilityStateIsComparedByTheStateItSaved()
    {
        Assert.True(CreatureStateTable.IsUtility(6));
        Assert.Equal(82, CreatureStateTransitions.ResolveComparisonState(6, 82));
    }

    [Fact]
    public void ANonUtilityStateIsComparedByItself()
    {
        Assert.False(CreatureStateTable.IsUtility(82));
        Assert.Equal(82, CreatureStateTransitions.ResolveComparisonState(82, 13));
    }

    /// <summary>How much of the rule the two state bytes actually settle, over every ordered
    /// pair of the 281 states. These are measured, not targets: they are here so that
    /// dumping the category table shows up as a change instead of passing unnoticed, and so
    /// that a wrong edit to the test order moves a number.</summary>
    [Fact]
    public void TheTwoBytesSettleAQuarterOfAllOrderedPairs()
    {
        int allowed = 0;
        int refused = 0;
        int needsTable = 0;

        for (int from = 0; from < CreatureStateTable.Count; from++)
        {
            for (int to = 0; to < CreatureStateTable.Count; to++)
            {
                if (from == to)
                {
                    continue;
                }

                switch (CreatureStateTransitions.Classify(from, to))
                {
                    case StateTransition.Allowed: allowed++; break;
                    case StateTransition.Refused: refused++; break;
                    default: needsTable++; break;
                }
            }
        }

        Assert.Equal(78680, allowed + refused + needsTable);
        Assert.Equal(4116, allowed);
        Assert.Equal(16512, refused);
        Assert.Equal(58052, needsTable);
    }
}
