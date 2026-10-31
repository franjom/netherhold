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

    const string RecordPath = "docs/research/functions/0050EA70.md";

    static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    static FunctionRecord Parse(string text, List<string> errors) => FunctionKb.Parse(RecordPath, Lines(text), errors);

    [Fact]
    public void ParsesEveryField()
    {
        var errors = new List<string>();
        FunctionRecord r = Parse(Sample, errors);
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
    public void RejectsABadHeader(string good, string bad, string key)
    {
        var errors = new List<string>();
        Parse(Sample.Replace(good, bad), errors);
        Assert.Contains(errors, e => e.Contains(key, StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingKeyIsOneError()
    {
        var errors = new List<string>();
        FunctionKb.Parse(RecordPath, Lines(Sample).Where(l => !l.StartsWith("name:", StringComparison.Ordinal)).ToArray(), errors);
        Assert.Single(errors);
        Assert.Contains("lacks 'name'", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyValueIsReportedAsEmpty()
    {
        var errors = new List<string>();
        Parse(Sample.Replace("name: WorldLog_Dump", "name:"), errors);
        Assert.Single(errors);
        Assert.Contains("'name' is empty", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyGivenTwiceIsReported()
    {
        var errors = new List<string>();
        Parse(Sample.Replace("name: WorldLog_Dump", "name: WorldLog_Dump\nname: Other"), errors);
        Assert.Single(errors);
        Assert.Contains("'name' given 2 times", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TrailingCommentsAreStrippedOnlyFromTokenKeys()
    {
        var errors = new List<string>();
        FunctionRecord r = Parse(Sample
            .Replace("role: Prints every world field and creature state into MESSAGE.LOG.", "role: Handles hero party #1 leaving the level")
            .Replace("status: READ", "status: READ  # note"), errors);
        Assert.Empty(errors);
        Assert.Equal("Handles hero party #1 leaving the level", r.Role);
        Assert.Equal("READ", r.Status);
    }

    [Fact]
    public void ALeadingBomIsIgnoredWithoutMutatingTheInput()
    {
        var errors = new List<string>();
        string[] lines = Lines("\uFEFF" + Sample);
        FunctionRecord r = FunctionKb.Parse(RecordPath, lines, errors);
        Assert.Empty(errors);
        Assert.Equal("0050EA70", r.Address);
        Assert.StartsWith("\uFEFF", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheFileNameMustBeTheAddress()
    {
        var errors = new List<string>();
        FunctionKb.Parse("docs/research/functions/00401000.md", Lines(Sample), errors);
        Assert.Contains(errors, e => e.Contains("file name", StringComparison.Ordinal));
    }

    [Fact]
    public void AHeaderKeyAfterTheBlankLineIsReported()
    {
        var errors = new List<string>();
        Parse(Sample.Replace("evidence:", "\nevidence:"), errors);
        Assert.Contains(errors, e => e.EndsWith(":7: header key 'evidence' comes after the blank line that ends the header", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.EndsWith(":8: header key 'implements' comes after the blank line that ends the header", StringComparison.Ordinal));
    }

    [Fact]
    public void AHeaderWithoutABlankLineIsOneError()
    {
        var errors = new List<string>();
        string text = Sample.Replace("\r\n", "\n")
            .Replace("\n\nThe function prints its own field names, which is how the offsets were pinned.\n\n## History", "\n## History");
        FunctionRecord r = Parse(text, errors);
        Assert.Single(errors);
        Assert.Contains("blank line", errors[0], StringComparison.Ordinal);
        Assert.Contains("## History", errors[0], StringComparison.Ordinal);
        Assert.Equal("", r.Body);
        Assert.Single(r.History);
    }

    [Fact]
    public void AHistoryLineNeedsDateAgentAndText()
    {
        var errors = new List<string>();
        Parse(Sample.Replace("- 2026-09-30 claude: created", "- created"), errors);
        Assert.Contains(errors, e => e.Contains(
            "History line must be '- YYYY-MM-DD agent: text' (agent is lower-case letters, digits, hyphens): - created from debug-dumpers.md",
            StringComparison.Ordinal));
    }

    [Fact]
    public void AnIndentedHistoryLineContinuesThePreviousEntry()
    {
        var errors = new List<string>();
        FunctionRecord r = Parse(Sample + "\n  and pinned by the worldlog.py decoder", errors);
        Assert.Empty(errors);
        Assert.Single(r.History);
        Assert.EndsWith("Cheat Engine call. and pinned by the worldlog.py decoder", r.History[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingHistorySectionIsReported()
    {
        var errors = new List<string>();
        string text = Sample.Replace("\r\n", "\n");
        Parse(text[..text.IndexOf("## History", StringComparison.Ordinal)], errors);
        Assert.Single(errors);
        Assert.Contains("no '## History' section", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyHistoryIsReported()
    {
        var errors = new List<string>();
        string text = Sample.Replace("\r\n", "\n");
        Parse(text[..(text.IndexOf("## History", StringComparison.Ordinal) + "## History".Length)], errors);
        Assert.Single(errors);
        Assert.Contains("History is empty", errors[0], StringComparison.Ordinal);
    }

    static FunctionRecord Record(string address, string evidence = "docs/research/debug-dumpers.md#world-log")
    {
        var errors = new List<string>();
        string text = Sample.Replace("0x0050EA70", "0x" + address)
                            .Replace("docs/research/debug-dumpers.md#world-log", evidence);
        return FunctionKb.Parse($"docs/research/functions/{address}.md", Lines(text), errors);
    }

    static readonly string[] Doc = ["# Debug dumpers", "", "## World log", "text"];

    [Fact]
    public void NamesTableSkipsGhidraDefaultsAndSortsByAddress()
    {
        var named = Record("0050EA70") with { Name = "Logging_World" };
        var earlier = Record("00405010") with { Name = "KeyHandler" };
        var unnamed = Record("00414BC0") with { Name = "FUN_00414BC0" };
        // A SUPERSEDED record may not even be an entry (00498E34 was four bytes into one).
        var superseded = Record("00498E34") with { Name = "Combat_MeleeSwing_MidEntry", Status = "SUPERSEDED" };
        Assert.Equal("00405010\tKeyHandler\n0050EA70\tLogging_World\n", FunctionKbNames.Table([named, unnamed, earlier, superseded]));
        Assert.Equal("", FunctionKbNames.Table([unnamed]));
    }

    [Fact]
    public void NamesTableMarksDoubtfulNamesWithTheirConfidence()
    {
        var likely = Record("004F39A0") with { Name = "Treasury_Tick", Confidence = "LIKELY" };
        var guess = Record("004DF6E0") with { Name = "HeroGate_Tick", Confidence = "GUESS" };
        var disputed = Record("0050F8C0") with { Name = "World_PlaceGeneratedSpecial", Confidence = "DISPUTED" };
        var confirmed = Record("004E8C60") with { Name = "Room_HatedCompanyStep" };
        Assert.Equal("004DF6E0\tHeroGate_Tick_GUESS\n004E8C60\tRoom_HatedCompanyStep\n004F39A0\tTreasury_Tick_LIKELY\n"
            + "0050F8C0\tWorld_PlaceGeneratedSpecial_DISPUTED\n",
            FunctionKbNames.Table([likely, guess, disputed, confirmed]));
    }

    [Fact]
    public void DuplicateAddressesAreOneFinding()
    {
        var a = Record("0050EA70");
        var b = Record("0050EA70") with { RelativePath = "docs/research/functions/copy.md" };
        string finding = Assert.Single(FunctionKb.CheckSet([a, b], _ => true, _ => Doc));
        Assert.Contains("twice", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceMustResolveToFileAndHeading()
    {
        var ok = Record("0050EA70", "docs/research/debug-dumpers.md#world-log");
        Assert.Empty(FunctionKb.CheckSet([ok], _ => true, _ => Doc));
        var badAnchor = Record("0050EA70", "docs/research/debug-dumpers.md#nowhere");
        Assert.Single(FunctionKb.CheckSet([badAnchor], _ => true, _ => Doc));
        var badFile = Record("0050EA70", "docs/research/missing.md");
        Assert.Single(FunctionKb.CheckSet([badFile], _ => false, _ => []));
    }

    [Fact]
    public void OnlyTheUnresolvedEvidenceLinkIsAFinding()
    {
        var r = Record("0050EA70", "docs/research/debug-dumpers.md#world-log\nevidence: docs/research/missing.md");
        string finding = Assert.Single(FunctionKb.CheckSet([r], f => f == "docs/research/debug-dumpers.md", _ => Doc));
        Assert.Contains("docs/research/missing.md", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void AChangedHeaderNeedsAHistoryLineDatedToday()
    {
        var before = Record("0050EA70");
        var now = before with { Confidence = "DISPUTED" };
        var today = new DateOnly(2026, 10, 01);
        string changed = Assert.Single(FunctionKb.HistoryRequired(now, before, today));
        Assert.EndsWith("header changed but no History line is dated 2026-10-01", changed, StringComparison.Ordinal);
        var withLine = now with { History = [.. now.History, new HistoryEntry("2026-10-01", "codex", "disputed: the caller passes a room, not a creature")] };
        Assert.Empty(FunctionKb.HistoryRequired(withLine, before, today));
        Assert.Empty(FunctionKb.HistoryRequired(before, before, today));       // untouched header
        string born = Assert.Single(FunctionKb.HistoryRequired(now, null, today));   // a new record also dates its birth
        Assert.EndsWith("is new and has no History line dated 2026-10-01", born, StringComparison.Ordinal);
    }

    /// <summary>CI checks a pushed range after the fact: a line dated on the day a commit in the range wrote it
    /// passes, though that day is not today.</summary>
    [Fact]
    public void ARangeCheckAcceptsTheDayACommitWroteTheRecord()
    {
        var before = Record("0050EA70");
        var now = before with
        {
            Confidence = "DISPUTED",
            History = [.. before.History, new HistoryEntry("2026-10-24", "codex", "disputed")],
        };
        DateOnly written = new(2026, 10, 24), pushed = new(2026, 10, 28);
        Assert.Single(FunctionKb.HistoryRequired(now, before, pushed));
        Assert.Empty(FunctionKb.HistoryRequired(now, before, [pushed, written]));
        string missing = Assert.Single(FunctionKb.HistoryRequired(now, before, [pushed, new DateOnly(2026, 10, 25)]));
        Assert.EndsWith("dated 2026-10-25 or 2026-10-28", missing, StringComparison.Ordinal);
    }

    /// <summary>A merge brings in records the other side dated on its own day; one whose header equals
    /// MERGE_HEAD's copy is inherited, and only a header the merge itself changes needs today's line.</summary>
    [Fact]
    public void AMergedInRecordIsDatedByTheSideThatWroteIt()
    {
        var theirs = Record("0050EA70");
        var today = new DateOnly(2026, 10, 03);
        Assert.Empty(FunctionKb.HistoryRequired(theirs, null, today, merged: theirs));

        var changedInTheMerge = theirs with { Confidence = "DISPUTED" };
        Assert.Single(FunctionKb.HistoryRequired(changedInTheMerge, null, today, merged: theirs));
    }

    [Fact]
    public void HeadingsSlugLikeGitHub()
    {
        Assert.Equal("the-runtime-map-reaching-a-tile-while-the-game-is-running",
            FunctionKb.Slug("# The runtime map: reaching a tile while the game is running"));
        Assert.Equal("fun_004508d0-and-friends", FunctionKb.Slug("## `FUN_004508D0` and friends"));
    }

    [Fact]
    public void AMentionInAChangedLineNeedsARecord()
    {
        var changes = new[]
        {
            ("docs/research/combat.md", new[] { "`FUN_0047AB10` decides the target", "and FUN_0050ea70 dumps it" }),
            ("src/DK2.Simulation/Combat.cs", new[] { "// mirrors FUN_0047AB10" }),
            ("docs/research/functions/0050EA70.md", new[] { "address: 0x0050EA70" }),   // records themselves are exempt
            ("docs/plans/2026-09-30-x.md", new[] { "example: FUN_00477777" }),          // plans are process, not claims
        };
        var findings = FunctionKb.MissingRecords(changes, new HashSet<string> { "0050EA70" }).ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Contains("0047AB10", f, StringComparison.Ordinal));
    }

    [Fact]
    public void TheIndexIsOneRowPerRecordSortedByAddress()
    {
        string index = FunctionKb.Index([Record("0050EA70"), Record("00452F60")]);
        string[] rows = index.Split('\n').Where(l => l.StartsWith("| `0x", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.StartsWith("| `0x00452F60`", rows[0]);
        Assert.Contains("[WorldLog_Dump](0050EA70.md)", index);
        Assert.Contains("generated by `dk2tools kb index`", index);
    }

    [Fact]
    public void APipeInARoleIsEscapedInItsIndexRow()
    {
        var record = Record("0050EA70") with { Role = "counts imps | dumps the world" };
        string row = FunctionKb.Index([record]).Split('\n').Single(l => l.StartsWith("| `0x", StringComparison.Ordinal));
        Assert.Contains(@"counts imps \| dumps the world", row, StringComparison.Ordinal);
        Assert.Equal(7, row.Count(c => c == '|'));   // six cell boundaries plus the escaped one
    }

    [Theory]
    [InlineData("calls 0x0048F250 for the test", true)]
    [InlineData("calls 0x48f250 for the test", true)]
    [InlineData("see FUN_0048F250", true)]
    [InlineData("KB `0048F250`", true)]
    [InlineData("0x0048F2500 is another number", false)]
    [InlineData("0x1048F250 is another address", false)]
    [InlineData("a bare 48F250 is not a citation", false)]
    public void CitesMatchesTheAddressInEveryWrittenForm(string text, bool expected)
        => Assert.Equal(expected, FunctionKb.Cites(text, "0048F250"));

    [Fact]
    public void UntouchedCitersSkipTheChangeTheRecordItselfTheIndexAndPlans()
    {
        (string, string)[] files =
        [
            ("src/Fear.cs", "// 0x0048F250: below a third"),
            ("src/Flowers.cs", "// 0x0048F250: below a sixth"),
            ("docs/research/functions/0048F250.md", "address: 0x0048F250"),
            ("docs/research/functions/INDEX.md", "| `0x0048F250` |"),
            ("docs/plans/old.md", "0x0048F250"),
            ("src/Other.cs", "0x0045EDA0"),
        ];
        var changed = new HashSet<string>(StringComparer.Ordinal) { "src/Flowers.cs" };

        var hits = FunctionKb.UntouchedCiters(["0048F250"], files, changed).ToList();

        (string address, IReadOnlyList<string> citers) = Assert.Single(hits);
        Assert.Equal("0048F250", address);
        Assert.Equal(["src/Fear.cs"], citers);
    }

    [Fact]
    public void ARecordGainsAReadingWhenNewOrWithANewHistoryLine()
    {
        var errors = new List<string>();
        FunctionRecord before = Parse(Sample, errors);
        FunctionRecord after = Parse(Sample + "\n- 2026-10-30 claude: re-read.", errors);
        Assert.Empty(errors);
        Assert.True(FunctionKb.GainedReading(before, null));
        Assert.False(FunctionKb.GainedReading(before, before));
        Assert.True(FunctionKb.GainedReading(after, before));
    }
}
