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
    public const int PortalDropState = 197;

    public static bool IsDropCentre(WorldState world, GameDatabase database, TilePos tile, PlayerId owner)
    {
        if (!world.Contains(tile)) return false;
        RoomInstance? room = RoomIndex.RoomAt(world, database, world.TileIndex(tile));
        return room is not null && room.OwnerPlayerId == owner
            && room.Definition.TypeId is RoomTypeId.Portal or RoomTypeId.HeroPortal
            && room.Width >= 3 && room.Height >= 3
            && PortalRules.EntranceTileIndex(world, room) == world.TileIndex(tile);
    }

    public static bool PortalDrop(SimulationContext context, CreatureState state, string system)
    {
        if (context.Tick.Value - state.StateEnteredTick == 1) state.StopMoving();
        if (!IsDropCentre(context.World, context.Database, state.Tile, state.OwnerPlayerId)) return false;
        context.FireCreatureEvent(state, CreatureTriggerEvent.Sacked);
        context.FirePlayerEvent(state.OwnerPlayerId, PlayerTriggerEvent.CreatureSacked);
        Sack(context, context.Database.CreatureById(state.CreatureTypeId), state, system, "thrown_in");
        return true;
    }

    /// <summary>Sets 194. Returns whether the rule took it: it refuses 194 from the captive,
    /// device and pit states, and the creature then stays where it was.</summary>
    public static bool Start(SimulationContext context, CreatureState state, string systemName) =>
        Systems.StateShadow.TryMove(context, systemName, state, ChoosingState);

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
        if (state.Dk2StateId == PortalDropState) return true;

        if (IsExitTile(world, database, state.Tile))
        {
            Gone(context, creature, state, systemName);
            return true;
        }

        if (state.HasRoute && state.Dk2StateId == Systems.StateShadow.TravelToPoint
            && state.UtilityNewState == AtTheEntranceState) return true;
        if (state.HasRoute) state.StopMoving();

        int exit = NearestExitTile(world, database, state);
        if (exit < 0) return false;
        target = new TilePos(exit % world.Width, exit / world.Width);
        if (!context.Navigation.TryRoute(state, target, Navigation.MoveOptions.Leaving)) return false;
        // A failed route leaves it in 194 to try again, as DK2's option 8 does.
        if (!Systems.StateShadow.IsTravelling(state))
            CreatureStateMachine.BeginTravel(context.Hooks, state, Systems.StateShadow.TravelToPoint, AtTheEntranceState);
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
        Sack(context, creature, state, systemName, "portal");
    }

    private static void Sack(SimulationContext context, CreatureDef creature, CreatureState state, string systemName, string via)
    {
        context.FireCreatureEvent(state, CreatureTriggerEvent.Sacked);
        context.FirePlayerEvent(state.OwnerPlayerId, PlayerTriggerEvent.CreatureSacked);
        ref PlayerState player = ref context.World.Player(state.OwnerPlayerId);
        player.LastSackedCreatureTypeId = state.CreatureTypeId;
        for (int i = 0; i < player.EntranceInfo.Count; i++)
            if (player.EntranceInfo[i].CreatureTypeId == state.CreatureTypeId)
                player.EntranceInfo[i] = player.EntranceInfo[i] with { Block = PortalChoiceRules.Fresh };
        CreatureSoundRules.Request(context, systemName, state, CreatureSoundEvent.Sacked);
        if (state.CreatureTypeId != 45)
            context.World.Messages.Post(context.World, state.OwnerPlayerId, PlayerMessageKind.CreatureLeft, state.CreatureTypeId, 0);
        ushort effect = context.Database.Room(RoomTypeId.Portal).CreatedObjectEffectId;
        if (effect != 0)
            context.RequestEffect(systemName, new ViewEffectRequest(new EffectId(effect), state.OwnerPlayerId,
                new(new Fp32(state.PosX), new Fp32(state.PosY), new Fp32(state.PosZ)), new Dk2Angle((ushort)state.Heading)));
        int gold = PurseRules.Spend(state, state.GoldHeld);
        context.Emit(systemName, "creature_left",
            $"creature={state.Id} type={creature.Name} player={state.OwnerPlayerId} " +
            $"at=({state.Tile.X},{state.Tile.Y}) gold_lost={gold} via={via}");
        context.World.RemoveCreature(state.Id);
    }
}
