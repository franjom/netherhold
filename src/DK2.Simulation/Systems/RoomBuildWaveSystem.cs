// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>Drains the player's room construction diagonals after keeper mana, before research selection.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/room-slab-chain.md")]
public sealed class RoomBuildWaveSystem : ISimulationSystem
{
    public string Name => "RoomBuildWave";

    public void Execute(SimulationContext context)
    {
        for (int playerIndex = 0; playerIndex < context.World.PlayerCount; playerIndex++)
            RoomBuildSystem.Drain(context, context.World.PlayerAtIndex(playerIndex).PlayerId);
    }
}
