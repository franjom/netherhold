// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;

namespace DK2.Tools.Wiki;

/// <summary>One ADR of docs/DECISIONS.md. Lines are 1-based; an amendment heading belongs to its ADR.</summary>
public sealed record Adr(
    int Number, string Heading, string Title, string Status, int? SupersededBy,
    IReadOnlyList<string>? ExplicitSystems, IReadOnlyList<string> CitedNotes, int Line, int EndLine);

/// <summary>Reads ADRs out of docs/DECISIONS.md for the docs gate and the wiki (ADR-0643).</summary>
public static partial class AdrIndex
{
    public const string RelativePath = "docs/DECISIONS.md";

    /// <summary>The status words an ADR can resolve to, after <see cref="NormaliseStatus"/>.</summary>
    /// <remarks>No "implemented": an ADR's status says it was decided, and <c>accepted</c> stays when the code lands (ADR-0660).</remarks>
    public static readonly string[] Statuses = ["proposed", "accepted", "amended", "superseded", "withdrawn"];

    [GeneratedRegex(@"^#{2,3} ADR-(\d{4})\b(.*)$")] private static partial Regex HeadingRx();
    [GeneratedRegex(@"^\s*amendment\b", RegexOptions.IgnoreCase)] private static partial Regex AmendmentRx();
    [GeneratedRegex(@"^(?:\*\*)?(?:Context|Decision|Consequences)\b")] private static partial Regex BodyRx();
    [GeneratedRegex(@"(?:\*\*)?Status(?:\*\*)?\s*[:.]?\s*(?:\*\*)?\s*[:.]?\s*(?:\*\*)?\s*([A-Za-z]+)")] private static partial Regex StatusRx();
    [GeneratedRegex(@"\b(?:superseded|reversed|replaced|withdrawn)\b[^.\n]*?\bby ADR-(\d{4})", RegexOptions.IgnoreCase)] private static partial Regex SupersededRx();
    [GeneratedRegex(@"\*\*Systems:?\*\*:?\s*([a-z0-9][a-z0-9 ,-]*)")] private static partial Regex SystemsRx();
    [GeneratedRegex(@"(?<![\w/.-])(?:docs/research/|research/)?([a-z0-9][a-z0-9-]*)\.md\b")] private static partial Regex NoteRx();

    /// <summary>A header runs from the heading to the first Context/Decision/Consequences line, at most this far.</summary>
    const int HeaderLines = 8;

    /// <summary>
    /// Every ADR in file order. <paramref name="noteNames"/> limits <see cref="Adr.CitedNotes"/> to notes
    /// that exist; a name it cites twice counts once. A line holding a duplicate ADR number makes a second
    /// entry, which <see cref="Duplicates"/> reports.
    /// </summary>
    public static IReadOnlyList<Adr> Parse(IReadOnlyList<string> lines, ISet<string> noteNames)
    {
        var starts = new List<(int Index, int Number, string Heading, string Tail)>();
        for (int i = 0; i < lines.Count; i++)
        {
            var m = HeadingRx().Match(lines[i]);
            if (!m.Success || AmendmentRx().IsMatch(m.Groups[2].Value)) continue;
            starts.Add((i, int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                lines[i].TrimStart('#').Trim(), m.Groups[2].Value));
        }

        var adrs = new List<Adr>(starts.Count);
        for (int k = 0; k < starts.Count; k++)
        {
            var (from, number, heading, rest) = starts[k];
            int to = k + 1 < starts.Count ? starts[k + 1].Index : lines.Count;
            // A trailing non-ADR section (the file's own closing headings) is not the last ADR's.
            for (int j = from + 1; j < to; j++)
                if (lines[j].StartsWith("## ", StringComparison.Ordinal) && !HeadingRx().IsMatch(lines[j])) { to = j; break; }

            var header = new List<string>();
            for (int j = from + 1; j < Math.Min(to, from + 1 + HeaderLines); j++)
            {
                if (BodyRx().IsMatch(lines[j].TrimStart())) break;
                header.Add(lines[j]);
            }
            string headerText = string.Join("\n", header);

            var status = StatusRx().Match(headerText);
            var superseded = SupersededRx().Match(headerText);
            var systems = SystemsRx().Match(headerText);
            var cited = new SortedSet<string>(StringComparer.Ordinal);
            for (int j = from; j < to; j++)
                foreach (Match note in NoteRx().Matches(lines[j]))
                    if (noteNames.Contains(note.Groups[1].Value)) cited.Add(note.Groups[1].Value);

            adrs.Add(new Adr(
                number,
                heading,
                Title(rest),
                status.Success ? NormaliseStatus(status.Groups[1].Value) : "",
                superseded.Success ? int.Parse(superseded.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null,
                systems.Success
                    ? systems.Groups[1].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray()
                    : null,
                cited.ToArray(),
                from + 1,
                to));
        }
        return adrs;
    }

    /// <summary>The status's first word, lower-cased; "reversed" is superseded and "provisional" proposed.
    /// Anything else is returned empty, which the gate reports.</summary>
    public static string NormaliseStatus(string word)
    {
        string w = word.ToLowerInvariant();
        return w switch
        {
            "reversed" => "superseded",
            "provisional" => "proposed",
            _ => Statuses.Contains(w) ? w : "",
        };
    }

    /// <summary>An ADR's systems: its explicit field, or else the union of its cited notes' systems,
    /// in catalogue order.</summary>
    public static IReadOnlyList<string> Systems(Adr adr, IReadOnlyDictionary<string, IReadOnlyList<string>> noteSystems,
        SystemCatalog catalog)
    {
        IEnumerable<string> ids = adr.ExplicitSystems
            ?? adr.CitedNotes.SelectMany(n => noteSystems.TryGetValue(n, out var s) ? s : []);
        return ids.Distinct(StringComparer.Ordinal)
            .OrderBy(catalog.OrderOf).ThenBy(s => s, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Every ADR number that heads more than one ADR (amendments excluded), ascending.</summary>
    public static IEnumerable<int> Duplicates(IEnumerable<Adr> adrs)
        => adrs.GroupBy(a => a.Number).Where(g => g.Count() > 1).Select(g => g.Key).Order();

    /// <summary>Numbers below the highest that no ADR uses, ascending.</summary>
    public static IEnumerable<int> Gaps(IEnumerable<Adr> adrs)
    {
        var used = adrs.Select(a => a.Number).ToHashSet();
        return used.Count == 0 ? [] : Enumerable.Range(1, used.Max()).Where(n => !used.Contains(n));
    }

    static string Title(string rest)
    {
        string t = rest.Trim();
        foreach (string sep in new[] { "—", "–", "-", ":" })
            if (t.StartsWith(sep, StringComparison.Ordinal)) return t[sep.Length..].Trim();
        return t;
    }
}
