// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Data.Save;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free options menu and named-save browser (ADR-0805).</summary>
public sealed class OptionsHudTests
{
    private static readonly HudScreen FullHd = new(1920, 1080);

    /// <summary>One 2x2 glyph 'A' at full coverage, 3 wide with its spacing, in a 10-pixel line.</summary>
    private static readonly BitmapFont Font = new(10, [new BitmapGlyph('A', 2, 2, 0, 0, 3, [15, 15, 15, 15])]);

    private static OptionsHud Options()
    {
        var hud = new OptionsHud(id => "text " + id, _ => Font);
        hud.SetVisible(true);
        return hud;
    }

    private static HudDraw[] Build(OptionsHud hud, (float, float) pointer = default)
    {
        var frame = new HudFrame();
        hud.Build(FullHd, pointer, frame);
        return frame.Draws.ToArray();
    }

    [Fact]
    public void TheRootPageShowsItsTitleAndEntriesAtTheirPlacesWithUnsupportedOnesGreyed()
    {
        HudDraw[] draws = Build(Options());
        Assert.Equal(8, draws.Length);
        Assert.Equal("text 94", draws[0].Image.Text!.Text);
        Assert.Equal((20f, 180f), (draws[0].Rect.X, draws[0].Rect.Y));
        Assert.Equal(HudColor.White, draws[0].Tint);
        Assert.Equal((20f, 340f), (draws[1].Rect.X, draws[1].Rect.Y));
        // A label is its text's own size: "AA" is 5 x 10 font pixels, at 1080's scale.
        var sized = new OptionsHud(_ => "AA", _ => Font);
        sized.SetVisible(true);
        Assert.Equal(new HudRect(20, 340, 5 / FullHd.Scale, 10 / FullHd.Scale), Build(sized)[1].Rect);
        Assert.Equal(HudColor.Grey(200), draws[1].Tint);   // Level objective
        Assert.Equal(HudColor.Grey(102), draws[2].Tint);   // Game options: not supported until the runner says so
    }

    [Fact]
    public void TheEntryUnderThePointerIsLitAndIsTheActionThere()
    {
        OptionsHud hud = Options();
        // Level objective: 8,136 and 496 wide in 1024x768 units, 38.4 high.
        Assert.Equal(ClassicOptionsAction.LevelObjective, hud.ActionAt(20 + 1239, 340 + 95));
        Assert.Equal(ClassicOptionsAction.None, hud.ActionAt(20 + 1240, 340));
        Assert.Equal(ClassicOptionsAction.None, hud.ActionAt(20, 200 * 2.5f));   // Game options, unsupported
        hud.SetSupported(ClassicOptionsAction.GameOptions, true);
        Assert.Equal(ClassicOptionsAction.GameOptions, hud.ActionAt(20, 200 * 2.5f));
        Assert.Equal(HudColor.White, Build(hud, (30, 350))[1].Tint);
    }

    [Fact]
    public void TheObjectivePageHasNoTitleAndAConfirmationTakesItsOwn()
    {
        OptionsHud hud = Options();
        hud.SetPage(ClassicOptionsPage.Objective, ClassicSessionAction.None);
        Assert.Equal(["text 142", "text 20"], Build(hud).Select(d => d.Image.Text!.Text));
        hud.SetPage(ClassicOptionsPage.Confirmation, ClassicSessionAction.Restart);
        HudDraw[] draws = Build(hud);
        Assert.Equal("text 1269", draws[0].Image.Text!.Text);
        Assert.Equal(HudColor.Grey(200), draws[1].Tint);   // the question itself takes no press
        hud.SetVisible(false);
        Assert.Empty(Build(hud));
        Assert.Equal(ClassicOptionsAction.None, hud.ActionAt(20, 900));
    }

    private static SaveSlotsView Saves(int count)
    {
        return new("saves", () => Enumerable.Range(0, count)
        .Select(i => new SavedSessionEntry($"save{i}.dk2save", null,
            new SaveIncompatibleException(SaveIncompatibleReason.Corrupt, "broken " + i))).ToArray());
    }

