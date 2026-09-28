// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Applies a selected special before creatures act on this tick.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/specials.md")]
public sealed class SpecialSystem : ISimulationSystem
{
    private const byte IncreaseLevel = 117;
    public string Name => "Special";

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand input in context.Input.All)
        {
            if (input is not ActivateSpecialCommand command) continue;
            ObjectState? item = context.World.ObjectById(command.ObjectId);
            if (item is null) continue;
            ObjectDef? definition = context.Database.ObjectById(item.ObjectTypeId);
            if (definition?.IsSpecial != true) continue;
            if (item.ObjectTypeId != IncreaseLevel)
            {
                context.Emit(Name, "special_unimplemented",
                    $"player={command.PlayerId} object={item.Id} type={item.ObjectTypeId}");
                continue;
            }

            int affected = 0;
            foreach (CreatureState creature in context.World.Creatures)
            {
                if (creature.OwnerPlayerId != command.PlayerId || creature.Health <= 0)
                    continue;
                CreatureDef creatureDef = context.Database.CreatureById(creature.CreatureTypeId);
                CreatureLevelRules.LevelUp(context.Database, creatureDef, creature);
                CreatureHealthRules.Apply(context.Database, creatureDef, creature,
                    CreatureHealthRules.MaxHealth(context.Database, creatureDef, creature));
                affected++;
            }
            context.World.RemoveObject(item.Id);
            context.Emit(Name, "special_activated",
                $"player={command.PlayerId} object={item.Id} type={item.ObjectTypeId} creatures={affected}");
        }
    }
}
