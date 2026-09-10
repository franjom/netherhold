// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Linq;
using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>DK2's transition rule, both halves. The differing-tags half answered
/// <c>NeedsCategoryTable</c> until the 32 category records were recovered (Q300); nothing is
/// unresolved now, so a pair either goes or it does not.</summary>
public sealed class CreatureStateTransitionsTests
{
    /// <summary>No utility state is involved in most of these, so the third argument is
    /// never consulted; passing the current state makes that explicit.</summary>
    private static StateTransition Classify(int from, int to) =>
        CreatureStateTransitions.Classify(from, to, from);

    /// <summary>Sleeping states 82 and 84 share group 7 and the sleeping category.</summary>
    [Fact]
    public void SameTagAndSameCategoryIsAllowed()
    {
        Assert.Equal(StateTransition.Allowed, Classify(82, 84));
    }

    /// <summary>State 79 is group 7 / lair, state 82 group 7 / sleeping: the tags match and
    /// the categories do not, so the original refuses it.</summary>
    [Fact]
    public void SameTagAndDifferentCategoryIsRefused()
    {
        Assert.Equal(StateTransition.Refused, Classify(79, 82));
    }

    /// <summary>States 2 and 3 are both group 2 / refused — the category the rule rejects as
    /// a target even when both sides match, because the rejection is tested first.</summary>
    [Fact]
    public void TheRefusedCategoryIsRejectedEvenWhenBothSidesAreInIt()
    {
        Assert.Equal(CreatureStateCategory.Refused, CreatureStateTable.Get(2).Category);
        Assert.Equal(CreatureStateCategory.Refused, CreatureStateTable.Get(3).Category);
        Assert.Equal(StateTransition.Refused, Classify(2, 3));
    }

    /// <summary>Group 3 with the dying category is admitted from any category, which is the
    /// rule that lets a creature die out of whatever it was doing. State 207 is group 3 in
    /// the refused category, which the test below it would otherwise reject.</summary>
    [Fact]
    public void ThePassthroughAdmitsDyingFromTheRefusedCategory()
    {
        Assert.Equal(CreatureStateTransitions.PassthroughGroup, CreatureStateTable.Get(207).Group);
        Assert.Equal(CreatureStateCategory.Refused, CreatureStateTable.Get(207).Category);
        Assert.Equal(StateTransition.Allowed, Classify(207, 216));
    }

    /// <summary>"Anything dies out of anything" is nearly true, and precisely not.
    /// Inside group 3 the passthrough admits it; outside, Dying's entry mask is all ones.
    /// But the mask is tested against the <i>source</i> category's bit, and record 0's bit
    /// is permanently zero because nothing in the program writes it
    /// (`state-categories.md` §3) — so the Refused states outside group 3 can never enter
    /// Dying. This pins that consequence, which is the zero bit's only visible effect so
    /// far.</summary>
    [Fact]
    public void EveryStateCanEnterDyingExceptThoseWhoseCategoryBitIsNeverWritten()
    {
        int[] refused = Enumerable.Range(0, CreatureStateTable.Count)
            .Where(id => Classify(id, 216) != StateTransition.Allowed)
            .ToArray();

        Assert.All(refused, id => Assert.Equal(
            CreatureStateCategory.Refused, CreatureStateTable.Get(id).Category));
        Assert.All(refused, id => Assert.NotEqual(
            CreatureStateTransitions.PassthroughGroup, CreatureStateTable.Get(id).Group));
        Assert.Equal(45, refused.Length);
    }

    /// <summary>Differing tags now resolve, on the target's entry mask against the bit of
    /// the category being left.</summary>
    [Fact]
    public void DifferingTagsAreDecidedByTheTargetsEntryMask()
    {
        Assert.NotEqual(CreatureStateTable.Get(13).Group, CreatureStateTable.Get(82).Group);

        uint workerBit = CreatureStateCategoryTable.Get(CreatureStateCategory.WorkerJob).Bit;
        uint sleepMask = CreatureStateCategoryTable.Get(CreatureStateCategory.Sleeping).EntryMask;

        Assert.NotEqual(0u, sleepMask);
        Assert.NotEqual(0u, workerBit & sleepMask);
        Assert.Equal(StateTransition.Allowed, Classify(13, 82));
    }

    /// <summary>A zero entry mask admits everything, which is the opposite of the natural
    /// reading. The prison's is zero, so crossing group tags into a captive state is never
    /// refused by the mask.</summary>
    [Fact]
    public void AZeroEntryMaskAdmitsAnyCategory()
    {
        Assert.Equal(0u, CreatureStateCategoryTable.Get(CreatureStateCategory.Prison).EntryMask);
        Assert.Equal(CreatureStateCategory.Prison, CreatureStateTable.Get(114).Category);
        Assert.NotEqual(CreatureStateTable.Get(13).Group, CreatureStateTable.Get(114).Group);
        Assert.Equal(StateTransition.Allowed, Classify(13, 114));
    }

    /// <summary>A creature in a movement state is judged by where the movement is heading,
    /// not by the movement itself.</summary>
    [Fact]
    public void AMovementStateIsJudgedByItsUtilityNewState()
    {
        Assert.True(CreatureStateTable.Get(6).IsUtility);

        Assert.Equal(82, CreatureStateTransitions.ResolveComparisonState(6, 82));
        Assert.Equal(13, CreatureStateTransitions.ResolveComparisonState(13, 82));
        Assert.Equal(StateTransition.Allowed, CreatureStateTransitions.Classify(6, 82, 82));
    }

    /// <summary>Every ordered pair of distinct states now resolves. These figures are
    /// measured, not targets: they are here so that a change to the rule or to either table
    /// moves a number instead of passing unnoticed.</summary>
    [Fact]
    public void EveryOrderedPairResolvesAndTheSplitIsMeasured()
    {
        int allowed = 0;
        int refused = 0;

        for (int from = 0; from < CreatureStateTable.Count; from++)
        {
            for (int to = 0; to < CreatureStateTable.Count; to++)
            {
                if (from == to)
                {
                    continue;
                }

                if (Classify(from, to) == StateTransition.Allowed)
                {
                    allowed++;
                }
                else
                {
                    refused++;
                }
            }
        }

        Assert.Equal(78680, allowed + refused);
        Assert.Equal(37791, allowed);
        Assert.Equal(40889, refused);
    }
}
