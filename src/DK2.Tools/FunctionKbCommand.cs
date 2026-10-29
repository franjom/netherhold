// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Tools;

/// <summary>dk2tools kb check|index|apply-names. The gate half of AGENTS.md's knowledge-base rule.</summary>
static class FunctionKbCommand
{
    const string Pointer = "See AGENTS.md, the function knowledge base rule: a function named in a change has a record; a changed verdict has a dated History line.";

    public static int Run(string[] args)
    {
        string? root = Repo.FindRepoRoot();
        if (root is null) { Console.Error.WriteLine("not inside the repository."); return 2; }
        return args[0] switch
        {
            "check" => Check(root, args),
            "index" => Index(root),
            "apply-names" => ApplyNames(root, args.Contains("--dry-run")),
            _ => Usage(),
        };
    }

    static int Usage()
    {
        Console.Error.WriteLine("usage: dk2tools kb check [--all|--staged|--since REF] | kb index | kb apply-names [--dry-run]");
        return 2;
    }

    const string NamesTsv = "reverse-engineering/.cache/kb-names.tsv";
    const string NamesOut = "reverse-engineering/.cache/kb-apply-names.txt";

    /// <summary>
    /// Writes the records' names into the canonical Ghidra project (ghidra.py --write, one writer at a time),
    /// so every later clone, headless run and MCP session sees them. --dry-run writes the table and prints
    /// the command without running it.
    /// </summary>
    static int ApplyNames(string root, bool dryRun)
    {
        try { return ApplyNamesCore(root, dryRun); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"kb apply-names: {e.Message}");
            return 2;
        }
    }

    static int ApplyNamesCore(string root, bool dryRun)
    {
        var errors = new List<string>();
        var records = Load(new Source(root, staged: false), errors);
        if (errors.Count > 0) { foreach (string e in errors) Console.Error.WriteLine(e); return 1; }
        string tsv = Path.Combine(root, NamesTsv.Replace('/', Path.DirectorySeparatorChar));
        string output = Path.Combine(root, NamesOut.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(tsv)!);
        string table = FunctionKbNames.Table(records);
        File.WriteAllText(tsv, table, new System.Text.UTF8Encoding(false));
        Console.WriteLine($"kb apply-names: {table.Count(c => c == '\n')} named of {records.Count} records -> {NamesTsv}");

        var psi = new System.Diagnostics.ProcessStartInfo("python") { WorkingDirectory = root, UseShellExecute = false };
        foreach (string a in (string[])["tools/ghidra/ghidra.py", "--write", "ApplyNames.py", output, tsv]) psi.ArgumentList.Add(a);
        if (dryRun)
        {
            Console.WriteLine("would run: python " + string.Join(" ", psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            return 0;
        }
        if (File.Exists(output)) File.Delete(output);
        using var process = System.Diagnostics.Process.Start(psi) ?? throw new IOException("could not start python");
        process.WaitForExit();
        string last = File.Exists(output) ? File.ReadLines(output).LastOrDefault() ?? "" : "";
        Console.WriteLine(last);
        if (process.ExitCode != 0) return process.ExitCode;
        if (last.Length == 0) { Console.Error.WriteLine($"kb apply-names: {NamesOut} was not written"); return 1; }
        // A row Ghidra refused (duplicate or invalid name, bad address) is a failure even though the run succeeded.
        return System.Text.RegularExpressions.Regex.IsMatch(last, @"\bfailed=[1-9]") ? 1 : 0;
    }

    static int Index(string root)
    {
        try { return IndexCore(root); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"kb index: {e.Message}");
            return 2;
        }
    }

    static int IndexCore(string root)
    {
        var errors = new List<string>();
        var records = Load(new Source(root, staged: false), errors);
        if (errors.Count > 0) { foreach (string e in errors) Console.Error.WriteLine(e); return 1; }
        string path = Path.Combine(root, FunctionKb.Folder.Replace('/', Path.DirectorySeparatorChar), "INDEX.md");
        File.WriteAllText(path, FunctionKb.Index(records), new System.Text.UTF8Encoding(false));
        Console.WriteLine($"kb index: {records.Count} records -> {FunctionKb.Folder}/INDEX.md");
        return 0;
    }

    /// <summary>
    /// Where the check reads from: the working tree, or the commit. In staged mode a path exists when the
    /// index holds it, and its lines come from disk unless the tree differs from the index, when they come
    /// from the index blob. Three git calls in all, however many records there are; reads are memoised.
    /// </summary>
    internal sealed class Source
    {
        readonly string root;
        readonly HashSet<string>? tracked;   // null in tree mode
        readonly HashSet<string>? dirty;
        readonly Dictionary<string, string[]> cache = new(StringComparer.Ordinal);

        public Source(string root, bool staged)
        {
            this.root = root;
            if (!staged) return;
            tracked = Paths(Repo.RunGit(root, "ls-files", "--cached"));
            dirty = Paths(Repo.RunGit(root, "diff", "--name-only"));
        }

        /// <summary>Paths git listed for the index; 0 in staged mode means git answered nothing.</summary>
        public int TrackedCount => tracked?.Count ?? 0;
        public string Full(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        public bool Exists(string relative) => tracked is null ? File.Exists(Full(relative)) : tracked.Contains(relative);

        public string[] Lines(string relative)
        {
            if (cache.TryGetValue(relative, out var lines)) return lines;
            return cache[relative] = dirty is not null && dirty.Contains(relative) ? Repo.GitLines(root, $":{relative}")
                : File.Exists(Full(relative)) ? File.ReadAllLines(Full(relative))
                : [];
        }

        /// <summary>Record paths at the top level of the folder, INDEX.md excluded; the index's view in staged mode.</summary>
        public IEnumerable<string> RecordPaths()
        {
            string dir = Full(FunctionKb.Folder);
            IEnumerable<string> paths = tracked is null
                ? Directory.Exists(dir) ? Directory.GetFiles(dir, "*.md").Select(f => FunctionKb.Folder + "/" + Path.GetFileName(f)) : []
                : tracked.Where(p => p.StartsWith(FunctionKb.Folder + "/", StringComparison.Ordinal)
                    && !p[(FunctionKb.Folder.Length + 1)..].Contains('/'));
            return paths.Where(p => p.EndsWith(".md", StringComparison.Ordinal) && !IsIndex(p)).Order(StringComparer.Ordinal);
        }

        public static bool IsIndex(string relative) => relative.EndsWith("/INDEX.md", StringComparison.Ordinal);

        static HashSet<string> Paths(string text)
            => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);
    }

    internal static List<FunctionRecord> Load(Source source, List<string> errors)
        => source.RecordPaths().Select(p => FunctionKb.Parse(p, source.Lines(p), errors)).ToList();

    static int Check(string root, string[] args)
    {
        try { return CheckCore(root, args); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"kb check: {e.Message}");
            return 2;
        }
    }

    static int CheckCore(string root, string[] args)
    {
        bool all = args.Contains("--all");
        bool staged = args.Contains("--staged");
        string baseRef = Repo.OptionValue(args, "--since") ?? "HEAD";
        var findings = new List<string>();
        var source = new Source(root, staged);

        var records = Load(source, findings);
        if (staged && source.TrackedCount == 0)
        {
            string dir = source.Full(FunctionKb.Folder);
            if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.md").Any())
            {
                Console.Error.WriteLine("kb check: git returned no index; refusing to pass silently");
                return 2;
            }
        }
        findings.AddRange(FunctionKb.CheckSet(records, source.Exists, source.Lines));

        if (!all)
        {
            var recorded = records.Select(r => r.Address).ToHashSet(StringComparer.Ordinal);
            IEnumerable<string> files = staged ? Repo.StagedFiles(root) : Repo.ChangedFiles(root, baseRef);
            var changes = new List<(string, string[])>();
            var today = DateOnly.FromDateTime(DateTime.Now);
            bool merging = Repo.RunGit(root, "rev-parse", "-q", "--verify", "MERGE_HEAD").Trim().Length > 0;
            foreach (string relative in files)
            {
                if (!(relative.StartsWith("docs/", StringComparison.Ordinal) || relative.StartsWith("src/", StringComparison.Ordinal))) continue;
                string[] lines = source.Lines(relative);
                HashSet<int> added = Repo.AddedLineNumbers(root, baseRef, relative, staged);
                changes.Add((relative, added.Where(n => n >= 1 && n <= lines.Length).Select(n => lines[n - 1]).ToArray()));

                var now = records.FirstOrDefault(r => r.RelativePath == relative);
                if (now is null) continue;
                // A record new in this change has no base blob; RunGit swallows git's error and GitLines yields [].
                string[] baseLines = Repo.GitLines(root, $"{baseRef}:{relative}");
                var before = baseLines.Length == 0 ? null : FunctionKb.Parse(relative, baseLines, []);
                string[] mergedLines = merging ? Repo.GitLines(root, $"MERGE_HEAD:{relative}") : [];
                var merged = mergedLines.Length == 0 ? null : FunctionKb.Parse(relative, mergedLines, []);
                IReadOnlyCollection<DateOnly> days = staged ? [today] : [today, .. Repo.CommitDays(root, baseRef, relative)];
                findings.AddRange(FunctionKb.HistoryRequired(now, before, days, merged));
            }
            findings.AddRange(FunctionKb.MissingRecords(changes, recorded));
        }

        if (findings.Count == 0)
        {
            Console.WriteLine($"kb check: {records.Count} records, no findings.");
            return 0;
        }
        Console.Error.WriteLine($"{findings.Count} KB finding(s):");
        foreach (string f in findings) Console.Error.WriteLine("  " + f);
        Console.Error.WriteLine();
        Console.Error.WriteLine(Pointer);
        return 1;
    }
}

/// <summary>The table dk2tools kb apply-names hands to tools/ghidra/scripts/ApplyNames.py.</summary>
public static class FunctionKbNames
{
    /// <summary>ADDRESS&lt;TAB&gt;name rows by address: every record whose name is not Ghidra's FUN_ default,
    /// except SUPERSEDED ones, whose address may not even be a function entry.</summary>
    public static string Table(IEnumerable<FunctionRecord> records)
        => string.Concat(records
            .Where(r => r.Address.Length > 0 && r.Name.Length > 0 && !r.Name.StartsWith("FUN_", StringComparison.Ordinal))
            .Where(r => r.Status != "SUPERSEDED")
            .OrderBy(r => r.Address, StringComparer.Ordinal)
            .Select(r => $"{r.Address}\t{GhidraName(r)}\n"));

    /// <summary>The name a decompile shows: a CONFIRMED record's name as it is, any other with its
    /// confidence appended (Treasury_Tick_LIKELY), because a plain name in Ghidra reads as settled
    /// (owner, 2026-10-14).</summary>
    public static string GhidraName(FunctionRecord record)
        => record.Confidence is "CONFIRMED" or "" ? record.Name : $"{record.Name}_{record.Confidence}";
}
