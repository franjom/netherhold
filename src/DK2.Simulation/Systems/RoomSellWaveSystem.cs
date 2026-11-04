// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>Drains the players' queued sales after the build wave, as DK2's keeper frame calls 0x004C7160 after
/// 0x004C7380.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/room-slab-chain.md")]
public sealed class RoomSellWaveSystem : ISimulationSystem
{
    public string Name => "RoomSellWave";

    public void Execute(SimulationContext context)
    {
        for (int playerIndex = 0; playerIndex < context.World.PlayerCount; playerIndex++)
            RoomSellSystem.Drain(context, context.World.PlayerAtIndex(playerIndex).PlayerId);
    }
}
