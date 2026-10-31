// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text.RegularExpressions;

namespace DK2.Tools;

public sealed record HistoryEntry(string Date, string Agent, string Text);

/// <summary>One docs/research/functions/&lt;ADDRESS&gt;.md, parsed. Address is eight upper-case hex digits.</summary>
public sealed record FunctionRecord(
    string RelativePath, string Address, string Name, string Role, string Confidence, string Status,
    IReadOnlyList<string> Evidence, IReadOnlyList<string> Implements, string Body, IReadOnlyList<HistoryEntry> History)
{
    /// <summary>The header as one string, so "did the verdict change" is one comparison.</summary>
    public string HeaderKey => string.Join("\n", Name, Role, Confidence, Status, string.Join(",", Evidence), string.Join(",", Implements));
}

public static partial class FunctionKb
{
    public const string Folder = "docs/research/functions";
    public static readonly string[] Confidences = ["CONFIRMED", "LIKELY", "GUESS", "DISPUTED"];
    public static readonly string[] Statuses = ["READ", "BUILT", "SUPERSEDED"];
    /// <summary>DKII.EXE code: .text from 0x401000, cseg ends before 0x6A0000 (Blocks.py, 2026-09-30).</summary>
    public const uint LowestCode = 0x00401000, HighestCode = 0x006A0000;

    const string HistoryHeading = "## History";
    const string HistoryRule = "History line must be '- YYYY-MM-DD agent: text' (agent is lower-case letters, digits, hyphens)";
    /// <summary>Header keys whose value is a token, so a trailing ' # comment' can be stripped; name and role are prose.</summary>
    static readonly string[] TokenKeys = ["address", "confidence", "status", "evidence", "implements"];
    static readonly string[] HeaderKeys = ["address", "name", "role", "confidence", "status", "evidence", "implements"];

    [GeneratedRegex(@"^0[xX](00[4-6][0-9A-F]{5})$")] private static partial Regex AddressRx();
    [GeneratedRegex(@"^- (\d{4}-\d{2}-\d{2}) ([a-z][a-z0-9-]*): (.+)$")] private static partial Regex HistoryRx();
    [GeneratedRegex(@"\bFUN_(00[4-6][0-9A-Fa-f]{5})\b")] private static partial Regex MentionRx();
    [GeneratedRegex(@"[^a-z0-9_ -]")] private static partial Regex NonSlugRx();

