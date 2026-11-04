// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;

namespace DK2.Tools.Wiki;

public sealed record ResearchNote(string Name, string Title, IReadOnlyList<string> Systems);

/// <summary>
/// The research notes: <c>docs/research/&lt;name&gt;.md</c>, top level only. The function records
/// beside them have their own header, and <c>open-questions.md</c> spans every system, so neither
/// carries front matter (ADR-0643).
/// </summary>
public static partial class ResearchNotes
{
    public const string Folder = "docs/research";
    public const string OpenQuestions = "open-questions";

    [GeneratedRegex(@"^docs/research/([a-z0-9][a-z0-9-]*)\.md$")] private static partial Regex PathRx();
    [GeneratedRegex(@"^systems:\s*\[(.*)\]\s*$")] private static partial Regex SystemsRx();

    /// <summary>The note name for a repository-relative path, or null when the path is not a note.</summary>
    public static string? NoteName(string relative)
    {
        var m = PathRx().Match(relative);
        return m.Success && m.Groups[1].Value != OpenQuestions ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// The number of lines the front matter takes, fences included; 0 when the file has none.
    /// Front matter opens with <c>---</c> on the first line and closes at the next <c>---</c>.
    /// </summary>
    public static int FrontMatterLength(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0 || lines[0].TrimStart('﻿').TrimEnd() != "---") return 0;
        for (int i = 1; i < lines.Count; i++)
            if (lines[i].TrimEnd() == "---") return i + 1;
        return 0;
    }

    /// <summary>
    /// The <c>systems:</c> list of the front matter, in its own order; null when the file has no
    /// front matter or no <c>systems: [...]</c> line. Only the flow form is read, the form the notes use.
    /// </summary>
    public static IReadOnlyList<string>? Systems(IReadOnlyList<string> lines)
    {
        int length = FrontMatterLength(lines);
        for (int i = 1; i < length - 1; i++)
        {
            var m = SystemsRx().Match(lines[i].Trim());
            if (!m.Success) continue;
            return m.Groups[1].Value.Split(',')
                .Select(s => s.Trim().Trim('"', '\''))
                .Where(s => s.Length > 0)
                .ToArray();
        }
        return null;
    }

    /// <summary>The first level-one heading after the front matter, or <paramref name="fallback"/>.</summary>
    public static string Title(IReadOnlyList<string> lines, string fallback)
    {
        for (int i = FrontMatterLength(lines); i < lines.Count; i++)
        {
            string line = lines[i].TrimStart('﻿');
            if (line.StartsWith("# ", StringComparison.Ordinal)) return line[2..].Trim();
        }
        return fallback;
    }

    /// <summary>Every note under <paramref name="root"/>, by name.</summary>
    public static IReadOnlyList<ResearchNote> Load(string root)
    {
        string folder = Path.Combine(root, Folder.Replace('/', Path.DirectorySeparatorChar));
        return Directory.EnumerateFiles(folder, "*.md")
            .Select(path => (Name: NoteName($"{Folder}/{Path.GetFileName(path)}"), Path: path))
            .Where(p => p.Name is not null)
            .Select(p =>
            {
                string[] lines = File.ReadAllLines(p.Path);
                return new ResearchNote(p.Name!, Title(lines, p.Name!), Systems(lines) ?? []);
            })
            .OrderBy(n => n.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
