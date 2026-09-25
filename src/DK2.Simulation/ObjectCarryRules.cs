// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>DK2's imp task kind 8: carry a loose object to the room it belongs in — a book back to
/// a library, a crate back to a workshop. Kinds 13 and 12 carry creatures instead, the unconscious
/// to a bed or a cell and the dead to a graveyard (ADR-0063, ADR-0064).</summary>
/// <remarks>
/// <para>It sits second in DK2's Tier A table, <c>14, 8, 9, 1, 3, 13, 12</c> (imp-task-priority.md
/// §4), above marked excavation and above claiming: a keeper who marks a wall and knocks a book
/// onto the floor gets the book put away first.</para>
/// <para>DK2 keeps the carry slot separate from the drag slot — its creature dump names
/// <c>CarryingId</c> at <c>0x3c3</c> and <c>DraggingId</c> at <c>0x3c5</c>, an object being carried
/// and a creature dragged. Reproduced as <see cref="CreatureState.CarriedObjectId"/> and
/// <see cref="CreatureState.CarriedCreatureId"/>, so an imp cannot hold a book and a body at once.</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/objects.md")]
public static class ObjectCarryRules
{
    /// <summary>The room an object of this type belongs in, or null when it belongs nowhere.
    /// <b>By the object's own flags</b>, in the order DK2's destination helper <c>0x0046E480</c>
    /// tests them (ADR-0183): <see cref="ObjectFlags.TypeSpellBook"/> and then
    /// <see cref="ObjectFlags.TypeSpecial"/> go to the Library, <see cref="ObjectFlags.TypeCrate"/>
    /// to the Workshop, and anything else is not carried.</summary>
    /// <remarks>This used to walk each room's <c>ObjectIds</c> list (Q120), which is the room's
    /// furniture: book cases, anvils, tombstones. None of those has a carry flag, and none of the
    /// spell book, the fifteen Specials or the eighteen crates is on any list, so the old rule
    /// carried what DK2 leaves alone and never carried what DK2 carries.</remarks>
    public static RoomTypeId? HomeRoomFor(GameDatabase database, byte objectTypeId)
    {
        if (database.ObjectById(objectTypeId) is not { } definition) return null;
        if (definition.Flags.HasFlag(ObjectFlags.TypeSpellBook)) return RoomTypeId.Library;
        if (definition.Flags.HasFlag(ObjectFlags.TypeSpecial)) return RoomTypeId.Library;
        if (definition.Flags.HasFlag(ObjectFlags.TypeCrate)) return RoomTypeId.Workshop;
        return null;
    }

    /// <summary>Whether this object is lying loose and belongs somewhere — what kind 8 looks for.
    /// DK2's test is objects owned by nobody, standing on the imp's own floor or on water. "Owned
    /// by nobody" separates a loose book from one already shelved, and it is why a bed and a
    /// treasury's gold are never candidates: a bed belongs to the creature that put it down, and a
    /// floor pile is owned by the level's floor-gold id, which is kind 7's business (Q144).</summary>
    public static bool IsLoose(GameDatabase database, ObjectState thing) =>
        thing.OwnerPlayerId == PlayerIds.Unowned
        && thing.CarriedByCreatureId < 0
        && thing.CreatureId == 0
        && thing.GoldHeld == 0
        && HomeRoomFor(database, thing.ObjectTypeId) is not null;

