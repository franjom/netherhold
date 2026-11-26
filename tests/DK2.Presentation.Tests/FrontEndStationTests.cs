// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class FrontEndStationTests
{
    [InstallFact]
    public void TheStationHoldsTheSceneryWithMarkersAndPicksEachLandAndMarkerBySlot()
    {
        var level = CampaignLevelData.Load(Dk2InstallGate.Path, GameSessionRequest.FrontEnd().LevelName);
        var station = FrontEndStation.Open(level, new CampaignProfile(), false, PlayerIds.FirstKeeper, 1);

        Assert.Empty(station.Simulation.World.Objects);
        Assert.Equal(54 + 24, station.BaseScenery.Count);
        Assert.Same(station.BaseScenery, station.View.FrontEndScenery);
        Assert.Equal(30 + 24, station.PickSlots.Count);
        FrontEndProp level1 = station.BaseScenery.Single(p => p.MapPiece == FrontEndMapPiece.Level1);
        Assert.Equal(new CampaignSlot(1), station.SlotOf(level1.View.Id));
        FrontEndProp marker = station.BaseScenery.Single(p => p.MarkerSlot == new CampaignSlot(6));
        Assert.Equal(new CampaignSlot(6), station.SlotOf(marker.View.Id));
        FrontEndProp holder = station.BaseScenery.Single(p => p.View.ObjectTypeId == 131);
        Assert.Equal(CampaignSlot.None, station.SlotOf(holder.View.Id));
    }

    [InstallFact]
    public void RefreshProjectsTheMapOncePerRevision()
    {
        var level = CampaignLevelData.Load(Dk2InstallGate.Path, GameSessionRequest.FrontEnd().LevelName);
        CampaignProfile profile = CampaignProgression.CompleteMap(new CampaignProfile(), new CampaignSlot(6));
        var station = FrontEndStation.Open(level, profile, false, PlayerIds.FirstKeeper, 1);
        var session = new FrontEndSession(_ => new[] { new KcsFrame(0, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000) });
        using var map = new CampaignMapController(session, profile, false, slot => new(slot, "", "", "", Array.Empty<string>()));

        Assert.True(station.Refresh(map));
        IReadOnlyList<FrontEndProp> shown = station.View.FrontEndScenery;
        Assert.NotSame(station.BaseScenery, shown);
        Assert.False(station.Refresh(map));
        Assert.Same(shown, station.View.FrontEndScenery);

        session.Update(TimeSpan.Zero); session.Update(TimeSpan.Zero); map.Continue(); session.Update(TimeSpan.FromSeconds(1));
        Assert.True(station.Refresh(map));
        Assert.Equal(0x55a, station.View.FrontEndScenery.Single(s => s.MapPiece == FrontEndMapPiece.Level6a).View.TexturePhase!.Value.Raw);
        Assert.True(station.View.FrontEndScenery.Single(s => s.MarkerSlot == new CampaignSlot(8)).Visible);
    }

    [Fact]
    public void ALoadedSaveOrRestartComesBeforeTheMapsLaunch()
    {
        var restart = GameSessionRequest.Restart(GameSessionRequest.Campaign("Level3", FrontEndPage.None));
        var context = SessionContext.Starting(restart, GameSessionRequest.Campaign("Level7"), "Level9", fromMap: true);
        Assert.Equal(("Level3", GameSessionMode.Campaign, FrontEndPage.None), (context.LevelName, context.Mode, context.ReturnPage));
        Assert.False(context.LaunchedFromFrontEnd);
    }

    [Fact]
    public void TheMapsLaunchStartsALevelThatReturnsToTheResults()
    {
        var context = SessionContext.Starting(null, GameSessionRequest.Campaign("Level7"), "Level9", fromMap: false);
        Assert.Equal(("Level7", GameSessionMode.Campaign, FrontEndPage.CampaignResults), (context.LevelName, context.Mode, context.ReturnPage));
        Assert.True(context.LaunchedFromFrontEnd);
    }

    [Theory]
    [InlineData("Level9", false, "Level9", FrontEndPage.None)]
    [InlineData("Level9", true, "Level9", FrontEndPage.CampaignResults)]
    [InlineData(null, false, "Level1", FrontEndPage.None)]
    public void WithoutARequestTheArgumentsChooseTheLevelAndFromMapTheReturnPage(string? argument, bool fromMap, string level,
        FrontEndPage page)
    {
        var context = SessionContext.Starting(null, null, argument, fromMap);
        Assert.Equal((level, GameSessionMode.Campaign, page), (context.LevelName, context.Mode, context.ReturnPage));
        Assert.Equal(fromMap, context.LaunchedFromFrontEnd);
    }
}
