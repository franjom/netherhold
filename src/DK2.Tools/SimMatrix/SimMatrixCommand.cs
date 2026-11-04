// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Text;

namespace DK2.Tools.SimMatrix;

/// <summary><c>dk2tools simmatrix --out &lt;dir&gt;</c>: which systems read and write which world state,
/// in tick order (issue 9, WP6). Writes the wiki page and its JSON.</summary>
internal static class SimMatrixCommand
{
    internal const string PageName = "Simulation-Read-Write-Matrix";

    internal static int Run(string[] args)
    {
        string? root = Repo.FindRepoRoot();
        if (root is null)
        {
            Console.Error.WriteLine("not inside the repository (no DK2.slnx above the working directory).");
            return 2;
        }
        string? output = Repo.OptionValue(args, "--out");
        if (output is null)
        {
            Console.Error.WriteLine("usage: dk2tools simmatrix --out <dir>");
            return 2;
        }

        var clock = Stopwatch.StartNew();
        SimMatrixResult result;
        try
        {
            result = SimMatrixAnalysis.Run(root);
        }
        // InvalidOperationException: the simulation's shape changed under a table or a parse (a .Single() that failed).
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"simmatrix: {e.Message}");
            return 2;
        }

        Write(output, result);
        Console.WriteLine($"simmatrix: {result.Files} files, {result.Rows.Count} rows, {result.Methods} methods, "
            + $"{result.StateTypes.Count} state types in {clock.Elapsed.TotalSeconds:0.0} s -> {output}");
        return 0;
    }

    /// <summary>Writes <see cref="PageName"/>.md and .json into <paramref name="output"/>.</summary>
    internal static void Write(string output, SimMatrixResult result)
    {
        Directory.CreateDirectory(output);
        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(output, PageName + ".md"), SimMatrixReport.Markdown(result), utf8);
        File.WriteAllText(Path.Combine(output, PageName + ".json"), SimMatrixReport.Json(result), utf8);
    }
}
