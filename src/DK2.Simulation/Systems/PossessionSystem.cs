// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation.Commands;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Systems;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public sealed class PossessionSystem : ISimulationSystem
{
    public string Name => "possession";

    private readonly PossessionCombat _combat = new();

    public void Execute(SimulationContext context)
    {
        context.Navigation.Refresh();
        var sight = new WorldSight(context.Navigation, context.World, context.Database);
        _combat.Hearts.Refresh(context.World, context.Database);
        foreach (PlayerCommand command in context.Input.All)
        {
            if (!context.World.HasPlayer(command.PlayerId)) continue;
            ref PlayerState player = ref context.World.Player(command.PlayerId);
            if (command is LeavePossessionCommand) PossessionRules.Leave(context, ref player);
            else if (player.PossessedCreatureId == 0 || context.World.CreatureById(player.PossessedCreatureId) is not { } creature) continue;
            else if (command is MovePossessedCreatureCommand move) PossessionRules.Move(context, creature, move.Pose);
            else if (command is ActPossessedCreatureCommand act) PossessionRules.Act(context, sight, _combat, creature, act);
            else if (command is DismissFollowersCommand) PossessionRules.Dismiss(context, command.PlayerId, creature);
            else if (command is ToggleSneakCommand sneak) PossessionRules.ToggleSneak(context, sight, creature, sneak.TileX, sneak.TileY);
            else if (command is HypnotiseCommand hypnotise) PossessionRules.Hypnotise(context, sight, command.PlayerId, creature, hypnotise.Aim);
            else if (command is SetSniperModeCommand sniper) PossessionRules.SetSniperMode(creature, sniper.On);
        }
        foreach (CreatureState creature in context.World.Creatures)
        {
            PossessionRules.EndCommandedAttack(context.World, creature);
            PossessionRules.ForgetStaleTarget(context, creature);
            PossessionRules.RefreshInfluence(context, creature, sight);
        }
    }
}
