// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A Prison's door and a jail break (<c>jail-break.md</c>, ADR-0229). The door is the tile
/// north of the first interior tile whose three northern neighbours are not interior
/// (<c>0x004E32C0</c>). A rescuer stepping into an enemy Prison that holds an ally goes to 122: it
/// walks to the door, faces the cell, unlocks and opens the door, and releases its side's
/// prisoners (<c>0x004E2E70</c>). A released prisoner walks out in 121 and is free once it stands
/// off the cell's floor.</summary>
/// <remarks>Not built: the door's objects (109 and 116) and their animations, the keeper's lock
/// toggle (<c>0x00514280</c>), the imps' door arms and the auto-close count they set, messages
/// <c>0x3E</c>, <c>0x3F</c> and <c>0x42</c>, and the broken prison's redraw.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/jail-break.md")]
public static class JailBreakRules
{
    /// <summary>121 (<c>0x004739E0</c>): a released prisoner walking out through the door.</summary>
    public const int EscapingState = 121;

    /// <summary>122 (<c>0x00473C60</c>): a rescuer breaking a Prison open.</summary>
    public const int BreakingOutState = 122;

    /// <summary>The room's <c>Activation</c> (<c>+0x59</c>), <c>DoorOpen</c> (<c>+0x5D</c>) and
    /// <c>PrisonJailBroken</c> (<c>+0x4D</c>), as bits of <see cref="TileState.PrisonDoor"/>.</summary>
    public const byte Activated = 1, Open = 2, Broken = 4;

    /// <summary><c>0x004E32C0</c>: the door tile, or -1 when the room has none: north of the first
    /// floor tile, in the room's slab list, whose N, NW and NE are not floor. DK2's list order depends
    /// on the camera when the room was placed (Q439). The owner saw the default camera put the door
    /// right of centre every time, so ours walks each row from the right, top row first.</summary>
    public static int DoorTile(WorldState world, RoomInstance room)
    {
        int w = world.Width;
        foreach (int tile in room.InteriorTileIndices.OrderBy(t => t / w).ThenByDescending(t => t % w))
        {
            int x = tile % w, y = tile / w;
            if (y == 0) continue;
            int north = tile - w;
            if (room.IsInterior(north)) continue;
            if (x > 0 && room.IsInterior(north - 1)) continue;
            if (x < w - 1 && room.IsInterior(north + 1)) continue;
            return north;
        }

        return -1;
    }

    public static ref byte DoorOf(WorldState world, RoomInstance room) => ref world.TileAtIndex(room.TileIndices[0]).PrisonDoor;

    /// <summary><c>0x004E8660</c>'s Prison arm, for a creature already past the flag and worker tests:
    /// an occupied Prison of a keeper it is not allied with (or the neutral player's), itself not a
    /// captive, not already breaking out and not fighting, with an ally held there.</summary>
    public static bool StartsABreakOut(WorldState world, GameDatabase database, CreatureState rescuer, RoomInstance room)
    {
        if (room.Definition.TypeId != RoomTypeId.Prison) return false;
        if (world.AreAllied(rescuer.OwnerPlayerId, room.OwnerPlayerId) && room.OwnerPlayerId != PlayerIds.Neutral) return false;
        if (PrisonRules.IsImprisoned(rescuer)) return false;
        if (rescuer.Dk2StateId == BreakingOutState || rescuer.UtilityNewState == BreakingOutState) return false;
        if (Combat.CombatRules.Preoccupied(rescuer)) return false;
        return AlliedPrisoners(world, rescuer, room).Any();
    }

    /// <summary>The Prison's captives the rescuer would free: not down, of an allied side that is
    /// not the neutral player's.</summary>
    public static IEnumerable<CreatureState> AlliedPrisoners(WorldState world, CreatureState rescuer, RoomInstance room)
    {
        foreach (CreatureState c in world.Creatures)
        {
            if (c.Id == rescuer.Id || !PrisonRules.IsImprisoned(c) || c.PrisonOwnerPlayerId != room.OwnerPlayerId) continue;
            if (HandRules.IsHeld(c) || CreatureDeathRules.IsDown(c) || c.OwnerPlayerId == PlayerIds.Neutral) continue;
            if (!room.Contains((c.Tile.Y * world.Width) + c.Tile.X)) continue;
            if (world.AreAllied(rescuer.OwnerPlayerId, c.OwnerPlayerId)) yield return c;
        }
    }

