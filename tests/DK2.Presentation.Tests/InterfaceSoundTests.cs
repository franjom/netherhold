// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>Phase W8: the interface sounds the world-sound scan found unwired (docs/research/sound-effects-interface.md).</summary>
public sealed class InterfaceSoundTests
{
    private static (SoundCategory Category, uint Id) Only(SoundRequests sounds)
    {
        SoundRequest request = Assert.Single(sounds.Pending);
        return (request.Category, request.Id.Value);
    }

    [Theory]
    [InlineData(141, "options_speech", 0x345u)]
    [InlineData(143, "options_music", 0x346u)]
    [InlineData(144, "object_fe_parchment", 0x31Du)]
    public void PressingAnAudioSliderStartsItsTestSoundOnThatSlidersCategoryInTheFrontEnd(ushort id, string category, uint sound)
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound);
        Assert.True(options.BeginSliderDrag(id));
        Assert.Equal((new SoundCategory(category), sound), Only(options.Sounds));
        Assert.NotEqual(SoundVoiceId.None, options.Sounds.Pending[0].Voice);
    }

    [Theory]
    [InlineData(139)]
    [InlineData(126)]
    [InlineData(154)]
    [InlineData(156)]
    [InlineData(158)]
    public void EveryOtherFrontEndSliderDragsWithTheParchmentSound(ushort id)
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound);
        Assert.True(options.BeginSliderDrag(id));
        Assert.Equal((new SoundCategory("object_fe_parchment"), 0x31Du), Only(options.Sounds));
    }

    [Fact]
    public void ReleasingTheSliderStopsTheVoiceItStartedAndNothingElseDoes()
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound);
        options.EndSliderDrag();
        Assert.Empty(options.Sounds.Pending);
        options.BeginSliderDrag(141);
        SoundVoiceId voice = options.Sounds.Pending[0].Voice;
        options.EndSliderDrag();
        Assert.Equal(2, options.Sounds.Pending.Count);
        Assert.True(options.Sounds.Pending[1].Stop);
        Assert.Equal(voice, options.Sounds.Pending[1].Voice);
        options.EndSliderDrag();
        Assert.Equal(2, options.Sounds.Pending.Count);
    }

    [Fact]
    public void ADragInProgressStartsNoSecondSound()
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound);
        options.BeginSliderDrag(143);
        options.BeginSliderDrag(143);
        options.BeginSliderDrag(144);
        Assert.Single(options.Sounds.Pending);
    }

    [Theory]
    [InlineData(133)]
    [InlineData(900)]
    [InlineData(676)]
    [InlineData(1)]
    public void CyclingButtonsOurOwnScaleSliderAndToggleSwitchesAreNotDraggedSliders(ushort id)
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound);
        Assert.False(options.BeginSliderDrag(id));
        Assert.Empty(options.Sounds.Pending);
    }

    [Theory]
    [InlineData(141, "options_speech", 0x345u)]
    [InlineData(143, "options_music", 0x346u)]
    [InlineData(144, "object_fe_parchment", 0x31Du)]
    public void InGameOnlyTheSpeechMusicAndEffectsSlidersSoundWithTheSameTests(ushort id, string category, uint sound)
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound, OptionsApplicationMode.InGame);
        Assert.True(options.BeginSliderDrag(id));
        Assert.Equal((new SoundCategory(category), sound), Only(options.Sounds));
    }

    [Theory]
    [InlineData(139)]
    [InlineData(126)]
    [InlineData(154)]
    [InlineData(156)]
    [InlineData(158)]
    public void InGameTheMasterVolumeGammaAndControlSlidersAreSilent(ushort id)
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound, OptionsApplicationMode.InGame);
        Assert.True(options.BeginSliderDrag(id));
        Assert.Empty(options.Sounds.Pending);
    }

    [Fact]
    public void LeavingThePageEndsADragInProgress()
    {
        var options = new OptionsPagesController(new GameSettings());
        options.Enter(FrontEndPage.Sound);
        options.BeginSliderDrag(143);
        options.Accept();
        Assert.True(options.Sounds.Pending[^1].Stop);
    }

    // Tab flash and slot flash share one latch (interface +0x4F0D), written by every flashing draw: 0x00412579..0x004125C3, 0x00413A70..0x00413AA8.
    [Fact]
    public void TheChimeSoundsOnTheDarkToBrightEdgeOnlyAndAFirstBrightDrawIsSilent()
    {
        var chime = new FlashChime();
        Assert.False(chime.Draw(dark: false, paused: false));
        Assert.False(chime.Draw(dark: true, paused: false));
        Assert.True(chime.Draw(dark: false, paused: false));
        Assert.False(chime.Draw(dark: false, paused: false));
        Assert.False(chime.Draw(dark: true, paused: false));
        Assert.True(chime.Draw(dark: false, paused: false));
    }

    [Fact]
    public void TwoFlashingWidgetsDrawnInTheSameBrightFrameChimeOnce()
    {
        var chime = new FlashChime();
        chime.Draw(dark: true, paused: false);
        Assert.True(chime.Draw(dark: false, paused: false));
        Assert.False(chime.Draw(dark: false, paused: false));
    }

    [Fact]
    public void ThePausedGameChimesNotAndTheLatchStillFollowsThePhase()
    {
        var chime = new FlashChime();
        chime.Draw(dark: true, paused: false);
        Assert.False(chime.Draw(dark: false, paused: true));
        Assert.False(chime.Draw(dark: false, paused: false));
    }

    [Theory]
    [InlineData(1, "gui_button_tab_rooms")]
    [InlineData(2, "gui_button_tab_spells")]
    [InlineData(3, "gui_button_tab_workshop")]
    [InlineData(4, "gui_button_tab_creature")]
    [InlineData(5, "gui_button_icon")]
    [InlineData(6, "gui_button_icon_large")]
    public void TheChimeIsAskedInTheCategoryOfTheWidgetsSoundSlot(int slot, string category)
    {
        SoundRequest request = FlashChime.Request(slot);
        Assert.Equal(new SoundCategory(category), request.Category);
        Assert.Equal(0x33Au, request.Id.Value);
        Assert.Null(request.World);
        Assert.Equal(SoundVoiceId.None, request.Voice);
    }

    [Theory]
    [InlineData(5, "gui_button_icon")]
    [InlineData(6, "gui_button_icon_large")]
    public void ACategoryChoicePressUsesTheSoundSlotItsGridGaveTheWidget(int widgetBank, string category)
    {
        Assert.Equal(new SoundCategory(category), BottomPanelGrid.SoundCategoryOf(widgetBank));
        Assert.Equal(widgetBank, new BottomPanelGrid(widgetBank == 6 ? BottomPanelGrid.LargeCell : BottomPanelGrid.SmallCell, 1, 1).WidgetBank);
    }

    [Theory]
    [InlineData(100, 66)]
    [InlineData(99, 66)]
    [InlineData(50, 33)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void SpeechTakesTwoThirdsOfTheMusicAndEffectsSettingsInWholePercent(int setting, int ducked)
    {
        Assert.Equal(ducked, SpeechDuck.Percent(setting));
    }
}

