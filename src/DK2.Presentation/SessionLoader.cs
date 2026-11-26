// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Campaign;
using DK2.Data.Save;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Presentation;

/// <summary>Reads a saved session and rebuilds its simulation before the running one is released: the level's records
/// come from the adapter's level loader (<see cref="CampaignLevelData.Load"/> over the install), the trigger tree is
/// fresh (<see cref="CampaignSimulationFactory.Triggers"/>) and the world is loaded as DK2 loads a saved game
/// (<see cref="WorldSnapshot.LoadGame"/>, ADR-0241): the snapshot is restored and every room chain comes back row-major,
/// so a loaded game's state hash can differ from the saved run's where a room was chained otherwise. The saved profile
/// is written to the profile file and not adopted: the next session reads it.</summary>
public sealed class SessionLoader
{
    private readonly CampaignProfileStore _profiles;
    private readonly Action _loadProfile;
    private readonly Func<string, CampaignLevelData> _loadLevel;
    public bool TerrainImportsSupported { get; init; }

    /// <param name="loadProfile">Loads the profile store, used when it has no file yet.</param>
    /// <param name="loadLevel">A level's records by name; an <see cref="InvalidOperationException"/> (a missing record) refuses the save.</param>
    public SessionLoader(CampaignProfileStore profiles, Action loadProfile, Func<string, CampaignLevelData> loadLevel)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _loadProfile = loadProfile ?? throw new ArgumentNullException(nameof(loadProfile));
        _loadLevel = loadLevel ?? throw new ArgumentNullException(nameof(loadLevel));
    }

    /// <summary>The campaign request that resumes the save at <paramref name="path"/>, carrying the restored simulation
    /// (<see cref="GameSessionRequest.Resume"/>). An adapter joins its tick worker first. Throws
    /// <see cref="SaveIncompatibleException"/> for a save of another mode or return page, or of a level the install lacks, and
    /// <see cref="IOException"/> when the profile store has no file to write the saved profile to; nothing is written then.</summary>
    public GameSessionRequest Load(string path)
    {
        SavedSession session = SavedSessionFile.Load(path);
        var mode = (GameSessionMode)session.Header.GameMode;
        bool supportedMode = mode == GameSessionMode.Campaign || mode == GameSessionMode.ImportedTerrain && TerrainImportsSupported;
        if (!supportedMode || !Enum.IsDefined(typeof(FrontEndPage), session.Header.ReturnPageId))
            throw new SaveIncompatibleException(SaveIncompatibleReason.Corrupt, "This saved session has an unsupported game mode or return page.");
        string name = session.Header.LevelName;
        CampaignLevelData level;
        try { level = _loadLevel(name); }
        catch (InvalidOperationException)
        {
            throw new SaveIncompatibleException(SaveIncompatibleReason.Corrupt, "The saved level is not available in this installation.");
        }
        TriggerTree? tree = mode == GameSessionMode.Campaign
            ? CampaignSimulationFactory.Triggers(level.Level, level.Players, level.Things, level.Triggers) : null;
        GameSimulation simulation = WorldSnapshot.LoadGame(session.Snapshot, level.Database, SimulationSystems.Default(), tree);
        if (_profiles.Path == null) _loadProfile();
        // A store left with no file (an adapter's fallback for an unreadable profile) cannot take the saved profile.
        if (_profiles.Path == null)
            throw new IOException("The campaign profile could not be read, so a save cannot be loaded.");
        CampaignProfileFile.Save(_profiles.Path, session.Profile);
        return GameSessionRequest.Resumed(new SessionResume(session, simulation));
    }
}
