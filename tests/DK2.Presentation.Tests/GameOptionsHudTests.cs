// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Settings;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free in-game settings pages (ADR-0805).</summary>
public sealed class GameOptionsHudTests
{
    private static readonly HudScreen FullHd = new HudScreen(1920, 1080);

    /// <summary>One 2x2 glyph 'A' at full coverage, 3 wide with its spacing, in a 10-pixel line.</summary>
    private static readonly BitmapFont Font = new BitmapFont(10, [new BitmapGlyph('A', 2, 2, 0, 0, 3, [15, 15, 15, 15])]);

    private static (GameOptionsHud Hud, OptionsPagesController Settings) Open(BitmapFont? font = null)
    {
        var controls = new ClassicHudControls(); controls.Apply(ClassicHudAction.ToggleOptions);
        var settings = new OptionsPagesController(new GameSettings());
        var hud = new GameOptionsHud(new InGameOptionsController(controls, settings), id => "AA", _ => font ?? Font, scan => "K" + scan.Value);
        hud.Show(true);
        return (hud, settings);
    }

    private static HudDraw[] Build(GameOptionsHud hud, (float, float) pointer = default)
    {
        var frame = new HudFrame();
        hud.Build(FullHd, pointer, frame);
        return frame.Draws.ToArray();
    }

    private static HudRect Virtual(FrontEndRect r)
    {
        return new HudRect(r.X * 2.5f, r.Y * 2.5f, r.Width * 2.5f, r.Height * 2.5f);
    }

    private static InGameOptionsWidget Widget(InGameOptionsPage page, ushort setting)
    {
        return ClassicGameOptionsLayout.ForPage(page).Single(w => w.Setting.Value == setting);
    }

    [Fact]
    public void TheRootPageTintsItsTitleItsLinksAndTheUnavailableCamerasAndAPressNavigates()
    {
        (GameOptionsHud hud, _) = Open();
        HudDraw[] draws = Build(hud);
        Assert.Equal(7, draws.Length);
        Assert.Equal(HudColor.White, draws[0].Tint);
        Assert.Equal(new HudRect(20, 180, 5 / FullHd.Scale, 10 / FullHd.Scale), draws[0].Rect);
        Assert.Equal(HudColor.Grey(200), draws[1].Tint);   // Graphics
        Assert.Equal(HudColor.Grey(89), draws[4].Tint);    // User cameras: no page yet
        Assert.False(hud.Hits(draws[4].Rect.X + 1, draws[4].Rect.Y + 1));

        Assert.Equal(HudColor.White, Build(hud, (draws[1].Rect.X + 1, draws[1].Rect.Y + 1))[1].Tint);
        Assert.True(hud.PointerDown(draws[1].Rect.X + 1, draws[1].Rect.Y + 1, 0));
        Assert.Equal(InGameOptionsPage.Graphics, hud.Model.Page);
    }

    [Fact]
    public void ASliderFollowsAPressAndADragAlongItsTrackAndItsArrowsStepByOne()
    {
        (GameOptionsHud hud, OptionsPagesController settings) = Open();
        hud.Model.Navigate(InGameOptionsPage.Graphics);
        Build(hud);
        HudRect track = Virtual(Widget(InGameOptionsPage.Graphics, 126).Bounds);
        (int lo, int hi) = settings.Range(126);
        Assert.True(hud.PointerDown(track.X + 1, track.Y + 1, 0));
        Assert.Equal(lo, settings.Value(126));
        hud.PointerMove(track.X + track.Width, 0);   // held: the pointer may leave the track
        Assert.Equal(hi, settings.Value(126));
        Assert.True(hud.Hits(0, 0));
        hud.PointerUp();
        Assert.False(hud.Hits(0, 0));

        HudDraw[] draws = Build(hud);
        HudDraw thumb = Assert.Single(draws, d => d.Image.Path == "GUI\\Scroll Bars\\h-scroll_bar" && d.Rect.Y == track.Y);
        Assert.Equal(track.X + track.Width - track.Height, thumb.Rect.X, 3);
        Assert.True(hud.PointerDown(track.X - track.Height / 2, track.Y + 1, 0));   // the left arrow
        Assert.Equal(hi - 1, settings.Value(126));
    }

