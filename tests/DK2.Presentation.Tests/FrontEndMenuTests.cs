// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class FrontEndMenuTests
{
    [Fact]
    public void LoadPageEnablesOnlyWithABrowserAndItsLoadRaisesTheSelectedPath()
    {
        var session = ReadySession();
        using var menu = new FrontEndMenuController(session, HitBounds);
        var entry = new DK2.Data.Save.SavedSessionEntry("first.dk2g", new("Level1", 1, 26,
            new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), "First save"));
        var view = new SaveSlotsView("unused", () => new[] { entry });
        menu.SetSaveBrowser(view);
        menu.Activate(new(1));
        Assert.True(menu.Activate(new(7)));
        session.Update(TimeSpan.FromSeconds(3));
        Assert.Equal(FrontEndPage.Load, session.Page);
        string? requested = null;
        menu.LoadRequested += path => requested = path;
        Assert.False(menu.Activate(new(73)));
        view.Select(0);
        Assert.True(menu.Activate(new(73)));
        Assert.Equal("first.dk2g", requested);
    }

    [Fact]
    public void CatalogueRetainsAllThirtyTwoPagesAndFourHundredTwoWidgets()
    {
        Assert.Equal(32, FrontEndPages.All.Count);
        Assert.Equal(402, FrontEndPages.All.Sum(p => p.Widgets.Count));
        Assert.Equal(32, FrontEndPages.All.Select(p => p.Id).Distinct().Count());
        var single = FrontEndPages.Get(FrontEndPage.Main).Widgets.Single(w => w.Id.Value == 1);
        Assert.Equal(71u, single.TextId);
        Assert.Equal(new FrontEndRect(628, 492, 1304, 172), single.DrawRect);
        Assert.Equal(FrontEndActionKind.SwapPage, single.Primary.Kind);
        Assert.Equal(FrontEndPage.SinglePlayer, single.Primary.Arguments.NextPage);
    }

    [Fact]
    public void PointerPressSwapsPageOnceUntilReleasedAndBlocksDuringCameraPaths()
    {
        var session = ReadySession();
        using var menu = new FrontEndMenuController(session, HitBounds);
        menu.UpdatePointer(new(1, 1, true));
        Assert.Equal(FrontEndPage.SinglePlayer, session.Page);
        menu.UpdatePointer(new(1, 1, true));
        Assert.Equal(FrontEndPage.SinglePlayer, session.Page);
        menu.UpdatePointer(new(1, 1, false));
        Assert.False(menu.IsEnabled(FrontEndPages.Get(FrontEndPage.SinglePlayer).Widgets.Single(w => w.Id.Value == 6)));
    }

    [Fact]
    public void QuitDialogCanCancelThenConfirmWithoutClosingTheGameOnOpen()
    {
        var session = ReadySession();
        using var menu = new FrontEndMenuController(session, HitBounds);
        int exits = 0;
        menu.QuitRequested += () => exits++;
        menu.Activate(new(5));
        Assert.Equal(FrontEndPage.Dialog, session.Page);
        Assert.Equal(28u, menu.Dialog!.TitleTextId);
        Assert.Equal(15u, menu.Dialog.BodyTextId);
        Assert.Equal(0, exits);
        menu.Activate(new(668));
        Assert.Equal(FrontEndPage.Main, session.Page);
        menu.Activate(new(5)); menu.Activate(new(667));
        Assert.Equal(1, exits);
    }

    [Fact]
    public void HoverSoundFiresOnceUntilTheNativeFadeHasExpired()
    {
        using var menu = new FrontEndMenuController(ReadySession(), HitBounds);
        var sounds = new List<int>();
        menu.SoundRequested += sound => sounds.Add(sound.Id);
        menu.UpdatePointer(new(1, 1, false)); menu.AdvanceFrame();
        menu.UpdatePointer(new(1, 1, false)); menu.AdvanceFrame();
        Assert.Equal(new[] { 0x30f }, sounds);
        menu.UpdatePointer(new(-1, -1, false));
        for (int i = 0; i < 9; i++) menu.AdvanceFrame();
        menu.UpdatePointer(new(1, 1, false));
        Assert.Equal(new[] { 0x30f, 0x30f }, sounds);
    }

    [Fact]
    public void OptionsStartsTheNativePathAndHidesWidgetsUntilItFinishes()
    {
        var session = ReadySession();
        using var menu = new FrontEndMenuController(session, HitBounds);
        menu.Activate(new(3));
        Assert.Equal(FrontEndPath.EntryToOptions, session.Path);
        Assert.Equal(FrontEndPage.None, session.Page);
        Assert.Empty(menu.Widgets);
        session.Update(TimeSpan.FromSeconds(3));
        Assert.Equal(FrontEndPage.Options, session.Page);
        menu.Activate(new(110));
        Assert.Equal(FrontEndPage.Graphics, session.Page);
        menu.Activate(new(130));
        Assert.Equal(FrontEndPage.Options, session.Page);
        menu.Activate(new(113));
        Assert.Equal(FrontEndPath.OptionsToEntry, session.Path);
    }

    /// <summary>Extras' Today's Top Keepers (2802) swaps 6 for 18 with D0x3D; page 18's accept, D0x41,
    /// shows Extras again (0x005306F0 case 0x41).</summary>
    [Fact]
    public void TodaysTopKeepersShowsTheHiScoresAndItsAcceptReturnsToExtras()
    {
        var session = ReadySession();
        using var menu = new FrontEndMenuController(session, HitBounds);
        menu.Activate(new(4));
        session.Update(TimeSpan.FromSeconds(3));
        Assert.Equal(FrontEndPage.Extras, session.Page);
        Assert.True(menu.Activate(new(19)));
        Assert.Equal(FrontEndPage.HighScores, session.Page);
        Assert.True(menu.Activate(new(47)));
        Assert.Equal(FrontEndPage.Extras, session.Page);
    }

    private static FrontEndRect HitBounds(FrontEndWidget widget) => new(0, 0, 10, 10);
    private static FrontEndSession ReadySession()
    {
        var session = new FrontEndSession(path => Enumerable.Range(0, path == FrontEndPath.Intro ? 1 : 30)
            .Select(i => new KcsFrame(i, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000)).ToArray());
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.Zero);
        return session;
    }
}
