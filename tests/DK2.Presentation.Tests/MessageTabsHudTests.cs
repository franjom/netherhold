// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free message tabs (ADR-0805), and the draw-list pieces they brought: quad clipping,
/// tiling and set text.</summary>
public sealed class MessageTabsHudTests
{
    private static readonly HudScreen FullHd = new HudScreen(1920, 1080);
    private static readonly HudVisibility Shown = new HudVisibility(ShowNormalHud: true, AllowOrdinaryInput: true);
    private static readonly HudRect Objective = new HudRect(1112, 1460, 88, 128);

    /// <summary>One 2x2 glyph 'A' at full coverage, 3 wide with its spacing, in a 10-pixel line.</summary>
    private static readonly BitmapFont Font = new BitmapFont(10, [new BitmapGlyph('A', 2, 2, 0, 0, 3, [15, 15, 15, 15])]);

    private sealed class Art : IBottomPanelArt
    {
        public string? Icon(BottomPanelChoiceKey key)
        {
            return "icon " + key.Value;
        }

        public string Text(uint id)
        {
            return "text " + id;
        }
    }

    private static MessageTabsHud Hud(MessageTabsView? view = null)
    {
        return new MessageTabsHud(new Art(), view ?? new MessageTabsView(PlayerIds.FirstKeeper), _ => Font, r => "message " + r, line => "AA");
    }

    private static HudDraw[] Build(MessageTabsHud hud, (float, float, bool) pointer = default, HudVisibility? visibility = null)
    {
        var frame = new HudFrame();
        hud.Build(FullHd, 1920, visibility ?? Shown, collapsed: false, default, pointer, frame);
        return frame.Draws.ToArray();
    }

    private static void Click(MessageTabsHud hud, HudRect at, int button = 0)
    {
        Assert.True(hud.PointerDown(at.X + at.Width / 2, at.Y + at.Height / 2, button));
    }

    [Fact]
    public void WithNoMessagesOnlyTheReviewObjectiveButtonDraws()
    {
        HudDraw[] draws = Build(Hud());
        HudDraw button = Assert.Single(draws);
        Assert.Equal("GUI\\Tabs\\Messages\\mt-objective-00", button.Image.Path);
        Assert.Equal(Objective, button.Rect);
    }

