// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Applies this tick's <see cref="SetAllianceCommand"/>s, the original's make- and
/// break-alliance handlers (<c>0x00513FF0</c>, <c>0x00513F60</c>, doors-and-traps.md). Each
/// writes both players' arrays; neither checks that the issuer is one of the pair, so this does
/// not either. A command naming a player the level does not have is dropped rather than faulted:
/// the original indexes an entity table by the id in the packet and a missing player is a null
/// there.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/doors-and-traps.md")]
public sealed class AllianceSystem : ISimulationSystem
{
    public string Name => "alliances";

    public void Execute(SimulationContext context)
    {
        foreach (SetAllianceCommand alliance in context.Commands<SetAllianceCommand>())
        {
            if (!Known(context, alliance.PlayerId) || !Known(context, alliance.OtherPlayerId)) continue;
            if (alliance.PlayerId >= 8 || alliance.OtherPlayerId >= 8) continue;
            context.World.SetAllied(alliance.PlayerId, alliance.OtherPlayerId.Value, alliance.Allied);
        }
    }

    private static bool Known(SimulationContext context, PlayerId playerId)
    {
        for (int i = 0; i < context.World.PlayerCount; i++)
            if (context.World.PlayerAtIndex(i).PlayerId == playerId) return true;
        return false;
    }
}
