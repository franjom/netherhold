// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free hover HUD (ADR-0805): status bar, scrolling status line and delayed tooltip.</summary>
public sealed class HoverHudTests
{
    private static readonly HudScreen FullHd = new(1920, 1080);
    private static readonly HudVisibility Shown = new(ShowNormalHud: true, AllowOrdinaryInput: true);
    private const float Scale = 1080 / 1920f;

    /// <summary>One 2x2 glyph 'A' at full coverage, 3 wide with its spacing, in a 10-pixel line.</summary>
    private static readonly BitmapFont Font = new(10, [new BitmapGlyph('A', 2, 2, 0, 0, 3, [15, 15, 15, 15])]);

    private static readonly WorldHoverHit Tile = new(WorldHoverKind.Tile, new TilePos(3, 4));

    private static HoverHud Hud()
    {
        return new(_ => Font, (64, 32));
    }

    private static (HudDraw[] Bar, HudDraw[] Top) Build(HoverHud hud, HoverTextSnapshot snapshot, long ms,
        (float, float) pointer = default)
    {
        HudFrame bar = new(), top = new();
        hud.Build(FullHd, 1920, snapshot, pointer, 80, ms, bar, top);
        return (bar.Draws.ToArray(), top.Draws.ToArray());
    }

    [Fact]
    public void TheBarTilesTheWholeWidthAtItsHeightUnderAnUnclippedEndCap()
    {
        (HudDraw[] bar, _) = Build(Hud(), HoverTextSnapshot.Empty, 0);
        // 64x32 at 88 high is 176 wide: 20 tiles over 3413.3, the last one cropped.
        Assert.Equal(new HudRect(0, 20, 176, 88), bar[0].Rect);
        Assert.Equal(20, bar.Count(d => d.Image == HoverHud.Bar));
        Assert.True(bar[^2].Uv!.Value.Width < 1);
        Assert.Equal(new HudDraw(HudDrawKind.Quad, HoverHud.Cap, new HudRect(FullHd.VirtualWidth - 80, 0, 128, 128), HudColor.White), bar[^1]);
    }

    [Fact]
    public void TheStatusLineSitsInsideTheStripAndALongOneScrollsBackAndForth()
    {
        HoverHud hud = Hud();
        HudDraw text = Assert.Single(Build(hud, new HoverTextSnapshot("AA", "", default), 0).Top);
        // The strip starts at 1020 units floored to pixels (573 px), the text 12 px in.
        Assert.Equal((573 + 12) / Scale, text.Rect.X, 3);
        Assert.Equal((573 + 12) / Scale, text.Clip!.Value.X, 3);
        Assert.Equal(20 + (88 - 10 / Scale) / 2, text.Rect.Y, 3);

        // 500 letters are 1499 px against a 1278 px window: 221 px to scroll at 60 a second.
        var longer = new HoverTextSnapshot(new string('A', 500), "", default);
        // A new line starts at 0 and moves in the same frame: 1 s since the last frame is 60 px.
        float start = (573 + 12) / Scale;
        Assert.Equal(start - 60 / Scale, Assert.Single(Build(hud, longer, 1000).Top).Rect.X, 3);
        Assert.Equal(start - 120 / Scale, Assert.Single(Build(hud, longer, 2000).Top).Rect.X, 3);
        Assert.Equal(start - 221 / Scale, Assert.Single(Build(hud, longer, 4000).Top).Rect.X, 3);
        Assert.Equal(start - 161 / Scale, Assert.Single(Build(hud, longer, 5000).Top).Rect.X, 3);
        // Back to the start, then forward again.
        Assert.Equal(start, Assert.Single(Build(hud, longer, 9000).Top).Rect.X, 3);
        Assert.Equal(start - 60 / Scale, Assert.Single(Build(hud, longer, 10000).Top).Rect.X, 3);
    }

    [Fact]
    public void TheTooltipWaitsThenPopsUpRightOfTheCursorInATextBox()
    {
        HoverHud hud = Hud();
        var snapshot = new HoverTextSnapshot("", "AA", Tile);
        Assert.Empty(Build(hud, snapshot, 0, (1000, 1000)).Top);
        Assert.Empty(Build(hud, snapshot, 1500, (1000, 1000)).Top);
        HudDraw[] popup = Build(hud, snapshot, 1501, (1000, 1000)).Top;
        Assert.Equal(10, popup.Length);
        Assert.All(popup[..9], d => Assert.StartsWith("GUI\\Windows\\text_box-", d.Image.Path));
        HudDraw text = popup[^1];
        Assert.True(text.Image.Text!.Centred);
        // 1000 units is 562.5 px; the box starts (80 + 18) x 2.25 px right of it, the text a padding of 11.7 in.
        float padding = 1080 * 1.3f / 120;
        Assert.Equal((562.5f + 98 * 2.25f + padding) / Scale, text.Rect.X, 2);
        Assert.Equal((562.5f + 98 * 2.25f) / Scale, popup[0].Rect.X, 2);
    }

