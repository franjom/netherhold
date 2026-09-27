// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Text.RegularExpressions;
using DK2.Assets;
using DK2.Convert;
using DK2.Core;
using DK2.Convert.Audio;
using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Data.Wad;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.Tools;

// DK2.Tools — command-line utilities for inspecting original game data.

if (args.Length == 0)
    return Usage();

switch (args[0])
{
    case "capture-diff" when args.Length is 3 or 5:
        return CaptureDiff(args);

    case "trigger-diff" when args.Length == 3:
        return DiffTriggerCsv(args[1], args[2]);

    case "trigger-run" when args.Length == 4:
        return RunTriggerCsv(
            args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3]);

    case "replay" when args.Length >= 4:
        return Replay(args);

    case "sgmap":
        return SgmapCommand.Run(args[1..], FindRepoRoot());

    case "verification-ledger":
        return VerificationLedger.Run(args[1..], FindRepoRoot());

    case "where":
    {
        string? dir = Dk2Install.Find();
        if (dir is null)
        {
            Console.Error.WriteLine("DK2 installation not found (set DK2_DIR).");
            return 2;
        }
        Console.WriteLine(dir);
        return 0;
    }

    case "status":
    {
        // Regenerates the plan's status paragraph instead of anybody typing it.
        // Every figure below is read from a file or from test discovery; none of
        // it is a constant in this source. --fast skips test discovery, the only
        // slow part; --check exits 1 if the plan's committed line disagrees, so a
        // hook can gate a commit on it.
        bool fast = args.Contains("--fast");
        bool check = args.Contains("--check");

        string? root = FindRepoRoot();
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

    case "docs":
    {
        // Budget checker for AGENTS.md's recording rules.
        //
        // It checks only what you CHANGED, because 161 of 234 question rows are
        // already over budget and a checker that reports all of them would be
        // switched off within a day. --all reports the backlog separately.
        string? docsRoot = FindRepoRoot();
        if (docsRoot is null)
        {
            Console.Error.WriteLine("not inside the repository (no DK2.slnx above the working directory).");
            return 2;
        }

        bool all = args.Contains("--all");
        string baseRef = OptionValue(args, "--since") ?? "HEAD";
        string? messagePath = OptionValue(args, "--message");

        // --message means a commit-msg hook, and a hook must judge the commit,
        // not the tree it happens to sit in. Without this, another session's
        // unstaged edit blocks a commit that does not touch it -- which happened,
        // and the only way past it was --no-verify.
        bool staged = args.Contains("--staged")
            || (messagePath is not null && !all && OptionValue(args, "--since") is null);

        var findings = new List<string>();

        void Report(string file, string what, int actual, int budget)
            => findings.Add($"{file}: {what} is {actual}, budget {budget}");

        // A commit message, for a commit-msg hook. The subject line is exempt;
        // the trailer is not the author's prose either.
        if (messagePath is not null && File.Exists(messagePath))
        {
            string[] msg = File.ReadAllLines(messagePath);
            int body = msg.Skip(1)
                .Where(l => !l.StartsWith("Co-Authored-By:", StringComparison.Ordinal))
                .Where(l => !l.StartsWith('#'))
                .Count(l => l.Length > 0);
            if (body > DocBudgets.CommitBody)
                Report(Path.GetFileName(messagePath), "commit body", body, DocBudgets.CommitBody);
        }

        IEnumerable<string> files = all ? AllTrackedFiles(docsRoot)
            : staged ? StagedFiles(docsRoot)
            : ChangedFiles(docsRoot, baseRef);

        foreach (string relative in files)
        {
            string full = Path.Combine(docsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!staged && !File.Exists(full)) continue;

            string[] lines = staged
                ? GitLines(docsRoot, $":{relative}")
                : File.ReadAllLines(full);

            // Whole-file measures only fail when the change made them worse. A
            // one-line fix to a doc that was already long is not the moment to
            // demand it be split, and a checker that says otherwise is one
            // nobody runs.
            string[] baseLines = all ? [] : GitLines(docsRoot, $"{baseRef}:{relative}");
            bool Worse(int now, int before) => all || now > before;
            HashSet<int>? touched = all
                ? null
                : AddedLineNumbers(docsRoot, baseRef, relative, staged);
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

            if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;

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

        static string Trim(string heading)
            => heading.Length <= 40 ? heading : heading[..40] + "...";
    }

    case "coverage":
    {
        // Which research is built, measured rather than asserted: HANDOFF.md's
        // hand-maintained list called combat absent after it had landed.
        //
        // Only a STALE CLAIM fails -- a doc naming a type the code lacks. A doc
        // naming no implementation is RESEARCH ONLY, legitimate, and counted.
        string? covRoot = FindRepoRoot();
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

    case "convert":
    {
        int outOption = Array.IndexOf(args, "--out");
        if (outOption < 0 || outOption + 1 >= args.Length || outOption + 2 != args.Length)
        {
            Console.Error.WriteLine("usage: dk2tools convert --out converted-assets/");
            return 2;
        }

        string output = RequireConvertedOutput(args[outOption + 1]);
        string install = Dk2Install.Require();
        GameDatabase database = GameDatabaseLoader.Load(install);
        IReadOnlyList<AssetConversionResult> results =
            AssetConverter.ConvertAll(install, database, output);
        FileAssetProvider provider = FileAssetProvider.Converted(output);
        foreach (AssetConversionResult result in results)
        {
            if (result.Produced && !provider.TryResolve(result.Reference, out _))
                throw new InvalidOperationException(
                    $"Converter reported '{result.Reference.Key}' but FileAssetProvider cannot resolve it.");
            string status = result.Produced ? "PRODUCED" : "MISSING ";
            string destination = result.OutputPath is null
                ? result.Detail
                : $"{Path.GetRelativePath(output, result.OutputPath)} ({result.Detail})";
            Console.WriteLine($"{status} {result.Reference.Key} -> {destination}");
        }
        int produced = results.Count(result => result.Produced);
        Console.WriteLine($"-- produced {produced}, could not produce {results.Count - produced}, " +
                          $"catalogue total {results.Count}");
        return 0;
    }

    case "wad" when args.Length >= 3 && args[1] == "list":
    {
        using var wad = WadArchive.Open(ResolveData(args[2]));
        foreach (WadEntry e in wad.Entries)
            Console.WriteLine($"{e.UncompressedSize,10} {(e.IsCompressed ? "C" : " ")} {e.Name}");
        Console.WriteLine($"-- {wad.Entries.Count} entries");
        return 0;
    }

    case "wad" when args.Length >= 4 && args[1] == "extract":
    {
        using var wad = WadArchive.Open(ResolveData(args[2]));
        string entryName = args[3];
        string outPath = args.Length > 4 ? args[4] : Path.GetFileName(entryName.Replace('\\', '/'));
        byte[] data = wad.Extract(entryName);
        File.WriteAllBytes(outPath, data);
        Console.WriteLine($"{entryName} -> {outPath} ({data.Length} bytes)");
        return 0;
    }

    case "sdt" when args.Length == 3 && args[1] == "list":
    {
        SdtArchive sdt = SdtArchive.Open(ResolveData(args[2]));
        foreach (SdtEntry entry in sdt.Entries)
        {
            string channels = entry.ChannelCount is int count ? $"{count}ch" : "?ch";
            string rate = entry.PayloadSampleRate is ushort payloadRate
                && payloadRate != entry.SampleRate
                ? $"{entry.SampleRate}Hz(stream {payloadRate}Hz)"
                : $"{entry.SampleRate}Hz";
            Console.WriteLine($"{entry.Index,4} {entry.StoredSize,10} {rate,-23} " +
                              $"{entry.BitsPerSample,2}bit {channels,-3} {entry.Codec,-11} {entry.Name}");
        }
        Console.WriteLine($"-- {sdt.Entries.Count} entries");
        return 0;
    }

    case "sdt" when args.Length is 3 or 5 && args[1] == "extract":
    {
        if (args.Length == 5 && args[3] != "--out")
        {
            Console.Error.WriteLine(
                "usage: dk2tools sdt extract <file.sdt> [--out converted-assets/audio]");
            return 2;
        }
        string output = RequireConvertedOutput(args.Length == 5
            ? args[4] : Path.Combine("converted-assets", "audio"));
        Directory.CreateDirectory(output);
        SdtArchive sdt = SdtArchive.Open(ResolveData(args[2]));
        int produced = 0;
        foreach (SdtEntry entry in sdt.Entries)
        {
            if (entry.Codec == SdtCodec.Empty)
            {
                Console.WriteLine($"SKIPPED  {entry.Index,4} {entry.Name} (empty placeholder)");
                continue;
            }
            string destination = Path.Combine(output, sdt.OutputFileName(entry));
            byte[] bytes = sdt.Extract(entry);
            File.WriteAllBytes(destination, bytes);
            string format = entry.Codec switch
            {
                SdtCodec.Pcm => "PCM in WAV",
                SdtCodec.MpegLayer1 => "raw MPEG Audio Layer I stream",
                SdtCodec.MpegLayer2 => "raw MPEG Audio Layer II stream",
                SdtCodec.MpegLayer3 => "raw MPEG Audio Layer III stream",
                _ => $"raw undecoded type {entry.Type}",
            };
            Console.WriteLine($"EXTRACTED {entry.Index,4} {entry.Name} -> " +
                              $"{Path.GetRelativePath(output, destination)} ({format}, {bytes.Length} bytes)");
            produced++;
        }
        Console.WriteLine($"-- extracted {produced}, skipped {sdt.Entries.Count - produced}");
        return 0;
    }

    case "str" when args.Length == 3 && args[1] == "list":
    {
        Dk2StringTable table = Dk2StringTable.Open(ResolveText(args[2]));
        foreach (Dk2StringEntry entry in table.Entries)
            Console.WriteLine($"{entry.Id,6} {EscapeForOneLine(entry.Value)}");
        Console.WriteLine($"-- {table.Entries.Count} entries, file id {table.FileId}");
        return 0;
    }

    case "str" when args.Length == 4 && args[1] == "get":
    {
        if (!uint.TryParse(args[3], out uint id))
        {
            Console.Error.WriteLine($"invalid string id: '{args[3]}'");
            return 2;
        }
        Dk2StringTable table = Dk2StringTable.Open(ResolveText(args[2]));
        if (!table.TryResolve(id, out string value))
        {
            Console.Error.WriteLine($"string id {id} is not in this table");
            return 2;
        }
        Console.WriteLine(value);
        return 0;
    }

    case "kwd" when args.Length >= 3 && args[1] == "chunks":
    {
        foreach (KwdChunk c in KwdReader.ReadFile(ResolveEditor(args[2])))
        {
            // The stride is the structural check every record layout has to pass:
            // a field list that does not sum to it is wrong. Prints "varies" for
            // the two tagged types, whose records carry their own size instead.
            string stride =
                c.ItemCount <= 0 ? "n/a"
                : KwdTaggedRecords.IsTagged(c.Type) ? "varies (tagged records)"
                : c.Content.Length % c.ItemCount == 0
                    ? (c.Content.Length / c.ItemCount).ToString()
                    : $"NOT A WHOLE NUMBER ({c.Content.Length}/{c.ItemCount})";

            Console.WriteLine(
                $"{c.Type,-16} items={c.ItemCount,-6} content={c.Content.Length,-8} stride={stride}");
        }
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "portals":
    {
        // Which shipped levels have more than one portal, and who starts with
        // them: Q86 needs a level where one keeper can hold two portals at once.
        string root = Dk2Install.Require();
        string mapsDir = Path.Combine(root, "Data", "editor", "maps");
        GameDatabase db = GameDatabaseLoader.Load(root);

        // Portal terrains from the ROOMS table rather than hardcoded, so a
        // renumbered id cannot make this quietly print nothing.
        var portalTerrain = new Dictionary<byte, string>();
        foreach (RoomDef room in db.Rooms.Values)
        {
            if (room.Name.Contains("Portal", StringComparison.OrdinalIgnoreCase))
                portalTerrain[room.TerrainId] = room.Name;
        }
        if (portalTerrain.Count == 0)
        {
            Console.Error.WriteLine("no room named *Portal* in the ROOMS table.");
            return 1;
        }
        Console.WriteLine("portal terrains: " + string.Join(", ",
            portalTerrain.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
        Console.WriteLine();

        int baseGlobal = db[VariableTypeId.EntranceGenerationSpeedSeconds];
        var interesting = new List<string>();

        foreach (string file in Directory.GetFiles(mapsDir, "*Map.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Map.kld".Length];
            LevelMap? grid = MapGrid.Parse(KwdReader.ReadFile(file));
            if (grid is null) continue;

            // Flood fill each portal terrain into 4-connected regions; one
            // region is one portal, whatever its shape.
            var seen = new bool[grid.Width * grid.Height];
            var regions = new List<(string Room, byte Owner, int Tiles, int X, int Y)>();
            var stack = new Stack<int>();

            for (int start = 0; start < seen.Length; start++)
            {
                if (seen[start]) continue;
                MapTile t0 = grid.Tiles[start];
                if (!portalTerrain.TryGetValue(t0.TerrainId, out string? name)) continue;

                seen[start] = true;
                stack.Push(start);
                int tiles = 0, sx = 0, sy = 0;
                byte owner = t0.PlayerId;

                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    tiles++;
                    int x = i % grid.Width, y = i / grid.Width;
                    sx += x; sy += y;
                    // A region takes the highest player id it touches, so a
                    // portal half-owned in the file still reports an owner.
                    if (grid.Tiles[i].PlayerId > owner) owner = grid.Tiles[i].PlayerId;

                    foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (!grid.Contains(nx, ny)) continue;
                        int n = ny * grid.Width + nx;
                        if (seen[n]) continue;
                        if (grid.Tiles[n].TerrainId != t0.TerrainId) continue;
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
                regions.Add((name, owner, tiles, sx / tiles, sy / tiles));
            }

            if (regions.Count == 0) continue;

            // The level's own base, because level 1 is not the only one allowed
            // to override it even if it is the only one that does.
            // Read the level's own VARIABLES rather than asking the campaign
            // loader, which accepts only campaign level names and would
            // silently hand back the global 25 for the rest — printing "base
            // 25" for levels that plainly override it. Five levels do.
            int levelBase = baseGlobal;
            string varsFile = file[..^"Map.kld".Length] + "Variables.kld";
            if (File.Exists(varsFile))
            {
                GlobalVariables? own = KwdVariables.Parse(KwdReader.ReadFile(varsFile));
                if (own is not null &&
                    own.TryGet(VariableTypeId.EntranceGenerationSpeedSeconds, out int v))
                {
                    levelBase = v;
                }
            }

            // Player 2 is the unowned marker, not 0: level 1's portal — which
            // the tutorial has you claim — is owner 2, and level 1's keeper is
            // player 3. Player 7 is the Heroes.
            const byte Unowned = 2;
            int claimable = regions.Count(r => r.Owner == Unowned && r.Room == "Portal");
            string mark = claimable >= 2 ? " <== TWO CLAIMABLE" : "";
            Console.WriteLine($"{level,-22} base={levelBase,3}s  portals={regions.Count}{mark}");
            foreach ((string room, byte owner, int tiles, int x, int y) in
                     regions.OrderBy(r => r.X).ThenBy(r => r.Y))
            {
                Console.WriteLine(
                    $"    {room,-12} owner={owner} tiles={tiles,2} at ({x},{y})");
            }
            if (claimable >= 2) interesting.Add($"{level} (base {levelBase}s, {claimable} claimable)");
        }

        Console.WriteLine();
        Console.WriteLine("levels with two or more unowned Portals — candidates for the Q86 timing:");
        if (interesting.Count == 0) Console.WriteLine("    none");
        foreach (string s in interesting) Console.WriteLine("    " + s);
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "alterterrain":
    {
        // Q74 (can a dug-out tile become rock again?): sweeps every shipped map's
        // TRIGGERS chunk for ALTER_TERRAIN_TYPE (action type 22) and reports the
        // terrain those actions write. Payload layout and its OpenKeeper
        // provenance: docs/research/external-resources.md; the 16-byte TRIGGERS
        // payload is CONFIRMED in docs/research/kwd-record-fields.md. Guard: every
        // decoded type lands in the enumerated range, every terrain id resolves.
        string root = Dk2Install.Require();
        string mapsDir = Path.Combine(root, "Data", "editor", "maps");
        GameDatabase db = GameDatabaseLoader.Load(root);

        const int ActionTypeAlterTerrain = 22;
        const int TriggerActionTag = 214;

        var writtenTerrain = new SortedDictionary<byte, int>();
        var actionTypeHistogram = new SortedDictionary<int, int>();
        int filesScanned = 0, actionRecords = 0, alterActions = 0;
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(mapsDir, "*Triggers.kld").OrderBy(f => f))
        {
            filesScanned++;

            // The level's own MAP, so each action can be compared against what
            // the tile starts as. "Writes Rock" only means "closes a tile" if
            // the tile was open to begin with.
            string mapFile = file[..^"Triggers.kld".Length] + "Map.kld";
            LevelMap? levelGrid = File.Exists(mapFile)
                ? MapGrid.Parse(KwdReader.ReadFile(mapFile)) : null;
            foreach (KwdChunk c in KwdReader.ReadFile(file))
            {
                if (!KwdTaggedRecords.IsTagged(c.Type)) continue;

                foreach (KwdTaggedRecords.TaggedRecord r in KwdTaggedRecords.Split(c))
                {
                    if (r.Tag != TriggerActionTag) continue;
                    ReadOnlySpan<byte> p = r.Payload.Span;
                    if (p.Length < 8) continue;

                    actionRecords++;
                    int actionType = p[p.Length - 2];
                    actionTypeHistogram.TryGetValue(actionType, out int seen);
                    actionTypeHistogram[actionType] = seen + 1;

                    if (actionType != ActionTypeAlterTerrain) continue;

                    alterActions++;
                    byte terrainId = p[0];
                    writtenTerrain.TryGetValue(terrainId, out int n);
                    writtenTerrain[terrainId] = n + 1;

                    // Trigger coordinates are 1-based (OpenKeeper subtracts one
                    // before touching the map); HYPOTHESIS, and the "was" column
                    // below is what judges it.
                    int posX = p[4] | (p[5] << 8);
                    int posY = p[6] | (p[7] << 8);
                    string name = db.Terrain.TryGetValue(terrainId, out TerrainDef? t)
                        ? t.Name : "UNKNOWN TERRAIN";
                    bool solid = t is not null && t.IsSolid;

                    string was = "?";
                    bool closesATile = false;
                    if (levelGrid is not null
                        && posX >= 1 && posY >= 1
                        && posX <= levelGrid.Width && posY <= levelGrid.Height)
                    {
                        byte before = levelGrid[posX - 1, posY - 1].TerrainId;
                        TerrainDef beforeDef = db.TerrainById(before);
                        was = $"{before} {beforeDef.Name}";
                        closesATile = solid && !beforeDef.IsSolid;
                    }

                    offenders.Add(
                        $"    {Path.GetFileNameWithoutExtension(file),-24} ({posX,2},{posY,2}) " +
                        $"p{p[1]}  was {was,-22} -> {terrainId,2} {name,-18} " +
                        $"{(solid ? "SOLID" : "open"),-5} " +
                        $"{(closesATile ? "  *** CLOSES AN OPEN TILE ***" : "")}");
                }
            }
        }

        Console.WriteLine($"{filesScanned} trigger files, {actionRecords} action records, " +
                          $"{alterActions} ALTER_TERRAIN_TYPE");
        Console.WriteLine();

        // The layout check: an action type outside the enumerated range would
        // mean the second-to-last byte is not the action type at all.
        int outOfRange = actionTypeHistogram.Keys.Count(k => k is < 1 or > 60);
        Console.WriteLine($"action types seen: {string.Join(", ", actionTypeHistogram.Keys)}");
        Console.WriteLine($"outside the enumerated 1..60 range: {outOfRange}" +
                          (outOfRange == 0 ? "  (layout hypothesis holds)" : "  (LAYOUT WRONG)"));
        Console.WriteLine();

        Console.WriteLine("terrain written by ALTER_TERRAIN_TYPE:");
        foreach ((byte id, int count) in writtenTerrain)
        {
            string name = db.Terrain.TryGetValue(id, out TerrainDef? t) ? t.Name : "UNKNOWN";
            string kind = t is null ? "?" : t.IsSolid ? "SOLID" : "open";
            Console.WriteLine($"  {id,3} {name,-24} {kind,-6} {count,4} times");
        }

        if (offenders.Count > 0 && args.Length > 2 && args[2] == "-v")
        {
            Console.WriteLine();
            Console.WriteLine("every occurrence:");
            foreach (string o in offenders) Console.WriteLine(o);
        }

        return 0;
    }

    case "kwd" when args.Length >= 3 && args[1] == "tagged":
    {
        // Splits THINGS/TRIGGERS into their self-describing records and reports
        // what tags the file carries. See docs/research/kwd-container.md.
        foreach (KwdChunk c in KwdReader.ReadFile(ResolveEditor(args[2])))
        {
            if (!KwdTaggedRecords.IsTagged(c.Type)) continue;

            var byTag = new SortedDictionary<int, (int Count, SortedSet<int> Sizes)>();
            foreach (KwdTaggedRecords.TaggedRecord r in KwdTaggedRecords.Split(c))
            {
                if (!byTag.TryGetValue(r.Tag, out (int Count, SortedSet<int> Sizes) e))
                    byTag[r.Tag] = e = (0, []);
                e.Sizes.Add(r.Payload.Length);
                byTag[r.Tag] = (e.Count + 1, e.Sizes);
            }

            Console.WriteLine($"{c.Type} — {c.ItemCount} records, {c.Content.Length} bytes, all accounted for");
            foreach ((int tag, (int count, SortedSet<int> sizes)) in byTag)
            {
                string name = c.Type == KwdRecordType.Things
                    ? KwdTaggedRecords.ThingPayloadSizes.TryGetValue(tag, out var t) ? t.Name : "?"
                    : KwdTaggedRecords.TriggerPayloadSizes.TryGetValue(tag, out var g) ? g.Name : "?";
                Console.WriteLine($"  tag {tag,-4} {name,-16} {count,5} records   payload={string.Join(",", sizes)}");
            }
        }
        return 0;
    }

    case "kwd" when args.Length >= 3 && args[1] == "records":
    {
        // Re-checkable field-level counterpart to `kwd chunks` and `kwd tagged`.
        // It deliberately prints ids/types and consumed lengths rather than a
        // wall of presentation data; ToBytes() is covered byte-for-byte by the
        // round-trip tests.
        List<KwdChunk> chunks = KwdReader.ReadFile(ResolveEditor(args[2]));
        foreach (KwdChunk chunk in chunks)
        {
            switch (chunk.Type)
            {
                case KwdRecordType.CreatureSpells:
                    foreach (CreatureSpellDef record in KwdAdditionalRecords.ParseCreatureSpells([chunk]))
                        Console.WriteLine($"CREATURE_SPELL id={record.CreatureSpellId,3} name={record.Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Shots:
                    foreach (ShotDef record in KwdAdditionalRecords.ParseShots([chunk]))
                        Console.WriteLine($"SHOT id={record.ShotId,3} name={record.Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Effects:
                    foreach (EffectDef record in KwdAdditionalRecords.ParseEffects([chunk]))
                        Console.WriteLine($"EFFECT id={record.EffectId,3} name={record.Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.EffectElements:
                    foreach (EffectElementDef record in KwdAdditionalRecords.ParseEffectElements([chunk]))
                        Console.WriteLine($"EFFECT_ELEMENT id={record.EffectElementId,3} name={record.Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Things:
                    foreach (ThingRecord record in KwdThings.Parse([chunk]))
                        Console.WriteLine($"THING tag={record.Tag} type={record.GetType().Name,-28} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Triggers:
                    foreach (TriggerRecord record in KwdTriggers.Parse([chunk]))
                        Console.WriteLine($"TRIGGER tag={record.Tag} id={record.TriggerId,4} type={record.Type,2} repeat={record.RepeatCount,3} bytes={record.ConsumedLength}");
                    break;
                case KwdRecordType.Level:
                    LevelDef level = KwdLevel.Parse([chunk])
                        ?? throw new InvalidDataException("LEVEL chunk did not produce a level.");
                    Console.WriteLine($"LEVEL name={level.Name} ticksPerSecond={level.TicksPerSecond} paths={level.Paths.Count} bytes={level.ConsumedLength}");
                    foreach (KwdLevelFilePath path in level.Paths)
                        Console.WriteLine($"  {path.RecordType,-16} {path.Path}");
                    break;
                default:
                    Console.WriteLine($"{chunk.Type}: use its existing typed dump; {chunk.ItemCount} records");
                    break;
            }
        }
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "things":
    {
        // The placed-entity dump: unlike `kwd records`, which prints only a
        // record's type and length, this prints the fields the projection into
        // WorldState reads. With a file argument it dumps that level; with none it
        // sweeps every <name>Things.kld in the install and prints the corpus
        // distribution.
        if (args.Length == 2)
        {
            string maps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
            var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int files = 0, maxX = 0, maxY = 0, maxW = 0, maxH = 0, outOfRange = 0;
            int rooms = 0, roomsWrongTerrain = 0, roomsWrongOwner = 0;
            foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").Order(StringComparer.OrdinalIgnoreCase))
            {
                files++;

                // The level's own grid, so "in range" is checked against the map
                // it belongs to rather than against the largest map in the game.
                string level = Path.GetFileName(file);
                level = level[..^"Things.kld".Length];
                LevelMap? grid = GameDatabaseLoader.LoadCampaignMap(Dk2Install.Require(), level);
                if (grid is not null) { maxW = Math.Max(maxW, grid.Width); maxH = Math.Max(maxH, grid.Height); }

                foreach (ThingRecord record in KwdThings.Parse(KwdReader.ReadFile(file)))
                {
                    string key = $"{record.Tag} {record.GetType().Name}";
                    totals[key] = totals.TryGetValue(key, out int n) ? n + 1 : 1;

                    foreach ((int x, int y) in ThingTiles(record))
                    {
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                        if (grid is not null && (x < 0 || y < 0 || x >= grid.Width || y >= grid.Height))
                            outOfRange++;
                    }

                    // Whether a ROOM record places a room or annotates one the
                    // MAP already holds. If every one of them agrees with the
                    // grid underneath it, projecting it as a placement would
                    // build the room twice.
                    if (record is RoomThing room && grid is not null && grid.Contains(room.X, room.Y))
                    {
                        rooms++;
                        MapTile tile = grid[room.X, room.Y];
                        if (tile.TerrainId != room.RoomType) roomsWrongTerrain++;
                        if (tile.PlayerId != room.PlayerId) roomsWrongOwner++;
                    }
                }
            }
            Console.WriteLine($"{files} THINGS files, {totals.Values.Sum()} top-level records");
            foreach ((string key, int count) in totals.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"  {key,-32} {count,6}");
            Console.WriteLine(
                $"coordinate range x 0..{maxX}, y 0..{maxY}; largest map {maxW}x{maxH}; "
                + $"{outOfRange} placed things outside their own level's grid");
            Console.WriteLine(
                $"{rooms} ROOM records checked against the grid under them; "
                + $"{roomsWrongTerrain} disagree on terrain id, {roomsWrongOwner} on owner");
            return outOfRange == 0 ? 0 : 1;
        }

        string thingsPath = ResolveEditor(args[2]);

        // The level's own grid, when the file is named the shipped way, so each
        // line can say what terrain is already under the thing. That is the
        // question a projection has to answer first: whether a placed record
        // duplicates the map or annotates it.
        LevelMap? under = null;
        string stem = Path.GetFileNameWithoutExtension(thingsPath);
        if (stem.EndsWith("Things", StringComparison.OrdinalIgnoreCase))
            under = GameDatabaseLoader.LoadCampaignMap(Dk2Install.Require(), stem[..^"Things".Length]);

        foreach (ThingRecord record in KwdThings.Parse(KwdReader.ReadFile(thingsPath)))
        {
            string line = DescribeThing(record);
            if (under is not null && ThingTiles(record).FirstOrDefault() is var (tx, ty)
                && under.Contains(tx, ty))
                line += $"  [map terrain {under[tx, ty].TerrainId}, owner {under[tx, ty].PlayerId}]";
            Console.WriteLine(line);
        }
        return 0;
    }

    case "kwd" when args.Length == 2 && args[1] == "corpus":
    {
        KwdCorpusAudit.Result audit = KwdCorpusAudit.Run(Dk2Install.Require());
        Console.WriteLine($"{audit.Files} KWD/KLD files; {audit.ParsedTypes.Count}/18 record types parsed");
        foreach (KwdRecordType type in Enum.GetValues<KwdRecordType>())
        {
            audit.RecordCounts.TryGetValue(type, out int count);
            Console.WriteLine($"  {type,-16} {count,7} records  {(audit.ParsedTypes.Contains(type) ? "parsed" : "MISSING")}");
        }
        return audit.ParsedTypes.Count == 18 ? 0 : 1;
    }

    case "kwd" when args.Length == 2 && args[1] == "trigger-types":
    {
        IReadOnlyList<KwdTriggerCorpusAudit.Entry> distribution =
            KwdTriggerCorpusAudit.Run(Dk2Install.Require());
        int total = 0;
        foreach (KwdTriggerCorpusAudit.Entry entry in distribution)
        {
            total += entry.Count;
            Console.WriteLine($"tag={entry.Tag} type={entry.Type,3} count={entry.Count,4} " +
                              $"levels={entry.Levels.Count,2}: {string.Join(",", entry.Levels)}");
        }
        Console.WriteLine($"-- {total} TRIGGERS records; {distribution.Count} tag/type combinations");
        return 0;
    }

    case "kwd" when args.Length is 2 or 3 && args[1] == "oracle":
    {
        // Which level makes the best two-sided experiment -- and, as it turns out,
        // that none of them does yet. A recorded capture is a played session, so a
        // column whose chain runs through something only a human does can never
        // agree. This measures the opposite: what a headless run moves on its own.
        // The reading is docs/EXPERIMENTS.md, 2026-09-09.
        int oracleTurns = args.Length == 3
            ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)
            : 1200;
        string oracleInstall = Dk2Install.Require();
        string oracleMaps = Path.Combine(oracleInstall, "Data", "editor", "maps");
        var oracleRows = new List<OracleReach>();

        foreach (string file in Directory.EnumerateFiles(oracleMaps, "*Triggers.kld")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string level = Path.GetFileName(file)[.. ^"Triggers.kld".Length];
            List<TriggerRecord>? recs = GameDatabaseLoader.LoadCampaignTriggers(oracleInstall, level);
            LevelDef? def = GameDatabaseLoader.LoadCampaignLevel(oracleInstall, level);
            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(oracleInstall, level);
            List<PlayerDef>? plyrs = GameDatabaseLoader.LoadCampaignPlayers(oracleInstall, level);
            if (recs is null || def is null || things is null || plyrs is null) continue;

            oracleRows.Add(MeasureOracleReach(
                oracleInstall, level, oracleTurns, recs, def, things, plyrs));
        }

        Console.WriteLine($"Every level run headless for {oracleTurns} turns, ranked by what a capture "
            + "of it could actually discriminate.");
        Console.WriteLine("moved  = flag/timer ids this run ever set.   after1 = those that moved past turn 1,");
        Console.WriteLine("         which is the only part a played capture can be compared against.");
        Console.WriteLine("could  = ids a walk reaching every type 1/2/34 gate would set -- an upper bound.");
        Console.WriteLine("         The gap to `moved` is NOT the frontier: level 4 has 24 ids of gap and its");
        Console.WriteLine("         one stall forecloses one record. Most of that gap is script the walk does");
        Console.WriteLine("         reach and answers false, because an idle world has nothing happening.");
        Console.WriteLine("writes = ids the file writes at all.");
        Console.WriteLine("stalls = distinct condition records the run reaches and cannot answer.");
        Console.WriteLine("gates  = records those stalls foreclose -- the subtrees they cut off that nothing");
        Console.WriteLine("         else reaches. THIS is the cost of the frontier; `stalls` only counts the");
        Console.WriteLine("         declines, and the two rank differently (see `reach` for the rest).");
        Console.WriteLine();
        Console.WriteLine($"{"level",-18} {"moved",5} {"after1",6} {"could",5} {"writes",6}  "
            + $"{"stalls",6} {"gates",5}  {"reach",5} {"of",5}  {"creature",8}");
        foreach (OracleReach r in oracleRows
                     .OrderByDescending(r => r.MovedAfterFirstTurn).ThenByDescending(r => r.Moved)
                     .ThenBy(r => r.Level, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"{r.Level,-18} {r.Moved,5} {r.MovedAfterFirstTurn,6} "
                + $"{r.Flags + r.Timers,5} {r.AllFlags + r.AllTimers,6}  "
                + $"{r.Stalls,6} {r.Gates,5}  {r.Reached,5} {r.Records,5}  {r.CreatureRoots,8}");

        int usable = oracleRows.Count(r => r.MovedAfterFirstTurn > 0);
        Console.WriteLine();
        Console.WriteLine($"{usable} of {oracleRows.Count} levels move any column past turn 1. "
            + "A capture is worth taking of a level that moves many.");
        return 0;
    }

    case "kwd" when args.Length == 2 && args[1] == "parties":
    {
        // Which of the world's 24 party slots a shipped HERO_PARTY lands in, and
        // which of its sixteen member slots count as used. Action type 14 indexes
        // that array by the id in its own body, so an executor has to choose
        // between the file's order and the record's own PartyId, and has to
        // decide what an unused member is. This prints the evidence for both
        // (docs/research/trigger-tree.md, "The 24 slots at +0x4030").
        string partyMaps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
        int partyFiles = 0, parties = 0, orderDiffers = 0, duplicates = 0, overSlots = 0;
        int withCreature = 0, creatureNoTrigger = 0, triggerNoCreature = 0, maxPartyId = -1;
        foreach (string file in Directory.EnumerateFiles(partyMaps, "*Things.kld")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            partyFiles++;
            var seen = new HashSet<byte>();
            int index = 0;
            foreach (HeroPartyThing party in KwdThings
                         .Parse(KwdReader.ReadFile(file)).OfType<HeroPartyThing>())
            {
                parties++;
                if (party.PartyId != index) orderDiffers++;
                if (!seen.Add(party.PartyId)) duplicates++;
                if (party.PartyId >= 24) overSlots++;
                maxPartyId = Math.Max(maxPartyId, party.PartyId);
                index++;
                foreach (GoodCreatureThing member in party.Members)
                {
                    if (member.CreatureId != 0)
                    {
                        withCreature++;
                        if (member.TriggerId == 0) creatureNoTrigger++;
                    }
                    else if (member.TriggerId != 0)
                    {
                        triggerNoCreature++;
                    }
                }
            }
        }
        Console.WriteLine($"{partyFiles} THINGS files, {parties} HERO_PARTY records, ids 0..{maxPartyId}");
        Console.WriteLine($"  {orderDiffers} whose PartyId differs from their position in the file");
        Console.WriteLine($"  {duplicates} sharing a PartyId with an earlier party in the same file");
        Console.WriteLine($"  {overSlots} with a PartyId past the 24 runtime slots");
        Console.WriteLine($"  {withCreature} members carry a creature id, {creatureNoTrigger} of those no trigger");
        Console.WriteLine($"  {triggerNoCreature} members carry a trigger but no creature id");
        return 0;
    }

    case "kwd" when args.Length is 2 or 3 && args[1] == "unreached":
    {
        // Where a level's script is when the run never touches it. `kwd oracle`'s
        // `reach` is a STATIC walk descending only through types 1/2/34, so its gap
        // is not "what the run missed" -- this is. One bucket per record, first to
        // claim it keeps it (ADR-0125). The default hour is deliberate: at 600 turns
        // the biggest closed group is level-time gates not yet due (ADR-0126).
        int unreachedTurns = args.Length == 3 ? int.Parse(args[2]) : 14400;
        string reachMaps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
        int total = 0, ran = 0, declined = 0, creature = 0, party = 0, orphan = 0, closed = 0;
        var closedByType = new Dictionary<byte, int>();
        var partialByArm = new Dictionary<(int Tag, byte Type, TriggerContext Context),
            HashSet<(string Level, ushort TriggerId)>>();
        Console.WriteLine($"Every level run headless for {unreachedTurns} turns. Records the run never");
        Console.WriteLine("walked, by why -- first bucket that claims a record keeps it:");
        Console.WriteLine("  declined = behind a condition this executor cannot answer (the frontier)");
        Console.WriteLine("  creature = only reachable below a creature root that stayed closed or inactive");
        Console.WriteLine("  party    = only reachable from a party subtree nothing armed (action 14");
        Console.WriteLine("             creates no creatures, Q254)");
        Console.WriteLine("  orphan   = reachable from no root at all -- dead script in the file");
        Console.WriteLine("  closed   = reached by no route above: the walk got there and a condition");
        Console.WriteLine("             answered FALSE. Only a mechanism, not an arm, opens these.");
        Console.WriteLine();
        Console.WriteLine($"{"level",-18} {"records",7} {"ran",6} {"declined",8} {"creature",8} "
            + $"{"party",6} {"orphan",6} {"closed",6}");

        foreach (string file in Directory.EnumerateFiles(reachMaps, "*Triggers.kld")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string level = Path.GetFileName(file)[..^"Triggers.kld".Length];
            string reachInstall = Dk2Install.Require();
            LevelMap? rmap = GameDatabaseLoader.LoadCampaignMap(reachInstall, level);
            LevelDef? rdef = GameDatabaseLoader.LoadCampaignLevel(reachInstall, level);
            List<TriggerRecord>? rrecords = GameDatabaseLoader.LoadCampaignTriggers(reachInstall, level);
            List<PlayerDef>? rplayers = GameDatabaseLoader.LoadCampaignPlayers(reachInstall, level);
            List<ThingRecord>? rthings = GameDatabaseLoader.LoadCampaignThings(reachInstall, level);
            if (rmap is null || rdef is null || rrecords is null || rplayers is null || rthings is null)
                continue;

            GameDatabase rdatabase = GameDatabaseLoader.LoadForCampaignLevel(reachInstall, level);
            WorldState rworld = WorldState.FromMap(
                rdatabase, rmap, rplayers.Select(p => p.PlayerId).ToArray());
            ThingProjection.Place(rworld, rdatabase, rthings);
            var rtree = new TriggerTree(
                rrecords, rdef.TriggerId,
                rthings.OfType<HeroPartyThing>(),
                rplayers.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
            var rsim = new GameSimulation(rdatabase, rworld, seed: 0, triggers: rtree)
            {
                TrackTriggerReach = true,
            };

            // The union across turns, not one turn's: a record first reached on turn
            // 300 was reached. Same for the declines, whose subtrees only stop being
            // foreclosed if some later turn walks them anyway.
            var walked = new HashSet<ushort>();
            var foreclosed = new HashSet<ushort>();
            var closedBy = new Dictionary<ushort, byte>();
            var closedSets = new Dictionary<ushort, HashSet<ushort>>();
            for (int turn = 1; turn <= unreachedTurns; turn++)
            {
                rsim.Tick();
                if (rsim.LastTriggerRun is not { } run) continue;
                if (run.Reached is not null) walked.UnionWith(run.Reached);
                foreach (BlockedTrigger item in run.Partial)
                {
                    var key = (item.Tag, item.Type, item.Context);
                    if (!partialByArm.TryGetValue(key, out HashSet<(string, ushort)>? records))
                        partialByArm[key] = records = [];
                    records.Add((level, item.TriggerId));
                }
                foreach (TriggerGate gate in TriggerReach.Gated(rtree, run))
                    foreclosed.UnionWith(gate.Foreclosed);
                foreach (TriggerClosure c in TriggerReach.Closed(rtree, run))
                {
                    closedBy[c.TriggerId] = c.Type;
                    if (!closedSets.TryGetValue(c.TriggerId, out HashSet<ushort>? set))
                        closedSets[c.TriggerId] = set = [];
                    set.UnionWith(c.Foreclosed);
                }
            }

            foreclosed.ExceptWith(walked);
            HashSet<ushort> fromCreatures = TriggerReach.Reachable(
                rtree,
                rthings.OfType<KeeperCreatureThing>().Select(c => c.TriggerId)
                    .Concat(rthings.OfType<GoodCreatureThing>().Select(c => c.TriggerId))
                    .Where(id => id != 0));
            HashSet<ushort> fromParties = TriggerReach.Reachable(
                rtree,
                rthings.OfType<HeroPartyThing>()
                    .SelectMany(p => p.Members.Select(m => m.TriggerId).Append(p.TriggerId))
                    .Where(id => id != 0));

            HashSet<ushort> fromAnyRoot = TriggerReach.Reachable(
                rtree,
                new[] { rdef.TriggerId }
                    .Concat(rplayers.Select(p => p.TriggerId))
                    .Concat(rthings.OfType<ActionPointThing>().Select(a => a.TriggerId))
                    .Concat(fromCreatures).Concat(fromParties)
                    .Where(id => id != 0));

            int lDeclined = 0, lCreature = 0, lParty = 0, lOrphan = 0, lClosed = 0;
            foreach (TriggerRecord record in rrecords)
            {
                ushort id = record.TriggerId;
                if (walked.Contains(id)) continue;
                if (foreclosed.Contains(id)) lDeclined++;
                else if (fromCreatures.Contains(id)) lCreature++;
                else if (fromParties.Contains(id)) lParty++;
                else if (!fromAnyRoot.Contains(id)) lOrphan++;
                else lClosed++;
            }

            // Attribute each still-unreached record to the false condition nearest it,
            // by taking the smallest foreclosed set that contains it: a record deep under
            // several false conditions belongs to the one that stopped the walk last.
            foreach (ushort id in rrecords.Select(r => r.TriggerId))
            {
                if (walked.Contains(id) || foreclosed.Contains(id)) continue;
                ushort best = 0;
                int bestSize = int.MaxValue;
                foreach ((ushort gateId, HashSet<ushort> set) in closedSets)
                {
                    if (!set.Contains(id) || set.Count >= bestSize) continue;
                    best = gateId;
                    bestSize = set.Count;
                }
                if (bestSize != int.MaxValue) closedByType[closedBy[best]] =
                    closedByType.GetValueOrDefault(closedBy[best]) + 1;
            }

            total += rrecords.Count;
            ran += walked.Count;
            declined += lDeclined; creature += lCreature; party += lParty;
            orphan += lOrphan; closed += lClosed;
            Console.WriteLine($"{level,-18} {rrecords.Count,7} {walked.Count,6} {lDeclined,8} "
                + $"{lCreature,8} {lParty,6} {lOrphan,6} {lClosed,6}");
        }

        Console.WriteLine();
        Console.WriteLine($"{"TOTAL",-18} {total,7} {ran,6} {declined,8} {creature,8} "
            + $"{party,6} {orphan,6} {closed,6}");

        // And what closes them: the condition type nearest each unreached record that
        // the walk did reach and answered false. This is the list a mechanism opens --
        // no dispatcher arm does, because these arms already work.
        Console.WriteLine();
        Console.WriteLine("closed by condition type (the nearest false condition above each record):");
        foreach ((byte type, int count) in closedByType.OrderByDescending(e => e.Value).Take(20))
            Console.WriteLine($"  type {type,3}: {count,5}");

        Console.WriteLine();
        Console.WriteLine("partial action records reached at least once, ranked by unique record:");
        foreach (((int tag, byte type, TriggerContext context), HashSet<(string, ushort)> records)
                 in partialByArm.OrderByDescending(e => e.Value.Count)
                     .ThenBy(e => e.Key.Type).ThenBy(e => e.Key.Context))
            Console.WriteLine($"  tag {tag} type {type,3} in {context,-8}: {records.Count,5}");
        return 0;
    }

    case "kwd" when args.Length == 2 && args[1] == "roots":
    {
        // Every trigger root in the campaign, by the walker that owns it. A level
        // spreads its script over four trees and the world one is often the
        // smallest, so this is what says how much of a level any given executor
        // can reach (docs/research/trigger-walkers.md).
        string rootMaps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
        int levels = 0, worldRoots = 0, partyRoots = 0, playerRoots = 0;
        int creatureRoots = 0, apRoots = 0, multiPlayer = 0;
        foreach (string file in Directory.EnumerateFiles(rootMaps, "*Triggers.kld")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string level = Path.GetFileName(file)[..^"Triggers.kld".Length];
            LevelDef? def = GameDatabaseLoader.LoadCampaignLevel(Dk2Install.Require(), level);
            List<ThingRecord>? rootThings = GameDatabaseLoader.LoadCampaignThings(Dk2Install.Require(), level);
            List<PlayerDef>? rootPlayers = GameDatabaseLoader.LoadCampaignPlayers(Dk2Install.Require(), level);
            if (def is null || rootThings is null || rootPlayers is null) continue;
            levels++;

            int players = rootPlayers.Count(p => p.TriggerId != 0);
            int parties = rootThings.OfType<HeroPartyThing>().Count(p => p.TriggerId != 0);
            int points = rootThings.OfType<ActionPointThing>().Count(a => a.TriggerId != 0);
            int creatures = rootThings.OfType<GoodCreatureThing>().Count(c => c.TriggerId != 0)
                + rootThings.OfType<KeeperCreatureThing>().Count(c => c.TriggerId != 0);

            if (def.TriggerId != 0) worldRoots++;
            partyRoots += parties;
            playerRoots += players;
            creatureRoots += creatures;
            apRoots += points;

            // Order within a turn is the world's runtime player list, not this
            // file's order, and the two are only distinguishable where a level
            // has more than one player root.
            if (players > 1) multiPlayer++;
        }
        Console.WriteLine($"{levels} levels: {worldRoots} with a world root, "
            + $"{partyRoots} party, {playerRoots} player, {creatureRoots} creature, "
            + $"{apRoots} action point roots");
        Console.WriteLine($"  {multiPlayer} levels have more than one player root "
            + "-- only those can show whether player order matters");
        return 0;
    }

    case "kwd" when args.Length >= 3 && args[1] == "triggers":
    {
        // One level's trigger records with their links and decoded parameters.
        // `kwd records` prints ids and types; `kwd trigger-types` counts them
        // corpus-wide; neither shows a tree. This does, because to design an
        // observation in the running game you have to know which condition
        // leads to which visible action.
        var byId = new Dictionary<int, TriggerRecord>();
        var order = new List<TriggerRecord>();
        foreach (TriggerRecord record in KwdTriggers.Parse(KwdReader.ReadFile(ResolveEditor(args[2]))))
        {
            order.Add(record);
            byId[record.TriggerId] = record;
        }

        // Optional filter: a tag and type, so "show me every timer condition"
        // is one command rather than a grep over thousands of lines.
        int? wantTag = args.Length >= 4 ? int.Parse(args[3]) : null;
        int? wantType = args.Length >= 5 ? int.Parse(args[4]) : null;

        foreach (TriggerRecord record in order)
        {
            if (wantTag is not null && record.Tag != wantTag) continue;
            if (wantType is not null && record.Type != wantType) continue;

            Console.WriteLine(
                $"tag={record.Tag} id={record.TriggerId,4} type={record.Type,3} "
                + $"next={record.NextTriggerId,4} child={record.ChildTriggerId,4} "
                + $"repeat={record.RepeatCount,3}  "
                + string.Join(" ", record.Parameters.Select(p => $"{p.Key}={p.Value}")));

            // One level of the tree, which is what says what the condition does.
            for (int child = record.ChildTriggerId; child != 0 && byId.ContainsKey(child);)
            {
                TriggerRecord c = byId[child];
                Console.WriteLine(
                    $"    -> tag={c.Tag} id={c.TriggerId,4} type={c.Type,3} "
                    + string.Join(" ", c.Parameters.Select(p => $"{p.Key}={p.Value}")));
                child = c.NextTriggerId == child ? 0 : c.NextTriggerId;
            }
        }
        return 0;
    }

    case "kwd" when args.Length >= 4 && args[1] == "trigger-params":
    {
        // The distribution of ONE named parameter across every shipped record
        // of one tag and type. `kwd trigger-types` says how many records exist;
        // this says what they carry, which is what settles a unit question:
        // a timer threshold authored in seconds and one authored in ticks are
        // the same field with values that differ by a factor of four.
        if (!int.TryParse(args[2], out int wantTag))
        {
            Console.Error.WriteLine("usage: kwd trigger-params <tag> <type> [parameterName]");
            return 2;
        }

        bool anyType = args[3] == "*";
        int wantType = anyType ? -1 : int.Parse(args[3]);
        string? wantParam = args.Length >= 5 ? args[4] : null;

        string maps = Path.Combine(Dk2Install.Require(), "Data", "editor", "maps");
        var tally = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int records = 0;

        foreach (string file in Directory.EnumerateFiles(maps, "*Triggers.kld").Order(StringComparer.OrdinalIgnoreCase))
        {
            foreach (TriggerRecord record in KwdTriggers.Parse(KwdReader.ReadFile(file)))
            {
                if (record.Tag != wantTag) continue;
                if (!anyType && record.Type != wantType) continue;

                records++;
                foreach ((string name, long value) in record.Parameters)
                {
                    if (wantParam is not null && name != wantParam) continue;
                    string key = $"{name}={value}";
                    tally[key] = tally.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
        }

        foreach ((string key, int count) in tally.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {key,-28} {count,5}");
        Console.WriteLine($"-- {records} records of tag {wantTag} type {(anyType ? "*" : $"{wantType}")}");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "terrain":
    {
        List<TerrainDef> terrains = KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Terrain.kwd")));

        Console.WriteLine($"{"id",3} {"name",-24} {"health",7} {"maxHp",7} {"gold",5} {"mana",5} " +
                          $"{"dmg",4} {"->maxHp",7} {"->dead",7}  flags");
        foreach (TerrainDef t in terrains.OrderBy(t => t.TerrainId))
        {
            string flags = string.Join(",", new[]
            {
                t.IsSolid ? "solid" : null,
                t.IsImpenetrable ? "impenetrable" : null,
                t.IsOwnable ? "ownable" : null,
                t.IsTaggable ? "taggable" : null,
                t.IsRoom ? "room" : null,
                t.IsWater ? "water" : null,
                t.IsLava ? "lava" : null,
            }.Where(s => s is not null));
            Console.WriteLine($"{t.TerrainId,3} {t.Name,-24} {t.StartingHealth,7} {t.MaxHealth,7} " +
                              $"{t.GoldValue,5} {t.ManaGain,5} {t.Damage,4} " +
                              $"{t.MaxHealthTypeTerrainId,7} {t.DestroyedTypeTerrainId,7}  {flags}");
        }
        Console.WriteLine($"-- {terrains.Count} terrain types");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "terrainmana":
    {
        List<TerrainDef> terrains = KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Terrain.kwd")));

        Console.WriteLine($"{"id",3} {"name",-32} {"manaGain",8} {"maxManaGain",11}");
        foreach (TerrainDef t in terrains.OrderBy(t => t.TerrainId))
            Console.WriteLine($"{t.TerrainId,3} {t.Name,-32} {t.ManaGain,8} {t.MaxManaGain,11}");

        Console.WriteLine($"-- {terrains.Count} terrain types");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "terrainflags":
    {
        List<TerrainDef> terrains = KwdTerrainRooms.ParseTerrain(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Terrain.kwd")));

        foreach (TerrainDef t in terrains.OrderBy(t => t.TerrainId))
            Console.WriteLine($"{t.TerrainId,3} {t.Name,-32} 0x{(uint)t.Flags:X8}  {t.Flags}");

        Console.WriteLine($"-- {terrains.Count} terrain types");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "roomflags":
    {
        List<RoomDef> flagged = KwdTerrainRooms.ParseRooms(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Rooms.kwd")));

        foreach (RoomDef r in flagged.OrderBy(r => r.RoomId))
            Console.WriteLine($"{r.RoomId,3} {r.Name,-32} 0x{(uint)r.Flags:X8}  {r.Flags}");

        Console.WriteLine($"-- {flagged.Count} room types");
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "rooms":
    {
        List<RoomDef> rooms = KwdTerrainRooms.ParseRooms(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Rooms.kwd")));

        Console.WriteLine($"{"id",3} {"name",-24} {"cost",5} {"sell%",5} {"terr",4} {"spawn",5} " +
                          $"{"size",5} {"construction",-20} flags");
        foreach (RoomDef r in rooms.OrderBy(r => r.RoomId))
        {
            string flags = string.Join(",", new[]
            {
                r.IsBuildable ? "buildable" : null,
                r.IsGood ? "good" : null,
                r.HasWalls ? "walls" : null,
            }.Where(s => s is not null));
            Console.WriteLine($"{r.RoomId,3} {r.Name,-24} {r.Cost,5} {r.ReturnPercentage,5} " +
                              $"{r.TerrainId,4} {r.CreatedCreatureId,5} " +
                              $"{r.RecommendedSizeX + "x" + r.RecommendedSizeY,5} " +
                              $"{r.Construction,-20} {flags}");
        }
        Console.WriteLine();
        Console.WriteLine("object ids per room (Q120: which room owns which object):");
        foreach (RoomDef r in rooms.OrderBy(r => r.RoomId))
        {
            string objs = string.Join(",", r.ObjectIds.Where(o => o != 0));
            if (objs.Length > 0) Console.WriteLine($"  {r.RoomId,3} {r.Name,-24} {objs}");
        }
        Console.WriteLine($"-- {rooms.Count} room types");
        return 0;
    }

    // Q64/Q16: the MAP record's four bytes per tile are terrainId, playerId and
    // two we do not use. A sold bridge reverts to the water or lava it spanned
    // (CONFIRMED, owner), so the underlying terrain has to be recorded per tile
    // somewhere, and one of these two bytes is the obvious candidate. This
    // cross-tabulates them against terrain so the question can be settled by
    // looking rather than by guessing.
    case "kwd" when args.Length >= 3 && args[1] == "mapbytes":
    {
        LevelMap? bytesGrid = MapGrid.Parse(KwdReader.ReadFile(ResolveEditor(args[2])));
        if (bytesGrid is null)
        {
            Console.Error.WriteLine("no MAP chunk in that file");
            return 2;
        }

        byte? only = args.Length >= 4 && byte.TryParse(args[3], out byte want) ? want : null;

        // "neighbours" mode: for every tile of the wanted terrain, what is
        // orthogonally next to it, grouped by the two unparsed bytes. This is
        // the test for whether byte 2 records the hazard a bridge spans, since
        // a fully bridged pond leaves no visible water to count.
        if (args.Length >= 5 && args[4] == "neighbours" && byte.TryParse(args[3], out byte wantN))
        {
            var tally = new Dictionary<string, int>();
            for (int y = 0; y < bytesGrid.Height; y++)
            {
                for (int x = 0; x < bytesGrid.Width; x++)
                {
                    MapTile t = bytesGrid[x, y];
                    if (t.TerrainId != wantN) continue;

                    int[] dx = [0, -1, 1, 0];
                    int[] dy = [-1, 0, 0, 1];
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = x + dx[i], ny = y + dy[i];
                        if (nx < 0 || ny < 0 || nx >= bytesGrid.Width || ny >= bytesGrid.Height) continue;
                        string key = $"flags={t.Flags} neighbour={bytesGrid[nx, ny].TerrainId}";
                        tally[key] = tally.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }
            }

            foreach (var kv in tally.OrderBy(k => k.Key))
                Console.WriteLine($"  {kv.Key,-28} {kv.Value,5}");
            return 0;
        }


        Console.WriteLine($"{"terr",4} {"flags",5} {"unk",4} {"count",6}");
        var groups = bytesGrid.Tiles
            .Where(t => only is null || t.TerrainId == only)
            .GroupBy(t => (t.TerrainId, t.Flags, t.Unknown))
            .OrderBy(g => g.Key.TerrainId).ThenBy(g => g.Key.Flags).ThenBy(g => g.Key.Unknown);

        foreach (var g in groups)
        {
            Console.WriteLine(
                $"{g.Key.TerrainId,4} {g.Key.Flags,5} {g.Key.Unknown,4} {g.Count(),6}");
        }
        return 0;
    }

    case "kwd" when args.Length >= 3 && args[1] == "map":
    {
        List<KwdChunk> mapChunks = KwdReader.ReadFile(ResolveEditor(args[2]));
        LevelMap? grid = MapGrid.Parse(mapChunks);
        if (grid is null)
        {
            Console.Error.WriteLine("no MAP chunk in that file");
            return 2;
        }

        Console.WriteLine($"{grid.Width} x {grid.Height} tiles");
        var byTerrain = grid.Tiles.GroupBy(t => t.TerrainId)
            .OrderByDescending(g => g.Count()).ToList();
        Console.WriteLine("terrain histogram:");
        foreach (var g in byTerrain.Take(12))
            Console.WriteLine($"  terrain {g.Key,3}: {g.Count(),6} tiles");
        var owners = grid.Tiles.GroupBy(t => t.PlayerId).OrderBy(g => g.Key);
        Console.WriteLine("owners: " + string.Join(", ", owners.Select(g => $"p{g.Key}={g.Count()}")));

        // Optional: where a given terrain type actually is, so scenarios can
        // name real coordinates instead of guessing at them.
        if (args.Length >= 4 && byte.TryParse(args[3], out byte wanted))
        {
            var found = new List<string>();
            for (int y = 0; y < grid.Height && found.Count < 24; y++)
                for (int x = 0; x < grid.Width && found.Count < 24; x++)
                    if (grid[x, y].TerrainId == wanted)
                        found.Add($"({x},{y})p{grid[x, y].PlayerId}");

            Console.WriteLine($"terrain {wanted} at: {string.Join(" ", found)}"
                              + (found.Count == 24 ? " ..." : ""));
        }

        return 0;
    }

    case "kwd" when args.Length >= 3 && args[1] == "creature":
    {
        List<CreatureDef> all = KwdCreatures.Parse(KwdReader.ReadFile(
            Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd")));
        CreatureDef? c = all.FirstOrDefault(x =>
            x.Name.Equals(args[2], StringComparison.OrdinalIgnoreCase));
        if (c is null)
        {
            Console.Error.WriteLine($"no creature named '{args[2]}'");
            return 2;
        }

        Console.WriteLine($"{c.Name} (id {c.CreatureId})  class={c.JobClass} style={c.FightStyle}");
        Console.WriteLine($"  hp={c.Hp} dmg={c.MeleeDamage} pay={c.Pay} fear={c.Fear} threat={c.Threat}");
        Console.WriteLine($"  speed={c.Speed} run={c.RunSpeed} see={c.DistanceCanSee} hear={c.DistanceCanHear}");
        Console.WriteLine($"  hungerRate={c.HungerRate} hungerFill={c.HungerFill} " +
                          $"awake={c.TimeAwake} sleep={c.TimeSleep}");
        Console.WriteLine($"  lairObject={c.LairObjectId}");
        Console.WriteLine($"  hpFromChicken={c.HpFromChicken} " +
                          $"ownLandHeal/s={c.OwnLandHealthIncreasePerSecond} " +
                          $"decompose={c.DecomposeValue}");
        Console.WriteLine($"  meleeRange={c.MeleeRange} recharge={c.MeleeRecharge} attack={c.MeleeAttackType}");
        Console.WriteLine($"  maxGoldHeld={c.MaxGoldHeld} initialGoldHeld={c.InitialGoldHeld}");
        Console.WriteLine($"  exp/lvl={c.ExpForNextLevel} exp/s={c.ExpPerSecond} train={c.ExpPerSecondTraining} " +
                          $"research={c.ResearchPerSecond} manufacture={c.ManufacturePerSecond}");
        Console.WriteLine($"  unhappyThreshold={c.UnhappyThreshold} angerNoLair={c.AngerNoLair} " +
                          $"angerNoFood={c.AngerNoFood} angerNoPay={c.AngerNoPay} angerNoWork={c.AngerNoWork}");
        Console.WriteLine($"  angerSlap={c.AngerSlap} angerInHand={c.AngerInHand}");
        Console.WriteLine($"  happy   : {string.Join(", ", c.HappyJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  unhappy : {string.Join(", ", c.UnhappyJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  angry   : {string.Join(", ", c.AngryJobs.Select(j => $"{j.Job}({j.ChancePercent}%)"))}");
        Console.WriteLine($"  hates   : {string.Join(", ", c.HateJobs)}");
        Console.WriteLine($"  altJobs : {string.Join(", ", c.AlternativeJobs.Select(j => j.Job))}");
        Console.WriteLine($"  attracts: {string.Join(", ", c.Attractions.Where(a => a.RoomId != 0)
                              .Select(a => $"room{a.RoomId}(size>={a.RoomSize})"))}");
        Console.WriteLine($"  spells  : {string.Join(", ", c.Spells.Select(s => $"#{s.CreatureSpellId}@lvl{s.LevelAvailable}"))}");
        Console.WriteLine($"  resists : {string.Join(", ", c.Resistances.Select(x => $"{x.AttackType}={x.Value}"))}");

        // Raw fixed-point values, for exact transcription into test fixtures.
        // Fp32's denominator is 4096, so the decimal forms above are lossy.
        Console.WriteLine($"  flags   : 0x{(uint)c.Flags:X8} {c.Flags}");
        Console.WriteLine($"  raw     : speed={c.Speed.Raw} run={c.RunSpeed.Raw} shuffle={c.ShuffleSpeed.Raw} " +
                          $"height={c.Height.Raw} see={c.DistanceCanSee.Raw} hear={c.DistanceCanHear.Raw} " +
                          $"meleeRange={c.MeleeRange.Raw} recharge={c.MeleeRecharge.Raw} " +
                          $"guard={c.GuardDuration.Raw} idle={c.IdleDuration.Raw}");
        return 0;
    }

    // The Library's rates, and what the loader's divide by variable 19 does to
    // each of them. Written for the research accrual (ADR-0090): the tabled
    // figure is not what the engine adds, because ResearchPerSecond is one of
    // the ten record fields divided at load time.
    case "kwd" when args.Length >= 2 && args[1] == "research":
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2Install.Require());

        // This tool has no dependency on DK2.Simulation, deliberately - it is a
        // data dump, not a second implementation. So the loader's divide is
        // inlined here: integer divide by variable 19's four, floored at one
        // for any non-zero file value. It mirrors
        // SimulationTiming.LoaderRatePerTick, which is the authority.
        const int TicksPerGameSecond = 4;
        static int PerTick(int tabled) =>
            tabled == 0 ? 0 : Math.Max(tabled / TicksPerGameSecond, 1);

        Console.WriteLine(" id name                 tabled  perTick  perSecond  lost  lvl10");
        int researchers = 0;
        foreach (CreatureDef c in db.Creatures.Values.OrderBy(c => c.CreatureId))
        {
            if (c.ResearchPerSecond == 0) continue;
            researchers++;
            int perTick = PerTick(c.ResearchPerSecond);
            int perSecond = perTick * TicksPerGameSecond;
            int atTen = perTick
                * db.StatPercent(CreatureStatId.ResearchPerSecond, 10) / 100
                * TicksPerGameSecond;
            Console.WriteLine(
                $"{c.CreatureId,3} {c.Name,-20} {c.ResearchPerSecond,6} {perTick,8} "
                + $"{perSecond,10} {c.ResearchPerSecond - perSecond,5} {atTen,6}");
        }

        Console.WriteLine();
        Console.WriteLine($"{researchers} creatures can research.");
        Console.WriteLine("Level curve for the Research row (stat 17):");
        for (int level = 1; level <= 10; level++)
        {
            Console.Write(
                $"   L{level}={db.StatPercent(CreatureStatId.ResearchPerSecond, level)}%");
        }
        Console.WriteLine();
        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "creatures":
    {
        string? carg = args.Skip(2).FirstOrDefault(a => !a.StartsWith('-'));
        string cpath = carg is not null
            ? ResolveEditor(carg)
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");
        List<CreatureDef> creatures = KwdCreatures.Parse(KwdReader.ReadFile(cpath));

        // -s: the three sight/hearing ranges, with a histogram, because a
        // "they are all the same" claim is only worth anything counted.
        if (args.Contains("-s"))
        {
            Console.WriteLine($"{"id",3} {"name",-16} {"see",12} {"hear",12} {"perception",12}");
            foreach (CreatureDef c in creatures.OrderBy(c => c.CreatureId))
            {
                Console.WriteLine(
                    $"{c.CreatureId,3} {c.Name,-16} {c.DistanceCanSee.Raw,12} " +
                    $"{c.DistanceCanHear.Raw,12} {c.PerceptionRange.Raw,12}");
            }

            Console.WriteLine();
            foreach (var g in creatures.GroupBy(c => c.PerceptionRange.Raw).OrderBy(g => g.Key))
            {
                Console.WriteLine(
                    $"perception 0x{g.Key:X} = {g.Key / 4096.0} tiles: {g.Count()} creatures");
            }

            Console.WriteLine($"-- {creatures.Count} creatures");
            return 0;
        }

        // -a: what attracts each creature to a dungeon, which is what a portal
        // reads. Its own column set, because the room ids and minimum sizes
        // have nothing to do with the combat stats above.
        if (args.Contains("-a"))
        {
            Console.WriteLine($"{"id",3} {"name",-16} attractions (roomId size, present)");
            foreach (CreatureDef c in creatures.OrderBy(c => c.CreatureId))
            {
                string slots = string.Join("  ", c.Attractions.Select((a, i) =>
                    $"[{i}] room={a.RoomId} size={a.RoomSize} present={a.Present}"));
                Console.WriteLine($"{c.CreatureId,3} {c.Name,-16} {slots}");
            }
            Console.WriteLine($"-- {creatures.Count} creatures");
            return 0;
        }

        Console.WriteLine($"{"id",3} {"name",-16} {"hp",5} {"dmg",4} {"pay",4} {"fear",5} {"threat",6} " +
                          $"{"speed",6} {"exp/lvl",7} {"class",-8} {"flags"}");
        foreach (CreatureDef c in creatures.OrderBy(c => c.CreatureId))
        {
            string flags = string.Join(",", new[]
            {
                c.Flags.HasFlag(CreatureFlags.IsWorker) ? "worker" : null,
                c.Flags.HasFlag(CreatureFlags.IsEvil) ? "evil" : null,
                c.Flags.HasFlag(CreatureFlags.CanFly) ? "fly" : null,
                c.Flags.HasFlag(CreatureFlags.CanWalkOnWater) ? "water" : null,
                c.Flags.HasFlag(CreatureFlags.CanWalkOnLava) ? "lava" : null,
                c.Flags.HasFlag(CreatureFlags.IsFearless) ? "fearless" : null,
                c.Flags.HasFlag(CreatureFlags.IsUnique) ? "unique" : null,
                c.Flags.HasFlag(CreatureFlags.AvailableViaPortal) ? "portal" : null,
                c.Flags.HasFlag(CreatureFlags.GenerateDeadBody) ? "body" : null,
                c.Flags.HasFlag(CreatureFlags.CanBeResurrected) ? "resurrect" : null,
                c.Flags.HasFlag(CreatureFlags.NeedBodyForFightIdle) ? "needbody" : null,
            }.Where(s => s is not null));

            Console.WriteLine($"{c.CreatureId,3} {c.Name,-16} {c.Hp,5} {c.MeleeDamage,4} {c.Pay,4} " +
                              $"{c.Fear,5} {c.Threat,6} {c.Speed,6} {c.ExpForNextLevel,7} {c.JobClass,-8} {flags}");
        }
        Console.WriteLine($"-- {creatures.Count} creatures");
        return 0;
    }

    // Generic offset hunt in a record type we do not parse: give it a file and a
    // number measured in the original and it prints every offset whose
    // u8/u16/s16/s32 reading holds that number in some record, with the whole
    // column beside it; optionally anchor on one record index. Judge a candidate
    // on the column, not on one hit — that is how the Create Imp mana cost fell
    // out at KeeperSpells 0x11C (docs/research/imps-and-mana.md).
    case "kwd" when args.Length >= 4 && args[1] == "recscan":
    {
        string rpath = ResolveEditor(args[2]);
        int target = int.Parse(args[3]);
        int anchor = args.Length > 4 ? int.Parse(args[4]) : -1;

        foreach (KwdChunk chunk in KwdReader.ReadFile(rpath))
        {
            if (chunk.ItemCount == 0 || chunk.Content.Length % chunk.ItemCount != 0) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            if (size < 8) continue;
            Console.WriteLine($"chunk {chunk.Type}: {chunk.ItemCount} records of {size} bytes");

            byte[] c = chunk.Content;
            string Nm(int r)
            {
                int e = Array.IndexOf(c, (byte)0, r * size, Math.Min(32, size));
                string s = System.Text.Encoding.ASCII.GetString(c, r * size, e < 0 ? 0 : e - r * size);
                return s.Length > 1 && s.All(ch => ch >= 32 && ch < 127) ? s : $"[{r}]";
            }
            int hits = 0;
            for (int off = 0; off < size; off++)
            {
                foreach ((string w, int n) in new[] { ("u8", 1), ("u16", 2), ("s16", 2), ("s32", 4) })
                {
                    if (off + n > size) continue;
                    int Read(int rec)
                    {
                        int b = rec * size + off;
                        return w switch
                        {
                            "u8" => c[b],
                            "u16" => c[b] | (c[b + 1] << 8),
                            "s16" => (short)(c[b] | (c[b + 1] << 8)),
                            _ => c[b] | (c[b + 1] << 8) | (c[b + 2] << 16) | (c[b + 3] << 24),
                        };
                    }

                    bool match = anchor >= 0
                        ? Read(anchor) == target
                        : Enumerable.Range(0, chunk.ItemCount).Any(r => Read(r) == target);
                    if (!match) continue;

                    hits++;
                    Console.WriteLine($"0x{off:X4} {w,-3} : " + string.Join(" ",
                        Enumerable.Range(0, chunk.ItemCount).Select(r => $"{Nm(r)}={Read(r)}")));
                }
            }
            Console.WriteLine($"-- {hits} candidate offsets");
        }
        return 0;
    }

    // Raw window onto the CREATURES record, for judging a candidate offset found
    // by creaturescan. Prints the same byte range for every creature so a column
    // can be read down.
    case "kwd" when args.Length >= 4 && args[1] == "creaturehex":
    {
        int start = Convert.ToInt32(args[2], 16);
        int count = int.Parse(args[3]);
        string hpath = args.Length > 4
            ? ResolveEditor(args[4])
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");

        foreach (KwdChunk chunk in KwdReader.ReadFile(hpath))
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            Console.Write($"{"creature",-20}");
            for (int o = start; o < start + count; o++) Console.Write($" {o:X4}");
            Console.WriteLine();
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                byte[] rec = chunk.Content[(i * size)..((i + 1) * size)];
                int end = Array.IndexOf(rec, (byte)0, 0, 32);
                string name = System.Text.Encoding.ASCII.GetString(rec, 0, end < 0 ? 32 : end);
                Console.Write($"{name,-20}");
                for (int o = start; o < start + count; o++) Console.Write($" {rec[o],4}");
                Console.WriteLine();
            }
            break;
        }
        return 0;
    }

    // The object-record twin of creaturehex. Written for the obstacle radius the
    // binary reads at objDef+0xf8 (Q88): the loader's frame slot names a file
    // offset, and a frame-to-record delta can only be checked by reading the
    // column and seeing whether the numbers behave.
    case "kwd" when args.Length >= 4 && args[1] == "objecthex":
    {
        int start = Convert.ToInt32(args[2], 16);
        int count = int.Parse(args[3]);
        int width = args.Length > 4 && args[4] == "-w" ? 2 : 1;
        string opath = Path.Combine(
            Dk2Install.Require(), "Data", "editor", "Objects.kwd");

        foreach (KwdChunk chunk in KwdReader.ReadFile(opath))
        {
            if (chunk.Type != KwdRecordType.Objects) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            Console.Write($"{"object",-28}");
            for (int o = start; o < start + count; o += width) Console.Write($" {o:X4}");
            Console.WriteLine();
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                byte[] rec = chunk.Content[(i * size)..((i + 1) * size)];
                int end = Array.IndexOf(rec, (byte)0, 0, 32);
                string name = System.Text.Encoding.ASCII.GetString(rec, 0, end < 0 ? 32 : end);
                Console.Write($"{name,-28}");
                for (int o = start; o < start + count; o += width)
                {
                    int v = width == 2
                        ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                            rec.AsSpan(o))
                        : rec[o];
                    Console.Write($" {v,4}");
                }
                Console.WriteLine();
            }
            break;
        }
        return 0;
    }

    // Offset hunt inside the CREATURES record: give it a creature and a value
    // measured in the original and it prints every offset whose u8/u16/s16
    // reading holds that value for that creature, with the whole column beside
    // it. Judge the column, not one hit (docs/research/imps-and-mana.md).
    case "kwd" when args.Length >= 4 && args[1] == "creaturescan":
    {
        string wanted = args[2];
        if (!int.TryParse(args[3], out int target))
        {
            Console.Error.WriteLine($"'{args[3]}' is not a number");
            return 2;
        }

        string spath = args.Length > 4
            ? ResolveEditor(args[4])
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");

        var records = new List<(string Name, byte[] Bytes)>();
        foreach (KwdChunk chunk in KwdReader.ReadFile(spath))
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                byte[] rec = chunk.Content[(i * size)..((i + 1) * size)];
                int end = Array.IndexOf(rec, (byte)0, 0, 32);
                string name = System.Text.Encoding.ASCII.GetString(
                    rec, 0, end < 0 ? 32 : end);
                records.Add((name, rec));
            }
            break;
        }

        int anchor = records.FindIndex(r =>
            r.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (anchor < 0)
        {
            Console.Error.WriteLine($"no creature named '{wanted}' in {records.Count} records");
            return 2;
        }

        int len = records[anchor].Bytes.Length;
        Console.WriteLine($"scanning {records.Count} records of {len} bytes for " +
                          $"{records[anchor].Name} == {target}");

        int hits = 0;
        for (int off = 0; off < len; off++)
        {
            foreach (string width in new[] { "u8", "u16", "s16" })
            {
                if (width != "u8" && off + 1 >= len) continue;
                int Read(byte[] r) => width switch
                {
                    "u8" => r[off],
                    "u16" => r[off] | (r[off + 1] << 8),
                    _ => (short)(r[off] | (r[off + 1] << 8)),
                };

                if (Read(records[anchor].Bytes) != target) continue;
                hits++;
                Console.WriteLine($"0x{off:X4} {width,-3} : " + string.Join(" ",
                    records.Select(r => $"{r.Name}={Read(r.Bytes)}")));
            }
        }
        Console.WriteLine($"-- {hits} candidate offsets");
        return 0;
    }

    // Which fields of the record are the same in every record? A constant field
    // cannot discriminate between records, so a rule this project keys on one is
    // either dead code or a rule we invented (Q213). The balance-table equivalent
    // of this sweep found ten live mechanisms shipped switched off (ADR-0085); the
    // creature and object records had never been swept at all.
    case "kwd" when args.Length >= 2 && args[1] == "fieldsweep":
    {
        string which = args.Length > 2 && !args[2].StartsWith('-') ? args[2] : "creatures";
        string root = Path.Combine(Dk2Install.Require(), "Data", "editor");

        var rows = new List<(string Name, object? Value)>();
        var records = new List<(string Label, object Record)>();

        switch (which.ToLowerInvariant())
        {
            case "creatures":
                foreach (CreatureDef c in KwdCreatures.Parse(
                             KwdReader.ReadFile(Path.Combine(root, "Creatures.kwd"))))
                {
                    records.Add((c.Name, c));
                }

                break;

            case "objects":
                foreach (ObjectDef o in KwdObjects.Parse(
                             KwdReader.ReadFile(Path.Combine(root, "Objects.kwd"))))
                {
                    records.Add((o.Name, o));
                }

                break;

            default:
                Console.Error.WriteLine("fieldsweep: expected 'creatures' or 'objects'");
                return 2;
        }

        if (records.Count == 0)
        {
            Console.Error.WriteLine("fieldsweep: no records parsed");
            return 2;
        }

        Console.WriteLine($"{which}: {records.Count} records");
        Console.WriteLine();

        System.Reflection.PropertyInfo[] props = records[0].Record.GetType()
            .GetProperties(System.Reflection.BindingFlags.Public
                           | System.Reflection.BindingFlags.Instance);

        var alwaysZero = new List<string>();
        var constant = new List<(string Field, string Value)>();
        var varying = new List<(string Field, int Distinct)>();

        foreach (System.Reflection.PropertyInfo p in props)
        {
            // Collections say nothing useful to a distinct-value count; the
            // per-slot tables have their own sweeps.
            if (p.PropertyType != typeof(string)
                && typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType))
            {
                continue;
            }

            var seen = new List<string>();
            foreach ((string _, object rec) in records)
            {
                string v = p.GetValue(rec)?.ToString() ?? "<null>";
                if (!seen.Contains(v)) seen.Add(v);
            }

            if (seen.Count == 1)
            {
                string only = seen[0];
                bool zeroish = only is "0" or "0.00" or "False" or "None" or "";
                if (zeroish) alwaysZero.Add(p.Name);
                else constant.Add((p.Name, only));
            }
            else
            {
                varying.Add((p.Name, seen.Count));
            }
        }

        Console.WriteLine($"ZERO FOR EVERY RECORD ({alwaysZero.Count}) "
                          + "- carries no information in v1.70:");
        foreach (string f in alwaysZero) Console.WriteLine($"   {f}");
        Console.WriteLine();

        Console.WriteLine($"THE SAME NON-ZERO VALUE FOR EVERY RECORD ({constant.Count}) "
                          + "- cannot discriminate:");
        foreach ((string f, string v) in constant) Console.WriteLine($"   {f,-38} = {v}");
        Console.WriteLine();

        Console.WriteLine($"VARIES ({varying.Count}):");
        foreach ((string f, int n) in varying.OrderBy(x => x.Distinct))
        {
            Console.WriteLine($"   {f,-38} {n} distinct");
        }

        return 0;
    }

    // Which (offset, bit) in the CREATURES record selects exactly this set of
    // creatures? The flag twin of creaturescan. The loader does not preserve file
    // offsets into the runtime record, so a runtime offset (the hero portal flag
    // is read as `byte ptr [def + 0x68a] & 8`, DKII.EXE) says nothing about where
    // the bit lives in the file — search for the roster the flag must select, not
    // for the offset. Reasoning from the layout gave a wrong answer once.
    case "kwd" when args.Length >= 3 && args[1] == "creaturebits":
    {
        string[] names = args[2].Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string bpath = args.Length > 3
            ? ResolveEditor(args[3])
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd");

        var recs = new List<(string Name, byte[] Bytes)>();
        foreach (KwdChunk chunk in KwdReader.ReadFile(bpath))
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;
            int size = chunk.Content.Length / chunk.ItemCount;
            for (int i = 0; i < chunk.ItemCount; i++)
            {
                byte[] rec = chunk.Content[(i * size)..((i + 1) * size)];
                int end = Array.IndexOf(rec, (byte)0, 0, 32);
                recs.Add((
                    System.Text.Encoding.ASCII.GetString(rec, 0, end < 0 ? 32 : end),
                    rec));
            }
            break;
        }

        var target = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string n in names)
        {
            if (!recs.Any(r => r.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"no creature named '{n}' in {recs.Count} records");
                return 2;
            }
            target.Add(n);
        }

        int blen = recs[0].Bytes.Length;
        Console.WriteLine($"{recs.Count} records of {blen} bytes; " +
                          $"target set has {target.Count} creatures");

        int pairs = 0;
        for (int off = 0; off < blen; off++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                List<string> set = recs
                    .Where(r => (r.Bytes[off] & (1 << bit)) != 0)
                    .Select(r => r.Name)
                    .ToList();

                if (set.Count == 0) continue;

                int inTarget = set.Count(target.Contains);
                int wrong = (target.Count - inTarget) + (set.Count - inTarget);

                // Within two of the target, so a near miss is visible rather
                // than silently filtered — a flag that is the right idea with
                // one creature added or dropped is the interesting case.
                if (wrong > 2) continue;

                pairs++;
                Console.WriteLine(
                    $"0x{off:X4} bit{bit} {(wrong == 0 ? "EXACT" : $"off-by-{wrong}"),-9} " +
                    $"n={set.Count} : {string.Join(" ", set)}");
            }
        }

        Console.WriteLine($"-- {pairs} offset/bit pairs within 2 of the target");
        return 0;
    }

    // The OBJECTS record (M6): 145 objects of 894 bytes. Only the gameplay
    // tail is parsed; the first 812 bytes are art. -t groups by type flag,
    // which is what identifies a bed, a chicken or a gold pile.
    case "kwd" when args.Length >= 2 && args[1] == "objects":
    {
        string? oarg = args.Skip(2).FirstOrDefault(a => !a.StartsWith('-'));
        string opath = oarg is not null
            ? ResolveEditor(oarg)
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "Objects.kwd");
        List<ObjectDef> objects = KwdObjects.Parse(KwdReader.ReadFile(opath));

        if (args.Contains("-t"))
        {
            foreach ((string label, Func<ObjectDef, bool> pick) in new (string, Func<ObjectDef, bool>)[]
            {
                ("lair (beds)", o => o.IsLair),
                ("food", o => o.IsFood),
                ("gold", o => o.IsGold),
                ("spell book", o => o.IsSpellBook),
                ("special", o => o.IsSpecial),
                ("crate", o => o.Flags.HasFlag(ObjectFlags.TypeCrate)),
                ("level gem", o => o.Flags.HasFlag(ObjectFlags.TypeLevelGem)),
                ("decays", o => o.Decays),

                // The Hand of Evil's two: what it may lift, and where it may
                // put it down (Q196).
                ("pick up", o => o.Flags.HasFlag(ObjectFlags.CanBePickedUp)),
                ("any land", o => o.Flags.HasFlag(ObjectFlags.CanBeDroppedOnAnyLand)),
                ("slappable", o => o.Flags.HasFlag(ObjectFlags.CanBeSlapped)),
            })
            {
                var hits = objects.Where(pick).OrderBy(o => o.ObjectId).ToList();
                Console.WriteLine($"{label,-12} {hits.Count,3}: " + string.Join(", ",
                    hits.Select(o => $"{o.ObjectId}:{o.Name}")));
                Console.WriteLine();
            }

            // Every flag bit the shipped data actually uses, so a bit nobody
            // has seen set cannot be quietly named.
            uint union = objects.Aggregate(0u, (a, o) => a | (uint)o.Flags);
            Console.WriteLine($"union of all flags: 0x{union:X8}  ({(ObjectFlags)union})");
            return 0;
        }

        // Every parsed field of one record, which is what transcribing a
        // fixture needs: the table below prints the seven columns worth
        // scanning, and a fixture needs all fourteen.
        if (args.Contains("-full"))
        {
            foreach (ObjectDef o in objects.OrderBy(o => o.ObjectId))
            {
                if (args.FirstOrDefault(a => a.StartsWith("-id=")) is { } want
                    && o.ObjectId != int.Parse(want[4..]))
                {
                    continue;
                }

                Console.WriteLine($"{o.ObjectId} {o.Name}");
                Console.WriteLine($"  Flags = 0x{(uint)o.Flags:X8}  ({o.Flags})");
                Console.WriteLine($"  Hp = {o.Hp}, ManaValue = {o.ManaValue}, " +
                                  $"RoomCapacity = {o.RoomCapacity}, StartState = {o.StartState}, " +
                                  $"PickUpPriority = {o.PickUpPriority}");
                Console.WriteLine($"  Width = {o.Width.Raw}, Height = {o.Height.Raw}, " +
                                  $"Mass = {o.Mass.Raw}, Speed = {o.Speed.Raw}");
                Console.WriteLine($"  NameStringId = {o.NameStringId}, " +
                                  $"TooltipStringId = {o.TooltipStringId}");
            }
            return 0;
        }

        Console.WriteLine($"{"id",3} {"name",-26} {"hp",5} {"mana",5} {"cap",4} " +
                          $"{"state",5} {"prio",4} flags");
        foreach (ObjectDef o in objects.OrderBy(o => o.ObjectId))
        {
            Console.WriteLine($"{o.ObjectId,3} {o.Name,-26} {o.Hp,5} {o.ManaValue,5} " +
                              $"{o.RoomCapacity,4} {o.StartState,5} {o.PickUpPriority,4} {o.Flags}");
        }
        Console.WriteLine($"-- {objects.Count} objects, " +
                          $"{objects.Select(o => o.ObjectId).Distinct().Count()} distinct ids");
        return 0;
    }

    // Q84/Q85: sweep every VARIABLES record in the corpus, not just the level
    // that happens to be open. Reports the (unk1, unk2) pair distribution, and
    // for each id how many distinct values it takes across the 66 level files -
    // which is the "a sweep of keys is not a sweep of values" check.
    case "kwd" when args.Length >= 2 && args[1] == "varsweep":
    {
        string root = Dk2Install.Require();
        string mapsDir = Path.Combine(root, "Data", "editor", "maps");

        var perId = new Dictionary<int, List<(string Level, int Value, int U1, int U2)>>();
        var pairs = new Dictionary<(int, int), int>();
        int files = 0;

        foreach (string file in Directory.GetFiles(mapsDir, "*Variables.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Variables.kld".Length];
            GlobalVariables v;
            try { v = KwdVariables.Parse(KwdReader.ReadFile(file)); }
            catch (Exception e) { Console.Error.WriteLine($"{level}: {e.Message}"); continue; }
            files++;
            foreach (MiscVariable m in v.Misc.Values)
            {
                int id = (int)m.Id;
                if (!perId.TryGetValue(id, out List<(string, int, int, int)>? list))
                {
                    list = [];
                    perId[id] = list;
                }
                list.Add((level, m.Value, m.Unk1, m.Unk2));
                var key = (m.Unk1, m.Unk2);
                pairs[key] = pairs.GetValueOrDefault(key) + 1;
            }
        }

        Console.WriteLine($"{files} level VARIABLES files, {perId.Count} distinct ids");
        Console.WriteLine();
        Console.WriteLine("(unk1, unk2) pairs, by how many records carry them:");
        foreach (((int u1, int u2), int n) in pairs.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"   unk1={u1,-6} unk2={u2,-6} {n} records");
        Console.WriteLine();

        Console.WriteLine("ids whose VALUE differs between levels (the key/value trap):");
        foreach ((int id, List<(string Level, int Value, int U1, int U2)> list)
                 in perId.OrderBy(kv => kv.Key))
        {
            var vals = list.Select(e => e.Value).Distinct().OrderBy(x => x).ToList();
            if (vals.Count <= 1) continue;
            string name = Enum.IsDefined(typeof(VariableTypeId), id)
                ? ((VariableTypeId)id).ToString() : $"Unknown_{id}";
            Console.WriteLine($"   {id,4} {name,-52} {vals.Count} values: "
                              + string.Join(", ", vals.Take(8))
                              + (vals.Count > 8 ? " ..." : "")
                              + $"   in {list.Count} levels");
        }
        Console.WriteLine();

        Console.WriteLine("ids present in EVERY level file with one value (constants):");
        int constants = 0;
        foreach ((int id, List<(string Level, int Value, int U1, int U2)> list)
                 in perId.OrderBy(kv => kv.Key))
        {
            if (list.Count == files && list.Select(e => e.Value).Distinct().Count() == 1)
                constants++;
        }
        Console.WriteLine($"   {constants}");
        Console.WriteLine();

        // Q84: id 66 looks like a player count. Test it against the one
        // per-level source that names players outright - the creature pool's
        // player ids - rather than against a hunch.
        Console.WriteLine("id 66 against the highest player id in the level's creature pool:");
        int agree = 0, disagree = 0, nopool = 0;
        var mismatches = new List<string>();
        foreach (string file in Directory.GetFiles(mapsDir, "*Variables.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Variables.kld".Length];
            GlobalVariables v;
            try { v = KwdVariables.Parse(KwdReader.ReadFile(file)); }
            catch { continue; }
            if (!v.Misc.TryGetValue((VariableTypeId)66, out MiscVariable? m)) continue;
            if (v.CreaturePool.Count == 0) { nopool++; continue; }
            int maxPlayer = v.CreaturePool.Max(e => e.PlayerId);
            if (maxPlayer == m.Value) agree++;
            else { disagree++; mismatches.Add($"{level}: id66={m.Value} maxPoolPlayer={maxPlayer}"); }
        }
        Console.WriteLine($"   agree={agree} disagree={disagree} (levels with no pool: {nopool})");
        foreach (string s2 in mismatches.Take(12)) Console.WriteLine($"      {s2}");
        Console.WriteLine();

        Console.WriteLine("ids present in only SOME level files:");
        foreach ((int id, List<(string Level, int Value, int U1, int U2)> list)
                 in perId.OrderBy(kv => kv.Key))
        {
            if (list.Count == files) continue;
            string name = Enum.IsDefined(typeof(VariableTypeId), id)
                ? ((VariableTypeId)id).ToString() : $"Unknown_{id}";
            var shown = list.Take(6).Select(e => $"{e.Level}={e.Value}");
            Console.WriteLine($"   {id,4} {name,-46} in {list.Count,2}/{files}: "
                              + string.Join(" ", shown) + (list.Count > 6 ? " ..." : ""));
            Console.WriteLine($"        unk1/unk2: " + string.Join(" ",
                list.Select(e => $"({e.U1},{e.U2})").Distinct()));
        }

        return 0;
    }

    case "kwd" when args.Length >= 2 && args[1] == "vars":
    {
        string? named = args.Skip(2).FirstOrDefault(a => !a.StartsWith("-"));
        string path = named is not null
            ? ResolveEditor(named)
            : Path.Combine(Dk2Install.Require(), "Data", "editor", "GlobalVariables.kwd");
        GlobalVariables vars = KwdVariables.Parse(KwdReader.ReadFile(path));

        Console.WriteLine($"# Misc variables ({vars.Misc.Count})");
        foreach (MiscVariable v in vars.Misc.Values.OrderBy(v => (int)v.Id))
        {
            string name = Enum.IsDefined(v.Id) ? v.Id.ToString() : $"Unknown_{(int)v.Id}";
            string extra = v.Unk1 != 0 || v.Unk2 != 0 ? $"   unk1={v.Unk1} unk2={v.Unk2}" : "";
            Console.WriteLine($"{(int)v.Id,5}  {name,-64} {v.Value,10}{extra}");
        }
        Console.WriteLine();
        Console.WriteLine($"# CreaturePool entries: {vars.CreaturePool.Count}");
        Console.WriteLine($"# Availability entries: {vars.Availabilities.Count}");
        Console.WriteLine($"# Sacrifice recipes:    {vars.Sacrifices.Count}");
        Console.WriteLine($"# Creature stat rows:   {vars.CreatureStats.Count}");

        // The per-level stat table (Q119). One row per (stat, level), and the
        // question it answers is what a level actually does to a creature —
        // the owner reports imps get faster as they level, and nothing in the
        // creature record varies by level.
        if (args.Contains("-s"))
        {
            Console.WriteLine();
            foreach (IGrouping<int, CreatureStatEntry> g in vars.CreatureStats
                         .Where(e => !e.FirstPerson)
                         .GroupBy(e => e.StatId)
                         .OrderBy(g => g.Key))
            {
                Console.WriteLine($"  stat {g.Key,3}: " + string.Join(" ",
                    g.OrderBy(e => e.Level).Select(e => $"L{e.Level}={e.Value}")));
            }

            Console.WriteLine();
            Console.WriteLine("first-person rows:");
            foreach (IGrouping<int, CreatureStatEntry> g in vars.CreatureStats
                         .Where(e => e.FirstPerson)
                         .GroupBy(e => e.StatId)
                         .OrderBy(g => g.Key))
            {
                Console.WriteLine($"  stat {g.Key,3}: " + string.Join(" ",
                    g.OrderBy(e => e.Level).Select(e => $"L{e.Level}={e.Value}")));
            }
        }

        // The two lists a level actually carries. Printed on request because the
        // question they answer — does a level's list replace the global one or
        // add to it? — needs them side by side (Q82).
        if (args.Contains("-a"))
        {
            Console.WriteLine();
            Console.WriteLine("CreaturePool: " + string.Join(", ",
                vars.CreaturePool.Select(e => $"creature{e.CreatureId}=+{e.Value} p{e.PlayerId}")));

            Console.WriteLine();
            Console.WriteLine("Availability, by kind:");
            foreach (IGrouping<ushort, AvailabilityEntry> g in
                     vars.Availabilities.GroupBy(a => a.Kind).OrderBy(g => g.Key))
            {
                Console.WriteLine($"  kind {g.Key}: " + string.Join(", ",
                    g.OrderBy(a => a.TypeId).Select(a => $"{a.TypeId}={a.Value}(p{a.PlayerId})")));
            }
        }

        // -s: the temple's sacrifice recipes, resolved to creature names where
        // the ingredient is a creature. Added to check the DK2 wiki's claim
        // that two Black Knights make a Vampire against the shipped table.
        if (args.Contains("-s"))
        {
            List<CreatureDef> sacCreatures = KwdCreatures.Parse(KwdReader.ReadFile(
                Path.Combine(Dk2Install.Require(), "Data", "editor", "Creatures.kwd")));
            string Name(byte type, byte id) => type == 0 && id == 0
                ? "-"
                : $"t{type}:" + (sacCreatures.FirstOrDefault(c => c.CreatureId == id)?.Name ?? $"#{id}");

            Console.WriteLine();
            Console.WriteLine($"Sacrifice recipes ({vars.Sacrifices.Count}):");
            foreach (SacrificeEntry s in vars.Sacrifices)
            {
                Console.WriteLine(
                    $"  {Name(s.Type1, s.Id1),-22} + {Name(s.Type2, s.Id2),-22} " +
                    $"+ {Name(s.Type3, s.Id3),-22} => rewardType={s.RewardType} " +
                    $"value={s.RewardValue} speech={s.SpeechId}");
            }
        }

        return 0;
    }

    default:
        return Usage();
}

static string ResolveData(string arg) =>
    File.Exists(arg) ? arg : Path.Combine(Dk2Install.Require(), "Data", arg);

static string ResolveText(string arg) =>
    File.Exists(arg)
        ? arg
        : Path.Combine(Dk2Install.Require(), "Data", "Text", "Default", arg);

static string EscapeForOneLine(string value) =>
    value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

static string ResolveEditor(string arg)
{
    if (File.Exists(arg)) return arg;
    string root = Dk2Install.Require();
    string editor = Path.Combine(root, "Data", "editor", arg);
    if (File.Exists(editor)) return editor;
    return Path.Combine(root, "Data", "editor", "maps", arg);
}

static string RequireConvertedOutput(string argument)
{
    string path = Path.GetFullPath(argument);
    string allowed = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "converted-assets"));
    string allowedPrefix = allowed.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (path.Equals(allowed, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        return path;
    throw new ArgumentException(
        $"Conversion output must be converted-assets/ or one of its children, not '{argument}'.");
}

static int Usage()
{
    Console.WriteLine("usage: dk2tools <command>");
    Console.WriteLine();
    Console.WriteLine("commands:");
    Console.WriteLine("  where                                  print resolved DK2 install path");
    Console.WriteLine("  convert --out converted-assets/        convert catalogue assets to GLB and PNG");
    Console.WriteLine("  wad list <file.WAD>                    list archive entries");
    Console.WriteLine("  coverage [--all] [--check]             which research is built: every type a research");
    Console.WriteLine("  verification-ledger [--check]          generate or validate the sweep ledger CSV");
    Console.WriteLine("                                         doc names must exist in src/; --all also lists");
    Console.WriteLine("                                         docs naming no implementation; --check exits 1");
    Console.WriteLine("                                         on a stale claim");
    Console.WriteLine("  docs [--since <ref>] [--all]           check AGENTS.md's recording budgets on what");
    Console.WriteLine("                                         changed; --all reports the whole backlog");
    Console.WriteLine("       [--message <file>]                also check a commit message body");
    Console.WriteLine("  status [--fast] [--check]              regenerate the plan's status paragraph;");
    Console.WriteLine("                                         --check exits 1 if the plan is stale");
    Console.WriteLine("  trigger-run <level> <turns> <out.csv>  our per-turn trigger state, in the live probe's shape");
    Console.WriteLine("  replay <level> <tags.csv> <out.csv> [--turns N] [--lag L] [--origin T]");
    Console.WriteLine("                                     [--rooms rooms.csv]");
    Console.WriteLine("                                         the same, driven by a captured task-pool log");
    Console.WriteLine("  capture-diff <ours.csv> <capture.csv> [--anchor tN]");
    Console.WriteLine("                                         event set and stamp-aligned capture timing");
    Console.WriteLine("  trigger-diff <left.csv> <right.csv>   exact per-turn diff for two of our own runs");
    Console.WriteLine("  wad extract <file.WAD> <entry> [out]   extract one entry");
    Console.WriteLine("  sdt list <file.sdt>                    list sound-bank entries and formats");
    Console.WriteLine("  sdt extract <file.sdt> [--out converted-assets/audio]");
    Console.WriteLine("                                         extract PCM as WAV, compressed audio raw");
    Console.WriteLine("  str list <file.str>                    list decoded string ids and values");
    Console.WriteLine("  str get <file.str> <id>                resolve one table-local string id");
    Console.WriteLine("  sgmap [--sgmap <path>] struct <name>   show a structure's concrete field layout");
    Console.WriteLine("  sgmap [--sgmap <path>] find <text>     find structures, fields and globals by name");
    Console.WriteLine("  sgmap [--sgmap <path>] global <hex>    identify a global by address");
    Console.WriteLine("  sgmap [--sgmap <path>] at <name> <hex> identify a field by structure offset");
    Console.WriteLine("  kwd chunks <file.kwd|kld>              list chunks of a KWD/KLD file");
    Console.WriteLine("  kwd vars [file.kwd]                    dump the global balance table");
    Console.WriteLine("  kwd alterterrain [-v]                  every ALTER_TERRAIN_TYPE trigger in every shipped");
    Console.WriteLine("                                         map, with what each target tile starts as (Q74)");
    Console.WriteLine("  kwd portals                            every portal in every shipped map, its owner and");
    Console.WriteLine("                                         size, flagging levels with two claimable ones (Q86)");
    Console.WriteLine();
    Console.WriteLine("offset hunting in records we do not parse:");
    Console.WriteLine("  kwd recscan <file.kwd> <value> [rec]   offsets holding a measured value, with the column");
    Console.WriteLine("  kwd records <file.kwd|kld>            dump decoded ids/types and consumed lengths");
    Console.WriteLine("  kwd trigger-types                     dump shipped trigger type counts and levels");
    Console.WriteLine("  kwd trigger-params <tag> <type|*> [name]  value distribution of one trigger parameter");
    Console.WriteLine("  kwd triggers <file> [tag] [type]        one level's triggers, their links and parameters");
    Console.WriteLine("  kwd corpus                             parse and round-trip the shipped KWD/KLD corpus");
    Console.WriteLine("  kwd things [file.kld]                  placed entities of one level, or the corpus");
    Console.WriteLine("  kwd parties                            how shipped HERO_PARTY records map onto the 24 runtime slots");
    Console.WriteLine("  kwd roots                              every trigger root in the campaign, by the walker that owns it");
    Console.WriteLine("  kwd unreached [turns]                 records a run never walks, and why -- declined, creature, party, orphan, closed (default 14400 = an hour)");
    Console.WriteLine("  kwd oracle [turns]                     every level run headless, ranked by how much of its");
    Console.WriteLine("                                         script a two-sided capture could discriminate");
    Console.WriteLine("  kwd creaturescan <name> <value>        the same, anchored on one creature");
    Console.WriteLine("  kwd creaturehex <offsetHex> <count>    raw byte window across every creature");
    Console.WriteLine("  kwd objecthex <offsetHex> <count> [-w]  the same across every object; -w reads u16s");
    return 1;
}

/// <summary>
/// Runs one campaign level's trigger tree for a number of turns and writes the
/// probe's CSV shape, so our side can be diffed against a live capture.
/// </summary>
static int RunTriggerCsv(
    string levelName, int turns, string outPath, InputLog? log = null, RoomLog? rooms = null,
    string? tilePath = null)
{
    string? install = Dk2Install.Find();
    if (install is null)
    {
        Console.Error.WriteLine("DK2 installation not found (set DK2_DIR).");
        return 2;
    }

    LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, levelName);
    LevelDef? level = GameDatabaseLoader.LoadCampaignLevel(install, levelName);
    List<TriggerRecord>? records = GameDatabaseLoader.LoadCampaignTriggers(install, levelName);
    List<PlayerDef>? players = GameDatabaseLoader.LoadCampaignPlayers(install, levelName);
    List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, levelName);
    if (map is null || level is null || records is null || players is null || things is null)
    {
        Console.Error.WriteLine($"'{levelName}' is missing map, level, trigger, player or thing data.");
        return 2;
    }

    GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, levelName);
    WorldState world = WorldState.FromMap(
        database, map, players.Select(p => p.PlayerId).ToArray());
    // The parties come from THINGS, not TRIGGERS, and without them twenty-four of the
    // world's twenty-five roots are empty and most of a level's script never runs.
    HeroPartyThing[] parties = things.OfType<HeroPartyThing>().ToArray();
    // And the action points, which carry a root each and the rectangle every area
    // condition measures over. A world with none of them walks no area tree.
    ThingProjection.Place(world, database, things);
    var tree = new TriggerTree(
        records, level.TriggerId, parties,
        players.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
    // A triggers-only run is right for an idle capture -- nothing in one depends on the
    // world changing, which is why both matched -- but it cannot honour input at all,
    // because TileTagSystem is not there to read it. A replay therefore runs the
    // declared system list; an idle run keeps the shape its baselines were measured in.
    var simulation = new GameSimulation(
        database, world, seed: 0,
        systems: log is null && rooms is null ? null : SimulationSystems.Default(),
        triggers: tree)
    {
        TrackTriggerReach = true,
    };

    // A replay's tags are worth nothing if they never land, and a wrong reading of the
    // task pool looks exactly like a level nobody dug: counting what TileTagSystem
    // accepted turns that silent failure into a number.
    var tally = new TagTally();
    if (log is not null || rooms is not null) simulation.ObserveWith(tally);

    // The same tiles the probe was sampling, so the two traces line up tile for
    // tile. Watching only those costs a handful of reads a tick instead of a sweep,
    // and a tile the capture never mentions has nothing to compare against anyway.
    TilePos[] watched = tilePath is null || log is null
        ? []
        : [.. log.Watched.OrderBy(t => t.Y).ThenBy(t => t.X)];
    using var tileWriter = tilePath is null ? null : new StreamWriter(tilePath);
    tileWriter?.WriteLine("tick,x,y,terrain,health");
    var seen = new Dictionary<TilePos, (byte Terrain, int Health)>();

    using (var writer = new StreamWriter(outPath))
    {
        simulation.EmitTriggerTurnsTo(writer);
        for (int i = 0; i < turns; i++)
        {
            // The logs are in DK2's turn numbers, which run one ahead of our tick.
            long turn = simulation.CurrentTick.Value + 1;
            simulation.Tick(Merge(log?.At(turn), rooms?.At(turn)));

            foreach (TilePos at in watched)
            {
                ref TileState tile = ref world.Tile(at);
                var now = (tile.TerrainId, tile.Health);
                if (seen.TryGetValue(at, out (byte, int) was) && was == now) continue;
                seen[at] = now;
                tileWriter?.WriteLine(
                    $"{turn},{at.X},{at.Y},{tile.TerrainId},{tile.Health}");
            }
        }
    }

    if (tilePath is not null)
        Console.WriteLine($"  tiles: {watched.Length} watched -> {tilePath}");

    // How many workers were available to answer the marks. A capture's approach
    // times cannot be compared against ours without it: two imps and five imps
    // dig the same tile at the same rate and reach the second one very differently.
    foreach (PlayerDef p in players)
    {
        int imps = ImpSupplyRules.ImpCount(world, database, p.PlayerId);
        if (imps > 0) Console.WriteLine($"  imps: player {p.PlayerId} has {imps}");
    }

    if (log is not null || rooms is not null) tally.Report();

    int occupied = tree.Parties.Count(s => s.TriggerId != 0);
    Console.WriteLine($"{levelName}: {turns} turns, root trigger {tree.RootTriggerId} "
        + $"repeat {tree.RootRepeat}, {occupied} occupied party roots -> {outPath}");

    // The entity-root counts make an empty output column readable: zero roots and roots
    // that all answered false are different observations.
    int apRoots = things.OfType<ActionPointThing>().Count(a => a.TriggerId != 0);
    int creatureRoots = things.OfType<KeeperCreatureThing>().Count(c => c.TriggerId != 0)
        + things.OfType<GoodCreatureThing>().Count(c => c.TriggerId != 0);
    Console.WriteLine($"  walked: {tree.PlayerRoots.Count} player roots "
        + $"[{string.Join(",", tree.PlayerRoots)}], {apRoots} action point roots, "
        + $"{creatureRoots} creature roots");
    // The count is what the walk declined; `gates` is what that cost -- the records
    // behind those declines it never reached because of them. They are not the same
    // number and do not rank the same way: one decline can foreclose a hundred records
    // and a dozen can foreclose none (`TriggerReach`).
    IReadOnlyList<TriggerGate> gates = simulation.LastTriggerRun is null
        ? []
        : TriggerReach.Gated(tree, simulation.LastTriggerRun);
    foreach (TriggerCensusRow r in
             (simulation.LastTriggerRun?.Summary ?? []).OrderByDescending(s => s.Count))
    {
        int foreclosed = gates
            .Where(g => g.Blocked.Type == r.Type && g.Blocked.Context == r.Context)
            .Sum(g => g.Records);
        Console.WriteLine(
            $"  blocked tag {r.Tag} type {r.Type} in {r.Context}: {r.Count}"
            + $" -- gates {foreclosed} records");
    }

    // Say plainly what ran only in part. These descend as DK2's do and leave the world
    // untouched, so anything downstream that waits on their effect -- a hero existing, a
    // tile changing hands -- cannot happen in this run, and a divergence there is ours.
    foreach (TriggerCensusRow r in
             (simulation.LastTriggerRun?.PartialSummary ?? []).OrderByDescending(s => s.Count))
        Console.WriteLine($"  PARTIAL tag {r.Tag} type {r.Type} in {r.Context}: {r.Count} "
            + "-- descended, effect missing");

    // And what DK2 itself refuses: dead script, not a gap of ours.
    foreach (TriggerCensusRow r in
             (simulation.LastTriggerRun?.UnhandledSummary ?? []).OrderByDescending(s => s.Count))
        Console.WriteLine($"  UNHANDLED tag {r.Tag} type {r.Type} in {r.Context}: {r.Count} "
            + "-- DK2's own dispatcher returns zero here");

    // And what was answered from an assumption rather than from state.
    foreach (TriggerCensusRow r in
             (simulation.LastTriggerRun?.AssumedSummary ?? []).OrderByDescending(s => s.Count))
        Console.WriteLine($"  ASSUMED tag {r.Tag} type {r.Type} in {r.Context}: {r.Count} "
            + "-- taken as true: no modal interface state in a headless run");
    return 0;
}

/// <summary>
/// Runs a level driven by a captured task-pool log, so our side of a played
/// capture can be diffed against DK2's the way an idle one already is.
/// </summary>
static int Replay(string[] arguments)
{
    int turns = 600;
    long lag = 1;
    long origin = 1;
    string? roomPath = null;
    string? tilePath = null;
    for (int i = 4; i < arguments.Length; i += 2)
    {
        if (i + 1 >= arguments.Length) return ReplayUsage();
        string value = arguments[i + 1];
        System.Globalization.CultureInfo ic = System.Globalization.CultureInfo.InvariantCulture;
        switch (arguments[i])
        {
            case "--turns" when int.TryParse(value, System.Globalization.NumberStyles.Integer, ic, out int t):
                turns = t;
                break;
            case "--lag" when long.TryParse(value, System.Globalization.NumberStyles.Integer, ic, out long l):
                lag = l;
                break;
            case "--origin" when long.TryParse(value, System.Globalization.NumberStyles.Integer, ic, out long o):
                origin = o;
                break;
            case "--rooms":
                roomPath = value;
                break;
            case "--tiles":
                tilePath = value;
                break;
            default:
                return ReplayUsage();
        }
    }

    InputLog log;
    RoomLog? rooms = null;
    try
    {
        log = InputLog.Read(arguments[2], lag, origin);
        if (roomPath is not null) rooms = RoomLog.Read(roomPath, lag, origin);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
        or InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    InputLogTally t2 = log.Tally;
    Console.WriteLine($"{arguments[2]}: {t2.Rows} rows -> {t2.Commands} commands over "
        + $"{log.Turns.Count} turns, probe turns {t2.FirstStamp}..{t2.LastStamp}");
    if (t2.Migrations > 0)
        Console.WriteLine($"  {t2.Migrations} rows moved a tile between kind lists without "
            + "adding or removing it -- the pool is a pipeline, not a set of marks");
    if (t2.Worked > 0)
        Console.WriteLine($"  {t2.Worked} pool changes were DK2's own imps taking or "
            + $"finishing work, not the player (mark kinds are "
            + $"{string.Join(", ", InputLog.TagKinds.Order())})");
    if (t2.Before > 0)
        Console.WriteLine($"  {t2.Before} commands dated before turn 1 and dropped -- the "
            + $"probe attached after the level began, or --origin {origin} is wrong");
    if (log.Turns.Count > 0 && log.Turns[^1] > turns)
        Console.WriteLine($"  the log runs to turn {log.Turns[^1]} but the run stops at "
            + $"{turns}; raise --turns to replay all of it");

    if (rooms is not null)
    {
        RoomLogTally r = rooms.Tally;
        Console.WriteLine($"{roomPath}: {r.Rows} rows -> {r.Commands} commands over "
            + $"{rooms.Turns.Count} turns");
        if (r.Unsettled > 0)
            Console.WriteLine($"  {r.Unsettled} rows skipped -- bounds did not enclose exactly "
                + "the slab count, so the room was still being built");
        if (r.Before > 0)
            Console.WriteLine($"  {r.Before} commands dated before turn 1 and dropped");
        if (r.Disowned > 0)
            Console.WriteLine($"  {r.Disowned} rows whose room names a different owner than "
                + "the list it was found in -- THE ROOM WALK IS WRONG, not the room");
    }

    return RunTriggerCsv(arguments[1], turns, arguments[3], log, rooms, tilePath);
}

/// <summary>
/// One tick's input from both logs. They are separate recordings of one
/// player's session, so a turn can carry commands from each; tags come first,
/// which is the declared system order and so cannot change what a room build
/// sees.
/// </summary>
static InputFrame Merge(InputFrame? tags, InputFrame? rooms)
{
    IReadOnlyList<PlayerCommand> a = tags?.All ?? [];
    IReadOnlyList<PlayerCommand> b = rooms?.All ?? [];
    if (b.Count == 0) return tags ?? InputFrame.Empty;
    if (a.Count == 0) return rooms ?? InputFrame.Empty;
    return new InputFrame([.. a, .. b]);
}

static int ReplayUsage()
{
    Console.Error.WriteLine("usage: dk2tools replay <level> <tags.csv> <out.csv> "
        + "[--turns N] [--lag L] [--origin T] [--rooms rooms.csv] [--tiles tiles.csv]");
    return 2;
}

static int CaptureDiff(string[] arguments)
{
    string? anchor = null;
    if (arguments.Length == 5)
    {
        if (arguments[3] != "--anchor")
        {
            Console.Error.WriteLine("usage: dk2tools capture-diff <ours.csv> <capture.csv> [--anchor tN]");
            return 2;
        }
        anchor = arguments[4];
    }

    try
    {
        TriggerCaptureComparisonResult result = TriggerCaptureComparison.Compare(
            arguments[1], arguments[2], anchor);

        static string Ids(IReadOnlyList<string> ids) =>
            ids.Count == 0 ? "(none)" : string.Join(", ", ids);
        Console.WriteLine("event set:");
        Console.WriteLine($"  in both ({result.InBoth.Count}): {Ids(result.InBoth)}");
        Console.WriteLine($"  capture only ({result.CaptureOnly.Count}): {Ids(result.CaptureOnly)}");
        Console.WriteLine($"  ours only ({result.OursOnly.Count}): {Ids(result.OursOnly)}");
        if (result.OursBeyondCapture.Count > 0)
            Console.WriteLine("  ours, due after the capture stopped -- untested, not a "
                + $"divergence ({result.OursBeyondCapture.Count}): {Ids(result.OursBeyondCapture)}");
        if (result.CaptureBeyondOurs.Count > 0)
            Console.WriteLine("  capture, due after our run ended -- untested, not a "
                + $"divergence ({result.CaptureBeyondOurs.Count}): {Ids(result.CaptureBeyondOurs)}");

        if (result.AnchorId is null)
        {
            Console.WriteLine($"stamp alignment unavailable: {result.AlignmentRefusal}.");
        }
        else
        {
            Console.WriteLine($"anchor {result.AnchorId}: ours stamp {result.OursOrigin}, "
                + $"capture stamp {result.CaptureOrigin}");
            Console.WriteLine("stamp-aligned turns (capture residual = capture - ours):");
            Console.WriteLine("  id       ours  capture  residual");
            foreach (TriggerAlignedEvent e in result.AlignedEvents)
            {
                string ours = e.OursTurnIsCeiling ? $"<={e.OursTurn}" : e.OursTurn.ToString();
                string capture = e.CaptureTurnIsCeiling
                    ? $"<={e.CaptureTurn}"
                    : e.CaptureTurn.ToString();
                string residualText = e.Residual.HasValue ? $"{e.Residual.Value:+#;-#;0}" : "unknown";
                Console.WriteLine($"  {e.Id,-6} {ours,6} {capture,8} {residualText,9}");
            }

            if (result.AlignedEvents.Any(e => e.Residual is not null and not 0))
                Console.WriteLine("  note: a capture turn is when the probe SAW the change, "
                    + "measured as one tick after\n        the game acted (run 8: timer 1 "
                    + "stored 1724, first seen at 1725). On a flag,\n        a +1 residual is "
                    + "agreement; the stamps below are the unsampled check.");
        }

        if (result.StampSeries.Count > 0)
        {
            Console.WriteLine("timer stamps, stored and origin-relative (no sampling in the path):");
            Console.WriteLine("  id       ours                 capture");
            foreach (TriggerStampSeries s in result.StampSeries)
            {
                string verdict = s.Agrees ? "same" : "DIFFER";
                if (s.Truncated) verdict += ", to the shorter run's window";
                Console.WriteLine($"  {s.Id,-6} {string.Join(", ", s.Ours),-20} "
                    + $"{string.Join(", ", s.Capture),-20} {verdict}");
            }
        }

        string match = result.EventSetsMatch ? "matched" : "differed";
        string largest = result.LargestAbsoluteResidual is long residual
            ? $"{residual} tick{(residual == 1 ? string.Empty : "s")}"
            : "unavailable";
        string stamps = result.StampSeries.Count == 0
            ? "no timer stamped on both sides"
            : result.StampsAgree ? "stamps agree exactly" : "STAMPS DIFFER";
        Console.WriteLine($"verdict: event set {match}; {stamps}; "
            + $"largest absolute sampled residual {largest}");
        return result.EventSetsMatch && result.StampsAgree ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
        or InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

static int DiffTriggerCsv(string leftPath, string rightPath)
{
    string[][] ReadRows(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0] != TriggerTurnCsvEmitter.Header)
            throw new InvalidDataException($"'{path}' does not have the live trigger-probe header.");
        return lines.Skip(1).Where(l => l.Length != 0).Select(l => l.Split(',')).ToArray();
    }

    try
    {
        string[] columns = TriggerTurnCsvEmitter.Header.Split(',');

        // Rows pair by TURN, never by position. A live capture starts at
        // whatever turn the probe attached on and may sample one turn twice, so
        // pairing by row silently compares different turns -- an answer that
        // looks like a divergence report and is noise.
        Dictionary<int, string[]> Index(string[][] rows, string path)
        {
            var byTurn = new Dictionary<int, string[]>();
            foreach (string[] row in rows)
            {
                if (row.Length != columns.Length)
                    throw new InvalidDataException(
                        $"'{path}' has a row of {row.Length} columns; expected {columns.Length}.");
                if (int.TryParse(row[1], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int t))
                    byTurn[t] = row;
            }
            return byTurn;
        }

        Dictionary<int, string[]> left = Index(ReadRows(leftPath), leftPath);
        Dictionary<int, string[]> right = Index(ReadRows(rightPath), rightPath);
        int[] turns = left.Keys.Intersect(right.Keys).OrderBy(t => t).ToArray();
        if (turns.Length == 0)
        {
            Console.Error.WriteLine("the two files share no turn number; nothing to compare.");
            return 1;
        }

        var compared = new int[columns.Length];
        var unknown = new int[columns.Length];
        var different = new int[columns.Length];
        (string Turn, string Column, string Left, string Right)? first = null;

        Console.WriteLine($"turns {turns[0]}..{turns[^1]} ({turns.Length} shared; "
            + $"{left.Count} left rows, {right.Count} right rows)");

        foreach (int turn in turns)
        {
            string[] a = left[turn];
            string[] b = right[turn];
            for (int col = 0; col < columns.Length; col++)
            {
                if (a[col].Length == 0 || b[col].Length == 0)
                {
                    unknown[col]++;
                    continue;
                }

                compared[col]++;
                if (a[col] == b[col]) continue;
                different[col]++;
                first ??= (turn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    columns[col], a[col], b[col]);
            }
        }

        if (first is { } f)
            Console.WriteLine($"first divergence: turn {f.Turn}, column {f.Column}: left='{f.Left}' right='{f.Right}'");
        else
            Console.WriteLine("no comparable values diverged");

        Console.WriteLine("per-column summary (different/compared, unknown):");
        for (int col = 0; col < columns.Length; col++)
            Console.WriteLine($"  {columns[col],-16} {different[col]}/{compared[col]}, {unknown[col]} unknown");
        return first is null ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

/// <summary>
/// One line per placed THING, showing the fields the world projection reads.
///
/// <para>Coordinates print as bare tile indices because that is what they are.
/// This was worth checking rather than assuming: the simulation carries
/// positions in fixed-point subtile units (4096 to the tile), and a THING's X
/// and Y are 32-bit ints, so the natural guess is that they are the same units.
/// They are not. Every coordinate in the shipped corpus is below the level's
/// width and height, and `kwd things` with no argument is what re-checks
/// it.</para>
/// </summary>
static string DescribeThing(ThingRecord record)
{
    static string Pos(int x, int y) => $"({x,3},{y,3})";

    return record switch
    {
        ObjectThing t =>
            $"OBJECT       {Pos(t.X, t.Y)} objectId={t.ObjectId,3} player={t.PlayerId,2} "
            + $"gold={t.MoneyAmount,6} spell={t.KeeperSpellId,3} trigger={t.TriggerId}",
        TrapThing t =>
            $"TRAP         {Pos(t.X, t.Y)} trapId={t.TrapId,3} player={t.PlayerId,2} shots={t.NumberOfShots}",
        DoorThing t =>
            $"DOOR         {Pos(t.X, t.Y)} doorId={t.DoorId,3} player={t.PlayerId,2} flags=0x{t.Flags:X2} trigger={t.TriggerId}",
        ActionPointThing t =>
            $"ACTION_POINT {Pos(t.StartX, t.StartY)}..{Pos(t.EndX, t.EndY)} id={t.ActionPointId,3} "
            + $"next={t.NextWaypointId,3} wait={t.WaitDelay,5} flags=0x{t.Flags:X4} trigger={t.TriggerId,4} name={t.Name}",
        KeeperCreatureThing t =>
            $"KEEPER_CREAT {Pos(t.X, t.Y)} z={t.Z,7} creatureId={t.CreatureId,3} player={t.PlayerId,2} "
            + $"level={t.Level,2} hp={t.InitialHealth,6} gold={t.GoldHeld,5} flags=0x{t.Flags:X2} trigger={t.TriggerId}",
        NeutralCreatureThing t =>
            $"NEUTRAL_CREAT{Pos(t.X, t.Y)} z={t.Z,7} creatureId={t.CreatureId,3} "
            + $"level={t.Level,2} hp={t.InitialHealth,6} gold={t.GoldHeld,5} flags=0x{t.Flags:X2} trigger={t.TriggerId}",
        GoodCreatureThing t =>
            $"GOOD_CREAT   {Pos(t.X, t.Y)} z={t.Z,7} creatureId={t.CreatureId,3} "
            + $"level={t.Level,2} hp={t.InitialHealth,6} gold={t.GoldHeld,5} objective={t.Objective,3} "
            + $"targetAp={t.ObjectiveTargetActionPointId,3} targetPlayer={t.ObjectiveTargetPlayerId,2} trigger={t.TriggerId}",
        HeroPartyThing t =>
            $"HERO_PARTY   partyId={t.PartyId,3} trigger={t.TriggerId,4} name={t.Name}"
            + string.Concat(t.Members
                .Where(m => m.CreatureId != 0)
                .Select(m => Environment.NewLine + "    member   " + DescribeThing(m))),
        RoomThing t =>
            $"ROOM         {Pos(t.X, t.Y)} roomType={t.RoomType,3} player={t.PlayerId,2} "
            + $"direction={t.Direction,3} hp={t.InitialHealth,6}",
        EffectGeneratorThing t =>
            $"EFFECT_GEN   {Pos(t.X, t.Y)} generatorId={t.GeneratorId,3} frequency={t.Frequency,3} "
            + $"effects=[{string.Join(",", t.EffectIds)}]",
        DeadBodyThing t =>
            $"DEAD_BODY    {Pos(t.X, t.Y)} creatureId={t.CreatureId,3} player={t.PlayerId,2} gold={t.GoldHeld}",
        CameraThing t =>
            $"CAMERA       {Pos(t.X, t.Y)} z={t.Z,7} cameraId={t.CameraId,3}",
        _ => $"THING tag={record.Tag} {record.GetType().Name}",
    };
}

/// <summary>
/// The tile a placed THING occupies, or nothing for the records that carry no
/// position. Deliberately independent of the world projection in
/// <c>DK2.Simulation</c>: this is the auditor, and an auditor that shares the
/// thing it audits proves nothing.
///
/// <para>HERO_PARTY members are excluded because their X and Y are zero in
/// every shipped record — a party is placed by the trigger that spawns it at an
/// action point, not by coordinates in the file.</para>
/// </summary>
static IEnumerable<(int X, int Y)> ThingTiles(ThingRecord record)
{
    switch (record)
    {
        case ObjectThing t: yield return (t.X, t.Y); break;
        case TrapThing t: yield return (t.X, t.Y); break;
        case DoorThing t: yield return (t.X, t.Y); break;
        case ActionPointThing t: yield return (t.StartX, t.StartY); yield return (t.EndX, t.EndY); break;
        case CreatureThing t: yield return (t.X, t.Y); break;
        case RoomThing t: yield return (t.X, t.Y); break;
        case EffectGeneratorThing t: yield return (t.X, t.Y); break;
        case DeadBodyThing t: yield return (t.X, t.Y); break;
        case CameraThing: break;
        case HeroPartyThing: break;
    }
}


/// <summary>
/// The repository root, found by walking up for <c>DK2.slnx</c>.
///
/// <para>Every path this subcommand reads is relative to the repository, not
/// to wherever the tool happened to be launched from.</para>
/// </summary>
static string? FindRepoRoot()
{
    DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "DK2.slnx"))) return dir.FullName;
        dir = dir.Parent;
    }
    return null;
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
static (int Total, int Gated) CountTests(string root, List<string> problems)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (string a in new[] { "test", "DK2.slnx", "-c", "Release", "--no-build", "--list-tests" })
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
static string DescribeRuns(int[] ids)
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
static string DescribeGaps(int[] ids)
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


/// <summary>One named option's value, or null.</summary>
static string? OptionValue(string[] args, string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

/// <summary>Runs git in the repository and returns stdout, or empty on failure.</summary>
static string RunGit(string root, params string[] arguments)
{
    var psi = new ProcessStartInfo("git")
    {
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (string a in arguments) psi.ArgumentList.Add(a);

    try
    {
        using Process? process = Process.Start(psi);
        if (process is null) return string.Empty;
        // Drain stderr concurrently: forty changed files' line-ending warnings fill the pipe
        // and deadlock a reader that waits for stdout first.
        Task<string> errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        errors.Wait();
        process.WaitForExit();
        return output;
    }
    catch (Exception)
    {
        return string.Empty;
    }
}

/// <summary>Working-tree, staged and untracked paths, relative to the root.</summary>
static IEnumerable<string> ChangedFiles(string root, string baseRef)
{
    var set = new SortedSet<string>(StringComparer.Ordinal);
    void Collect(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string t = line.Trim();
            if (t.Length > 0) set.Add(t);
        }
    }

    Collect(RunGit(root, "diff", "--name-only", baseRef));
    Collect(RunGit(root, "diff", "--cached", "--name-only", baseRef));
    Collect(RunGit(root, "ls-files", "--others", "--exclude-standard"));
    return set;
}

/// <summary>
/// The paths a commit would actually carry. Deletions are excluded — there is
/// nothing left in them to measure.
/// </summary>
static IEnumerable<string> StagedFiles(string root)
    => RunGit(root, "diff", "--cached", "--name-only", "--diff-filter=d").Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0);

/// <summary>
/// One blob's lines, from the index (<c>:path</c>) or a commit (<c>ref:path</c>).
///
/// <para>Git returns LF-terminated text whatever the working tree uses, so the
/// carriage return and the trailing empty element are stripped here. A stray
/// <c>\r</c> adds one to every measured line length, which on the question-row
/// budget is the difference between passing and failing.</para>
/// </summary>
static string[] GitLines(string root, string spec)
{
    string text = RunGit(root, "show", spec);
    if (text.Length == 0) return [];
    if (text.EndsWith('\n')) text = text[..^1];
    return text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
}

/// <summary>Every tracked path, for the --all backlog report.</summary>
static IEnumerable<string> AllTrackedFiles(string root)
    => RunGit(root, "ls-files").Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0);

/// <summary>
/// The 1-based line numbers this change adds to one file.
///
/// <para>A file with no readable diff is treated as entirely new, which is the
/// safe direction: a checker that stays silent because it could not read the
/// diff is worse than one that over-reports.</para>
/// </summary>
static HashSet<int> AddedLineNumbers(string root, string baseRef, string relative, bool staged)
{
    var added = new HashSet<int>();

    // Staged mode asks only the index. Falling back to the working tree there
    // would reintroduce exactly the leak this mode exists to close.
    string diff = staged
        ? RunGit(root, "diff", "--cached", "-U0", baseRef, "--", relative)
        : RunGit(root, "diff", "-U0", baseRef, "--", relative);
    if (!staged && diff.Trim().Length == 0)
        diff = RunGit(root, "diff", "--cached", "-U0", baseRef, "--", relative);

    if (diff.Trim().Length == 0)
    {
        string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        int count = staged ? GitLines(root, $":{relative}").Length
            : File.Exists(full) ? File.ReadAllLines(full).Length
            : 0;
        for (int i = 1; i <= count; i++) added.Add(i);
        return added;
    }

    foreach (string line in diff.Split('\n'))
    {
        Match hunk = Regex.Match(line, @"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@");
        if (!hunk.Success) continue;

        int start = int.Parse(hunk.Groups[1].Value);
        int count = hunk.Groups[2].Success ? int.Parse(hunk.Groups[2].Value) : 1;
        for (int i = 0; i < count; i++) added.Add(start + i);
    }

    return added;
}



/// <summary>
/// Walks one level's trigger roots the way <c>TriggerExecutor</c> would, descending
/// only where a headless run could get through, and reports what that reaches.
/// </summary>
/// <remarks>
/// Condition types 1, 2 and 34 are the whole of the passable set: a flag and a timer
/// are the script's own state and level time is the clock. Every other type either
/// measures a dungeon somebody is playing or is one the executor declines, and both
/// stop the walk in exactly the same place the executor stops it.
/// </remarks>
static OracleReach MeasureOracleReach(
    string install,
    string level,
    int turns,
    List<TriggerRecord> records,
    LevelDef def,
    List<ThingRecord> things,
    List<PlayerDef> players)
{
    var conditions = new Dictionary<ushort, TriggerRecord>();
    var actions = new Dictionary<ushort, TriggerRecord>();
    foreach (TriggerRecord record in records)
    {
        if (record.Tag == 213) conditions[record.TriggerId] = record;
        else if (record.Tag == 214) actions[record.TriggerId] = record;
    }

    var seen = new HashSet<(int Tag, ushort Id)>();
    var flags = new HashSet<byte>();
    var timers = new HashSet<byte>();
    var armed = new HashSet<int>();

    void Walk(short id)
    {
        while (id != 0)
        {
            bool isCondition = id > 0;
            ushort raw = unchecked((ushort)id);
            TriggerRecord? record = isCondition
                ? conditions.GetValueOrDefault(raw)
                : actions.GetValueOrDefault(raw);

            // A record already seen has had its whole next chain followed.
            if (record is null || !seen.Add((record.Tag, raw))) return;

            bool descend;
            if (isCondition)
            {
                descend = record.Type is 1 or 2 or 34;
            }
            else
            {
                descend = record.Type is 7 or 8 or 14;
                if (record.Type == 7 && record.Parameters.TryGetValue("FlagId", out long flag))
                    flags.Add(unchecked((byte)flag));
                if (record.Type == 8 && record.Parameters.TryGetValue("TimerId", out long timer))
                    timers.Add(unchecked((byte)timer));
                if (record.Type == 14 && record.Parameters.TryGetValue("PartyId", out long party))
                    armed.Add(unchecked((byte)party));
            }

            if (descend) Walk(unchecked((short)record.ChildTriggerId));
            id = unchecked((short)record.NextTriggerId);
        }
    }

    if (def.TriggerId != 0) Walk(unchecked((short)def.TriggerId));
    foreach (PlayerDef player in players)
        if (player.TriggerId != 0) Walk(unchecked((short)player.TriggerId));
    foreach (ActionPointThing point in things.OfType<ActionPointThing>())
        if (point.TriggerId != 0) Walk(unchecked((short)point.TriggerId));

    // A party subtree starts disabled and only a type 14 that actually ran arms it,
    // so those roots join the walk as they are reached rather than at the start.
    HeroPartyThing[] parties = things.OfType<HeroPartyThing>().ToArray();
    int before;
    do
    {
        before = seen.Count;
        foreach (HeroPartyThing party in parties)
        {
            if (!armed.Contains(party.PartyId)) continue;
            if (party.TriggerId != 0) Walk(unchecked((short)party.TriggerId));
            foreach (GoodCreatureThing member in party.Members)
                if (member.CreatureId != 0 && member.TriggerId != 0)
                    Walk(unchecked((short)member.TriggerId));
        }
    }
    while (seen.Count != before);

    var allFlags = new HashSet<byte>();
    var allTimers = new HashSet<byte>();
    foreach (TriggerRecord record in records)
    {
        if (record.Tag != 214) continue;
        if (record.Type == 7 && record.Parameters.TryGetValue("FlagId", out long flag))
            allFlags.Add(unchecked((byte)flag));
        if (record.Type == 8 && record.Parameters.TryGetValue("TimerId", out long timer))
            allTimers.Add(unchecked((byte)timer));
    }

    int creatureRoots = things.OfType<GoodCreatureThing>().Count(c => c.TriggerId != 0)
        + things.OfType<KeeperCreatureThing>().Count(c => c.TriggerId != 0);

    (int moved, int after, int stalls, int gates) =
        RunOracleLevel(install, level, turns, records, def, things, players);

    return new OracleReach(
        level, flags.Count, timers.Count, seen.Count, records.Count,
        allFlags.Count, allTimers.Count, creatureRoots, moved, after, stalls, gates);
}

/// <summary>
/// Runs one level headless and counts the flag and timer ids that ever leave zero,
/// and how many of those do it after the opening turn.
/// </summary>
/// <remarks>Turn 1 is where a level's unconditional opening fires, and a played
/// capture agrees with us there for free. Everything a comparison can actually learn
/// from is in the columns that move later.</remarks>
static (int Moved, int MovedAfterFirstTurn, int Stalls, int Gates) RunOracleLevel(
    string install,
    string level,
    int turns,
    List<TriggerRecord> records,
    LevelDef def,
    List<ThingRecord> things,
    List<PlayerDef> players)
{
    LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
    if (map is null) return (0, 0, 0, 0);

    GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, level);
    WorldState world = WorldState.FromMap(
        database, map, players.Select(p => p.PlayerId).ToArray());
    ThingProjection.Place(world, database, things);

    var tree = new TriggerTree(
        records, def.TriggerId,
        things.OfType<HeroPartyThing>(),
        players.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
    var simulation = new GameSimulation(database, world, seed: 0, triggers: tree)
    {
        TrackTriggerReach = true,
    };

    var firstTurn = new Dictionary<string, int>();
    var stalls = new HashSet<ushort>();
    var gated = new HashSet<ushort>();
    for (int turn = 1; turn <= turns; turn++)
    {
        simulation.Tick();
        foreach (BlockedTrigger b in simulation.LastTriggerRun?.Blocked ?? [])
            if (b.Tag == 213) stalls.Add(b.TriggerId);
        // Union across turns, like `stalls`: a decline on turn 300 forecloses records the
        // turn-1 walk had no reason to want.
        if (simulation.LastTriggerRun is { } run)
            foreach (TriggerGate gate in TriggerReach.Gated(tree, run))
                gated.UnionWith(gate.Foreclosed);
        TriggerState state = world.Triggers;
        for (byte id = 0; id < TriggerState.FlagCount; id++)
            if (state.Read(id) != 0) firstTurn.TryAdd($"f{id}", turn);
        for (byte id = 0; id < TriggerState.TimerCount; id++)
            if (state.IsStarted(id)) firstTurn.TryAdd($"t{id}", turn);
    }

    return (firstTurn.Count, firstTurn.Values.Count(t => t > 1), stalls.Count, gated.Count);
}

/// <summary>One level's suitability as a two-sided experiment. See `kwd oracle`.</summary>
record OracleReach(
    string Level,
    int Flags,
    int Timers,
    int Reached,
    int Records,
    int AllFlags,
    int AllTimers,
    int CreatureRoots,
    int Moved,
    int MovedAfterFirstTurn,
    int Stalls,
    int Gates);

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

/// <summary>Counts what the replayed tags did, from the events the tag system already emits.</summary>
sealed class TagTally : ISimulationEventSink
{
    private int _applied;
    private int _rejected;

    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.System != "TileTag") return;
        foreach (string part in simEvent.Detail.Split(' '))
        {
            if (part.StartsWith("applied=", StringComparison.Ordinal))
                _applied += int.Parse(part[8..], System.Globalization.CultureInfo.InvariantCulture);
            else if (part.StartsWith("rejected=", StringComparison.Ordinal))
                _rejected += int.Parse(part[9..], System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Rejections are the first-capture check. The pool's layout has been read but
    /// never sampled, so if its coordinates are not tile coordinates every tag lands
    /// off the map or on floor and this is where that shows, rather than in a
    /// divergence report that blames a rule.
    /// </summary>
    public void Report()
    {
        Console.WriteLine($"  tags: {_applied} applied, {_rejected} rejected");
        if (_applied == 0 && _rejected > 0)
            Console.WriteLine("  NOTHING landed -- suspect the pool's coordinates rather "
                + "than the level");
    }
}
