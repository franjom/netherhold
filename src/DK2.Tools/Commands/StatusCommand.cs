// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DK2.Tools.Commands;

internal static class StatusCommand
{
    internal static int Run(string[] args)
    {
        // Regenerates the plan's status paragraph instead of anybody typing it.
        // Every figure below is read from a file or from test discovery; none of
        // it is a constant in this source. --fast skips test discovery, the only
        // slow part; --check exits 1 if the plan's committed line disagrees, so a
        // hook can gate a commit on it.
        bool fast = args.Contains("--fast");
        bool check = args.Contains("--check");

        string? root = Repo.FindRepoRoot();
        if (root is null)
        {
            Console.Error.WriteLine("not inside the repository (no DK2.slnx above the working directory).");
            return 2;
        }

        var problems = new List<string>();

        string Read(string relative)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return File.ReadAllText(path);
            problems.Add($"missing file: {relative}");
            return string.Empty;
        }

        // --- ADRs. Distinct ids, because some are recorded as sub-headings and
        // counting headings double-counts amendments.
        string decisions = Read("docs/DECISIONS.md");
        int[] adrIds = Regex.Matches(decisions, @"^#{2,3} ADR-(\d{4})", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct().OrderBy(n => n).ToArray();

        // --- research docs
        string researchDir = Path.Combine(root, "docs", "research");
        int researchDocs = Directory.Exists(researchDir)
            ? Directory.GetFiles(researchDir, "*.md").Length
            : Fail("docs/research is missing");

        // --- open questions. Rows, not the highest id: ids get retired.
        string questions = Read("docs/research/open-questions.md");
        // The letter suffix is not decoration: Q37a, Q37b, Q55x and Q118c are
        // real rows split off from an original question. A regex without it
        // undercounts by four, and a plain "starts with | Q" overcounts by three
        // because the file has three table headers reading "| Q | Question |".
        MatchCollection rows = Regex.Matches(
            questions, @"^\| Q(\d+)[a-z]? \|", RegexOptions.Multiline);
        int questionRows = rows.Count;
        int highestQuestion = rows.Count == 0
            ? 0
            : rows.Select(m => int.Parse(m.Groups[1].Value)).Max();

        // --- scenarios, counted from their definitions rather than from a
        // build output directory that a stale run would inflate.
        string scenarioDir = Path.Combine(root, "scenarios");
        int scenarios = Directory.Exists(scenarioDir)
            ? Directory.GetFiles(scenarioDir, "*.yaml", SearchOption.AllDirectories).Length
            : Fail("scenarios/ is missing");

        // --- the determinism pair, from the two files that own them.
        string sim = Read("src/DK2.Simulation/GameSimulation.cs");
        Match hashVersion = Regex.Match(sim, @"HashVersion\s*=\s*(\d+)");
        if (!hashVersion.Success) problems.Add("could not find HashVersion in GameSimulation.cs");

        // The LIVE assertion, not the commented history of previous values.
        string determinism = Read("tests/DK2.Simulation.Tests/DeterminismTests.cs");
        Match pin = Regex.Match(determinism, @"Assert\.Equal\((0x[0-9A-Fa-f]{16})UL,\s*sim\.StateHash\(\)\)");
        if (!pin.Success) problems.Add("could not find the pinned hash assertion in DeterminismTests.cs");

        // --- tests. Discovery agrees with the runner exactly, and takes about
        // four seconds, so there is no reason to approximate it by counting
        // [Fact] attributes -- a [Theory] would make that wrong anyway.
        int tests = -1, gated = -1;
        if (!fast)
        {
            (tests, gated) = CountTests(root, problems);
        }

        // ---- report ------------------------------------------------------
        Console.WriteLine("computed from the working tree:");
        Console.WriteLine();
        if (!fast)
        {
            Console.WriteLine($"  tests                 {tests}");
            Console.WriteLine($"  ... needing the data  {gated}");
        }
        else
        {
            Console.WriteLine("  tests                 (skipped: --fast)");
        }
        Console.WriteLine($"  scenarios             {scenarios}");
        Console.WriteLine($"  ADRs                  {adrIds.Length}   {DescribeRuns(adrIds)}");
        Console.WriteLine($"  reserved ADR ids      {DescribeGaps(adrIds)}");
        Console.WriteLine($"  research docs         {researchDocs}");
        Console.WriteLine($"  question rows         {questionRows}, highest Q{highestQuestion}");
        Console.WriteLine($"  HashVersion           {(hashVersion.Success ? hashVersion.Groups[1].Value : "?")}");
        Console.WriteLine($"  determinism pin       {(pin.Success ? pin.Groups[1].Value : "?")}");
        Console.WriteLine();

        string testClause = fast
            ? "(test count not measured)"
            : $"**{tests} tests** (of which **{gated} need the game data and SKIP "
              + "without it** — ADR-0100)";

        string paragraph =
            $"**Status ({DateTime.Now:yyyy-MM-dd}, regenerated by `dk2tools status`).** "
            + $"{testClause}, {scenarios} scenarios, **{adrIds.Length} ADRs** "
            + $"({DescribeRuns(adrIds)}; {DescribeGaps(adrIds)} reserved), "
            + $"{researchDocs} research docs, {questionRows} open-question rows "
            + $"running to Q{highestQuestion}, `HashVersion` "
            + $"**{(hashVersion.Success ? hashVersion.Groups[1].Value : "?")}**, "
            + $"pin `{(pin.Success ? pin.Groups[1].Value : "?")}`.";

        Console.WriteLine("paste this into DK2_REIMPLEMENTATION_PLAN.md (regenerate, do not edit):");
        Console.WriteLine();
        Console.WriteLine(paragraph);
        Console.WriteLine();

        foreach (string problem in problems)
            Console.Error.WriteLine($"warning: {problem}");

        if (!check)
            return problems.Count == 0 ? 0 : 1;

        // --check: compare against what the plan currently claims. Only the
        // numbers are compared, because the prose around them is edited by
        // hand on purpose.
        string plan = Read("DK2_REIMPLEMENTATION_PLAN.md");
        Match claimed = Regex.Match(
            plan,
            @"\*\*Status \([^)]*\)\.\*\*(?<body>.*?)(?:\r?\n){2}",
            RegexOptions.Singleline);
        if (!claimed.Success)
        {
            Console.Error.WriteLine("could not find a status paragraph in the plan to check.");
            return 2;
        }

        string body = claimed.Groups["body"].Value;
        var stale = new List<string>();
        void Expect(string label, string? actual)
        {
            if (actual is null) return;
            if (!Regex.IsMatch(body, @"(?<![0-9A-Za-zx])" + Regex.Escape(actual) + @"(?![0-9A-Za-z])"))
                stale.Add($"{label}: the plan does not mention {actual}");
        }

        if (!fast) Expect("tests", tests.ToString());
        Expect("scenarios", scenarios.ToString());
        Expect("ADRs", adrIds.Length.ToString());
        Expect("research docs", researchDocs.ToString());
        Expect("question rows", questionRows.ToString());
        Expect("highest question", $"Q{highestQuestion}");
        if (hashVersion.Success) Expect("HashVersion", hashVersion.Groups[1].Value);
        if (pin.Success) Expect("pin", pin.Groups[1].Value);

        if (stale.Count == 0)
        {
            Console.WriteLine("--check: the plan's status line agrees with the tree.");
            return 0;
        }

        Console.Error.WriteLine("--check: the plan's status line is STALE:");
        foreach (string s in stale) Console.Error.WriteLine($"  {s}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Regenerate it with `dk2tools status` rather than editing the numbers.");
        return 1;

        int Fail(string message)
        {
            problems.Add(message);
            return 0;
        }
    }