    [Fact]
    public void AValueTogglesItsOnOrOffTextAndSoundQualityCyclesThroughThree()
    {
        var controls = new ClassicHudControls(); controls.Apply(ClassicHudAction.ToggleOptions);
        var settings = new OptionsPagesController(new GameSettings());
        // Each text a different length, so the label's text tells which is shown.
        var hud = new GameOptionsHud(new InGameOptionsController(controls, settings), id => new string('A', (int)(id % 7) + 1), _ => Font, _ => "");
        hud.Show(true);
        hud.Model.Navigate(InGameOptionsPage.Sound);
        HudRect toggle = Virtual(Widget(InGameOptionsPage.Sound, 678).Bounds);
        Assert.True(settings.Supports(678));
        HudDraw before = Assert.Single(Build(hud), d => d.Rect.X == toggle.X && d.Rect.Y == toggle.Y);
        Assert.Equal(HudColor.Grey(200), before.Tint);
        int old = settings.Value(678);
        Assert.True(hud.PointerDown(toggle.X + 1, toggle.Y + 1, 0));
        Assert.Equal(old == 0 ? 1 : 0, settings.Value(678));
        HudDraw after = Assert.Single(Build(hud), d => d.Rect.X == toggle.X && d.Rect.Y == toggle.Y);
        Assert.NotEqual(before.Image.Text!.Text, after.Image.Text!.Text);

        HudRect quality = Virtual(Widget(InGameOptionsPage.Sound, 133).Bounds);
        var seen = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            Build(hud);
            int value = settings.Value(133);
            Assert.True(hud.PointerDown(quality.X + 1, quality.Y + 1, 0));
            Assert.Equal(value % 3 + 1, settings.Value(133));
            seen.Add(settings.Value(133));
        }
        Assert.Equal([1, 2, 3], seen.Order());
    }

    [Fact]
    public void OnlyALeftPressHoldsASliderAndWhileHeldNoLabelLights()
    {
        (GameOptionsHud hud, _) = Open();
        hud.Model.Navigate(InGameOptionsPage.Graphics);
        Build(hud);
        HudRect track = hud.SliderRect(126)!.Value;
        Assert.False(hud.PointerDown(track.X + 1, track.Y + 1, 1));
        Assert.False(hud.Holding);
        Assert.True(hud.PointerDown(track.X + track.Width / 2, track.Y + 1, 0));
        Assert.True(hud.Holding);
        HudRect back = hud.WidgetRect(12)!.Value;
        Assert.DoesNotContain(Build(hud, (back.X + 1, back.Y + 1)), d => d.Rect == back && d.Tint == HudColor.White);
        hud.PointerUp();
        Assert.False(hud.Holding);
        Assert.Contains(Build(hud, (back.X + 1, back.Y + 1)), d => d.Rect == back && d.Tint == HudColor.White);
    }

    [Fact]
    public void TheKeyListClipsItsRowsScrollsWhileAnArrowIsHeldAndARowPressCapturesAKey()
    {
        // 60-pixel lines: more keys than the list shows.
        (GameOptionsHud hud, OptionsPagesController settings) = Open(new BitmapFont(60, Font.Glyphs));
        hud.Model.Navigate(InGameOptionsPage.Controls);
        hud.Model.Navigate(InGameOptionsPage.Keyboard);
        FrontEndRect bounds = ClassicGameOptionsLayout.ForPage(InGameOptionsPage.Keyboard).Single(w => w.Kind == InGameOptionsWidgetKind.KeyboardList).Bounds;
        HudRect list = Virtual(bounds);
        HudDraw[] draws = Build(hud);
        Assert.All(draws.Where(d => d.Clip != null), d =>
            Assert.True(d.Clip!.Value.Y >= list.Y && d.Clip.Value.Y + d.Clip.Value.Height <= list.Y + list.Height + .01f));
        float firstRow = draws.First(d => d.Clip != null).Rect.Y;
        Assert.Equal(list.Y, firstRow);

        HudRect down = Virtual(new FrontEndRect(bounds.X + bounds.Width, bounds.Y + bounds.Height - 25.6f, 25.6f, 25.6f));
        Assert.True(hud.PointerDown(down.X + 1, down.Y + 1, 0));
        Build(hud); Build(hud);
        hud.PointerUp();
        Assert.True(draws.First(d => d.Clip != null).Rect.Y > Build(hud).First(d => d.Clip != null).Rect.Y);

        int captured = 0;
        hud.KeyCaptureStarted += () => captured++;
        HudDraw row = Build(hud).First(d => d.Clip != null && d.Rect.Y >= list.Y && d.Tint == HudColor.Grey(200));
        Assert.True(hud.PointerDown(list.X + 1, row.Rect.Y + 1, 0));
        Assert.Equal(1, captured);
        Assert.NotNull(settings.CapturingAction);
    }

    [Fact]
    public void HiddenItDrawsNothingAndTakesNoPress()
    {
        (GameOptionsHud hud, _) = Open();
        HudRect title = Build(hud)[1].Rect;
        hud.Show(false);
        Assert.Empty(Build(hud));
        Assert.False(hud.Hits(title.X + 1, title.Y + 1));
        Assert.False(hud.PointerDown(title.X + 1, title.Y + 1, 0));
    }

    private static GameBindingInput Pressed(KeyModifiers modifiers, params int[] scans)
    {
        return new GameBindingInput(new GameSettings(), modifiers, _ => false, s => scans.Contains(s.Value));
    }

    [Fact]
    public void ACapturingRowTakesThePressedKeyItAcceptsWithTheHeldModifiers()
    {
        (GameOptionsHud hud, OptionsPagesController settings) = Open();
        Assert.False(hud.CaptureKey(Pressed(KeyModifiers.None, 0x30)));   // nothing is capturing
        hud.Model.Navigate(InGameOptionsPage.Controls);
        hud.Model.Navigate(InGameOptionsPage.Keyboard);
        Assert.True(settings.BeginKeyCapture(GameAction.Heart));
        Assert.False(hud.CaptureKey(Pressed(KeyModifiers.None)));         // no key went down
        Assert.False(hud.CaptureKey(Pressed(KeyModifiers.Shift, 0x2A)));  // a lone modifier waits for its key
        Assert.Equal(GameAction.Heart, settings.CapturingAction);
        Assert.True(hud.CaptureKey(Pressed(KeyModifiers.Shift, 0x2A, 0x30)));
        Assert.Null(settings.CapturingAction);
        Assert.Equal(new GameKeyBinding(GameAction.Heart, 0x30, KeyModifiers.Shift), settings.Pending.Binding(GameAction.Heart));
    }

    /// <summary>Two keys down in one frame: the adapters try them in scan-code order. A (scan 0x1E) comes before Space
    /// (0x39), where Unity's KeyCode order (Space 32, A 97) is the other way round. DK2's own order is not known: its key
    /// capture (0x0042B770) is unread, and plausibly takes the keys in event order.</summary>
    [Fact]
    public void TwoKeysDownInOneFrameAreTriedInScanOrderAsDk2sOrderIsNotKnown()
    {
        (GameOptionsHud hud, OptionsPagesController settings) = Open();
        hud.Model.Navigate(InGameOptionsPage.Controls);
        hud.Model.Navigate(InGameOptionsPage.Keyboard);
        Assert.True(settings.BeginKeyCapture(GameAction.Heart));
        Assert.True(hud.CaptureKey(Pressed(KeyModifiers.None, 0x39, 0x1E)));
        Assert.Equal(new GameKeyBinding(GameAction.Heart, 0x1E), settings.Pending.Binding(GameAction.Heart));
    }

    [Fact]
    public void PagesNeverShownCaptureNoKey()
    {
        var controls = new ClassicHudControls(); controls.Apply(ClassicHudAction.ToggleOptions);
        var settings = new OptionsPagesController(new GameSettings());
        var model = new InGameOptionsController(controls, settings);
        var hud = new GameOptionsHud(model, id => "AA", _ => Font, scan => "K" + scan.Value);
        model.Open();   // the pages' model, without Show(true)
        Assert.True(model.Navigate(InGameOptionsPage.Controls));
        Assert.True(settings.BeginKeyCapture(GameAction.Heart));
        Assert.False(hud.CaptureKey(Pressed(KeyModifiers.None, 0x30)));
        Assert.Equal(GameAction.Heart, settings.CapturingAction);
    }
}
