// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text.RegularExpressions;

namespace DK2.Tools.Commands;

internal static class DocsCommand
{
    internal static int Run(string[] args)
    {
        // Budget checker for AGENTS.md's recording rules.
        //
        // It checks only what you CHANGED, because 161 of 234 question rows are
        // already over budget and a checker that reports all of them would be
        // switched off within a day. --all reports the backlog separately.
        string? docsRoot = Repo.FindRepoRoot();
        if (docsRoot is null)
        {
            Console.Error.WriteLine("not inside the repository (no DK2.slnx above the working directory).");
            return 2;
        }

        bool all = args.Contains("--all");
        string baseRef = Repo.OptionValue(args, "--since") ?? "HEAD";
        string? messagePath = Repo.OptionValue(args, "--message");

        // --message means a commit-msg hook, and a hook must judge the commit,
        // not the tree it happens to sit in. Without this, another session's
        // unstaged edit blocks a commit that does not touch it -- which happened,
        // and the only way past it was --no-verify.
        bool staged = args.Contains("--staged")
            || (messagePath is not null && !all && Repo.OptionValue(args, "--since") is null);

        var findings = new List<string>();

        void Report(string file, string what, int actual, int budget)
            => findings.Add($"{file}: {what} is {actual}, budget {budget}");

        // A commit message, for a commit-msg hook. The subject line is exempt;
        // the trailer is not the author's prose either.
        if (messagePath is not null && File.Exists(messagePath))
        {
            string[] msg = File.ReadAllLines(messagePath);
            int body = CommitBodyLines(msg);
            if (body > DocBudgets.CommitBody)
                Report(Path.GetFileName(messagePath), "commit body", body, DocBudgets.CommitBody);
        }

        IEnumerable<string> files = all ? Repo.AllTrackedFiles(docsRoot)
            : staged ? Repo.StagedFiles(docsRoot)
            : Repo.ChangedFiles(docsRoot, baseRef);

        foreach (string relative in files)
        {
            string full = Path.Combine(docsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!staged && !File.Exists(full)) continue;

            string[] lines = staged
                ? Repo.GitLines(docsRoot, $":{relative}")
                : File.ReadAllLines(full);

            // Whole-file measures only fail when the change made them worse. A
            // one-line fix to a doc that was already long is not the moment to
            // demand it be split, and a checker that says otherwise is one
            // nobody runs.
            string[] baseLines = all ? [] : Repo.GitLines(docsRoot, $"{baseRef}:{relative}");
            HashSet<int>? touched = all
                ? null
                : Repo.AddedLineNumbers(docsRoot, baseRef, relative, staged);

            CheckFile(relative, lines, baseLines, all, touched, findings);
        }

        if (findings.Count == 0)
        {
            Console.WriteLine(all
                ? "no budget violations anywhere."
                : $"no budget violations in what changed since {baseRef}.");
            return 0;
        }

        Console.Error.WriteLine(all
            ? $"{findings.Count} budget violation(s) in the tree:"
            : $"{findings.Count} budget violation(s) in what changed since {baseRef}:");
        foreach (string f in findings.OrderBy(x => x, StringComparer.Ordinal))
            Console.Error.WriteLine($"  {f}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("See AGENTS.md, \"what to record, and how much\". Over budget means");
        Console.Error.WriteLine("the material belongs somewhere else -- not that it should be squeezed.");
        return 1;
    }

    internal static int CommitBodyLines(string[] msg)
        => msg.Skip(1)
            .Where(l => !l.StartsWith("Co-Authored-By:", StringComparison.Ordinal))
            .Where(l => !l.StartsWith('#'))
            .Count(l => l.Length > 0);

    internal static void CheckFile(string relative, string[] lines, string[] baseLines, bool all,
        HashSet<int>? touched, List<string> findings)
    {
        void Report(string file, string what, int actual, int budget)
            => findings.Add($"{file}: {what} is {actual}, budget {budget}");
        bool Worse(int now, int before) => all || now > before;
        bool Touched(int from, int to)
            => touched is null || Enumerable.Range(from, to - from + 1).Any(touched.Contains);

        // Whole-file budgets.
        if (relative.Equals("docs/HANDOFF.md", StringComparison.OrdinalIgnoreCase)
            && lines.Length > DocBudgets.Handoff
            && Worse(lines.Length, baseLines.Length))
            Report(relative, "length", lines.Length, DocBudgets.Handoff);

        // A claim that something has NOT happened rots the moment it does.
        // HANDOFF said the input channel awaited its first capture five days
        // after two runs closed it, and that sentence was repeated to the
        // owner as a request for work already done. A date does not make the
        // claim true; it makes the staleness visible to the next reader.
        if (relative.Equals("docs/HANDOFF.md", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i],
                        @"\b(never\s+(been|sampled|run|measured|tried|captured|solved)|not yet|awaits|still no |still carry no)",
                        RegexOptions.IgnoreCase))
                    continue;
                if (Regex.IsMatch(lines[i], @"\b20\d\d-\d\d-\d\d\b")) continue;