    /// <summary>
    /// The test total, and how many of them are gated on the game installation.
    ///
    /// <para><b>Discovery rather than a source count.</b> Counting
    /// <c>[Fact]</c> attributes would be wrong the moment a <c>[Theory]</c> carries
    /// more than one <c>[InlineData]</c>, and it is the runner's number that the
    /// status line has always quoted. <c>--list-tests</c> agrees with the runner
    /// exactly and costs about four seconds.</para>
    /// </summary>
    internal static (int Total, int Gated) CountTests(string root, List<string> problems)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in new[] { "test", "DK2.slnx", "-c", "Release", "--no-build", "--list-tests", "-m:1" })
            psi.ArgumentList.Add(a);

        string output;
        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                problems.Add("could not start dotnet for test discovery");
                return (-1, -1);
            }
            Task<string> errors = process.StandardError.ReadToEndAsync();
            output = process.StandardOutput.ReadToEnd();
            errors.Wait();
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            problems.Add($"test discovery failed: {ex.Message}");
            return (-1, -1);
        }

        var discovered = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (Regex.IsMatch(trimmed, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+(\(.*\))?$"))
                discovered.Add(trimmed);
        }

        if (discovered.Count == 0)
        {
            problems.Add("test discovery returned nothing - build first: "
                         + "dotnet build DK2.slnx -c Release");
            return (-1, -1);
        }

        // Which of them need the installation. Read off the attribute in the
        // source, keyed on Class.Method so that a method of the same name in
        // another class cannot inflate the count.
        var gatedMethods = new HashSet<string>(StringComparer.Ordinal);
        string testsDir = Path.Combine(root, "tests");
        if (Directory.Exists(testsDir))
        {
            foreach (string file in Directory.GetFiles(testsDir, "*.cs", SearchOption.AllDirectories))
            {
                string currentClass = string.Empty;
                bool armed = false;
                foreach (string raw in File.ReadLines(file))
                {
                    string line = raw.Trim();

                    Match cls = Regex.Match(
                        line,
                        @"^(?:public |internal |sealed |abstract |partial |static )*class ([A-Za-z_][A-Za-z0-9_]*)");
                    if (cls.Success)
                    {
                        currentClass = cls.Groups[1].Value;
                        armed = false;
                        continue;
                    }

                    if (line.StartsWith("[Install", StringComparison.Ordinal))
                    {
                        armed = true;
                        continue;
                    }

                    if (!armed) continue;

                    Match method = Regex.Match(line, @"(?:void|Task) ([A-Za-z_][A-Za-z0-9_]*)\s*\(");
                    if (method.Success)
                    {
                        gatedMethods.Add($"{currentClass}.{method.Groups[1].Value}");
                        armed = false;
                    }
                }
            }
        }
        else
        {
            problems.Add("tests/ is missing");
        }

        int gated = 0;
        foreach (string id in discovered)
        {
            string name = id;
            int paren = name.IndexOf('(', StringComparison.Ordinal);
            if (paren >= 0) name = name[..paren];

            string[] parts = name.Split('.');
            if (parts.Length < 2) continue;
            if (gatedMethods.Contains($"{parts[^2]}.{parts[^1]}")) gated++;
        }

        return (discovered.Count, gated);
    }

    /// <summary>
    /// Contiguous runs of ADR ids, so a gap reads as a gap rather than as a
    /// miscount. The status line has been wrong about this before: ADRs were once
    /// counted by heading, which double-counted amendments.
    /// </summary>
    internal static string DescribeRuns(int[] ids)
    {
        if (ids.Length == 0) return "none";

        var parts = new List<string>();
        int start = ids[0], prev = ids[0];
        foreach (int id in ids.Skip(1))
        {
            if (id == prev + 1) { prev = id; continue; }
            parts.Add(Run(start, prev));
            start = prev = id;
        }
        parts.Add(Run(start, prev));
        return string.Join(" plus ", parts);

        static string Run(int a, int b) =>
            a == b ? $"ADR-{a:D4}" : $"ADR-{a:D4} \u2026 ADR-{b:D4}";
    }

    /// <summary>The ids skipped between runs, which are the reserved ones.</summary>
    internal static string DescribeGaps(int[] ids)
    {
        var parts = new List<string>();
        for (int i = 1; i < ids.Length; i++)
        {
            int lo = ids[i - 1] + 1, hi = ids[i] - 1;
            if (hi < lo) continue;
            parts.Add(lo == hi ? $"{lo:D4}" : $"{lo:D4}\u2013{hi:D4}");
        }
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }
}