    private static HudDraw[] Build(SaveLoadHud hud)
    {
        var frame = new HudFrame();
        hud.Build(FullHd, frame);
        return frame.Draws.ToArray();
    }

    [Fact]
    public void LoadingListsSevenSavesUnderItsTitleAndTheWheelScrollsTheRest()
    {
        var hud = new SaveLoadHud(id => "text " + id, _ => Font);
        hud.Show(Saves(9), visible: true, save: false);
        HudDraw[] draws = Build(hud);
        Assert.Equal("text 143", draws[0].Image.Text!.Text);
        Assert.Equal("save0   (cannot be loaded)", draws[1].Image.Text!.Text);
        Assert.Equal(new HudRect(20, 280, draws[1].Rect.Width, draws[1].Rect.Height), draws[1].Rect);
        Assert.Equal(HudColor.Grey(120), draws[1].Tint);
        Assert.Equal(new HudRect(20, 280 + 6 * 80, draws[7].Rect.Width, draws[7].Rect.Height), draws[7].Rect);
        // No save selected: no Load entry, only Continue and Back.
        Assert.Equal(["text 142", "text 20"], draws[8..].Select(d => d.Image.Text!.Text));

        hud.Wheel(-5);
        Assert.Equal("save2   (cannot be loaded)", Build(hud)[1].Image.Text!.Text);
    }

    [Fact]
    public void PressingAnUnreadableSaveSelectsItAndSaysWhy()
    {
        var hud = new SaveLoadHud(id => "text " + id, _ => Font);
        SaveSlotsView view = Saves(2);
        hud.Show(view, visible: true, save: false);
        HudRect second = Build(hud)[2].Rect;
        Assert.True(hud.PointerDown(second.X + 1, second.Y + 1, 0));
        Assert.Equal(1, view.SelectedIndex);
        HudDraw[] draws = Build(hud);
        Assert.Equal(HudColor.White, draws[2].Tint);
        Assert.Equal("broken 1", draws[^1].Image.Text!.Text);
        Assert.Equal(new HudRect(20, 1200, draws[^1].Rect.Width, draws[^1].Rect.Height), draws[^1].Rect);
        Assert.False(hud.Hits(1, 1));
    }

    [Fact]
    public void SavingTakesTypedCharactersAndEnterOrAPressOnTheNameSubmits()
    {
        var hud = new SaveLoadHud(id => "text " + id, _ => Font);
        SaveSlotsView view = Saves(0);
        int submitted = 0, back = 0;
        hud.Submit += () => submitted++;
        hud.HudAction += action => back += action == ClassicHudAction.BackToOptions ? 1 : 100;
        hud.Show(view, visible: true, save: true);
        hud.Type("AB\bC");
        Assert.Equal("AC", view.Name);
        hud.Type("\r");
        Assert.Equal(1, submitted);
        HudDraw[] draws = Build(hud);
        HudDraw name = Assert.Single(draws, d => d.Image.Text!.Text == "AC_");
        Assert.Equal(1100, name.Rect.Y);
        Assert.True(hud.PointerDown(name.Rect.X + 1, name.Rect.Y + 1, 0));
        Assert.Equal(2, submitted);
        HudDraw backEntry = Assert.Single(draws, d => d.Image.Text!.Text == "text 20");
        Assert.Equal(new HudRect(1280, 1280, backEntry.Rect.Width, backEntry.Rect.Height), backEntry.Rect);
        Assert.True(hud.PointerDown(backEntry.Rect.X + 1, backEntry.Rect.Y + 1, 0));
        Assert.Equal(1, back);
        Assert.False(hud.PointerDown(backEntry.Rect.X + 1, backEntry.Rect.Y + 1, 1));
    }

