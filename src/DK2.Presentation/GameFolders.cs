// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>Locations for the campaign profile, settings, saves and hi-score table under <paramref name="Root"/>.
/// A nonblank override supplies a different path.</summary>
public sealed record GameFolders(string Root)
{
    public const string ProfileFileName = "campaign-profile.dk2p";
    public const string SettingsFileName = "game-settings.ini";
    public const string SavesFolderName = "Saves";
    public const string HiScoresFileName = "HiScores.dat";

    public string? ProfileFile { get; init; }
    /// <summary>Unset, the settings follow the profile into its folder.</summary>
    public string? SettingsFile { get; init; }
    public string? SavesDirectory { get; init; }
    public string? HiScoresFile { get; init; }

    public string Profile => Named(ProfileFile) ?? Path.Combine(Root, ProfileFileName);

    public string Settings => Named(SettingsFile)
        ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Profile)) ?? Root, SettingsFileName);

    public string Saves => Named(SavesDirectory) ?? Path.Combine(Root, SavesFolderName);

    public string HiScores => Named(HiScoresFile) ?? Path.Combine(Root, HiScoresFileName);

    private static string? Named(string? path) => string.IsNullOrWhiteSpace(path) ? null : path;
}
