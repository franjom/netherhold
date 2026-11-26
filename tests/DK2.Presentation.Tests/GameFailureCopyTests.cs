// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class GameFailureCopyTests
{
    [Fact]
    public void AMissingInstallOffersTheLauncherAndNamesTheFolderGiven()
    {
        string folder = Path.Combine(Path.GetTempPath(), "dk2-no-install-" + Guid.NewGuid().ToString("N"));
        GameFailureScreen screen = GameFailureCopy.For(new GameFailure(GameFailureKind.NoInstall, Path: folder), "C:/logs/godot.log");
        Assert.Equal("Dungeon Keeper 2 was not found", screen.Title);
        Assert.Contains(folder, screen.Detail);
        Assert.Contains("no DKII.EXE", screen.Detail);
        Assert.Equal("The details are in the log file:", screen.LogLine);
        Assert.Equal("C:/logs/godot.log", screen.LogFile);
        Assert.Equal([GameFailureAction.OpenLauncher, GameFailureAction.Quit], screen.Choices.Select(c => c.Action));
        Assert.Contains("DK2_DIR", GameFailureCopy.For(new GameFailure(GameFailureKind.NoInstall), null).Detail);
    }

    [Theory]
    [InlineData(GameFailureKind.LevelDidNotStart)]
    [InlineData(GameFailureKind.TurnFailed)]
    [InlineData(GameFailureKind.SaveImportFailed)]
    public void EveryOtherFailureGoesBackToTheMenuOrQuits(GameFailureKind kind)
    {
        GameFailureScreen screen = GameFailureCopy.For(new GameFailure(kind, "  boom  "), null);
        Assert.Equal([GameFailureAction.ReturnToMenu, GameFailureAction.Quit], screen.Choices.Select(c => c.Action));
        Assert.Equal("Boom", screen.Detail);
        Assert.Equal(GameFailureAction.ReturnToMenu, GameFailureCopy.ForKey(screen, GameFailureKey.Confirm));
        Assert.Equal(GameFailureAction.Quit, GameFailureCopy.ForKey(screen, GameFailureKey.Cancel));
        Assert.Equal(["Enter", "Esc"], screen.Choices.Select(c => c.Key));
        Assert.Contains("file logging is off", screen.LogLine);
        Assert.Null(screen.LogFile);
    }

    [Fact]
    public void TheWordsNameTheLevelTheTurnAndTheFile()
    {
        GameFailureScreen turn = GameFailureCopy.For(new GameFailure(GameFailureKind.TurnFailed, "x", "Level7", 412), null);
        Assert.Equal("The game stopped", turn.Title);
        Assert.StartsWith("Turn 412 of Level7 failed", turn.Summary);
        Assert.StartsWith("A turn failed", GameFailureCopy.For(new GameFailure(GameFailureKind.TurnFailed), null).Summary);
        Assert.StartsWith("Level3 failed while it loaded",
            GameFailureCopy.For(new GameFailure(GameFailureKind.LevelDidNotStart, Level: "Level3"), null).Summary);
        Assert.StartsWith("C:/a.sav could not be read",
            GameFailureCopy.For(new GameFailure(GameFailureKind.SaveImportFailed, Path: "C:/a.sav"), null).Summary);
        Assert.Null(GameFailureCopy.For(new GameFailure(GameFailureKind.SaveImportFailed, " "), null).Detail);
    }

    [Fact]
    public void OnlyARunWithADisplayWaitsForAChoice()
    {
        Assert.True(GameFailureCopy.WaitsForChoice(hasDisplay: true));
        Assert.False(GameFailureCopy.WaitsForChoice(hasDisplay: false));
        Assert.Equal(1, GameFailureCopy.ExitCode);
    }

    [Fact]
    public void TheLogFileIsTheEnginesArgumentThenTheDefaultWhenLoggingIsOn()
    {
        Assert.Equal("b.log", GameFailureCopy.LogFile(["--log-file", "a.log", "--headless", "--log-file", "b.log"], true, "user.log"));
        Assert.Equal("a.log", GameFailureCopy.LogFile(["--log-file", "a.log"], false, "user.log"));
        Assert.Equal("user.log", GameFailureCopy.LogFile(["--headless", "--log-file"], true, "user.log"));
        Assert.Null(GameFailureCopy.LogFile([], false, "user.log"));
        Assert.Null(GameFailureCopy.LogFile([], true, ""));
    }
}
