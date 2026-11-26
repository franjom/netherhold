// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data;

/// <summary>What a folder holds, judged by the one rule for an install: <c>DKII.EXE</c> and a <c>Data</c> folder.</summary>
public enum Dk2InstallCheck
{
    /// <summary>No folder was named.</summary>
    Empty,
    /// <summary>The folder is missing or has no <c>DKII.EXE</c>.</summary>
    NoGame,
    /// <summary><c>DKII.EXE</c> is there but the <c>Data</c> folder is not.</summary>
    NoData,
    Found,
}

/// <summary>Locates the local Dungeon Keeper 2 installation.
/// <para>Resolution order (<see cref="Resolve"/>): the <c>DK2_DIR</c> environment variable, then a folder named on the
/// command line, each authoritative when set (no other location is considered, so a run can be pointed at a specific
/// install, or at nothing at all, without surprises); then the folder the launcher saved, when it still holds an
/// install; then known default paths.</para>
/// <para>Returning null is a normal outcome, not an error: CI has no installation and the
/// install-dependent tests skip. Nothing in the shipped game may require this type to
/// succeed.</para></summary>
public static class Dk2Install
{
    /// <summary>
    /// Where the reference install lives on the development machine
    /// (docs/HANDOFF.md). Overridden by DK2_DIR anywhere else.
    /// </summary>
    private static readonly string[] DefaultCandidates =
    [
        @"E:\GogGames\Dungeon Keeper 2",
    ];

    public static string? Find() => Find(null, null);

    /// <summary>The install by <see cref="Resolve"/>'s order, with DK2_DIR read from the environment.</summary>
    /// <param name="commandLine">A folder the run named on its command line, or null.</param>
    /// <param name="saved">The folder the launcher saved, or null.</param>
    public static string? Find(string? commandLine, string? saved) =>
        Resolve(Environment.GetEnvironmentVariable("DK2_DIR"), commandLine, saved, DefaultCandidates);

    /// <summary>The first install by precedence: <paramref name="environment"/>, then <paramref name="commandLine"/>
    /// (either, when not blank, decides alone, null if it is no install), then <paramref name="saved"/> if it holds an
    /// install (a moved folder falls through), then the first of <paramref name="defaults"/> that does.</summary>
    public static string? Resolve(string? environment, string? commandLine, string? saved, IReadOnlyList<string> defaults)
    {
        if (defaults is null)
        {
            throw new ArgumentNullException(nameof(defaults));
        }

        if (!string.IsNullOrWhiteSpace(environment))
        {
            return IsInstall(environment) ? environment : null;
        }

        if (!string.IsNullOrWhiteSpace(commandLine))
        {
            return IsInstall(commandLine) ? commandLine : null;
        }

        if (IsInstall(saved))
        {
            return saved;
        }

        foreach (string candidate in defaults)
        {
            if (IsInstall(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string Require() =>
        Find() ?? throw new InvalidOperationException(
            "Dungeon Keeper 2 installation not found. Set the DK2_DIR environment " +
            "variable to the game directory (the one containing DKII.EXE).");

    /// <summary>What <paramref name="dir"/> holds by the install rule (<see cref="Dk2InstallCheck"/>).</summary>
    public static Dk2InstallCheck Check(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return Dk2InstallCheck.Empty;
        }

        try
        {
            if (!File.Exists(Path.Combine(dir, "DKII.EXE")))
            {
                return Dk2InstallCheck.NoGame;
            }

            return Directory.Exists(Path.Combine(dir, "Data")) ? Dk2InstallCheck.Found : Dk2InstallCheck.NoData;
        }
        catch (ArgumentException) { return Dk2InstallCheck.NoGame; }   // a typed path with characters no path can hold (Mono)
    }

    public static bool IsInstall(string? dir) => Check(dir) == Dk2InstallCheck.Found;
}
