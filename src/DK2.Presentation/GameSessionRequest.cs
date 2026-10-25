// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

public enum GameSessionMode : byte { Campaign = 1, FrontEnd = 5 }

public sealed record GameSessionRequest
{
    private GameSessionRequest(GameSessionMode mode, string levelName, FrontEndPage returnPage)
        => (Mode, LevelName, ReturnPage) = (mode, levelName, returnPage);

    public GameSessionMode Mode { get; }
    public string LevelName { get; }
    public FrontEndPage ReturnPage { get; }
    public static GameSessionRequest FrontEnd(FrontEndPage returnPage = FrontEndPage.None)
        => new(GameSessionMode.FrontEnd, "FrontEnd3DLevel", returnPage);

    public static GameSessionRequest Campaign(string levelName, FrontEndPage returnPage = FrontEndPage.CampaignMap)
    {
        if (string.IsNullOrEmpty(levelName) || !levelName.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= '0' and <= '9' or '_' or '-'))
            throw new ArgumentException("A level needs a plain map name, without a path or extension.", nameof(levelName));
        return new(GameSessionMode.Campaign, levelName, returnPage);
    }
}
