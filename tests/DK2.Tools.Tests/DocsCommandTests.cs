// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Tools.Commands;

namespace DK2.Tools.Tests;

public class DocsCommandTests
{
    private static List<string> Check(string relative, string[] lines, string[]? baseLines = null,
        bool all = true, HashSet<int>? touched = null)
    {
        var findings = new List<string>();
        DocsCommand.CheckFile(relative, lines, baseLines ?? [], all, touched, findings);
        return findings;
    }

    private static string[] Repeat(string line, int count) => Enumerable.Repeat(line, count).ToArray();

    [Fact]
    public void CommitBodySkipsTheSubjectBlankLinesCommentsAndCoAuthorTrailers()
    {
        string[] message = ["subject", "", "one", "Co-Authored-By: someone", "# git comment", "two", ""];
        Assert.Equal(2, DocsCommand.CommitBodyLines(message));
    }

    [Fact]
    public void DocCommentOverTwelveLinesIsReportedAndTwelveIsNot()
    {
        Assert.Equal(["src/A.cs:1: doc comment is 13, budget 12"],
            Check("src/A.cs", [.. Repeat("/// x", 13), "class A { }"]));
        Assert.Empty(Check("src/A.cs", [.. Repeat("/// x", 12), "class A { }"]));
    }

    [Fact]
    public void InlineCommentOverSixLinesIsReportedOnlyWhenTouched()
    {
        string[] lines = ["class A", "{", .. Repeat("    // x", 7), "}"];
        Assert.Equal(["src/A.cs:3: inline comment is 7, budget 6"], Check("src/A.cs", lines));
        Assert.Empty(Check("src/A.cs", lines, all: false, touched: [1]));
        Assert.Single(Check("src/A.cs", lines, all: false, touched: [9]));
    }

    [Fact]
    public void CommentRatioFailsOnlyWhenTheChangeMadeItWorse()
    {
        string[] half = Enumerable.Range(0, 80).Select(i => i % 2 == 0 ? "// c" : "x();").ToArray();
        string[] quarter = Enumerable.Range(0, 80).Select(i => i % 4 == 0 ? "// c" : "x();").ToArray();
        Assert.Equal(["src/A.cs: comment lines is 50, budget 40"], Check("src/A.cs", half));
        Assert.Empty(Check("src/A.cs", half, half, all: false));
        Assert.Single(Check("src/A.cs", half, quarter, all: false));
        Assert.Empty(Check("docs/A.md", half));
    }

    [Fact]
    public void QuestionRowOverEightHundredCharactersIsReported()
    {
        string row = "| Q12 | " + new string('x', 793);
        Assert.Equal(801, row.Length);
        Assert.Equal(["docs/research/open-questions.md:2: question row is 801, budget 800"],
            Check("docs/research/open-questions.md", ["| Q | Question |", row]));
        Assert.Empty(Check("docs/research/open-questions.md", ["| Q12 | short |"]));
    }

    [Fact]
    public void AdrSectionOverFiftyLinesIsReportedWithItsHeading()
    {
        string[] lines = ["## ADR-0001 A decision whose heading runs past forty characters", .. Repeat("text", 50),
            "## ADR-0002 Short", "text"];
        Assert.Equal(["docs/DECISIONS.md:1: ## ADR-0001 A decision whose heading run... is 51, budget 50"],
            Check("docs/DECISIONS.md", lines));
    }

    [Fact]
    public void ResearchDocLengthFailsOnlyWhenItGrew()
    {
        string[] long401 = Repeat("text", 401);
        Assert.Equal(["docs/research/a.md: length is 401, budget 400"], Check("docs/research/a.md", long401));
        Assert.Empty(Check("docs/research/a.md", long401, long401, all: false));
        Assert.Empty(Check("docs/research/functions/INDEX.md", long401));
    }

    [Fact]
    public void HandoffNotDoneYetClaimNeedsADate()
    {
        Assert.Equal(["docs/HANDOFF.md:1: a 'not done yet' claim with no date on it"],
            Check("docs/HANDOFF.md", ["The capture is not yet run."]));
        Assert.Empty(Check("docs/HANDOFF.md", ["The capture is not yet run (2026-10-06)."]));
    }
}
