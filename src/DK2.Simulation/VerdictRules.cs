// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Ending a player's game: DK2's <c>0x004BE630</c>, every caller's way in. The first verdict
/// stands (<see cref="WorldState.EndGame"/>); in skirmish and multiplayer it then carries to the
/// other keepers through alliances (win-and-defeat.md §7, ADR-0547).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/win-and-defeat.md")]
public static class VerdictRules
{
    /// <summary>Ends <paramref name="playerId"/>'s game with <paramref name="status"/>; returns whether
    /// this call ended it. Each player it ends, this one first, is reported to <paramref name="ended"/>.
    /// The loops follow the score block, which returns in modes 1 and 4, and walk the active
    /// player list (<see cref="WorldState.ActivePlayerOrder"/>), keepers only.</summary>
    public static bool End(WorldState world, PlayerId playerId, byte status, Action<PlayerId, byte>? ended = null)
    {
        if (!world.EndGame(playerId, status)) return false;
        ended?.Invoke(playerId, status);
        if (world.GameMode is not (GameMode.Skirmish or GameMode.Multiplayer)) return true;

        // 0x004BEFE1: a winner ends every keeper that is not its mutual ally; allies play on.
        if (status == PlayerStatus.Won)
        {
            foreach (PlayerId other in world.ActivePlayerOrder)
                if (PlayerIds.IsKeeper(other.Value) && other != playerId && !MutualAllies(world, playerId, other))
                    End(world, other, PlayerStatus.Lost, ended);
            return true;
        }

        // 0x004BEF40: a lost or conquered keeper, unless the time limit ran out (+0x72A1 bit 3).
        if (world.Triggers.LevelTimeUp) return true;
        int remaining = 0;
        PlayerId last = default;
        foreach (PlayerId other in world.ActivePlayerOrder)
            if (StillIn(world, other) && other != playerId) { remaining++; last = other; }

        if (remaining == 1)
        {
            if (world.Player(last).Status == PlayerStatus.Playing) End(world, last, PlayerStatus.Won, ended);
            return true;
        }
        if (remaining < 2) return true;

        // 0x004BF0AE: the last one met wins only when every other one left is its mutual ally,
        // then (0x004BF172) each ally still playing wins with it.
        foreach (PlayerId other in world.ActivePlayerOrder)
            if (StillIn(world, other) && other != last && !MutualAllies(world, last, other)) return true;
        End(world, last, PlayerStatus.Won, ended);
        foreach (PlayerId other in world.ActivePlayerOrder)
            if (PlayerIds.IsKeeper(other.Value) && other != last
                && world.Player(other).Status == PlayerStatus.Playing && MutualAllies(world, last, other))
                End(world, other, PlayerStatus.Won, ended);
        return true;
    }

    /// <summary>A keeper still in the game for the loss branch's count: playing, or already won.</summary>
    private static bool StillIn(WorldState world, PlayerId id) =>
        PlayerIds.IsKeeper(id.Value) && world.Player(id).Status is PlayerStatus.Playing or PlayerStatus.Won;

    private static bool MutualAllies(WorldState world, PlayerId a, PlayerId b) =>
        world.AreAllied(a, b.Value) && world.AreAllied(b, a.Value);
}
