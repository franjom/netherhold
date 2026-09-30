# Function Knowledge Base Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.
> **Claude never commits** (CLAUDE.md). Where a step says *Commit*, Codex commits; Claude stages the named paths and hands over with the message.

**Goal:** One text record per DKII.EXE function, kept current by a gate, editable by Claude and Codex from any session, with Ghidra reduced to one canonical project and self-deleting clones.

**Architecture:** Records live in `docs/research/functions/<ADDRESS>.md` (design: `docs/plans/2026-09-30-function-knowledge-base-design.md`). `dk2tools kb` (C#, in DK2.Tools beside the docs gate) parses, checks, indexes and applies names; the commit-msg hook runs `kb check --staged`. `tools/kb/dk2kb.py` is a small stdio MCP server both agents spawn. `tools/ghidra/mcp-serve.py` and `tools/ghidra/ghidra.py` clone the canonical project per session or run.

**Tech Stack:** .NET 10 / C# (DK2.Tools, xunit tests), Python 3.13 with `uv` (PEP 723 scripts, `mcp` SDK), PyGhidra 12.1.4, PowerShell 5.1 for the one owner-run cleanup script.

**Build and test commands** (from the repo root):

```
dotnet build DK2.slnx -c Release
dotnet test tests/DK2.Tools.Tests/DK2.Tools.Tests.csproj -c Release --no-build --filter FullyQualifiedName~FunctionKb
dotnet src/DK2.Tools/bin/Release/net10.0/DK2.Tools.dll kb check --all
```

Read before starting: AGENTS.md (recording rules, ADR-0046), `.agents/skills/ghidra-queries/SKILL.md`, `src/DK2.Tools/Program.cs:237-450` (the docs gate you are extending), `tests/DK2.Tools.Tests/VerificationLedgerTests.cs` (test style).

---

### Task 1: Parse a record

**Files:**
- Create: `src/DK2.Tools/FunctionKb.cs`
- Test: `tests/DK2.Tools.Tests/FunctionKbTests.cs`

**Step 1: Write the failing tests**

```csharp
// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Tools;

namespace DK2.Tools.Tests;

public sealed class FunctionKbTests
{
    internal const string Sample = """
        address: 0x0050EA70
        name: WorldLog_Dump
        role: Prints every world field and creature state into MESSAGE.LOG.
        confidence: CONFIRMED
        status: READ
        evidence: docs/research/debug-dumpers.md#world-log
        implements: tools/worldlog.py

        The function prints its own field names, which is how the offsets were pinned.

        ## History
        - 2026-09-30 claude: created from debug-dumpers.md; CONFIRMED by the Cheat Engine call.
        """;

    static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    [Fact]
    public void ParsesEveryField()
    {
        var errors = new List<string>();
        FunctionRecord r = FunctionKb.Parse("docs/research/functions/0050EA70.md", Lines(Sample), errors)!;
        Assert.Empty(errors);
        Assert.Equal("0050EA70", r.Address);
        Assert.Equal("WorldLog_Dump", r.Name);
        Assert.Equal("CONFIRMED", r.Confidence);
        Assert.Equal("READ", r.Status);
        Assert.Equal(["docs/research/debug-dumpers.md#world-log"], r.Evidence);
        Assert.Equal(["tools/worldlog.py"], r.Implements);
        Assert.Contains("own field names", r.Body);
        Assert.Single(r.History);
        Assert.Equal("2026-09-30", r.History[0].Date);
        Assert.Equal("claude", r.History[0].Agent);
    }

    [Theory]
    [InlineData("address: 0x0050EA70", "address: 0x50EA70", "address")]           // not eight digits
    [InlineData("confidence: CONFIRMED", "confidence: SURE", "confidence")]
    [InlineData("status: READ", "status: DONE", "status")]
    [InlineData("name: WorldLog_Dump", "", "name")]                              // missing key
    public void RejectsABadHeader(string good, string bad, string key)
    {
        var errors = new List<string>();
        FunctionKb.Parse("docs/research/functions/0050EA70.md", Lines(Sample.Replace(good, bad)), errors);
        Assert.Contains(errors, e => e.Contains(key, StringComparison.Ordinal));
    }

    [Fact]
    public void TheFileNameMustBeTheAddress()
    {
        var errors = new List<string>();
        FunctionKb.Parse("docs/research/functions/00401000.md", Lines(Sample), errors);
        Assert.Contains(errors, e => e.Contains("file name", StringComparison.Ordinal));
    }

    [Fact]
    public void AHistoryLineNeedsDateAgentAndText()
    {
        var errors = new List<string>();
        FunctionKb.Parse("docs/research/functions/0050EA70.md",
            Lines(Sample.Replace("- 2026-09-30 claude: created", "- created")), errors);
        Assert.Contains(errors, e => e.Contains("History", StringComparison.Ordinal));
    }
}
```

**Step 2: Run the tests to verify they fail**

Run: `dotnet build DK2.slnx -c Release` — Expected: build error, `FunctionKb` does not exist.

**Step 3: Write the parser**

```csharp
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

    [GeneratedRegex(@"^0X(00[4-6][0-9A-F]{5})$")] private static partial Regex AddressRx();   // matched after ToUpperInvariant, so 0X
    [GeneratedRegex(@"^- (\d{4}-\d{2}-\d{2}) ([a-z][a-z0-9-]*): (.+)$")] private static partial Regex HistoryRx();
    [GeneratedRegex(@"\bFUN_(00[4-6][0-9A-Fa-f]{5})\b")] private static partial Regex MentionRx();

    public static FunctionRecord? Parse(string relativePath, string[] lines, List<string> errors)
    {
        string where = relativePath;
        var header = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        int i = 0;
        for (; i < lines.Length && lines[i].Trim().Length > 0; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) { errors.Add($"{where}:{i + 1}: header line is not 'key: value'"); continue; }
            string key = lines[i][..colon].Trim();
            string value = lines[i][(colon + 1)..].Trim();
            int hash = value.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) value = value[..hash].Trim();            // trailing comment
            if (!header.TryGetValue(key, out var list)) header[key] = list = [];
            list.Add(value);
        }

        string One(string key)
        {
            if (!header.TryGetValue(key, out var v) || v.Count == 0 || v[0].Length == 0)
            { errors.Add($"{where}: header lacks '{key}'"); return ""; }
            if (v.Count > 1) errors.Add($"{where}: '{key}' given {v.Count} times");
            return v[0];
        }
        IReadOnlyList<string> Many(string key) => header.TryGetValue(key, out var v) ? v : [];

        string addressText = One("address");
        var m = AddressRx().Match(addressText.ToUpperInvariant());
        string address = m.Success ? m.Groups[1].Value : "";
        if (!m.Success && addressText.Length > 0) errors.Add($"{where}: address '{addressText}' is not 0x00xxxxxx inside code");
        if (address.Length > 0 && !Path.GetFileNameWithoutExtension(relativePath).Equals(address, StringComparison.Ordinal))
            errors.Add($"{where}: file name must be the address {address}");

        string name = One("name"), role = One("role"), confidence = One("confidence"), status = One("status");
        if (confidence.Length > 0 && !Confidences.Contains(confidence)) errors.Add($"{where}: confidence '{confidence}' is not one of {string.Join("|", Confidences)}");
        if (status.Length > 0 && !Statuses.Contains(status)) errors.Add($"{where}: status '{status}' is not one of {string.Join("|", Statuses)}");
        if (Many("evidence").Count == 0) errors.Add($"{where}: header lacks 'evidence'");

        int historyAt = Array.FindIndex(lines, l => l.Trim().Equals("## History", StringComparison.Ordinal));
        if (historyAt < 0) { errors.Add($"{where}: no '## History' section"); historyAt = lines.Length; }
        string body = string.Join("\n", lines.Skip(i).Take(historyAt - i)).Trim();

        var history = new List<HistoryEntry>();
        for (int j = historyAt + 1; j < lines.Length; j++)
        {
            if (lines[j].Trim().Length == 0) continue;
            if (lines[j].StartsWith("  ", StringComparison.Ordinal) && history.Count > 0)
            { history[^1] = history[^1] with { Text = history[^1].Text + " " + lines[j].Trim() }; continue; }
            var h = HistoryRx().Match(lines[j]);
            if (!h.Success) { errors.Add($"{where}:{j + 1}: History line must be '- YYYY-MM-DD agent: text'"); continue; }
            history.Add(new HistoryEntry(h.Groups[1].Value, h.Groups[2].Value, h.Groups[3].Value));
        }
        if (history.Count == 0 && historyAt < lines.Length) errors.Add($"{where}: History is empty");

        return new FunctionRecord(relativePath, address, name, role, confidence, status, Many("evidence"), Many("implements"), body, history);
    }

    /// <summary>Every FUN_00xxxxxx in the text, as eight upper-case hex digits.</summary>
    public static IEnumerable<string> Mentions(string text)
        => MentionRx().Matches(text).Select(m => m.Groups[1].Value.ToUpperInvariant());
}
```

**Step 4: Run the tests to verify they pass**

Run the build and the filtered test command from the header. Expected: `Passed! - Failed: 0` with 7 tests.

**Step 5: Commit / hand over**

`git add src/DK2.Tools/FunctionKb.cs tests/DK2.Tools.Tests/FunctionKbTests.cs` — message: `parse a function record: header, reasoning, dated history`.

---

### Task 2: Check a set of records

Rules: unique addresses inside code; every `evidence` link resolves to a file and, when it has `#anchor`, to a heading that slugs to it (lower-case, non-alphanumerics dropped, spaces to hyphens, as GitHub does); a record whose header changed against its base needs a History line dated today.

**Files:**
- Modify: `src/DK2.Tools/FunctionKb.cs` (append to the partial class)
- Test: `tests/DK2.Tools.Tests/FunctionKbTests.cs` (append)

**Step 1: Write the failing tests**

```csharp
    static FunctionRecord Record(string address, string evidence = "docs/research/debug-dumpers.md#world-log")
    {
        var errors = new List<string>();
        string text = Sample.Replace("0x0050EA70", "0x" + address)
                            .Replace("docs/research/debug-dumpers.md#world-log", evidence);
        return FunctionKb.Parse($"docs/research/functions/{address}.md", Lines(text), errors)!;
    }

    [Fact]
    public void DuplicateAddressesAreOneFinding()
    {
        var a = Record("0050EA70");
        var b = Record("0050EA70") with { RelativePath = "docs/research/functions/copy.md" };
        Assert.Contains(FunctionKb.CheckSet([a, b], _ => true, _ => []), f => f.Contains("twice", StringComparison.Ordinal));
    }

    [Fact]
    public void EvidenceMustResolveToFileAndHeading()
    {
        string[] doc = ["# Debug dumpers", "", "## World log", "text"];
        var ok = Record("0050EA70", "docs/research/debug-dumpers.md#world-log");
        Assert.Empty(FunctionKb.CheckSet([ok], _ => true, _ => doc));
        var badAnchor = Record("0050EA70", "docs/research/debug-dumpers.md#nowhere");
        Assert.Single(FunctionKb.CheckSet([badAnchor], _ => true, _ => doc));
        var badFile = Record("0050EA70", "docs/research/missing.md");
        Assert.Single(FunctionKb.CheckSet([badFile], _ => false, _ => []));
    }

    [Fact]
    public void AChangedHeaderNeedsAHistoryLineDatedToday()
    {
        var before = Record("0050EA70");
        var now = before with { Confidence = "DISPUTED" };
        var today = new DateOnly(2026, 10, 01);
        Assert.Single(FunctionKb.HistoryRequired(now, before, today));
        var withLine = now with { History = [.. now.History, new HistoryEntry("2026-10-01", "codex", "disputed: the caller passes a room, not a creature")] };
        Assert.Empty(FunctionKb.HistoryRequired(withLine, before, today));
        Assert.Empty(FunctionKb.HistoryRequired(before, before, today));       // untouched header
        Assert.Single(FunctionKb.HistoryRequired(now, null, today));           // a new record also dates its birth
    }

    [Fact]
    public void HeadingsSlugLikeGitHub()
    {
        Assert.Equal("the-runtime-map-reaching-a-tile-while-the-game-is-running",
            FunctionKb.Slug("# The runtime map: reaching a tile while the game is running"));
        Assert.Equal("fun_004508d0-and-friends", FunctionKb.Slug("## `FUN_004508D0` and friends"));
    }
```

**Step 2: Run to verify they fail** — build error: `CheckSet`, `HistoryRequired`, `Slug` missing.

**Step 3: Implement**

```csharp
public static partial class FunctionKb
{
    [GeneratedRegex(@"[^a-z0-9_ -]")] private static partial Regex NonSlugRx();   // GitHub keeps underscores

    public static string Slug(string heading)
    {
        string t = heading.TrimStart('#').Trim().ToLowerInvariant();
        t = NonSlugRx().Replace(t, "");
        return t.Replace(' ', '-');
    }

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

            uint value = uint.Parse(r.Address, NumberStyles.HexNumber);
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

    public static IEnumerable<string> HistoryRequired(FunctionRecord now, FunctionRecord? before, DateOnly today)
    {
        if (before is not null && before.HeaderKey == now.HeaderKey) yield break;
        string date = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!now.History.Any(h => h.Date == date))
            yield return $"{now.RelativePath}: the header changed but no History line is dated {date}";
    }
}
```

**Step 4: Run tests** — Expected: 11 passed.

**Step 5: Commit / hand over** — `check a record set: unique code addresses, live evidence links, dated history`.

---

### Task 3: A mention without a record is a finding

**Files:**
- Modify: `src/DK2.Tools/FunctionKb.cs`
- Test: `tests/DK2.Tools.Tests/FunctionKbTests.cs`

**Step 1: Failing test**

```csharp
    [Fact]
    public void AMentionInAChangedLineNeedsARecord()
    {
        var changes = new[]
        {
            ("docs/research/combat.md", new[] { "`FUN_0047AB10` decides the target", "and FUN_0050ea70 dumps it" }),
            ("src/DK2.Simulation/Combat.cs", new[] { "// mirrors FUN_0047AB10" }),
            ("docs/research/functions/0050EA70.md", new[] { "address: 0x0050EA70" }),   // records themselves are exempt
        };
        var findings = FunctionKb.MissingRecords(changes, new HashSet<string> { "0050EA70" }).ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Contains("0047AB10", f, StringComparison.Ordinal));
    }
```

**Step 2: Run** — build error: `MissingRecords` missing.

**Step 3: Implement**

```csharp
    /// <summary>One finding per (file, address) for a FUN_ mentioned in an added line with no record.</summary>
    public static IEnumerable<string> MissingRecords(
        IEnumerable<(string File, string[] AddedLines)> changes, ISet<string> recorded)
    {
        foreach (var (file, lines) in changes)
        {
            if (file.StartsWith(Folder + "/", StringComparison.Ordinal)) continue;
            if (!(file.StartsWith("docs/", StringComparison.Ordinal) || file.StartsWith("src/", StringComparison.Ordinal))) continue;
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string line in lines)
                foreach (string address in Mentions(line))
                    if (!recorded.Contains(address)) missing.Add(address);
            foreach (string address in missing)
                yield return $"{file}: mentions FUN_{address} and {Folder}/{address}.md does not exist";
        }
    }
```

**Step 4: Run tests** — 12 passed. **Step 5:** hand over — `a function named in a changed line must have a record`.

---

### Task 4: `dk2tools kb check` and the hook

**Files:**
- Modify: `src/DK2.Tools/Program.cs` (new `case "kb":` next to `case "docs":` at line 237; new static helper `KbCheck` near `ChangedFiles` at line 3634)
- Modify: `.githooks/commit-msg` (add a blocking call after the `docs --message` block)

**Step 1: Add the command**

In `Main`'s switch, before `case "docs":`:

```csharp
    case "kb" when args.Length >= 2:
        return FunctionKbCommand.Run(args.Skip(1).ToArray());
```

Create `src/DK2.Tools/FunctionKbCommand.cs`:

```csharp
// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Tools;

/// <summary>dk2tools kb check|index|apply-names. The gate half of AGENTS.md's KB rule.</summary>
static class FunctionKbCommand
{
    public static int Run(string[] args)
    {
        string? root = Program.FindRepoRoot();
        if (root is null) { Console.Error.WriteLine("not inside the repository."); return 2; }
        return args[0] switch
        {
            "check" => Check(root, args),
            "index" => Index(root),
            _ => Usage(),
        };
    }

    static int Usage()
    {
        Console.Error.WriteLine("usage: dk2tools kb check [--all|--staged|--since REF] | kb index");
        return 2;
    }

    /// <summary>Records from the tree, or from the index when the hook asks about a commit.</summary>
    internal static List<FunctionRecord> Load(string root, bool staged, List<string> errors)
    {
        var records = new List<FunctionRecord>();
        string dir = Path.Combine(root, FunctionKb.Folder.Replace('/', Path.DirectorySeparatorChar));
        IEnumerable<string> paths = staged
            ? Program.RunGit(root, "ls-files", "--cached", FunctionKb.Folder).Split('\n').Select(l => l.Trim()).Where(l => l.EndsWith(".md", StringComparison.Ordinal))
            : Directory.Exists(dir) ? Directory.GetFiles(dir, "*.md").Select(f => FunctionKb.Folder + "/" + Path.GetFileName(f)) : [];
        foreach (string relative in paths)
        {
            if (relative.EndsWith("/INDEX.md", StringComparison.Ordinal)) continue;
            string[] lines = staged ? Program.GitLines(root, $":{relative}") : File.ReadAllLines(Path.Combine(root, relative));
            var r = FunctionKb.Parse(relative, lines, errors);
            if (r is not null) records.Add(r);
        }
        return records;
    }

    static int Check(string root, string[] args)
    {
        bool all = args.Contains("--all");
        bool staged = args.Contains("--staged");
        string baseRef = Program.OptionValue(args, "--since") ?? "HEAD";
        var findings = new List<string>();

        var records = Load(root, staged, findings);
        findings.AddRange(FunctionKb.CheckSet(records,
            f => File.Exists(Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar))),
            f => File.ReadAllLines(Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar)))));

        if (!all)
        {
            var recorded = records.Select(r => r.Address).ToHashSet(StringComparer.Ordinal);
            IEnumerable<string> files = staged ? Program.StagedFiles(root) : Program.ChangedFiles(root, baseRef);
            var changes = new List<(string, string[])>();
            var today = DateOnly.FromDateTime(DateTime.Now);
            foreach (string relative in files)
            {
                bool isRecord = relative.StartsWith(FunctionKb.Folder + "/", StringComparison.Ordinal) && !relative.EndsWith("INDEX.md", StringComparison.Ordinal);
                string[] lines = staged ? Program.GitLines(root, $":{relative}")
                    : File.Exists(Path.Combine(root, relative)) ? File.ReadAllLines(Path.Combine(root, relative)) : [];
                HashSet<int> added = Program.AddedLineNumbers(root, baseRef, relative, staged);
                changes.Add((relative, added.Where(n => n >= 1 && n <= lines.Length).Select(n => lines[n - 1]).ToArray()));

                if (!isRecord) continue;
                var now = records.FirstOrDefault(r => r.RelativePath == relative);
                if (now is null) continue;
                string[] baseLines = Program.GitLines(root, $"{baseRef}:{relative}");
                var before = baseLines.Length == 0 ? null : FunctionKb.Parse(relative, baseLines, []);
                findings.AddRange(FunctionKb.HistoryRequired(now, before, today));
            }
            findings.AddRange(FunctionKb.MissingRecords(changes, recorded));
        }

        if (findings.Count == 0)
        {
            Console.WriteLine($"kb check: {records.Count} records, no findings.");
            return 0;
        }
        Console.WriteLine($"{findings.Count} KB finding(s):");
        foreach (string f in findings) Console.WriteLine("  " + f);
        Console.WriteLine();
        Console.WriteLine("See AGENTS.md, 'The function knowledge base'. A function named in a change has a record; a changed verdict has a dated History line.");
        return 1;
    }

    static int Index(string root) => throw new NotImplementedException("Task 5");
}
```

Make `FindRepoRoot`, `OptionValue`, `RunGit`, `GitLines`, `ChangedFiles`, `StagedFiles`, `AddedLineNumbers` in Program.cs `internal static` (they are file-local `static` today; `Program` is the top-level-statements class, so refer to them as `Program.X`). If `Program` is not addressable, move those seven helpers into a new `static class Repo` in `src/DK2.Tools/Repo.cs` and update the docs case to call `Repo.X` — mechanical, the tests for `docs` will tell you if you broke it.

**Step 2: Build and try it**

```
dotnet build DK2.slnx -c Release
dotnet src/DK2.Tools/bin/Release/net10.0/DK2.Tools.dll kb check --all
```
Expected: `kb check: 0 records, no findings.` (the folder does not exist yet).

Then create `docs/research/functions/0050EA70.md` with the `Sample` text from Task 1 (a real record: the world-log dumper, evidence `docs/research/debug-dumpers.md#<slug of its heading>` — read the file, take the heading that covers the world log, slug it by the rule). Run `kb check` (no flag): expected `kb check: 1 records, no findings.` Change `confidence:` to `LIKELY` without a History line: expected one finding naming today's date. Revert.

**Step 3: Hook**

In `.githooks/commit-msg`, after the `docs --message` block and before the advisory status block:

```sh
# --- Blocking: the function knowledge base (AGENTS.md, HARD rule) -------
#
# A function named in this commit has a record; a record whose verdict this
# commit changes carries a History line dated today.
if ! dotnet "$TOOL" kb check --staged; then
    echo >&2
    echo "commit-msg: the function knowledge base is behind this commit." >&2
    echo "            docs/research/functions/<ADDRESS>.md, then re-run." >&2
    exit 1
fi
```

Test the hook without committing: `sh .githooks/commit-msg scratch-msg.txt` with any message file, on a tree where `git add` of a doc mentioning an unrecorded `FUN_` is staged. Expected: exit 1 with the finding. Unstage it.

**Step 4: Commit / hand over** — files: `src/DK2.Tools/Program.cs`, `src/DK2.Tools/FunctionKbCommand.cs`, `.githooks/commit-msg`, `docs/research/functions/0050EA70.md` — `gate the function knowledge base in the commit hook`.

---

### Task 5: `kb index` writes INDEX.md

The sqlite of the design is deferred: INDEX.md plus grep answers every question asked so far, and `dk2kb` (Task 8) searches the files directly. Add sqlite only when a query needs it.

**Files:**
- Modify: `src/DK2.Tools/FunctionKb.cs` (`Index` text), `src/DK2.Tools/FunctionKbCommand.cs` (`Index`)
- Modify: `src/DK2.Tools/Program.cs:334-340` — exempt `docs/research/functions/INDEX.md` from the research-doc length budget (it is generated; add `&& !relative.EndsWith("functions/INDEX.md", ...)`)
- Test: append to `FunctionKbTests.cs`

**Step 1: Failing test**

```csharp
    [Fact]
    public void TheIndexIsOneRowPerRecordSortedByAddress()
    {
        string index = FunctionKb.Index([Record("0050EA70"), Record("00452F60")]);
        string[] rows = index.Split('\n').Where(l => l.StartsWith("| `0x", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.StartsWith("| `0x00452F60`", rows[0]);
        Assert.Contains("[0050EA70.md](0050EA70.md)", index);
        Assert.Contains("generated by `dk2tools kb index`", index);
    }
