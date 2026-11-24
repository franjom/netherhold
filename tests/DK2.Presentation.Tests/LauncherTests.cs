// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;

namespace DK2.Presentation.Tests;

public sealed class LauncherTests
{
    [Fact]
    public void SettingsRoundTripThroughTheirText()
    {
        var settings = new LauncherSettings
        {
            InstallFolder = @"E:\Games\Dungeon Keeper 2", MenuFont = @"C:\Fonts\a b.ttf",
            Screen = LauncherScreenMode.FullScreenWindow, WindowSize = new LauncherWindowSize(1600, 900), StartDirectly = true,
        };
        Assert.Equal(settings, LauncherSettings.Parse(settings.Format()));
        Assert.Equal(LauncherSettings.Default, LauncherSettings.Parse(LauncherSettings.Default.Format()));
    }

    [Fact]
    public void UnknownKeysAndMalformedValuesKeepTheDefaults()
    {
        LauncherSettings parsed = LauncherSettings.Parse(
            "# comment\r\nfuture=1\r\nscreen=sideways\r\nwindow=99x99\r\nstart_directly=yes\r\ninstall=\r\nmenu_font = \r\n" +
            "noequals\r\n=1\r\n");
        Assert.Equal(LauncherSettings.Default, parsed);
        Assert.Equal(new LauncherWindowSize(1280, 720), LauncherSettings.Parse("WINDOW=1280x720").WindowSize);
        Assert.Equal(LauncherScreenMode.FullScreen, LauncherSettings.Parse("screen=Full_Screen").Screen);
        Assert.Equal(@"D:\a=b", LauncherSettings.Parse(@"install=D:\a=b").InstallFolder);
    }

