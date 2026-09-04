// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data;

/// <summary>
/// Locates the local Dungeon Keeper 2 installation used as reference material.
///
/// Resolution order:
/// <list type="number">
/// <item>the <c>DK2_DIR</c> environment variable, which is authoritative — if
/// it is set, no other location is considered, so a run can be pointed at a
/// specific install (or at nothing at all) without surprises;</item>
/// <item>otherwise, known default paths.</item>
/// </list>
///
/// Returning null is a normal outcome, not an error: CI has no installation and
/// the install-dependent tests skip. Nothing in the shipped game may require
/// this type to succeed.
/// </summary>
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

    public static string? Find()
    {
        string? env = Environment.GetEnvironmentVariable("DK2_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return IsValid(env) ? env : null;

        foreach (string candidate in DefaultCandidates)
            if (IsValid(candidate)) return candidate;

        return null;
    }

    public static string Require() =>
        Find() ?? throw new InvalidOperationException(
            "Dungeon Keeper 2 installation not found. Set the DK2_DIR environment " +
            "variable to the game directory (the one containing DKII.EXE).");

    private static bool IsValid(string? dir) =>
        !string.IsNullOrEmpty(dir)
        && File.Exists(Path.Combine(dir, "DKII.EXE"))
        && Directory.Exists(Path.Combine(dir, "Data"));
}