```

**Step 2: Run** — fails, `Index` missing.

**Step 3: Implement**

```csharp
    public static string Index(IReadOnlyList<FunctionRecord> records)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("# Functions of DKII.EXE: the register\n\n");
        sb.Append("One row per record in this folder, generated by `dk2tools kb index`; edit the records, not this table. ");
        sb.Append("The record holds the reasoning and history; its evidence links hold the proof (ADR-0046).\n\n");
        sb.Append("| Address | Name | Role | Confidence | Status |\n|---|---|---|---|---|\n");
        foreach (var r in records.OrderBy(r => r.Address, StringComparer.Ordinal))
            sb.Append($"| `0x{r.Address}` | [{r.Name}]({r.Address}.md) | {r.Role} | {r.Confidence} | {r.Status} |\n");
        return sb.ToString();
    }
```

and in `FunctionKbCommand`:

```csharp
    static int Index(string root)
    {
        var errors = new List<string>();
        var records = Load(root, staged: false, errors);
        if (errors.Count > 0) { foreach (string e in errors) Console.Error.WriteLine(e); return 1; }
        string path = Path.Combine(root, FunctionKb.Folder.Replace('/', Path.DirectorySeparatorChar), "INDEX.md");
        File.WriteAllText(path, FunctionKb.Index(records), new System.Text.UTF8Encoding(false));
        Console.WriteLine($"kb index: {records.Count} records -> {FunctionKb.Folder}/INDEX.md");
        return 0;
    }
