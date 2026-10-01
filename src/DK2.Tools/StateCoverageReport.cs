// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Tools;

/// <summary><c>dk2tools coverage states</c>: <see cref="CreatureStateCoverage"/> counted, so a
/// document quotes this command instead of a number that goes stale.</summary>
internal static class StateCoverageReport
{
    public static int Print(TextWriter output)
    {
        IReadOnlyList<StateCoverageRow> rows = CreatureStateCoverage.All;
        output.WriteLine($"creature states: {rows.Count}"
            + $"  built: {Count(rows, StateStatus.Built)}"
            + $"  unreachable: {Count(rows, StateStatus.Unreachable)}"
            + $"  unbuilt: {Count(rows, StateStatus.Unbuilt)}");
        output.WriteLine();

        output.WriteLine($"  {"category",-21} {"states",6} {"built",6} {"unreachable",12} {"unbuilt",8}");
        foreach (IGrouping<CreatureStateCategory, StateCoverageRow> category in rows
                     .GroupBy(r => CreatureStateTable.Get(r.StateId).Category)
                     .OrderBy(g => (int)g.Key))
        {
            var inCategory = category.ToList();
            output.WriteLine($"  {$"{category.Key} ({(int)category.Key})",-21} {inCategory.Count,6}"
                + $" {Count(inCategory, StateStatus.Built),6}"
                + $" {Count(inCategory, StateStatus.Unreachable),12}"
                + $" {Count(inCategory, StateStatus.Unbuilt),8}");
        }

        output.WriteLine();
        output.WriteLine($"unbuilt ({Count(rows, StateStatus.Unbuilt)}):");
        foreach (StateCoverageRow row in rows.Where(r => r.Status == StateStatus.Unbuilt))
        {
            output.WriteLine($"  {row.StateId,3}  {CreatureStateTable.Get(row.StateId).Category,-16} {row.Detail}");
        }

        return 0;
    }

    private static int Count(IEnumerable<StateCoverageRow> rows, StateStatus status) =>
        rows.Count(r => r.Status == status);
}
