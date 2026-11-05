// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;

namespace DK2.Simulation.Tests;

/// <summary><see cref="CreatureActionCoverage"/> on its own: one row per action, every Built row pointing at
/// code that exists, every other row citing its evidence.</summary>
public class ActionCoverageTests
{
    [Fact]
    public void EveryOneOfThe81ActionsHasExactlyOneRow()
    {
        int[] ids = CreatureActionCoverage.All.Select(r => r.ActionId).ToArray();

        Assert.Equal(Enumerable.Range(0, CreatureActionTable.Count), ids);
        for (int id = 0; id < CreatureActionTable.Count; id++)
            Assert.Equal(id, CreatureActionCoverage.Get(id).ActionId);
    }

    [Fact]
    public void EveryBuiltRowNamesAMemberThatExists_AndAConstantNamesItsOwnAction()
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var wrong = new List<string>();
        foreach (ActionCoverageRow row in CreatureActionCoverage.All.Where(r => r.Status == ActionStatus.Built))
        {
            MemberInfo[] members = row.Beginner!.GetMember(row.Detail, any);
            if (members.Length == 0)
            {
                wrong.Add($"{row.ActionId}: {row.Beginner.Name} has no member {row.Detail}");
                continue;
            }

            // A constant is the action it names; a method that begins it is not checked.
            if (members[0] is FieldInfo { IsLiteral: true } constant
                && constant.GetRawConstantValue() is int value && value != row.ActionId)
                wrong.Add($"{row.ActionId}: {row.Beginner.Name}.{row.Detail} is {value}");
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void EveryBegunActionNamesItsStarters_AndEveryUnbuiltOneItsBlocker()
    {
        Assert.All(CreatureActionCoverage.All.Where(r => r.Status != ActionStatus.NeverBegun),
            r => Assert.False(string.IsNullOrWhiteSpace(r.Starters), $"{r.ActionId} names no starter"));
        Assert.All(CreatureActionCoverage.All.Where(r => r.Status == ActionStatus.Unbuilt),
            r => Assert.False(string.IsNullOrWhiteSpace(r.Detail), $"{r.ActionId} names no blocker"));
    }

    /// <summary>The binary's own answer (2026-11-05): the 109 begin calls pass 67 distinct indices, and the
    /// only other writers of <c>+0x3A9</c> clear it.</summary>
    [Fact]
    public void TheNeverBegunActionsAreTheOnesNothingBegins()
    {
        int[] neverBegun = CreatureActionCoverage.All.Where(r => r.Status == ActionStatus.NeverBegun)
            .Select(r => r.ActionId).ToArray();

        Assert.Equal([0, 3, 12, 14, 21, 23, 28, 33, 34, 38, 42, 48, 51, 58], neverBegun);
        Assert.All(CreatureActionCoverage.All.Where(r => r.Status == ActionStatus.NeverBegun),
            r => Assert.Contains(".md", r.Detail));
    }

    [Fact]
    public void OnlyABuiltRowNamesTheCodeThatBeginsIt()
    {
        Assert.All(CreatureActionCoverage.All, r =>
            Assert.Equal(r.Status == ActionStatus.Built, r.Beginner is not null));
    }
}
