// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free bottom panel (ADR-0805): what it draws and how it answers the pointer,
/// with no install and no engine. A 16:9 screen is 3413.3 virtual units wide.</summary>
public sealed class BottomPanelHudTests
{
    private const float Wide = 1920f * 16 / 9;
    private static readonly HudRect RoomsTab = BottomPanelLayout.Tab((int)BottomPanelCategory.Rooms);

    private sealed class Art : IBottomPanelArt
    {
        public string? Icon(BottomPanelChoiceKey key) => "icon " + key.Value;
        public string Text(uint id) => "text " + id;
    }

    private static BottomPanelHud Panel() =>
        new(new Art(), new BottomPanelView(new GameDatabase([], [], [], new GlobalVariables()), PlayerIds.FirstKeeper, (_, name) => name));

    private static HudFrame Build(BottomPanelHud hud, long now = 0)
    {
        var frame = new HudFrame();
        hud.Build(Wide, now, frame);
        return frame;
    }

    private static void Click(BottomPanelHud hud, HudRect at, bool shift = false)
    {
        float x = at.X + at.Width / 2, y = at.Y + at.Height / 2;
        Assert.True(hud.PointerDown(x, y, 0));
        Assert.True(hud.PointerUp(x, y, 0, shift, false, false));
    }

    [Fact]
    public void TheStoneAndRailSpanTheScreenBehindTheTabs()
    {
        HudFrame frame = Build(Panel());
        HudDraw[] draws = frame.Draws.ToArray();
        HudDraw[] stone = draws.Where(d => d.Image.Path == "GUI\\Windows\\Panel-BG").ToArray();
        Assert.Equal((int)Math.Ceiling(Wide / 160), stone.Length);
        Assert.Equal(new HudRect(0, 1600, 160, 300), stone[0].Rect);
        Assert.Equal(Wide, stone[^1].Rect.X + stone[^1].Rect.Width, 3);
        // The metal pair covers the last 480 units; the wood stops where it starts.
        Assert.Equal(new HudRect(Wide - 480, 1460, 240, 128), draws.Single(d => d.Image.Path == "GUI\\Windows\\Bar-02").Rect);
        Assert.Equal(Wide - 480, draws.Where(d => d.Image.Path == "GUI\\Windows\\Bar-00").Max(d => d.Rect.X + d.Rect.Width), 3);
        // Back to front: the stone first, the tabs over the rail.
        Assert.Equal("GUI\\Windows\\Panel-BG", draws[0].Image.Path);
        Assert.True(Array.FindIndex(draws, d => d.Image.Path == "GUI\\Tabs\\t-rooms") > Array.FindIndex(draws, d => d.Image.Path == "GUI\\Windows\\Bar-03"));
    }

    [Fact]
    public void WithNoSpellOrWorkshopItemOnlyCreaturesAndRoomsTabsShow()
    {
        string[] tabs = Build(Panel()).Draws.ToArray().Select(d => d.Image.Path!).Where(p => p.StartsWith("GUI\\Tabs\\")).ToArray();
        Assert.Equal(["GUI\\Tabs\\t-creatures", "GUI\\Tabs\\t-rooms"], tabs);
    }

    [Fact]
    public void HoveringATabDrawsTheAdditiveHighlightOverIt()
    {
        BottomPanelHud hud = Panel();
        Build(hud);
        hud.PointerMove(RoomsTab.X + 10, RoomsTab.Y + 10);
        HudDraw hover = Build(hud).Draws.ToArray().Single(d => d.Image.Path == "GUI\\Icons\\hilight");
        Assert.Equal(RoomsTab, hover.Rect);
        Assert.Equal(HudBlend.Additive, hover.Blend);
        Assert.Equal("text " + HudWidgetText.Tab(BottomPanelCategory.Rooms), hud.HoverText.TooltipName);

        hud.PointerLeave();
        Assert.DoesNotContain(Build(hud).Draws.ToArray(), d => d.Image.Path == "GUI\\Icons\\hilight");
    }

    [Fact]
    public void ClickingATabPostsItsButtonAndOpensIt()
    {
        BottomPanelHud hud = Panel();
        var posted = new List<GuiButtonTarget>();
        hud.GuiButton += posted.Add;
        Build(hud);
        Click(hud, RoomsTab);
        Assert.Equal([GuiButtonPairs.ForTab(BottomPanelCategory.Rooms)], posted);
        Assert.True(hud.OpenPanel.Contains(BottomPanelCategory.Rooms));
        Assert.False(hud.HasPointerCapture);
        Assert.Contains(Build(hud).Draws.ToArray(), d => d.Image.Path == "GUI\\Icons\\selected-room" && d.Rect == RoomsTab);
    }

    [Fact]
    public void AReleaseAwayFromThePressedWidgetDoesNothing()
    {
        BottomPanelHud hud = Panel();
        Build(hud);
        Assert.True(hud.PointerDown(RoomsTab.X + 1, RoomsTab.Y + 1, 0));
        Assert.True(hud.HasPointerCapture);
        Assert.True(hud.PointerUp(10, 10, 0, false, false, false));
        Assert.False(hud.HasPointerCapture);
        Assert.False(hud.OpenPanel.Contains(BottomPanelCategory.Rooms));
    }

    [Fact]
    public void ARightClickOnATabIsNotTakenAndAMismatchedReleaseKeepsThePress()
    {
        BottomPanelHud hud = Panel();
        Build(hud);
        Assert.False(hud.PointerDown(RoomsTab.X + 1, RoomsTab.Y + 1, 1));
        Assert.True(hud.PointerDown(RoomsTab.X + 1, RoomsTab.Y + 1, 0));
        Assert.False(hud.PointerUp(RoomsTab.X + 1, RoomsTab.Y + 1, 1, false, false, false));
        Assert.True(hud.HasPointerCapture);
    }