    /// <summary>122 (<c>0x00473C60</c>). On a Prison with a door: walk to the door, resuming 122;
    /// there, turn to the room's centre; then unlock it (<c>0x004E3680</c>) and open it
    /// (<c>0x004E35C0</c>), which takes the turn; with it open, release the rescuer's side
    /// (<c>0x004E2E70(1, owner, 0)</c>) and wander (1, then 87). False is idle.</summary>
    public static bool BreakOut(SimulationContext context, string system, CreatureState rescuer)
    {
        WorldState world = context.World;
        int here = (rescuer.Tile.Y * world.Width) + rescuer.Tile.X;
        if (RoomIndex.RoomAt(world, context.Database, here) is not { } room || room.Definition.TypeId != RoomTypeId.Prison) return false;
        int door = DoorTile(world, room);
        if (door < 0) return false;

        if (door != here)
        {
            if (!context.Navigation.TryRouteTo(rescuer, CreatureState.CentreOf(door % world.Width), CreatureState.CentreOf(door / world.Width), Navigation.MoveOptions.Loose))
                return false;
            CreatureStateMachine.BeginTravel(rescuer, StateShadow.TravelToPoint, BreakingOutState);
            context.Emit(system, "rescuer_to_door", $"creature={rescuer.Id} door=({door % world.Width},{door / world.Width})");
            return true;
        }

        (int cx, int cy) = CombatPitSpectatorRules.Middle(room);
        int heading = rescuer.Heading;
        int left = Steering.Bearing.TurnToward(ref heading,
            Steering.Bearing.Of(CreatureState.CentreOf(cx) - rescuer.PosX, CreatureState.CentreOf(cy) - rescuer.PosY),
            context.Database.CreatureById(rescuer.CreatureTypeId).TurnRate);
        rescuer.Heading = heading;
        if (left != 0) return true;

        ref byte flags = ref DoorOf(world, room);
        if ((flags & Activated) == 0) flags = Unlocked(flags);
        if ((flags & Open) == 0)
        {
            flags |= Open;
            context.Emit(system, "prison_door_opened", $"creature={rescuer.Id} player={room.OwnerPlayerId} door=({door % world.Width},{door / world.Width})");
            return true;
        }

        Release(context, system, room, rescuer.OwnerPlayerId, all: false);
        StateShadow.Move(context, system, rescuer, CreatureStateMachine.Idle);
        StateShadow.Move(context, system, rescuer, ImpStates.Wandering);
        return true;
    }

    /// <summary><c>0x004E2E70(message, player, all)</c>: every prisoner on the cell's floor, not down,
    /// of <paramref name="player"/> (or any, with <paramref name="all"/>), sets out in 121: to a
    /// random point of the tile south of the door, or with no door to a random rim tile
    /// (<c>0x004E8430</c>). The room is marked broken (<c>0x004E2D40(1)</c>). Ours releases only
    /// prisoners, where DK2 takes every creature of the side standing there.</summary>
    public static void Release(SimulationContext context, string system, RoomInstance room, byte player, bool all)
    {
        WorldState world = context.World;
        int door = DoorTile(world, room);
        int w = world.Width;
        foreach (CreatureState c in world.Creatures)
        {
            if (!PrisonRules.IsImprisoned(c) || HandRules.IsHeld(c) || CreatureDeathRules.IsDown(c)) continue;
            if (TortureRules.IsBeingTortured(c)) continue;
            int at = (c.Tile.Y * w) + c.Tile.X;
            if (!room.IsInterior(at) || (!all && c.OwnerPlayerId != player)) continue;

            int x, y;
            if (door < 0)
            {
                if (RimTile(context, c, room) is not int rim) continue;
                x = CreatureState.CentreOf(rim % w);
                y = CreatureState.CentreOf(rim / w);
            }
            else
            {
                x = ((door % w) << 12) | context.Rng.NextInt(0x1000);
                y = (((door / w) + 1) << 12) | context.Rng.NextInt(0x1000);
            }
            // 0x004E2D40(1), once per creature released; with no door only when a rim was found.
            DoorOf(world, room) |= Broken;
            if (!Walk(context, c, x, y)) continue;
            context.Emit(system, "prisoner_released", $"creature={c.Id} owner={c.OwnerPlayerId} held_by={c.PrisonOwnerPlayerId}");
        }
    }

