// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A set of players, one bit per player id (bit <c>n</c> is player <c>n</c>), 16 bits wide.
/// Our representation of per-player tile state, not a claim about DK2's packing. An id without a bit
/// (above <see cref="MaxPlayerId"/>) is never in a mask: <see cref="Contains"/> says no and
/// <see cref="Without"/> changes nothing, while <see cref="With"/> refuses it.</summary>
public readonly record struct PlayerMask(ushort Bits)
{
    /// <summary>Highest player id that has a bit.</summary>
    public const int MaxPlayerId = 15;

    public static PlayerMask None => default;

    public bool IsEmpty => Bits == 0;

    public bool Contains(PlayerId playerId) =>
        playerId.Value <= MaxPlayerId && (Bits & (1 << playerId.Value)) != 0;

    public PlayerMask With(PlayerId playerId)
    {
        if (playerId.Value > MaxPlayerId)
            throw new ArgumentOutOfRangeException(
                nameof(playerId), playerId.Value, $"A player mask only holds player ids 0..{MaxPlayerId}.");

        return new PlayerMask((ushort)(Bits | (1 << playerId.Value)));
    }

    public PlayerMask Without(PlayerId playerId) =>
        playerId.Value > MaxPlayerId ? this : new PlayerMask((ushort)(Bits & ~(1 << playerId.Value)));
}
