// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public class FrontEndFlowTests
{
    [Fact]
    public void MainPageWaitsForTheIntroPathAndOnlyOpensOnce()
    {
        var flow = new FrontEndFlow();
        Assert.Equal(FrontEndPath.Intro, flow.Update(false).StartPath);
        Assert.Null(flow.Update(true).ShowPage);
        Assert.Equal(FrontEndPage.Main, flow.Update(false).ShowPage);
        Assert.Null(flow.Update(false).ShowPage);
    }

    [Theory]
    [InlineData(FrontEndPage.CampaignMap, FrontEndPath.EntryToTable, FrontEndOperation.CampaignMapFrame)]
    [InlineData(FrontEndPage.Lobby, FrontEndPath.EntryToExtras, FrontEndOperation.InitializeSkirmish)]
    [InlineData(FrontEndPage.Sessions, FrontEndPath.EntryToOptions, FrontEndOperation.InitializeMultiplayer)]
    [InlineData(FrontEndPage.PetDungeon, FrontEndPath.StaticPetDungeon, FrontEndOperation.InitializePetDungeon)]
    public void ReturnRoutingKeepsTheOriginalPathsEvenWhenTheStationNameLooksOdd(
        FrontEndPage returnPage, FrontEndPath expectedPath, FrontEndOperation operation)
    {
        var flow = new FrontEndFlow(returnPage);
        FrontEndStep first = flow.Update(false);
        Assert.Equal(expectedPath, first.StartPath);
        Assert.Equal(returnPage == FrontEndPage.CampaignMap ? FrontEndOperation.EnterCampaignMap : FrontEndOperation.None, first.Operation);
        Assert.Equal(default, flow.Update(true));
        Assert.Equal(operation, flow.Update(false).Operation);
    }

    [Fact]
    public void ReturningToEntryPlaysTheStaticEntryBeforeOpeningTheMainPage()
    {
        var flow = new FrontEndFlow();
        flow.Update(false); flow.Update(false);
        flow.BeginTransition(new FrontEndTransition(FrontEndPath.OptionsToEntry, FrontEndState.ReturnMain));
        Assert.Equal(FrontEndPath.StaticEntry, flow.Update(false).StartPath);
        Assert.Equal(FrontEndPage.Main, flow.Update(false).ShowPage);
    }

    [Fact]
    public void SessionUsesWallTimeAndKeepsTheFinalCameraPoseAtTheStation()
    {
        var session = new FrontEndSession(path => path == FrontEndPath.Intro ? Frames(1) : Frames(60));
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.Zero);
        Assert.True(session.TryTransition(new FrontEndTransition(FrontEndPath.EntryToOptions, FrontEndState.OptionsReady)));
        Assert.Equal(FrontEndPage.None, session.Page);
        Assert.False(session.TryTransition(new FrontEndTransition(FrontEndPath.EntryToTable, FrontEndState.CampaignMap)));
        session.Update(TimeSpan.FromSeconds(1));
        Assert.True(session.PathPlaying);
        Assert.Equal(FrontEndPage.None, session.Page);
        session.Update(TimeSpan.FromSeconds(2));
        Assert.False(session.PathPlaying);
        Assert.Equal(FrontEndPage.Options, session.Page);
        CameraPathPose last = session.Pose!.Value;
        session.Update(TimeSpan.FromHours(1));
        Assert.Equal(last, session.Pose);
        Assert.Equal(3.5f + 59, last.Position.X);
        Assert.Equal(-12.5f, last.Position.Z);
    }

    [Fact]
    public void APathThatCannotLoadDoesNotHideThePageOrChangeTheCamera()
    {
        var session = new FrontEndSession(path => path == FrontEndPath.Intro ? Frames(1) : null);
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.Zero);
        var pose = session.Pose;
        Assert.False(session.TryTransition(new FrontEndTransition(FrontEndPath.EntryToTable, FrontEndState.CampaignMap)));
        Assert.Equal(FrontEndPage.Main, session.Page);
        Assert.Equal(pose, session.Pose);
    }

    [Theory]
    [InlineData("../Level1")]
    [InlineData("Data/Level1")]
    [InlineData("")]
    public void RuntimeLevelRequestsRejectPathsInsteadOfLevelNames(string name) =>
        Assert.Throws<ArgumentException>(() => GameSessionRequest.Campaign(name));

    private static IReadOnlyList<KcsFrame> Frames(int count) => Enumerable.Range(0, count)
        .Select(i => new KcsFrame(i, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000)).ToArray();
}