                // The paragraph that documents this rule quotes the words
                // it matches on, and a checker that fails its own
                // description is one somebody deletes.
                if (lines[i].Contains("docs --check", StringComparison.Ordinal)) continue;
                if (!Touched(i + 1, i + 1)) continue;
                findings.Add($"{relative}:{i + 1}: a 'not done yet' claim with no date on it");
            }
        }

        if (relative.StartsWith("docs/research/", StringComparison.OrdinalIgnoreCase)
            && relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && !relative.EndsWith("open-questions.md", StringComparison.OrdinalIgnoreCase)
            && !relative.EndsWith("functions/INDEX.md", StringComparison.OrdinalIgnoreCase)
            && lines.Length > DocBudgets.ResearchDoc
            && Worse(lines.Length, baseLines.Length))
            Report(relative, "length", lines.Length, DocBudgets.ResearchDoc);

        // One row per question. Long rows are this repo's worst offender and
        // they are always append-accretion, never one long thought.
        if (relative.EndsWith("open-questions.md", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\| Q\d+[a-z]? \|")) continue;
                if (!Touched(i + 1, i + 1)) continue;
                if (lines[i].Length > DocBudgets.QuestionRow)
                    Report($"{relative}:{i + 1}", "question row", lines[i].Length, DocBudgets.QuestionRow);
            }
        }

        // An ADR is the decision and the reason; the evidence lives in the
        // research doc. Measured per section, not per file.
        if (relative.EndsWith("DECISIONS.md", StringComparison.OrdinalIgnoreCase))
        {
            var starts = new List<int>();
            for (int i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], @"^#{2,3} ADR-\d{4}")) starts.Add(i);

            for (int k = 0; k < starts.Count; k++)
            {
                int from = starts[k];
                int to = (k + 1 < starts.Count ? starts[k + 1] : lines.Length) - 1;
                if (!Touched(from + 1, to + 1)) continue;
                int length = to - from + 1;
                if (length > DocBudgets.Adr)
                    Report($"{relative}:{from + 1}", Trim(lines[from]), length, DocBudgets.Adr);
            }
        }

        if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
        if (relative.Contains("/obj/") || relative.Contains("/bin/")) return;

        // Comment runs. A doc comment past its budget is a research note in
        // the wrong file; an inline comment past its budget is narration.
        int runStart = -1;
        bool runIsDoc = false;
        for (int i = 0; i <= lines.Length; i++)
        {
            string t = i < lines.Length ? lines[i].TrimStart() : string.Empty;
            bool doc = t.StartsWith("///", StringComparison.Ordinal);
            bool inline = !doc && t.StartsWith("//", StringComparison.Ordinal);

            if ((doc || inline) && runStart >= 0 && doc == runIsDoc) continue;

            if (runStart >= 0)
            {
                int length = i - runStart;
                int budget = runIsDoc ? DocBudgets.DocComment : DocBudgets.InlineComment;
                if (length > budget && Touched(runStart + 1, i))
                    Report($"{relative}:{runStart + 1}",
                           runIsDoc ? "doc comment" : "inline comment", length, budget);
            }

            runStart = doc || inline ? i : -1;
            runIsDoc = doc;
        }

        // Ratio, whole-file: the check that catches a file explaining itself
        // rather than one oversized block.
        int comments = lines.Count(l =>
        {
            string t = l.TrimStart();
            return t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith("*", StringComparison.Ordinal)
                || t.StartsWith("/*", StringComparison.Ordinal);
        });
        if (lines.Length >= 80)
        {
            int percent = comments * 100 / lines.Length;
            int baseComments = baseLines.Count(l =>
            {
                string t = l.TrimStart();
                return t.StartsWith("//", StringComparison.Ordinal)
                    || t.StartsWith("*", StringComparison.Ordinal)
                    || t.StartsWith("/*", StringComparison.Ordinal);
            });
            int basePercent = baseLines.Length > 0
                ? baseComments * 100 / baseLines.Length
                : 0;
            if (percent > DocBudgets.CommentPercent && Worse(percent, basePercent))
                Report(relative, "comment lines", percent, DocBudgets.CommentPercent);
        }

        static string Trim(string heading)
            => heading.Length <= 40 ? heading : heading[..40] + "...";
    }
}

/// <summary>
/// AGENTS.md's recording budgets, in one place so the rules and the checker
/// cannot drift apart. Changing one here without changing AGENTS.md is a bug.
/// </summary>
static class DocBudgets
{
    public const int QuestionRow = 800;
    public const int DocComment = 12;
    public const int InlineComment = 6;
    public const int CommentPercent = 40;
    public const int CommitBody = 15;
    public const int ResearchDoc = 400;
    public const int Adr = 50;
    public const int Handoff = 500;
}
