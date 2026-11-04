// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Tools.SimMatrix;

namespace DK2.Tools.Wiki;

/// <summary><c>dk2tools wiki --out &lt;dir&gt;</c>: writes the generated wiki pages (ADR-0643), the simulation
/// read/write matrix (<see cref="SimMatrixCommand"/>) among them.</summary>
internal static partial class WikiCommand
{
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
            Console.Error.WriteLine("usage: dk2tools wiki --out <dir>");
            return 2;
        }

        WikiInputs inputs;
        SimMatrixResult matrix;
        try
        {
            inputs = Load(root);
            matrix = SimMatrixAnalysis.Run(root);
        }
        // InvalidOperationException: the simulation's shape changed under a table or a parse (a .Single() that failed).
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"wiki: {e.Message}");
            return 2;
        }

        Directory.CreateDirectory(output);
        var utf8 = new UTF8Encoding(false);
        var pages = WikiPages.All(inputs);
        foreach (var (file, content) in pages)
            File.WriteAllText(Path.Combine(output, file), content, utf8);
        SimMatrixCommand.Write(output, matrix);
        Console.WriteLine($"wiki: {pages.Count + 1} pages -> {output}");
        return 0;
    }

    /// <summary>Everything the pages read, from the working tree under <paramref name="root"/>.</summary>
    internal static WikiInputs Load(string root)
    {
        var catalog = SystemCatalog.Load(root);
        var notes = ResearchNotes.Load(root);
        var names = notes.Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
        string decisions = Path.Combine(root, AdrIndex.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var adrs = AdrIndex.Parse(File.ReadAllLines(decisions), names);
        string questions = Path.Combine(root, ResearchNotes.Folder, ResearchNotes.OpenQuestions + ".md");
        var rows = WikiPages.ParseQuestions(File.ReadAllLines(questions), names);
        var errors = new List<string>();
        var records = FunctionKbCommand.Load(new FunctionKbCommand.Source(root, staged: false), errors);
        if (errors.Count > 0) throw new InvalidDataException($"function records: {errors[0]} (and {errors.Count - 1} more)");
        return new WikiInputs(catalog, notes, adrs, rows, records, AdrsCitedInCode(root));
    }

    /// <summary>Code folders whose <c>ADR-NNNN</c> citations count as an ADR being built: the source and the Unity
    /// scripts, not tests.</summary>
    internal static readonly string[] CodeFolders = ["src", "unity/DK2.Unity/Assets"];

    /// <summary>Every ADR number a <c>.cs</c> file under <see cref="CodeFolders"/> cites, build output skipped.</summary>
    internal static IReadOnlySet<int> AdrsCitedInCode(string root)
    {
        var cited = new HashSet<int>();
        foreach (string folder in CodeFolders)
        {
            string dir = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Contains("obj") || parts.Contains("bin")) continue;
                foreach (System.Text.RegularExpressions.Match m in AdrCitation().Matches(File.ReadAllText(file)))
                    foreach (System.Text.RegularExpressions.Capture number in m.Groups[1].Captures.Concat(m.Groups[2].Captures))
                        cited.Add(int.Parse(number.ValueSpan, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        return cited;
    }

    // ADR-0058, and the shorthand ADR-0058/0063/0065 for several.
    [System.Text.RegularExpressions.GeneratedRegex(@"\bADR-(\d{4})(?:/(\d{4}))*\b")]
    private static partial System.Text.RegularExpressions.Regex AdrCitation();
}