    [Fact]
    public void TypingReadsOnlyTheGivenPartOfAReusedBuffer()
    {
        var hud = new SaveLoadHud(id => "text " + id, _ => Font);
        SaveSlotsView view = Saves(0);
        int submitted = 0;
        hud.Submit += () => submitted++;
        hud.Show(view, visible: true, save: true);
        char[] buffer = ['A', 'B', '\b', 'C', '\n', 'Z'];
        hud.Type(buffer.AsSpan(0, 4));
        Assert.Equal("AC", view.Name);
        Assert.Equal(0, submitted);
        hud.Type(buffer.AsSpan(4, 1));
        Assert.Equal(1, submitted);
        Assert.Equal("AC", view.Name);
    }

    [Theory]
    [InlineData(ClassicOptionsAction.Save, true)]
    [InlineData(ClassicOptionsAction.Load, true)]
    [InlineData(ClassicOptionsAction.GameOptions, false)]
    [InlineData(ClassicOptionsAction.ContinueGame, false)]
    [InlineData(ClassicOptionsAction.Back, false)]
    [InlineData(ClassicOptionsAction.None, false)]
    public void OnlySaveAndLoadRereadTheSaveList(ClassicOptionsAction entry, bool expected)
    {
        Assert.Equal(expected, OptionsHud.RefreshesSaves(entry));
    }

    [Fact]
    public void TheBackAndContinueLinesRaiseTheMenusBackAndContinueGameActions()
    {
        var hud = new SaveLoadHud(id => "text " + id, _ => Font);
        var raised = new List<ClassicHudAction>();
        hud.HudAction += raised.Add;
        hud.Show(Saves(1), visible: true, save: false);
        HudDraw[] draws = Build(hud);
        HudDraw back = Assert.Single(draws, d => d.Image.Text!.Text == "text 20");
        HudDraw continueGame = Assert.Single(draws, d => d.Image.Text!.Text == "text 142");
        Assert.True(hud.PointerDown(back.Rect.X + 1, back.Rect.Y + 1, 0));
        Assert.True(hud.PointerDown(continueGame.Rect.X + 1, continueGame.Rect.Y + 1, 0));
        Assert.Equal(new[] { ClassicHudAction.BackToOptions, ClassicHudAction.ToggleOptions }, raised);
        Assert.Equal(new[] { OptionsHud.HudAction(ClassicOptionsAction.Back), OptionsHud.HudAction(ClassicOptionsAction.ContinueGame) }, raised);
    }

    [Fact]
    public void TheFrontEndWheelStopsAtItsElevenLineLastPage()
    {
        var hud = new SaveLoadHud(id => "text " + id, _ => Font);
        hud.Show(Saves(9), visible: true, save: false, frontEnd: true);
        hud.Wheel(-1);
        Assert.Equal("save0   (cannot be loaded)", Build(hud)[0].Image.Text!.Text);
        hud.Show(Saves(15), visible: true, save: false, frontEnd: true);
        hud.Wheel(-10);
        HudDraw[] draws = Build(hud);
        Assert.Equal(11, draws.Length);
        Assert.Equal("save4   (cannot be loaded)", draws[0].Image.Text!.Text);
        hud.Wheel(10);
        Assert.Equal("save0   (cannot be loaded)", Build(hud)[0].Image.Text!.Text);
    }

