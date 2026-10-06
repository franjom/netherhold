// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A Casino jackpot's aftermath for each creature (ADR-0396): the creature update's two
/// countdowns, the drunk timer and a winner's deadline, and a winner's own states 152 and 155–157
/// (<see cref="CasinoJackpotRules"/>).</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/casino.md")]
public sealed class CasinoJackpotSystem : ISimulationSystem
{
    private const string SystemName = "CasinoJackpot";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (CreatureDeathRules.IsCorpse(creature)) continue;
            CasinoJackpotRules.CountDown(context, SystemName, creature);
            if (creature.Activity != CreatureActivity.CollectingWinnings || creature.Possessed) continue;
            if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Held | CreatureGuards.Down)) continue;
            if (!CasinoJackpotRules.Turn(context, SystemName, creature) && CreatureStateMachine.HasState(creature)
                && creature.Dk2StateId != CreatureStateMachine.Idle)
                StateShadow.ToIdle(context, SystemName, creature);
        }
    }
}
