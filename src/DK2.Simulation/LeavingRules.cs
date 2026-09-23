// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation;

/// <summary>The walk out — DK2's state 194 "leaving Dungeon" as this project drives it: on an exit
/// tile the creature is gone with its gold; mid-route it is left to the movement system; otherwise
/// the nearest exit is routed. <see cref="Systems.CreatureAngerSystem"/> carries its own copy for the
/// deserter and should delegate here once that file is free to edit (2026-09-23); a fallen keeper's
/// creatures come through <see cref="DefeatRules"/>.</summary>
public static class LeavingRules
{
    /// <summary>One step of the walk. Returns <see langword="false"/> only when no exit can be
    /// reached — the creature then stands where it is, as the deserter does (Q116).</summary>
    public static bool Step(
        SimulationContext context, IPathfinder pathfinder, List<TilePos> route,
        CreatureDef creature, CreatureState state, Func<TilePos, bool> isExit, string systemName)
    {
        if (isExit(state.Tile))
        {
            Gone(context, creature, state, systemName);
            return true;
        }

        if (state.HasRoute) return true;

        var grid = new WorldPathGrid(context.Database, context.World, creature);
        bool routed = pathfinder.TryFindNearest(grid, state.Tile, isExit, route);
        return routed && route.Count > 0
            && context.Navigation.TryRoute(state, route[^1], Navigation.MoveOptions.Leaving);
    }

    /// <summary>It reached the exit. Gone, with whatever gold it was carrying (ADR-0084).</summary>
    public static void Gone(SimulationContext context, CreatureDef creature, CreatureState state, string systemName)
    {
        context.Emit(systemName, "creature_left",
            $"creature={state.Id} type={creature.Name} player={state.OwnerPlayerId} " +
            $"at=({state.Tile.X},{state.Tile.Y}) gold_lost={state.GoldHeld} via=portal");
        context.World.RemoveCreature(state.Id);
    }
}
