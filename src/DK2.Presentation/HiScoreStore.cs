// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Campaign;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The hi-score table: our own file (<see cref="GameFolders.HiScores"/>); on first use the install's runtime
/// table is read, never written; else ten empty rows (ADR-0538's import pattern).</summary>
public sealed class HiScoreStore
{
    public HiScoreStore(string path, string install)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        InstallPath = System.IO.Path.Combine(install ?? throw new ArgumentNullException(nameof(install)), "Data", "Settings", "HiScores.dat");
    }

    public string Path { get; }

    /// <summary>The install's own table, read only when <see cref="Path"/> has none.</summary>
    public string InstallPath { get; }

    /// <summary>The first readable table of <see cref="Path"/> and <see cref="InstallPath"/>, else
    /// <see cref="HiScoreTable.Empty"/>; a file that exists but cannot be read is passed to <paramref name="unreadable"/>.</summary>
    public IReadOnlyList<HiScoreEntry> Load(Action<string>? unreadable = null) =>
        Read(Path, unreadable) ?? Read(InstallPath, unreadable) ?? HiScoreTable.Empty;

    /// <summary>Writes every table the map changes to <see cref="Path"/>.</summary>
    public void Track(CampaignMapController map)
    {
        if (map is null) throw new ArgumentNullException(nameof(map));
        map.HiScoresChanged += rows => HiScoresFile.Save(Path, rows);
    }

    private static IReadOnlyList<HiScoreEntry>? Read(string candidate, Action<string>? unreadable)
    {
        try { if (File.Exists(candidate)) return HiScoresFile.Read(File.ReadAllBytes(candidate)); }
        catch (InvalidDataException) { unreadable?.Invoke(candidate); }
        return null;
    }
}
