// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>What our stand-ins for DK2's one current-action field (<c>creature+0x3A9</c>) say: whether an action runs
/// (<c>Creature_IsActionRunning</c>, <c>0x00496D00</c>: the index is neither 0 nor 21), and the index of the ones ours models.
/// Read-only; presentation uses it for the hover sound (docs/research/sound-effects-continuous.md section 2).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-continuous.md#2-the-action-table")]
public static class CreatureCurrentAction
{
    /// <summary>The stand-ins <see cref="CreatureStateMachine"/> clears when it ends an action: a swing, an action hold
    /// (idle, flourish, a cast, the Workshop's work), the tunnel's dig, a trainee's swing, an imp's dig or mine loop,
    /// or an ordinary claim, floor-attack or room-conversion action.</summary>
    public static bool IsRunning(CreatureState creature, GameTick tick) =>
        creature.PossessedExcavation.Kind != PossessedExcavationKind.None
        || creature.ClaimAction.Kind != ClaimActionKind.None
        || creature.IdleAction != IdleActionId.None || creature.SwingInProgress || tick.Value < creature.ActionUntilTick || creature.Tunnel.Digging
        || creature.TrainingActionId != 0 || WorkLoopIndex(creature) != 0;

    /// <summary>The index of the action that runs when ours names it: 30 and 31 for a trainee's, 1 and 2 for an imp's dig and
    /// mine, 4, 5 and 10 for ordinary claim-family actions, 1 or 2 for a tunneller's dig, 17, 16 or 15 for a swing, cast
    /// or free-aimed cast in progress, 17 for a melee swing in its hold, 16 for a cast in its hold; 0 for none or one ours does not tell apart.</summary>
    public static int IndexOf(CreatureState creature, CreatureDef definition, GameTick tick)
    {
        if (creature.PossessedExcavation.Kind != PossessedExcavationKind.None) return (int)creature.PossessedExcavation.Kind;
        if (creature.ClaimAction.Kind != ClaimActionKind.None) return (int)creature.ClaimAction.Kind;
        if (creature.IdleAction != IdleActionId.None) return (int)creature.IdleAction;
        if (creature.TrainingActionId != 0)
            return creature.TrainingActionId == (byte)TrainingActionKind.Spell ? CreatureActionTable.TrainSpell : CreatureActionTable.TrainMelee;
        if (WorkLoopIndex(creature) is var work and not 0) return work;
        if (creature.Tunnel.Digging) return creature.Tunnel.Action;
        if (creature.SwingInProgress)
            return creature.SwingKind switch
            {
                SpellRules.SpellCast => CreatureActionTable.SpellCast,
                SpellRules.FreeAimCast => CreatureActionTable.FreeAimCast,
                _ => CreatureActionTable.MeleeSwing,
            };
        if (FightRules.MeleeActionIsRunning(creature, definition, tick)) return CreatureActionTable.MeleeSwing;
        if (creature.SwingKind == SpellRules.SpellCast && tick.Value < creature.ActionUntilTick) return CreatureActionTable.SpellCast;
        return 0;
    }

    private static int WorkLoopIndex(CreatureState creature) =>
        creature.ActionArtSlot != ImpWorkSlot || creature.TargetTileIndex < 0 ? 0
            : creature.TargetWork switch
            {
                TileWorkKind.MineGold => CreatureActionTable.Mine,
                TileWorkKind.Dig or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall => CreatureActionTable.Dig,
                _ => 0,
            };

    /// <summary>The art slot of the imp's dig and mine actions (<c>ImpTaskSystem</c>'s <c>WorkArtSlot</c>).</summary>
    private const byte ImpWorkSlot = 5;
}
