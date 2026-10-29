// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

public sealed class FrontEndStartupTests
{
    [Fact]
    public void TitleHoldsTenSecondsAndIgnoresSkip()
    {
        var startup = new FrontEndStartup();
        startup.Skip();
        Assert.Equal(FrontEndStartupStage.Title, startup.Update(TimeSpan.FromSeconds(9.9)));
        Assert.Equal(FrontEndStartupStage.BullfrogIntro, startup.Update(TimeSpan.FromSeconds(10)));
        startup.MovieFinished();
        Assert.Equal(FrontEndStartupStage.Intro, startup.Update(TimeSpan.FromSeconds(11)));
        startup.Skip();
        Assert.Equal(FrontEndStartupStage.Done, startup.Update(TimeSpan.FromSeconds(12)));
    }

    [Fact]
    public void IntroWaitsForTheMovieRatherThanAnAssumedDuration()
    {
        var startup = new FrontEndStartup();
        Assert.Equal(FrontEndStartupStage.BullfrogIntro, startup.Update(TimeSpan.FromHours(1)));
        Assert.Equal(FrontEndStartupStage.BullfrogIntro, startup.Update(TimeSpan.FromHours(2)));
        startup.MovieFinished();
        Assert.Equal(FrontEndStartupStage.Intro, startup.Stage);
    }

    [Fact]
    public void AttractReplaysAfterThreeMinutesIdleAndAlternates()
    {
        var attract = new FrontEndAttract();
        Assert.Null(attract.Update(TimeSpan.FromSeconds(179), FrontEndPage.Main, false));
        Assert.Null(attract.Update(TimeSpan.FromSeconds(180), FrontEndPage.Main, false));
        Assert.Equal(FrontEndMovie.BullfrogIntro, attract.Update(TimeSpan.FromMilliseconds(180001), FrontEndPage.Main, false));
        Assert.Equal(FrontEndMovie.Intro, attract.Update(TimeSpan.FromMilliseconds(360002), FrontEndPage.Main, false));
        Assert.Null(attract.Update(TimeSpan.FromSeconds(540), FrontEndPage.Sessions, false));
    }

    [Fact]
    public void PointerOrKeyResetsIdleAndBlockedPagesDoNotPlayMovies()
    {
        var attract = new FrontEndAttract();
        Assert.Null(attract.Update(TimeSpan.FromSeconds(170), FrontEndPage.Main, true));
        Assert.Null(attract.Update(TimeSpan.FromSeconds(180), FrontEndPage.Main, false));
        Assert.Null(attract.Update(TimeSpan.FromSeconds(340), FrontEndPage.Main, false, keyPressed: true));
        foreach (ushort page in new ushort[] { 10, 11, 32, 35, 39, 44 })
            Assert.Null(attract.Update(TimeSpan.FromSeconds(1000), (FrontEndPage)page, false));
        Assert.Null(attract.Update(TimeSpan.FromSeconds(1000), FrontEndPage.Main, false));
        Assert.Equal(FrontEndMovie.BullfrogIntro, attract.Update(TimeSpan.FromMilliseconds(1180001), FrontEndPage.Main, false));
    }
}
