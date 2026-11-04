// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Tools.Commands;
using DK2.Tools.Wiki;

namespace DK2.Tools.Tests;

/// <summary>The ADR-0643 index: systems, note tags, ADR parsing, the docs gate's rules and the generated pages.</summary>
public class WikiTests
{
    static string Root => Repo.FindRepoRoot() ?? throw new InvalidOperationException("no repository above the test");

    const string Catalog = """
        systems:
          - id: combat
            title: Combat
            scope: Fights.
          - id: rooms
            title: Rooms
            scope: Rooms.
        """;

    static DocsContext Context(params (string Note, string[] Systems)[] notes)
        => new(SystemCatalog.Parse(Catalog),
            notes.ToDictionary(n => n.Note, n => (IReadOnlyList<string>)n.Systems, StringComparer.Ordinal),
            notes.Select(n => n.Note).ToHashSet(StringComparer.Ordinal));

    [Fact]
    public void AdrsCitedInCodeComeFromSourceNotTestsOrBuildOutput()
    {
        string root = Directory.CreateTempSubdirectory("dk2-adrs-").FullName;
        try
        {
            void Write(string relative, string text)
            {
                string path = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            Write("src/A/Rules.cs", "// ADR-0001; see ADR-0058/0063/0065");
            Write("src/A/obj/Gen.cs", "// ADR-0002");
            Write("tests/A.Tests/T.cs", "// ADR-0003");
            Write("unity/DK2.Unity/Assets/DK2/Scripts/View.cs", "// ADR-0004");
            Write("src/A/Notes.md", "ADR-0005");

            Assert.Equal([1, 4, 58, 63, 65], WikiCommand.AdrsCitedInCode(root).Order());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static List<string> Check(string relative, string[] lines, DocsContext context, string[]? baseLines = null,
        bool all = true)
    {
        var findings = new List<string>();
        DocsCommand.CheckFile(relative, lines, baseLines ?? [], all, touched: null, findings, context);
        return findings;
    }

    [Fact]
    public void TheRealSystemListLoadsAndHasTheIssuesNineteenSystems()
    {
        var catalog = SystemCatalog.Load(Root);
        Assert.Equal(19, catalog.Systems.Count);
        Assert.Equal("simulation-core", catalog.Systems[0].Id);
        Assert.True(catalog.Contains("imps-tile-work"));
    }

    [Theory]
    [InlineData("  - id: combat\n    title: Again\n    scope: Twice.\n", "listed twice")]
    [InlineData("  - id: Bad_Id\n    title: Bad\n    scope: Bad.\n", "is not lower-case")]
    [InlineData("  - id: audio\n    title: Audio\n", "needs a title and a scope")]
    public void TheSystemListRejectsADuplicateABadIdAndAMissingField(string extra, string message)
    {
        var e = Assert.Throws<InvalidDataException>(() => SystemCatalog.Parse(Catalog + "\n" + extra));
        Assert.Contains(message, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontMatterIsReadAndMeasured()
    {
        string[] lines = ["﻿---", "systems: [combat, rooms]", "---", "# Title", "text"];
        Assert.Equal(3, ResearchNotes.FrontMatterLength(lines));
        Assert.Equal(["combat", "rooms"], ResearchNotes.Systems(lines));
        Assert.Equal("Title", ResearchNotes.Title(lines, "fallback"));
        Assert.Null(ResearchNotes.Systems(["# Title", "systems: [combat]"]));
        Assert.Equal(0, ResearchNotes.FrontMatterLength(["---", "never closed"]));
    }

    [Theory]
    [InlineData("docs/research/combat.md", "combat")]
    [InlineData("docs/research/open-questions.md", null)]
    [InlineData("docs/research/functions/0050EA70.md", null)]
    [InlineData("docs/plans/combat.md", null)]
    public void OnlyTopLevelResearchNotesAreNotes(string path, string? name)
        => Assert.Equal(name, ResearchNotes.NoteName(path));

    [Fact]
    public void AnUntaggedNoteAndAnUnknownSystemFailTheGate()
    {
        var context = Context();
        Assert.Single(Check("docs/research/fear.md", ["# Fear", "text"], context), f => f.Contains("names no systems"));
        Assert.Single(Check("docs/research/fear.md", ["---", "systems: [magic]", "---", "# Fear"], context),
            f => f.Contains("'magic' is not in docs/systems.yaml"));
        Assert.Empty(Check("docs/research/fear.md", ["---", "systems: [combat]", "---", "# Fear"], context));
        Assert.Empty(Check("docs/research/open-questions.md", ["# Open"], context));
    }

    [Fact]
    public void FrontMatterIsLeftOutOfTheLengthBudget()
    {
        // Already over budget before the change: tagging it must not count as making it worse.
        string[] prose = Enumerable.Repeat("x", DocBudgets.ResearchDoc + 1).ToArray();
        string[] tagged = ["---", "systems: [combat]", "---", .. prose];
        Assert.DoesNotContain(Check("docs/research/fear.md", tagged, Context(), baseLines: prose, all: false),
            f => f.Contains("length"));
    }

    const string Decisions = """
        # Architecture Decision Records

        ## ADR-0001 — Integer maths
        **Date:** 2026-08-29 · **Status:** accepted

        **Context.** See `research/fear.md` and research/fear.md again.

        ### ADR-0001 amendment — still integers (2026-09-01)
        Cites docs/research/rooms-note.md, which is not a note here.

        ## ADR-0002: a newer header
        **Date:** 2026-11-03. **Status:** implemented, 2026-11-03. · **Systems:** rooms

        **Decision.** Cites fear.md, but the explicit field wins.

        ## ADR-0003 - an old one
        **Status:** **reversed 2026-09-07 by ADR-0002** — no longer.

        **Context.** Nothing cited.
        """;

    static IReadOnlyList<Adr> ParseDecisions(string text, params string[] notes)
        => AdrIndex.Parse(text.Split('\n'), notes.ToHashSet(StringComparer.Ordinal));

    [Fact]
    public void AdrsAreParsedFromBothHeaderFormatsAndAnAmendmentStaysWithItsAdr()
    {
        var adrs = ParseDecisions(Decisions, "fear");
        Assert.Equal([1, 2, 3], adrs.Select(a => a.Number));
        Assert.Equal(["Integer maths", "a newer header", "an old one"], adrs.Select(a => a.Title));
        Assert.Equal(["accepted", "implemented", "superseded"], adrs.Select(a => a.Status));
        Assert.Equal(["fear"], adrs[0].CitedNotes);
        Assert.Null(adrs[0].ExplicitSystems);
        Assert.Equal(["rooms"], adrs[1].ExplicitSystems);
        Assert.Equal(2, adrs[2].SupersededBy);
        Assert.Empty(adrs[2].CitedNotes);
    }

    [Fact]
    public void AnAdrInheritsItsNotesSystemsUnlessItNamesItsOwn()
    {
        var context = Context(("fear", ["combat", "rooms"]));
        var adrs = AdrIndex.Parse(Decisions.Split('\n'), context.NoteNames);
        Assert.Equal(["combat", "rooms"], AdrIndex.Systems(adrs[0], context.NoteSystems, context.Catalog));
        Assert.Equal(["rooms"], AdrIndex.Systems(adrs[1], context.NoteSystems, context.Catalog));
        Assert.Empty(AdrIndex.Systems(adrs[2], context.NoteSystems, context.Catalog));
    }

    [Fact]
    public void AReusedAdrNumberAndAnAdrWithNoSystemFailTheGate()
    {
        var context = Context(("fear", ["combat"]));
        string reused = Decisions + "\n## ADR-0002 — reused\n**Status:** accepted · **Systems:** combat\n";
        var findings = Check("docs/DECISIONS.md", reused.Split('\n'), context);
        Assert.Contains(findings, f => f.Contains("ADR-0002 is used by more than one ADR"));
        Assert.Contains(findings, f => f.Contains("ADR-0003 has no system"));
        Assert.DoesNotContain(findings, f => f.Contains("ADR-0001 has no system"));
    }

    [Fact]
    public void AnAmendmentHeadingIsNotAReusedNumber()
        => Assert.Empty(AdrIndex.Duplicates(ParseDecisions(Decisions)));

    [Fact]
    public void EveryRealResearchNoteNamesKnownSystems()
    {
        var catalog = SystemCatalog.Load(Root);
        var notes = ResearchNotes.Load(Root);
        Assert.True(notes.Count > 250);
        Assert.All(notes, n => Assert.True(n.Systems.Count > 0 && n.Systems.All(catalog.Contains), n.Name));
    }

    [Fact]
    public void EveryRealAdrResolvesToAStatusAndASystemAndNoNumberIsReused()
    {
        var inputs = WikiCommand.Load(Root);
        var noteSystems = inputs.Notes.ToDictionary(n => n.Name, n => n.Systems, StringComparer.Ordinal);
        Assert.True(inputs.Adrs.Count > 600);
        Assert.Empty(AdrIndex.Duplicates(inputs.Adrs));
        Assert.All(inputs.Adrs, a =>
        {
            Assert.True(a.Status.Length > 0, $"ADR-{a.Number:D4} has no status");
            Assert.True(AdrIndex.Systems(a, noteSystems, inputs.Catalog).Count > 0, $"ADR-{a.Number:D4} has no system");
        });
    }

    [Fact]
    public void TheGeneratedPagesAreByteStable()
    {
        var first = WikiPages.All(WikiCommand.Load(Root));
        var second = WikiPages.All(WikiCommand.Load(Root));
        Assert.Equal(first.Select(p => p.File), second.Select(p => p.File));
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Content, second[i].Content);
            Assert.DoesNotContain('\r', first[i].Content);
        }
    }

    [Fact]
    public void AnOpenQuestionTakesItsStatePhraseAndTheNotesItCites()
    {
        string[] lines =
        [
            "| Q7 | Who digs? | **PARTLY ANSWERED** in fear.md | Run it |",
            "| Q8 | Done? | STRONGLY INFERRED (binary) | — |",
            "| Q9 | Done again? | ANSWERED, see unknown-note.md | — |",
        ];
        var rows = WikiPages.ParseQuestions(lines, new HashSet<string>(StringComparer.Ordinal) { "fear" });
        Assert.Equal(["PARTLY ANSWERED", "STRONGLY INFERRED", "ANSWERED"], rows.Select(r => r.State));
        Assert.Equal([true, false, false], rows.Select(r => r.IsOpen));
        Assert.Equal(["fear"], rows[0].CitedNotes);
        Assert.Empty(rows[2].CitedNotes);
    }
}
