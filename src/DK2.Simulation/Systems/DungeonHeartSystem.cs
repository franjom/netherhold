// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The dungeon heart's own frame and the verdict it can bring, three of DK2's
/// routines in the order their turn runs them (docs/research/win-and-defeat.md, ADR-0162):
/// the heart room's update <c>0x004D9A60</c> regenerates the heart object once a second
/// and, finding no object, ruins the room (<c>0x004E9130</c>, <c>+0x9A</c> bit 0); the
/// player frame's tail <c>0x004BB2A0</c> reads <c>KilledByPlayerId</c> and ends the game
/// — lost to heroes or neutrals, conquered by a keeper — then awards the win to the one
/// keeper left playing. The verdict is one-shot: a <c>WIN_GAME</c> already performed stands.
/// <para>Not built, and named so nothing here is mistaken for it: what a defeat <i>does</i>
/// to creatures, rooms and gold (Q40, Q358) lives in <c>0x004DAA10</c>/<c>0x004DAE40</c>,
/// unread; and alliances end games together in DK2 (Q359), where here allies are not
/// consulted.</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/win-and-defeat.md")]
public sealed class DungeonHeartSystem : ISimulationSystem
{
    public string Name => "dungeon heart";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int max = database[VariableTypeId.DungeonHeartObjectHealth];
        int regen = database[VariableTypeId.DungeonHeartHealthRegenerationPerSecond];
        bool secondEnds = (context.Tick.Value + 1) % SimulationTiming.TicksPerSecond == 0;
        bool anyoneFell = false;

        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            if (!PlayerIds.IsKeeper(player.PlayerId)) continue;

            if (ImpSupplyRules.HeartTileOf(world, database, player.PlayerId) >= 0)
                RoomFrame(context, ref player, max, regen, secondEnds);

            // The frame's tail: a stamped killer is the whole condition, and the
            // verdict depends only on which side struck the blow.
            if (player.KilledByPlayerId == 0 || PlayerStatus.HasEnded(player.Status)) continue;
            byte verdict = PlayerIds.IsKeeper(player.KilledByPlayerId)
                ? PlayerStatus.Conquered
                : PlayerStatus.Lost;
            if (world.EndGame(player.PlayerId, verdict))
            {
                anyoneFell = true;
                context.Emit(Name, verdict == PlayerStatus.Conquered ? "conquered" : "lost",
                    $"player={player.PlayerId} by={player.KilledByPlayerId}");
            }
        }

        if (anyoneFell) AwardLastKeeperStanding(context);
    }

    /// <summary>Once a second the heart heals by the regeneration rate, never past its
    /// maximum and never from zero — a dead heart does not come back. The room whose
    /// heart object is gone becomes a ruin on this frame, one turn after the death.</summary>
    private void RoomFrame(
        SimulationContext context, ref PlayerState player, int max, int regen, bool secondEnds)
    {
        if (player.HeartHealth <= 0)
        {
            if (player.HeartRuined) return;
            player.HeartRuined = true;
            context.Emit(Name, "heart_ruined", $"player={player.PlayerId}");
            return;
        }

        if (!secondEnds) return;
        player.HeartHealth = Math.Min(max, player.HeartHealth + regen);
    }

    /// <summary>DK2 counts the keepers still playing only when somebody has just fallen,
    /// and only outside its multiplayer mode; exactly one left means that one has won.</summary>
    private void AwardLastKeeperStanding(SimulationContext context)
    {
        WorldState world = context.World;
        int standing = 0;
        byte survivor = 0;
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            if (!PlayerIds.IsKeeper(player.PlayerId) || PlayerStatus.HasEnded(player.Status)) continue;
            standing++;
            survivor = player.PlayerId;
        }

        if (standing == 1 && world.EndGame(survivor, PlayerStatus.Won))
            context.Emit(Name, "won", $"player={survivor} last keeper standing");
    }
}