    /// <summary>Whether <paramref name="worker"/> may pick <paramref name="thing"/> up. The same
    /// shape and the same one-slot rule as <see cref="CarryRules.CanCarry"/>: a worker already
    /// carrying an object is not offered another, and one carrying gold or a body is doing
    /// something else.</summary>
    public static bool CanCarry(
        GameDatabase database, CreatureDef definition, CreatureState worker, ObjectState thing)
    {
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker)) return false;
        if (worker.CarriedObjectId >= 0) return false;
        if (worker.CarriedCreatureId >= 0) return false;
        if (worker.GoldHeld > 0) return false;
        if (HandRules.IsHeld(worker) || CreatureDeathRules.IsDown(worker)) return false;
        if (PrisonRules.IsImprisoned(worker)) return false;

        return IsLoose(database, thing);
    }

    /// <summary>A tile with nothing standing on it in <paramref name="playerId"/>'s nearest room of
    /// the right kind, or -1 when there is nowhere to put it. Chosen before the walk starts, like a
    /// bed, a cell and a graveyard tile, so an imp with nowhere to put a book does not cross the
    /// dungeon first. One object per tile is ours, the same occupancy rule every room here uses
    /// (Q166): DK2 stores <c>UsedObjectCapacity</c> on the room and each object definition carries
    /// its own <c>RoomCapacity</c>, so the original's accounting is finer and has not been read.</summary>
    public static int FreeTileFor(
        WorldState world, GameDatabase database, byte playerId, RoomTypeId type, TilePos from)
    {
        int best = -1;
        long bestDistance = long.MaxValue;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != playerId) continue;
            if (room.Definition.TypeId != type) continue;

            foreach (int tileIndex in room.TileIndices)
            {
                if (ObjectOn(world, tileIndex) is not null) continue;

                long dx = (tileIndex % world.Width) - from.X;
                long dy = (tileIndex / world.Width) - from.Y;
                long distance = (dx * dx) + (dy * dy);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = tileIndex;
            }
        }

        return best;
    }

    /// <summary>The object standing on one tile, or null. Ties go to the lower id.</summary>
    public static ObjectState? ObjectOn(WorldState world, int tileIndex)
    {
        ObjectState? best = null;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.TileIndex != tileIndex) continue;
            if (thing.CarriedByCreatureId >= 0) continue;
            if (best is null || thing.Id < best.Id) best = thing;
        }
        return best;
    }

    /// <summary>Where <paramref name="thing"/> has to go, as a tile index, or -1. A crate goes to
    /// the first crate tile of <see cref="WorkshopFor"/>; the carry itself picks a random one
    /// (ADR-0214).</summary>
    public static int TryDestinationFor(
        WorldState world, GameDatabase database, CreatureState worker, ObjectState thing)
    {
        if (HomeRoomFor(database, thing.ObjectTypeId) is not { } type) return -1;
        if (type != RoomTypeId.Workshop) return FreeTileFor(world, database, worker.OwnerPlayerId, type, worker.Tile);
        if (WorkshopFor(world, database, worker) is not { } workshop) return -1;

        var tiles = new List<int>();
        CrateRules.CrateTiles(world, database, workshop, tiles);
        return tiles.Count == 0 ? -1 : tiles[0];
    }

    /// <summary><c>0x004C6610</c> for a crate: the worker's keeper's Workshop nearest to it that has
    /// space (<see cref="RoomCapacityRules.HasRoom"/> for a crate's places, none), or null. Ours
    /// measures to the room's first tile rather than its position word, and leaves the
    /// reachability test to the walk.</summary>
    public static RoomInstance? WorkshopFor(WorldState world, GameDatabase database, CreatureState worker)
    {
        RoomInstance? best = null;
        long bestDistance = long.MaxValue;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != worker.OwnerPlayerId || room.Definition.TypeId != RoomTypeId.Workshop) continue;
            if (room.TileIndices.Count == 0 || !RoomCapacityRules.HasRoom(world, database, room, units: 0)) continue;
            long distance = EatingRules.DistanceSquared(world, worker.Tile, room.TileIndices[0]);
            if (distance >= bestDistance) continue;
            best = room;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>Puts <paramref name="thing"/> into <paramref name="worker"/>'s arms.</summary>
    public static void PickUp(CreatureState worker, ObjectState thing)
    {
        worker.CarriedObjectId = thing.Id;
        thing.CarriedByCreatureId = worker.Id;
    }

    /// <summary>Takes it back out, wherever it happens to be standing.</summary>
    public static void PutDown(CreatureState worker, ObjectState thing)
    {
        worker.CarriedObjectId = -1;
        thing.CarriedByCreatureId = -1;
    }

    /// <summary>Moves a carried object to wherever its carrier is standing. An object has a tile and
    /// not a position, so this is coarser than <see cref="CarryRules.FollowCarrier"/> — which is
    /// right: nothing asks where within a tile a book is.</summary>
    public static void FollowCarrier(WorldState world, CreatureState worker, ObjectState thing) =>
        thing.TileIndex = (worker.Tile.Y * world.Width) + worker.Tile.X;

    /// <summary>Makes <paramref name="worker"/> let go of whatever it was carrying, wherever it is
    /// standing, and says whether it was holding anything. Called wherever a worker stops being a
    /// worker mid-job — picked up by the keeper, killed, put in a cell. The object stays loose: it
    /// is on the floor where the imp was, and the next imp along will take it to the same
    /// room.</summary>
    public static bool DropWhatever(WorldState world, CreatureState worker)
    {
        worker.TargetObjectId = -1;
        if (worker.CarriedObjectId < 0) return false;

        ObjectState? thing = world.ObjectById(worker.CarriedObjectId);
        if (thing is null)
        {
            worker.CarriedObjectId = -1;
            return false;
        }

        FollowCarrier(world, worker, thing);
        PutDown(worker, thing);
        return true;
    }

    /// <summary>Puts an object down in a room and makes it that room's: the object's owner becomes
    /// the keeper, which is what takes it out of <see cref="IsLoose"/> and stops it being fetched
    /// again for ever.</summary>
    public static void Deliver(
        CreatureState worker, ObjectState thing, byte playerId, int tileIndex)
    {
        PutDown(worker, thing);
        thing.TileIndex = tileIndex;
        thing.OwnerPlayerId = playerId;
    }
}
