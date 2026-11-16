// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The live campaign profile a session shares between the front end, the level end and the saves, and the file
/// it is kept in (<see cref="GameFolders.Profile"/>). The adapter supplies the importer a missing file is seeded from
/// (both pass the read-only, Windows-only <see cref="InstalledCampaignRegistry.ReadCurrentUser"/>) and the local clock
/// the secret-five moon phase is read from.</summary>
public sealed class CampaignProfileStore
{
    private readonly Func<CampaignProfile?>? _import;
    private readonly Func<DateTime> _localClock;

    public CampaignProfileStore(Func<CampaignProfile?>? import, Func<DateTime> localClock)
    {
        _import = import;
        _localClock = localClock ?? throw new ArgumentNullException(nameof(localClock));
    }

    /// <summary>The profile file, null until the first <see cref="Load"/>.</summary>
    public string? Path { get; private set; }

    /// <summary>The live profile, null until the first <see cref="Load"/>. Its owner writes it with <see cref="Save"/>.</summary>
    public CampaignProfile? Profile { get; set; }

    /// <summary>Whether the moon phase at the last <see cref="Load"/> opens secret level five.</summary>
    public bool SecretFiveAvailable { get; private set; }

    /// <summary>Raised for each profile a tracked map changes, after <see cref="Profile"/> takes it and before the file is written.</summary>
    public event Action<CampaignProfile>? Changed;

    /// <summary>Whether entering the front end re-applies the won map's branch row. Ours repairs only on the
    /// campaign-results page; see 0x00534F70's record for the return states DK2 reaches it from.</summary>
    public static bool RepairsAfterLevel(GameSessionMode mode, FrontEndPage returnPage) =>
        mode == GameSessionMode.FrontEnd && returnPage == FrontEndPage.CampaignResults;

    /// <summary>Reads the file, or creates it from the import; opens a map if none is open, repairs after a level
    /// (<see cref="RepairsAfterLevel"/>), writes the file back only if either changed it, and samples the moon.</summary>
    public CampaignProfile Load(string path, GameSessionMode mode, FrontEndPage returnPage)
    {
        Path = path;
        CampaignProfile loaded = CampaignProfileFile.LoadOrCreate(path, _import);
        CampaignProfile profile = CampaignProgression.EnsureOpenMap(loaded);
        if (RepairsAfterLevel(mode, returnPage)) profile = CampaignProgression.RepairAfterLevel(profile);
        Profile = profile;
        if (!ReferenceEquals(loaded, profile)) CampaignProfileFile.Save(path, profile);
        SecretFiveAvailable = CampaignMoon.IsSecretFiveAvailable(CampaignMoon.PhaseAt(_localClock()));
        return profile;
    }

    /// <summary>Uses <paramref name="profile"/> with no file, for an adapter whose file could not be read or created:
    /// <see cref="Path"/> is null, so tracked changes stay in memory. Opens a map and samples the moon as <see cref="Load"/> does.</summary>
    public CampaignProfile Adopt(CampaignProfile profile)
    {
        Path = null;
        Profile = CampaignProgression.EnsureOpenMap(profile ?? throw new ArgumentNullException(nameof(profile)));
        SecretFiveAvailable = CampaignMoon.IsSecretFiveAvailable(CampaignMoon.PhaseAt(_localClock()));
        return Profile;
    }

    /// <summary>Writes the live profile to its file.</summary>
    public void Save() => CampaignProfileFile.Save(Path!, Profile!);

    /// <summary>Keeps every profile the map changes: it becomes the live one, <see cref="Changed"/> runs, then the file
    /// is written (not after <see cref="Adopt"/>, which has none).</summary>
    public void Track(CampaignMapController map)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        map.ProfileChanged += profile =>
        {
            Profile = profile;
            Changed?.Invoke(profile);
            if (Path != null) CampaignProfileFile.Save(Path, profile);
        };
    }
}
