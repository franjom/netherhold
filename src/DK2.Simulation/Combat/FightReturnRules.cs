// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Combat;

/// <summary>The walk back to a fight withdrawn from (ADR-0225). The withdraw state stamps
/// <c>OldFightPos</c>/<c>OldFightTick</c> (<c>+0x52A</c>, <c>+0x52E</c>; <c>0x00461801</c>) for any
/// creature but an Imp; state 1 (<c>0x004773D0</c>) walks there once health is back above three
/// quarters, within 500 turns of the stamp, before any of its other arms.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-fear.md")]
public static class FightReturnRules
{
    /// <summary>How long a stamp stays good (<c>+0x52E + 500</c>), in turns.</summary>
    public const int ReturnWindowTicks = 500;

    /// <summary>The stamp, at the end of a successful withdrawal. DK2's own quirk is kept: both words
    /// are the x coordinate (<c>+0x1A</c> twice, <c>0x004617FB</c>–<c>0x00461811</c>).</summary>
    public static void Stamp(SimulationContext context, CreatureState creature)
    {
        if (creature.CreatureTypeId == CombatRules.ImpCreatureTypeId) return;
        creature.OldFightTileX = creature.PosX >> 12;
        creature.OldFightTileY = creature.PosX >> 12;
        creature.OldFightTick = context.Tick.Value;
    }

    /// <summary>State 1's first arm. A stale stamp is cleared; a fresh one with health above
    /// <c>(MaxHealth × 3) &gt;&gt; 2</c> walks to the tile's centre, resuming 1, options <c>0x80</c>
    /// for an Imp and 0 otherwise, and is cleared once the walk is taken. True when it walks.</summary>
    public static bool TryReturn(SimulationContext context, CreatureState creature)
    {
        if (creature.OldFightTileX == 0) return false;
        if (context.Tick.Value >= creature.OldFightTick + ReturnWindowTicks)
        {
            Clear(creature);
            return false;
        }
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        int max = CreatureHealthRules.MaxHealth(context.Database, definition, creature);
        if (((max * 3) >> 2) >= creature.Health) return false;

        int x = CreatureState.CentreOf(creature.OldFightTileX), y = CreatureState.CentreOf(creature.OldFightTileY);
        int options = creature.CreatureTypeId == CombatRules.ImpCreatureTypeId ? Navigation.MoveOptions.Loose : Navigation.MoveOptions.Plain;
        if (!context.World.Contains(new TilePos(x >> 12, y >> 12))) return false;
        if (!context.Navigation.TryRouteTo(creature, x, y, options)) return false;
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, CreatureStateMachine.Idle);
        Clear(creature);
        context.Emit("FightReturn", "returns_to_fight",
            $"creature={creature.Id} to=({x >> 12},{y >> 12}) health={creature.Health}/{max}");
        return true;
    }

    private static void Clear(CreatureState creature)
    {
        creature.OldFightTileX = 0;
        creature.OldFightTileY = 0;
        creature.OldFightTick = 0;
    }
}