```

**Step 4: Run tests (13 passed), then** `dk2tools kb index` and read `docs/research/functions/INDEX.md`: one row.

**Step 5: Hand over** — `generate the function register from the records`.

---

### Task 6: The HARD rule, in words

**Files:**
- Modify: `AGENTS.md` — new section after *One fact, one home* (line ~124)
- Modify: `.agents/skills/ghidra-queries/SKILL.md` — one bullet in *The MCP server* section, one in *The rule first*
- Modify: `.claude/skills/ghidra-queries/SKILL.md` — one sentence
- Modify: `docs/DECISIONS.md` — ADR (≤ 50 lines) recording the decision, pointing at the design doc

**Step 1: AGENTS.md text** (keep it this short; the gate carries the detail):

```markdown
### HARD RULE: the function knowledge base is kept current

`docs/research/functions/<ADDRESS>.md` is the one home for what a DKII.EXE
function is, how sure we are, why, and how that changed. **Every new finding
or correction about a function goes into its record in the same change, with
a `History` line dated that day.** A document, ADR or source file that names a
`FUN_` without a record does not pass `dk2tools kb check`, and the commit hook
runs it (`docs/plans/` is exempt: a plan is a process document, not a claim).
Read the record before you decompile: a later session may already have
disputed what you are about to conclude. `implements` names our code that
mirrors the function, not code that consumes its output. `INDEX.md` there is
generated (`dk2tools kb index`); never edit it by hand. Design:
`docs/plans/2026-09-30-function-knowledge-base-design.md`.
```

**Step 2: ADR** in `docs/DECISIONS.md`, next free number, title `Function records are the one home for per-function knowledge`, ≤ 50 lines: decision, why (no renames ever made in Ghidra; 225 functions cited across 50 docs with no verdict), the gate, what stays where (evidence in research docs, decompiler text nowhere).

**Step 3: Run** `dk2tools docs --check` and `dk2tools kb check`. Expected: no findings. **Step 4: Hand over** — `state the knowledge-base rule and its decision`.

---

### Task 7: Import the 225 cited functions

**Files:**
- Create: `tools/kb/import_from_docs.py`

One-off but kept: it never overwrites an existing record, so it can be re-run when a doc cites a new function.

**Step 1: Write the script**

```python
#!/usr/bin/env python3
"""Create a draft record for every FUN_ cited in docs/research that has none.

    python tools/kb/import_from_docs.py [--dry-run]

Each draft is GUESS/READ: its role is the sentence of the first citation, in
the doc's own words, and its evidence is that doc's nearest heading above the
citation. Existing records are never touched. Run `dk2tools kb check --all`
and `dk2tools kb index` afterwards.
"""
import datetime
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
RESEARCH = os.path.join(REPO, 'docs', 'research')
FUNCTIONS = os.path.join(RESEARCH, 'functions')
MENTION = re.compile(r'\bFUN_(00[4-6][0-9A-Fa-f]{5})\b')
HEADING = re.compile(r'^#{1,6}\s')


