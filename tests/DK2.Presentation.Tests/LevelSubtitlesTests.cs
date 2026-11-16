// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class LevelSubtitlesTests
{
    [Fact]
    public void AnIdThatNamesNoTableHasNoLines()
    {
        LevelSubtitles subtitles = LevelSubtitles.Open(Path.GetTempPath(), 0, _ => Assert.Fail("no table, no warning"));
        Assert.Null(subtitles.Line(0));
    }

    [Fact]
    public void AMissingTableHasNoLinesAndAnUnreadableOneWarns()
    {
        string install = Path.Combine(Path.GetTempPath(), "dk2-subtitles-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(LevelSubtitles.Open(install, 1, _ => Assert.Fail("a missing table is not a warning")).Line(0));
            Directory.CreateDirectory(LevelSubtitles.TextFolder(install));
            File.WriteAllBytes(Path.Combine(LevelSubtitles.TextFolder(install), "LEVEL1.str"), [1, 2, 3]);
            var warnings = new List<string>();
            Assert.Null(LevelSubtitles.Open(install, 1, warnings.Add).Line(0));
            Assert.StartsWith("subtitles unavailable: ", Assert.Single(warnings));
        }
        finally { if (Directory.Exists(install)) Directory.Delete(install, recursive: true); }
    }

    [InstallFact]
    public void Level1sTableGivesItsLinesByIndex()
    {
        LevelSubtitles subtitles = LevelSubtitles.Open(Dk2InstallGate.Path, 1);
        Assert.NotNull(subtitles.Line(0));
        Assert.Null(subtitles.Line(-1));
        Assert.Null(subtitles.Line(int.MaxValue));
    }
}