    [Fact]
    public void TheFileIsTheArgumentsOrTheUserFolders()
    {
        Assert.Equal(Path.Combine("root", "launcher.cfg"), LauncherSettingsFile.PathFor([], "root"));
        Assert.Equal("x.cfg", LauncherSettingsFile.PathFor(["--launcher-config", "x.cfg"], "root"));
        string folder = Path.Combine(Path.GetTempPath(), "dk2-launcher-tests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "sub", "launcher.cfg");
        try
        {
            Assert.Equal(LauncherSettings.Default, LauncherSettingsFile.Load(path));
            var settings = new LauncherSettings { InstallFolder = "here", StartDirectly = true };
            LauncherSettingsFile.Save(path, settings);
            Assert.Equal(settings, LauncherSettingsFile.Load(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void TheDefaultWindowIsTheLargestWideSizeWithAMargin()
    {
        Assert.Equal(new LauncherWindowSize(1600, 900), LauncherWindowSizes.Default(1920, 1032));
        Assert.Equal(new LauncherWindowSize(1920, 1080), LauncherWindowSizes.Default(2560, 1392));
        Assert.Equal(new LauncherWindowSize(3200, 1800), LauncherWindowSizes.Default(3840, 2112));
        Assert.Equal(new LauncherWindowSize(960, 540), LauncherWindowSizes.Default(800, 600));
    }

    [Fact]
    public void OnlySizesThatFitWithTheirFrameAreOffered()
    {
        IReadOnlyList<LauncherWindowSize> sizes = LauncherWindowSizes.Fitting(1920, 1032);
        Assert.DoesNotContain(new LauncherWindowSize(1920, 1080), sizes);
        Assert.Contains(new LauncherWindowSize(1600, 900), sizes);
        Assert.DoesNotContain(new LauncherWindowSize(1680, 1050), sizes);
        for (int i = 1; i < sizes.Count; i++) Assert.True(sizes[i - 1].Width >= sizes[i].Width);
        Assert.Equal([new LauncherWindowSize(960, 540)], LauncherWindowSizes.Fitting(640, 480));
    }

    [Fact]
    public void ASavedSizeIsKeptWhileItFits()
    {
        var (sizes, selected) = LauncherWindowSizes.Options(new LauncherWindowSize(1280, 720), 1920, 1032);
        Assert.Equal(new LauncherWindowSize(1280, 720), sizes[selected]);
        (sizes, selected) = LauncherWindowSizes.Options(new LauncherWindowSize(2560, 1440), 1920, 1032);
        Assert.Equal(new LauncherWindowSize(1600, 900), sizes[selected]);
        (sizes, selected) = LauncherWindowSizes.Options(new LauncherWindowSize(1500, 800), 1920, 1032);
        Assert.Equal(new LauncherWindowSize(1500, 800), sizes[selected]);
        Assert.Equal(new LauncherWindowSize(1600, 900), sizes[selected - 1]);
        Assert.Equal(new LauncherWindowSize(1440, 900), sizes[selected + 1]);
    }

    [Theory]
    [InlineData(96f, 1920, 1032, 1f)]
    [InlineData(144f, 3840, 2088, 1.5f)]
    [InlineData(192f, 1920, 1032, 1.25f)]
    [InlineData(192f, 3840, 2112, 2f)]
    [InlineData(72f, 1920, 1032, 1f)]
    public void TheLauncherScalesWithTheScreensDpiAndStaysOnIt(float dpi, int width, int height, float scale) =>
        Assert.Equal(scale, LauncherWindowSizes.LauncherScale(dpi, width, height));

    [Fact]
    public void ScriptedRunsStartAsBeforeUnlessTheLauncherIsAskedFor()
    {
        var plain = new LauncherStartFacts(LauncherRequested: false, Automated: false, ShiftHeld: false, StartDirectly: true,
            InstallFound: true);
        Assert.Equal(LauncherStart.StartWithSavedScreen, LauncherStartRule.Decide(plain));
        Assert.Equal(LauncherStart.ShowLauncher, LauncherStartRule.Decide(plain with { ShiftHeld = true }));
        Assert.Equal(LauncherStart.ShowLauncher, LauncherStartRule.Decide(plain with { StartDirectly = false }));
        Assert.Equal(LauncherStart.ShowLauncher, LauncherStartRule.Decide(plain with { InstallFound = false }));
        Assert.Equal(LauncherStart.StartAsBefore, LauncherStartRule.Decide(plain with { Automated = true, ShiftHeld = true }));
        Assert.Equal(LauncherStart.StartAsBefore, LauncherStartRule.Decide(plain with { Automated = true, StartDirectly = false }));
        Assert.Equal(LauncherStart.ShowLauncher, LauncherStartRule.Decide(plain with { Automated = true, LauncherRequested = true }));
    }

    [Fact]
    public void ARunIsScriptedWithoutADisplayUnderMovieMakerWithASceneOrAMovie()
    {
        string[] engine = ["--path", "godot/DK2.Godot", "--log-file", "x.log"];
        Assert.False(LauncherStartRule.IsAutomated(engine, ["--profile", "p"], headless: false, movieMaker: false));
        Assert.True(LauncherStartRule.IsAutomated(engine, [], headless: true, movieMaker: false));
        Assert.True(LauncherStartRule.IsAutomated(engine, [], headless: false, movieMaker: true));
        Assert.True(LauncherStartRule.IsAutomated([.. engine, "res://InGameHud.tscn"], [], false, false));
        Assert.True(LauncherStartRule.IsAutomated(["Main.SCN"], [], false, false));
        Assert.True(LauncherStartRule.IsAutomated(engine, ["--play-movie", "Data/Movies/x.tgq"], false, false));
        Assert.True(LauncherStartRule.IsAutomated(engine, ["--level", "Level6a", "--seed", "1"], false, false));
        Assert.False(LauncherStartRule.IsAutomated(engine, ["--seed", "1"], false, false));
        Assert.True(LauncherStartRule.Requested(["--profile", "p", "--launcher"]));
        Assert.False(LauncherStartRule.Requested(["--launcher-config", "x"]));
    }

    [Fact]
    public void EachInstallCheckHasItsLine()
    {
        Assert.Equal(new LauncherMessage("Found Dungeon Keeper 2.", LauncherTone.Ready), LauncherCopy.Install(Dk2InstallCheck.Found));
        Assert.Equal(new LauncherMessage("Choose the folder that holds DKII.EXE and the Data folder.", LauncherTone.Plain),
            LauncherCopy.Install(Dk2InstallCheck.Empty));
        Assert.Equal(new LauncherMessage("This folder has no DKII.EXE. Choose the folder the game was installed to.",
            LauncherTone.Problem), LauncherCopy.Install(Dk2InstallCheck.NoGame));
        Assert.Equal(LauncherTone.Problem, LauncherCopy.Install(Dk2InstallCheck.NoData).Tone);
    }

    [Fact]
    public void AMenuFontIsAnExistingTrueTypeOrOpenTypeFile()
    {
        Func<string, bool> exists = path => path == @"C:\f.ttf" || path == @"C:\f.TXT";
        Assert.Equal(LauncherFontCheck.Empty, LauncherCopy.CheckFont(" ", exists));
        Assert.Equal(LauncherFontCheck.Found, LauncherCopy.CheckFont(@" C:\f.ttf ", exists));
        Assert.Equal(LauncherFontCheck.Missing, LauncherCopy.CheckFont(@"C:\g.OTF", exists));
        Assert.Equal(LauncherFontCheck.NotAFont, LauncherCopy.CheckFont(@"C:\f.TXT", exists));
    }

    [Fact]
    public void APastedPathLosesItsQuotesAndBlanks()
    {
        Assert.Equal(@"C:\Games\DK2", LauncherCopy.CleanPath("  \"C:\\Games\\DK2\" "));
        Assert.Null(LauncherCopy.CleanPath(" \"\" "));
        Assert.Null(LauncherCopy.CleanPath(null));
        Assert.Equal("\"a", LauncherCopy.CleanPath("\"a"));
    }

    [Fact]
    public void TheMenuFontIsLookedForByTheEnvironmentThenTheCommandLineThenTheLauncher()
    {
        Assert.Equal([("DK2_REPLACEMENT_FONT", "e"), ("--menu-font", "c"), ("the launcher's menu font", "s")],
            MenuFontSearch.Named("e", "c", "s"));
        Assert.Equal([("the launcher's menu font", "s")], MenuFontSearch.Named(" ", null, "s"));
    }
}
