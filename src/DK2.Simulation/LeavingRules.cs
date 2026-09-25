// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The walk out — DK2's states 194–196 "leaving Dungeon" (creature-leaving.md): the
/// creature picks the entrance nearest by straight line among every player's Portals and Hero
/// Portals, reachability untested, routes to it, and on arrival is removed with its gold. Ours
/// routes to the nearest entrance tile, and arrival is any entrance tile rather than DK2's
/// point beside the room's centre; a failed route is retried each call, and
/// DK2's no-entrance fallback to wandering (state 87) is not built (Q41), the creature standing
/// instead. Shared by the deserter (<see cref="Systems.CreatureAngerSystem"/>) and a fallen
/// keeper's creatures (<see cref="DefeatRules"/>). On state ids (ADR-0207): 194, a walk in
/// state 6 resuming 195, then 196; ours has no second walk from 195.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-leaving.md")]
public static class LeavingRules
{
    /// <summary>State 194: choose the nearest entrance and route to it.</summary>
    public const int ChoosingState = 194;

    /// <summary>State 195: at the entrance, route to a point beside its centre.</summary>
    public const int AtTheEntranceState = 195;

    /// <summary>State 196: sacked, its gold gone, removed.</summary>
    public const int SackedState = 196;

    /// <summary>Sets 194, from idle when the creature holds no state yet. Returns whether the
    /// rule took it: it refuses 194 from the captive, device and pit states, and the creature
    /// then stays where it was.</summary>
    public static bool Start(SimulationContext context, CreatureState state, string systemName)
    {
        Systems.StateShadow.FromIdle(state);
        return Systems.StateShadow.TryMove(context, systemName, state, ChoosingState);
    }

    /// <summary>Portal or Hero Portal terrain, whoever owns it — DK2's room finder treats types
    /// 3 and 25 alike and walks every player's list.</summary>
    public static bool IsExitTile(WorldState world, GameDatabase database, TilePos at) =>
        world.Contains(at) && PortalRules.IsPortalTerrain(database, world.Tile(at.X, at.Y).TerrainId);

    /// <summary>The entrance tile nearest the creature by squared straight-line distance, ties
    /// broken by tile order; -1 when no entrance exists. DK2 measures to the room and then aims
    /// beside its centre; from outside a room the nearest tile is an edge tile anyway.</summary>
    public static int NearestExitTile(WorldState world, GameDatabase database, CreatureState state)
    {
        int best = -1;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < world.TileCount; i++)
        {
            if (!PortalRules.IsPortalTerrain(database, world.TileAtIndex(i).TerrainId)) continue;
            long dx = CreatureState.CentreOf(i % world.Width) - state.PosX;
            long dy = CreatureState.CentreOf(i / world.Width) - state.PosY;
            long distance = (dx * dx) + (dy * dy);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }
        return best;
    }

    /// <summary>One step of the walk. <paramref name="target"/> is the entrance tile chosen or
    /// stood on. Returns <see langword="false"/> when no entrance exists or the route to the
    /// nearest one failed — the creature then stands where it is and tries again next call.</summary>
    public static bool Step(
        SimulationContext context, CreatureDef creature, CreatureState state, string systemName, out TilePos target)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        target = state.Tile;

        if (IsExitTile(world, database, state.Tile))
        {
            Gone(context, creature, state, systemName);
            return true;
        }

        if (state.HasRoute) return true;

        int exit = NearestExitTile(world, database, state);
        if (exit < 0) return false;
        target = new TilePos(exit % world.Width, exit / world.Width);
        if (!context.Navigation.TryRoute(state, target, Navigation.MoveOptions.Leaving)) return false;
        // A failed route leaves it in 194 to try again, as DK2's option 8 does.
        if (!Systems.StateShadow.IsTravelling(state))
            CreatureStateMachine.BeginTravel(state, Systems.StateShadow.TravelToPoint, AtTheEntranceState);
        return true;
    }

    /// <summary>It reached the entrance. Gone, and its gold with it — DK2 zeroes the carried
    /// amount without dropping or crediting it (creature-leaving.md §2).</summary>
    public static void Gone(SimulationContext context, CreatureDef creature, CreatureState state, string systemName)
    {
        // State 196's first act (0x0045A0E1, 0x0045A0ED): the creature's tree sees event 63 and its
        // owner's tree event 61, before the sacking, the gold and the removal.
        Systems.StateShadow.Arrive(context, systemName, state);
        Systems.StateShadow.Move(context, systemName, state, SackedState);
        context.FireCreatureEvent(state, CreatureTriggerEvent.Sacked);
        context.FirePlayerEvent(state.OwnerPlayerId, PlayerTriggerEvent.CreatureSacked);
        context.Emit(systemName, "creature_left",
            $"creature={state.Id} type={creature.Name} player={state.OwnerPlayerId} " +
            $"at=({state.Tile.X},{state.Tile.Y}) gold_lost={state.GoldHeld} via=portal");
        context.World.RemoveCreature(state.Id);
    }
}
