// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>How far this project has come with one record of DK2's creature action table.</summary>
public enum ActionStatus : byte
{
    /// <summary>Our code begins the action where DK2 does, as an art slot and a hold at the caller (ours has
    /// no action table). <see cref="ActionCoverageRow.Beginner"/> and <see cref="ActionCoverageRow.Detail"/>
    /// name the member that does: a constant naming the action, or the method that begins it. Built is
    /// not every starter: <see cref="ActionCoverageRow.Note"/> names the ones not mirrored.</summary>
    Built,

    /// <summary>DK2 begins it and ours does not; the detail names the starters and the blocker.</summary>
    Unbuilt,

    /// <summary>Begun only in states DK2 never enters (their <see cref="StateStatus.Unreachable"/> rows), such
    /// as the Crypt's, which is dead code; the detail cites why.</summary>
    Unreachable,

    /// <summary>Nothing in the binary begins it: none of the 109 calls of the four begin entry points
    /// passes it, save state 148's 0, and every other write of the action field (<c>+0x3A9</c>) clears it. Index 0 is the null action
    /// (state 148 passes it to mean none, which starts and ends at once) and 21 the one the runner
    /// excludes.</summary>
    NeverBegun,
}

/// <summary>One action's row. For <see cref="ActionStatus.Built"/>, <see cref="Beginner"/> and
/// <see cref="Detail"/> name the type and member that begin it; otherwise the detail is the evidence or the
/// blocker.</summary>
public sealed class ActionCoverageRow
{
    internal ActionCoverageRow(int actionId, ActionStatus status, Type? beginner, string detail, string starters, string? note)
    {
        ActionId = actionId;
        Status = status;
        Beginner = beginner;
        Detail = detail;
        Starters = starters;
        Note = note;
    }

    public int ActionId { get; }
    public ActionStatus Status { get; }
    public Type? Beginner { get; }
    public string Detail { get; }

    /// <summary>Where DK2 begins it: the states or routines that call a begin entry point with it
    /// (docs/research/creature-action-table.md section 4); empty for <see cref="ActionStatus.NeverBegun"/>.</summary>
    public string Starters { get; }

    /// <summary>For a Built row, the starters ours does not mirror or a stand-in it uses; null for none.</summary>
    public string? Note { get; }
}

/// <summary>Every one of DK2's 81 creature actions (<c>0x006E4198</c>) with its status here, the one source
/// <c>dk2tools coverage actions</c> counts. Ours has no action table: each rule that mirrors a starting
/// state plays the action's art slot with a hold of its own, so this list is how "which actions are built"
/// is read. docs/research/creature-action-table.md.</summary>
/// <remarks>The starters are the 109 begin calls read on 2026-11-05 (section 4); the Built rows were read
/// from the code on the same day. Keep a row in step when a rule starts or stops beginning an action.</remarks>
public static partial class CreatureActionCoverage
{
    private static readonly ActionCoverageRow[] Rows = Build();

    /// <summary>All 81 rows, by action id.</summary>
    public static IReadOnlyList<ActionCoverageRow> All => Rows;

    public static ActionCoverageRow Get(int actionId)
    {
        if ((uint)actionId >= CreatureActionTable.Count) throw new ArgumentOutOfRangeException(nameof(actionId));
        return Rows[actionId];
    }

    private static ActionCoverageRow[] Build()
    {
        var rows = new List<ActionCoverageRow>(CreatureActionTable.Count);
        AddRows(rows);
        var byId = new ActionCoverageRow[CreatureActionTable.Count];
        foreach (ActionCoverageRow row in rows)
        {
            if (byId[row.ActionId] is not null)
                throw new InvalidOperationException($"Action {row.ActionId} has two coverage rows.");
            byId[row.ActionId] = row;
        }

        for (int id = 0; id < byId.Length; id++)
        {
            if (byId[id] is null) throw new InvalidOperationException($"Action {id} has no coverage row.");
        }

        return byId;
    }

    private static ActionCoverageRow Built(int id, Type handler, string member, string starters, string? notMirrored = null) =>
        new(id, ActionStatus.Built, handler, member, starters, notMirrored);

    private static ActionCoverageRow Unbuilt(int id, string starters, string blocker) =>
        new(id, ActionStatus.Unbuilt, null, blocker, starters, null);

    private static ActionCoverageRow Unreachable(int id, string starters, string evidence) =>
        new(id, ActionStatus.Unreachable, null, evidence, starters, null);

    private static ActionCoverageRow NeverBegun(int id, string evidence) =>
        new(id, ActionStatus.NeverBegun, null, evidence, "", null);
}