    [Fact]
    public void NearTheRightEdgeTheTooltipFlipsLeftOfTheCursor()
    {
        HoverHud hud = Hud();
        var snapshot = new HoverTextSnapshot("", "AA", Tile);
        Build(hud, snapshot, 0);
        float x = 1900 / Scale;
        HudDraw box = Build(hud, snapshot, 2000, (x, 1000)).Top[0];
        float width = 5 + 2 * (1080 * 1.3f / 120);
        Assert.Equal((1900 - width - 30 * 2.25f) / Scale, box.Rect.X, 2);
    }

    [Fact]
    public void TheTooltipStaysBetweenTheBarAndTenPixelsAboveThePanelsBottom()
    {
        HoverHud hud = Hud();
        var snapshot = new HoverTextSnapshot("", "AA", Tile);
        Build(hud, snapshot, 0);
        float height = 10 + 2 * (1080 * 1.3f / 120);
        // At 150% the panel's bottom is 1280 units: 720 px.
        HudFrame bar = new(), top = new();
        hud.Build(FullHd, 1280, snapshot, (1000, 1270), 80, 2000, bar, top);
        Assert.Equal((720 - height - 10) / Scale, top.Draws[0].Rect.Y, 2);
        top.Clear();
        hud.Build(FullHd, 1280, snapshot, (1000, 0), 80, 2100, bar, top);
        Assert.Equal(88, top.Draws[0].Rect.Y, 2);
    }

    [Fact]
    public void ShownButWithInputBlockedTheBarLetsThePointerThrough()
    {
        HoverHud hud = Hud();
        Build(hud, HoverTextSnapshot.Empty, 0);
        hud.SetVisibility(new HudVisibility(ShowNormalHud: true, AllowOrdinaryInput: false), 0);
        Assert.False(hud.ContainsPointer(10, 30));
        Assert.NotEmpty(Build(hud, HoverTextSnapshot.Empty, 10).Bar);
    }

    [Fact]
    public void ACreaturesTooltipFollowsTheSetting()
    {
        HoverHud hud = Hud();
        hud.ShowCreatureTooltip = false;
        var snapshot = new HoverTextSnapshot("", "Imp", new WorldHoverHit(WorldHoverKind.Creature, default, 7));
        Build(hud, snapshot, 0);
        Assert.Empty(Build(hud, snapshot, 2000).Top);
    }

    [Fact]
    public void HiddenItDrawsNothingTakesNoPointerAndTheTooltipWaitsAgain()
    {
        HoverHud hud = Hud();
        var snapshot = new HoverTextSnapshot("AA", "AA", Tile);
        Build(hud, snapshot, 0);
        Assert.True(hud.ContainsPointer(10, 30));
        Assert.False(hud.ContainsPointer(10, 120));

        hud.SetVisibility(new HudVisibility(false, false), 100);
        (HudDraw[] bar, HudDraw[] top) = Build(hud, snapshot, 2000);
        Assert.Empty(bar); Assert.Empty(top);
        Assert.False(hud.ContainsPointer(10, 30));

        hud.SetVisibility(Shown, 2000);
        Assert.DoesNotContain(Build(hud, snapshot, 2100).Top, d => d.Image.Path?.StartsWith("GUI\\Windows\\text_box") == true);
        Assert.Contains(Build(hud, snapshot, 3700).Top, d => d.Image.Path?.StartsWith("GUI\\Windows\\text_box") == true);
    }

    [Fact]
    public void CentredTextIndentsEachLineByHalfItsGapToTheWidest()
    {
        byte[] pixels = HudTextPixels.Render(new HudText(Font, "AA\nA", 100, Centred: true), out int width, out int height);
        Assert.Equal((5, 20), (width, height));
        Assert.Equal(0, pixels[(10 * width + 0) * 4 + 3]);
        Assert.Equal(255, pixels[(10 * width + 1) * 4 + 3]);
    }
}
