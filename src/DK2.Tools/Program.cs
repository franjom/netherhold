// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Text.RegularExpressions;
using DK2.Assets;
using DK2.Convert;
using DK2.Convert.Audio;
using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Data.Wad;
using DK2.Simulation;

// DK2.Tools — command-line utilities for inspecting original game data.

if (args.Length == 0)
    return Usage();

switch (args[0])
{
    case "trigger-diff" when args.Length == 3:
        return DiffTriggerCsv(args[1], args[2]);

    case "trigger-run" when args.Length == 4:
        return RunTriggerCsv(
            args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture), args[3]);

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
        //
        // That line has gone stale three times -- revision 6 by six commits,
        // revision 7 by three, revision 10 by a whole session -- and the plan
        // diagnoses the cause itself: "every number here is a one-line shell
        // command, so it should be a dk2tools status subcommand". This is that
        // subcommand. Every figure below is read from a file or from test
        // discovery; none of it is a constant in this source.
        //
        // --fast skips test discovery, which is the only slow part.
        // --check exits 1 if the plan's committed line disagrees, so this can
        //         gate a commit rather than merely offering the truth.
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

    // Q74: is there ANY way a dug-out tile becomes rock again? The owner says no
    // keeper can fill one in, and the terrain table plainly carries a fill-in
    // transition. The remaining candidate is the level script: DK2 has an
    // ALTER_TERRAIN_TYPE trigger action (type 22) that writes a terrain id
    // straight onto a tile. This sweeps every shipped map's TRIGGERS chunk,
    // decodes those actions, and reports which terrain they write - so the
    // question is answered from the install rather than from anybody's word.
    //
    // Layout HYPOTHESIS from OpenKeeper's KwdFile.java (facts, not code): the
    // action type is the second-to-last byte of the payload and the repeat count
    // the last, and an ALTER_TERRAIN_TYPE body is terrainId(u8), playerId(u8),
    // two nulls, posX(u16), posY(u16). Verified here by whether every decoded
    // action type lands inside the enumerated range and every terrain id names a
    // terrain the database has.
    case "kwd" when args.Length >= 2 && args[1] == "portals":
    {
        // Which shipped levels have more than one portal, and who starts with
        // them. Written for Q86: the per-room / keeper-wide tally question needs
        // a level where a keeper can hold two portals at once, and hunting for
        // one by playing is far more expensive than reading 66 grids.
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
        // The placed-entity dump. `kwd records` prints one line per THINGS
        // record saying only its type and length; this prints the fields the
        // projection into WorldState actually reads, so a claim about where
        // level 1's heart is can be re-checked from the shipped file.
        //
        // With a file argument it dumps that level. With none it sweeps every
        // <name>Things.kld in the install and prints the corpus distribution,
        // which is what bounds "how much of a level is a THING".
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

    // Offset hunting in a record we only partly understand. Give it a creature
    // and a value measured in the original, and it prints every offset whose
    // u8 / u16 / s16 reading holds that value for that creature, with the whole
    // column beside it so the reading can be judged plausible across all of
    // them. This is the technique that found the Create Imp mana cost in
    // KeeperSpells.kwd; a single hit is luck, a whole column that ranks the way
    // the game plays is not. See docs/research/imps-and-mana.md.
    // Raw window onto the CREATURES record, for judging a candidate offset
    // found by creaturescan. Prints the same byte range for every creature so a
    // column can be read down.
    // Generic offset hunt in a record type we do not parse. Give it a file and
    // a number measured in the original, and it prints every offset whose
    // u8/u16/s16/s32 reading holds that number in some record, with the whole
    // column beside it. Optionally anchor on one record index. This is the
    // technique that found the Create Imp mana cost at KeeperSpells 0x11C; a
    // single hit is luck, a column that ranks the way the game plays is not.
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

    // The object-record twin of creaturehex. Added 2026-09-07 while hunting the
    // obstacle radius the binary reads at objDef+0xf8 (Q88): the loader's frame
    // slot named a file offset, and the only way to check a frame-to-record
    // delta is to look at the column and see whether the numbers behave.
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

    // Which (offset, bit) in the CREATURES record selects exactly this set of
    // creatures? The twin of creaturescan, for flags rather than values.
    //
    // Added 2026-09-07 hunting the hero portal's creature flag. The binary reads
    // it as `byte ptr [def + 0x68a] & 8`, and the loader does not preserve file
    // offsets into the runtime record — so the runtime offset says nothing about
    // where the bit lives in the file. What *is* known is the roster the flag has
    // to select, and searching for the roster finds the bit. Reasoning from the
    // layout instead had already produced one wrong answer (file 0xF00, which
    // turned out to be a 3000/2800 value, not flags).
    // A field that is the same for every record cannot discriminate between
    // records, so any rule this project keys on one is either dead code or a
    // rule we invented. The balance-table equivalent of this sweep (ADR-0085)
    // found ten live mechanisms shipped switched off; the creature and object
    // records had never been swept at all. Q213.
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
    Console.WriteLine("  docs [--since <ref>] [--all]           check AGENTS.md's recording budgets on what");
    Console.WriteLine("                                         changed; --all reports the whole backlog");
    Console.WriteLine("       [--message <file>]                also check a commit message body");
    Console.WriteLine("  status [--fast] [--check]              regenerate the plan's status paragraph;");
    Console.WriteLine("                                         --check exits 1 if the plan is stale");
    Console.WriteLine("  trigger-diff <left.csv> <right.csv>   first divergent turn/column and column totals");
    Console.WriteLine("  wad extract <file.WAD> <entry> [out]   extract one entry");
    Console.WriteLine("  sdt list <file.sdt>                    list sound-bank entries and formats");
    Console.WriteLine("  sdt extract <file.sdt> [--out converted-assets/audio]");
    Console.WriteLine("                                         extract PCM as WAV, compressed audio raw");
    Console.WriteLine("  str list <file.str>                    list decoded string ids and values");
    Console.WriteLine("  str get <file.str> <id>                resolve one table-local string id");
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
    Console.WriteLine("  kwd creaturescan <name> <value>        the same, anchored on one creature");
    Console.WriteLine("  kwd creaturehex <offsetHex> <count>    raw byte window across every creature");
    Console.WriteLine("  kwd objecthex <offsetHex> <count> [-w]  the same across every object; -w reads u16s");
    return 1;
}

