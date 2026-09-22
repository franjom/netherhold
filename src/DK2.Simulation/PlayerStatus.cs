// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>The values of a player's <c>Status</c>, DK2's own name for the dword at
/// <c>player+0x19A</c>. Only one routine ever writes it, <c>0x004BE630</c>, and only when it
/// is still <see cref="Playing"/>: a game ends once. The codes are DK2's — <c>WIN_GAME</c>
/// pushes 2, <c>LOSE_GAME</c> 3, and the player frame pushes 1 when a rival keeper destroyed
/// the heart and 3 when heroes or neutrals did (docs/research/win-and-defeat.md).</summary>
public static class PlayerStatus
{
    public const byte Playing = 0;

    /// <summary>Heart destroyed by another keeper. Distinct from <see cref="Lost"/> in DK2,
    /// which shows the conqueror and the conquered different objective texts.</summary>
    public const byte Conquered = 1;

    public const byte Won = 2;
    public const byte Lost = 3;

    public static bool HasEnded(byte status) => status != Playing;
}