    /// <summary>
    /// Parses one record. <paramref name="lines"/> carry no line terminators; a BOM on the first line is ignored.
    /// The header runs to the first blank line, the body to '## History', and every problem is appended to
    /// <paramref name="errors"/> as 'path[:line]: message'. A record is always returned, with empty fields where parsing failed.
    /// </summary>
    public static FunctionRecord Parse(string relativePath, string[] lines, List<string> errors)
    {
        if (lines.Length > 0 && lines[0].StartsWith('\uFEFF'))
            lines = [lines[0][1..], .. lines.Skip(1)];

        var header = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        int i = 0;
        for (; i < lines.Length && lines[i].Trim().Length > 0; i++)
        {
            if (lines[i].Trim().Equals(HistoryHeading, StringComparison.Ordinal))
            { errors.Add($"{relativePath}:{i + 1}: no blank line ends the header before '{HistoryHeading}'"); break; }
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) { errors.Add($"{relativePath}:{i + 1}: header line is not 'key: value'"); continue; }
            string key = lines[i][..colon].Trim();
            string value = lines[i][(colon + 1)..].Trim();
            int hash = value.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0 && TokenKeys.Contains(key)) value = value[..hash].Trim();
            if (!header.TryGetValue(key, out var list)) header[key] = list = [];
            list.Add(value);
        }

        string One(string key)
        {
            if (!header.TryGetValue(key, out var v)) { errors.Add($"{relativePath}: header lacks '{key}'"); return ""; }
            if (v.Count > 1) errors.Add($"{relativePath}: '{key}' given {v.Count} times");
            if (v[0].Length == 0) errors.Add($"{relativePath}: '{key}' is empty");
            return v[0];
        }
        IReadOnlyList<string> Many(string key) => header.TryGetValue(key, out var v) ? v : [];

        string addressText = One("address");
        var m = AddressRx().Match(addressText.ToUpperInvariant());
        string address = m.Success ? m.Groups[1].Value : "";
        if (!m.Success && addressText.Length > 0) errors.Add($"{relativePath}: address '{addressText}' is not 0x00xxxxxx inside code");
        if (address.Length > 0 && !Path.GetFileNameWithoutExtension(relativePath).Equals(address, StringComparison.Ordinal))
            errors.Add($"{relativePath}: file name must be the address {address}");

        string name = One("name"), role = One("role"), confidence = One("confidence"), status = One("status");
        if (confidence.Length > 0 && !Confidences.Contains(confidence)) errors.Add($"{relativePath}: confidence '{confidence}' is not one of {string.Join("|", Confidences)}");
        if (status.Length > 0 && !Statuses.Contains(status)) errors.Add($"{relativePath}: status '{status}' is not one of {string.Join("|", Statuses)}");
        if (Many("evidence").Count == 0) errors.Add($"{relativePath}: header lacks 'evidence'");

        int historyAt = Array.FindIndex(lines, l => l.Trim().Equals(HistoryHeading, StringComparison.Ordinal));
        if (historyAt < 0) { errors.Add($"{relativePath}: no '{HistoryHeading}' section"); historyAt = lines.Length; }
        int bodyStart = Math.Min(i, historyAt);
        for (int j = bodyStart; j < historyAt; j++)
        {
            int colon = lines[j].IndexOf(':');
            if (colon > 0 && HeaderKeys.Contains(lines[j][..colon]))
                errors.Add($"{relativePath}:{j + 1}: header key '{lines[j][..colon]}' comes after the blank line that ends the header");
        }
        string body = string.Join("\n", lines.Skip(bodyStart).Take(historyAt - bodyStart)).Trim();

        var history = new List<HistoryEntry>();
        for (int j = historyAt + 1; j < lines.Length; j++)
        {
            if (lines[j].Trim().Length == 0) continue;
            if (lines[j].StartsWith("  ", StringComparison.Ordinal) && history.Count > 0)
            { history[^1] = history[^1] with { Text = history[^1].Text + " " + lines[j].Trim() }; continue; }
            var h = HistoryRx().Match(lines[j]);
            if (!h.Success) { errors.Add($"{relativePath}:{j + 1}: {HistoryRule}: {lines[j]}"); continue; }
            history.Add(new HistoryEntry(h.Groups[1].Value, h.Groups[2].Value, h.Groups[3].Value));
        }
        if (history.Count == 0 && historyAt < lines.Length) errors.Add($"{relativePath}: History is empty");

        return new FunctionRecord(relativePath, address, name, role, confidence, status, Many("evidence"), Many("implements"), body, history);
    }

    /// <summary>Every FUN_00xxxxxx in the text, as eight upper-case hex digits.</summary>
    public static IEnumerable<string> Mentions(string text)
        => MentionRx().Matches(text).Select(m => m.Groups[1].Value.ToUpperInvariant());

    /// <summary>Whether <paramref name="text"/> cites <paramref name="address"/> (eight hex digits): after
    /// <c>0x</c> or <c>FUN_</c> with any leading zeros, or as the bare eight digits, case-insensitively.</summary>
    public static bool Cites(string text, string address) => CitationRx(address).IsMatch(text);

    static Regex CitationRx(string address) =>
        new($"(?<![0-9A-Za-z_])(?:(?:0[xX]|FUN_)0*{address.TrimStart('0')}|{address})(?![0-9A-Za-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Whether a change gave this record a new reading: it is new, or it gained a History line.</summary>
    public static bool GainedReading(FunctionRecord now, FunctionRecord? before)
        => before is null || now.History.Count > before.History.Count;

    /// <summary>For each changed record, the files outside the change that cite its address. A note, not
    /// a finding: the reading moved, and code or prose written against the old one does not move with it
    /// (2026-10-30: a record said "below a sixth" while the code that relied on it divided by three). The
    /// record itself, <c>INDEX.md</c> and <c>docs/plans/</c> are skipped.</summary>
    public static IEnumerable<(string Address, IReadOnlyList<string> Files)> UntouchedCiters(
        IReadOnlyCollection<string> addresses, IEnumerable<(string File, string Text)> files, ISet<string> changed)
    {
        var hits = addresses.ToDictionary(a => a, _ => new List<string>(), StringComparer.Ordinal);
        var patterns = addresses.ToDictionary(a => a, CitationRx, StringComparer.Ordinal);
        foreach (var (file, text) in files)
        {
            if (changed.Contains(file) || file.StartsWith("docs/plans/", StringComparison.Ordinal)
                || file.EndsWith("/INDEX.md", StringComparison.Ordinal)) continue;
            foreach (string address in addresses)
                if (file != $"{Folder}/{address}.md" && patterns[address].IsMatch(text)) hits[address].Add(file);
        }
        foreach (var (address, list) in hits.OrderBy(h => h.Key, StringComparer.Ordinal))
            if (list.Count > 0) yield return (address, list);
    }

    /// <summary>A Markdown heading's anchor as GitHub makes it: lower-case, punctuation dropped, spaces to hyphens.</summary>
    public static string Slug(string heading)
    {
        string t = heading.TrimStart('#').Trim().ToLowerInvariant();
        t = NonSlugRx().Replace(t, "");
        return t.Replace(' ', '-');
    }

    /// <summary>
    /// Unique addresses inside code; every evidence link resolves to a file and, with #anchor, to a heading.
    /// Lenient: a line starting with '#' inside a fenced code block counts as a heading, and GitHub's '-1' suffix
    /// for a repeated heading is not modelled.
    /// </summary>
    /// <param name="fileExists">repo-relative path → exists</param>
    /// <param name="readLines">repo-relative path → its lines</param>
    public static IEnumerable<string> CheckSet(IReadOnlyList<FunctionRecord> records,
        Func<string, bool> fileExists, Func<string, string[]> readLines)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in records)
        {
            if (r.Address.Length == 0) continue;
            if (seen.TryGetValue(r.Address, out string? other))
                yield return $"{r.RelativePath}: address {r.Address} appears twice (also {other})";
            seen[r.Address] = r.RelativePath;

            uint value = uint.Parse(r.Address, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (value < LowestCode || value >= HighestCode)
                yield return $"{r.RelativePath}: {r.Address} is outside DKII.EXE code";

            foreach (string link in r.Evidence)
            {
                int hash = link.IndexOf('#');
                string file = hash < 0 ? link : link[..hash];
                if (!fileExists(file)) { yield return $"{r.RelativePath}: evidence '{file}' does not exist"; continue; }
                if (hash < 0) continue;
                string anchor = link[(hash + 1)..];
                bool found = readLines(file).Any(l => l.StartsWith('#') && Slug(l).Equals(anchor, StringComparison.Ordinal));
                if (!found) yield return $"{r.RelativePath}: no heading in {file} slugs to '#{anchor}'";
            }
        }
    }

    /// <summary>INDEX.md: one row per record, sorted by address. Generated; the records are the source.</summary>
    public static string Index(IReadOnlyList<FunctionRecord> records)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# Functions of DKII.EXE: the register\n\n");
        sb.Append("One row per record in this folder, generated by `dk2tools kb index`; edit the records, not this table. ");
        sb.Append("The record holds the reasoning and history; its evidence links hold the proof (ADR-0046).\n\n");
        sb.Append("| Address | Name | Role | Confidence | Status |\n|---|---|---|---|---|\n");
        foreach (var r in records.OrderBy(r => r.Address, StringComparer.Ordinal))
            sb.Append($"| `0x{r.Address}` | [{r.Name}]({r.Address}.md) | {Cell(r.Role)} | {r.Confidence} | {r.Status} |\n");
        return sb.ToString();
    }

    static string Cell(string text) => text.Replace("|", "\\|");   // a pipe in a role would split the row

    /// <summary>A header that differs from its base, or a record with no base, needs a History line dated today.
    /// In a merge, a header equal to the incoming side's (<paramref name="merged"/>, MERGE_HEAD) was dated there.</summary>
    public static IEnumerable<string> HistoryRequired(FunctionRecord now, FunctionRecord? before, DateOnly today, FunctionRecord? merged = null)
        => HistoryRequired(now, before, [today], merged);

    /// <summary>As above, with any of <paramref name="days"/> accepted: a range check (CI's <c>--since</c>)
    /// accepts the days the range's commits touched the record, since it runs after they were written.</summary>
    public static IEnumerable<string> HistoryRequired(FunctionRecord now, FunctionRecord? before, IReadOnlyCollection<DateOnly> days, FunctionRecord? merged = null)
    {
        if (before is not null && before.HeaderKey == now.HeaderKey) yield break;
        if (merged is not null && merged.HeaderKey == now.HeaderKey) yield break;
        string[] dates = days.Distinct().Order().Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();
        if (now.History.Any(h => dates.Contains(h.Date))) yield break;
        string date = string.Join(" or ", dates);
        yield return before is null
            ? $"{now.RelativePath}: is new and has no History line dated {date}"
            : $"{now.RelativePath}: header changed but no History line is dated {date}";
    }

    /// <summary>Files the mention rule never reads: the records themselves, and plans (process, not claims about the binary).</summary>
    static readonly string[] ExemptPrefixes = [Folder + "/", "docs/plans/"];

    /// <summary>One finding per (file, address) for a FUN_ mentioned in an added line with no record.</summary>
    /// <remarks>The records folder and docs/plans/ are exempt (<see cref="ExemptPrefixes"/>).</remarks>
    public static IEnumerable<string> MissingRecords(
        IEnumerable<(string File, string[] AddedLines)> changes, ISet<string> recorded)
    {
        foreach (var (file, lines) in changes)
        {
            if (ExemptPrefixes.Any(p => file.StartsWith(p, StringComparison.Ordinal))) continue;
            if (!(file.StartsWith("docs/", StringComparison.Ordinal) || file.StartsWith("src/", StringComparison.Ordinal))) continue;
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string line in lines)
                foreach (string address in Mentions(line))
                    if (!recorded.Contains(address)) missing.Add(address);
            foreach (string address in missing)
                yield return $"{file}: mentions FUN_{address} and {Folder}/{address}.md does not exist";
        }
    }
}
