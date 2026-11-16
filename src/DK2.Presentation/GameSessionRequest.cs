// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Save;
using DK2.Simulation;

namespace DK2.Presentation;

public enum GameSessionMode : byte { Campaign = 1, FrontEnd = 5 }

public sealed record GameSessionRequest
{
    private GameSessionRequest(GameSessionMode mode, string levelName, FrontEndPage returnPage)
        => (Mode, LevelName, ReturnPage) = (mode, levelName, returnPage);

    public GameSessionMode Mode { get; }
    public string LevelName { get; }
    public FrontEndPage ReturnPage { get; }

    /// <summary>A loaded save to continue instead of a fresh level (<see cref="SessionLoader"/>), or null.</summary>
    public SessionResume? Resume { get; private init; }

    public static GameSessionRequest FrontEnd(FrontEndPage returnPage = FrontEndPage.None)
        => new(GameSessionMode.FrontEnd, "FrontEnd3DLevel", returnPage);

    public static GameSessionRequest Restart(GameSessionRequest current)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (current.Mode != GameSessionMode.Campaign) throw new ArgumentException("Only a running level can restart.", nameof(current));
        return Campaign(current.LevelName, current.ReturnPage);
    }

    public static GameSessionRequest Campaign(string levelName, FrontEndPage returnPage = FrontEndPage.CampaignResults)
    {
        if (string.IsNullOrEmpty(levelName) || !levelName.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= '0' and <= '9' or '_' or '-'))
            throw new ArgumentException("A level needs a plain map name, without a path or extension.", nameof(levelName));
        return new(GameSessionMode.Campaign, levelName, returnPage);
    }

    /// <summary>The campaign request a loaded save continues: its level and return page, carrying the restored simulation.
    /// <see cref="Restart"/> of it starts the level afresh.</summary>
    public static GameSessionRequest Resumed(SessionResume resume)
    {
        if (resume is null) throw new ArgumentNullException(nameof(resume));
        SavedSessionHeader header = resume.Session.Header;
        return Campaign(header.LevelName, (FrontEndPage)header.ReturnPageId) with { Resume = resume };
    }
}

/// <summary>A save as <see cref="SessionLoader"/> restored it: the file's contents and the simulation rebuilt from its
/// checkpoint, ready to tick on from the saved turn.</summary>
public sealed record SessionResume(SavedSession Session, GameSimulation Simulation);
