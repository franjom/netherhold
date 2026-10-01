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
            switch (command)
            {
                case CastSpellOnCreatureCommand cast:
                    KeeperSpellRules.CastOnCreature(context, cast, out KeeperSpellRefusal refusal);
                    context.Emit(Name, refusal == KeeperSpellRefusal.None ? "cast" : "refused",
                        $"player={cast.PlayerId} spell={cast.SpellId} target={cast.CreatureId} reason={refusal}");
                    break;
                case CastSpellAtPlaceCommand place:
                    KeeperSpellRules.CastAtPlace(context, place, out KeeperSpellRefusal placeRefusal);
                    context.Emit(Name, placeRefusal == KeeperSpellRefusal.None ? "cast" : "refused",
                        $"player={place.PlayerId} spell={place.SpellId} at={place.X >> 12},{place.Y >> 12} reason={placeRefusal}");
                    break;
                case GrantCreatureLevelCommand grant:
                    GrantLevel(context, grant);
                    break;
            }
        }
    }

    /// <summary>The computer keeper's readiness grant (ADR-0298): an own living creature takes one
    /// ordinary level-up with its health rescaled, as <c>0x0048B120</c> does; anything else is refused.</summary>
    private void GrantLevel(SimulationContext context, GrantCreatureLevelCommand grant)
    {
        CreatureState? creature = context.World.CreatureById(grant.CreatureId);
        string refusal = creature is null || creature.Activity == CreatureActivity.Dead ? "missing"
            : creature.OwnerPlayerId != grant.PlayerId ? "owner"
            : !CreatureLevelRules.LevelUp(context.Hooks, context.Database.CreatureById(creature.CreatureTypeId), creature) ? "level"
            : "";
        context.Emit(Name, refusal.Length == 0 ? "level_granted" : "level_grant_refused",
            $"player={grant.PlayerId} creature={grant.CreatureId}" +
            (refusal.Length == 0 ? $" level={creature!.Level} health={creature.Health}" : $" reason={refusal}"));
    }
}
