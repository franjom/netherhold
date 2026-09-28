// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using System.Text.RegularExpressions;

namespace DK2.Tools;

public static class VerificationLedger
{
    private const string Header = "Id,Source,Symbol,Confidence,Research,ADRs,CandidateTests,CommentAddresses,A,B,C,D,CheckedDate";
    private static readonly Regex Attribute = new(
        @"\[Dk2Behavior\(Confidence\.(?<confidence>\w+),\s*""(?<research>[^""]+)""\)\]",
        RegexOptions.Compiled);
    private static readonly Regex Address = new(@"\b0x(?:00)?[4-6][0-9A-Fa-f]{5}\b", RegexOptions.Compiled);
    private static readonly Regex Adr = new(@"\bADR-\d{4}\b", RegexOptions.Compiled);

    internal static int Run(string[] args, string? root)
    {
        if (root is null || args.Length > 1 || (args.Length == 1 && args[0] != "--check"))
        {
            Console.Error.WriteLine("usage: dk2tools verification-ledger [--check]");
            return 2;
        }

        string output = Path.Combine(root, "docs", "verification-ledger.csv");
        var previous = File.Exists(output)
            ? File.ReadAllLines(output).Skip(1).Select(ParseCsv)
                .Where(fields => fields.Count == 13)
                .ToDictionary(fields => fields[0], fields => fields, StringComparer.Ordinal)
            : new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var tests = Directory.GetFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);
        var lines = new List<string> { Header };
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string source = File.ReadAllText(file);
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            foreach (Match match in Attribute.Matches(source))
            {
                string confidence = match.Groups["confidence"].Value;
                string research = match.Groups["research"].Value;
                int declarationStart = match.Index + match.Length;
                string symbol = SymbolAt(source, declarationStart);
                string id = relative + "#" + symbol + "#" + research;
                if (!seen.Add(id))
                    throw new InvalidOperationException($"Duplicate ledger id: {id}");

                string commentScope = CommentScope(source, match.Index, declarationStart);
                string researchPath = Path.Combine(root, research.Replace('/', Path.DirectorySeparatorChar));
                string researchText = File.Exists(researchPath) ? File.ReadAllText(researchPath) : "";
                string[] adrs = Distinct(Adr.Matches(commentScope + "\n" + researchText));
                string[] addresses = Distinct(Address.Matches(commentScope));
                string shortName = symbol.Split('.').Last();
                string[] testFiles = tests.Where(pair => Regex.IsMatch(pair.Value,
                        @"\b" + Regex.Escape(shortName) + @"\b"))
                    .Select(pair => Path.GetRelativePath(root, pair.Key).Replace('\\', '/'))
                    .OrderBy(path => path, StringComparer.Ordinal).ToArray();

                var row = new[]
                {
                    id, relative, symbol, confidence, research,
                    string.Join(";", adrs), string.Join(";", testFiles), string.Join(";", addresses),
                    "", "", "", "", ""
                };
                if (previous.TryGetValue(id, out List<string>? old))
                    for (int i = 8; i < row.Length; i++) row[i] = old[i];
                lines.Add(string.Join(",", row.Select(EscapeCsv)));
            }
        }

        var removed = previous.Keys.Except(seen).ToArray();
        if (removed.Any(id => previous[id].Skip(8).Any(value => value.Length != 0)))
        {
            Console.Error.WriteLine("Checked ledger rows no longer match source attributes:");
            foreach (string id in removed) Console.Error.WriteLine("  " + id);
            return 1;
        }

        string generated = string.Join("\n", lines) + "\n";
        if (args.Contains("--check"))
        {
            bool current = File.Exists(output) && File.ReadAllText(output).Replace("\r\n", "\n") == generated;
            Console.WriteLine(current ? $"ledger current: {lines.Count - 1} rules" : "ledger stale");
            return current ? 0 : 1;
        }

        File.WriteAllText(output, generated, new UTF8Encoding(false));
        Console.WriteLine($"wrote {Path.GetRelativePath(root, output)}: {lines.Count - 1} rules");
        return 0;
    }

    /// <summary>The name declared after an attribute ending at <paramref name="start"/>: the type's
    /// name, or the member's — the identifier just before its first <c>(</c>, <c>{</c>, <c>;</c> or
    /// <c>=</c>, so a field is named and not its initialiser.</summary>
    public static string SymbolAt(string source, int start)
    {
        string tail = source[start..Math.Min(source.Length, start + 500)];
        tail = tail.TrimStart();
        while (tail.StartsWith("[", StringComparison.Ordinal))
        {
            int close = tail.IndexOf(']');
            if (close < 0) break;
            tail = tail[(close + 1)..].TrimStart();
        }
        int end = tail.IndexOfAny(['(', '{', ';', '=']);
        Match type = Regex.Match(tail, @"\b(?:class|struct|interface|enum|record)\s+(?<name>\w+)");
        if (type.Success && (end < 0 || type.Index < end))
            return type.Groups["name"].Value;

        string head = end < 0 ? "" : tail[..end].TrimEnd();
        if (head.EndsWith('>'))
        {
            int depth = 0;
            for (int i = head.Length - 1; i >= 0; i--)
            {
                if (head[i] == '>') depth++;
                else if (head[i] == '<' && --depth == 0)
                {
                    head = head[..i].TrimEnd();
                    break;
                }
            }
        }
        Match member = Regex.Match(head, @"(?<name>\w+)$");
        return member.Success ? member.Groups["name"].Value : "line-" + (source[..start].Count(c => c == '\n') + 1);
    }

    private static string CommentScope(string source, int attributeStart, int declarationStart)
    {
        int before = source.LastIndexOf('\n', Math.Max(0, attributeStart - 1));
        while (before > 0)
        {
            int prior = source.LastIndexOf('\n', before - 1);
            string line = source[(prior + 1)..before].Trim();
            if (!line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith("*", StringComparison.Ordinal)
                && line != "/*" && line != "") break;
            before = prior;
        }

        int open = source.IndexOf('{', declarationStart);
        int end = open < 0 ? declarationStart : MatchingBrace(source, open);
        string block = source[Math.Max(0, before)..Math.Min(source.Length, end + 1)];
        return string.Join("\n", block.Split('\n').Where(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            || line.TrimStart().StartsWith("*", StringComparison.Ordinal)));
    }

    private static int MatchingBrace(string source, int open)
    {
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}' && --depth == 0) return i;
        }
        return source.Length - 1;
    }

    private static string[] Distinct(MatchCollection matches) => matches.Select(match => match.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

    private static string EscapeCsv(string value) => value.Contains('"') || value.Contains(',') || value.Contains('\n')
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private static List<string> ParseCsv(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"')
            {
                field.Append('"');
                i++;
            }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else field.Append(c);
        }
        fields.Add(field.ToString());
        return fields;
    }
}
