// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Combat;

/// <summary>The landing/get-up target attempt (0x00481400); see door-blocking.md section 8.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/door-blocking.md")]
internal sealed class DropCombatRules
{
    private static readonly TilePos[] Neighbours = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];
    private readonly HeartSites _hearts = new();
    private readonly RoomCache _rooms = new();
    private readonly List<Fixed16Vector> _scratch = [];
    private WorldSight? _sight;

    internal bool TryAcquire(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (NeedRules.ResolvedCategory(creature) == CreatureStateCategory.Combat
            || !CombatRules.MayLookForAFight(context.World, context.Database, creature, force: false)) return false;

        context.Navigation.Refresh();
        _sight ??= new WorldSight(context.Navigation, context.World, context.Database);
        _hearts.Refresh(context.World, context.Database);
        CombatTargetRules.SelectAVictim(context, _sight, _hearts, _rooms, creature, definition, _scratch);
        if (NeedRules.ResolvedCategory(creature) == CreatureStateCategory.Combat) return true;

        TilePos origin = creature.Tile;
        foreach (TilePos offset in Neighbours)
        {
            var at = new TilePos(origin.X + offset.X, origin.Y + offset.Y);
            if (!context.World.Contains(at)) continue;
            DoorState? door = context.World.DoorAt(at.X, at.Y);
            if (door is null || door.UnderConstruction
                || (door.PlayerId != PlayerIds.Neutral && context.World.AreAllied(creature.OwnerPlayerId, door.PlayerId))) continue;

            if (creature.AttackingId != CombatRules.NoTarget)
                CombatPitRules.ClearForcedTarget(context.World, creature);
            creature.AttackAbnormalTargets = true;
            creature.AttackingId = door.Id;
            creature.AttackingKind = FightTargetKind.Door;
            CombatTargetRules.SelectAVictim(context, _sight, _hearts, _rooms, creature, definition, _scratch);
            // DK2 returns handled even when this second selection fails: the room fallback waits.
            return true;
        }
        return false;
    }
}
