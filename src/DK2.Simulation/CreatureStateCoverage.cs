// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>How far this project has come with one DK2 state.</summary>
public enum StateStatus : byte
{
    /// <summary>Our code enters the state and acts for a creature in it.
    /// <see cref="StateCoverageRow.Handler"/> names a member that does one or the other: a constant
    /// for the state, or the method that picks it. Built is not complete: 108's kick to 110 at the
    /// level cap is missing, for one; the gaps are in the ADRs and <see cref="CategoryHooks.Slots"/>.</summary>
    Built,

    /// <summary>Shipped data never enters it; <see cref="StateCoverageRow.Detail"/> cites why.</summary>
    Unreachable,

    /// <summary>Reachable, or not shown otherwise, and not built; the detail names the blocker.</summary>
    Unbuilt,
}

/// <summary>One state's row. For <see cref="StateStatus.Built"/>, <see cref="Handler"/> and
/// <see cref="Detail"/> name the type and member that handle the state (a constant naming it,
/// or the method that picks it); otherwise the detail is the evidence or the blocker.</summary>
public sealed class StateCoverageRow
{
    internal StateCoverageRow(int stateId, StateStatus status, Type? handler, string detail, string? unexercisedBecause)
    {
        StateId = stateId;
        Status = status;
        Handler = handler;
        Detail = detail;
        UnexercisedBecause = unexercisedBecause;
    }

    public int StateId { get; }
    public StateStatus Status { get; }
    public Type? Handler { get; }
    public string Detail { get; }

    /// <summary>For a Built state no simulation test enters, why not: the coverage tests'
    /// allow-list. Null for every other row.</summary>
    public string? UnexercisedBecause { get; }
}

/// <summary>Every one of DK2's 281 states with its status here, the one source that
/// <c>dk2tools coverage states</c> counts and the coverage tests hold to what the code does:
/// an entered state must be Built or explicitly mapped to UnbuiltHandler, and an unexercised
/// Built state must say why. UnbuiltHandler destinations retain Unbuilt coverage.</summary>
/// <remarks>Read from the code and the research notes on 2026-10-01, not inferred from the
/// activity labels (<see cref="ActivityRules"/>), which are not a fidelity measure.</remarks>
public static partial class CreatureStateCoverage
{
    /// <summary>The <see cref="StateCoverageRow.UnexercisedBecause"/> of a state only the
    /// scenario suite enters, which <c>DK2.Behavior.Tests</c> checks it does.</summary>
    public const string ScenarioSuiteOnly = "entered by the scenario suite, not by a simulation test";

    private static readonly StateCoverageRow[] Rows = Build();

    /// <summary>All 281 rows, by state id.</summary>
    public static IReadOnlyList<StateCoverageRow> All => Rows;

    public static StateCoverageRow Get(int stateId)
    {
        _ = CreatureStateTable.Get(stateId);
        return Rows[stateId];
    }

    private static StateCoverageRow[] Build()
    {
        var rows = new List<StateCoverageRow>(CreatureStateTable.Count);
        AddRows(rows);
        StateCoverageRow[] byId = new StateCoverageRow[CreatureStateTable.Count];
        foreach (StateCoverageRow row in rows)
        {
            if (byId[row.StateId] is not null)
                throw new InvalidOperationException($"State {row.StateId} has two coverage rows.");
            byId[row.StateId] = row;
        }

        for (int id = 0; id < byId.Length; id++)
        {
            if (byId[id] is null) throw new InvalidOperationException($"State {id} has no coverage row.");
        }

        return byId;
    }

    private static StateCoverageRow Built(int id, Type handler, string member, string? unexercisedBecause = null) =>
        new(id, StateStatus.Built, handler, member, unexercisedBecause);

    private static StateCoverageRow Unreachable(int id, string evidence) =>
        new(id, StateStatus.Unreachable, null, evidence, null);

    private static StateCoverageRow Unbuilt(int id, string blocker) =>
        new(id, StateStatus.Unbuilt, null, blocker, null);
}
