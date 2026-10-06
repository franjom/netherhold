// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;

namespace DK2.Tools.Commands;

internal static class CoverageCommand
{
    internal static int Run(string[] args)
    {
        // Which research is built, measured rather than asserted: HANDOFF.md's
        // hand-maintained list called combat absent after it had landed.
        //
        // Only a STALE CLAIM fails -- a doc naming a type the code lacks. A doc
        // naming no implementation is RESEARCH ONLY, legitimate, and counted.
        string? covRoot = Repo.FindRepoRoot();
        if (covRoot is null)
        {
            Console.Error.WriteLine("not inside the repository (no DK2.slnx above the working directory).");
            return 2;
        }

        bool covCheck = args.Contains("--check");
        bool covAll = args.Contains("--all");

        // Every identifier the source uses, not only the types it declares:
        // docs cite members and folders as freely as types, and a
        // declarations-only set reported thirty false positives. A checker that
        // cries wolf is one nobody runs. All three areas, because the Unity
        // adapter and the tests are implementation the docs legitimately cite.
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (string area in new[] { "src", "unity", "tests" })
        {
            string dir = Path.Combine(covRoot, area);
            if (!Directory.Exists(dir)) continue;
            foreach (string cs in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || cs.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(cs), @"[A-Za-z_][A-Za-z0-9_]*"))
                    declared.Add(m.Value);
                declared.Add(Path.GetFileNameWithoutExtension(cs));
            }
        }

        // A doc names its implementation on a line carrying one of these
        // markers. "Nothing here is built" is a claim too, and an honest one.
        var claimLine = new Regex(
            @"(\*\*)?Implementation(\*\*)?\s*[:.|]|^\s*\|\s*Implementation\s*\||\*\*Built\b|\bis built\b|\bare built\b|\bBuilt as\b",
            RegexOptions.IgnoreCase);
        var saysUnbuilt = new Regex(
            @"nothing here is built|nothing is built|is not built|are not built|not yet built|\bunbuilt\b|\bunimplemented\b",
            RegexOptions.IgnoreCase);

        var stale = new List<string>();
        var researchOnly = new List<string>();
        int builtDocs = 0;

        foreach (string doc in Directory
                     .GetFiles(Path.Combine(covRoot, "docs", "research"), "*.md")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(doc);

            // The question log is a backlog, not a claim of work done.
            if (name == "open-questions.md") continue;

            string[] lines = File.ReadAllLines(doc);

            // A claim is either inline ("Implementation: `X`, `Y`") or a heading
            // with the types in the prose under it. Reading only the marker line
            // called eating.md and training.md unbuilt, which they are not, so a
            // heading opens a short window instead.
            var claimLines = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], @"^#{1,6}\s+.*\bImplementation\b", RegexOptions.IgnoreCase))
                    claimLines.AddRange(lines.Skip(i + 1).Take(6));
                else if (claimLine.IsMatch(lines[i]))
                    claimLines.Add(lines[i]);
            }

            var named = new List<string>();
            foreach (string line in claimLines)
            {
                foreach (Match tick in Regex.Matches(line, @"`([^`]+)`"))
                {
                    string cited = tick.Groups[1].Value.Trim();
                    // A claim cites `Type`, `Namespace.Type`, `Folder/Type` or
                    // `Type.Member`. Any segment resolving is enough: the doc is
                    // pointing at real code either way.
                    if (!Regex.IsMatch(cited, @"^[A-Za-z_][A-Za-z0-9_./\\]*$")) continue;
                    // Ghidra's own names for the original's code are evidence,
                    // never our types: a doc citing FUN_004dbf90 is pointing at
                    // DKII.EXE, so it must not read as an unbuilt claim.
                    if (Regex.IsMatch(cited, @"^(FUN|DAT|LAB|SUB|UNK|PTR|thunk)_")) continue;
                    string[] parts = cited.Split('/', '\\', '.');
                    if (!parts.Any(p => p.Length >= 4 && char.IsUpper(p[0])
                                        && !p.All(c => char.IsUpper(c) || c == '_')))
                        continue;
                    named.Add(cited);
                }
            }

            var missing = named.Distinct(StringComparer.Ordinal)
                .Where(c => !c.Split('/', '\\', '.').Any(declared.Contains))
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();

            if (named.Count == 0)
            {
                bool honest = lines.Any(l => saysUnbuilt.IsMatch(l));
                researchOnly.Add($"{name}{(honest ? " (says so)" : "")}");
            }
            else if (missing.Length > 0)
                stale.Add($"{name}: names {string.Join(", ", missing)}, absent from src/");
            else
                builtDocs++;
        }

        // One fewer than `status` counts: the question log is a backlog, not a
        // claim, and saying so here stops the two numbers reading as a defect.
        Console.WriteLine($"research docs: {builtDocs + stale.Count + researchOnly.Count}"
            + " (open-questions.md excluded)"
            + $"  built: {builtDocs}  research only: {researchOnly.Count}  stale claims: {stale.Count}");

        if (covAll && researchOnly.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Research with no implementation named:");
            foreach (string r in researchOnly) Console.WriteLine($"  {r}");
        }

        if (stale.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No document claims a type that src/ does not declare.");
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"{stale.Count} stale implementation claim(s):");
        foreach (string s in stale) Console.Error.WriteLine($"  {s}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("A doc naming a type src/ does not declare is claiming work that");
        Console.Error.WriteLine("is not there. Fix the claim or build the type.");
        return covCheck ? 1 : 0;
    }
}
