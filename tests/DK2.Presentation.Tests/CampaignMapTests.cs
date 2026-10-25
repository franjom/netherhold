// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class CampaignMapTests
{
    private static FrontEndSession Session()
    {
        var session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000) });
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.FromSeconds(1));
        return session;
    }

    [Fact]
    public void PickingCannotBypassLocksAndTheStationFinishesBeforeBriefing()
    {
        var session = Session();
        var map = new CampaignMapController(session, new CampaignProfile(), false,
            slot => new CampaignBriefing(slot, "A realm", "A description", "Keep the Heart alive.", Array.Empty<string>()));
        int saves = 0; map.ProfileChanged += _ => saves++;
        Assert.True(map.Continue());
        Assert.False(map.Select(new CampaignSlot(1)));
        session.Update(TimeSpan.FromSeconds(2));
        Assert.Equal(FrontEndPage.CampaignMap, session.Page);
        Assert.False(map.Select(new CampaignSlot(2)));
        Assert.False(map.Select(new CampaignSlot(25)));
        Assert.True(map.Select(new CampaignSlot(1)));
        Assert.Equal(FrontEndPath.TableToBriefing, session.Path);
        Assert.Null(map.Briefing);
        session.Update(TimeSpan.FromSeconds(3));
        Assert.Equal(FrontEndPage.Briefing, session.Page);
        Assert.Equal(new CampaignSlot(1), map.Briefing!.Slot);
        Assert.Equal(string.Concat((char)34, "A realm", (char)34, " - A description"), map.Briefing.Heading);
        Assert.Equal(1, map.Profile.LevelNumber);
        Assert.Equal(0u, map.Profile.SecretLevelNumber);
        Assert.True(saves > 0);
        Assert.False(map.CanLaunch);
        CampaignSlot launched = CampaignSlot.None;
        map.LaunchRequested += slot => launched = slot;
        Assert.True(map.Launch()); Assert.Equal(new CampaignSlot(1), launched);
        Assert.True(map.BackToMap()); session.Update(TimeSpan.FromSeconds(4));
        Assert.Equal(FrontEndPage.CampaignMap, session.Page);
        Assert.True(map.BackToMenu()); session.Update(TimeSpan.FromSeconds(5));
        Assert.Equal(FrontEndPage.SinglePlayer, session.Page);
    }

    [Fact]
    public void SecretFiveUsesTheStartupGateAndClosedBranchCannotBeHovered()
    {
        var session = Session();
        var p = CampaignProgression.CompleteMap(new CampaignProfile(), new CampaignSlot(6));
        var map = new CampaignMapController(session, p, false,
            slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()));
        map.Continue(); session.Update(TimeSpan.FromSeconds(2));
        map.Hover(new CampaignSlot(7)); Assert.Equal(CampaignSlot.None, map.Hovered);
        map.Hover(new CampaignSlot(6)); Assert.Equal(new CampaignSlot(6), map.Hovered);
        Assert.Equal(0xfff, map.MarkerVisual(new CampaignSlot(6)).TexturePhase.Raw);
        Assert.Equal(0x55a, map.LandVisual(new CampaignSlot(6)).TexturePhase.Raw);
        map.Hover(CampaignSlot.None);
        Assert.Equal(0x55a, map.LandVisual(new CampaignSlot(6)).TexturePhase.Raw);
        Assert.False(map.LandVisual(new CampaignSlot(30)).Visible);
        Assert.False(map.Select(new CampaignSlot(30)));
    }
    [Fact]
    public void MenuResetConfirmationCancelsWithoutChangingProgressAndYesStartsANewMap()
    {
        var session = Session();
        var profile = CampaignProgression.CompleteMap(
            CampaignProgression.CompleteMap(new CampaignProfile(), new CampaignSlot(1)), new CampaignSlot(5));
        using var map = new CampaignMapController(session, profile, false,
            slot => new CampaignBriefing(slot, "", "", "", Array.Empty<string>()));
        using var menu = new FrontEndMenuController(session, _ => new(0, 0, 10, 10), map);
        menu.Activate(new(1));
        Assert.Equal(FrontEndPage.SinglePlayer, session.Page);
        Assert.True(menu.IsEnabled(menu.Widgets.First(w => w.Id.Value == 567)));
        menu.Activate(new(6));
        Assert.Equal(FrontEndPage.Dialog, session.Page);
        Assert.Equal(140u, menu.Dialog!.TitleTextId);
        menu.Activate(new(668));
        Assert.Equal(FrontEndPage.SinglePlayer, session.Page);
        Assert.Equal(CampaignLevelStatus.Won, map.Profile.PlayerLevelStatus[4]);
        menu.Activate(new(6)); menu.Activate(new(667));
        session.Update(TimeSpan.FromSeconds(2));
        Assert.Equal(FrontEndPage.CampaignMap, session.Page);
        Assert.Equal(CampaignLevelStatus.Open, map.Profile.PlayerLevelStatus[0]);
        Assert.Equal(CampaignLevelStatus.Locked, map.Profile.PlayerLevelStatus[4]);
        Assert.True(menu.Activate(new(534)));
    }

    [Fact]
    public void OpenMarkersBlinkAfterTheRouteIntroAndHoldOtherMarkersWhileHovering()
    {
        var session = Session();
        var states = new CampaignLevelStatus[40]; states[0] = states[1] = CampaignLevelStatus.Open;
        using var map = new CampaignMapController(session, new CampaignProfile { PlayerLevelStatus = states }, false,
            slot => new(slot, "", "", "", Array.Empty<string>()));
        map.Continue(); session.Update(TimeSpan.FromSeconds(2));
        session.Update(TimeSpan.FromSeconds(2.201)); session.Update(TimeSpan.FromSeconds(2.202));
        Assert.Equal(0xfff, map.MarkerVisual(new CampaignSlot(2)).TexturePhase.Raw);
        map.Hover(new CampaignSlot(1));
        session.Update(TimeSpan.FromSeconds(2.401)); session.Update(TimeSpan.FromSeconds(2.402));
        Assert.Equal(0xfff, map.MarkerVisual(new CampaignSlot(2)).TexturePhase.Raw);
        Assert.True(map.LandVisual(new CampaignSlot(1)).Lift > 0);
        map.Hover(CampaignSlot.None);
        session.Update(TimeSpan.FromSeconds(2.403));
        Assert.Equal(0, map.MarkerVisual(new CampaignSlot(2)).TexturePhase.Raw);
    }

    [Fact]
    public void HoverRiseUsesTheUnclampedCreationHeightAndStopsSeventyTwoUnitsAboveFloor()
    {
        var session = Session();
        using var map = new CampaignMapController(session, new CampaignProfile(), false,
            slot => new(slot, "", "", "", Array.Empty<string>()));
        map.Continue(); session.Update(TimeSpan.FromSeconds(2)); map.Hover(new CampaignSlot(1));
        for (int i = 1; i <= 20; i++) session.Update(TimeSpan.FromSeconds(2 + i * .05));
        Assert.Equal(72, map.LandVisual(new CampaignSlot(1)).Lift);
        map.Hover(CampaignSlot.None); session.Update(TimeSpan.FromSeconds(5.5));
        Assert.Equal(0, map.LandVisual(new CampaignSlot(1)).Lift);
    }

    [Fact]
    public void LeavingTheMapKeepsReturningTheRaisedLandDuringTheCameraFlightAndBriefing()
    {
        var session = Session();
        using var map = new CampaignMapController(session, new CampaignProfile(), false,
            slot => new(slot, "", "", "", Array.Empty<string>()));
        map.Continue(); session.Update(TimeSpan.FromSeconds(2)); map.Hover(new CampaignSlot(1));
        for (int i = 1; i <= 20; i++) session.Update(TimeSpan.FromSeconds(2 + i * .05));
        Assert.Equal(72, map.LandVisual(new CampaignSlot(1)).Lift);
        map.Select(new CampaignSlot(1)); session.Update(TimeSpan.FromSeconds(3.5));
        Assert.InRange(map.LandVisual(new CampaignSlot(1)).Lift, 1, 71);
        session.Update(TimeSpan.FromSeconds(5.5));
        Assert.Equal(FrontEndPage.Briefing, session.Page);
        Assert.Equal(0, map.LandVisual(new CampaignSlot(1)).Lift);
    }

}
