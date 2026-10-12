// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-turncoat.md")]
public static class TurncoatRules
{
    public const byte SpellId = 7;
    public const byte ProcessType = 13;

    public static bool IsActive(CreatureState creature) => creature.TurncoatTicks != 0;

    public static bool AreCombatEnemies(WorldState world, CreatureState attacker, CreatureState target) =>
        !world.AreAllied(CombatPlayer(world, attacker).PlayerId, CombatPlayer(world, target).PlayerId.Value);

    private static ref PlayerState CombatPlayer(WorldState world, CreatureState creature) =>
        ref world.Player(CombatSide(creature));

    /// <summary>The side a creature fights for: <c>+0x455</c> while Turncoat runs, else its owner.</summary>
    public static PlayerId CombatSide(CreatureState creature) =>
        IsActive(creature) ? unchecked((byte)creature.TurncoatPlayerId) : creature.OwnerPlayerId;

    public static void Strike(SimulationContext context, ShotState shot, CreatureState target, CreatureDef definition)
    {
        if (!CreatureStateMachine.HasState(target) || definition.Flags.HasFlag(CreatureFlags.IsImmuneToTurncoat)) return;
        target.TurncoatTicks = unchecked((ushort)(shot.Data1 * SimulationTiming.OriginalTicksPerGameSecond));
        target.TurncoatPlayerId = shot.OwnerPlayerId;
        if (NeedRules.ResolvedCategory(target) == CreatureStateCategory.Combat
            && CreatureStateMachine.TrySetState(context.Hooks, target, CreatureStateMachine.Idle, context.Tick.Value))
            target.StopMoving();
        context.Emit("turncoat", "applied", $"creature={target.Id} player={shot.OwnerPlayerId} ticks={target.TurncoatTicks}");
    }
}
