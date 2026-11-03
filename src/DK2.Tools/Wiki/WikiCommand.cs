// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;

namespace DK2.Tools.Wiki;

/// <summary><c>dk2tools wiki --out &lt;dir&gt;</c>: writes the generated wiki pages (ADR-0643).</summary>
internal static class WikiCommand
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
        try
        {
            inputs = Load(root);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"wiki: {e.Message}");
            return 2;
        }

        Directory.CreateDirectory(output);
        var utf8 = new UTF8Encoding(false);
        foreach (var (file, content) in WikiPages.All(inputs))
            File.WriteAllText(Path.Combine(output, file), content, utf8);
        Console.WriteLine($"wiki: {WikiPages.All(inputs).Count} pages -> {output}");
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
        return new WikiInputs(catalog, notes, adrs, rows, records);
    }
}
