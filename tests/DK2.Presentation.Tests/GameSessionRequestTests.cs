// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
    public void RestartRejectsAFrontEndRequest()
        => Assert.Throws<ArgumentException>(() => GameSessionRequest.Restart(GameSessionRequest.FrontEnd()));
}
