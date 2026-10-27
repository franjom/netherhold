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
public sealed class CasinoJackpotSystem : ICreatureUpdateStep
{
    private const string SystemName = "CasinoJackpot";

    public string Name => SystemName;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    public void Update(SimulationContext context, CreatureState creature)
    {
        if (CreatureDeathRules.IsCorpse(creature)) return;
        CasinoJackpotRules.CountDown(context, SystemName, creature);
        if (creature.Activity != CreatureActivity.CollectingWinnings || creature.Possessed) return;
        if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Held | CreatureGuards.Down)) return;
        if (!CasinoJackpotRules.Turn(context, SystemName, creature) && CreatureStateMachine.HasState(creature)
            && creature.Dk2StateId != CreatureStateMachine.Idle)
            StateShadow.ToIdle(context, SystemName, creature);
    }
}
