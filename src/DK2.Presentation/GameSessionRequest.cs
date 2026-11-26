// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Save;
using DK2.Simulation;

namespace DK2.Presentation;

public enum GameSessionMode : byte { Campaign = 1, FrontEnd = 5, ImportedTerrain = SavedSessionHeader.TerrainImportMode }

public sealed record GameSessionRequest
{
    private GameSessionRequest(GameSessionMode mode, string levelName, FrontEndPage returnPage)
        => (Mode, LevelName, ReturnPage) = (mode, levelName, returnPage);

    public GameSessionMode Mode { get; }
    public string LevelName { get; }
    public FrontEndPage ReturnPage { get; }

    /// <summary>A loaded save to continue instead of a fresh level (<see cref="SessionLoader"/>), or null.</summary>
    public SessionResume? Resume { get; private init; }

    /// <summary>The terrain and ownership a legacy save initializes the session with.</summary>
    public LevelMap? ImportedTerrain { get; private init; }

    public static GameSessionRequest FrontEnd(FrontEndPage returnPage = FrontEndPage.None)
        => new(GameSessionMode.FrontEnd, "FrontEnd3DLevel", returnPage);

    public static GameSessionRequest Restart(GameSessionRequest current)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (current.Mode is not (GameSessionMode.Campaign or GameSessionMode.ImportedTerrain)) throw new ArgumentException("Only a running level can restart.", nameof(current));
        return current with { Resume = null };
    }

    public static GameSessionRequest Campaign(string levelName, FrontEndPage returnPage = FrontEndPage.CampaignResults)
    {
        if (string.IsNullOrEmpty(levelName) || !levelName.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                or >= '0' and <= '9' or '_' or '-'))
            throw new ArgumentException("A level needs a plain map name, without a path or extension.", nameof(levelName));
        return new(GameSessionMode.Campaign, levelName, returnPage);
    }

    /// <summary>Loads the terrain from a save path, with the install's save directory as a fallback for relative paths.</summary>
    public static GameSessionRequest LoadDk2Save(string install, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A DK2 save path is required.", nameof(path));
        if (!Path.IsPathRooted(path) && !File.Exists(path)) path = Path.Combine(Dk2SaveReader.SaveDirectory(install), path);
        SavedGame save = Dk2SaveReader.Load(install, path)
            ?? throw new InvalidDataException("The installation has no map for the level named by the DK2 save.");
        return FromDk2Save(save);
    }

    public static GameSessionRequest FromDk2Save(SavedGame save)
    {
        if (save is null) throw new ArgumentNullException(nameof(save));
        return ForTerrain(save.Header.LevelName, save.Map);
    }

    private static GameSessionRequest ForTerrain(string levelName, LevelMap terrain) =>
        new(GameSessionMode.ImportedTerrain, levelName, FrontEndPage.None) { ImportedTerrain = terrain };

    /// <summary>The session the options menu's confirmed entry requests
    /// (<c>IHudPressHost.StartSession</c>): Restart starts the running level afresh with its return page, Return to Menu
    /// opens the front end at the campaign map when the level came from it and at the main page otherwise. Quit, and
    /// <see cref="ClassicSessionAction.None"/>, ask for no session: null.</summary>
    public static GameSessionRequest? ForSessionAction(ClassicSessionAction action, SessionContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        return action switch
        {
            ClassicSessionAction.Restart => context.ImportedTerrain is LevelMap terrain ? ForTerrain(context.LevelName, terrain)
                : Restart(Campaign(context.LevelName, context.ReturnPage)),
            ClassicSessionAction.ReturnToMenu => FrontEnd(context.LaunchedFromFrontEnd ? FrontEndPage.CampaignMap : FrontEndPage.None),
            _ => null,
        };
    }

    /// <summary>The campaign request a loaded save continues: its level and return page, carrying the restored simulation.
    /// <see cref="Restart"/> of it starts the level afresh.</summary>
    public static GameSessionRequest Resumed(SessionResume resume)
    {
        if (resume is null) throw new ArgumentNullException(nameof(resume));
        SavedSessionHeader header = resume.Session.Header;
        GameSessionRequest request = header.GameMode == SavedSessionHeader.TerrainImportMode
            ? ForTerrain(header.LevelName, GameSessionStarter.TerrainOf(resume.Simulation.World))
            : Campaign(header.LevelName, (FrontEndPage)header.ReturnPageId);
        return request with { Resume = resume };
    }
}

/// <summary>A save as <see cref="SessionLoader"/> restored it: the file's contents and the simulation rebuilt from its
/// checkpoint, ready to tick on from the saved turn.</summary>
public sealed record SessionResume(SavedSession Session, GameSimulation Simulation);
