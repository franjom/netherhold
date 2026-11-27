// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free creature query details (ADR-0805): menu 40's text widget and its scroll bar.</summary>
public sealed class CreatureQueryHudTests
{
    private static readonly HudScreen FullHd = new HudScreen(1920, 1080);
    private static readonly HudVisibility Shown = new HudVisibility(ShowNormalHud: true, AllowOrdinaryInput: true);
    private static readonly HudRect Widget = new HudRect(1040, 1000, 1096, 288);

    /// <summary>One 2x2 glyph 'A' at full coverage, 3 wide with its spacing, in a 10-pixel line.</summary>
    private static readonly BitmapFont Font = new BitmapFont(10, [new BitmapGlyph('A', 2, 2, 0, 0, 3, [15, 15, 15, 15])]);

    private static HudDraw[] Build(CreatureQueryHud hud, string text, bool otherPanelOpen = false)
    {
        var frame = new HudFrame();
        hud.Build(FullHd, new CreatureQuerySnapshot(default, text, default), Shown, otherPanelOpen, frame);
        return frame.Draws.ToArray();
    }

    [Fact]
    public void TextThatFitsTakesTheWholeWidgetWithoutABar()
    {
        HudDraw text = Assert.Single(Build(new CreatureQueryHud(_ => Font), "AA"));
        Assert.Equal(Widget, text.Clip);
        Assert.Equal(new HudRect(1040, 1000, 5 / FullHd.Scale, 10 / FullHd.Scale), text.Rect);
        // 1096 units at 1080 is 616 pixels.
        Assert.Equal(616, text.Image.Text!.WrapWidth);
    }

    [Fact]
    public void TallerTextIsWrapped64NarrowerAndShowsTheBarWithItsThumbAtTheTop()
    {
        string tall = string.Join("\n", Enumerable.Repeat("A", 20));   // 200 px against the widget's 162
        HudDraw[] draws = Build(new CreatureQueryHud(_ => Font), tall);
        Assert.Equal(5, draws.Length);
        Assert.Equal(580, draws[0].Image.Text!.WrapWidth);
        Assert.Equal(Widget, draws[0].Clip);
        Assert.Equal(new HudRect(2136, 1000, 64, 64), draws[1].Rect);
        Assert.Equal(new HudRect(2136, 1064, 64, 160), draws[2].Rect);
        Assert.Equal(new HudRect(2156, 1068, 28, 8), draws[3].Rect);
        Assert.Equal(new HudRect(2136, 1224, 64, 64), draws[4].Rect);
        Assert.Equal("GUI\\Scroll Bars\\v-down_arrow", draws[4].Image.Path);
    }

    [Fact]
    public void NewTextIsSetAgainAndCanBringTheBar()
    {
        var hud = new CreatureQueryHud(_ => Font);
        Assert.Single(Build(hud, "AA"));
        Assert.Equal(5, Build(hud, string.Join("\n", Enumerable.Repeat("A", 20))).Length);
        Assert.Equal(616, Assert.Single(Build(hud, "A")).Image.Text!.WrapWidth);
    }

    [Fact]
    public void NoTextOrAnOpenPanelHidesItAndLetsThePointerThrough()
    {
        var hud = new CreatureQueryHud(_ => Font);
        Build(hud, "AA");
        Assert.True(hud.ContainsPointer(1100, 1100, Shown));
        Assert.False(hud.ContainsPointer(1100, 1100, new HudVisibility(true, false)));
        Assert.Empty(Build(hud, "AA", otherPanelOpen: true));
        Assert.False(hud.ContainsPointer(1100, 1100, Shown));
        Assert.Empty(Build(hud, ""));
    }
}