def slug(heading):
    t = heading.lstrip('#').strip().lower()
    t = re.sub(r'[^a-z0-9_ -]', '', t)      # same rule as FunctionKb.Slug: underscores survive
    return t.replace(' ', '-')


def sentence_around(line, address):
    """The sentence of the line that holds the mention, backticks and pipes stripped."""
    text = line.replace('`', '').replace('|', ' ').strip()
    for part in re.split(r'(?<=[.;])\s+', text):
        if address.lower() in part.lower():
            return re.sub(r'\s+', ' ', part).strip(' -*')[:200]
    return re.sub(r'\s+', ' ', text)[:200]


def first_citations():
    """address -> (relative doc path, anchor, sentence), from the first doc that cites it."""
    found = {}
    for name in sorted(os.listdir(RESEARCH)):
        if not name.endswith('.md') or name == 'open-questions.md':
            continue
        heading = None
        with open(os.path.join(RESEARCH, name), encoding='utf-8-sig') as stream:
            for line in stream:
                if HEADING.match(line):
                    heading = slug(line)
                for m in MENTION.finditer(line):
                    address = m.group(1).upper()
                    if address not in found:
                        anchor = '#' + heading if heading else ''
                        found[address] = ('docs/research/' + name + anchor, sentence_around(line, 'FUN_' + address))
    return found