    [Fact]
    public void HoveringTheObjectiveButtonLightsItAndNamesIt()
    {
        MessageTabsHud hud = Hud();
        Build(hud);
        hud.PointerMove(Objective.X + 1, Objective.Y + 1);
        Assert.Equal("GUI\\Tabs\\Messages\\mt-objective-01", Build(hud)[0].Image.Path);
        Assert.Equal(new HoverTextSnapshot("text 2164", "text 2163", default), hud.HoverText);
        hud.PointerLeave();
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText);
    }

    [Fact]
    public void TheObjectiveButtonOpensMenu33OverThePlayAreaAndAClickOnItClosesIt()
    {
        MessageTabsHud hud = Hud();
        Build(hud);
        Click(hud, Objective);
        HudDraw[] draws = Build(hud);
        HudDraw pane = Assert.Single(draws, d => d.Kind == HudDrawKind.Fill);
        Assert.Equal(new HudRect(0, 180, FullHd.VirtualWidth, 1280), pane.Rect);
        Assert.Equal(new HudColor(20, 20, 20, 153), pane.Tint);
        HudDraw text = Assert.Single(draws, d => d.Image.Text != null);
        Assert.StartsWith("text 1408\nAA\n\ntext 1409", text.Image.Text!.Text);
        Assert.Equal(180 + 250, text.Rect.Y);
        // Menu33 claims the whole screen for the world's purposes while it shows.
        Assert.True(hud.ContainsPointer(10, 10));

        Click(hud, new HudRect(500, 500, 10, 10));
        Assert.DoesNotContain(Build(hud), d => d.Kind == HudDrawKind.Fill);
    }

    [Fact]
    public void ButtonsIgnoreClicksWhileOrdinaryInputIsBlocked()
    {
        MessageTabsHud hud = Hud();
        Build(hud, visibility: new HudVisibility(ShowNormalHud: true, AllowOrdinaryInput: false));
        Assert.False(hud.PointerDown(Objective.X + 1, Objective.Y + 1, 0));
        Assert.False(hud.ContainsPointer(Objective.X + 1, Objective.Y + 1));
    }

    [Fact]
    public void AHiddenPanelDrawsNothingAndForgetsItsHover()
    {
        MessageTabsHud hud = Hud();
        Build(hud);
        hud.PointerMove(Objective.X + 1, Objective.Y + 1);
        Assert.Empty(Build(hud, visibility: new HudVisibility(false, false)));
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText);
    }

    [Fact]
    public void AClippedQuadCarriesItsWindowAndTheCutKeepsOnlyTheVisiblePartOfTheImage()
    {
        var frame = new HudFrame();
        var rect = new HudRect(0, -50, 100, 200);
        HudQuads.Clipped(frame, "img", rect, new HudRect(0, 0, 50, 100), HudColor.White);
        HudDraw clipped = Assert.Single(frame.Draws.ToArray());
        Assert.Equal(rect, clipped.Rect);
        Assert.Equal(new HudRect(0, 0, 50, 100), clipped.Clip);

        Assert.True(HudQuads.Cut(rect, new HudRect(0, 0, 1, 1), new HudRect(0, 0, 50, 100), out HudRect drawn, out HudRect uv));
        Assert.Equal(new HudRect(0, 0, 50, 100), drawn);
        Assert.Equal(new HudRect(0, .25f, .5f, .5f), uv);

        frame.Clear();
        HudQuads.Clipped(frame, "img", new HudRect(200, 200, 10, 10), new HudRect(0, 0, 50, 50), HudColor.White);
        Assert.Equal(0, frame.Count);
    }

    [Fact]
    public void TheOptionsScreensObjectiveShowsMenu33WithoutClaimingThePointer()
    {
        MessageTabsHud hud = Hud();
        var frame = new HudFrame();
        hud.Build(FullHd, 1920, Shown, collapsed: false, default, default, frame, optionsOpen: true, optionsObjective: true);
        Assert.Contains(frame.Draws.ToArray(), d => d.Kind == HudDrawKind.Fill);
        Assert.False(hud.ContainsPointer(500, 500));
    }

    [Fact]
    public void AStillPointerLosesItsHoverWhenTheButtonMovesAwayUnderIt()
    {
        MessageTabsHud hud = Hud();
        Build(hud);
        hud.PointerMove(Objective.X + 1, Objective.Y + 1);
        Assert.Equal("text 2163", hud.HoverText.TooltipName);
        // Collapsing the strip moves the button down; a re-pick at the still pointer finds nothing there.
        var frame = new HudFrame();
        hud.Build(FullHd, 1920, Shown, collapsed: true, default, default, frame);
        hud.Repick();
        Assert.Equal(HoverTextSnapshot.Empty, hud.HoverText);
    }

    [Fact]
    public void TilingRepeatsFromTheCornerAndCropsTheLastRowAndColumn()
    {
        var frame = new HudFrame();
        HudQuads.Tiled(frame, "tile", new HudRect(10, 20, 50, 30), 20, HudColor.White);
        HudDraw[] tiles = frame.Draws.ToArray();
        Assert.Equal(6, tiles.Length);
        Assert.Equal(new HudRect(10, 20, 20, 20), tiles[0].Rect);
        Assert.Equal(new HudRect(0, 0, 1, 1), tiles[0].Uv);
        Assert.Equal(new HudRect(50, 40, 10, 10), tiles[^1].Rect);
        Assert.Equal(new HudRect(0, 0, .5f, .5f), tiles[^1].Uv);
    }

    [Fact]
    public void SetTextIsWhiteWithTheGlyphCoverageAsAlpha()
    {
        byte[] pixels = HudTextPixels.Render(new HudText(Font, "AA", 100), out int width, out int height);
        Assert.Equal((5, 10), (width, height));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels[0..4]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, pixels[8..12]);   // the gap between the two letters
        Assert.Equal((5, 10), HudTextPixels.Measure(new HudText(Font, "AA", 100)));
    }

    [InstallFact]
    public void AnOpenedMessageShowsItsFramedTextAndItsExitButtonDismissesIt()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        world.Messages.Post(world, PlayerIds.FirstKeeper, PlayerMessageKind.HeartAttacked, 0, 0);
        var view = new MessageTabsView(PlayerIds.FirstKeeper);
        view.Capture(world, 1000); view.Capture(world, 1650);
        MessageTabsHud hud = Hud(view);
        HudDraw[] closed = Build(hud);
        HudDraw tab = closed[1];
        Assert.StartsWith("GUI\\Tabs\\Messages\\", tab.Image.Path);

        Click(hud, tab.Rect);
        HudDraw[] open = Build(hud);
        Assert.Equal(9, open.Select(d => d.Image.Path).Where(p => p?.StartsWith("GUI\\Windows\\text_box-") == true).Distinct().Count());
        HudDraw text = Assert.Single(open, d => d.Image.Text != null);
        Assert.Equal("message " + view.Reference(view.Open!.Value), text.Image.Text!.Text);
        HudDraw exit = Assert.Single(open, d => d.Image.Path == "GUI\\Tabs\\Messages\\mt-exit-00");

        Click(hud, exit.Rect);
        Assert.Null(view.Open);
    }
}
