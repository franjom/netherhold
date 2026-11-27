// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Save;

namespace DK2.Presentation.Tests;

public sealed class GameSessionRequestTests
{
    [Theory]
    [InlineData(FrontEndPage.CampaignResults)]
    [InlineData(FrontEndPage.None)]
    public void RestartPreservesTheCurrentCampaignRequest(FrontEndPage returnPage)
    {
        var current = GameSessionRequest.Campaign("Level11A", returnPage);
        Assert.Equal(current, GameSessionRequest.Restart(current));
    }

    [Fact]
    public void ImportUsesTheSavedLevelAndRestartsItsTerrain()
    {
        var save = new SavedGame(new Dk2SaveHeader(220, 0, Dk2SaveReader.SupportedBuildStamp, "Level1", 15),
            new LevelMap(1, 1, [new MapTile(2, 3, 0, 0)]), [], 1, 0x134);
        var request = GameSessionRequest.FromDk2Save(save);
        var session = SessionContext.Starting(null, request, "Level7", fromMap: true);

        Assert.Equal("Level1", session.LevelName);
        Assert.False(session.LaunchedFromFrontEnd);
        Assert.Same(save.Map, session.ImportedTerrain);
        Assert.Same(save.Map, GameSessionRequest.ForSessionAction(ClassicSessionAction.Restart, session)?.ImportedTerrain);
    }

    [Fact]
    public void RestartRejectsAFrontEndRequest()
    {
        Assert.Throws<ArgumentException>(() => GameSessionRequest.Restart(GameSessionRequest.FrontEnd()));
    }

    [Theory]
    [InlineData(FrontEndPage.CampaignResults)]
    [InlineData(FrontEndPage.None)]
    public void TheOptionsMenusRestartStartsTheSameLevelWithItsReturnPage(FrontEndPage returnPage)
    {
        var context = new SessionContext("Level7", GameSessionMode.Campaign, returnPage);
        Assert.Equal(GameSessionRequest.Campaign("Level7", returnPage),
            GameSessionRequest.ForSessionAction(ClassicSessionAction.Restart, context));
    }

    [Theory]
    [InlineData(FrontEndPage.CampaignResults, FrontEndPage.CampaignMap)]
    [InlineData(FrontEndPage.None, FrontEndPage.None)]
    public void ReturnToMenuOpensTheCampaignMapOnlyForALevelLaunchedFromIt(FrontEndPage returnPage, FrontEndPage opened)
    {
        var context = new SessionContext("Level1", GameSessionMode.Campaign, returnPage);
        Assert.Equal(GameSessionRequest.FrontEnd(opened), GameSessionRequest.ForSessionAction(ClassicSessionAction.ReturnToMenu, context));
    }

    [Theory]
    [InlineData(ClassicSessionAction.Quit)]
    [InlineData(ClassicSessionAction.None)]
    public void QuitAndNoneAskForNoSession(ClassicSessionAction action)
    {
        Assert.Null(GameSessionRequest.ForSessionAction(action, new SessionContext("Level1", GameSessionMode.Campaign, FrontEndPage.None)));
    }
}