def main():
    dry = '--dry-run' in sys.argv
    today = datetime.date.today().isoformat()
    os.makedirs(FUNCTIONS, exist_ok=True)
    created = 0
    for address, (evidence, sentence) in sorted(first_citations().items()):
        path = os.path.join(FUNCTIONS, address + '.md')
        if os.path.exists(path):
            continue
        record = (
            f'address: 0x{address}\n'
            f'name: FUN_{address}\n'
            f'role: {sentence}\n'
            f'confidence: GUESS\n'
            f'status: READ\n'
            f'evidence: {evidence}\n'
            f'\n'
            f'Imported from the first citation in docs/research; the role is that sentence, not a verdict. '
            f'Raise the confidence only after reading the evidence section.\n'
            f'\n'
            f'## History\n'
            f'- {today} import: drafted by tools/kb/import_from_docs.py from {evidence.split("#")[0]}\n'
        )
        created += 1
        if dry:
            print(address, evidence)
        else:
            with open(path, 'w', encoding='utf-8', newline='\n') as stream:
                stream.write(record)
    print('%s %d records' % ('would create' if dry else 'created', created))


if __name__ == '__main__':
    main()
```

**Step 2: Dry run** — `python tools/kb/import_from_docs.py --dry-run` — Expected: about 224 lines (0050EA70 exists) and `would create 224 records`. Spot-check five: the evidence anchors must exist as headings (open the doc).

**Step 3: Run for real, then the gate**

```
python tools/kb/import_from_docs.py
dotnet src/DK2.Tools/bin/Release/net10.0/DK2.Tools.dll kb check --all
dotnet src/DK2.Tools/bin/Release/net10.0/DK2.Tools.dll kb index
```
Expected: `kb check: 225 records, no findings.` If anchors fail (a heading with a character the slug rule handles differently), fix `Slug` in C# and `slug` in Python together, with a test in `HeadingsSlugLikeGitHub`. A `FUN_` cited only in a code block that is really a data address (outside code range) fails `CheckSet`: delete that draft and note the doc line in the hand-over.

**Step 4: Hand over** — `git add tools/kb/import_from_docs.py docs/research/functions` — `import the 225 cited functions as GUESS records`. Note in the message body that these are drafts and Task 10 reviews them.

---

### Task 8: `dk2kb`, the knowledge-base MCP server

**Files:**
- Create: `tools/kb/dk2kb.py`
- Modify: `.mcp.json` (add server), `~/.codex/config.toml` (add server; owner's file, same pattern as pyghidra-mcp)

**Step 1: Write the server**

```python
# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=1.26,<2"]
# ///
"""dk2kb: the function knowledge base over MCP, for Claude and Codex.

    uv run tools/kb/dk2kb.py            # stdio server, spawned by the client
    uv run tools/kb/dk2kb.py --selftest # spawn itself and exercise every tool

Tools read and write docs/research/functions/<ADDRESS>.md directly. kb_upsert
appends a History line and refuses to save anything `dk2tools kb check`
rejects. Concurrency is per file: two sessions editing two functions never
meet; the same function twice is last-writer-wins and git shows both.
"""
import asyncio
import datetime
import os
import re
import subprocess
import sys

