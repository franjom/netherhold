// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>A keeper's Create Imp casts (packet action 54), with the other keeper actions and so
/// before the thing pass: <c>World_UpdateTurn</c> (<c>0x0050A450</c>) dispatches the packet's actions
/// before it updates the things, so the cast's shot is in the shot list when it is walked this turn
/// and the imp is made on the cast's own turn, as the Level 4 casts at packet turns 392 and 552 show
/// (imp-observation-clocks.md). A free cast is made in the player update after that walk and
/// waits a turn (<see cref="ImpSupplySystem"/>, ADR-0635).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/functions/0050A450.md")]
public sealed class CreateImpCastSystem : ISimulationSystem
{
    public string Name => "CreateImpCast";

    public void Execute(SimulationContext context)
    {
        foreach (CreateImpCommand create in context.Commands<CreateImpCommand>())
            ImpSupplySystem.Buy(context, create);
    }
}