    /// <summary>The lock toggle <c>0x004E3680</c> as it unlocks: <see cref="Activated"/> on, and
    /// <see cref="Broken"/> cleared, since the toggle starts with <c>0x004E2D40(0)</c>.</summary>
    private static byte Unlocked(byte flags) => (byte)((flags | Activated) & ~Broken);

    /// <summary>121 (<c>0x004739E0</c>), once a turn. Off the cell's floor, the prisoner is free
    /// (the handler's 0, idle). South of the door it steps onto the door, unlocking and opening it;
    /// elsewhere in the cell it walks to a random point of the tile south of the door; with no door,
    /// to a random rim tile.</summary>
    public static void Escape(SimulationContext context, string system, CreatureState prisoner)
    {
        WorldState world = context.World;
        int w = world.Width;
        int here = (prisoner.Tile.Y * w) + prisoner.Tile.X;
        RoomInstance? room = RoomIndex.RoomAt(world, context.Database, here);
        if (room is null || room.Definition.TypeId != RoomTypeId.Prison || !room.IsInterior(here))
        {
            context.Emit(system, "prisoner_freed",
                $"creature={prisoner.Id} owner={prisoner.OwnerPlayerId} was_held_by={prisoner.PrisonOwnerPlayerId} " +
                $"at=({prisoner.Tile.X},{prisoner.Tile.Y}) reason=walked_out");
            PrisonRules.Release(prisoner);
            return;
        }

        int door = DoorTile(world, room);
        if (door < 0)
        {
            if (RimTile(context, prisoner, room) is int rim) Walk(context, prisoner, CreatureState.CentreOf(rim % w), CreatureState.CentreOf(rim / w));
            return;
        }

        int dx = door % w, dy = door / w;
        if (prisoner.Tile.X == dx && prisoner.Tile.Y == dy + 1)
        {
            if (!Walk(context, prisoner, CreatureState.CentreOf(dx), CreatureState.CentreOf(dy))) return;
            ref byte flags = ref DoorOf(world, room);
            if ((flags & Activated) == 0) flags = Unlocked(flags);
            flags |= Open;
            return;
        }

        Walk(context, prisoner, (dx << 12) | context.Rng.NextInt(0x1000), ((dy + 1) << 12) | context.Rng.NextInt(0x1000));
    }

    /// <summary><c>0x00471890(point, 121, 0x6C)</c>.</summary>
    private static bool Walk(SimulationContext context, CreatureState creature, int x, int y)
    {
        if (!context.Navigation.TryRouteTo(creature, x, y, Navigation.MoveOptions.Loose)) return false;
        CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToPoint, EscapingState);
        return true;
    }

    /// <summary><c>0x004E8430</c>: a random tile of the room off its floor that the creature can
    /// stand on, round from a random start.</summary>
    private static int? RimTile(SimulationContext context, CreatureState creature, RoomInstance room)
    {
        IReadOnlyList<int> tiles = room.TileIndices;
        int start = context.Rng.NextInt(tiles.Count);
        var grid = new WorldPathGrid(context.Database, context.World, context.Database.CreatureById(creature.CreatureTypeId));
        int w = context.World.Width;
        for (int k = 0; k < tiles.Count; k++)
        {
            int tile = tiles[(start + k) % tiles.Count];
            if (!room.IsInterior(tile) && grid.IsWalkable(tile % w, tile / w)) return tile;
        }

        return null;
    }

    /// <summary>Whether a prisoner is walking out, so the cell's own states leave it alone.</summary>
    public static bool IsEscaping(CreatureState c) =>
        c.Dk2StateId == EscapingState || (StateShadow.IsTravelling(c) && c.UtilityNewState == EscapingState);
}