from mcp.server.fastmcp import FastMCP

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
FUNCTIONS = os.path.join(REPO, 'docs', 'research', 'functions')
TOOL = os.path.join(REPO, 'src', 'DK2.Tools', 'bin', 'Release', 'net10.0', 'DK2.Tools.dll')
KEYS = ('address', 'name', 'role', 'confidence', 'status')

mcp = FastMCP('dk2kb', instructions='DKII.EXE function knowledge base: read a record before deciding what a function is; write every new finding or correction back with kb_upsert.')


def normalise(address):
    a = address.strip().upper()
    a = a[2:] if a.startswith('0X') else a
    a = a[4:] if a.startswith('FUN_') else a
    if not re.fullmatch(r'00[4-6][0-9A-F]{5}', a):
        raise ValueError('address must be 0x00xxxxxx inside DKII.EXE code, got %r' % address)
    return a


def path_of(address):
    return os.path.join(FUNCTIONS, address + '.md')


def parse(text):
    head, _, rest = text.partition('\n\n')
    fields = {}
    for line in head.splitlines():
        key, _, value = line.partition(':')
        fields.setdefault(key.strip(), []).append(value.split(' #')[0].strip())
    body, _, history = rest.partition('## History')
    return fields, body.strip(), [h[2:] for h in history.strip().splitlines() if h.startswith('- ')]


@mcp.tool()
def kb_get(address: str) -> dict:
    """The record of one function: header fields, reasoning, history. Empty dict if none."""
    a = normalise(address)
    if not os.path.exists(path_of(a)):
        return {}
    fields, body, history = parse(open(path_of(a), encoding='utf-8').read())
    return {'address': a, 'fields': {k: v if len(v) > 1 else v[0] for k, v in fields.items()}, 'reasoning': body, 'history': history}


@mcp.tool()
def kb_search(text: str, limit: int = 20) -> list:
    """Records whose name, role or reasoning contain the text (case-insensitive); address, name, role, confidence."""
    needle = text.lower()
    hits = []
    for name in sorted(os.listdir(FUNCTIONS)):
        if not name.endswith('.md') or name == 'INDEX.md':
            continue
        content = open(os.path.join(FUNCTIONS, name), encoding='utf-8').read()
        if needle in content.lower():
            fields, _, _ = parse(content)
            hits.append({'address': name[:-3], 'name': fields['name'][0], 'role': fields['role'][0], 'confidence': fields['confidence'][0]})
            if len(hits) >= limit:
                break
    return hits


@mcp.tool()
def kb_history(address: str) -> list:
    """The dated History lines of one function, oldest first."""
    return kb_get(address).get('history', [])


@mcp.tool()
def kb_upsert(address: str, agent: str, note: str, name: str = '', role: str = '', confidence: str = '',
              status: str = '', evidence: str = '', implements: str = '', reasoning: str = '') -> str:
    """Create or change a record. Only the fields you pass change; `note` becomes today's History line
    (say what changed and why, with the ADR or commit). evidence/implements: comma-separated, replace the list.
    Returns 'saved' or the gate's findings, in which case nothing was written."""
    a = normalise(address)
    today = datetime.date.today().isoformat()
    if os.path.exists(path_of(a)):
        fields, body, history = parse(open(path_of(a), encoding='utf-8').read())
    else:
        fields, body, history = {'address': ['0x' + a], 'name': ['FUN_' + a], 'role': [''], 'confidence': ['GUESS'], 'status': ['READ'], 'evidence': []}, '', []
    for key, value in (('name', name), ('role', role), ('confidence', confidence), ('status', status)):
        if value:
            fields[key] = [value.strip()]
    if evidence:
        fields['evidence'] = [e.strip() for e in evidence.split(',') if e.strip()]
    if implements:
        fields['implements'] = [e.strip() for e in implements.split(',') if e.strip()]
    if reasoning:
        body = reasoning.strip()
    history.append('%s %s: %s' % (today, agent.strip().lower(), ' '.join(note.split())))

    lines = []
    for key in KEYS:
        lines.append('%s: %s' % (key, fields[key][0]))
    for key in ('evidence', 'implements'):
        for value in fields.get(key, []):
            lines.append('%s: %s' % (key, value))
    text = '\n'.join(lines) + '\n\n' + body + '\n\n## History\n' + ''.join('- %s\n' % h for h in history)

    os.makedirs(FUNCTIONS, exist_ok=True)
    tmp = path_of(a) + '.tmp'
    with open(tmp, 'w', encoding='utf-8', newline='\n') as stream:
        stream.write(text)
    backup = open(path_of(a), encoding='utf-8').read() if os.path.exists(path_of(a)) else None
    os.replace(tmp, path_of(a))
    check = subprocess.run(['dotnet', TOOL, 'kb', 'check', '--all'], capture_output=True, text=True, cwd=REPO)
    if check.returncode != 0:
        if backup is None:
            os.remove(path_of(a))
        else:
            with open(path_of(a), 'w', encoding='utf-8', newline='\n') as stream:
                stream.write(backup)
        return 'not saved: ' + check.stdout.strip()
    subprocess.run(['dotnet', TOOL, 'kb', 'index'], capture_output=True, text=True, cwd=REPO)
    return 'saved %s' % path_of(a)