    [Fact]
    public void FrontEndSaveDateAndTimeUseTheirOwnColumnsAndSelectTheSameSave()
    {
        var header = new SavedSessionHeader("Level1", (byte)GameSessionMode.Campaign, 0,
            new DateTime(2026, 1, 17, 8, 45, 0, DateTimeKind.Utc), "Mine");
        var view = new SaveSlotsView("saves", () => new[] { new SavedSessionEntry("mine.dk2g", header, null) });
        var hud = new SaveLoadHud(id => "text " + id, _ => Font);
        hud.Show(view, visible: true, save: false, frontEnd: true);
        HudDraw[] draws = Build(hud);
        Assert.Equal(new[] { "Mine", header.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            header.SavedAtUtc.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) }, draws.Select(d => d.Image.Text!.Text));
        Assert.Equal(1630f, draws[1].Rect.X + draws[1].Rect.Width / 2);
        Assert.Equal(2100f, draws[2].Rect.X + draws[2].Rect.Width / 2);
        Assert.True(hud.PointerDown(2100, draws[2].Rect.Y + 1, 0));
        Assert.Equal(0, view.SelectedIndex);
        Assert.All(Build(hud), draw => Assert.Equal(HudColor.White, draw.Tint));
    }

    [Fact]
    public void TheFrontEndListHasNoTitleOrButtonsAndTypingIsIgnored()
    {
        var hud = new SaveLoadHud(id => "text " + id, _ => Font);
        SaveSlotsView view = Saves(1);
        hud.Show(view, visible: true, save: true, frontEnd: true);
        hud.Type("A");
        Assert.Equal("", view.Name);
        HudDraw line = Assert.Single(Build(hud));
        Assert.Equal(new HudRect(280, 472, line.Rect.Width, line.Rect.Height), line.Rect);
        hud.Show(view, visible: false, save: true, frontEnd: true);
        Assert.Empty(Build(hud));
    }

    [Theory]
    [InlineData(ClassicOptionsAction.None, ClassicHudAction.None)]
    [InlineData(ClassicOptionsAction.ContinueGame, ClassicHudAction.ToggleOptions)]
    [InlineData(ClassicOptionsAction.GameOptions, ClassicHudAction.OpenGameOptions)]
    [InlineData(ClassicOptionsAction.LevelObjective, ClassicHudAction.ShowObjective)]
    [InlineData(ClassicOptionsAction.Back, ClassicHudAction.BackToOptions)]
    [InlineData(ClassicOptionsAction.RestartLevel, ClassicHudAction.RestartLevel)]
    [InlineData(ClassicOptionsAction.EndGame, ClassicHudAction.EndGame)]
    [InlineData(ClassicOptionsAction.ReturnToMenu, ClassicHudAction.ReturnToMenu)]
    [InlineData(ClassicOptionsAction.QuitGame, ClassicHudAction.QuitGame)]
    [InlineData(ClassicOptionsAction.Confirm, ClassicHudAction.Confirm)]
    [InlineData(ClassicOptionsAction.Cancel, ClassicHudAction.CancelConfirmation)]
    [InlineData(ClassicOptionsAction.Save, ClassicHudAction.OpenSave)]
    [InlineData(ClassicOptionsAction.Load, ClassicHudAction.OpenLoad)]
    public void AnEntrysPressAppliesItsHudAction(ClassicOptionsAction entry, ClassicHudAction expected)
    {
        Assert.Equal(expected, OptionsHud.HudAction(entry));
    }

    [Fact]
    public void AnEntryMapsToNoHudActionExactlyWhenItsActionIsNone()
    {
        foreach (ClassicOptionsPage page in Enum.GetValues<ClassicOptionsPage>())
        {
            foreach (ClassicOptionsEntry entry in ClassicOptionsLayout.ForPage(page))
            {
                Assert.Equal(entry.Action == ClassicOptionsAction.None, OptionsHud.HudAction(entry.Action) == ClassicHudAction.None);
            }
        }
    }

    [Fact]
    public void OnlyTheRunnersPagesAreUnsupportedByDefaultAndNoneNeverIs()
    {
        var hud = new OptionsHud(_ => "", _ => Font);
        foreach (ClassicOptionsAction action in Enum.GetValues<ClassicOptionsAction>())
        {
            Assert.Equal(action is not (ClassicOptionsAction.None or ClassicOptionsAction.Save or ClassicOptionsAction.Load
                or ClassicOptionsAction.GameOptions), hud.Supported(action));
        }

        hud.SetSupported(ClassicOptionsAction.Save, true);
        Assert.True(hud.Supported(ClassicOptionsAction.Save));
        hud.SetSupported(ClassicOptionsAction.None, true);
        Assert.False(hud.Supported(ClassicOptionsAction.None));
    }
}
