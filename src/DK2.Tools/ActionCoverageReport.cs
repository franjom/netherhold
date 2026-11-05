// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Tools;

/// <summary><c>dk2tools coverage actions</c>: <see cref="CreatureActionCoverage"/> counted, so a document
/// quotes this command instead of a number that goes stale.</summary>
internal static class ActionCoverageReport
{
    public static int Print(TextWriter output)
    {
        IReadOnlyList<ActionCoverageRow> rows = CreatureActionCoverage.All;
        output.WriteLine($"creature actions: {rows.Count}"
            + $"  built: {Count(rows, ActionStatus.Built)}"
            + $"  unbuilt: {Count(rows, ActionStatus.Unbuilt)}"
            + $"  unreachable: {Count(rows, ActionStatus.Unreachable)}"
            + $"  never begun: {Count(rows, ActionStatus.NeverBegun)}");
        output.WriteLine();

        output.WriteLine($"built ({Count(rows, ActionStatus.Built)}):");
        foreach (ActionCoverageRow row in rows.Where(r => r.Status == ActionStatus.Built))
        {
            output.WriteLine($"  {row.ActionId,2}  {row.Beginner!.Name}.{row.Detail}"
                + (row.Note is null ? "" : $"  (gaps: {row.Note})"));
        }

        output.WriteLine();
        output.WriteLine($"unbuilt ({Count(rows, ActionStatus.Unbuilt)}):");
        foreach (ActionCoverageRow row in rows.Where(r => r.Status == ActionStatus.Unbuilt))
        {
            output.WriteLine($"  {row.ActionId,2}  {row.Starters}: {row.Detail}");
        }

        output.WriteLine();
        output.WriteLine($"unreachable ({Count(rows, ActionStatus.Unreachable)}):");
        foreach (ActionCoverageRow row in rows.Where(r => r.Status == ActionStatus.Unreachable))
        {
            output.WriteLine($"  {row.ActionId,2}  {row.Starters}: {row.Detail}");
        }

        output.WriteLine();
        output.WriteLine($"never begun: {string.Join(", ", rows.Where(r => r.Status == ActionStatus.NeverBegun).Select(r => r.ActionId))}");
        return 0;
    }

    private static int Count(IEnumerable<ActionCoverageRow> rows, ActionStatus status) =>
        rows.Count(r => r.Status == status);
}