public sealed class FrontEndClickCategoryTests
{
    // Records of the GUI sound table are 0x30 bytes with the name at +4 of the record whose handle the widget reads, so the main menu's slot 32 is FRONT_END.
    [Fact]
    public void AMainMenuLabelsClickIsAskedInTheFrontEndCategoryAndABackIconsInTheParchments()
    {
        var session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000) });
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromSeconds(1));
        using var menu = new FrontEndMenuController(session, _ => new(0, 0, 10, 10));
        menu.Activate(new(1));
        SoundRequest main = Assert.Single(menu.Sounds.Pending);
        Assert.Equal((SoundCategory.FrontEnd, 0x30Au), (main.Category, main.Id.Value));
        menu.Sounds.Clear();
        Assert.True(session.TryShowPage(FrontEndPage.Options));
        FrontEndWidget icon = menu.Widgets.First(w => w.SoundSlot is >= 33 and <= 36 && menu.IsEnabled(w));
        Assert.True(menu.Activate(icon.Id));
        SoundRequest back = Assert.Single(menu.Sounds.Pending);
        Assert.Equal((SoundCategory.FrontEndClick, 0x30Au + (uint)icon.SoundSlot - 32), (back.Category, back.Id.Value));
    }
}

/// <summary>The campaign map's land sound (<c>0x00538D00</c> asks <c>0x32A</c> in <c>OBJECT_FE_MAP</c>) when a hover starts a land rising.</summary>
public sealed class CampaignMapSoundTests
{
    private static FrontEndSession Session()
    {
        var session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000) });
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromSeconds(1));
        return session;
    }

    private static CampaignMapController AtTheMap(FrontEndSession session)
    {
        var map = new CampaignMapController(session, new CampaignProfile(), false, slot => new(slot, "", "", "", Array.Empty<string>()));
        map.Continue(); session.Update(TimeSpan.FromSeconds(2));
        return map;
    }

    [Fact]
    public void EnteringALandAsksTheMapSoundOnceAndStayingDoesNotAskAgain()
    {
        FrontEndSession session = Session();
        using CampaignMapController map = AtTheMap(session);
        map.Hover(new CampaignSlot(1));
        SoundRequest request = Assert.Single(map.Sounds.Pending);
        Assert.Equal(new SoundCategory("object_fe_map"), request.Category);
        Assert.Equal(0x32Au, request.Id.Value);
        Assert.Null(request.World);
        Assert.Equal(SoundVoiceId.None, request.Voice);
        for (int i = 1; i <= 4; i++) { session.Update(TimeSpan.FromSeconds(2 + i * .05)); map.Hover(new CampaignSlot(1)); }
        Assert.Single(map.Sounds.Pending);
    }

    [Fact]
    public void AnUnselectableLandAsksNothing()
    {
        FrontEndSession session = Session();
        using CampaignMapController map = AtTheMap(session);
        map.Hover(new CampaignSlot(2));
        Assert.Empty(map.Sounds.Pending);
    }

    [Fact]
    public void ALandHoveredAgainAfterItHasLoweredAFrameAsksAgain()
    {
        FrontEndSession session = Session();
        using CampaignMapController map = AtTheMap(session);
        map.Hover(new CampaignSlot(1));
        map.Hover(CampaignSlot.None);
        session.Update(TimeSpan.FromSeconds(2.05));
        map.Hover(new CampaignSlot(1));
        Assert.Equal(2, map.Sounds.Pending.Count);
    }

    // 0x004A35E0 sets the arming word (+0x184) only on an object tick with no hover; with no tick between, the second entry is silent.
    // Whether that tick runs once per frame in the front end is unread (STRONGLY INFERRED), so this pins our choice.
    [Fact]
    public void ALandHoveredAgainWithoutAFrameBetweenStaysSilentBecauseNothingRearmedIt()
    {
        FrontEndSession session = Session();
        using CampaignMapController map = AtTheMap(session);
        map.Hover(new CampaignSlot(1));
        map.Hover(CampaignSlot.None);
        map.Hover(new CampaignSlot(1));
        Assert.Single(map.Sounds.Pending);
    }

    // The height test (z <= creation z + 0x1040) cannot fail while the arming word is set: the update that sets it also lowers the land eight units.
    [Fact]
    public void AFullyRaisedLandThatLoweredOneFrameAndIsEnteredAgainSoundsAtTheLowerHeight()
    {
        FrontEndSession session = Session();
        using CampaignMapController map = AtTheMap(session);
        map.Hover(new CampaignSlot(1));
        for (int i = 1; i <= 20; i++) session.Update(TimeSpan.FromSeconds(2 + i * .05));
        Assert.Equal(72, map.LandVisual(new CampaignSlot(1)).Lift);
        map.Sounds.Clear();
        map.Hover(CampaignSlot.None);
        session.Update(TimeSpan.FromSeconds(3.2));
        Assert.Equal(64, map.LandVisual(new CampaignSlot(1)).Lift);
        map.Hover(new CampaignSlot(1));
        Assert.Single(map.Sounds.Pending);
    }
}
