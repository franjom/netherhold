// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary><see cref="CreatureStateCoverage"/> on its own: one row per state, every Built row
/// pointing at code that exists, every other row citing its evidence.</summary>
public class StateCoverageTableTests
{
    [Fact]
    public void EveryOneOfThe281StatesHasExactlyOneRow()
    {
        int[] ids = CreatureStateCoverage.All.Select(r => r.StateId).ToArray();

        Assert.Equal(CreatureStateTable.Count, ids.Length);
        Assert.Equal(Enumerable.Range(0, CreatureStateTable.Count), ids.Order());
        for (int id = 0; id < CreatureStateTable.Count; id++)
            Assert.Equal(id, CreatureStateCoverage.Get(id).StateId);
    }

    [Fact]
    public void EveryBuiltRowNamesAMemberThatExists_AndAConstantNamesItsOwnState()
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var wrong = new List<string>();
        foreach (StateCoverageRow row in CreatureStateCoverage.All.Where(r => r.Status == StateStatus.Built))
        {
            MemberInfo[] members = row.Handler!.GetMember(row.Detail, any);
            if (members.Length == 0)
            {
                wrong.Add($"{row.StateId}: {row.Handler.Name} has no member {row.Detail}");
                continue;
            }

            // A constant is the state it names; a method (a state picker, a handler) is not checked.
            if (members[0] is FieldInfo { IsLiteral: true } constant
                && constant.GetRawConstantValue() is int value && value != row.StateId)
                wrong.Add($"{row.StateId}: {row.Handler.Name}.{row.Detail} is {value}");
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void EveryRowThatIsNotBuiltCitesAQuestionANoteOrSaysNotRead()
    {
        string[] bare = CreatureStateCoverage.All
            .Where(r => r.Status != StateStatus.Built)
            .Where(r => !(r.Detail.Contains('Q') || r.Detail.Contains(".md") || r.Detail.Contains("ADR-")
                          || r.Detail.Contains("not read")))
            .Select(r => $"{r.StateId} {r.Status}: {r.Detail}")
            .ToArray();

        Assert.True(bare.Length == 0, string.Join("\n", bare));
    }

    [Theory]
    [InlineData(9, "Q415")]
    [InlineData(88, "Q415")]
    [InlineData(140, "Q431")]
    public void NineEightyEightAndOneForty_AreUnreachableOnlyByAScanOfTheBinary_SoTheirOpenQuestionStaysCited(
        int stateId, string question)
    {
        StateCoverageRow row = CreatureStateCoverage.Get(stateId);

        Assert.Equal(StateStatus.Unreachable, row.Status);
        Assert.Contains(question, row.Detail);
        Assert.Contains("STRONGLY INFERRED", row.Detail);
    }

    [Fact]
    public void OnlyABuiltRowNamesAHandlerOrAnExerciseReason()
    {
        Assert.All(CreatureStateCoverage.All, r =>
        {
            Assert.Equal(r.Status == StateStatus.Built, r.Handler is not null);
            if (r.Status != StateStatus.Built) Assert.Null(r.UnexercisedBecause);
        });
    }
}

/// <summary>Runs after every parallel test collection in the assembly (xUnit runs collections
/// that disable parallelisation last), so <see cref="StateEntryLog"/> holds what the whole
/// simulation suite entered.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AfterEveryOtherTest
{
    public const string Name = "state coverage, after every other test";
}

/// <summary>The table against what the simulation suite actually does. Entered implies Built
/// fails on any offender. Built implies entered keeps an allow-list, each entry with its reason
/// (<see cref="StateCoverageRow.UnexercisedBecause"/>): a handler nothing here reaches is
/// worth knowing about, and failing would only invite a throwaway test or a demoted row.</summary>
[Collection(AfterEveryOtherTest.Name)]
public class StateCoverageRunTests
{
    [Fact]
    public void EveryStateTheSimulationSuiteEntersIsBuilt()
    {
        Assert.True(StateEntryLog.Enabled, $"{StateEntryLog.SwitchName} is off in this test project.");
        string[] offenders = StateEntryLog.Entered()
            .Where(id => CreatureStateCoverage.Get(id).Status != StateStatus.Built)
            .Select(id => $"{id} ({CreatureStateCoverage.Get(id).Status}), first by {StateEntryLog.FirstEnteredBy(id)}")
            .ToArray();

        Assert.True(offenders.Length == 0, "Entered but not Built:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryBuiltStateIsEnteredBySomeSimulationTest_OrIsAllowListedWithAReason()
    {
        var entered = StateEntryLog.Entered().ToHashSet();
        Assert.True(entered.Count > 50, $"Only {entered.Count} states entered: did this run first?");

        string[] unexercised = CreatureStateCoverage.All
            .Where(r => r.Status == StateStatus.Built && r.UnexercisedBecause is null && !entered.Contains(r.StateId))
            .Select(r => $"{r.StateId} ({r.Handler!.Name}.{r.Detail})")
            .ToArray();
        string[] stale = CreatureStateCoverage.All
            .Where(r => r.UnexercisedBecause is not null && entered.Contains(r.StateId))
            .Select(r => $"{r.StateId}: allow-listed ({r.UnexercisedBecause}) but entered by {StateEntryLog.FirstEnteredBy(r.StateId)}")
            .ToArray();

        Assert.True(unexercised.Length == 0,
            "Built, but no simulation test enters it and it is not allow-listed:\n" + string.Join("\n", unexercised));
        Assert.True(stale.Length == 0, "Allow-listed but entered:\n" + string.Join("\n", stale));
    }
}
