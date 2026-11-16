// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class GameFoldersTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "dk2-folders");

    [Fact]
    public void EveryFileDefaultsUnderTheEnginesRoot()
    {
        var folders = new GameFolders(Root);
        Assert.Equal(Path.Combine(Root, "campaign-profile.dk2p"), folders.Profile);
        Assert.Equal(Path.Combine(Path.GetFullPath(Root), "game-settings.ini"), folders.Settings);
        Assert.Equal(Path.Combine(Root, "Saves"), folders.Saves);
        Assert.Equal(Path.Combine(Root, "HiScores.dat"), folders.HiScores);
    }

    [Fact]
    public void TheSettingsFollowANamedProfileUnlessTheyAreNamedThemselves()
    {
        string elsewhere = Path.Combine(Path.GetTempPath(), "dk2-other", "me.dk2p");
        var folders = new GameFolders(Root) { ProfileFile = elsewhere };
        Assert.Equal(elsewhere, folders.Profile);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(elsewhere)!, "game-settings.ini"), folders.Settings);
        string settings = Path.Combine(Path.GetTempPath(), "s.ini");
        Assert.Equal(settings, (folders with { SettingsFile = settings }).Settings);
    }

    [Fact]
    public void ARelativeProfileIsKeptAsNamedAndItsSettingsResolveFromTheWorkingDirectory()
    {
        string relative = Path.Combine("profiles", "me.dk2p");
        var folders = new GameFolders(Root) { ProfileFile = relative };
        Assert.Equal(relative, folders.Profile);
        Assert.Equal(Path.Combine(Path.GetFullPath("profiles"), "game-settings.ini"), folders.Settings);
    }

    [Fact]
    public void ABlankOverrideKeepsTheDefault()
    {
        var folders = new GameFolders(Root) { ProfileFile = " ", SavesDirectory = "", HiScoresFile = "\t", SettingsFile = " " };
        Assert.Equal(new GameFolders(Root).Profile, folders.Profile);
        Assert.Equal(new GameFolders(Root).Settings, folders.Settings);
        Assert.Equal(new GameFolders(Root).Saves, folders.Saves);
        Assert.Equal(new GameFolders(Root).HiScores, folders.HiScores);
        Assert.Equal("x", (folders with { SavesDirectory = "x" }).Saves);
    }
}