async def selftest():
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client
    params = StdioServerParameters(command=sys.executable, args=[os.path.abspath(__file__)])
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = sorted(t.name for t in (await session.list_tools()).tools)
            assert tools == ['kb_get', 'kb_history', 'kb_search', 'kb_upsert'], tools
            got = await session.call_tool('kb_get', {'address': '0x0050EA70'})
            assert 'WorldLog' in got.content[0].text, got.content[0].text
            hits = await session.call_tool('kb_search', {'text': 'MESSAGE.LOG'})
            assert '0050EA70' in hits.content[0].text
    print('PASS dk2kb')


if __name__ == '__main__':
    if '--selftest' in sys.argv:
        asyncio.run(selftest())
    else:
        mcp.run(transport='stdio')
```

Note: `--selftest` runs the server with `sys.executable`, which inside `uv run` is the environment that has `mcp`; that is why the selftest lives in the same file.

**Step 2: Run** `uv run tools/kb/dk2kb.py --selftest` — Expected: `PASS dk2kb`.

**Step 3: A write through the tool.** From a Python one-liner using the same client, call `kb_upsert` on `0x0050EA70` with `agent='claude'`, `note='selftest: no change of verdict'`. Expected: `saved ...`, and `git diff docs/research/functions/0050EA70.md` shows exactly one new History line. Then call it with `confidence='SURE'`: expected `not saved: ...confidence 'SURE'...` and no diff beyond the first line. Revert the History line (`git checkout -- docs/research/functions/0050EA70.md`).

**Step 4: Register it.** `.mcp.json`:

```json
    "dk2kb": {
      "type": "stdio",
      "command": "uv",
      "args": ["run", "D:/DungeonKeeperRemake/tools/kb/dk2kb.py"]
    }
```

`~/.codex/config.toml`:

```toml
[mcp_servers.dk2kb]
command = "uv"
args = ["run", 'D:\DungeonKeeperRemake\tools\kb\dk2kb.py']
startup_timeout_sec = 60
```

Add `mcp__dk2kb__kb_get`, `mcp__dk2kb__kb_search`, `mcp__dk2kb__kb_history` to `permissions.allow` in `.claude/settings.json` (`kb_upsert` stays an ask). `claude mcp list` → `dk2kb ... ✔ Connected`.

**Step 5: Hand over** — `tools/kb/dk2kb.py .mcp.json .claude/settings.json` — `serve the function knowledge base to both agents over MCP`. Tell the owner the Codex config line.

---

### Task 9: One canonical Ghidra project, clones that delete themselves

**Files:**
- Modify: `tools/ghidra/ghidra.py:35-41` (PROJECTS → CANONICAL), `:137-160` (clone per run unless `--write`)
- Modify: `tools/ghidra/mcp-serve.py` (clone per session under `<agent>-<pid>`, delete at exit; shared index at `D:/Ghidra/mcp/index`)
- Modify: `tools/ghidra/mcp-check.py` (no change needed if it still passes `--agent`; keep `DK2_MCP_ROOT`)
- Create: `tools/ghidra/scripts/ApplyNames.py`
- Create: `tools/ghidra/cleanup-ghidra-folders.ps1` (owner runs it)
- Modify: `.agents/skills/ghidra-queries/SKILL.md` (project rules), `AGENTS.md:30-33` (setup line), `.claude/skills/ghidra-queries/SKILL.md`

**Step 1: Move the project.** Owner or executor, with no Ghidra process alive (`tasklist | findstr /i "java pyghidra"` empty):

```
Move-Item 'D:\Ghidra\ghidra projects\DK2_RE.gpr' 'D:\Ghidra\DK2_RE.gpr'
Move-Item 'D:\Ghidra\ghidra projects\DK2_RE.rep' 'D:\Ghidra\DK2_RE.rep'
```
(Codex's project; identical in names to Claude's, checked 2026-09-30 with Funcs.py over 13,920 functions.)

**Step 2: ghidra.py.** Replace `PROJECTS` with

```python
CANONICAL = os.environ.get('DK2_GHIDRA_PROJECT', 'D:/Ghidra/DK2_RE')
CLONES = os.environ.get('DK2_MCP_ROOT', 'D:/Ghidra/mcp')
```

Keep `--agent` (it names the log file) but make it optional. In `main`, after the arguments: if `options.write` use `CANONICAL` and keep the existing lock checks; otherwise

```python
    project = clone_project(CANONICAL, os.path.join(CLONES, 'run-%s-%d' % (options.agent or 'any', os.getpid())))
    atexit.register(shutil.rmtree, os.path.dirname(project), True)
```

with

```python
def clone_project(source, target_dir):
    """Copy <source>.gpr/.rep under target_dir; instant on ReFS. Returns the clone's base path."""
    os.makedirs(target_dir, exist_ok=True)
    base = os.path.join(target_dir, os.path.basename(source))
    shutil.copy2(source + '.gpr', base + '.gpr')
    shutil.copytree(source + '.rep', base + '.rep')
    for root, _dirs, files in os.walk(base + '.rep'):
        for name in files:
            if name.endswith(('.lock', '.lock~')):
                os.remove(os.path.join(root, name))
    return base
