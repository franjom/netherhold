// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A hero knocking down the rooms it walks across: the creature update's arm
/// <c>0x00488F61</c>–<c>0x0048902E</c>, the walk <c>0x004585C0</c> and state 267's handler
/// <c>0x00458620</c>, which runs action 11 (<see cref="TileWorkKind.AttackRoom"/>) on the tile
/// underfoot until it is no room (room-health.md §5, ADR-0342).</summary>
/// <remarks>Job 11's states 179–181, the other starter of action 11, are not built (Q523).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/room-health.md")]
public static class RoomAttackRules
{
    /// <summary>State 267, "destroying rooms" (<c>0x00458620</c>).</summary>
    public const int DestroyingRoomsState = 267;

    /// <summary>
    /// The creature update's arm: a creature that has just entered a new tile, of the Good player,
    /// with <see cref="CreatureState.DestroysRooms"/>, allowed to look for a fight
    /// (<c>0x0045A6D0(0)</c>) and not a pit fighter, standing on a room of a player who is neither
    /// Good nor Neutral whose definition is <c>Buildable</c> and <c>PlaceableOnLand</c>, walks to
    /// the tile's centre and resumes in 267. Returns whether it set off.
    /// </summary>
    public static bool TryBegin(SimulationContext context, string system, CreatureState creature)
    {
        if (creature.OwnerPlayerId != PlayerIds.Good || !creature.DestroysRooms || creature.CombatPitFighter) return false;
        TilePos at = creature.Tile;
        if (at.X == creature.LastPosX >> 12 && at.Y == creature.LastPosY >> 12) return false;

        WorldState world = context.World;
        GameDatabase database = context.Database;
        ref TileState tile = ref world.Tile(at);
        if (tile.OwnerPlayerId is PlayerIds.Good or PlayerIds.Neutral) return false;
        if (database.RoomByTerrain(tile.TerrainId) is not { IsBuildable: true, PlaceableOnLand: true }) return false;
        if (!Combat.CombatRules.MayLookForAFight(world, database, creature, force: false)) return false;

        int x = CreatureState.CentreOf(at.X), y = CreatureState.CentreOf(at.Y);
        if (!TunnelRules.RouteOrTunnel(context, system, creature, x, y, Systems.StateShadow.TravelToPoint,
                DestroyingRoomsState, options: 0, at.X, at.Y))
            return false;

        context.Emit(system, "room_attack_begun", $"creature={creature.Id} at=({at.X},{at.Y})");
        return true;
    }

    /// <summary>
    /// State 267: while the tile underfoot is a room, one action-11 application a turn, landing in
    /// the next turn's tile work. STRONGLY INFERRED: action 11 is one blow an action, and how long a
    /// hero's blow runs is unread; once a turn is action 10's measured cadence. The state ends the
    /// turn after the tile falls.
    /// <see cref="CreatureState.StateDeadlineTick"/> holds the turn of the last application queued.
    /// </summary>
    public static bool DestroyRoomTile(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        TilePos at = creature.Tile;
        long now = context.Tick.Value;
        if (context.Database.RoomByTerrain(world.Tile(at).TerrainId) is null)
            return creature.StateDeadlineTick == now - 1;

        world.QueueWork(new TileWorkOrder((at.Y * world.Width) + at.X, TileWorkKind.AttackRoom, creature.OwnerPlayerId, creature.Id));
        creature.StateDeadlineTick = now;
        return true;
    }
}
