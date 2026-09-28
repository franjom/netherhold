// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-thunderbolt.md")]
public sealed class KeeperSpellSystem : ISimulationSystem
{
    public string Name => "keeper_spells";

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            if (command is not CastSpellOnCreatureCommand cast) continue;
            KeeperSpellRules.CastOnCreature(context, cast, out KeeperSpellRefusal refusal);
            context.Emit(Name, refusal == KeeperSpellRefusal.None ? "cast" : "refused",
                $"player={cast.PlayerId} spell={cast.SpellId} target={cast.CreatureId} reason={refusal}");
        }
    }
}