```

Remove the `claim()` marker for read-only runs (clones cannot collide); keep it for `--write`. Update the docstring. Test: `python tools/ghidra/ghidra.py --agent claude Funcs.py <abs scratch path> 0x401000 0x401100` twice in parallel from two terminals: both succeed, and `D:\Ghidra\mcp` holds no `run-*` folder afterwards.

**Step 3: mcp-serve.py.** Replace `refresh_copy` with `clone_project` (import it from ghidra.py through the same `load_runner()`), clone to `os.path.join(MCP_ROOT, '%s-%d' % (agent, os.getpid()))`, copy `D:/Ghidra/mcp/index/DK2_RE-pyghidra-mcp` beside it when it exists, register `atexit` removal, and after `subprocess.call` returns also remove the clone. `--index-only` builds into the shared `D:/Ghidra/mcp/index` (clone, build, move the `-pyghidra-mcp` folder there). Drop `--refresh-only` and `--reindex`; the docstring says why. Run `uv run tools/ghidra/mcp-check.py claude`: PASS, and no `claude-*` folder left under `D:\Ghidra\mcp`.

**Step 4: ApplyNames.py** (`Args: <out> <names.tsv>`; TSV rows `ADDRESS<TAB>name`; renames existing functions only, reports the rest):

```python
# Apply names from the knowledge base to the functions of the current program.
# Args: <out> <names.tsv>   (rows: ADDRESS \t name; ADDRESS is eight hex digits)
from ghidra.program.model.symbol import SourceType


def main():
    args = getScriptArgs()
    out = open(args[0], 'w')
    af = currentProgram.getAddressFactory().getDefaultAddressSpace()
    fm = currentProgram.getFunctionManager()
    renamed = missing = same = 0
    for line in open(args[1]):
        parts = line.rstrip('\n').split('\t')
        if len(parts) != 2 or parts[1].startswith('FUN_'):
            continue
        function = fm.getFunctionAt(af.getAddress(int(parts[0], 16)))
        if function is None:
            missing += 1
            out.write('MISSING %s %s\n' % (parts[0], parts[1]))
        elif function.getName() == parts[1]:
            same += 1
        else:
            function.setName(parts[1], SourceType.USER_DEFINED)
            renamed += 1
            out.write('RENAMED %s %s\n' % (parts[0], parts[1]))
    out.write('renamed=%d same=%d missing=%d\n' % (renamed, same, missing))
    out.close()
    print('WROTE %s' % args[0])


main()
```

`dk2tools kb apply-names` in `FunctionKbCommand`: write `reverse-engineering/.cache/kb-names.tsv` from the records (skip names starting `FUN_`), then run `python tools/ghidra/ghidra.py --write ApplyNames.py <abs out> <abs tsv>` and print the last line of the out file. Test after Task 10 has given at least one real name: the next MCP session's decompile shows that name.

**Step 5: Cleanup script** `tools/ghidra/cleanup-ghidra-folders.ps1` — lists, then deletes only with `-Confirm`:

```powershell
$remove = @('D:\Ghidra\backup_projects_11', 'D:\Ghidra\ghidra_11.0.1_PUBLIC', 'D:\Ghidra\claude ghidra',
            'D:\Ghidra\ghidra projects codex-thunder', 'D:\Ghidra\ghidra projects claude',
            'D:\Ghidra\mcp\claude', 'D:\Ghidra\mcp\codex', 'D:\Ghidra\mcp\check')
$keep = @('D:\Ghidra\ghidra_12.1.4_PUBLIC', 'D:\Ghidra\DK2_RE.gpr', 'D:\Ghidra\DK2_RE.rep', 'D:\Ghidra\mcp\index')
New-Item -ItemType Directory -Force 'D:\Ghidra\other projects' | Out-Null
Get-ChildItem 'D:\Ghidra\ghidra projects' | Move-Item -Destination 'D:\Ghidra\other projects'
Remove-Item 'D:\Ghidra\ghidra projects' -Force
foreach ($p in $remove) { if (Test-Path $p) { Remove-Item $p -Recurse -Force -Confirm } }
foreach ($p in $keep) { if (-not (Test-Path $p)) { Write-Warning "expected $p" } }
```

The owner runs it after Step 2 and after every MCP session is closed. Do not run it for them.

**Step 6: Docs.** SKILL.md: replace rules 2 and 3 of *The rule first* with one: *every run and every MCP session works on its own clone of `D:\Ghidra\DK2_RE`, deleted at exit; only `--write` (used by `kb apply-names`) opens the canonical project, and one at a time.* AGENTS.md line 31: projects under `D:\Ghidra\` → `the one project D:\Ghidra\DK2_RE`. Update memory notes the executor keeps.

**Step 7: Hand over** — `one canonical Ghidra project; runs and sessions work on clones that delete themselves`.

---

### Task 10: Review the imported drafts

Not code. In waves of four subagents (fifteen at once hit the session limit), each given ~15 records: read the evidence section, set `name` (verb-first, `Area_Verb`), rewrite `role` as a verdict, set `confidence` from the doc's own confidence row, add `implements` where `src/` mirrors it (grep `FUN_<addr>` in `src/`), one History line each. Each wave ends with `kb check --all`, `kb index`, and a hand-over listing the addresses touched. Acceptance: no record still says `Imported from the first citation`.

---

### Task 11: Verification

1. `dotnet build DK2.slnx -c Release` and `dotnet test tests/DK2.Tools.Tests/DK2.Tools.Tests.csproj -c Release --no-build`: all green.
2. `dk2tools kb check --all`: `225 records, no findings` (or more, after Task 10 adds some).
3. `dk2tools docs --check`: no findings in the KB files.
4. `uv run tools/ghidra/mcp-check.py claude` and `codex`: PASS; `D:\Ghidra\mcp` afterwards holds only `index`.
5. `uv run tools/kb/dk2kb.py --selftest`: PASS.
6. One record written through `dk2kb` from a Codex session and one from a Claude session, in that order, both landing in git with their History lines.
7. `dk2tools kb apply-names`, then one MCP decompile of a renamed function shows the name.
8. The owner has run `cleanup-ghidra-folders.ps1`; `D:\Ghidra` holds `ghidra_12.1.4_PUBLIC`, `DK2_RE.*`, `mcp\index`, `other projects`.
