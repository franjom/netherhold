// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-possession.md")]
public static class PossessionRules
{
    public const byte SpellId = 2;
    public const byte ProcessType = 26;

    public static void ApplySpeedModifier(GameDatabase database, CreatureState creature)
    {
        if (!creature.Possessed && !creature.InfluencedByPossessed) return;
        const uint limit = Systems.CreatureMovementSystem.MaxSpeedPerTick;
        // The original's preceding setters already clamp before the entity-speed getter.
        uint speed = Math.Min(unchecked((uint)(creature.Body.MaxSpeed >> 4)), limit);
        uint product = unchecked(speed * (uint)(100 + database.FirstPersonStat(CreatureStatId.Speed)));
        uint scaled = product / (creature.Possessed ? 100U : 200U);
        creature.Body.MaxSpeed = (int)Math.Min(scaled, limit) << 4;
    }

    public static void Strike(SimulationContext context, CreatureState target)
    {
        ref PlayerState player = ref context.World.Player(target.OwnerPlayerId);
        if (player.PossessedCreatureId != 0 || JackInTheBoxRules.IsJack(target)
            || target.Possessed) return;
        target.Possessed = true;
        target.NextPossessionInfluenceTick = new GameTick(0);
        player.PossessedCreatureId = target.Id;
    }

    public static void Leave(SimulationContext context, ref PlayerState player)
    {
        CreatureState? target = context.World.CreatureById(player.PossessedCreatureId);
        if (target is not null && CreatureStateMachine.HasState(target))
        {
            target.Possessed = false;
            if (!HandRules.IsHeld(target))
                CreatureStateMachine.TrySetState(context.Hooks, target, CreatureStateMachine.Idle, context.Tick.Value);
            foreach (CreatureState other in context.World.Creatures)
                if (other.OwnerPlayerId == target.OwnerPlayerId) other.InfluencedByPossessed = false;
            target.NextPossessionInfluenceTick = new GameTick(0);
            if (target.FrozenTicks != 0)
            {
                target.SwingInProgress = false;
                target.ActionUntilTick = 0;
                target.Tunnel.ActionUntilTick = 0;
                if (!HandRules.IsHeld(target))
                    CreatureStateMachine.TrySetState(context.Hooks, target, CombatStates.Frozen, context.Tick.Value);
            }
            CarryRules.DropWhatever(context.World, target);
            ObjectCarryRules.DropWhatever(context.World, target);
        }
        player.PossessedCreatureId = 0;
    }

    public static void RefreshInfluence(SimulationContext context, CreatureState possessed, ISightBlocker sight)
    {
        if (!possessed.Possessed || unchecked((uint)context.Tick.Value)
            <= unchecked((uint)possessed.NextPossessionInfluenceTick.Value)) return;
        foreach (CreatureState other in context.World.Creatures)
        {
            if (other == possessed || other.OwnerPlayerId != possessed.OwnerPlayerId) continue;
            other.InfluencedByPossessed = LineOfSight.Clear(sight, possessed.PosX, possessed.PosY, other.PosX, other.PosY);
        }
        // The creature update adds twenty turns after a strict unsigned deadline test.
        possessed.NextPossessionInfluenceTick = new GameTick(unchecked((uint)context.Tick.Value + 20U));
    }
}