    [Fact]
    public void ResizeCollapsesThePanelAndDropsTheStrip()
    {
        BottomPanelHud hud = Panel();
        var collapsed = new List<bool>();
        hud.CollapsedChanged += collapsed.Add;
        Build(hud);
        Click(hud, new HudRect(472 + 544, 1460, 128, 128));
        Assert.Equal([true], collapsed);
        int drop = ClassicCamera.CollapsedPanelTop - ClassicCamera.ExpandedPanelTop;
        HudDraw rooms = Build(hud).Draws.ToArray().Single(d => d.Image.Path == "GUI\\Tabs\\t-rooms");
        Assert.Equal(RoomsTab.Y + drop, rooms.Rect.Y);
        Assert.Equal(1460 + drop, hud.Bounds.Y);
        Assert.DoesNotContain(Build(hud).Draws.ToArray(), d => d.Image.Path == "GUI\\Spells\\reaper_talisman-00");
    }

    [Fact]
    public void AStillPointerLosesTheResizeHoverWhenTheButtonDrops()
    {
        BottomPanelHud hud = Panel();
        var resize = new HudRect(472 + 544, 1460, 128, 128);
        Build(hud);
        hud.PointerMove(resize.X + 64, resize.Y + 10);
        Assert.Equal("text " + HudWidgetText.ResizePanel, hud.HoverText.TooltipName);
        Click(hud, resize);
        Build(hud);
        hud.Repick();
        Assert.Equal("", hud.HoverText.TooltipName);
    }

    [Fact]
    public void AHiddenPanelDrawsNothingAndTakesNoPointer()
    {
        BottomPanelHud hud = Panel();
        Build(hud);
        hud.SetVisibility(new HudVisibility(ShowNormalHud: false, AllowOrdinaryInput: false));
        Assert.Equal(0, Build(hud).Count);
        Assert.Equal(-1, hud.HitAction(RoomsTab.X + 1, RoomsTab.Y + 1));
        Assert.False(hud.ContainsPointer(600, 1700));
    }

    [Fact]
    public void ThePanelClaimsTheStripAndTheReaperButNotTheWorldAbove()
    {
        BottomPanelHud hud = Panel();
        Build(hud);
        Assert.True(hud.ContainsPointer(600, 1700));
        Assert.True(hud.ContainsPointer(Wide - 200, 1700));
        Assert.False(hud.ContainsPointer(600, 1000));
        Assert.False(hud.ContainsPointer(100, 1700));   // under the minimap
    }

    [Fact]
    public void AnUnchangedPanelBuildsTheSameFrame()
    {
        BottomPanelHud hud = Panel();
        HudFrame first = Build(hud), copy = new();
        copy.CopyFrom(first);
        Assert.True(Build(hud).SameAs(copy));
        hud.PointerMove(RoomsTab.X + 1, RoomsTab.Y + 1);
        Assert.False(Build(hud).SameAs(copy));
    }

    [Theory]
    [InlineData(25, 4)]
    [InlineData(50, 5)]
    [InlineData(99, 7)]
    [InlineData(0, 0)]
    [InlineData(100, 0)]
    public void ASectorFanReachesTheCornersItSweepsPast(int percent, int vertices)
    {
        Span<HudVertex> fan = stackalloc HudVertex[HudSector.MaxVertices];
        Assert.Equal(vertices, HudSector.Fan(new HudRect(0, 0, 128, 128), percent, fan));
        if (vertices == 0) return;
        Assert.Equal(new HudVertex(64, 64, .5f, .5f), fan[0]);
        Assert.Equal(new HudVertex(64, 0, .5f, 0), fan[1]);
    }

    [Fact]
    public void HalfASectorEndsAtSixOClock()
    {
        Span<HudVertex> fan = stackalloc HudVertex[HudSector.MaxVertices];
        int n = HudSector.Fan(new HudRect(0, 0, 128, 128), 50, fan);
        Assert.Equal(64, fan[n - 1].X, 3);
        Assert.Equal(128, fan[n - 1].Y, 3);
    }

    [Fact]
    public void TheTalismanKeepsItsQuartersClockwiseFromTheTopLeft()
    {
        byte[] opaque = Enumerable.Repeat((byte)255, 2 * 2 * 4).ToArray();
        static bool[] Kept(byte[] p) => [p[3] != 0, p[7] != 0, p[11] != 0, p[15] != 0];   // TL, TR, BL, BR
        Assert.Equal([true, false, false, false], Kept(HudImagePixels.Apply(opaque, 2, 2, new HudImage("t", HudImageVariant.TalismanArc, 1))));
        Assert.Equal([true, true, false, false], Kept(HudImagePixels.Apply(opaque, 2, 2, new HudImage("t", HudImageVariant.TalismanArc, 2))));
        Assert.Equal([true, true, false, true], Kept(HudImagePixels.Apply(opaque, 2, 2, new HudImage("t", HudImageVariant.TalismanArc, 3))));
        Assert.Equal([false, false, false, true], Kept(HudImagePixels.Apply(opaque, 2, 2, new HudImage("t", HudImageVariant.TalismanQuarter, 3))));
        Assert.All(opaque, b => Assert.Equal(255, b));
    }

    [Fact]
    public void TheAdditiveStandInScalesColourToFullAndMovesBrightnessIntoAlpha()
    {
        byte[] baked = HudImagePixels.AdditiveOverAlpha([100, 50, 0, 255, 0, 0, 0, 255]);
        Assert.Equal(new byte[] { 255, 127, 0, 100, 0, 0, 0, 0 }, baked);
    }
}