/// <summary>
/// Runs one campaign level's trigger tree for a number of turns and writes the
/// probe's CSV shape, so our side can be diffed against a live capture.
/// </summary>
static int RunTriggerCsv(string levelName, int turns, string outPath)
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
    if (map is null || level is null || records is null || players is null)
    {
        Console.Error.WriteLine($"'{levelName}' is missing map, level, trigger or player data.");
        return 2;
    }

    GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, levelName);
    WorldState world = WorldState.FromMap(
        database, map, players.Select(p => p.PlayerId).ToArray());
    var tree = new TriggerTree(records, level.TriggerId);
    var simulation = new GameSimulation(database, world, seed: 0, triggers: tree);

    using (var writer = new StreamWriter(outPath))
    {
        simulation.EmitTriggerTurnsTo(writer);
        for (int i = 0; i < turns; i++) simulation.Tick();
    }

    Console.WriteLine($"{levelName}: {turns} turns, root trigger {tree.RootTriggerId} "
        + $"repeat {tree.RootRepeat} -> {outPath}");
    foreach ((int tag, byte type, int count) in
             (simulation.LastTriggerRun?.Summary ?? []).OrderByDescending(s => s.Count))
        Console.WriteLine($"  blocked tag {tag} type {type}: {count}");
    return 0;
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
        output = process.StandardOutput.ReadToEnd();
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
        string output = process.StandardOutput.ReadToEnd();
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
